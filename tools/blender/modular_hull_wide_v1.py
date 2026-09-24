"""W2 hull family: wider beam and deeper body with the existing M1 wheel."""
import json
import sys
from pathlib import Path

import bpy

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v3 as v3

family=v3.family
ship=v3.ship
OUT=HERE.parents[1]/'art-staging/modular-hull-wide-v1'


def configure():
    v3.v2.configure()
    ship.BEAM=[w*1.25 for w in ship.BEAM]
    levels=[-2.75+2.03*(max(0,x-5)/(ship.ZF-5))**2 for x in ship.ST]
    ship.keel=lambda x:ship.interp(x,levels)


def setup():
    v3.setup()
    OUT.mkdir(parents=True,exist_ok=True)
    family.OUT=OUT
    family.INTERFACE_STANDARD='W2-r1'
    family.STERN, family.MIDDLE, family.BOW='Stern_W2','Midship_W2','Bow_W2'
    family.MODULES={family.STERN:(ship.ZT,family.CUT_A),
                    family.MIDDLE:(family.CUT_A,family.CUT_B),
                    family.BOW:(family.CUT_B,ship.ZF)}
    family.LENGTHS={m:b-a for m,(a,b) in family.MODULES.items()}
    family.CROSS_SECTION={'deck_beam':11.60,'deck_height':1.76,'keel_height':-2.75}
    family.SLOT_OFFSET=4.35
    family.configure_geometry=configure


def main():
    setup()
    family.main()
    manifest=json.loads((OUT/'manifest.json').read_text())
    manifest['compatibility']='W2-r1 sections join only W2-r1. M1 rotor/carrier remain shared with W1-r2. No W1/W2 adapter supplied.'
    manifest['shape']='25 percent wider deck beam; deeper body; unchanged flat working deck, M1 wheel, fitting sizes and V3 pointed prow.'
    manifest['bow_length_note']='Includes decorative prow, not a joinable forward extension.'
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'short/ship.blend'))
    length=family.LENGTHS[family.STERN]+family.LENGTHS[family.BOW]
    mid=length/2
    for name,eye,target,scale,res in [
        ('side',(mid,-50,2),(mid,0,2),24,(1800,900)),
        ('top',(mid,0,50),(mid,0,0),24,(1800,1150)),
        ('front',(50,0,1.6),(mid,0,1.6),14,(1200,1100)),
        ('back',(-40,0,1.6),(mid,0,1.6),14,(1200,1100))]:
        family.render(OUT/(name+'.png'),eye,target,scale,res)


if __name__=='__main__': main()
