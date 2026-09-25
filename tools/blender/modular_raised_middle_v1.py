"""Raised expanded middle bay and matching continuous-deck end variants."""
import ast,json,sys
from pathlib import Path
import bpy,bmesh
from mathutils import Vector,Matrix
HERE=Path(__file__).resolve().parent;sys.path.insert(0,str(HERE))
import modular_raised_stern_v2 as revision
family,ship,ss,fore=revision.family,revision.ship,revision.ss,revision.fore
OUT=HERE.parents[1]/'art-staging/modular-raised-middle-v1';OUT.mkdir(parents=True,exist_ok=True)
tree=ast.parse((HERE/'modular_width_inserts_v1.py').read_text())
fn=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='combined')
exec(compile(ast.Module(body=[fn],type_ignores=[]),str(HERE/'modular_width_inserts_v1.py'),'exec'))
bpy.ops.wm.open_mainfile(filepath=str(OUT.parent/'modular-width-raised-v1/both-raised.blend'))
names=['Stern_W1','Midship_W1','Bow_W1'];roots={n:bpy.data.objects[n] for n in names}
shells=[bpy.data.objects[n+'__Hull_Shell'] for n in names]
whole,check=combined('Continuous_Hull',shells,True)
for mat in shells[0].data.materials:whole.data.materials.append(mat)
bpy.context.scene.collection.objects.link(whole)
whole.data.color_attributes.active_color=whole.data.color_attributes['Col']
for ob in shells:bpy.data.objects.remove(ob,do_unlink=True)

def volume(name,x0,x1,y0,y1,z0,z1):
    b=ss.Builder();fore.box(b,x0,x1,y0,y1,z0,z1,'deck')
    ob=ship.mesh(name,b)
    bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    return ob

# Union against the complete closed shell first. Splitting afterwards eliminates
# doubled mating bulkheads and keeps exact upper and lower connection loops.
ship.boolean_into(whole,volume('Middle_Upper_Volume',9.25,15.35,-6.04,6.04,1.70,4.20),'UNION')
for sign in [-1,1]:
    lo,hi=sorted([sign*4.62,sign*5.80])
    ship.boolean_into(whole,volume('Fill_Obsolete_Stair_Well',6.055,9.31,lo-.003,hi+.003,1.70,4.20),'UNION')

def clean(ob):
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-6)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    for p in ob.data.polygons:p.use_smooth=False
    if ob.data.color_attributes.get('Col'):ob.data.color_attributes.active_color=ob.data.color_attributes['Col']
clean(whole)
check=ship.check(whole)
assert not any(check[k] for k in ['boundary_edges','overconnected_edges','degenerate_faces']),check
for name,lo,hi in [('Stern_W1',-100,9.3),('Midship_W1',9.3,15.3),('Bow_W1',15.3,100)]:
    ob=family.trim(whole,name+'__Hull_Shell',lo,hi)
    if lo==-100:ob.data.transform(Matrix.Translation((-100,0,0)))
    ob.parent=roots[name];clean(ob)
bpy.data.objects.remove(whole,do_unlink=True)

# Flush adjoining decks have no need for drop-edge guards, entrance doors or
# stairs descending into the newly enclosed middle section. Hatches stay usable.
remove={'ForwardGuard','RearGuard','TwinStairs','StairGuards','StairNosings','Door'}
for name,root in roots.items():
    for ob in list(root.children):
        component=ob.name.split('__')[-1].split('.')[0]
        if ob.type=='MESH' and (component in remove or name=='Midship_W1' and component=='Rails_Teal'):
            bpy.data.objects.remove(ob,do_unlink=True)
        elif ob.type=='EMPTY' and any(t in ob.name for t in ['Stair','DoorEntry','DeckSlot']):
            bpy.data.objects.remove(ob,do_unlink=True)
        elif ob.type=='MESH' and component=='PortalFrames':
            bm=bmesh.new();bm.from_mesh(ob.data)
            doomed=[v for v in bm.verts if v.co.x<.5] if name=='Bow_W1' else [v for v in bm.verts if v.co.x>8.8]
            bmesh.ops.delete(bm,geom=doomed,context='VERTS');bm.to_mesh(ob.data);bm.free()
        elif ob.type=='MESH' and component=='UpperRails':
            for v in ob.data.vertices:
                if name=='Bow_W1' and v.co.x<.15:v.co.x-=.04
                if name=='Stern_W1' and v.co.x>9.15:v.co.x+=.04

mid=roots['Midship_W1'];iron=bpy.data.objects['Midship_W1__Hull_Ironwork']
bm=bmesh.new();bm.from_mesh(iron.data);bm.normal_update()
# Remove obsolete low rail posts as whole connected components.
todo=set(bm.verts)
while todo:
    seed=todo.pop();stack=[seed];group={seed}
    while stack:
        for e in stack.pop().link_edges:
            for v in e.verts:
                if v in todo:todo.remove(v);group.add(v);stack.append(v)
    if min(v.co.z for v in group)>1.70:bmesh.ops.delete(bm,geom=list(group),context='VERTS')
