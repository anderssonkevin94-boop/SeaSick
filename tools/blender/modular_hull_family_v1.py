"""One beam family, reusable stern/bow and a removable six-unit midship bay.

Authoring and review only. All outputs stay outside Unity Assets.
"""
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import modular_ship_study_v1 as wheel_study

ship=wheel_study.ship
ss=ship.ss
OUT=HERE.parents[1]/'art-staging/modular-hull-family-v1'
OUT.mkdir(parents=True,exist_ok=True)
CUT_A=-2.25
CUT_B=3.75
STERN, MIDDLE, BOW='Stern_W1', 'Midship_W1', 'Bow_W1'
CROSS_SECTION={'deck_beam':9.28,'deck_height':1.76,'keel_height':-1.92}
SLOT_OFFSET=3.45
MODULES={STERN:(ship.ZT,CUT_A), MIDDLE:(CUT_A,CUT_B), BOW:(CUT_B,ship.ZF)}
LENGTHS={name:b-a for name,(a,b) in MODULES.items()}
EPS=1e-5
INTERFACE_STANDARD='W1'


def boundary_points(obj,plane):
    bm=bmesh.new(); bm.from_mesh(obj.data)
    points=sorted(set((round(v.co.y,5),round(v.co.z,5))
                      for e in bm.edges if e.is_boundary
                      for v in e.verts if abs(v.co.x-plane)<EPS))
    bm.free()
    return points


def section_vertices(obj,plane):
    bm=bmesh.new(); bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    ids={v.index for e in bm.edges if e.is_boundary for v in e.verts
         if abs(v.co.x-plane)<EPS}
    bm.free()
    return [obj.data.vertices[i] for i in sorted(ids)]


def snap_section(obj,plane,master):
    vertices=section_vertices(obj,plane)
    assert len(vertices)==len(master),(obj.name,len(vertices),len(master))
    max_error=0
    for v in vertices:
        match=min(master,key=lambda p:(v.co.y-p[0])**2+(v.co.z-p[1])**2)
        error=math.hypot(v.co.y-match[0],v.co.z-match[1])
        assert error<.00005,(obj.name,'non-numerical section mismatch',error)
        max_error=max(max_error,error)
        v.co.y,v.co.z=match
    obj.data.update()
    return max_error


def trim(obj,name,lo,hi):
    mesh=obj.data.copy()
    bm=bmesh.new(); bm.from_mesh(mesh)
    for plane,remove_inner in [(lo,True),(hi,False)]:
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
            dist=1e-6,plane_co=(plane,0,0),plane_no=(1,0,0),
            clear_inner=remove_inner,clear_outer=not remove_inner)
    if not bm.faces:
        bm.free(); bpy.data.meshes.remove(mesh)
        return None
    # Canonical section corners: remove triangulation-specific points on straight
    # boundary edges, so independently cut ends have identical weldable loops.
    for _ in range(5):
        candidates=[]
        for v in bm.verts:
            if min(abs(v.co.x-lo),abs(v.co.x-hi))>EPS: continue
            edges=[e for e in v.link_edges if e.is_boundary]
            if len(edges)!=2: continue
            a=edges[0].other_vert(v).co-v.co
            b=edges[1].other_vert(v).co-v.co
            if a.length>EPS and b.length>EPS and a.normalized().dot(b.normalized())<-.999999:
                candidates.append(v)
        if not candidates: break
        bmesh.ops.dissolve_verts(bm,verts=candidates,use_face_split=False,use_boundary_tear=False)
    for v in bm.verts:
        if abs(v.co.x-lo)<EPS: v.co.x=lo
        if abs(v.co.x-hi)<EPS: v.co.x=hi
        v.co.x-=lo
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free(); mesh.update()
    out=bpy.data.objects.new(name,mesh)
    bpy.context.scene.collection.objects.link(out)
    for p in mesh.polygons: p.use_smooth=False
    return out


def empty(name,location=(0,0,0),parent=None):
    obj=bpy.data.objects.new(name,None)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent=parent; obj.location=location; obj.empty_display_size=.35
    return obj


def configure_geometry():
    # A constant cross-section in the joint region gives both end modules a
    # common interface. The quarter-ellipse stern and tapered bow are retained.
    ship.ST=sorted(set(ship.ST+[CUT_A,CUT_B]))
    bow_knots=[3,5,7,8.6,9.9,10.9,11.7,12.4]
    bow_beam=[4.64,4.64,4.08,3.53,2.80,1.97,1.08,.08]
    ship.BEAM=[ship.stern_beam(x) if x<=-5 else 4.64 if x<=3
               else ship.hermite(x,bow_knots,bow_beam) for x in ship.ST]


