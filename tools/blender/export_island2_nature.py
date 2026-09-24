"""Bake approved meshes into a compact, engine-axis vertex-colour library."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-staging/island-nature-kit-v1/unity-kit.json'
entries=[]
def pack(name,objects,low=False):
    vertices=[]; normals=[]; colors=[]; triangles=[]
    for original in objects:
        ob=original.copy(); ob.data=original.data.copy(); bpy.context.scene.collection.objects.link(ob)
        if low:
            bpy.context.view_layer.objects.active=ob
            mod=ob.modifiers.new('Distant silhouette','DECIMATE'); mod.ratio=.45
            bpy.ops.object.modifier_apply(modifier=mod.name)
        data=ob.data; data.calc_loop_triangles()
        attr=data.color_attributes.get('Col')
        matrix=ob.matrix_world; normalMatrix=matrix.to_3x3().inverted().transposed()
        for tri in data.loop_triangles:
            for li in tri.loops:
                p=matrix@data.vertices[data.loops[li].vertex_index].co
                n=(normalMatrix@data.polygons[tri.polygon_index].normal).normalized()
                c=attr.data[li].color if attr and attr.domain=='CORNER' else ((.4,.5,.2,1) if not attr else attr.data[data.loops[li].vertex_index].color)
                if name.startswith('Forest_') and ('_Canopy' in original.name or '_Wood' in original.name):
                    canopy='Canopy' in original.name
                    hexcode=('#82934F' if 'Birch' in name else '#7A8E46') if canopy else ('#B6B8A1' if 'Birch' in name else '#826647')
                    rgb=[int(hexcode[i:i+2],16)/255 for i in (1,3,5)]
                    c=tuple(q/12.92 if q<=.04045 else ((q+.055)/1.055)**2.4 for q in rgb)+(1,)
                # Proper rotation, determinant +1: retain winding and authored normals.
                vertices.append({'x':p.x,'y':p.z,'z':-p.y})
                normals.append({'x':n.x,'y':n.z,'z':-n.y})
                colors.append({'r':c[0],'g':c[1],'b':c[2],'a':1})
                triangles.append(len(vertices)-1)
        bpy.data.objects.remove(ob,do_unlink=True)
    entries.append({'name':name+('_LOD1' if low else ''),'vertices':vertices,'normals':normals,'colors':colors,'triangles':triangles})

for path in sorted((ROOT/'art-staging/forest-lowpoly-astra-v4').glob('Forest_*.fbx')):
    if '_Stump' in path.stem: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),colors_type='LINEAR')
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
    pack(path.stem,objects); pack(path.stem,objects,True)
for path in sorted((ROOT/'art-staging/island-nature-kit-v1/fbx').glob('*.fbx')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),colors_type='LINEAR')
    pack(path.stem,[o for o in bpy.context.scene.objects if o.type=='MESH'])
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'art-staging/groundcover-midweight-v1/groundcover-midweight-v1.blend'))
for ob in bpy.data.collections['Reusable assets - ground pivots'].objects:
    if ob.type=='MESH': pack(ob.name,[ob])
OUT.write_text(json.dumps({'entries':entries},separators=(',',':')))
print('EXPORTED',len(entries),'templates',OUT)
