from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
import json
p=Path(__file__).resolve().parent;im=Image.new('RGB',(1440,1080),'#203139');d=ImageDraw.Draw(im);font=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',25);small=ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc',18)
d.text((24,18),'WORKER TOOLS • V1 REVIEW',font=font,fill='#f4e0b9');d.text((24,55),'Individual framing for inspection — not shown at equal scale',font=small,fill='#b4c5c8')
meta=json.loads((p/'manifest.json').read_text())
for i,(name,label) in enumerate([('Axe','AXE'),('Hammer','HAMMER'),('Saw','HANDSAW'),('Hoe','HOE'),('StirPaddle','STIRRING PADDLE'),('SpearStone','STONE SPEAR'),('SpearIron','IRON SPEAR')]):
 x=(i%4)*360;y=100+(i//4)*490;src=Image.open(p/(name+'.png')).convert('RGBA');im.paste(src,(x,y+35),src);d.text((x+18,y),label,font=font,fill='#f4e0b9');a=next(a for a in meta if a['name']==name);d.text((x+18,y+455),f"{a['size_blender_m'][2]:.2f} m · {a['triangles']} triangles",font=small,fill='#b4c5c8')
d.text((1098,662),'Grip-centred pivots\nExisting reach points\nGameColor palette\nOne material each\nNo textures',font=small,fill='#b4c5c8',spacing=14)
im.save(p/'worker-tools-review.png');im.resize((720,540),Image.Resampling.LANCZOS).save(p/'worker-tools-small.png')
