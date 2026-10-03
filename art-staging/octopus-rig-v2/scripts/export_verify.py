"""Export Octopus_Rigged.fbx from octopus_rig.blend and verify against a fresh import of Meshy's FBX."""
import bpy, os, math, json
from mathutils import Vector
HERE=os.path.dirname(os.path.abspath(__file__)); OUT=os.path.abspath(os.path.join(HERE,'..'))
SRC="/Users/kevinandersson/Downloads/Meshy_AI_Crimson_Octopus_1003103504_texture_fbx/Meshy_AI_Crimson_Octopus_1003103504_texture.fbx"
FBX=os.path.join(OUT,"Octopus_Rigged.fbx")
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,"octopus_rig.blend"))
rig=bpy.data.objects['OctopusRig']; ob=bpy.data.objects['Octopus']; me=ob.data
rig.data.pose_position='REST'
if rig.animation_data: rig.animation_data.action=None
for pb in rig.pose.bones: pb.rotation_mode='XYZ'; pb.rotation_euler=(0,0,0); pb.location=(0,0,0); pb.scale=(1,1,1)
print("OBJECTS",[(o.name,o.type) for o in bpy.data.objects])
for o in (rig,ob): print("XFORM",o.name,tuple(o.location),tuple(o.rotation_euler),tuple(o.scale))
# rest-curl table: bend at each joint about the child's local X (positive = rest pose already curled toward suckers)
table={}
for b in rig.data.bones:
    if not b.name.startswith('Arm'): continue
    e=dict(length=round(b.length,4))
    if b.parent and b.parent.name.startswith('Arm'):
        M=b.matrix_local.to_3x3(); pd=(M.inverted()@b.parent.matrix_local.to_3x3().col[1]).normalized()   # parent dir in child frame
        e['rest_bend_deg']=round(math.degrees(math.atan2(-pd.z,pd.y)),1)
    table[b.name]=e
json.dump(dict(note="rest_bend_deg = bend from parent to this bone about this bone's local X; positive = curled toward the suckers already in the bind pose. Rotating this bone by -rest_bend_deg about local X straightens that joint.",bones=table),open(os.path.join(OUT,'arm_rest_bends.json'),'w'),indent=1)
for k in range(len([b for b in rig.data.bones if b.name.endswith('_00')])):
    print("RESTBEND arm%d"%k,[table["Arm%d_%02d"%(k,i)].get('rest_bend_deg') for i in range(1,10)],"sum %.0f"%sum(table["Arm%d_%02d"%(k,i)].get('rest_bend_deg',0) for i in range(1,10)))
# weights check
nz=0; mx=0; bad=0
for v in me.vertices:
    ws=[g.weight for g in v.groups if g.weight>0]
    if not ws: nz+=1
    mx=max(mx,len(ws))
    if ws and abs(sum(ws)-1)>1e-3: bad+=1
print("WEIGHTS unweighted",nz,"max influences",mx,"not-normalised",bad,"groups",len(ob.vertex_groups))
mine=dict(v=len(me.vertices),f=len(me.polygons),l=len(me.loops),uv=[u.name for u in me.uv_layers],
          co=[tuple(v.co) for v in me.vertices],uvd=[tuple(d.uv) for d in me.uv_layers[0].data],mats=[m.name for m in me.materials])
for o in bpy.data.objects: o.select_set(o in (rig,ob))
bpy.ops.export_scene.fbx(filepath=FBX,use_selection=True,object_types={'ARMATURE','MESH'},apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,use_armature_deform_only=False,path_mode='STRIP',
    primary_bone_axis='Y',secondary_bone_axis='X',use_mesh_modifiers=False)
print("EXPORTED",FBX,os.path.getsize(FBX))
# ---- Octopus_Poses.fbx: same rig+mesh, Pose_P1..Pose_P3 as animation takes (2 keys each, frames 1-5)
FBXP=os.path.join(OUT,"Octopus_Poses.fbx")
for a in list(bpy.data.actions):
    if a.name not in ("Pose_P1","Pose_P2","Pose_P3"): bpy.data.actions.remove(a)
