"""Replacement forecastle bow: continuous hull shell, no platform stilts."""
import json
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_foredeck_v1 as fore

family,ship,ss=fore.family,fore.ship,fore.ss
MODE=fore.MODE
OUT=HERE.parents[1]/'art-staging/modular-raised-bow-v1'/MODE
OUT.mkdir(parents=True,exist_ok=True)
CUT=fore.START
HEIGHT=fore.HEIGHT
TIP=fore.hull_point(ship.ZF)[0]
RAKE=(HEIGHT-ship.deck(ship.ZF))*.55


def rake(x):
    return RAKE*max(0,min(1,(x-CUT)/(TIP-CUT)))


def paint(bm,faces,color):
    linear=bm.loops.layers.float_color.get('Col')
    layer=linear or bm.loops.layers.color.get('Col')
    value=(*(ss.srgb_to_linear(ss.PAL[color]) if linear else ss.PAL[color]),1)
    for f in faces:
        for loop in f.loops: loop[layer]=value


def raised_shell(obj):
    bm=bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                          plane_co=(CUT,0,0),plane_no=(1,0,0),dist=1e-6)
    bm.normal_update()
    remove=[f for f in bm.faces if min(v.co.x for v in f.verts)>=CUT-1e-5
            and min(v.co.z for v in f.verts)>1.7 and f.normal.z>.90]
    assert remove,'No forward deck faces found'
    bmesh.ops.delete(bm,geom=remove,context='FACES')
    boundary=[e for e in bm.edges if e.is_boundary and min(v.co.x for v in e.verts)>=CUT-1e-5
              and min(v.co.z for v in e.verts)>1.7]
    assert boundary
    result=bmesh.ops.extrude_edge_only(bm,edges=boundary)
    upper=[v for v in result['geom'] if isinstance(v,bmesh.types.BMVert)]
    for v in upper:
        v.co.x+=rake(v.co.x)
        v.co.z=HEIGHT
    walls=[f for f in result['geom'] if isinstance(f,bmesh.types.BMFace)]
    paint(bm,walls,'wood')
    edges=[e for e in bm.edges if all(v in upper for v in e.verts)]
    cap=bmesh.ops.holes_fill(bm,edges=edges,sides=0)['faces']
    assert cap,'Raised deck did not close'
    paint(bm,cap,'deck')
    bmesh.ops.triangulate(bm,faces=cap)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(obj.data); bm.free(); obj.data.update()


def trim_forward(obj,top_only=False):
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                          plane_co=(CUT,0,0),plane_no=(1,0,0),dist=1e-6)
    remove=[f for f in bm.faces if min(v.co.x for v in f.verts)>=CUT-1e-5
            and (not top_only or min(v.co.z for v in f.verts)>1.72)]
    bmesh.ops.delete(bm,geom=remove,context='FACES')
    edges=[e for e in bm.edges if e.is_boundary and all(abs(v.co.x-CUT)<1e-4 for v in e.verts)]
    if edges: bmesh.ops.holes_fill(bm,edges=edges,sides=0)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(obj.data);bm.free();obj.data.update()


def guard_only(obj):
    bm=bmesh.new();bm.from_mesh(obj.data)
    todo=set(bm.verts)
    while todo:
        seed=todo.pop(); group={seed}; stack=[seed]
        while stack:
            v=stack.pop()
            for e in v.link_edges:
                w=e.other_vert(v)
                if w in todo: todo.remove(w);group.add(w);stack.append(w)
        if max(v.co.z for v in group)<HEIGHT+.01:
            bmesh.ops.delete(bm,geom=list(group),context='VERTS')
    bm.to_mesh(obj.data);bm.free();obj.data.update()


