"""Splits a prop pack (Sketchfab GLB/glTF/FBX/OBJ: many props in one scene) into one FBX per prop for Unity.

    blender -b -P Tools/Blender/split_pack.py -- <in> <out_dir> [--list] [--max-tris 20000] [--only REGEX] [--drop REGEX]
        [--level N] [--prefix name_] [--whole --name NAME] [--rot X,Y,Z] [--textures DIR]

Props are the children of the first node with more than one child (Sketchfab wraps a pack in single-child
roots: Sketchfab_model > root > GLTF_SceneRootNode > props); --level N goes N nodes further down instead, for packs
grouped by category; --textures DIR links <Material>_BaseColor / _Normal images from DIR to materials that have
none (OBJ packs whose .mtl names no maps); --whole treats the whole file as one prop (a single model made of parts); --rot X,Y,Z turns it by those
Euler degrees first (models exported lying on their side). --list only prints each candidate (name, size in metres, triangles, materials).

Each prop: its meshes joined, transforms applied, origin at the middle of its footprint on the floor (Unity's
Kit.Fit convention), decimated to --max-tris, exported to <out_dir>/<prefix><name>.fbx, its textures written
to <out_dir>/Textures (prefixed, so packs can share a folder). Names are cleaned to lower_snake_case.
"""
import bpy, sys, os, re, math, mathutils

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
flags = set(a for a in args[2:] if a in ("--list", "--whole"))
rest = [a for a in args[2:] if a not in flags]
opt = dict(zip(rest[0::2], rest[1::2]))
max_tris = int(opt.get("--max-tris", 20000))
only = re.compile(opt["--only"], re.I) if "--only" in opt else None
drop = re.compile(opt["--drop"], re.I) if "--drop" in opt else None
level = int(opt.get("--level", 0))
prefix = opt.get("--prefix", "")
Vec = mathutils.Vector

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = src.lower().rsplit(".", 1)[1]
if ext in ("glb", "gltf"):
    bpy.ops.import_scene.gltf(filepath=src)
elif ext == "obj":
    bpy.ops.wm.obj_import(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)


def meshes_under(o):
    return [c for c in [o] + list(o.children_recursive) if c.type == "MESH" and not c.hide_render]


def tris(objs):
    n = 0
    for o in objs:
        for p in o.data.polygons:
            n += len(p.vertices) - 2
    return n


