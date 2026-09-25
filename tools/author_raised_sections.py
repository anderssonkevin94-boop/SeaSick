#!/usr/bin/env python3
"""Module JSON + Resources meshes for the raised-section modules (docs/RAISED-SECTIONS.md sec 4).

Input: art-staging/modular-raised-sections-v1/{build-report.json, <Label>/manifest.json, *.fbx}
(tools/blender/modular_raised_sections_v1.py) and the Hydrostatics/HullW1xRSections_v1 tables.
Output: Modules/hull.{middle.w1xr.wa,wf,wb, stern.w1xr.wf, bow.w1xr.wa}.v1.json and
Meshes/HullW1xRSections_v1/<Label>/*.fbx. Plain python, no Blender.
"""
import copy
import json
import math
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RES = ROOT / 'Assets/_Project/Resources/ShipModules'
MODDIR = RES / 'Modules'
STAGE = ROOT / 'art-staging/modular-raised-sections-v1'
HYDRO = RES / 'Hydrostatics/HullW1xRSections_v1'
MESHES = RES / 'Meshes/HullW1xRSections_v1'
U2_TO_M2 = 0.25
DECK_KG, WALL_KG, FLIGHT_KG = 110.0, 95.0, 60.0
LOW, UP = 1.76, 4.20
BAND = (4.62, 5.80)
CLEAR = (1.8, 2.3)

MODULES = {
    'MiddleWF': dict(id='hull.middle.w1xr.wf.v1', template='hull.middle.w1xr.v1', w1x='hull.middle.w1x.v1',
                     aft='W1xR', fwd='W1x', stairs=[(2.76, 5.96)], passage=(0.0, 5.80),
                     name='Midship_W1 (raised, end wall forward)'),
    'MiddleWA': dict(id='hull.middle.w1xr.wa.v1', template='hull.middle.w1xr.v1', w1x='hull.middle.w1x.v1',
                     aft='W1x', fwd='W1xR', stairs=[(0.04, 3.24)], passage=(0.20, 6.0),
                     name='Midship_W1 (raised, end wall aft)'),
    'MiddleWB': dict(id='hull.middle.w1xr.wb.v1', template='hull.middle.w1xr.v1', w1x='hull.middle.w1x.v1',
                     aft='W1x', fwd='W1x', stairs=[(2.76, 5.96)], passage=(0.20, 5.80),
                     name='Midship_W1 (raised, end walls both: stairs forward, door-only aft)'),
    'SternWF': dict(id='hull.stern.w1xr.wf.v1', template='hull.stern.w1xr.v1', w1x='hull.stern.w1x.v1',
                    aft=None, fwd='W1x', stairs=[(6.06, 9.26)], passage=(3.16, 9.10),
                    name='Stern_W1 (raised, end wall forward -- Astra modular-width-raised-v1)'),
    'BowWA': dict(id='hull.bow.w1xr.wa.v1', template='hull.bow.w1xr.v1', w1x='hull.bow.w1x.v1',
                  aft='W1x', fwd=None, stairs=[], passage=(0.20, 4.40),
                  name='Bow_W1 (raised, end wall aft -- Astra modular-width-raised-v1)'),
}


def interp(ws, vs, z):
    for i in range(len(ws) - 1):
        if ws[i] <= z <= ws[i + 1]:
            t = (z - ws[i]) / (ws[i + 1] - ws[i])
            return vs[i] + t * (vs[i + 1] - vs[i])
    return vs[-1] if z > ws[-1] else vs[0]


def hits_stairwell(pos, stairs):
    x0, x1 = pos['x'] - CLEAR[0] / 2, pos['x'] + CLEAR[0] / 2
    y0, y1 = abs(pos['y']) - CLEAR[1] / 2, abs(pos['y']) + CLEAR[1] / 2
    return any(x0 < b and a < x1 and y0 < BAND[1] and BAND[0] < y1 for a, b in stairs)


