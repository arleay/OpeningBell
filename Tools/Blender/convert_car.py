"""Turns a detailed car model (GLB/glTF/FBX/.blend from Sketchfab, BlendSwap and the like) into the car-kit layout
CarFactory drives: a root with children "body" and "wheel-front-left/-right", "wheel-back-left/-right", front
towards Unity's +z, wheels on the ground, sized to a real length.

    blender -b -P Tools/Blender/convert_car.py -- <in> <out.fbx> [--length 4.7] [--max-tris 60000 | 0 = keep all]
        [--wheels REGEX|auto] [--front +y|-y|+x|-x] [--drop REGEX] [--only REGEX] [--scale 1.35]
        [--paint REGEX] [--paint-colour r,g,b]

Only visible, rendered meshes count; --drop removes more by name (backdrops, lights, signs) and --only keeps just
the matching ones (one car out of a pack). Wheels are found by name (--wheels REGEX; default tyres/rims/hubs/discs,
brake calipers stay on the body since they steer but don't spin) or, when parts are unnamed (--wheels auto, also
the fallback when names find nothing), by shape: round in the side view, in the bottom of the car, near a corner.
A part holding both wheels of an axle is split into its loose pieces first. Wheel parts are grouped into four
wheels by corner. The front is guessed from part names containing "front"/"rear" (else --front). Everything else
is joined into the body; body and wheels are decimated to --max-tris overall. Textures go to Textures/ beside the FBX.
"""
import bpy, sys, os, re, mathutils

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
opt = dict(zip(args[2::2], args[3::2]))
# CarFactory scales every car model by 1.35 (the Kenney kit's were small), so its files are exported smaller.
# A controller that uses the model at real size (no rescale) wants --scale 1.
FACTORY_SCALE = float(opt.get("--scale", 1.35))
length = float(opt.get("--length", 4.7))
max_tris = int(opt.get("--max-tris", 60000))
wheel_opt = opt.get("--wheels", r"tire|tyre|wheel|rim|hub|disk|disc|opona|reifen|felge")
not_wheel = re.compile(r"brake|caliper|calliper|steering|arch|radlauf|well|fender|trim|badge|logo", re.I)
drop = re.compile(opt["--drop"], re.I) if "--drop" in opt else None
only = re.compile(opt["--only"], re.I) if "--only" in opt else None
Vec = mathutils.Vector

ext = src.lower().rsplit(".", 1)[1]
if ext == "blend":
    bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if ext in ("glb", "gltf"):
        bpy.ops.import_scene.gltf(filepath=src)
    elif ext == "obj":
        bpy.ops.wm.obj_import(filepath=src)
    else:
        bpy.ops.import_scene.fbx(filepath=src)
scene = bpy.context.scene

# Modifiers (mirror, subdivision, bevels) baked in; hidden, non-rendered, dropped or other-pack parts removed.
dg = bpy.context.evaluated_depsgraph_get()
meshes = []
for o in list(scene.objects):
    if o.type != "MESH":
        continue
    keep = o.visible_get() and not o.hide_render and not (drop and drop.search(o.name)) and not (only and not only.search(o.name))
    if not keep:
        continue
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    me.transform(o.matrix_world)
    n = bpy.data.objects.new(o.name, me)
    scene.collection.objects.link(n)
    meshes.append(n)
for o in list(scene.objects):
    if o not in meshes:
        bpy.data.objects.remove(o)
meshes = [o for o in meshes if len(o.data.polygons) > 0]


def flatten_materials(objs):
    """FBX carries a base colour and one colour texture per material, nothing in between. Exports that tint paint
    through Mix nodes and vertex colours (common on Sketchfab) would arrive white, so each such material gets the
    image plugged straight into Base Color, or, with no image, a flat colour: the average vertex colour of the faces
    that use it (times any constant colour in the mix)."""
    def image_in(sock, depth=0):
        for link in sock.links:
            n = link.from_node
            if n.type == "TEX_IMAGE":
                return n
            if depth < 4:
                for inp in n.inputs:
                    found = image_in(inp, depth + 1)
                    if found:
                        return found
        return None

    avg = {}
    for o in objs:
        me = o.data
        attr = me.color_attributes.active_color if me.color_attributes else None
        if attr is None:
            continue
        for poly in me.polygons:
            if poly.material_index >= len(o.material_slots) or o.material_slots[poly.material_index].material is None:
                continue
            m = o.material_slots[poly.material_index].material
            acc = avg.setdefault(m.name, [0.0, 0.0, 0.0, 0])
            for li in poly.loop_indices:
                c = attr.data[li if attr.domain == "CORNER" else me.loops[li].vertex_index].color
                acc[0] += c[0]; acc[1] += c[1]; acc[2] += c[2]; acc[3] += 1
    for m in bpy.data.materials:
        if not m.node_tree:
            continue
        p = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if p is None or not p.inputs["Base Color"].links:
            continue
        base = p.inputs["Base Color"]
        direct = base.links[0].from_node
        if direct.type == "TEX_IMAGE":
            continue
        img = image_in(base)
        tree = m.node_tree
        for link in list(base.links):
            tree.links.remove(link)
        if img is not None:
            tree.links.new(img.outputs["Color"], base)
        elif m.name in avg and avg[m.name][3] > 0:
            a = avg[m.name]
            base.default_value = (a[0] / a[3], a[1] / a[3], a[2] / a[3], 1.0)