bm.normal_update()
caps=[f for f in bm.faces if f.normal.z>.9 and 1.70<f.calc_center_median().z<1.90 and abs(f.calc_center_median().y)>4]
assert len(caps)==2,len(caps)
for f in caps:
    result=bmesh.ops.extrude_face_region(bm,geom=[f])
    for v in result['geom']:
        if isinstance(v,bmesh.types.BMVert):v.co.z=4.16
    bmesh.ops.delete(bm,geom=[f],context='FACES_ONLY')
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(iron.data);bm.free()
seams=bpy.data.objects['Midship_W1__Plank_Seams'];bm=bmesh.new();bm.from_mesh(seams.data)
bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.co.z>1.72],context='VERTS');bm.to_mesh(seams.data);bm.free()
rail,posts,planks,bolts=[ss.Builder() for _ in range(4)]
for sign in [-1,1]:
    y=sign*6.04
    fore.tube(rail,[(0,y,4.90),(6,y,4.90)],.28,.25,'teal')
    for x in [1.5,3,4.5]:
        fore.box(posts,x-.085,x+.085,y-.085,y+.085,4.20,4.80,'wood')
        fore.box(posts,x-.12,x+.12,y-.12,y+.12,4.79,4.94,'iron')
    for z in [2.4,3,3.6]:fore.tube(planks,[(0,y+sign*.01,z),(6,y+sign*.01,z)],.012,.016,'seam')
    for z in [2.7,3.8]:bolts.cyl(fore.point(4.85,y+sign*.06,z),fore.point(4.85,y+sign*.14,z),.08,.08,6,'bolt')
for n in range(-8,9):
    y=n*.7;fore.box(planks,0,6,y-.007,y+.007,4.203,4.215,'seam')
    x=1.5+(n%3)*1.3;fore.box(planks,x-.007,x+.007,y,y+.7,4.203,4.215,'seam')
for name,b in [('UpperRails',rail),('UpperPosts',posts),('UpperPlanks',planks),('UpperBraceBolts',bolts)]:
    ob=ship.mesh('RaisedMiddle__'+name,b);ob.parent=mid
for x,label in [(0,'Aft'),(6,'Forward')]:family.empty('RaisedMiddle_'+label+'UpperSocket',(x,0,4.2),mid)
chimney=bpy.data.objects['Chimney'];chimney.location=(12.3,0,4.20)

def hull_check():
    bpy.context.view_layer.update()
    ob,result=combined('Check',[bpy.data.objects[n+'__Hull_Shell'] for n in names],True)
    bpy.data.meshes.remove(ob.data)
    assert not any(result[k] for k in ['boundary_edges','overconnected_edges','degenerate_faces']),result
    return result
manifest={'interface':'W1-center-expansion-r1','middle_length':6,'deck_height':4.2,'beam':12.08,'source_axes':'+X forward, +Y port, +Z up','export_axes':'game (-y,z,x)','assemblies':{'one_middle':hull_check()},'modules':{},'chimney_position':list(chimney.location)}
for name,root in roots.items():
    folder=OUT/name;folder.mkdir(exist_ok=True);entry={}
    for ob in root.children:
        if ob.type!='MESH':continue
        clean(ob);test=ship.check(ob)
        assert not test['overconnected_edges'] and not test['degenerate_faces'],(ob.name,test)
        path=folder/(ob.name+'.fbx');ss.export_fbx(ob,str(path))
        entry[ob.name]={'file':str(path.relative_to(OUT)),'position':list(ob.location),'rotation_radians':list(ob.rotation_euler),'triangles':test['triangles']}
    manifest['modules'][name]=entry
manifest['triangles']=sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type=='MESH' for p in o.data.polygons)
ss.export_fbx(chimney,str(OUT/'Fittings/Chimney.fbx'))
manifest['chimney_file']='Fittings/Chimney.fbx'
family.render(OUT/'assembled.png',(-16,-38,29),(13,0,3),36,(1600,1100))
family.render(OUT/'side.png',(13,-50,3),(13,0,3),34,(1600,850))
family.save(OUT/'continuous-upper-deck.blend')
hidden={o:o.hide_render for o in bpy.context.scene.objects if o.type=='MESH'}
for ob in hidden:ob.hide_render=ob.parent!=mid
family.render(OUT/'middle-isolated.png',(0,-20,15),(12.3,0,1.6),21,(1400,1050))
for ob,hide in hidden.items():ob.hide_render=hide
# The 6 m repeat is geometric, not a stretched mesh.
copies=[]
for ob in list(mid.children):
    if ob.type!='MESH':continue
    cp=ob.copy();bpy.context.scene.collection.objects.link(cp);cp.location.x+=6;copies.append(cp)
roots['Bow_W1'].location.x+=6;bpy.context.view_layer.update()
ob,test=combined('TwoMiddleCheck',[bpy.data.objects[n+'__Hull_Shell'] for n in names]+[o for o in copies if 'Hull_Shell' in o.name],True)
assert not any(test[k] for k in ['boundary_edges','overconnected_edges','degenerate_faces']),test
manifest['assemblies']['two_middle']=test;bpy.data.meshes.remove(ob.data)
family.render(OUT/'two-middle.png',(-16,-42,31),(16,0,3),42,(1600,1100))
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
print('RAISED MIDDLE PASS',manifest['triangles'],manifest['assemblies'])
