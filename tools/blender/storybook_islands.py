"""Authored graphic-adventure island kit. Individual FBXs, paired LODs and contact sheet.
Run through Blender MCP. Does not save or clear the user's original Blender scene.
"""
import bpy, math, random
from mathutils import Vector
from pathlib import Path
ROOT=Path('/Users/kevinandersson/Desktop/SeaSick')
OUT=ROOT/'Assets/_Project/Resources/Flora/Storybook'
COLORS={'bark':(.20,.095,.037),'leaf':(.17,.32,.035),'pine':(.045,.16,.095),
        'rock':(.48,.45,.36),'bush':(.20,.36,.045),'ore':(.23,.29,.34),'wheat':(.59,.38,.08)}

def mesh(name,verts,faces,color,coll):
    m=bpy.data.meshes.new(name); m.from_pydata(verts,[],faces);m.update()
    o=bpy.data.objects.new(name,m);coll.objects.link(o)
    attr=m.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
    for poly in m.polygons:
        # Broad light-facing planes, without random per-triangle confetti.
        shade=.78+.24*max(0,poly.normal.z)+.10*max(0,poly.normal.x)
        for li in poly.loop_indices: attr.data[li].color=(*[min(1,c*shade) for c in color],1)
    mat=bpy.data.materials.get('Storybook_Vertex')
    if not mat:
        mat=bpy.data.materials.new('Storybook_Vertex');mat.use_nodes=True
        bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.87
        v=mat.node_tree.nodes.new('ShaderNodeVertexColor');v.layer_name='Color'
        mat.node_tree.links.new(v.outputs['Color'],bs.inputs['Base Color'])
    m.materials.append(mat)
    return o

def limb(a,b,r0,r1,color,coll,sides=7):
    a,b=Vector(a),Vector(b);axis=(b-a).normalized();u=axis.cross(Vector((0,1,0))).normalized();v=axis.cross(u)
    vs=[]
    for p,r in ((a,r0),(b,r1)):
        vs += [tuple(p+r*(u*math.cos(i*math.tau/sides)+v*math.sin(i*math.tau/sides))) for i in range(sides)]
    fs=[tuple(range(sides-1,-1,-1)),tuple(range(sides,sides*2))]
    fs += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    return mesh('branch',vs,fs,color,coll)

def crown(at,scale,color,coll,seed,lod=False):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1 if lod else 2,radius=1,location=at)
    o=bpy.context.object
    for c in list(o.users_collection): c.objects.unlink(o)
    coll.objects.link(o)
    rng=random.Random(seed)
    for v in o.data.vertices:
        f=1+rng.uniform(-.09,.09);v.co.x*=scale[0]*f;v.co.y*=scale[1]*f;v.co.z*=scale[2]*f
    o.data.update()
    # Transfer geometry into the common flat-shaded vertex-colour builder.
    q=mesh('canopy',[tuple(v.co+Vector(at)) for v in o.data.vertices],[tuple(p.vertices) for p in o.data.polygons],color,coll)
    bpy.data.objects.remove(o,do_unlink=True);return q