def bounds(objs):
    lo = Vec((1e18,) * 3); hi = -lo
    for o in objs:
        for v in o.data.vertices:
            lo = Vec(map(min, lo, v.co)); hi = Vec(map(max, hi, v.co))
    return lo, hi


def centre(objs):
    lo, hi = bounds(objs)
    return (lo + hi) / 2


def size(objs):
    lo, hi = bounds(objs)
    return hi - lo


def transform(objs, m):
    for o in objs:
        o.data.transform(m)
        o.data.update()


# Length onto Y (whichever horizontal axis the car is longer on), ground at z 0, centred.
lo, hi = bounds(meshes)
if hi.x - lo.x > hi.y - lo.y:
    transform(meshes, mathutils.Matrix.Rotation(1.5707963, 4, "Z"))
lo, hi = bounds(meshes)
transform(meshes, mathutils.Matrix.Translation(-Vec(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))))
car = size(meshes)
flatten_materials(meshes)


def tag_glass():
    """Unity swaps every car material named "glass" for the tinted see-through one (ArtImportRules), but authors name
    windows anything ("vitre phares" on the Urus). A material that transmits light, or is mostly transparent, is glass:
    the name gets " glass" so the swap finds it."""
    for m in bpy.data.materials:
        if "glass" in m.name.lower() or not m.node_tree:
            continue
        clear = False
        for n in m.node_tree.nodes:
            if n.type in ("BSDF_GLASS", "BSDF_TRANSPARENT", "BSDF_REFRACTION"):
                clear = True
            elif n.type == "BSDF_PRINCIPLED":
                t = n.inputs.get("Transmission Weight")
                a = n.inputs.get("Alpha")
                clear |= t is not None and not t.links and t.default_value >= 0.5
                clear |= a is not None and not a.links and a.default_value <= 0.5
        if clear:
            m.name += " glass"


tag_glass()


def fix_materials():
    """Three repairs, then the paint tag.
    - Importers duplicate a material per use ("tyre.001", "tyre.002") and sometimes only one copy keeps its image: the
      Raptor's tyres came out white on three wheels. Faces on an untextured copy move to a textured one of the same
      base name.
    - GTA ports leave paint slots in placeholder magenta ("__PAINT_2_" rims): anything not tagged as paint is dark
      gunmetal instead.
    - --paint REGEX names the body paint: those materials get " [paint]", which is all a respray recolours
      (CarFactory.Paint); a car's other materials (lights, trim, interior, badges) never change. --paint-colour r,g,b
      sets its factory colour, for sources that ship white (NFS and GTA ports colour paint at runtime)."""
    def base(name):
        return re.sub(r"\.\d{3}$", "", name)

    def textured(m):
        p = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if m.node_tree else None
        return p is not None and p.inputs["Base Color"].links and p.inputs["Base Color"].links[0].from_node.type == "TEX_IMAGE"

    with_image = {}
    for m in bpy.data.materials:
        if textured(m):
            with_image.setdefault(base(m.name), m)
    for o in meshes:
        for slot in o.material_slots:
            m = slot.material
            # Blender's default names ("Material.001", "Material.049") are different materials, not copies.
            if m is not None and not textured(m) and base(m.name) in with_image and base(m.name).lower() != "material":
                slot.material = with_image[base(m.name)]

    paint = re.compile(opt["--paint"], re.I) if "--paint" in opt else None
    colour = tuple(float(c) for c in opt["--paint-colour"].split(",")) if "--paint-colour" in opt else None
    for m in bpy.data.materials:
        p = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if m.node_tree else None
        if paint is not None and paint.search(m.name) and "glass" not in m.name.lower():
            m.name += " [paint]"
            if colour is not None:
                # Older shaders (no Principled node) export their viewport colour.
                m.diffuse_color = colour + (1.0,)
            if colour is not None and p is not None:
                for link in list(p.inputs["Base Color"].links):
                    m.node_tree.links.remove(link)
                p.inputs["Base Color"].default_value = colour + (1.0,)
        elif "__paint_" in m.name.lower() and p is not None and not p.inputs["Base Color"].links:
            p.inputs["Base Color"].default_value = (0.12, 0.12, 0.13, 1.0)