rig.animation_data_create(); rig.animation_data.action=bpy.data.actions["Pose_P1"]
rig.data.pose_position='POSE'
bpy.context.scene.frame_start=1; bpy.context.scene.frame_end=5
bpy.ops.export_scene.fbx(filepath=FBXP,use_selection=True,object_types={'ARMATURE','MESH'},apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
    bake_anim_use_all_bones=True,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0.0,use_armature_deform_only=False,
    path_mode='STRIP',primary_bone_axis='Y',secondary_bone_axis='X',use_mesh_modifiers=False)
print("EXPORTED",FBXP,os.path.getsize(FBXP))
# fresh import of the original
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
o0=[o for o in bpy.data.objects if o.type=='MESH'][0]; m0=o0.data
same_co=all((Vector(a)-v.co).length<1e-6 for a,v in zip(mine['co'],m0.vertices))
import bmesh as _bm
_b=_bm.new(); _b.from_mesh(m0); _b.verts.ensure_lookup_table(); _lab=[-1]*len(m0.vertices); _isl=[]
for _v in _b.verts:
    if _lab[_v.index]>=0: continue
    _st=[_v]; _c=[]; _lab[_v.index]=len(_isl)
    while _st:
        _a=_st.pop(); _c.append(_a.index)
        for _e in _a.link_edges:
            _o=_e.other_vert(_a)
            if _lab[_o.index]<0: _lab[_o.index]=len(_isl); _st.append(_o)
    _isl.append(_c)
_isl.sort(key=len,reverse=True)
head_isl=[j for isl in _isl[1:4] for j in isl]     # mantle shell + 2 eyes
ident=[j for j,(a,v) in enumerate(zip(mine['co'],m0.vertices)) if tuple(a)==tuple(v.co)]
moved=[(Vector(a)-v.co).length for a,v in zip(mine['co'],m0.vertices)]
print("IDENTITY mantle+eyes verts %d bit-identical %s | verts bit-identical overall %d/%d | max displacement any vertex %.4f m"%(
      len(head_isl), all(tuple(mine['co'][j])==tuple(m0.vertices[j].co) for j in head_isl), len(ident), len(m0.vertices), max(moved)))
same_uv=all(abs(a[0]-d.uv[0])<1e-7 and abs(a[1]-d.uv[1])<1e-7 for a,d in zip(mine['uvd'],m0.uv_layers[0].data))
print("ORIG v %d f %d loops %d uv %s | RIG v %d f %d loops %d uv %s | same positions %s same UVs %s"%(len(m0.vertices),len(m0.polygons),len(m0.loops),[u.name for u in m0.uv_layers],mine['v'],mine['f'],mine['l'],mine['uv'],same_co,same_uv))
# re-import the export
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX,automatic_bone_orientation=False)
for o in bpy.data.objects: print("REIMPORT",o.name,o.type,tuple(round(x,4) for x in o.scale),tuple(round(x,4) for x in o.rotation_euler))
m1=[o for o in bpy.data.objects if o.type=='MESH'][0]; a1=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
print("REIMPORT mesh v %d f %d uv %s groups %d mats %s"%(len(m1.data.vertices),len(m1.data.polygons),[u.name for u in m1.data.uv_layers],len(m1.vertex_groups),[m.name for m in m1.data.materials]))
sc=[b.matrix_local.to_scale() for b in a1.data.bones]
print("REIMPORT bones",len(a1.data.bones),"bone scale range %.4f..%.4f"%(min(min(s) for s in sc),max(max(s) for s in sc)), "dims",tuple(round(x,3) for x in m1.dimensions))
nz=sum(1 for v in m1.data.vertices if not [g for g in v.groups if g.weight>0])
print("REIMPORT unweighted",nz)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(OUT,"Octopus_Poses.fbx"),automatic_bone_orientation=False)
m2=[o for o in bpy.data.objects if o.type=='MESH'][0]
print("POSESFBX mesh v %d f %d uv %s | actions %s"%(len(m2.data.vertices),len(m2.data.polygons),[u.name for u in m2.data.uv_layers],
      [(a.name,tuple(a.frame_range)) for a in bpy.data.actions]))
