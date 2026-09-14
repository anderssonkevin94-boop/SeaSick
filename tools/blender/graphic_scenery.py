"""Build the isolated graphic scenery kit using the existing project builders.
Preserves the active Blender scene and never clears the original KIT_V2.
"""
from pathlib import Path
import bpy

ROOT = Path('/Users/kevinandersson/Desktop/SeaSick')

def build():
    source = ROOT/'tools/blender/seasick_style.py'
    g = {'__name__':'graphic_scenery_builders', 'SS_NO_AUTORUN':True}
    exec(compile(source.read_text(),str(source),'exec'),g)
    scene = bpy.data.scenes.get('SeaSick_GraphicScenery') or bpy.data.scenes.new('SeaSick_GraphicScenery')
    previous = bpy.context.window.scene
    try:
        bpy.context.window.scene = scene
        for o in list(scene.objects):
            bpy.data.objects.remove(o,do_unlink=True)
        coll = bpy.data.collections.get('GRAPHIC_KIT') or bpy.data.collections.new('GRAPHIC_KIT')
        if coll.name not in scene.collection.children:
            scene.collection.children.link(coll)
        g['C'].update(
            spruce_d=(.12,.24,.24), spruce_m=(.22,.38,.26), spruce_l=(.39,.52,.26),
            broad_d=(.17,.28,.16), broad_m=(.32,.46,.18), broad_l=(.51,.62,.25),
            palm_d=(.18,.34,.18), palm_l=(.46,.62,.24),
            stone_d=(.33,.40,.51), stone_m=(.51,.55,.59), stone_l=(.70,.69,.59),
            bark_d=(.27,.19,.13), bark_m=(.43,.29,.17), bark_l=(.62,.43,.24),
        )
        kit = [
            g['build_spruce']('Spruce',coll,h=13,tiers=4,sides=7,crown=.23,base=.18,seed=3,tuck=.85,tip_radius=.055),
            g['build_spruce']('Spruce_LOD1',coll,h=13,tiers=3,sides=5,crown=.23,base=.18,seed=3,tuck=.85,tip_radius=.055,trunk_sides=4,trunk_segs=2),
            g['build_broadleaf']('Broad',coll,h=10.5,crown=.37,seed=5,pts=30),
            g['build_broadleaf']('Broad_LOD1',coll,h=10.5,crown=.37,seed=5,pts=16,lod=True),
            g['build_palm']('Palm',coll,h=9,seed=9),
            g['build_palm']('Palm_LOD1',coll,h=9,seed=9,lod=True),
            g['build_ore']('Ore',coll,seed=7),
        ]
        for i in range(3):
            kit += [g['build_crop']('Crop_%d'%i,coll,seed=70+i),
                    g['build_crop']('Crop_%d_LOD1'%i,coll,seed=70+i,lod=True),
                    g['build_scrub']('Scrub_%d'%i,coll,seed=73+i),
                    g['build_scrub']('Scrub_%d_LOD1'%i,coll,seed=73+i,lod=True)]
        for i in range(4):
            kit.append(g['build_unit_shard']('Boulder_%d'%i,coll,400+i))
        for i in range(3):
            o = g['build_unit_shard']('Cliff_%d'%i,coll,500+i,tall=True)
            # Broader planes, with the same vertical template extent. These
            # remain visual rocks rather than new terrain/collider surfaces.
            for v in o.data.vertices:
                v.co.x *= 1.3
                v.co.y *= 1.3
            kit.append(o)
        # Blender suffixes global object names when the original kit is open.
        # Temporarily free the export names, then restore the user's objects.
        renamed=[]
        try:
            for o in kit:
                desired=o.name.split('.')[0]
                existing=bpy.data.objects.get(desired)
                if existing is not None and existing != o:
                    renamed.append((existing,existing.name))
                    existing.name='Original_'+existing.name
                o.name=desired
            out=ROOT/'Assets/_Project/Resources/Flora/graphic_flora.fbx'
            g['export_kit'](kit,str(out))
            print('Graphic kit:',len(kit),'meshes;',sum(g['tris'](o) for o in kit),'triangles')
        finally:
            for o in kit: o.name='Graphic_'+o.name
            for o,name in renamed: o.name=name
    finally:
        bpy.context.window.scene=previous

if __name__=='__main__': build()
