"""Offline split-shell width prototype from the approved W1-r2/V3 meshes."""
import sys, json, math
from pathlib import Path
import bpy, bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family
import export_hull_hydrostatics as hydro
ss=family.ss
OUT=HERE.parents[1]/'art-staging/modular-width-inserts-v1'
OUT.mkdir(parents=True,exist_ok=True)
SOURCE=OUT.parent/'modular-hull-family-v3/long/ship.blend'
SHIFT=1.4
CORE=3.12
EPS=2e-5
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
roots={n:bpy.data.objects[n] for n in ['Stern_W1','Midship_W1','Bow_W1']}
sources=[o for o in bpy.context.scene.objects if o.type=='MESH']
independent=[o for o in sources if not o.parent]
byroot={n:[o for o in sources if o.parent==r] for n,r in roots.items()}
record={'standard':'W1-center-expansion-r1','source':str(SOURCE),'added_beam_units':2*SHIFT,
        'standard_beam_units':9.28,'expanded_beam_units':12.08,'depth_changed':False,
        'coordinates':'Blender +X bow, +Y port, +Z up; existing authoring units, not declared game metres',
        'runtime_scaling':False,'modules':{},'status':'Offline review prototype; not registered with the backend'}
allparts=[];hullparts={};outerparts=[];insertparts=[]

def make(name,mesh,parent):
    ob=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(ob);ob.parent=parent
    allparts.append(ob)
    return ob

def clip(src,lo,hi,name):
    me=src.data.copy();bm=bmesh.new();bm.from_mesh(me)
    for y,inner in [(lo,True),(hi,False)]:
        if abs(y)>50:continue
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-7,
            plane_co=(0,y,0),plane_no=(0,1,0),clear_inner=inner,clear_outer=not inner)
    if not bm.faces:bm.free();bpy.data.meshes.remove(me);return None
    for v in bm.verts:
        for y in [lo,hi]:
            if abs(v.co.y-y)<EPS:v.co.y=y
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-8)
    bm.to_mesh(me);bm.free();me.update()
    return make(name,me,src.parent)

def expansion(co,module):
    if module!='Bow_W1':return SHIFT
    # Recover the approved bow's pre-rake station. Every height reaches zero
    # expansion at the actual stem, keeping one pointed nose and its prow.
    x=co.x+3.75;lo,hi=3.75,14.8
    for _ in range(28):
        p=(lo+hi)/2;t=max(0,min(1,(p-5)/7.4));w=t*t*(3-2*t)
        if p+w*(.35+.55*(co.z-1))<x:lo=p
        else:hi=p
    t=max(0,min(1,((lo+hi)/2-5)/7.4))
    return SHIFT*(1-t*t*(3-2*t))

def bridge(side,plane,sign,module,name):
    bm=bmesh.new();bm.from_mesh(side.data)
    layer=bm.loops.layers.color.get('Col')
    byte_color=layer is not None
    if layer is None:layer=bm.loops.layers.float_color.get('Col')
    verts=[];faces=[];colors=[]
    for e in bm.edges:
        if not e.is_boundary or not all(abs(v.co.y-plane)<EPS for v in e.verts):continue
        a,b=[v.co.copy() for v in e.verts];c,d=a.copy(),b.copy()
        c.y+=sign*expansion(c,module);d.y+=sign*expansion(d,module)
        poly=[a,b,d,c]
        clean=[]
        for p in poly:
            if not clean or (p-clean[-1]).length>1e-7:clean.append(p)
        if len(clean)>1 and (clean[0]-clean[-1]).length<1e-7:clean.pop()
        if len(clean)<3:continue
        normal=sum(((clean[i]-clean[0]).cross(clean[i+1]-clean[0]) for i in range(1,len(clean)-1)),Vector())
        if normal.length<1e-10:continue
        donor=e.link_faces[0]
        if normal.dot(donor.normal)<0:clean.reverse()
        start=len(verts);verts.extend(clean);faces.append(tuple(range(start,start+len(clean))))
        colors.append(tuple(donor.loops[0][layer]) if layer else (.6,.3,.1,1))
    bm.free()
    if not faces:return None
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    for mat in side.data.materials:me.materials.append(mat)
    attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for p,color in zip(me.polygons,colors):
        for i in p.loop_indices:
            if byte_color:attr.data[i].color_srgb=color
            else:attr.data[i].color=color
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bm.to_mesh(me);bm.free()
    ob=make(name,me,side.parent);insertparts.append(ob);return ob

