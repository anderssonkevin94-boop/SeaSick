"""Footprint-preserving upper modules for an offline third-layer study."""
import sys,json,ast
from pathlib import Path
import bpy,bmesh
from mathutils import Matrix,Vector
HERE=Path(__file__).resolve().parent;sys.path.insert(0,str(HERE))
import modular_raised_stern_v2 as rev
family,ship,ss,fore=rev.family,rev.ship,rev.ss,rev.fore
OUT=HERE.parents[1]/'art-staging/modular-third-layer-v1';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT.parent/'modular-raised-middle-v1/continuous-upper-deck.blend'))
STEP=2.44;BASE=4.20;names=['Stern_W1','Midship_W1','Bow_W1'];tops={};shells=[]
manifest={'interface':'W1-center-expansion-r1','layer_rise':STEP,'mount_height':BASE,'third_deck_height':BASE+STEP,'modules':{},'status':'Offline shape prototype; crew clearance and stability NOT certified'}
for name in names:
    support=bpy.data.objects[name];root=family.empty('Upper_'+name,(support.location.x,0,BASE));tops[name]=root
    source=bpy.data.objects[name+'__Hull_Shell'];me=source.data.copy();bm=bmesh.new();bm.from_mesh(me)
    keep=[f for f in bm.faces if all(abs(v.co.z-BASE)<1e-4 for v in f.verts)]
    assert keep,name
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if f not in keep],context='FACES')
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    boundary=[e for e in bm.edges if e.is_boundary]
    for v in bm.verts:v.co.z=STEP
    outline=[(e.verts[0].co.copy(),e.verts[1].co.copy()) for e in boundary]
    result=bmesh.ops.extrude_edge_only(bm,edges=boundary)
    for v in result['geom']:
        if isinstance(v,bmesh.types.BMVert):v.co.z=0
    layer=bm.loops.layers.color.get('Col');linear=bm.loops.layers.float_color.get('Col')
    for f in result['geom']:
        if isinstance(f,bmesh.types.BMFace):
            for loop in f.loops:
                if linear:loop[linear]=(*ss.srgb_to_linear(ss.PAL['wood']),1)
                else:loop[layer]=(*ss.PAL['wood'],1)
    # Separate internal joining walls; they are optional end closures, not
    # buried geometry in the continuous three-module assembly.
    endplanes=[0,9.3] if name=='Stern_W1' else [0,6] if name=='Midship_W1' else [0]
    caps=[f for f in bm.faces if max(v.co.z for v in f.verts)-min(v.co.z for v in f.verts)>1 and any(all(abs(v.co.x-x)<1e-4 for v in f.verts) for x in endplanes)]
    if name=='Stern_W1':caps=[f for f in caps if all(abs(v.co.x-9.3)<1e-4 for v in f.verts)]
    capmesh=bpy.data.meshes.new('EndClosures');cb=bmesh.new()
    cmap={}
    for f in caps:
        vs=[]
        for v in f.verts:
            key=tuple(v.co)
            if key not in cmap:cmap[key]=cb.verts.new(v.co)
            vs.append(cmap[key])
        cb.faces.new(vs)
    cb.to_mesh(capmesh);cb.free()
    capob=bpy.data.objects.new(root.name+'__OptionalEndClosures',capmesh);bpy.context.scene.collection.objects.link(capob);capob.parent=root;capob.hide_render=True
    for mat in me.materials:capmesh.materials.append(mat)
    attr=capmesh.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for c in attr.data:c.color=(*ss.srgb_to_linear(ss.PAL['wood']),1)
    bmesh.ops.delete(bm,geom=caps,context='FACES')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free();me.update()
    ob=bpy.data.objects.new(root.name+'__Shell',me);bpy.context.scene.collection.objects.link(ob);ob.parent=root;shells.append(ob)
    me.color_attributes.active_color=me.color_attributes['Col']
    for p in me.polygons:p.use_smooth=False
    detail=ss.Builder();brace=ss.Builder()
    for a,c in outline:
        if max(abs(a.y),abs(c.y))<2.5:continue
        if any(abs(a.x-x)<1e-4 and abs(c.x-x)<1e-4 for x in endplanes):continue
        for h in [.6,1.2,1.8]:
            fore.tube(detail,[(a.x,a.y+( .012 if a.y>0 else -.012),h),(c.x,c.y+(.012 if c.y>0 else -.012),h)],.012,.016,'seam')
    iron=bpy.data.objects.get(name+'__Hull_Ironwork')
    if iron:
        b=bmesh.new();b.from_mesh(iron.data);b.normal_update()
        for face in b.faces:
            if face.normal.z<.9 or not 4.10<face.calc_center_median().z<4.22:continue
            bottom=[fore.point(v.co.x,v.co.y,0) for v in face.verts]
            top=[fore.point(v.co.x,v.co.y,STEP-.04) for v in face.verts]
            brace.solid([bottom,top]+[[bottom[i],bottom[(i+1)%len(bottom)],top[(i+1)%len(top)],top[i]] for i in range(len(bottom))],'iron')
            center=face.calc_center_median();s=1 if center.y>0 else -1
            for z in [.65,1.85]:brace.cyl(fore.point(center.x,center.y+s*.045,z),fore.point(center.x,center.y+s*.12,z),.08,.08,6,'bolt')
        b.free()
    for label,builder in [('WallPlanks',detail),('BraceExtensions',brace)]:
        extra=ship.mesh(root.name+'__'+label,builder);extra.parent=root
    for src in list(support.children):
        if src.type!='MESH':continue
        component=src.name.split('__')[-1].split('.')[0]
        if component not in ['UpperRails','UpperPosts','UpperPlanks','PlankDetails','Hatch','PortalFrames','InternalLadder','Helm','Prow','StemExtension']:continue
        cp=src.copy();cp.data=src.data.copy();bpy.context.scene.collection.objects.link(cp);cp.parent=root;cp.name=root.name+'__'+component;cp.location.z+=STEP-BASE
        if component in ['UpperPlanks','PlankDetails']:
            b=bmesh.new();b.from_mesh(cp.data)
            bmesh.ops.delete(b,geom=[v for v in b.verts if v.co.z<BASE-.001],context='VERTS');b.to_mesh(cp.data);b.free()
        if component not in ['InternalLadder','PortalFrames']:src.hide_render=True
    family.empty(root.name+'__Mount',(0,0,0),root)
    family.empty(root.name+'__Top',(0,0,STEP),root)
    manifest['modules'][name]={'mount_position':list(root.location),'parts':{}}
    for part in root.children:
        if part.type!='MESH':continue
        check=ship.check(part);assert not check['overconnected_edges'] and not check['degenerate_faces'],(part.name,check)
        path=OUT/name/(part.name+'.fbx');ss.export_fbx(part,str(path))
        manifest['modules'][name]['parts'][part.name]={'file':str(path.relative_to(OUT)),'position':list(part.location),'rotation_radians':list(part.rotation_euler),'triangles':check['triangles'],'optional':part==capob}

