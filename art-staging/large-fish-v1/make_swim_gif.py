from PIL import Image
from pathlib import Path
p=Path(__file__).resolve().parent
files=sorted((p/'swim-frames').glob('*.png'))
assert len(files)==48, f'Expected 48 frames, got {len(files)}'
frames=[Image.open(f).convert('RGB') for f in files]
# One shared palette avoids color flicker between frames.
sheet=Image.new('RGB',(800,600*4))
for i,j in enumerate([0,12,24,36]):sheet.paste(frames[j],(0,i*600))
palette=sheet.quantize(colors=192)
indexed=[im.quantize(palette=palette,dither=Image.Dither.NONE) for im in frames]
indexed[0].save(p/'fish-swimming.gif',save_all=True,append_images=indexed[1:],duration=[40,40,40,40,40,50]*8,loop=0,optimize=False,disposal=2)
print('Created fish-swimming.gif: 48 frames / 2 seconds, looping.')
