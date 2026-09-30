from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
p=Path(__file__).resolve().parent;o=Image.new('RGB',(1600,910),'#194455');d=ImageDraw.Draw(o);f=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',25);sm=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',18)
d.text((24,15),'SEA DISCOVERY KIT • V1 REVIEW',font=f,fill='#f4e5c9');d.text((24,53),'Individually framed • Blender previews, not in-game water',font=sm,fill='#b8d5d9')
items=[('MessageBottle','MESSAGE BOTTLE'),('SalvageCluster','SALVAGE • APPROVED CRATE'),('LashedBoardBundle','LASHED BOARDS'),('BrokenBoardLong','BROKEN BOARD • LONG'),('ReefSplitPeak','REEF • SPLIT PEAK'),('ReefLowLedge','REEF • LOW LEDGE'),('ReefLeaningTeeth','REEF • LEANING TEETH'),('BrokenBoardShort','BROKEN BOARD • SHORT')]
for i,(name,title) in enumerate(items):
 x=i%4*400;y=100+i//4*400;im=Image.open(p/(name+'.png')).convert('RGBA');o.paste(im,(x,y+35),im);d.text((x+15,y),title,font=sm,fill='#f4e5c9')
o.save(p/'sea-discovery-review.png');o.resize((800,455),Image.Resampling.LANCZOS).save(p/'sea-discovery-small.png')