def build():
    bow=bpy.data.objects[family.BOW]
    shell=bpy.data.objects[family.BOW+'__Hull_Shell']
    original_loop=family.boundary_points(shell,0)
    raised_shell(shell)
    assert family.boundary_points(shell,0)==original_loop,'Aft hull interface changed'
    trim_forward(bpy.data.objects[family.BOW+'__Rails_Teal'])
    trim_forward(bpy.data.objects[family.BOW+'__Plank_Seams'],True)
    # Remove old front rail posts, preserving lower iron bands and the stem.
    iron=bpy.data.objects[family.BOW+'__Hull_Ironwork']
    bm=bmesh.new();bm.from_mesh(iron.data)
    todo=set(bm.verts)
    while todo:
        seed=todo.pop();group={seed};stack=[seed]
        while stack:
            for e in stack.pop().link_edges:
                for w in e.verts:
                    if w in todo:todo.remove(w);group.add(w);stack.append(w)
        if min(v.co.x for v in group)>CUT and min(v.co.z for v in group)>1.7:
            bmesh.ops.delete(bm,geom=list(group),context='VERTS')
    bm.to_mesh(iron.data);bm.free();iron.data.update()
    # Upper perimeter uses the actual hull outline, not the old inset platform.
    fore.SECTIONS=[(x+rake(x),w+.28,z) for x,w,z in [fore.hull_point(t) for t in [8.3,9.8,11.2,12.4]]]
    fore.END=fore.SECTIONS[-1][0]
    parts,sockets=fore.make_parts(bow)
    bpy.data.objects.remove(parts.pop('Deck'),do_unlink=True)
    guard_only(parts['Frame'])
    detail=ss.Builder();trim=ss.Builder()
    bottom=CUT-2.88;z=fore.base_height(bottom)+.025
    for s in [-1,1]:
        fore.tube(detail,[(bottom,s*.70,z+.04),(CUT,s*.70,HEIGHT-.10)],.15,.22,'wood2')
        fore.box(detail,bottom-.07,bottom+.07,s*.79-.07,s*.79+.07,z,z+.78,'wood')
        # Hull-level plank courses continue across the raised cheeks.
        for h in [2.50,3.05,3.60]:
            pts=[]
            for original in [8.3,9.8,11.2,12.4]:
                x,w,z0=fore.hull_point(original)
                pts.append((x+rake(x)*(h-z0)/(HEIGHT-z0),s*(w+.288),h))
            fore.tube(trim,pts,.012,.016,'seam')
        # A single deliberately broad band follows the new upper side.
        old_x,w,base=fore.hull_point(9.8)
        y=s*(w+.303)
        x=old_x+rake(old_x)
        a=[fore.point(xx,yy,base) for xx,yy in [(old_x-.14,y-.04),(old_x+.14,y-.04),(old_x+.14,y+.04),(old_x-.14,y+.04)]]
        b=[fore.point(xx,yy,HEIGHT-.04) for xx,yy in [(x-.14,y-.04),(x+.14,y-.04),(x+.14,y+.04),(x-.14,y+.04)]]
        detail.solid([a,b]+[[a[i],a[(i+1)%4],b[(i+1)%4],b[i]] for i in range(4)],'iron')
        for h in [2.7,3.85]:
            xx=old_x+rake(old_x)*(h-base)/(HEIGHT-base)
            detail.cyl(fore.point(xx,y,h),fore.point(xx,y+s*.08,h),.07,.07,6,'bolt')
    # Bulkhead plank courses stop cleanly at the hull corners.
    for h in [2.50,3.05,3.60]:
        fore.box(trim,CUT-.012,CUT+.002,-fore.width(CUT),fore.width(CUT),h-.008,h+.008,'seam')
    fore.tube(detail,[(x,-w,HEIGHT-.10) for x,w,_ in fore.SECTIONS]+
                    [(x,w,HEIGHT-.10) for x,w,_ in reversed(fore.SECTIONS)],.10,.20,'rim')
    for name,b in [('StructuralTrim',detail),('HullCourses',trim)]:
        obj=ship.mesh('RaisedBow__'+name,b);obj.parent=bow;parts[name]=obj
    # Lift the carved head to the new stem height, with a connecting iron stem.
    prow=bpy.data.objects[family.BOW+'__Prow']
    delta=HEIGHT-ship.deck(ship.ZF)
    for v in prow.data.vertices:
        v.co.z+=delta
        v.co.x+=RAKE
    stem=ss.Builder()
    fore.tube(stem,[(TIP,0,ship.deck(ship.ZF)-.03),(fore.END,0,HEIGHT+.22)],.26,.18,'iron')
    obj=ship.mesh('RaisedBow__StemExtension',stem);obj.parent=bow;parts['StemExtension']=obj
    parts.update({o.name.split('__',1)[1]:o for o in bow.children if o.type=='MESH' and o.name.startswith(family.BOW+'__')})
    bow['length']=max(v.co.x for obj in parts.values() for v in obj.data.vertices)
    bpy.data.objects[family.BOW+'__ForwardSocket'].location.x=bow['length']
    return bow,parts,sockets