def bounds(objs):
    lo, hi = Vec((1e9,) * 3), Vec((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vec(c)
            lo = Vec(map(min, lo, w))
            hi = Vec(map(max, hi, w))
    return lo, hi


def clean(name):
    name = re.sub(r"[._]?\d{3}$", "", name)
    name = re.sub(r"(_low|_\d+)$", "", name, flags=re.I)
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_") or "prop"


# GLB textures arrive packed in memory: write them out beside the FBXs so the exporter can point at real files.
tex_dir = os.path.join(out, "Textures")
used_names = set()
if "--list" not in flags:
    for img in bpy.data.images:
        if img.packed_file is None:
            continue
        os.makedirs(tex_dir, exist_ok=True)
        extn = {"JPEG": ".jpg", "PNG": ".png"}.get(img.file_format, ".png")
        # Sketchfab GLBs often name every image "Image": number them so none overwrites another.
        base = prefix + clean(img.name)
        stem, n = base, 1
        while stem in used_names:
            n += 1
            stem = f"{base}_{n}"
        used_names.add(stem)
        path = os.path.join(tex_dir, stem + extn)
        with open(path, "wb") as fh:
            fh.write(img.packed_file.data)
        img.filepath = path
        img.unpack(method="REMOVE")  # drop the packed copy; keep the path just written
        img.filepath = path

# Unlit glTF materials (KHR_materials_unlit: emission mixed with transparency, no Principled BSDF) export to FBX
# with no diffuse at all, so they arrive white in Unity: feed whatever colours the emission into a Principled BSDF.
for mat in bpy.data.materials:
    if not mat.use_nodes or any(n.type == "BSDF_PRINCIPLED" for n in mat.node_tree.nodes):
        continue
    emit = next((n for n in mat.node_tree.nodes if n.type == "EMISSION"), None)
    sink = next((n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if emit is None or sink is None:
        continue
    bsdf = mat.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.6
    color = emit.inputs["Color"]
    if color.links:
        mat.node_tree.links.new(color.links[0].from_socket, bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = color.default_value
    mat.node_tree.links.new(bsdf.outputs["BSDF"], sink.inputs["Surface"])

if "--textures" in opt:
    for mat in bpy.data.materials:
        if not mat.use_nodes or any(n.type == "TEX_IMAGE" for n in mat.node_tree.nodes):
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        base = os.path.join(opt["--textures"], mat.name.split(".")[0] + "_BaseColor.png")
        if bsdf is None or not os.path.exists(base):
            continue
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(base)
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        normal = base.replace("_BaseColor", "_Normal")
        if os.path.exists(normal):
            nt = mat.node_tree.nodes.new("ShaderNodeTexImage")
            nt.image = bpy.data.images.load(normal)
            nt.image.colorspace_settings.name = "Non-Color"
            nm = mat.node_tree.nodes.new("ShaderNodeNormalMap")
            mat.node_tree.links.new(nt.outputs["Color"], nm.inputs["Color"])
            mat.node_tree.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

# Find the node whose children are the props.
roots = [o for o in bpy.context.scene.objects if o.parent is None]
node = roots[0] if len(roots) == 1 else None
while node is not None and len([c for c in node.children if meshes_under(c)]) == 1:
    node = [c for c in node.children if meshes_under(c)][0]
candidates = roots if node is None else [c for c in node.children if meshes_under(c)]
for _ in range(level):
    candidates = [g for c in candidates for g in c.children if meshes_under(g)] or candidates
if "--whole" in flags:
    whole = bpy.data.objects.new(opt.get("--name", "prop"), None)
    bpy.context.scene.collection.objects.link(whole)
    for r in roots:
        r.parent = whole
    if "--rot" in opt:
        whole.rotation_euler = [math.radians(float(a)) for a in opt["--rot"].split(",")]
    candidates = [whole]

bpy.context.view_layer.update()
seen = {}
for c in candidates:
    name = clean(c.name)
    if only and not only.search(c.name) or drop and drop.search(c.name):
        continue
    objs = meshes_under(c)
    lo, hi = bounds(objs)
    size = hi - lo
    mats = sorted({s.material.name for o in objs for s in o.material_slots if s.material})
    print(f"PROP {c.name!r:40} -> {name:28} size {size.x:.2f}x{size.z:.2f}x{size.y:.2f} m (w x h x d) tris {tris(objs):>7} mats {len(mats)}")
    if "--list" in flags:
        continue

    # Join a copy of the prop's meshes, apply transforms, stand it on its footprint centre.
    bpy.ops.object.select_all(action="DESELECT")
    copies = []
    for o in objs:
        dup = o.copy()
        dup.data = o.data.copy()
        bpy.context.scene.collection.objects.link(dup)
        dup.parent = None
        dup.matrix_world = o.matrix_world.copy()
        copies.append(dup)
    for d in copies:
        d.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1:
        bpy.ops.object.join()
    prop = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    prop.data.transform(mathutils.Matrix.Translation(-Vec(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))))
    n = tris([prop])
    if n > max_tris:
        mod = prop.modifiers.new("decimate", "DECIMATE")
        mod.ratio = max_tris / n
        bpy.ops.object.modifier_apply(modifier=mod.name)
    count = seen.get(name, 0)
    seen[name] = count + 1
    label = prefix + name + (f"_{count + 1}" if count else "")
    prop.name = label
    os.makedirs(out, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, label + ".fbx"), use_selection=True, path_mode="RELATIVE",
                             embed_textures=False, axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_ALL",
                             mesh_smooth_type="FACE")
    bpy.data.objects.remove(prop, do_unlink=True)
    print(f"  -> {label}.fbx ({min(n, max_tris)} tris)")
