"""Independent saved geometry and FBX inspection for the F4 material/form study."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import verify_f_coaster_v2 as checks
checks.OUT=checks.ROOT/'art-staging/f-coaster-v4'
if '--' not in sys.argv:
    sys.argv.extend(['--','--names','design-study'])
checks.main()