def main():
    report={'mode':MODE,'assemblies':{},'parts':{}}
    for assembly in ['short','long']:
        bpy.ops.wm.open_mainfile(filepath=str(fore.SOURCE/assembly/'ship.blend'))
        bow,parts,sockets=build()
        bpy.context.view_layer.update()
        for name,obj in parts.items():
            check=ship.check(obj);report['parts'][name]=check
            assert check['overconnected_edges']==check['degenerate_faces']==0,(name,check)
            if assembly=='short':ss.export_fbx(obj,str(OUT/'models'/(name+'.fbx')))
        # The replaced shell must still weld to the unchanged stern/middle hull.
        assembled={m:{} for m in family.MODULES}
        for m in assembled:assembled[m]['Hull_Shell']=bpy.data.objects[m+'__Hull_Shell']
        bm=bmesh.new()
        for m in [family.STERN]+([family.MIDDLE] if assembly=='long' else [])+[family.BOW]:
            obj=assembled[m]['Hull_Shell'];mesh=obj.data.copy();mesh.transform(obj.matrix_world)
            bm.from_mesh(mesh);bpy.data.meshes.remove(mesh)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
        welded={'boundary_edges':sum(e.is_boundary for e in bm.edges),'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges)}
        bm.free();assert not any(welded.values()),welded
        previews=fore.cannon_previews(sockets);bpy.context.view_layer.update()
        hits=[]
        for obj in previews:
            tree=family.wheel_study.bvh(obj)
            for key in ['Rails','Frame','Stairs']:
                if tree.overlap(family.wheel_study.bvh(parts[key])):hits.append([obj.name,key])
        assert not hits,hits
        report['assemblies'][assembly]={'welded_hull':welded,'cannon_surface_intersections':hits}
        folder=OUT/assembly;folder.mkdir(exist_ok=True)
        mid=(20.28+(6 if assembly=='long' else 0))/2
        family.render(folder/'hero.png',(mid-26,-34,28),(mid,0,1.8),31,(1600,1150))
        family.render(folder/'side.png',(mid,-50,2),(mid,0,2),29,(1650,800))
        family.render(folder/'top.png',(mid,0,50),(mid,0,0),29,(1650,1100))
        family.render(folder/'detail.png',(bow.location.x+CUT-8,-12,14),(bow.location.x+CUT+1,0,3),15,(1400,1100))
        family.save(folder/'fit-review.blend')
        for obj in previews:obj.hide_render=True;obj.hide_set(True)
        report['assemblies'][assembly]['triangles_without_preview_cannons']=sum(len(p.vertices)-2
            for obj in bpy.context.scene.objects if obj.type=='MESH' and not obj.hide_render for p in obj.data.polygons)
        family.render(folder/'clean.png',(mid-26,-34,28),(mid,0,1.8),31,(1600,1150))
        family.save(folder/'ship.blend')
        if assembly=='short':
            (OUT/'manifest.json').write_text(json.dumps({'id':'RaisedBow_'+MODE+'_v1','interface':family.INTERFACE_STANDARD,
                'replaces':family.BOW,'placement':'replace entire matching bow module at identical transform; do not overlay',
                'length':bow['length'],'forward_marker_note':'decorative tip extent, not a joinable forward interface',
                'units':'V8 authoring units, not calibrated game metres','coordinates':'Blender +X bow +Y port +Z up; game (-y,z,x)',
                'files':{k:'models/'+k+'.fbx' for k in parts},'deck_height':HEIGHT,
                'sockets':{o.name:{'position':list(o.location),'rotation_euler':list(o.rotation_euler)} for o in bow.children if o.type=='EMPTY'},
                'status':'offline shape prototype; preview cannons excluded from mesh exports'},indent=2))
    report['replacement_bow_triangles']=sum(v['triangles'] for v in report['parts'].values())
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print('RAISED BOW PASS',MODE,report['replacement_bow_triangles'])


if __name__=='__main__':main()