fix_materials()


def split_loose(o):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    return list(bpy.context.selected_objects)


def roundish(d):
    """Side view (y, z) about square, narrower across (x), sized like a wheel, sitting low."""
    return (abs(d.y - d.z) < 0.15 * max(d.y, d.z) and d.x < 0.9 * max(d.y, d.z)
            and 0.08 * car.y < max(d.y, d.z) < 0.3 * car.y)


def by_shape(parts):
    found = []
    for o in list(parts):
        d, c = size([o]), centre([o])
        if c.z > 0.45 * car.z:
            continue
        # One part carrying an axle or all four wheels (low, wide): split into pieces and look again.
        low_and_wide = c.z + d.z / 2 < 0.55 * car.z and d.x > 0.6 * car.x and d.y > 0.08 * car.y
        if low_and_wide and not roundish(d):
            parts.remove(o)
            pieces = split_loose(o)
            parts.extend(pieces)
            found.extend(p for p in pieces if roundish(size([p])) and abs(centre([p]).x) > 0.2 * car.x)
            continue
        if roundish(d) and abs(c.x) > 0.2 * car.x and abs(c.y) > 0.15 * car.y:
            found.append(o)
    # Pieces inside a found wheel's box (spokes, nuts, discs) go with it.
    boxes = [bounds([w]) for w in found]
    for o in parts:
        if o in found or not_wheel.search(o.name):
            continue
        lo, hi = bounds([o])
        for blo, bhi in boxes:
            pad = 0.02 * car.y
            if all(lo[k] >= blo[k] - pad and hi[k] <= bhi[k] + pad for k in range(3)):
                found.append(o)
                break
    return found


wheel_parts = []
if wheel_opt != "auto":
    rx = re.compile(wheel_opt, re.I)
    wheel_parts = [o for o in meshes if rx.search(o.name) and not not_wheel.search(o.name)
                   and centre([o]).z < 0.45 * car.z and max(size([o])) < 0.35 * car.y]
if len(wheel_parts) < 4:
    wheel_parts = by_shape(meshes)
if "--debug" in opt:
    for o in wheel_parts:
        lo, hi = bounds([o])
        print("DBG wheel part", o.name, [round(v, 2) for v in lo], [round(v, 2) for v in hi])
body_parts = [o for o in meshes if o not in wheel_parts]
if len(wheel_parts) < 4:
    sys.exit(f"only {len(wheel_parts)} wheel parts found: pass --wheels REGEX")

# Which end is the front: named parts first, else --front, else the end whose wheels are further from the centre
# of the body (a long rear overhang is rarer than a long front one on the cars we get: guess, then check a render).
front_sign = None
if "--front" in opt:
    front_sign = -1 if opt["--front"].startswith("-") else 1
else:
    fronts = [o for o in body_parts if re.search(r"front|hood|bonnet|headl|grill|\.ft\b|_ft\b|vorn|motorhaube", o.name, re.I)]
    rears = [o for o in body_parts if re.search(r"rear|back|trunk|tail|\.bk\b|_bk\b|heck", o.name, re.I)]
    if fronts and rears:
        front_sign = 1 if centre(fronts).y > centre(rears).y else -1
if front_sign is None:
    front_sign = 1
    print("WARN front not detected from names; assumed +y: check the render and pass --front if it's backwards")

groups = {}
for o in wheel_parts:
    c = centre([o])
    groups.setdefault(("front" if c.y * front_sign > 0 else "back", c.x > 0), []).append(o)
if len(groups) != 4:
    sys.exit(f"wheel parts fell into {len(groups)} corners, expected 4")


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = o.data.name = name
    return o


body = join(body_parts, "body")
wheels = []
for (end, pos_x), parts in groups.items():
    # Blender +x ends up as Unity -x with this export, so a wheel at +x (front towards -y) is on the left.
    wheels.append(join(parts, f"wheel-{end}-" + ("left" if pos_x == (front_sign < 0) else "right")))

