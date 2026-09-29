"""Independent saved-mesh and export inspection of the reshaped F15 hull."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import verify_f_coaster_v2 as checks
checks.OUT=checks.ROOT/'art-staging/f-coaster-v27'
if '--' not in sys.argv:
    sys.argv.extend(['--','--names','curved-bow-transitions'])
checks.main()