def join(parts,name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:p.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    if len(parts)>1:bpy.ops.object.join()
    o=parts[0];o.name=name
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    return o

def oak(name,coll,seed=1,lod=False):
    rng=random.Random(seed);parts=[]
    variant=name.replace('_LOD1','')
    # Mature oak, airy birch, low coastal oak and young woodland tree.
    wide,tall,lean,col,bark = {
        'Broad':(1.15,1.0,.35,(.19,.34,.045),COLORS['bark']),
        'Broad_B':(.70,1.20,-.4,(.27,.42,.075),(.53,.49,.36)),
        'Broad_C':(1.28,.78,1.4,(.115,.26,.065),COLORS['bark']),
        'Broad_Young':(.62,.72,.2,(.31,.43,.055),(.27,.15,.07)),
    }[variant]
    parts.append(limb((0,0,0),(lean,0,6.6*tall),.38*wide,.13,bark,coll,5 if lod else 7))
    for i in range(5):
        a=i*math.tau/5+.4;tip=(lean+math.cos(a)*2.5*wide,math.sin(a)*2.2*wide,(6.6+rng.uniform(-.9,1.0))*tall)
        parts.append(limb((lean*.5,0,3.7*tall),tip,.20*wide,.055,bark,coll,5))
        parts.append(crown(tip,(2.15*wide,2.0*wide,1.9*tall),tuple(c*(.84+.07*i) for c in col),coll,seed*20+i,lod))
    parts.append(crown((lean,.1,8.7*tall),(2.4*wide,2.2*wide,1.95*tall),tuple(c*1.14 for c in col),coll,seed+88,lod))
    return join(parts,name)

def pine(name,coll,seed=2,lod=False):
    rng=random.Random(seed);parts=[limb((0,0,0),(.25,0,8.0 if name.startswith('Spruce_Young') else 11.7),.32,.045,COLORS['bark'],coll,5)]
    for k,(z,r,hh) in enumerate([(2.5,2.8,5.1),(4.6,2.45,4.8),(6.7,1.9,4.3),(8.8,1.15,3.5)]):
        n=7 if lod else 10;vs=[]
        if name.startswith('Spruce_B'): r*=.72;z*=1.08;hh*=1.06
        elif name.startswith('Spruce_Young'):r*=.80;z*=.67;hh*=.72
        # Jagged but connected umbrella edges, broad sloping faces.
        for i in range(n):
            a=i*math.tau/n+k*.7;rr=r*rng.uniform(.85,1.13)
            vs.append((math.cos(a)*rr+.15,math.sin(a)*rr,z+rng.uniform(-.28,.2)))
        vs.append((.2,0,z+hh));vs.append((.15,0,z+.35))
        fs=[(i,(i+1)%n,n) for i in range(n)]+[((i+1)%n,i,n+1) for i in range(n)]
        parts.append(mesh('needles',vs,fs,tuple(c*(.86+.11*k) for c in ((.065,.22,.16) if name.startswith('Spruce_B') else (.16,.29,.095) if name.startswith('Spruce_Young') else COLORS['pine'])),coll))
    return join(parts,name)

def stone(name,coll,seed,tall=False,ore=False):
    rng=random.Random(seed);n=5;vs=[]
    # Blunt block, beveled shoulder and broad flat crown; no crystal apex.
    outline=[(x*rng.uniform(.88,1.12),y*rng.uniform(.88,1.12)) for x,y in [(-1,-.55),(.10,-1),(.95,-.30),(.65,.75),(-.5,.9)]]
    for z,scale,offset in [(-.12,.84,0),(.12,1,0),(.78,.91,.08),(1,.67,.13)]:
        for x,y in outline:vs.append((x*scale+offset*2,y*scale-offset*.8,(z+max(0,z)*(.20*x+.12*y))*(1.05 if tall else .82)))
    fs=[tuple(range(n-1,-1,-1))]
    for j in range(3):
        fs += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for i in range(n)]
    fs.append(tuple(range(3*n,4*n)))
    aspect=([(1.0,.72,1.5),(.75,.85,1.85),(1.2,.65,1.25)][seed%3] if tall else [(1.2,.75,.7),(.85,1.12,1.2),(1.4,.9,.55),(.9,.85,1.0)][seed%4])
    vs=[(x*aspect[0],y*aspect[1],z*aspect[2]) for x,y,z in vs]
    rockcol=[(.48,.45,.36),(.40,.43,.44),(.56,.51,.39),(.43,.44,.35)][seed%4]
    parts=[mesh(name,vs,fs,COLORS['ore'] if ore else rockcol,coll)]
    if ore:
        for i in range(4):parts.append(crown((math.cos(i*1.8)*.65,math.sin(i*1.8)*.6,.5),(.22,.19,.21),(.36,.44,.43),coll,seed+i,True))
    return join(parts,name)

