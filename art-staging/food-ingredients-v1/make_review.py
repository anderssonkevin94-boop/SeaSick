from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
p=Path(__file__).resolve().parent;names=['Potato','Carrot','Onion','Wheat','Apple','Fish','Meat'];out=Image.new('RGB',(1792,695),'#23343c');d=ImageDraw.Draw(out);font=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',23);small=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',17)
d.text((24,16),'INGREDIENT KIT • V1 REVIEW',font=font,fill='#f4e1bb');d.text((24,50),'Matching 3D models, crate-top stock displays and 256px icons • independently framed',font=small,fill='#b5c8cd')
for i,name in enumerate(names):
 x=i*256;d.text((x+18,90),name.upper(),font=font,fill='#f4e1bb');im=Image.open(p/'icons'/(name+'.png')).convert('RGBA');out.paste(im,(x,125),im);stock=Image.open(p/(name+'_Display.png')).convert('RGBA').resize((256,256),Image.Resampling.LANCZOS);out.paste(stock,(x,415),stock)
out.save(p/'food-review.png');out.resize((896,348),Image.Resampling.LANCZOS).save(p/'food-review-small.png')
# Actual 48px icon test on dark and light surfaces.
a=Image.new('RGB',(7*112,180),'#23343c');ad=ImageDraw.Draw(a);ad.rectangle((0,90,784,180),fill='#e2d7bf')
for i,name in enumerate(names):
 icon=Image.open(p/'icons'/(name+'.png')).convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
 for y in [12,102]:a.paste(icon,(i*112+32,y),icon)
 ad.text((i*112+5,63),name,font=small,fill='#e2d7bf');ad.text((i*112+5,153),name,font=small,fill='#23343c')
a.save(p/'icons-48px-check.png')