for module,root in roots.items():
    hullparts[module]=[];record['modules'][module]={'parts':[],'length':float(root['length'])}
    for src in byroot[module]:
        component=src.name.split('__')[-1]
        if component not in ['Hull_Shell','Rails_Teal','Hull_Ironwork','Plank_Seams']:
            allparts.append(src);continue
        split=CORE if module=='Stern_W1' else 0.0
        made=[]
        if split:
            center=clip(src,-split,split,module+'__Core__'+component)
            if center:made.append(center)
        for sign,label in [(-1,'Starboard'),(1,'Port')]:
            plane=sign*split
            side=clip(src,-100 if sign<0 else plane,plane if sign<0 else 100,module+'__'+label+'__'+component)
            if side is None:continue
            strip=None if component=='Plank_Seams' else bridge(side,plane,sign,module,module+'__Insert_'+label+'__'+component)
            if strip:made.append(strip)
            if module=='Bow_W1':
                standard=side.data.copy()
                standard_ob=bpy.data.objects.new(side.name+'__Standard',standard)
                ss.export_fbx(standard_ob,str(OUT/'models'/'Standard_Bow_Halves'/(standard_ob.name+'.fbx')))
                bpy.data.objects.remove(standard_ob);bpy.data.meshes.remove(standard)
                for v in side.data.vertices:v.co.y+=sign*expansion(v.co,module)
            else:side.location.y=sign*SHIFT
            side.data.update();made.append(side);outerparts.append(side)
        if component=='Hull_Shell':hullparts[module]=made
        bpy.data.objects.remove(src,do_unlink=True)
    for o in root.children:
        if o.type=='EMPTY' and 'DeckSlot' in o.name:o.location.y+=math.copysign(SHIFT,o.location.y)
    root['interface_standard']='W1-center-expansion-r1';root['deck_beam']=12.08

# Carry the transom crown across the new shoulder spans. The housing and
# axle stay fixed; only the narrow transom wall and its capping rail change.
for ob in roots['Stern_W1'].children:
    if ob.type!='MESH' or not ob.name.endswith(('Hull_Shell','Rails_Teal')):continue
    for v in ob.data.vertices:
        p=ob.matrix_basis@v.co
        if p.x>.24:continue
        y=abs(p.y)
        old_y=y if y<=CORE else CORE if y<=CORE+SHIFT else y-SHIFT
        delta=.97*((old_y/3.85)**2-(y/(3.85+SHIFT))**2)
        if ob.name.endswith('Rails_Teal'):v.co.z+=delta
        elif p.z>1.80:
            old_top=2.09+.97*(1-(old_y/3.85)**2)
            weight=max(0,min(1,(p.z-1.80)/max(.01,old_top-1.80)))
            v.co.z+=delta*weight
    ob.data.update()

