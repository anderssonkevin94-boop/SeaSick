from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
p=Path(__file__).resolve().parent;out=Image.new('RGB',(1600,1020),'#19282d');d=ImageDraw.Draw(out)
f=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',25);small=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',18)
d.text((32,16),'RESOURCE KIT • V1 REVIEW',font=f,fill='#f2e4c6')
for col,name in enumerate(['Timber','Boards','Stone','Ore','Brick']):
 d.text((col*320+20,63),name.upper(),font=f,fill='#f2e4c6')
 for row,kind in enumerate(['Stack','Unit','Carry']):
  out.paste(Image.open(p/(name+'_'+kind+'.png')),(col*320,100+row*300));d.text((col*320+15,105+row*300),kind,font=small,fill='#f2e4c6')
out.save(p/'resource-kit-review.png');out.resize((800,510),Image.Resampling.LANCZOS).save(p/'resource-kit-small.png')
