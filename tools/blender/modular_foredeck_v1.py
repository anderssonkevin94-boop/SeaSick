"""Offline bow-local raised foredeck, with removable actual-cannon fit previews."""
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_hull_family_v3 as narrow
import modular_hull_wide_v1 as wide

MODE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'narrow'
assert MODE in ['narrow','wide']
revision=wide if MODE=='wide' else narrow
revision.setup()
family=revision.family
ship=revision.ship
ss=family.ss
family.configure_geometry()
SOURCE=family.OUT
OUT=HERE.parents[1]/'art-staging/modular-foredeck-v1'/MODE
OUT.mkdir(parents=True,exist_ok=True)
HEIGHT=4.20


def point(x,y,z):
    return (-y,z,x)


def box(b,x0,x1,y0,y1,z0,z1,color):
    b.box(-y1,-y0,z0,z1,x0,x1,color)


def tube(b,pts,width,height,color):
    ship.tube(b,[point(*p) for p in pts],width,height,color)


def slab(b,outline,bottom,top,color):
    ship.cap(b,[point(x,y,top) for x,y in outline],color,(0,1,0))
    ship.cap(b,[point(x,y,bottom) for x,y in outline],color,(0,-1,0))
    for i,(x,y) in enumerate(outline):
        xx,yy=outline[(i+1)%len(outline)]
        b.face([point(x,y,bottom),point(xx,yy,bottom),point(xx,yy,top),point(x,y,top)],
               'rim',hint=(xx-x,0,yy-y))


def hull_point(original):
    t=max(0,min(1,(original-5)/(ship.ZF-5)))
    weight=t*t*(3-2*t)
    z=ship.deck(original)
    return (original-family.CUT_B+weight*(.35+.55*(z-1)),
            ship.interp(original,ship.BEAM)*(1-.10*weight)-.28,z)


SECTIONS=[hull_point(x) for x in [8.3,9.8,11.2,12.0]]
START=SECTIONS[0][0]
END=SECTIONS[-1][0]


def width(x):
    for (a,w,_),(b,v,_) in zip(SECTIONS,SECTIONS[1:]):
        if x<=b: return w+(v-w)*(x-a)/(b-a)
    return SECTIONS[-1][1]


def base_height(x):
    stations=[hull_point(z) for z in ship.ST if z>=family.CUT_B]
    for (a,_,z),(b,_,zz) in zip(stations,stations[1:]):
        if x<=b: return z+(zz-z)*(x-a)/(b-a)
    return stations[-1][2]