def main():
    report = json.loads((STAGE / 'build-report.json').read_text())
    out = {}
    for label, spec in MODULES.items():
        tpl = json.loads((MODDIR / f"{spec['template']}.json").read_text())
        w1x = json.loads((MODDIR / f"{spec['w1x']}.json").read_text())
        man = json.loads((STAGE / label / 'manifest.json').read_text())
        rep = report[label]
        m = rep['measure']
        table = json.loads((HYDRO / f'{label}.json').read_text())
        w1x_table = json.loads((RES / 'Hydrostatics/HullW1x_v1' / f"{tpl['hydrostatics']['resourcePath'].split('/')[-1]}.json").read_text())
        d = copy.deepcopy(tpl)
        d['id'] = spec['id']
        d['displayName'] = spec['name']
        d['source'] = f'art-staging/modular-raised-sections-v1/{label}/manifest.json'
        walls = [f for f, s in (('aft', spec['aft']), ('fwd', spec['fwd'])) if s == 'W1x']
        d['description'] = (f"Raised section (upper deck Z 4.20) closed by an END WALL on its {' and '.join(walls)} face "
                            "(bulkhead 1.76-4.20, guard rail, door; twin stairs recessed in the two outboard corners "
                            "unless noted), for a LOW neighbour on that side. docs/RAISED-SECTIONS.md sec 2-4. "
                            "Wall faces use join standard W1x, connected faces W1xR.")
        # sockets: join standards, dropped gun slots
        dropped = []
        socks = []
        for s in d['sockets']:
            if s['id'] == 'AftSocket' and spec['aft']:
                s['standard'] = spec['aft']
            if s['id'] == 'ForwardSocket' and spec['fwd']:
                s['standard'] = spec['fwd']
            if s['role'] == 'deck.slot' and hits_stairwell(s['posU'], spec['stairs']):
                dropped.append(s['id'])
                continue
            socks.append(s)
        d['sockets'] = socks
        d['equipmentSlots'] = [e for e in d['equipmentSlots'] if e['socketId'] not in dropped]
        if dropped:
            d['droppedGunSlots'] = {'ids': dropped, 'reason': (
                f"clearance box {CLEAR[0]}x{CLEAR[1]}x1.65 at Z 4.20 around the socket overlaps a stairwell "
                f"(x {spec['stairs']}, |y| {BAND[0]}-{BAND[1]}); RAISED-SECTIONS.md sec 4.")}
        else:
            d.pop('droppedGunSlots', None)
        gun = [i for i in tpl['capacity']['gunSlots']['ids'] if i not in dropped]
        d['capacity']['gunSlots']['ids'] = gun
        d['capacity']['gunSlots']['source'] = (f"{spec['template']} gun slots minus those whose clearance box hits a "
                                               f"stairwell (dropped: {dropped or 'none'}). Provisional.")
        # visuals
        vis = []
        for name, e in sorted(man['modules'][label].items()):
            vis.append({'id': name, 'resourcePath': f'ShipModules/Meshes/HullW1xRSections_v1/{label}/{name}',
                        'placeholder': False,
                        'localPositionU': dict(zip('xyz', [round(c, 4) for c in e['position']])),
                        'yawDegU': round(math.degrees(e['rotation_radians'][2]), 3),
                        'notes': f"{man['modules'][label][name]['file']}, {e['triangles']} triangles."
                                 + (' Hinge pivot = localPositionU; open pose as authored (Astra).' if name.endswith(('Door', 'Door_Fwd', 'Door_Aft')) else '')
                                 + (' Hinge pivot = localPositionU; Astra authored it pitched -80 deg about +Y (open), '
                                    'baked into this mesh because VisualPart has yaw only.' if name.endswith('Hatch') else '')})
        d['visuals'] = vis
        # passage over the raised deck, clear of guard rails; stair corners are outside |y| <= 2.30
        x0, x1 = spec['passage']
        for p in d['passages']:
            p['centreU']['x'] = round((x0 + x1) / 2, 3)
            p['sizeU']['x'] = round(x1 - x0, 3)
            p['notes'] = (f"Upper passage over the raised deck, x {x0}-{x1} (stops short of the end-wall guard rail); "
                          "|y| <= 2.30, so the stair corners (|y| 4.62-5.80) are not in it. Z centre 5.90, 3.4 tall.")
        # hydrostatics
        v_low = interp(table['waterlineZU'], table['integratedVolumeU3'], LOW)
        v_low_w1x = interp(w1x_table['waterlineZU'], w1x_table['integratedVolumeU3'], LOW)
        v_up = table['integratedVolumeU3'][-1]
        rel = abs(v_low - v_low_w1x) / v_low_w1x
        d['hydrostatics'] = {'resourcePath': f'ShipModules/Hydrostatics/HullW1xRSections_v1/{label}',
                             'sourceGeometrySha256': table['sourceGeometrySha256'],
                             'validWaterlineZU': table['validWaterlineZU'], 'offsetZU': 0,
                             'notes': (f"export_hull_hydrostatics.export_module on the module-local Hull_Shell, deck_z 4.20 "
                                       f"(tools/blender/modular_raised_sections_v1.py). Volume to 1.76: {v_low:.4f} U^3 vs "
                                       f"{spec['w1x']} {v_low_w1x:.4f} U^3 ({rel*100:.3f}%, gate <= 0.5%). To 4.20: {v_up:.4f} U^3 "
                                       "(stair notches and the door alcove are outside the shell, so already excluded).")}
        # mass
        deck_m2 = m['deckAreaU2'] * U2_TO_M2
        wall_m2 = m['wallAreaU2'] * U2_TO_M2
        deck_kg, wall_kg = round(deck_m2 * DECK_KG, 2), round(wall_m2 * WALL_KG, 2)
        stairs_kg = m['stairFlights'] * FLIGHT_KG
        upper = round(deck_kg + wall_kg + stairs_kg, 2)
        cz = (deck_kg * UP + wall_kg * m['wallCentroidZU'] + stairs_kg * (LOW + UP) / 2) / upper
        d['upperStructure'] = {
            'massKg': upper, 'centroidZU': round(cz, 4),
            'deckMassKg': deck_kg, 'deckAreaM2': round(deck_m2, 3),
            'wallMassKg': wall_kg, 'wallAreaM2': round(wall_m2, 3), 'wallAreaCentroidZU': round(m['wallCentroidZU'], 4),
            'bulkheadAreaM2': round(m['bulkheadAreaU2'] * U2_TO_M2, 3),
            'stairsMassKg': stairs_kg, 'stairFlights': m['stairFlights'],
            'source': ("Measured on the module-local Hull_Shell ABOVE Z 1.76 (faces clipped at 1.76, so the W1x "
                       "topside band below it is not double counted -- unlike hull.*.w1xr.v1, whose wall area starts "
                       "at Z 0.84): deck = flat top cap at 4.20 x 110 kg/m^2; walls = every other face above 1.76 "
                       "(topsides, bulkheads, stair-notch walls, alcove) x 95 kg/m^2; stairs 60 kg per flight. "
                       "centroidZU mass-weighted (stairs at 2.98). Provisional.")}
        d['lightship'] = {'massKg': round(w1x['lightship']['massKg'] + upper, 2), 'provisional': True,
                          'rule': f"{w1x['lightship']['massKg']} kg ({spec['w1x']} lightship) + {upper} kg upper structure."}
        upper_vol = v_up - v_low
        hold_add = round(0.7 * upper_vol / 38.229)
        berth_add = math.floor(0.3 * m['deckAreaU2'] / 12.8)
        d['capacity']['holdCells'] = {'value': w1x['capacity']['holdCells']['value'] + hold_add, 'provisional': True,
                                      'source': f"{spec['w1x']} {w1x['capacity']['holdCells']['value']} + round(0.7 x {upper_vol:.3f} / 38.229) = +{hold_add}; upper volume = own table V(4.20) - V(1.76), stairwells already outside the shell."}
        d['capacity']['berths'] = {'value': w1x['capacity']['berths']['value'] + berth_add, 'provisional': True,
                                   'source': f"{spec['w1x']} {w1x['capacity']['berths']['value']} + floor(0.3 x {m['deckAreaU2']:.3f} U^2 / 12.8) = +{berth_add}."}
        d['stairwells'] = [{'xU': [a, b], 'absYU': list(BAND), 'zU': [LOW, UP],
                            'notes': 'Twin stairs, bottom at the wall face (low deck 1.76), top inboard (4.20). Not walkable deck, not in the passage.'}
                           for a, b in spec['stairs']]
        d['walls'] = {'aft': spec['aft'] == 'W1x', 'fwd': spec['fwd'] == 'W1x',
                      'notes': 'W1x face = END WALL (bulkhead + guard rail + door) toward a LOW neighbour.'}
        d.pop('boundsNote', None)
        (MODDIR / f"{spec['id']}.json").write_text(json.dumps(d, indent=4) + '\n')
        dst = MESHES / label
        if dst.exists():
            shutil.rmtree(dst)
        dst.mkdir(parents=True)
        for f in (STAGE / label).glob('*.fbx'):
            shutil.copy2(f, dst / f.name)
        out[label] = {'id': spec['id'], 'dropped': dropped, 'kept': gun, 'upperKg': upper, 'lightshipKg': d['lightship']['massKg'],
                      'hold': d['capacity']['holdCells']['value'], 'berths': d['capacity']['berths']['value'],
                      'vol176': round(v_low, 3), 'vol176_w1x': round(v_low_w1x, 3), 'rel': round(rel * 100, 3), 'vol420': round(v_up, 3)}
    print(json.dumps(out, indent=1))


if __name__ == '__main__':
    main()
