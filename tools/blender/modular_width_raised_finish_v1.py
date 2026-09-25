"""Complete raised end replacements on the expanded hull, staged only."""
import sys,json,math,ast
from pathlib import Path
import bpy,bmesh
from mathutils import Vector,Matrix
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_raised_stern_v2 as stairs
old=stairs.old;base=old.base;fore=old.fore;ship=old.ship;ss=old.ss;family=old.family
OUT=HERE.parents[1]/'art-staging/modular-width-raised-v1'
OUT.mkdir(parents=True,exist_ok=True)
# Share the tested seam welding function without executing the lower-kit generator.
tree=ast.parse((HERE/'modular_width_inserts_v1.py').read_text())
fn=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='combined')
exec(compile(ast.Module(body=[fn],type_ignores=[]),str(HERE/'modular_width_inserts_v1.py'),'exec'))
bpy.ops.wm.open_mainfile(filepath=str(OUT.parent/'modular-width-inserts-v1/expanded-ship.blend'))
for module in ['Stern_W1','Midship_W1','Bow_W1']:
    root=bpy.data.objects[module]
    for component in ['Hull_Shell','Rails_Teal','Hull_Ironwork','Plank_Seams']:
        pieces=[o for o in root.children if o.type=='MESH' and (o.name.endswith(component) or component=='Plank_Seams' and o.name.endswith('Insert_Plank_Joints'))]
        if not pieces:continue
        ob,stats=combined(module+'__'+component,pieces)
        for mat in pieces[0].data.materials:ob.data.materials.append(mat)
        for p in pieces:bpy.data.objects.remove(p,do_unlink=True)
        bpy.context.scene.collection.objects.link(ob);ob.parent=root
        if ob.data.color_attributes.get('Col'):ob.data.color_attributes.active_color=ob.data.color_attributes['Col']
        for p in ob.data.polygons:p.use_smooth=False
family.INTERFACE_STANDARD='W1-center-expansion-r1'
# Upper perimeter follows the exact widened lower deck stations.
original_hull_point=fore.hull_point
points={x:original_hull_point(x) for x in set(ship.ST+[family.CUT_B,5,6.7,8.3,9.8,11.2,12.4])}
def widened_point(x):
    a,w,z=points[x];t=max(0,min(1,(x-5)/7.4))
    return a,w+1.4*(1-t*t*(3-2*t)),z
fore.hull_point=widened_point
ship.BEAM=[w+1.4 if x<=5 else w for x,w in zip(ship.ST,ship.BEAM)]
base.OUT=OUT/'bow-source';(base.OUT/'short').mkdir(parents=True,exist_ok=True)
manifest={'interface':family.INTERFACE_STANDARD,'status':'Offline end-section replacements; no Unity changes','modules':{},'remaining':['Raised middle bay not supplied','Same-height end-to-end upper deck joins need a separate tested treatment','Runtime UI, navigation and balance integration not included']}
def export_section(label,root,parts,moving):
    folder=OUT/label;folder.mkdir(exist_ok=True)
    entry={'root':root.name,'length':float(root['length']),'moving_parts':moving,'parts':{},'sockets':{o.name:list(o.location) for o in root.children if o.type=='EMPTY'}}
    for name,ob in parts.items():
        check=ship.check(ob)
        assert not check['overconnected_edges'] and not check['degenerate_faces'],(name,check)
        ss.export_fbx(ob,str(folder/(name+'.fbx')))
        entry['parts'][name]={'file':label+'/'+name+'.fbx','position':list(ob.location),'rotation_radians':list(ob.rotation_euler),'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),'topology':check}
    manifest['modules'][label]=entry
    keep={root,*root.children_recursive}
    hidden={o:o.hide_render for o in bpy.context.scene.objects if o.type=='MESH'}
    for ob in hidden:ob.hide_render=ob not in keep
    x=root.location.x
    family.render(folder/'isolated.png',(x-12,-19,16),(x+4.5,0,2),23,(1400,1050))
    family.render(folder/'access.png',(x+18,-18,15) if label=='raised-stern' else (x-9,-13,13),(x+5 if label=='raised-stern' else x+1.5,0,2.5),20 if label=='raised-stern' else 14,(1400,1050))
    for ob,hide in hidden.items():ob.hide_render=hide
    return entry

bow,bowparts,bowmoving=base.build()
export_section('raised-bow',bow,bowparts,bowmoving)
family.save(base.OUT/'short/ship.blend')
family.render(OUT/'bow-raised.png',(-15,-37,29),(13,0,2),36,(1600,1100))
family.save(OUT/'bow-raised.blend')
stern,sternparts,sternmoving=old.build()
export_section('raised-stern',stern,sternparts,sternmoving)
bpy.context.view_layer.update()
joined,check=combined('AssembledRaisedHull',[bpy.data.objects[n+'__Hull_Shell'] for n in ['Stern_W1','Midship_W1','Bow_W1']],True)
assert not check['boundary_edges'] and not check['overconnected_edges'] and not check['degenerate_faces'],check
bpy.data.meshes.remove(joined.data);manifest['assembled_hull']=check
rotor=bpy.data.objects['Rotor'];saved=rotor.rotation_euler.copy();shell_tree=family.wheel_study.bvh(sternparts['Hull_Shell'])
for angle in range(0,360,5):
    rotor.rotation_euler.y=math.radians(angle);bpy.context.view_layer.update()
    assert not shell_tree.overlap(family.wheel_study.bvh(rotor)),('Wheel collision',angle)
rotor.rotation_euler=saved
manifest['wheel_rotation_samples_passed']=72
manifest['triangles']=sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type=='MESH' and not o.hide_render for p in o.data.polygons)
for name,eye,target,scale,res in [('both-raised',(-16,-38,29),(13,0,2),36,(1600,1100)),('top',(13,0,50),(13,0,0),34,(1600,1100)),('side',(13,-50,2),(13,0,2),34,(1600,850))]:family.render(OUT/(name+'.png'),eye,target,scale,res)
family.save(OUT/'both-raised.blend')
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
print('RAISED WIDTH PASS',manifest['triangles'],check)