def make_parts(root):
    deck=ss.Builder(); rails=ss.Builder(); frame=ss.Builder(); steps=ss.Builder(); seams=ss.Builder()
    outline=[(x,-w) for x,w,_ in SECTIONS]+[(x,w) for x,w,_ in reversed(SECTIONS)]
    slab(deck,outline,HEIGHT-.22,HEIGHT,'wood')
    # Seams share the original plank pitch and end at the tapered deck boundary.
    for n in range(-int(width(START)/.70),int(width(START)/.70)+1):
        y=n*.70
        if abs(y)+.10>width(START): continue
        end=END
        for (a,w,_),(b,v,_) in zip(SECTIONS,SECTIONS[1:]):
            if v<abs(y)+.10<=w:
                end=a+(b-a)*(w-abs(y)-.10)/(w-v)
                break
        if end>START+.12:
            box(seams,START+.06,end,y-.008,y+.008,HEIGHT+.005,HEIGHT+.015,'seam')
    gun_x=START+.96
    gap=(START+.22,START+1.73)
    rail_z=HEIGHT+.73
    for sign in [-1,1]:
        # Aft guard has a true central stair opening, not a rail across access.
        tube(rails,[(START,sign*.81,rail_z),(START,sign*width(START),rail_z),
                    (gap[0],sign*width(gap[0]),rail_z)],.25,.23,'teal')
        for x in [gap[0],gap[1],SECTIONS[2][0],END]:
            y=sign*width(x)
            box(frame,x-.075,x+.075,y-.075,y+.075,HEIGHT,rail_z-.10,'wood')
        for y in [sign*.81,sign*width(START)]:
            box(frame,START-.09,START+.09,y-.09,y+.09,HEIGHT,rail_z-.1,'wood')
            box(frame,START-.13,START+.13,y-.13,y+.13,rail_z-.10,rail_z+.05,'iron')
        # Separate short uprights support the deck inside the original hull rail.
        for x in [START+.20,SECTIONS[1][0],SECTIONS[2][0]]:
            y=sign*max(.25,width(x)-.20)
            box(frame,x-.12,x+.12,y-.12,y+.12,base_height(x),HEIGHT-.22,'wood')
        x=START+.20
        y=sign*(width(x)-.20)
        # Compact knee under the aft cross-beam, joined to the upright.
        corners=[(x+.12,HEIGHT-.80),(x+.12,HEIGHT-.23),(x+.75,HEIGHT-.23)]
        a=[point(xx,y-.09,z) for xx,z in corners]
        b=[point(xx,y+.09,z) for xx,z in corners]
        frame.solid([a,b]+[[a[i],a[(i+1)%3],b[(i+1)%3],b[i]] for i in range(3)],'wood2')
    xs=[gap[1]]+[x for x,_,_ in SECTIONS if gap[1]<x<END]+[END]
    tube(rails,[(x,-width(x),rail_z) for x in xs]+
                [(x,width(x),rail_z) for x in reversed(xs)],.25,.23,'teal')
    for x in [START+.16,SECTIONS[1][0]]:
        box(frame,x-.13,x+.13,-width(x)+.08,width(x)-.08,HEIGHT-.42,HEIGHT-.22,'wood2')
    bottom_x=START-2.88
    bottom_z=base_height(bottom_x)+.025
    rise=(HEIGHT-bottom_z)/8
    for i in range(8):
        x=bottom_x+i*.36
        z=bottom_z+(i+1)*rise
        box(steps,x,x+.36,-.66,.66,z-.13,z,'wood')
    for sign in [-1,1]:
        tube(frame,[(bottom_x,sign*.70,bottom_z+.04),(START,sign*.70,HEIGHT-.10)],.15,.22,'wood2')
        tube(rails,[(bottom_x,sign*.79,bottom_z+.78),(START,sign*.79,rail_z)],.13,.13,'teal')
        box(frame,bottom_x-.07,bottom_x+.07,sign*.79-.07,sign*.79+.07,
            bottom_z,bottom_z+.78,'wood')
    parts={}
    for name,b in [('Deck',deck),('Rails',rails),('Frame',frame),('Stairs',steps),('Seams',seams)]:
        o=ship.mesh('Foredeck_'+MODE+'__'+name,b)
        o.parent=root
        parts[name]=o
    sockets=[]
    for sign in [-1,1]:
        location=(gun_x,sign*(width(gun_x)-.66),HEIGHT)
        o=family.empty('Foredeck_Cannon_'+('Port' if sign>0 else 'Starboard'),location,root)
        o.rotation_euler.z=math.pi if sign>0 else 0
        o['status']='provisional static cannon fit; no recoil, aiming or crew simulation'
        sockets.append(o)
    family.empty('Foredeck_StairBottom',(bottom_x,0,bottom_z),root)
    family.empty('Foredeck_StairTop',(START,0,HEIGHT),root)
    return parts,sockets


def cannon_previews(sockets):
    with bpy.data.libraries.load(str(HERE/'source/cannon-astra-v1.blend'),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n not in ['Review','Camera','Light']]
    templates=[o for o in dst.objects if o is not None]
    for o in templates: bpy.context.scene.collection.objects.link(o)
    cannon=next(o for o in templates if o.name.startswith('Cannon_Root'))
    meshes=[o for o in templates if o.type=='MESH']
    bpy.context.view_layer.update()
    local={o:o.matrix_world.copy() for o in meshes}
    output=[]
    for socket in sockets:
        for o in meshes:
            copy=o.copy()
            copy.name='FIT_PREVIEW_'+socket.name+'__'+o.name
            bpy.context.scene.collection.objects.link(copy)
            copy.parent=socket
            copy.matrix_parent_inverse=Matrix.Identity(4)
            copy.matrix_basis=local[o]
            output.append(copy)
    for o in templates: bpy.data.objects.remove(o,do_unlink=True)
    return output


