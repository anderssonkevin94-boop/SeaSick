"""Small mesh helpers for staged flat-shaded building assets; no scene side effects."""
import bpy
import bmesh
import math
from mathutils import Vector
import steamer as ss

class Kit:
    def __init__(self,name,colors):
        self.colors=colors;self.objects=[];self.group='Structure'
        self.root=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(self.root)
        self.mat=bpy.data.materials.new(name+'_VertexColor');self.mat.use_nodes=True
        node=self.mat.node_tree.nodes.new('ShaderNodeVertexColor');node.layer_name='Col'
        bsdf=next(n for n in self.mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        self.mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color']);bsdf.inputs['Roughness'].default_value=.9
    def mesh(self,name,verts,faces,color):
        me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
        bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
        ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob);ob.parent=self.root;ob['module']=self.group
        me.materials.append(self.mat);attr=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
        for p in me.polygons:
            p.use_smooth=False;c=color[p.index%len(color)] if isinstance(color,list) else color
            for i in p.loop_indices:attr.data[i].color=(*ss.srgb_to_linear(ss._hex(self.colors[c])),1)
        self.objects.append(ob);return ob
    def box(self,name,p,size,color,bevel=.015):
        verts=[tuple(p[i]+s[i]*size[i]/2 for i in range(3)) for s in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
        ob=self.mesh(name,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],color)
        if bevel:
            bpy.context.view_layer.objects.active=ob;m=ob.modifiers.new('Worn arris','BEVEL');m.width=min(bevel,min(size)*.35);m.segments=1;bpy.ops.object.modifier_apply(modifier=m.name)
        return ob
    def beam(self,name,a,b,width,depth,color):
        a,b=Vector(a),Vector(b);ob=self.box(name,(0,0,0),(width,depth,(b-a).length),color)
        ob.location=(a+b)/2;ob.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return ob
    def rod(self,name,a,b,r,color,n=8):
        a,b=Vector(a),Vector(b);d=(b-a).normalized();u=d.cross(Vector((0,0,1)))
        if u.length<.01:u=d.cross(Vector((0,1,0)))
        u.normalize();v=d.cross(u)
        verts=[p+r*(u*math.cos(i*math.tau/n)+v*math.sin(i*math.tau/n)) for p in [a,b] for i in range(n)]
        return self.mesh(name,verts,[tuple(reversed(range(n))),tuple(n+i for i in range(n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],color)
    def lathe(self,name,p,rings,color,n=10):
        verts=[(p[0]+r*math.cos(i*math.tau/n),p[1]+r*math.sin(i*math.tau/n),p[2]+z) for r,z in rings for i in range(n)]
        faces=[tuple(reversed(range(n))),tuple((len(rings)-1)*n+i for i in range(n))]
        faces += [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(rings)-1) for i in range(n)]
        return self.mesh(name,verts,faces,color)
    def marker(self,name,p,role):
        ob=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(ob);ob.parent=self.root;ob.location=p;ob['role']=role;return ob
    def join_modules(self):
        groups={};modules={}
        for ob in self.objects:groups.setdefault(ob['module'],[]).append(ob)
        for name,items in groups.items():
            bpy.ops.object.select_all(action='DESELECT')
            for ob in items:ob.select_set(True)
            bpy.context.view_layer.objects.active=items[0]
            if len(items)>1:bpy.ops.object.join()
            items[0].name=name;modules[name]=items[0]
        self.objects=list(modules.values());return modules
