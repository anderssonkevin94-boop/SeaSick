"""Low-poly worn-earth road surfaces and separate unlit torch holders."""
import sys, math, json
from pathlib import Path
import bpy, bmesh
from mathutils import Vector
HERE=Path(__file__).resolve().parent;sys.path.insert(0,str(HERE))
import steamer as ss
from staged_building_kit import Kit
OUT=HERE.parents[1]/'art-staging/roads-astra-lvl1-v1';OUT.mkdir(parents=True,exist_ok=True)
ss.clear_scene()
COL={'earth':'#A38B65','worn':'#B09A76','shoulder':'#968763','grass':'#889E64',
     'wood':'#795940','edge':'#AC8055','iron':'#414E52','rope':'#BCAA80','char':'#423F36'}
parts={};contract={};report={}
def finish(name,k,sockets,open_surface=False):
    mods=k.join_modules();markers=[k.marker(name+'__'+n,p,'local snap/VFX reference') for n,p in sockets.items()]
    tris=0;boundary=0
    for ob in mods.values():
        bm=bmesh.new();bm.from_mesh(ob.data)
        tris+=sum(len(f.verts)-2 for f in bm.faces);boundary+=sum(e.is_boundary for e in bm.edges)
        assert not any(f.calc_area()<1e-10 for f in bm.faces),name
        assert not any(len(e.link_faces)>2 for e in bm.edges),name
        if not open_surface:assert not any(not e.is_manifold for e in bm.edges),name
        bm.free()
    report[name]={'triangles':tris,'intentional_boundary_edges':boundary,'flat_shaded':True}
    contract[name]={'file':name+'.fbx','sockets_blender':sockets,'width_including_feather_m':2.16 if open_surface else None}
    contract[name]['marker_names']={n:name+'__'+n for n in sockets}
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [k.root]+list(mods.values())+markers:ob.select_set(True)
    bpy.context.view_layer.objects.active=k.root
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,use_triangles=True,colors_type='LINEAR',mesh_smooth_type='FACE',use_custom_props=True)
    parts[name]=(k.root,mods,markers)
    return k.root
def shade(ob,alpha,tones):
    attr=ob.data.color_attributes['Col']
    for p in ob.data.polygons:
        for loop in p.loop_indices:
            index=ob.data.loops[loop].vertex_index
            attr.data[loop].color=(*ss.srgb_to_linear(ss._hex(tones[index])),alpha[index])
def road(name,length=4,angle=0,variant=0):
    k=Kit(name,COL);k.group='Surface';k.root['terrain_conform']=True
    n=8 if angle else int(length*2);widths=[-1.08,-.88,-.55,0,.55,.88,1.08]
    verts=[];alphas=[];tones=[];radius=3.6
    for j in range(n+1):
        t=j/n;theta=angle*t
        cx,cy=(radius*math.sin(theta),radius*(1-math.cos(theta))) if angle else (length*t,0)
        for i,w in enumerate(widths):
            irregular=.13*math.sin(t*math.pi)**2*math.sin(j*2.1+variant*1.7)*(abs(w)/1.08)**2
            lateral=w+irregular
            verts.append((cx-math.sin(theta)*lateral,cy+math.cos(theta)*lateral,.012))
            alphas.append(0 if i in [0,6] else 1)
            tones.append(COL['grass' if i in [0,6] else 'shoulder' if i in [1,5] else 'worn' if i==3 else 'earth'])
    faces=[]
    for j in range(n):
        for i in range(6):
            a=j*7+i;faces.append((a,a+7,a+8,a+1))
    ob=k.mesh('Ground_Ribbon',verts,faces,'earth');shade(ob,alphas,tones)
    # Broad irregular compacted patches, without extra overlays or tiny geometry.
    for face in ob.data.polygons:
        for li in face.loop_indices:
            vi=ob.data.loops[li].vertex_index;row=vi//7;band=vi%7
            if row not in [0,n] and band in [1,2,3,4,5]:
                tint=1+.09*math.sin(row*1.1+band*2.1+variant*2.1)*math.sin(row*math.pi/n)
                rgba=ob.data.color_attributes['Col'].data[li].color
                ob.data.color_attributes['Col'].data[li].color=(*(min(1,c*tint) for c in rgba[:3]),rgba[3])
    end=(radius*math.sin(angle),radius*(1-math.cos(angle)),0) if angle else (length,0,0)
    root=finish(name,k,{'Start':(0,0,0),'End':end},True)
    contract[name]['end_heading_degrees']=math.degrees(angle)
    return root