def bluff(name,coll,variant):
    # A handful of broad geological planes, rather than a scaled pillar.
    outlines=[
        [(-1.7,-.5),(-.6,-.78),(1.65,-.6),(1.8,.35),(.9,.72),(-1.65,.6)],
        [(-1.5,-.6),(.2,-.88),(1.75,-.15),(1.25,.68),(-1.7,.55)],
        [(-1.8,-.3),(-.9,-.75),(1.4,-.7),(1.75,.6),(-1.5,.55)],
        [(-1.6,-.6),(.8,-.8),(1.8,-.05),(1.2,.8),(-1.75,.5)],
    ]
    outline=outlines[variant];n=len(outline);vs=[]
    for z,scale,lean in [(-.12,1.04,0),(.12,1,0),(.62,.92,.16),(1,.60,.35)]:
        for x,y in outline:
            px=x*scale+lean*(-1 if variant%2 else 1)
            py=y*scale+lean*.5
            vs.append((px,py,z*(1+.22*px*(1 if variant<2 else -1)+.13*py)))
    fs=[tuple(range(n-1,-1,-1))]
    for row in range(3):
        fs += [(row*n+i,row*n+(i+1)%n,(row+1)*n+(i+1)%n,(row+1)*n+i) for i in range(n)]
    fs.append(tuple(range(3*n,4*n)))
    return mesh(name,vs,fs,[(.43,.42,.35),(.46,.44,.36),(.40,.415,.37),(.45,.43,.35)][variant],coll)

def shrub(name,coll,seed,lod=False):
    parts=[]
    variant=int(name.split('_')[1]);spread=[.75,1.25,.55][variant];height=[1.2,.7,1.6][variant]
    for i in range(3):
        a=i*2.1;parts.append(crown((math.cos(a)*.5,math.sin(a)*.5,.6+i*.12),(.85*spread,.75*spread,.65*height),tuple(c*(.78+.13*variant+.1*i) for c in COLORS['bush']),coll,seed+i,lod))
    return join(parts,name)

def fronds(name,coll,seed,grass=False):
    rng=random.Random(seed);vs=[];fs=[]
    for i in range(7 if grass else 9):
        a=i*2.399;r=rng.uniform(.65,1.1);h=rng.uniform(.55,1.15);base=len(vs)
        u=Vector((math.cos(a),math.sin(a),0));v=Vector((-u.y,u.x,0));w=.07 if grass else .23
        for p in (Vector((0,0,0)),u*r*.4-v*w+Vector((0,0,h*.8)),u*r+Vector((0,0,h*.65)),u*r*.4+v*w+Vector((0,0,h*.8))):vs.append(tuple(p))
        fs.extend([(base,base+1,base+2),(base,base+2,base+3),(base+2,base+1,base),(base+3,base+2,base)])
    col=(.17,.25,.045) if name.endswith('_Dry') else (.085,.205,.026) if name.endswith('_B') else (.100,.243,.030)
    if name.endswith('_B'):vs=[(x*.7,y*.7,z*1.5) for x,y,z in vs]
    return mesh(name,vs,fs,col,coll)

def sticks(name,coll):
    col=(.34,.24,.13) if name=='Driftwood' else COLORS['bark']
    parts=[limb((-1,0,.15),(1.2,.22,.22),.14,.08,col,coll,5),limb((.15,.12,.18),(.62,.85,.25),.07,.025,col,coll,5)]
    return join(parts,name)

def export(objects,path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,add_leaf_bones=False,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_custom_props=False,colors_type='LINEAR')

