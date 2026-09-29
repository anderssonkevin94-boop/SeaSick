"""Independent saved-mesh and export inspection of the reshaped F12 hull."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import verify_f_coaster_v2 as checks
checks.OUT=checks.ROOT/'art-staging/f-coaster-v12'
if '--' not in sys.argv:
    sys.argv.extend(['--','--names','long-shoulders'])
checks.main()
