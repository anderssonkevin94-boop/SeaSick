"""Centreline hatch, retaining the starboard door and continuous braces."""
import json
import sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_raised_bow_v3 as previous
base=previous.base
base.HATCH_Y=0.0
base.OUT=HERE.parents[1]/'art-staging/modular-raised-bow-v4'/base.MODE
base.OUT.mkdir(parents=True,exist_ok=True)
if __name__=='__main__':
    base.main()
    path=base.OUT/'manifest.json'
    manifest=json.loads(path.read_text())
    manifest['id']='RaisedBowFull_'+base.MODE+'_v4'
    manifest['access']='Starboard DoorEntry at Y=-2.15 connects through the lined internal passage to centreline HatchExit at Y=0. Traversal is not yet implemented.'
    path.write_text(json.dumps(manifest,indent=2))