def build():
    previous=bpy.context.window.scene
    scene=bpy.data.scenes.get('Storybook_Island_Assets') or bpy.data.scenes.new('Storybook_Island_Assets')
    originals=[]
    try:
        bpy.context.window.scene=scene
        for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
        coll=bpy.data.collections.new('Storybook_Kit');scene.collection.children.link(coll)
        names=['Broad','Broad_B','Broad_C','Broad_Young','Spruce','Spruce_B','Spruce_Young','Palm','Ore','Fern','Fern_B','Grass','Grass_B','Grass_Dry','Sticks','Driftwood']+[f'{k}_{i}' for k,n in [('Boulder',4),('Cliff',3),('Bluff',4),('Scrub',3),('Crop',3)] for i in range(n)]
        for name in names:
            for nm in [name,name+'_LOD1']:
                old=bpy.data.objects.get(nm)
                if old:originals.append((old,nm));old.name='Preserved_'+nm
        kit=[];pairs=[]
        for i,name in enumerate(names):
            make=None
            if name.startswith('Broad'):make=lambda lod,n=name,j=i:oak(n+('_LOD1' if lod else ''),coll,j+7,lod)
            elif name.startswith('Spruce'):make=lambda lod,n=name,j=i:pine(n+('_LOD1' if lod else ''),coll,j+7,lod)
            elif name.startswith('Scrub'):make=lambda lod,n=name,j=i:shrub(n+('_LOD1' if lod else ''),coll,j+7,lod)
            if make:pair=[make(False),make(True)]
            elif name.startswith('Bluff'):pair=[bluff(name,coll,int(name.split('_')[1]))]
            elif name.startswith('Boulder') or name.startswith('Cliff') or name=='Ore':pair=[stone(name,coll,i+10,name.startswith('Cliff'),name=='Ore')]
            elif name.startswith(('Fern','Grass')):pair=[fronds(name,coll,i,name.startswith('Grass'))]
            elif name in ('Sticks','Driftwood'):pair=[sticks(name,coll)]
            else:
                # Preserve recognisable palm and wheat resource forms via existing builders.
                g={'__name__':'builders','SS_NO_AUTORUN':True};src=ROOT/'tools/blender/seasick_style.py';exec(compile(src.read_text(),str(src),'exec'),g)
                pair=[g['build_palm'](name,coll,h=10,seed=i)] if name=='Palm' else [g['build_crop'](name,coll,seed=i+70)]
                pair.append(g['build_palm'](name+'_LOD1',coll,h=10,seed=i,lod=True) if name=='Palm' else g['build_crop'](name+'_LOD1',coll,seed=i+70,lod=True))
            pairs.append((name,pair));kit.extend(pair)
        OUT.mkdir(parents=True,exist_ok=True)
        for name,pair in pairs:export(pair,OUT/(name+'.fbx'))
        export(kit,OUT.parent/'storybook_flora.fbx')
        # Put the assets into a readable contact-sheet arrangement after export.
        for i,(name,pair) in enumerate(pairs):
            for o in pair:o.location=((i%6)*15,(i//6)*17,0)
            for o in pair[1:]:o.hide_render=True;o.hide_set(True)
        scene.render.engine='CYCLES';scene.cycles.samples=24
        scene.world=bpy.data.worlds.new('Storybook_Studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.36,.43,.52,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
        ld=bpy.data.lights.new('Kit_Sun','SUN');lo=bpy.data.objects.new('Kit_Sun',ld);coll.objects.link(lo);lo.rotation_euler=(.45,-.5,-.4);ld.energy=2
        cd=bpy.data.cameras.new('Kit_Camera');co=bpy.data.objects.new('Kit_Camera',cd);coll.objects.link(co);co.location=(102,-105,112);co.rotation_euler=(Vector((37,34,3))-co.location).to_track_quat('-Z','Y').to_euler();cd.type='ORTHO';cd.ortho_scale=130;scene.camera=co
        scene.render.resolution_x=1500;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
        scene.view_settings.view_transform='Standard';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ROOT/'docs/art-direction/storybook-asset-kit.png');bpy.ops.render.render(write_still=True)
        bpy.data.libraries.write(str(ROOT/'tools/blender/source/storybook-islands.blend'), {scene})
        print('Exported',len(pairs),'individual assets;',len(kit),'LOD meshes')
    finally:
        for o,n in originals:
            current=bpy.data.objects.get(n)
            if current and current!=o:current.name='Storybook_'+n
            o.name=n
        bpy.context.window.scene=previous
if __name__=='__main__':build()
