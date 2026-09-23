"""Turns a detailed car model (GLB/glTF/FBX from Sketchfab and the like) into the car-kit layout CarFactory
drives: a root with children "body" and "wheel-front-left/-right", "wheel-back-left/-right", front towards
Unity's +z, wheels on the ground, sized to a real length.

    blender -b -P Tools/Blender/convert_car.py -- <in.glb> <out.fbx> [--length 4.7] [--max-tris 60000]
        [--wheels REGEX] [--front +y|-y] [--scale 1.35]

Wheel parts are found by object name (--wheels, default tyres/rims/hubs/discs; brake calipers stay on the body
since they steer but don't spin) and grouped into four wheels by which corner they sit in. The front is
guessed from part names containing "front"/"rear" (override with --front). Everything else is joined into
the body, which is decimated if the whole car exceeds --max-tris. Textures go to Textures/ beside the FBX.
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
wheel_re = re.compile(opt.get("--wheels", r"tire|tyre|wheel|rim|hub|disk|disc"), re.I)
not_wheel = re.compile(r"brake|caliper|steering", re.I)

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = src.lower().rsplit(".", 1)[1]
if ext in ("glb", "gltf"):
    bpy.ops.import_scene.gltf(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
# Bake every transform into its mesh (shared meshes are copied first so each bake is independent).
for o in meshes:
    if o.data.users > 1:
        o.data = o.data.copy()
    mw = o.matrix_world.copy()
    o.parent = None
    o.data.transform(mw)
    o.matrix_world = mathutils.Matrix.Identity(4)
for o in list(bpy.context.scene.objects):
    if o.type != "MESH":
        bpy.data.objects.remove(o)


def bounds(objs):
    lo = mathutils.Vector((1e18,) * 3); hi = -lo
    for o in objs:
        for v in o.data.vertices:
            lo = mathutils.Vector(map(min, lo, v.co)); hi = mathutils.Vector(map(max, hi, v.co))
    return lo, hi


def centre(objs):
    lo, hi = bounds(objs)
    return (lo + hi) / 2


wheel_parts = [o for o in meshes if wheel_re.search(o.name) and not not_wheel.search(o.name)]
body_parts = [o for o in meshes if o not in wheel_parts]
if len(wheel_parts) < 4:
    sys.exit(f"only {len(wheel_parts)} wheel parts matched {wheel_re.pattern}: pass --wheels")

# Length runs along whichever horizontal axis the car is longer on; turn it onto Y.
lo, hi = bounds(meshes)
if hi.x - lo.x > hi.y - lo.y:
    turn = mathutils.Matrix.Rotation(1.5707963, 4, "Z")
    for o in meshes:
        o.data.transform(turn)

wc = [centre([o]) for o in wheel_parts]
mid_x = sum(c.x for c in wc) / len(wc)
mid_y = sum(c.y for c in wc) / len(wc)

# Which end is the front: named parts first, else --front.
front_sign = None
if "--front" in opt:
    front_sign = -1 if opt["--front"].startswith("-") else 1
else:
    fronts = [o for o in body_parts if re.search(r"front|hood|bonnet|headl", o.name, re.I)]
    rears = [o for o in body_parts if re.search(r"rear|back|trunk|tail", o.name, re.I)]
    if fronts and rears:
        front_sign = 1 if centre(fronts).y > centre(rears).y else -1
if front_sign is None:
    sys.exit("can't tell the front from part names: pass --front +y or -y")

groups = {}
for o, c in zip(wheel_parts, wc):
    key = ("front" if (c.y - mid_y) * front_sign > 0 else "back", c.x - mid_x)
    groups.setdefault((key[0], key[1] > 0), []).append(o)
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
    # (checked against CarFactory's convention by VehicleTests only for facing; sides only label the pivots)

everything = [body] + wheels
# Front towards -Y (Blender's FBX export maps -Y to Unity's +z), ground at z 0, centred on the wheelbase.
if front_sign > 0:
    rot = mathutils.Matrix.Rotation(3.14159265, 4, "Z")
    for o in everything:
        o.data.transform(rot)
blo, bhi = bounds([body])
wlo, whi = bounds(wheels)
fy = centre([w for w in wheels if "front" in w.name]).y
by = centre([w for w in wheels if "back" in w.name]).y
shift = mathutils.Matrix.Translation(-mathutils.Vector(((blo.x + bhi.x) / 2, (fy + by) / 2, wlo.z)))
scale = mathutils.Matrix.Scale(length / FACTORY_SCALE / (bhi.y - blo.y), 4)
for o in everything:
    o.data.transform(scale @ shift)
    o.data.update()


def tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)


before = tris(everything)
if before > max_tris:
    # Only the body is simplified: wheels are small on screen but round silhouettes show faceting first.
    keep = max(0.1, (max_tris - tris(wheels)) / tris([body]))
    mod = body.modifiers.new("decimate", "DECIMATE")
    mod.ratio = keep
    mod.use_collapse_triangulate = False
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=mod.name)

tex = os.path.join(os.path.dirname(out), "Textures")
os.makedirs(tex, exist_ok=True)
for img in bpy.data.images:
    if img.size[0] == 0:
        continue
    name = re.sub(r"[^A-Za-z0-9_.-]", "_", img.name)
    if not name.lower().endswith(".png"):
        name += ".png"
    img.filepath_raw = os.path.join(tex, name)
    img.file_format = "PNG"
    img.save()
    img.filepath = img.filepath_raw

# Each wheel's pivot at its own centre: wheel-collider controllers tell front from rear (and left from right) by the
# wheel transforms' positions, not their meshes.
for w in wheels:
    lo, hi = bounds([w])
    c = (lo + hi) / 2
    w.data.transform(mathutils.Matrix.Translation(-c))
    w.location = c

root = bpy.data.objects.new(os.path.splitext(os.path.basename(out))[0], None)
bpy.context.scene.collection.objects.link(root)
for o in everything:
    o.parent = root
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"EMPTY", "MESH"},
                         apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
                         path_mode="RELATIVE", embed_textures=False, mesh_smooth_type="OFF", add_leaf_bones=False)
blo, bhi = bounds([body])
print("CAR", os.path.basename(out), "tris", before, "->", tris(everything),
      "body", tuple(round(v * FACTORY_SCALE, 2) for v in (bhi - blo)), "m in game;",
      {w.name: len(groups[k]) for w, k in zip(wheels, groups)}, "parts per wheel")