chimney=bpy.data.objects['Chimney'];chimney.location.z=BASE+STEP
manifest['chimney_position']=list(chimney.location)
bpy.context.view_layer.update()
# Open bottom edges are intentional: the supporting deck remains the floor.
tree=ast.parse((HERE/'modular_width_inserts_v1.py').read_text());fn=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='combined')
exec(compile(ast.Module(body=[fn],type_ignores=[]),str(HERE/'modular_width_inserts_v1.py'),'exec'))
test,stats=combined('UpperShellCheck',shells,True)
b=bmesh.new();b.from_mesh(test.data)
unexpected=[e for e in b.edges if e.is_boundary and not all(abs(v.co.z-BASE)<1e-4 for v in e.verts)]
assert not unexpected,[(tuple(e.verts[0].co),tuple(e.verts[1].co)) for e in unexpected]
b.free();bpy.data.meshes.remove(test.data);manifest['upper_shell_check']=stats
family.render(OUT/'assembled.png',(-18,-40,31),(13,0,3.5),38,(1600,1100))
family.render(OUT/'side.png',(13,-50,4),(13,0,4),35,(1600,900))
family.save(OUT/'third-layer-study.blend')
for o in bpy.context.scene.objects:
    if o.type=='MESH':o.hide_render=not (o.parent==tops['Midship_W1'] and 'Optional' not in o.name)
family.render(OUT/'middle-upper-only.png',(0,-20,17),(12.3,0,5.5),19,(1400,1000))
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
print('THIRD LAYER PASS',stats)