def build():
    ss.clear_scene()
    configure_geometry()
    fixed=[ship.hull()]+ship.fittings()+ship.housing_fittings()
    tops=ship.topsides()
    helm=next(o for o in tops if o.name=='Helm')
    chimney=next(o for o in tops if o.name=='Chimney')
    chimney.data.transform(Matrix.Translation((-.3,0,-1.76)))
    fixed.append(helm)
    parts={}
    roots={}
    interface={}
    for module,(lo,hi) in MODULES.items():
        root=empty(module)
        root['interface_standard']=INTERFACE_STANDARD
        for key,value in CROSS_SECTION.items(): root[key]=value
        root['length']=hi-lo
        roots[module]=root
        parts[module]={}
        for src in fixed:
            # Preserve fittings beyond the physical bow/stern rather than
            # cutting the end caps. Only the actual module interfaces are cut.
            clip_lo=lo if module!=STERN else -100
            clip_hi=hi if module!=BOW else 100
            o=trim(src,module+'__'+src.name,clip_lo,clip_hi)
            if o is None: continue
            o.data.transform(Matrix.Translation((clip_lo-lo,0,0)))
            o.parent=root
            parts[module][src.name]=o
        empty(module+'__AftSocket',(0,0,0),root)
        empty(module+'__ForwardSocket',(hi-lo,0,0),root)
        for i,x in enumerate([1.5,3.5,5.5]):
            if x>hi-lo-1: continue
            # End sections keep the wheel service area and tapered bow clear.
            if module==STERN and x<5.5: continue
            if module==BOW and x>3.5: continue
            for side in [-1,1]:
                sock=empty(f'{module}__DeckSlot_{i}_{side}',(x,side*SLOT_OFFSET,1.76),root)
                sock['status']='provisional attachment location; cannon fit not yet verified'
    for o in fixed: bpy.data.objects.remove(o,do_unlink=True)
    for name in ['Hull_Shell','Rails_Teal','Hull_Ironwork','Plank_Seams']:
        master=[(v.co.y,v.co.z) for v in section_vertices(parts[STERN][name],LENGTHS[STERN])]
        for mod,end in [(MIDDLE,'aft'),(MIDDLE,'forward'),(BOW,'aft')]:
            snap_section(parts[mod][name],LENGTHS[mod] if end=='forward' else 0,master)
        ends={}
        for mod,end in [(STERN,'forward'),(MIDDLE,'aft'),
                        (MIDDLE,'forward'),(BOW,'aft')]:
            ends[mod+'_'+end]=boundary_points(parts[mod][name],LENGTHS[mod] if end=='forward' else 0)
        interface[name]=ends
    (OUT/'interfaces.json').write_text(json.dumps(interface,indent=2))
    rot=wheel_study.rotor(1.62,True)
    carrier=wheel_study.carrier(1.62)
    for o in [rot,carrier]:
        o.parent=roots[STERN]
        o.location=wheel_study.AXLE-Vector((ship.ZT,0,0))
    empty('WheelModuleSocket',tuple(rot.location),roots[STERN])
    return parts,roots,rot,carrier,chimney,interface


def assemble(roots,chimney,count):
    roots[STERN].location=(0,0,0)
    roots[MIDDLE].location=(LENGTHS[STERN],0,0)
    roots[BOW].location=(LENGTHS[STERN]+count*LENGTHS[MIDDLE],0,0)
    for o in roots[MIDDLE].children:
        o.hide_render=not count
        o.hide_set(not count)
    roots[MIDDLE].hide_set(not count)
    # Chimney is an independent fitting, not baked into the removable bay.
    total=LENGTHS[STERN]+count*LENGTHS[MIDDLE]+LENGTHS[BOW]
    chimney.location=(total*.50,0,1.76)
    bpy.context.view_layer.update()
    return total


def joined_check(parts,roots,count):
    report={}
    for component in ['Hull_Shell','Rails_Teal','Hull_Ironwork','Plank_Seams']:
        bm=bmesh.new()
        placements=[(STERN,0)]+[(MIDDLE,i*LENGTHS[MIDDLE]) for i in range(count)]+[(BOW,0)]
        for module,offset in placements:
            obj=parts[module][component]
            mesh=obj.data.copy(); mesh.transform(Matrix.Translation((offset,0,0))@obj.matrix_world)
            bm.from_mesh(mesh); bpy.data.meshes.remove(mesh)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00005)
        report[component]={'boundary_edges':sum(e.is_boundary for e in bm.edges),
                           'overconnected_edges':sum(len(e.link_faces)>2 for e in bm.edges),
                           'degenerate_faces':sum(f.calc_area()<1e-9 for f in bm.faces)}
        bm.free()
    return report


def render(path,eye,target,scale,res=(1500,1050)):
    scene=bpy.context.scene
    ss.setup_render(scene)
    scene.display.shading.show_shadows=False
    scene.display.shading.cavity_type='BOTH'
    scene.display.shading.studiolight_rotate_z=.5
    scene.view_settings.exposure=.50
    scene.world.color=ss.srgb_to_linear(ss._hex('#99B5C4'))
    scene.display.shading.background_type='WORLD'
    cam=scene.camera
    if cam is None:
        data=bpy.data.cameras.new('ReviewCamera')
        cam=bpy.data.objects.new('ReviewCamera',data)
        scene.collection.objects.link(cam); scene.camera=cam
    cam.data.type='ORTHO'; cam.data.ortho_scale=scale
    cam.location=eye
    cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x,scene.render.resolution_y=res
    scene.render.resolution_percentage=100
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)