for v in range(3):road('Road_Straight_4m_'+chr(65+v),variant=v)
road('Road_Straight_2m',2);road('Road_Curve_45',angle=math.pi/4);road('Road_Curve_90',angle=math.pi/2)

def junction():
    name='Road_T_Junction';k=Kit(name,COL);k.group='Surface'
    xs=[-2.16,-1.08,-.88,-.55,0,.55,.88,1.08,2.16]
    ys=[-1.08,-.88,-.55,0,.55,.88,1.08,2.16,3.24]
    verts=[(x+2.16,y,.012) for y in ys for x in xs];faces=[];n=len(xs)
    for j in range(len(ys)-1):
        for i in range(n-1):
            if (ys[j]+ys[j+1])/2>1.08 and abs((xs[i]+xs[i+1])/2)>1.08:continue
            a=j*n+i;faces.append((a,a+1,a+n+1,a+n))
    ob=k.mesh('Joined_Earth',verts,faces,'earth')
    edges={}
    for f in faces:
        for a,b in zip(f,f[1:]+f[:1]):
            key=tuple(sorted((a,b)));edges[key]=edges.get(key,0)+1
    borders=[]
    for (a,b),count in edges.items():
        if count!=1:continue
        p,q=Vector(verts[a]),Vector(verts[b])
        if abs(p.x-q.x)<1e-6 and (abs(p.x)<1e-6 or abs(p.x-4.32)<1e-6):continue
        if abs(p.y-3.24)<1e-6 and abs(q.y-3.24)<1e-6:continue
        borders.append((p,q))
    alpha=[];tones=[]
    for p in map(Vector,verts):
        dist=min((p-(a+(b-a)*max(0,min(1,(p-a).dot(b-a)/(b-a).length_squared)))).length for a,b in borders)
        alpha.append(min(1,dist/.2))
        tones.append(COL['grass' if dist<.01 else 'shoulder' if dist<.25 else 'earth' if dist<.60 else 'worn'])
    shade(ob,alpha,tones)
    finish(name,k,{'Start':(0,0,0),'End':(4.32,0,0),'Branch':(2.16,3.24,0)},True)
junction()

def endcap():
    k=Kit('Road_End',COL);k.group='Surface'
    widths=[-1.08,-.88,-.55,0,.55,.88,1.08];verts=[];alpha=[];tones=[]
    for j,shrink in enumerate([1,.97,.78,.35,.05]):
        for i,w in enumerate(widths):
            verts.append((j*.4,w*shrink,.012));alpha.append((0 if i in [0,6] else 1)*(1-j/4)**.45)
            tones.append(COL['grass' if i in [0,6] or j==4 else 'shoulder' if i in [1,5] else 'worn' if i==3 else 'earth'])
    faces=[(j*7+i,j*7+i+7,j*7+i+8,j*7+i+1) for j in range(4) for i in range(6)]
    ob=k.mesh('Fading_Footpath',verts,faces,'earth');shade(ob,alpha,tones)
    finish('Road_End',k,{'Start':(0,0,0)},True)
endcap()

def torch(name,lean):
    k=Kit(name,COL);k.group='Holder'
    k.beam('Hand_Hewn_Post',(0,0,0),(lean,.025,1.45),.17,.19,'wood')
    k.box('Post_Foot',(0,0,.06),(.22,.23,.12),'edge',.02)
    for z in [.29,1.17]:k.box('Binding',(lean*z/1.45,.025*z/1.45,z),(.19,.21,.06),'rope',.008)
    k.beam('Fork_Left',(lean,.025,1.22),(lean-.16,.025,1.57),.09,.10,'edge')
    k.beam('Fork_Right',(lean,.025,1.22),(lean+.16,.025,1.57),.09,.10,'edge')
    k.lathe('Iron_Cup',(lean,.025,1.38),[(.065,0),(.14,.15),(.14,.19),(.105,.19),(.10,.14),(.04,.035)],'iron',8)
    k.group='Fuel_Insert'
    for x,y in [(-.045,-.035),(.04,-.03),(0,.045)]:
        k.rod('Charred_Torch_Wood',(lean+x,.025+y,1.48),(lean+x*1.4,.025+y*1.4,1.69),.034,'char',5)
    finish(name,k,{'Flame_Anchor':(lean,.025,1.70),'Ground_Anchor':(0,0,0)})
