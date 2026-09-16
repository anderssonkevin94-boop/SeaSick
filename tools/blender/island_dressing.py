"""Separate resource instances for the approved island study; no Unity export."""
import bpy
import random
import math
import json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
SOURCE=ROOT/'tools/blender/source'
OUT=ROOT/'docs/art-direction'
previous=bpy.context.window.scene
old=bpy.data.scenes.get('Island_Dressed_Study')
if old:
    for ob in list(old.objects): bpy.data.objects.remove(ob,do_unlink=True)
    bpy.data.scenes.remove(old)
with bpy.data.libraries.load(str(SOURCE/'island-foreland-study.blend'),link=False) as (src,dst):
    dst.scenes=['Island_Foreland_Study']
scene=dst.scenes[0];scene.name='Island_Dressed_Study'
bpy.context.window.scene=scene
try:
    rng=random.Random(91627)
    terrain=[]
    for ob in scene.objects:
        if ob.type=='MESH' and not ob.name.startswith('Sea level'):
            verts=[ob.matrix_world@v.co for v in ob.data.vertices]
            polys=[tuple(p.vertices) for p in ob.data.polygons]
            terrain.append((ob,BVHTree.FromPolygons(verts,polys,all_triangles=False)))
    def ground(x,y):
        hits=[]
        for ob,bvh in terrain:
            co,no,idx,dist=bvh.ray_cast(Vector((x,y,180)),Vector((0,0,-1)),200)
            if co is not None:
                grass=ob.name.startswith('Flat grassy') or (ob.name.startswith('Mountain silhouette') and ob.data.polygons[idx].material_index==1)
                hits.append((co.z,no.z,grass))
        return max(hits,key=lambda h:h[0]) if hits else None
    names=['Broad','Broad_B','Broad_C','Broad_Young','Spruce','Spruce_B','Spruce_Young',
           'Scrub_0','Scrub_1','Scrub_2','Grass','Grass_B','Fern','Sticks','Driftwood']+['Boulder_'+str(i) for i in range(4)]
    with bpy.data.libraries.load(str(SOURCE/'storybook-islands.blend'),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n in names]
    templates={ob.name.split('.')[0]:ob for ob in dst.objects}
    # Foreground ground cover shares the meadow palette instead of dark cut-outs.
    for name in ['Grass','Grass_B','Fern']:
        ob=templates[name];ob.data=ob.data.copy()
        attr=ob.data.color_attributes.get('Color')
        for datum in attr.data:
            c=datum.color
            datum.color=(c[0]*.35+.24*.65,c[1]*.35+.38*.65,c[2]*.35+.065*.65,1)
    collections={}
    for kind in ['Trees','Understory','Shore stones']:
        coll=bpy.data.collections.new('Dressing — '+kind);scene.collection.children.link(coll);collections[kind]=coll
    counts={};occupied=[];placements=[]
    def place(name,x,y,scale,kind,grass_only=True,clearance=0):
        hit=ground(x,y)
        if not hit or (grass_only and (not hit[2] or hit[1]<.78)):return None
        z=hit[0]
        if clearance:
            for dx,dy in [(clearance,0),(-clearance,0),(0,clearance),(0,-clearance)]:
                h=ground(x+dx,y+dy)
                if not h or not h[2] or abs(h[0]-z)>1.4:return None
        ob=templates[name].copy();ob.data=templates[name].data
        collections[kind].objects.link(ob);ob.hide_render=False;ob.hide_set(False)
        ob.location=(x,y,z-.06);ob.rotation_euler=(0,0,rng.uniform(0,math.tau))
        ob.scale=(scale*rng.uniform(.92,1.07),scale*rng.uniform(.92,1.07),scale)
        ob.name='Resource_'+name+'_'+str(counts.get(name,0)+1).zfill(3)
        ob['asset_id']=name;ob['resource_type']='tree' if kind=='Trees' else 'stone' if kind=='Shore stones' else 'foliage'
        counts[name]=counts.get(name,0)+1;placements.append((ob.name,x,y,z))
        return ob
    # Unequal woodland masses frame open grass routes and the offset summit.
    groves=[(-67,47,20,22,23),(-40,74,27,18,29),(-3,99,29,18,34),
            (47,88,25,19,30),(72,49,19,24,22),(-56,-4,17,16,13),
            (49,6,14,12,8),(-25,58,16,10,11),(17,75,14,8,6)]
    for cx,cy,rx,ry,target in groves:
        accepted=0
        for attempt in range(target*35):
            if accepted>=target:break
            a=rng.uniform(0,math.tau);r=math.sqrt(rng.random())
            x=cx+math.cos(a)*rx*r;y=cy+math.sin(a)*ry*r
            if any((x-px)**2+(y-py)**2<spacing**2 for px,py,spacing in occupied):continue
            name=rng.choices(['Broad','Broad_B','Broad_C','Broad_Young','Spruce','Spruce_B','Spruce_Young'],[25,17,15,12,15,8,8])[0]
            size=rng.uniform(.65,1.15)
            if place(name,x,y,size,'Trees',clearance=1.2):
                occupied.append((x,y,4.5*size));accepted+=1
    # Deliberately sparse crest accents keep the mountain's outline legible.
    for x,y,name,size in [(12,69,'Broad_C',.63),(22,74,'Broad_Young',.65),(8,73,'Spruce_Young',.65)]:
        if place(name,x,y,size,'Trees',clearance=.8):occupied.append((x,y,4))
    # A few asymmetric arrangements at woodland edges: one bush anchors a
    # pocket, with smaller tufts, a fern and occasional half-buried stone.
    # No independent confetti around every trunk; broad grass stays quiet.
    for index,(x,y,spacing) in enumerate(occupied):
        if index%3:continue
        a=rng.uniform(0,math.tau);r=rng.uniform(2.8,4.7)
        cx=x+math.cos(a)*r;cy=y+math.sin(a)*r
        bush=place('Scrub_'+str((index//3)%3),cx,cy,rng.uniform(.85,1.25),'Understory',clearance=.6)
        if not bush:continue
        for dx,dy,name,size in [(1.1,.25,'Grass',.60),(1.65,.8,'Grass_B',.45),(-.6,.85,'Fern',.65)]:
            px=cx+dx*math.cos(a)-dy*math.sin(a);py=cy+dx*math.sin(a)+dy*math.cos(a)
            place(name,px,py,size,'Understory')
        if index%9==0:
            stone=place('Boulder_'+str(index%4),cx-1.3*math.cos(a),cy-1.3*math.sin(a),.8,'Shore stones')
            if stone:stone.location.z-=.18
            place('Sticks',cx+.8,cy-1.1,.7,'Understory')
    # Deliberate pockets along the low cliff feet; open central approach.
    for x,y in [(-55,20),(-45,29),(45,28),(64,32),(-74,18)]:
        for dx,dy,name,size in [(0,0,'Scrub_1',1.2),(1.8,.5,'Scrub_2',.65),(-1.2,-.4,'Grass',.7),(.8,-1,'Fern',.6)]:
            place(name,x+dx,y+dy,size,'Understory',clearance=.5)
    # Small, asymmetrical shoreline families, never an armour of repeated cliffs.
    for cx,cy in [(-105,-25),(-82,-69),(98,-30),(99,39),(-82,86),(61,106)]:
        for j in range(5):
            x=cx+rng.uniform(-6,6);y=cy+rng.uniform(-6,6)
            place('Boulder_'+str(rng.randrange(4)),x,y,3.0 if j==0 else rng.uniform(.55,1.7),'Shore stones',False)
    for x,y in [(-84,-56),(86,-62),(-98,10)]:place('Driftwood',x,y,1.1,'Understory',False)
    for ob in templates.values():bpy.data.objects.remove(ob,do_unlink=True)
    camera=scene.camera
    views=[('overview',(-180,-270,225),(0,10,17),325),('harbour',(20,-320,60),(0,32,21),300),('plan',(0,10,400),(0,10,0),355)]
    scene.cycles.samples=32
    for label,position,target,scale in views:
        camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=scale;scene.render.filepath=str(OUT/f'island-dressed-{label}.png')
        bpy.ops.render.render(write_still=True)
    camera.location=views[0][1];camera.rotation_euler=(Vector(views[0][2])-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=325
    bpy.data.libraries.write(str(SOURCE/'island-dressed-study.blend'),{scene})
    (OUT/'island-dressing-validation.json').write_text(json.dumps({'counts':counts,'individual_objects':len(placements),'seed':91627,'unity_exported':False},indent=2)+'\n')
    print(json.dumps(counts));print('Saved dressed Blender study. Unity unchanged.')
finally:
    bpy.context.window.scene=previous