def combined(name,objects,world=False):
    bm=bmesh.new()
    for ob in objects:
        me=ob.data.copy()
        me.transform(ob.matrix_world if world else ob.matrix_basis)
        bm.from_mesh(me);bpy.data.meshes.remove(me)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=2e-5)
    # A split introduces collinear vertices on longitudinal sockets. Split the
    # opposing edge at those same points before welding; never hide a T joint.
    for _ in range(3):
        boundary=[e for e in bm.edges if e.is_boundary]
        vertices=list({v for e in boundary for v in e.verts});changed=False
        for e in boundary:
            if not e.is_valid:continue
            a,b=e.verts;d=b.co-a.co
            if d.length_squared<1e-14:continue
            hits=[]
            for v in vertices:
                if v in e.verts:continue
                t=(v.co-a.co).dot(d)/d.length_squared
                if 1e-5<t<1-1e-5 and (v.co-(a.co+t*d)).length<2e-5:hits.append((t,v.co.copy()))
            if hits:
                _,p=min(hits,key=lambda x:x[0]);fac=(p-a.co).length/d.length
                _,new=bmesh.utils.edge_split(e,a,fac);new.co=p;changed=True
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=2e-5)
        if not changed:break
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-8)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    stats={'boundary_edges':sum(e.is_boundary for e in bm.edges),'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces),'triangles':sum(len(f.verts)-2 for f in bm.faces)}
    me=bpy.data.meshes.new(name);bm.to_mesh(me);bm.free();ob=bpy.data.objects.new(name,me)
    return ob,stats

# Sparse new seams keep the added deck area at the original plank scale.
for module,root in roots.items():
    b=ss.Builder();start=0;end=float(root['length'])
    if module=='Stern_W1':
        lanes=[s*(CORE+.70) for s in [-1,1]]
        for y in lanes:b.box(y-.007,y+.007,1.765,1.785,.23,end,'seam')
    elif module=='Midship_W1':
        for y in [-.70,0,.70]:b.box(y-.007,y+.007,1.765,1.785,0,end,'seam')
    else:
        shell,_=combined('Temporary_Bow_Surface',hullparts[module])
        bm=bmesh.new();bm.from_mesh(shell.data);tree=BVHTree.FromBMesh(bm)
        for y in [-.7,0,.7]:
            points=[]
            for i in range(21):
                x=i*.5
                p,n,idx,dist=tree.ray_cast(Vector((x,y,8)),Vector((0,0,-1)),12)
                if p is not None and n.z>.5 and abs(y)<expansion(p,module)-.025:
                    points.append((y,p.z+.014,x))
            if len(points)>1:family.ship.tube(b,points,.014,.018,'seam')
        bm.free();bpy.data.meshes.remove(shell.data)
    ob=family.ship.mesh(module+'__Insert_Plank_Joints',b);ob.parent=root;allparts.append(ob)

bpy.context.view_layer.update()
hydro.self_test()
joined,stats=combined('Expanded_Hull_Validation',[o for pieces in hullparts.values() for o in pieces],True)
record['assembled_hull_validation']=stats
assert not stats['boundary_edges'] and not stats['overconnected_edges'] and not stats['degenerate_faces'],stats
bpy.data.meshes.remove(joined.data)
record['repeat_tests']={}
for count in [0,2]:
    old=roots['Bow_W1'].location.copy()
    roots['Bow_W1'].location.x=9.3+count*6
    items=list(hullparts['Stern_W1'])+list(hullparts['Bow_W1'])
    if count:items+=list(hullparts['Midship_W1'])
    clones=[]
    if count==2:
        for ob in hullparts['Midship_W1']:
            cp=ob.copy();bpy.context.scene.collection.objects.link(cp);cp.location.x+=6;clones.append(cp)
        items+=clones
    bpy.context.view_layer.update()
    joined,st=combined('Repeat_Test',items,True)
    assert not st['boundary_edges'] and not st['overconnected_edges'] and not st['degenerate_faces'],(count,st)
    record['repeat_tests'][str(count)]=st;bpy.data.meshes.remove(joined.data)
    for cp in clones:bpy.data.objects.remove(cp,do_unlink=True)
    roots['Bow_W1'].location=old
bpy.context.view_layer.update()
for module,objects in hullparts.items():
    ob,st=combined(module+'__Hull_Shell',objects)
    (OUT/'hydrostatics').mkdir(exist_ok=True)
    record['modules'][module]['hydrostatics']=hydro.export_module(ob,1.76,OUT/'hydrostatics'/(module+'.json'))
    bpy.data.meshes.remove(ob.data)

for module,root in roots.items():
    folder=OUT/'models'/module;folder.mkdir(parents=True,exist_ok=True)
    for ob in root.children:
        if ob.type!='MESH':continue
        filename=ob.name+'.fbx';ss.export_fbx(ob,str(folder/filename))
        record['modules'][module]['parts'].append({'name':ob.name,'file':str((folder/filename).relative_to(OUT)),'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),'local_position':list(ob.location)})
for o in independent:
    ss.export_fbx(o,str(OUT/'models'/'Fittings'/(o.name+'.fbx')))
record['total_visible_triangles']=sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type=='MESH' and not o.hide_render for p in o.data.polygons)
record['fittings']={'chimney_position':list(bpy.data.objects['Chimney'].location),'wheel_mount':'M1 unchanged','rotor_spin_axis':'local Y'}
record['bow_rule']='Select Standard_Bow_Halves for standard width, authored expanded Bow_W1 halves for expansion. Taper is baked at authoring time, never a runtime scale.'
(OUT/'manifest.json').write_text(json.dumps(record,indent=2))
mid=13.14
for name,eye,target,scale,res in [
    ('assembled',(-15,-32,24),(mid,0,.8),33,(1600,1100)),
    ('top',(mid,0,50),(mid,0,0),32,(1600,1000)),
    ('front',(55,0,5),(mid,0,1),16,(1100,1000)),
    ('back',(-35,0,5),(mid,0,1),16,(1100,1000)),
    ('side',(mid,-50,3),(mid,0,3),32,(1600,850))]:family.render(OUT/(name+'.png'),eye,target,scale,res)
family.save(OUT/'expanded-ship.blend')
for ob in outerparts:ob.location.y+=3 if '__Port__' in ob.name else -3
for ob in insertparts:ob.location.z+=1.5
for ob in bpy.context.scene.objects:
    if ob.type=='MESH' and ('Plank' in ob.name or ob.name=='Chimney'):ob.hide_render=True
bpy.context.view_layer.update()
family.render(OUT/'exploded.png',(-12,-37,32),(mid,0,1),38,(1600,1100))
family.save(OUT/'exploded-parts.blend')
for ob in bpy.context.scene.objects:
    if ob.type=='MESH' and ob.parent!=roots['Midship_W1']:ob.hide_render=True
family.render(OUT/'middle-insert-detail.png',(2,-19,14),(12.3,0,.5),22,(1400,1000))
print('WIDTH EXPANSION PASS',record['total_visible_triangles'],stats,flush=True)
