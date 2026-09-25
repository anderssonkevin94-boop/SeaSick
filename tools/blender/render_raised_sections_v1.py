"""Visual + welded-topology check of the raised-section modules (RAISED-SECTIONS.md sec 3).

Opens the build output of modular_raised_sections_v1.py (raised-sections.blend), appends the
connected raised ends (art-staging/modular-raised-middle-v1/continuous-upper-deck.blend) and
the low W1x modules (modular-width-inserts-v1/expanded-ship.blend, the source of
Resources/ShipModules/Meshes/HullW1x_v1), then renders every variant alone and four mixed
ships, and welds each ship's Hull_Shell parts with Astra's combined() to count open edges.

  (a) SternWF (Astra raised stern) + low middle + BowWA (Astra raised bow)
  (b) low stern + MiddleWB + low bow
  (c) connected raised stern + MiddleWF + low middle + low bow
  (d) low stern + MiddleWA + connected raised bow

Run: SECTIONS_SCRATCH=<dir> LOW_W1X_BLEND=<expanded-ship.blend> \
     Blender --background --python tools/blender/render_raised_sections_v1.py
"""
import json
import os
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import modular_raised_stern_v2 as revision  # noqa: E402
import modular_raised_sections_v1 as build  # noqa: E402

family = revision.family
ROOT = HERE.parents[1]
OUT = ROOT / 'art-staging/modular-raised-sections-v1'
SCRATCH = Path(os.environ.get('SECTIONS_SCRATCH', str(OUT)))
CONNECTED = ROOT / 'art-staging/modular-raised-middle-v1/continuous-upper-deck.blend'
LOW = Path(os.environ.get('LOW_W1X_BLEND', str(ROOT / 'art-staging/modular-width-inserts-v1/expanded-ship.blend')))

bpy.ops.wm.open_mainfile(filepath=str(SCRATCH / 'raised-sections.blend'))


def append(path, roots, prefix):
    with bpy.data.libraries.load(str(path), link=False) as (src, dst):
        dst.objects = [n for n in src.objects]
    keep = set()
    for ob in dst.objects:
        if ob is None:
            continue
        top = ob
        while top.parent:
            top = top.parent
        if top.name in roots:
            keep.add(ob)
    for ob in dst.objects:
        if ob is None:
            continue
        if ob in keep and (ob.type in ('MESH', 'EMPTY')):
            bpy.context.scene.collection.objects.link(ob)
        else:
            bpy.data.objects.remove(ob, do_unlink=True)
    for ob in keep:
        ob.name = prefix + ob.name
    return {prefix + r: bpy.data.objects[prefix + r] for r in roots}


roots = {n: bpy.data.objects[n] for n in ['MiddleWF', 'MiddleWA', 'MiddleWB', 'SternWF', 'BowWA']}
roots.update(append(CONNECTED, ['Stern_W1', 'Bow_W1'], 'C_'))
roots.update(append(LOW, ['Stern_W1', 'Midship_W1', 'Bow_W1'], 'L_'))


def descendants(root):
    out = []
    for ob in root.children:
        out.append(ob)
        out += descendants(ob)
    return out


def show(layout):
    for ob in bpy.data.objects:
        if ob.type == 'MESH':
            ob.hide_render = True
    for name, x in layout:
        roots[name].location.x = x
        for ob in descendants(roots[name]):
            if ob.type == 'MESH':
                ob.hide_render = False
    bpy.context.view_layer.update()


def render(name, eye, target, scale, res=(1200, 850)):
    family.render(OUT / name, eye, target, scale, res)


report = {'variants': {}, 'assemblies': {}}
for label in ['MiddleWF', 'MiddleWA', 'MiddleWB']:
    show([(label, 9.3)])
    kit = {}
    for ob in descendants(roots[label]):
        if ob.type != 'MESH' or not ob.name.split('__')[1].endswith(('_Fwd', '_Aft')):
            continue
        xs = [(ob.matrix_world @ Vector(c)).x - 9.3 for c in ob.bound_box]
        kit[ob.name] = [round(min(xs), 3), round(max(xs), 3)]
    report['variants'][label] = {'wall_kit_x_extents_local': kit}
    render(f'{label}/threequarter-fwd.png', (12.3 + 13, -17, 13), (12.3, 0, 2.2), 15.5)
    render(f'{label}/threequarter-aft.png', (12.3 - 13, -17, 13), (12.3, 0, 2.2), 15.5)
    render(f'{label}/side.png', (12.3, -50, 1.4), (12.3, 0, 1.4), 12, (1200, 900))
    render(f'{label}/top.png', (12.3, -0.001, 50), (12.3, 0, 0), 13.5, (1100, 1000))
for label, x in [('SternWF', 0.0), ('BowWA', 15.3)]:
    show([(label, x)])
    c = 4.65 if label == 'SternWF' else 20.6
    render(f'{label}/threequarter.png', (c + (14 if label == 'SternWF' else -14), -17, 13), (c, 0, 2.6), 15)

SHIPS = {
    'a': [('SternWF', 0.0), ('L_Midship_W1', 9.3), ('BowWA', 15.3)],
    'b': [('L_Stern_W1', 0.0), ('MiddleWB', 9.3), ('L_Bow_W1', 15.3)],
    'c': [('C_Stern_W1', 0.0), ('MiddleWF', 9.3), ('L_Midship_W1', 15.3), ('L_Bow_W1', 21.3)],
    'd': [('L_Stern_W1', 0.0), ('MiddleWA', 9.3), ('C_Bow_W1', 15.3)],
}
for key, layout in SHIPS.items():
    show(layout)
    shells = [ob for name, _ in layout for ob in descendants(roots[name]) if ob.type == 'MESH' and 'Hull_Shell' in ob.name]
    pieces = []
    for ob in shells:
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.transform(ob.matrix_world)
        pieces.append(bm)
    bm = build.weld(pieces, 'Check_' + key)   # combined()'s weld with more T-joint passes
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    result = build.stats(bm)
    result['open_edges_at'] = [[tuple(round(c, 3) for c in v.co) for v in e.verts] for e in bm.edges if e.is_boundary][:8]
    bm.free()
    report['assemblies'][key] = {'layout': layout, 'shell_parts': len(shells), **result}
    length = layout[-1][1] + 11.0
    mid = length / 2
    render(f'assembled-{key}-threequarter.png', (mid + 16, -30, 22), (mid, 0, 1.5), length * 1.12, (1500, 950))
    render(f'assembled-{key}-threequarter-aft.png', (mid - 16, -30, 22), (mid, 0, 1.5), length * 1.12, (1500, 950))
    render(f'assembled-{key}-side.png', (mid, -60, 2.2), (mid, 0, 2.2), length * 1.08, (1600, 600))
    render(f'assembled-{key}-top.png', (mid, -0.001, 60), (mid, 0, 0), length * 1.08, (1600, 700))
    print(key, report['assemblies'][key], flush=True)
(OUT / 'render-report.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report['variants'], indent=1))
print('RAISED SECTIONS RENDERED', flush=True)