def main():
    report={'family':MODE,'assemblies':{},'parts':{}}
    for assembly in ['short','long']:
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE/assembly/'ship.blend'))
        bow=bpy.data.objects[family.BOW]
        root=family.empty('Foredeck_'+MODE,(0,0,0),bow)
        root['interface']=family.INTERFACE_STANDARD
        root['pivot']='same as bow module; local zero at aft interface and ship datum'
        parts,sockets=make_parts(root)
        bpy.context.view_layer.update()
        for name,o in parts.items():
            check=ship.check(o)
            assert all(check[k]==0 for k in ['boundary_edges','overconnected_edges','degenerate_faces']),(name,check)
            report['parts'][name]=check
            if assembly=='short':
                ss.export_fbx(o,str(OUT/'models'/(name+'.fbx')))
        chimney=bpy.data.objects['Chimney']
        stair_tree=family.wheel_study.bvh(parts['Stairs'])
        assert not stair_tree.overlap(family.wheel_study.bvh(chimney)),'Stair/chimney intersection'
        bounds=[bow.matrix_world@Vector((START-2.88,0,base_height(START-2.88))),bow.matrix_world@Vector((START,0,HEIGHT))]
        report['assemblies'][assembly]={'stair_chimney_intersections':0,'stair_endpoints_world':[list(v) for v in bounds]}
        previews=cannon_previews(sockets)
        bpy.context.view_layer.update()
        # Test rail and frame surfaces, excluding intentional wheels-on-deck contact.
        hits=[]
        for o in previews:
            tree=family.wheel_study.bvh(o)
            for key in ['Rails','Frame','Stairs']:
                if tree.overlap(family.wheel_study.bvh(parts[key])): hits.append([o.name,key])
        report['assemblies'][assembly]['cannon_surface_intersections']=hits
        assert not hits,hits
        total=20.28+(6 if assembly=='long' else 0)
        middle=total/2
        folder=OUT/assembly
        folder.mkdir(exist_ok=True)
        family.render(folder/'hero.png',(middle-26,-34,28),(middle,0,1.7),31,(1600,1150))
        family.render(folder/'top.png',(middle,0,50),(middle,0,0),29,(1650,1100))
        family.render(folder/'side.png',(middle,-50,2),(middle,0,2),29,(1650,800))
        family.render(folder/'foredeck-detail.png',(bow.location.x+START-8,-12,14),
                      (bow.location.x+START+1,0,3),15,(1400,1100))
        family.save(folder/'fit-review.blend')
        for o in previews: o.hide_render=True; o.hide_set(True)
        family.render(folder/'clean.png',(middle-26,-34,28),(middle,0,1.7),31,(1600,1150))
        family.save(folder/'ship.blend')
        if assembly=='short':
            manifest={'id':'Foredeck_'+MODE+'_v1','interface':family.INTERFACE_STANDARD,
                'units':'unchanged V8 authoring units, not calibrated game metres',
                'coordinates':'Blender +X bow, +Y port, +Z up; game position maps to (-y,z,x)',
                'parent':'matching bow module; identity local transform',
                'deck_height':HEIGHT,'deck_outline':[[x,-w] for x,w,_ in SECTIONS]+[[x,w] for x,w,_ in reversed(SECTIONS)],
                'stairs':{'clear_tread_width':1.32,'run':2.88,'treads':8,'bottom_x':START-2.88,'top_x':START},
                'files':{name:'models/'+name+'.fbx' for name in parts},
                'sockets':{s.name:{'location':list(s.location),'rotation_euler':list(s.rotation_euler)} for s in root.children if s.type=='EMPTY'},
                'preview_cannons':'existing asset, unscaled; preview only, excluded from foredeck FBXs',
                'limitations':'static fit prototype; no recoil, crew clearance certification, colliders, navigation, balance, Unity import or iOS testing'}
            (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
    report['addon_triangles']=sum(v['triangles'] for v in report['parts'].values())
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print('FOREDECK PASS',MODE,report['addon_triangles'],flush=True)


if __name__=='__main__': main()
