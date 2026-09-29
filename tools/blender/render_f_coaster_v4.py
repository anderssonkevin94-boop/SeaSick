"""Detail renders of the saved F4 boat; leaves the source scene unchanged."""
from pathlib import Path
import sys
import argparse
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
import f_coaster_v1 as kit
p=argparse.ArgumentParser();p.add_argument('--name',default='design-study');p.add_argument('--side-only',action='store_true')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=Path(__file__).resolve().parents[2]/'art-staging/f-coaster-v4'/a.name
bpy.ops.wm.open_mainfile(filepath=str(out/'ship.blend'))
bpy.context.scene.cycles.samples=32
if not a.side_only:
    kit.shot(out/'bow-detail.png',(27,-16,15),(19,0,1.6),13,(1500,1200))
    kit.shot(out/'stern-detail.png',(-17,-18,12),(1,0,2),19,(1500,1250))
kit.shot(out/'side.png',(11.0,-65,2.7),(11.0,0,2.7),30,(1700,900))