everything = [body] + wheels
# Front towards -Y (Blender's FBX export maps -Y to Unity's +z), ground at z 0, centred on the wheelbase.
if front_sign > 0:
    transform(everything, mathutils.Matrix.Rotation(3.14159265, 4, "Z"))
blo, bhi = bounds([body])
wlo, whi = bounds(wheels)
fy = centre([w for w in wheels if "front" in w.name]).y
by = centre([w for w in wheels if "back" in w.name]).y
shift = mathutils.Matrix.Translation(-Vec(((blo.x + bhi.x) / 2, (fy + by) / 2, wlo.z)))
transform(everything, mathutils.Matrix.Scale(length / FACTORY_SCALE / (bhi.y - blo.y), 4) @ shift)


def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


def decimate(o, target):
    """Collapse-decimate toward target triangles. Collapsing hundreds of tiny loose pieces (lug nuts, badges) can
    fling vertices far off and crumple thin shells, so the result is kept only if the part's bounds didn't grow;
    otherwise the ratio is relaxed and retried, and finally the original is kept."""
    t = tris([o])
    if t <= target:
        return
    original = o.data.copy()
    lo, hi = bounds([o])
    tolerance = 0.02 * (hi - lo).length
    for ratio in (max(0.02, target / t), max(0.1, target / t), 0.3):
        mod = o.modifiers.new("decimate", "DECIMATE")
        mod.ratio = ratio
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
        nlo, nhi = bounds([o])
        if (nlo - lo).length < tolerance and (nhi - hi).length < tolerance:
            return
        print(f"WARN decimating {o.name} to {ratio:.2f} moved its bounds; retrying gentler")
        o.data = original.copy()
    print(f"WARN {o.name} kept at full detail ({t} tris)")


before = tris(everything)
# Wheels get a fixed budget each (round silhouettes show faceting first, but 100k-triangle tyres are absurd);
# the body gets the rest.
# --max-tris 0 keeps the source mesh untouched: detailed cars you buy and drive are seen up close, and Unity's mesh
# LODs (ArtImportRules) lighten them with distance instead.
# --wheel-tris caps each wheel on its own: subdivided tyres (the Urus' were ~900k triangles each) add nothing visible
# and a mesh that size overflows the D3D12 upload buffer.
if max_tris > 0 or "--wheel-tris" in opt:
    for w in wheels:
        decimate(w, int(opt.get("--wheel-tris", min(6000, max_tris // 10))))
if max_tris > 0:
    decimate(body, max_tris - tris(wheels))


# One UV map and no colour layers: materials take one colour texture and vertex colours were folded into them
# (flatten_materials). Spare layers cost vertex size: the Aventador's 100 bytes a vertex put its body over the D3D12
# upload buffer.
for o in everything:
    uv = o.data.uv_layers
    keep = next((l for l in uv if l.active_render), uv.active)
    for l in [l for l in uv if l != keep]:
        uv.remove(l)
    for a in list(o.data.color_attributes):
        o.data.color_attributes.remove(a)

tex = os.path.join(os.path.dirname(out), "Textures")
os.makedirs(tex, exist_ok=True)
for img in bpy.data.images:
    if img.size[0] == 0 or img.source not in ("FILE", "GENERATED"):
        continue
    name = re.sub(r"[^A-Za-z0-9_.-]", "_", img.name)
    if not name.lower().endswith(".png"):
        name += ".png"
    try:
        img.filepath_raw = os.path.join(tex, name)
        img.file_format = "PNG"
        img.save()
        img.filepath = img.filepath_raw
    except Exception as e:
        print("WARN texture", img.name, e)

# Each wheel's pivot at its own centre: wheel-collider controllers tell front from rear (and left from right) by
# the wheel transforms' positions, not their meshes.
for w in wheels:
    c = centre([w])
    w.data.transform(mathutils.Matrix.Translation(-c))
    w.location = c

root = bpy.data.objects.new(os.path.splitext(os.path.basename(out))[0], None)
scene.collection.objects.link(root)
for o in everything:
    o.parent = root
bpy.ops.object.select_all(action="DESELECT")
for o in [root] + everything:
    o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"EMPTY", "MESH"},
                         apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
                         path_mode="RELATIVE", embed_textures=False, mesh_smooth_type="OFF", add_leaf_bones=False)
blo, bhi = bounds([body])
print("CAR", os.path.basename(out), "tris", before, "->", tris(everything),
      "body", tuple(round(v * FACTORY_SCALE, 2) for v in (bhi - blo)), "m in game;",
      {w.name: len(groups[k]) for w, k in zip(wheels, groups)}, "parts per wheel")