torch('Torch_Post_A',.06);torch('Torch_Post_B',-.10)

(OUT/'validation.json').write_text(json.dumps(report,indent=2))
(OUT/'module-contract.json').write_text(json.dumps({'axes':'Blender +X along road, +Z up; FBX -Z forward/Y up','modules':contract,'road_surface':'open terrain-conforming ribbon; Col.a feather mask, Col.rgb dirt/edge colours','torch_spacing_suggestion_m':[8,12],'effects':'No flame, smoke, light or flicker supplied.'},indent=2))
scene=bpy.context.scene;ss.setup_render(scene);scene.view_settings.exposure=1.05
scene.world.color=ss.srgb_to_linear(ss._hex('#ADB8A2'))
cam=bpy.data.objects.new('Road_Review',bpy.data.cameras.new('Road_Review'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
def visible(root,on):
    for ob in [root]+list(root.children_recursive):ob.hide_render=not on;ob.hide_set(not on)
def render(name,eye,target,scale,res=(1400,1000)):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.resolution_x,scene.render.resolution_y=res;scene.render.resolution_percentage=100;scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
# Layout board: source roots remain easy to select, with exports at local origin.
layout={'Road_Straight_4m_A':(-5,4,0),'Road_Straight_4m_B':(-5,1,0),'Road_Straight_4m_C':(-5,-2,0),
        'Road_Straight_2m':(-5,-5,0),'Road_Curve_45':(1,4,0),'Road_Curve_90':(1,-1,0),
        'Road_T_Junction':(6,-3,0),'Torch_Post_A':(4,5,0),'Torch_Post_B':(5,5,0),'Road_End':(-1,-5,0)}
for name,(root,_,_) in parts.items():root.location=layout[name]
render('kit-overview',(14,-19,23),(2,0,0),22)
for root,_,_ in parts.values():visible(root,False)
review=bpy.data.collections.new('Review_Only_Not_Exported');scene.collection.children.link(review)
def instance(name,p,angle=0):
    root,mods,_=parts[name]
    parent=bpy.data.objects.new('Review_'+name,None);review.objects.link(parent);parent.location=p;parent.rotation_euler.z=angle
    for ob in mods.values():
        copy=ob.copy();copy.data=ob.data;review.objects.link(copy);copy.parent=parent;copy.hide_render=False;copy.hide_set(False)
    return parent
instance('Road_Straight_4m_A',(-5,-2,0));instance('Road_Straight_4m_B',(-1,-2,0))
instance('Road_Curve_90',(3,-2,0));instance('Road_Straight_4m_C',(6.6,1.6,0),math.pi/2)
instance('Road_End',(-5,-2,0),math.pi);instance('Road_End',(6.6,5.6,0),math.pi/2)
instance('Torch_Post_A',(-3,-.65,0));instance('Torch_Post_B',(7.95,3.5,0))
# Preview ground is presentation only. Roads are never exported with a grass slab.
ground=Kit('Review_Ground',COL);ground.group='Review';ob=ground.mesh('Meadow',[(-15,-12,-.015),(15,-12,-.015),(15,15,-.015),(-15,15,-.015)],[(0,1,2,3)],'grass')
render('road-in-context',(15,-20,23),(1,1,0),20)
render('road-close',(3,-10,9),(-2,-2,0),8)
visible(ground.root,False)
for ob in review.objects:ob.hide_render=True;ob.hide_set(True)
for root,_,_ in parts.values():visible(root,True)
render('kit-overview',(14,-19,23),(2,0,0),22)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            s=area.spaces.active;s.shading.color_type='VERTEX';s.region_3d.view_location=(2,0,0);s.region_3d.view_distance=23;s.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'roads-lvl1.blend'))
print('ROADS PASS',json.dumps(report))