def save(path):
    scene=bpy.context.scene
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.shading.color_type='VERTEX'
                area.spaces.active.region_3d.view_rotation=scene.camera.rotation_euler.to_quaternion()
                area.spaces.active.region_3d.view_distance=32
                area.spaces.active.region_3d.view_location=(12,0,1)
    bpy.ops.wm.save_as_mainfile(filepath=str(path))


def main():
    parts,roots,rot,carrier,chimney,interface=build()
    report={'interfaces':{},'assemblies':{},'modules':{},'repeat_tests':{}}
    for component,ends in interface.items():
        values=list(ends.values())
        report['interfaces'][component]={'point_counts':{k:len(v) for k,v in ends.items()},
                                        'exact_match_5dp':all(v==values[0] for v in values)}
    for mod,pieces in parts.items():
        report['modules'][mod]={name:ship.check(o) for name,o in pieces.items()}
        folder=OUT/'models'/mod; folder.mkdir(parents=True,exist_ok=True)
        for name,o in pieces.items(): ss.export_fbx(o,str(folder/(name+'.fbx')))
    for o in [rot,carrier,chimney]:
        ss.export_fbx(o,str(OUT/'models'/'Fittings'/(o.name+'.fbx')))
    for name,count in [('short',0),('long',1)]:
        total=assemble(roots,chimney,count)
        report['assemblies'][name]={'length':total,'joined_geometry':joined_check(parts,roots,count)}
        report['assemblies'][name]['triangles']=sum(len(p.vertices)-2 for o in bpy.context.scene.objects
              if o.type=='MESH' and not o.hide_render for p in o.data.polygons)
        out=OUT/name; out.mkdir(exist_ok=True)
        middle=total/2
        render(out/'side.png',(middle,-45,1.6),(middle,0,1.6),28.5,(1500,600))
        render(out/'top.png',(middle,0,50),(middle,0,0),28.5,(1500,750))
        render(out/'hero.png',(middle-29,-34,25),(middle,0,1.2),30.5)
        save(out/'ship.blend')
    for count in [2,3]:
        assemble(roots,chimney,count)
        report['repeat_tests'][str(count)]=joined_check(parts,roots,count)
    assemble(roots,chimney,1)
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print('VALIDATION',json.dumps(report['interfaces']),json.dumps(report['assemblies']),flush=True)
    assert all(v['exact_match_5dp'] for v in report['interfaces'].values()),'Interface mismatch; inspect interfaces.json'
    assert all(not any(c.values()) for a in report['assemblies'].values() for c in a['joined_geometry'].values()),'Joined geometry is not clean'
    assert all(not any(c.values()) for a in report['repeat_tests'].values() for c in a.values()),'Repeated bays do not join cleanly'
    manifest={'standard':INTERFACE_STANDARD,'coordinates':'Blender +X bow, +Y port, +Z up; unchanged V8 authoring units',
              'cross_section':dict(CROSS_SECTION),
              'modules':{m:{'length':LENGTHS[m],'origin':'aft connection plane, ship height datum',
                            'aft_socket':[0,0,0],'forward_socket':[LENGTHS[m],0,0],
                            'files':[f'models/{m}/{n}.fbx' for n in parts[m]],
                            'triangles':sum(c['triangles'] for c in report['modules'][m].values())} for m in MODULES},
              'assemblies':{name:{'midship_count':n,'stern_position':[0,0,0],
                                  'midship_first_position':[LENGTHS[STERN],0,0],
                                  'bow_position':[LENGTHS[STERN]+n*6,0,0],
                                  'length':LENGTHS[STERN]+n*6+LENGTHS[BOW]} for name,n in [('short',0),('long',1)]},
              'wheel':{'mount':'M1','socket_stern_local':list(rot.location),
                       'rotor_file':'models/Fittings/Rotor.fbx','carrier_file':'models/Fittings/Carrier.fbx',
                       'spin_axis_blender':'Y','spin_axis_game':'X'},
              'chimney':{'file':'models/Fittings/Chimney.fbx','origin':'base centre',
                         'placement':'(assembled length / 2, 0, 1.76) in Blender'},
              'export_axes':'Existing V8 FBX convention: Blender (x,y,z) location maps to game (-y,z,x).',
              'sockets':{o.name:{'parent':o.parent.name if o.parent else None,'position':list(o.location)}
                         for o in bpy.context.scene.objects if o.type=='EMPTY' and o.parent},
              'status':'Offline prototype; no colliders, LODs, navigation, balance or Unity import.'}
    from export_hull_hydrostatics import attach as attach_hydrostatics
    attach_hydrostatics(manifest,parts,OUT)
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
    # The exploded library makes actual shared modules and their open join loops visible.
    roots[MIDDLE].location.x+=4
    roots[BOW].location.x+=8
    chimney.hide_render=True; chimney.hide_set(True)
    render(OUT/'exploded.png',(-12,-40,34),(16,0,0),40,(1800,1100))
    save(OUT/'module-library.blend')


if __name__=='__main__': main()
