"""Ground-only colour study. Existing geometry, composition and lighting stay unchanged."""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector, noise

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'art-staging/forest-groundcover-mockup-v1'
OUT=ROOT/'art-staging/forest-ground-palette-v2'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'forest-groundcover-mockup-v1.blend'))
scene=bpy.context.scene
ground=bpy.data.objects['Preview terrain - not a game asset']
study=bpy.data.collections['Reference arrangement']
trees=[(ob.location.x,ob.location.y) for ob in study.objects if '_Wood' in ob.name]
rocks=[(ob.location.x,ob.location.y) for ob in study.objects if ob.name.startswith(('Boulder_Broad','Boulder_Long'))]
assert len(trees)==58, len(trees)
before=(len(ground.data.vertices),len(ground.data.polygons))

def col(h):
    def linear(c): return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    return tuple(linear(int(h[i:i+2],16)/255) for i in (1,3,5))
def mix(a,b,t):
    t=max(0,min(1,t)); return tuple(x*(1-t)+y*t for x,y in zip(a,b))
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a))); return t*t*(3-2*t)
def field(x,y,scale,seed):
    return noise.noise_vector(Vector((x*scale,y*scale,seed))).x

grass=col('#909C51'); dry=col('#B0B565'); fresh=col('#809B51')
moss=col('#4F7847'); earth=col('#9C8058'); trail=col('#B8A573')
colors=[]
for vertex in ground.data.vertices:
    x,y,z=vertex.co
    # Warped coordinates avoid circular tree halos and parallel noise bands.
    wx=x+field(x,y,.22,3)*2.7; wy=y+field(x,y,.22,7)*2.7
    broad=field(wx,wy,.075,11)
    detail=field(x,y,.31,17)
    canopy=sum(math.exp(-(((wx-tx)/3.0)**2+((wy-ty)/3.6)**2)) for tx,ty in trees)
    root=max((math.exp(-(((wx-tx)/2.0)**2+((wy-ty)/1.40)**2))
              *(.48+.38*math.sin(tx*.7+ty*.45)**2) for tx,ty in trees),default=0)
    stone=max((math.exp(-(((wx-rx-.45)/3.1)**2+((wy-ry+.3)/2.2)**2)) for rx,ry in rocks),default=0)
    c=mix(grass,dry,smooth(-.13,.16,broad)*(1-smooth(.45,1.5,canopy)))
    c=mix(c,fresh,smooth(.02,.4,detail)*.28)
    moss_mask=smooth(.28,.88,canopy+detail*.8+broad*.45)
    c=mix(c,moss,moss_mask*.82)
    c=mix(c,earth,smooth(.18,.63,max(root,stone*.9)+detail*.24)*.86)
    # Worn route breaks up at its edges; broad clearings stay grassy.
    center=2.6*math.sin(y*.16)+.035*y
    route=math.exp(-((x-center+field(x,y,.35,29)*.30)/1.25)**4)
    c=mix(c,trail,route*(.58+.18*smooth(-.3,.3,broad)))
    c=tuple(max(0,value+detail*.006) for value in c)
    colors.append(c+(1,))

# Shared vertex samples interpolate across the existing triangles: no checkerboard facets.
attr=ground.data.color_attributes['Col']
for poly in ground.data.polygons:
    for li in poly.loop_indices: attr.data[li].color=colors[ground.data.loops[li].vertex_index]
ground.data.update()
assert before==(len(ground.data.vertices),len(ground.data.polygons))
report={'changed':'ground corner colours only','terrain_vertices':before[0],
        'terrain_triangles':before[1],'added_geometry':0,'added_materials':0,
        'tree_count':len(trees),'unity_imported':False,
        'note':'Blender colour study, not a verified Unity terrain implementation or iPhone benchmark.'}
(OUT/'study-audit.json').write_text(json.dumps(report,indent=2))
cam=scene.camera
def render(name,eye,target,scale,res):
    cam.location=eye; cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale; scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.filepath=str(OUT/(name+'.png')); bpy.ops.render.render(write_still=True)

render('forest-overview',(27,-38,43),(0,8,1),57,(1800,1400))
render('forest-phone',(15,-28,36),(0,4,1),44,(1080,1600))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'forest-ground-palette-v2.blend'))
print('GROUND_PALETTE_COMPLETE',json.dumps(report))
