"""Run through Blender MCP. Convert the reviewed GLBs to Unity FBX; preserve the artist scene."""
import bpy
from pathlib import Path
R = Path('/Users/kevinandersson/Desktop/SeaSick')
previous = bpy.context.window.scene
original_objects = set(bpy.data.objects)
original_meshes = set(bpy.data.meshes)
original_materials = set(bpy.data.materials)
files = [(p, R/'Assets/_Project/Art/SettlementKitV1/Models'/f'{p.stem}.fbx')
         for p in sorted((R/'tools/blender/exports/settlement-kit-v1').glob('*.glb'))]
files.append((R/'tools/blender/exports/crew-weathered-v2/male-coat-deckhand.glb',
              R/'Assets/_Project/Art/CrewWeatheredV2/Models/WeatheredSailor.fbx'))
try:
    for source, target in files:
        scene = bpy.data.scenes.new('Unity_Export_Temporary')
        bpy.context.window.scene = scene
        try:
            bpy.ops.import_scene.gltf(filepath=str(source))
            palette = bpy.data.materials.new('Imported_VertexPalette')
            for ob in scene.objects:
                if ob.type != 'MESH': continue
                mesh = ob.data
                # The source preview used two-sided roofs; point roof surfaces outward
                # for Unity's backface-culling shader without changing source files.
                flipped = 0
                for poly in mesh.polygons:
                    mat = mesh.materials[poly.material_index]
                    if 'roof' in mat.name.lower() and poly.normal.z < -0.1:
                        poly.flip()
                        flipped += 1
                if flipped:
                    mesh.update()
                    normals = [None] * len(mesh.loops)
                    for poly in mesh.polygons:
                        for li in poly.loop_indices: normals[li] = tuple(poly.normal)
                    mesh.normals_split_custom_set(normals)
                    print('Corrected roof faces:', flipped)
                colors = mesh.color_attributes.active_color
                if colors is None:
                    colors = mesh.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='CORNER')
                    for poly in mesh.polygons:
                        mat = mesh.materials[poly.material_index]
                        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
                        color = bsdf.inputs['Base Color'].default_value if bsdf else mat.diffuse_color
                        for li in poly.loop_indices: colors.data[li].color = color
                mesh.color_attributes.active_color = colors
                mesh.materials.clear()
                mesh.materials.append(palette)
                for poly in mesh.polygons: poly.material_index = 0
            for ob in scene.objects:
                for key in list(ob.keys()):
                    value = ob[key]
                    if hasattr(value, 'to_list'):
                        ob[key] = [float(v) for v in value]
            bpy.ops.object.select_all(action='SELECT')
            bpy.ops.export_scene.fbx(filepath=str(target), use_selection=True,
                object_types={'MESH','EMPTY'}, use_custom_props=True,
                axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS',
                colors_type='LINEAR', use_mesh_modifiers=True, bake_anim=False,
                add_leaf_bones=False)
            print('EXPORTED', target.name, len(scene.objects))
        finally:
            bpy.context.window.scene = previous
            for ob in list(scene.objects): bpy.data.objects.remove(ob, do_unlink=True)
            bpy.data.scenes.remove(scene)
finally:
    bpy.context.window.scene = previous
    for mesh in set(bpy.data.meshes) - original_meshes:
        if mesh.users == 0: bpy.data.meshes.remove(mesh)
    for mat in set(bpy.data.materials) - original_materials:
        if mat.users == 0: bpy.data.materials.remove(mat)
    assert set(bpy.data.objects) == original_objects, 'Artist object set changed'
    print('Artist scene restored:', previous.name)
