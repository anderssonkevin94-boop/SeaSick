"""Render the saved final F2 geometry; no regeneration or changes to source files."""
import argparse
import json
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector, Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit

OUT=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v2'
p=argparse.ArgumentParser();p.add_argument('--name',default='narrow-raised');p.add_argument('--detail',action='store_true');p.add_argument('--cutaway',action='store_true')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:])
dest=OUT/a.name;m=json.loads((dest/'manifest.json').read_text());level=m['stern_levels']
bpy.ops.wm.open_mainfile(filepath=str(dest/'ship.blend'))
scene=bpy.context.scene;scene.cycles.samples=32
if a.cutaway:
    # Real median-plane section through the saved model, not an invented diagram.
    for o in list(scene.objects):
        if o.type!='MESH' or o.name=='Studio_Ground':continue
        if o.name.startswith('Review_') or o.get('review_only'):
            o.hide_render=True;continue
        dg=bpy.context.evaluated_depsgraph_get();ev=o.evaluated_get(dg)
        mesh=bpy.data.meshes.new_from_object(ev);mesh.transform(o.matrix_world)
        o.parent=None;o.matrix_world=Matrix.Identity(4);o.modifiers.clear();o.data=mesh
        bm=bmesh.new();bm.from_mesh(mesh)
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
            plane_co=(0,0,0),plane_no=(0,1,0),clear_inner=True,dist=.00001)
        bm.to_mesh(mesh);bm.free()
    kit.shot(dest/'wheel-well-cutaway.png',(-12,-25,14),(3.1,0,3.1),22,(1550,1200))
else:
    kit.shot(dest/'hero.png',(-24,-33,26+level*3),(9.76,0,3.1+level),34)
    # Save a useful opening camera in the deliverable .blend.
    bpy.ops.wm.save_as_mainfile(filepath=str(dest/'ship.blend'))
    if a.detail:
        kit.shot(dest/'stern-detail.png',(-17,-18,12+level*3),(1.0,0,2.0+level*1.5),19,(1500,1250))
        kit.shot(dest/'side.png',(10.49,-65,4),(10.49,0,3.8),30,(1750,850))
        kit.shot(dest/'top.png',(10.74,0,65),(10.74,0,0),30,(1600,1100))
        kit.shot(dest/'deck-detail.png',(18.2,-17,21),(12.2,0,2.76),19,(1400,1100))
print('F2_RENDER_DONE '+a.name)
