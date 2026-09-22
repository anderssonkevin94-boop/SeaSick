"""Package the rendered fleet as a browsable gallery and overview sheets."""
import json, html
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'docs/art-direction/fleet-v3'
ships=json.loads((OUT/'manifest.json').read_text())
font='/System/Library/Fonts/Supplemental/Arial.ttf'
F=lambda n:ImageFont.truetype(font,n)
for start,end,name in [(0,20,'fleet-overview'),(0,5,'01-05'),(5,10,'06-10'),(10,15,'11-15'),(15,20,'16-20')]:
 subset=ships[start:end];cols=4 if end-start==20 else 2;cw=600;ch=520;nr=(len(subset)+cols-1)//cols
 sheet=Image.new('RGB',(cols*cw,nr*ch+100),'#102c35');d=ImageDraw.Draw(sheet)
 d.text((26,25),'SEASICK / THE PLAYER FLEET',font=F(30),fill='#f2d6a3')
 d.text((26,65),'Upgrade studies · individual framing, not a shared scale',font=F(19),fill='#aac2c6')
 for j,s in enumerate(subset):
  x=(j%cols)*cw;y=100+(j//cols)*ch;im=Image.open(OUT/s['image']).convert('RGB');im.thumbnail((cw,ch-70));sheet.paste(im,(x,y))
  d.text((x+18,y+452),f"{s['stage']:02d}  {s['name']}",font=F(23),fill='#f2d6a3')
  d.text((x+18,y+483),f"{s['length']:g} × {s['beam']:g} m   |   {s['cannons']} cannons",font=F(18),fill='#aac2c6')
 sheet.save(OUT/f'{name}.jpg',quality=94)
# Match the three images rendered with the same orthographic scale.
if all((OUT/f'{k:02d}-same-scale.png').exists() for k in [12,13,14]):
 comparison=Image.new('RGB',(2880,840),'#102c35');cd=ImageDraw.Draw(comparison)
 for j,k in enumerate([12,13,14]):
  comparison.paste(Image.open(OUT/f'{k:02d}-same-scale.png').convert('RGB'),(j*960,60));spec=ships[k-1]
  cd.text((j*960+24,782),f"{k:02d}  {spec['name']}  /  {spec['cannons']} cannons",font=F(28),fill='#f2d6a3')
 cd.text((24,15),'SEASICK / SHIPS 12 → 13 → 14 / IDENTICAL IMAGE SCALE',font=F(28),fill='#f2d6a3')
 comparison.save(OUT/'12-14-comparison.jpg',quality=95)
cards=[]
for s in ships:
 detail=f" · <a href='{s['stage']:02d}-stern.png'>Stern close-up</a>" if (OUT/f"{s['stage']:02d}-stern.png").exists() else ''
 cards.append(f'''<article id="ship-{s['stage']}"><a href="{s['image']}" target="_blank"><img loading="lazy" src="{s['image']}" alt="{s['name']}"></a><div><span class="number">{s['stage']:02d}</span><h2>{s['name']}</h2><p class="stats">{s['length']:g} × {s['beam']:g} m · {s['cannons']} cannons · {s['masts']} mast{'s' if s['masts']>1 else ''}</p><p>{html.escape(s['feature'])}</p><a href="{s['image']}" download>Download screenshot</a> · <a href="../../../tools/blender/source/fleet-v3/{s['model']}">Blender model</a>{detail}</div></article>''')
(OUT/'index.html').write_text('''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SeaSick — Player Fleet</title><style>*{box-sizing:border-box}body{margin:0;background:#102c35;color:#e9eddf;font:17px/1.55 system-ui}header,main{max-width:1500px;margin:auto;padding:40px 28px}header{padding-bottom:12px}h1{font-size:48px;line-height:1.1;color:#f4d497;margin:8px 0 22px}header p{max-width:850px;color:#b6c9cc}small,.stats{color:#aac2c6}main{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:30px}article{background:#173b46;border:1px solid #36545a;border-radius:12px;overflow:hidden}article img{width:100%;display:block}article div{padding:24px}h2{margin:0;font-size:26px;color:#f4d497}.number{float:right;color:#779fa8;font-size:32px}.stats{margin:6px 0}a{color:#efc46f}article p{margin-bottom:12px}nav{display:flex;gap:20px;flex-wrap:wrap}footer{padding:30px;text-align:center;color:#aac2c6}@media(max-width:800px){main{grid-template-columns:1fr}h1{font-size:36px}}@media print{main{display:block}article{break-inside:avoid;margin-bottom:20px}}</style><header><small>SEASICK / ART DIRECTION / FLEET V3</small><h1>Every upgrade earns its character.</h1><p>Twenty ship studies, from a working skiff to the 24-cannon Sea Crown. Revised with continuous hull-integrated sterns, complete rear railings, open helms and sweeping bows. Ships 16–20 now combine a raised enclosed battery with weather-deck cannons. Every ship from stage 4 is armed. Cannon totals never decrease, and ship 13 now matches the fleet construction and proportions.</p><nav><a href="fleet-overview.jpg">Fleet overview</a><a href="12-14-comparison.jpg">Ships 12–14 at the same scale</a><a href="#ship-1">Skiffs & launches</a><a href="#ship-7">Sloops & escorts</a><a href="#ship-13">Brigs</a><a href="#ship-16">Two-deckers</a></nav><p><small>Each image uses individual framing for inspection; image size does not indicate hull size. Dimensions are nominal hull sizes before the extended prow. These are art studies, not changes to gameplay balance.</small></p></header><main>'''+''.join(cards)+'''</main><footer>20 designs · 20 screenshots · 20 editable Blender scenes</footer></html>''')
(OUT/'README.md').write_text('''# SeaSick player fleet — design studies v3

Twenty upgrade stages following the compact 34 × 9.5 m, 24-cannon endgame direction. Open index.html for individual images and model downloads. fleet-overview.jpg shows the progression; 01.png through 20.png are full-size renders.

These are standalone exterior art studies. They do not update the current gameplay ladder or replace production assets. Guns shown are fitted concept counts, not crew-balanced gameplay loadouts. The revised enclosed gunports are cut through the hull shell, with timber reveals and open lids. Ships 16–20 place the enclosed battery halfway between the old rows and the second battery on the weather deck. The ships still require production topology, modular sockets, collision and gameplay integration. Ship 13 is rebuilt at 26 × 7.8 m nominal hull dimensions with 14 cannons, between the 12-cannon Guild escort and 16-cannon Long brig.

The v2 construction revision integrates the raised stern into the hull sides using shared mesh edges, adds complete rear railings, removes the stage 6 canopy, and increases the rake and upward sweep of the bow. Revision 3 arms every ship from stage 4 onward and rebuilds stage 13 in the same fleet geometry. Stages 1–3 and 16–20 retain the preceding approved revision. The 12–14 comparison uses one common image scale.

Every image has identical lighting and camera direction, but framing is adjusted per ship. Compare nominal hull dimensions rather than image pixel dimensions. Source scenes are in tools/blender/source/fleet-v3. The procedural generator is tools/blender/fleet_design_v3.py; run in a separate Blender process, not an existing authoring session.

| Stage | Ship | Hull length × beam | Cannons | Visible upgrade |
| --- | --- | --- | --- | --- |
'''+''.join(f"| {s['stage']:02d} | {s['name']} | {s['length']:g} × {s['beam']:g} m | {s['cannons']} | {s['feature']} |\n" for s in ships))
print('Packaged 20-ship gallery and five overview sheets.')
import zipfile
with zipfile.ZipFile(OUT/'ship-screenshots.zip','w',compression=zipfile.ZIP_DEFLATED,compresslevel=1) as archive:
 for s in ships:archive.write(OUT/s['image'],f"{s['stage']:02d}-{s['name'].lower().replace(' ','-')}.png")
 archive.write(OUT/'fleet-overview.jpg','fleet-overview.jpg')
 archive.write(OUT/'README.md','README.md')
 if (OUT/'12-14-comparison.jpg').exists():archive.write(OUT/'12-14-comparison.jpg','12-14-comparison.jpg')
 for detail in OUT.glob('*-stern.png'):archive.write(detail,detail.name)
