"""Orthographic review of the approved short V2 assembly; no geometry edits."""
import sys
from pathlib import Path
import bpy

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v1 as family

ROOT=HERE.parents[1]/'art-staging/modular-hull-family-v2'
OUT=ROOT/'shape-review-short'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'short/ship.blend'))
centre=17.95/2
# Constant pixel scale in all four views: 80 pixels per authoring unit.
family.render(OUT/'top.png',(centre,0,50),(centre,0,0),22,(1760,960))
family.render(OUT/'side.png',(centre,-50,2.5),(centre,0,2.5),22,(1760,960))
family.render(OUT/'front.png',(50,0,2.5),(centre,0,2.5),12,(960,960))
family.render(OUT/'back.png',(-50,0,2.5),(centre,0,2.5),12,(960,960))
print('Four orthographic views rendered. Source blend unchanged.')
