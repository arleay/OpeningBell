"""Blender: turn Quaternius GLBs from poly.pizza (CC0) into FBX models laid out the way the game expects.
The GLBs aren't kept in the repo: download each id below from https://poly.pizza/m/<id> into <glb dir>/pp or
<glb dir>/pp2 as <tag>_<id>.glb (e.g. pp/pine_699sFuLCN2.glb).

Nature: one FBX per plant, origin at the base centre, scaled to exactly 1 unit tall (placement code then scales
by height in metres). Cars: Kenney car-kit layout (root with children body + wheel-front-left/-right +
wheel-back-left/-right, front towards Unity's +z) so CarFactory drives them unchanged.
    blender -b -P Tools/Blender/convert_quaternius.py -- <glb dir> <project>"""
import bpy, bmesh, sys, os, mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
S, P = argv[0], argv[1]
NATURE = os.path.join(P, "Assets", "Art", "ThirdParty", "Quaternius", "Nature")
CARS = os.path.join(P, "Assets", "Art", "ThirdParty", "Quaternius", "Cars")
TEX = os.path.join(NATURE, "Textures")
for d in (NATURE, CARS, TEX):
    os.makedirs(d, exist_ok=True)

# (glb, object in the file, exported name)
PLANTS = [
    ("pp/pine_699sFuLCN2", "Pine_3", "q_pine_a"),
    ("pp/pine_79gmlLnweB", "Pine_4", "q_pine_b"),
    ("pp/pine_Zt62gceKXZ", "Pine_2", "q_pine_c"),
    ("pp/tree_etFGNvsiFv", "NormalTree_1", "q_tree_a"),
    ("pp/tree_etFGNvsiFv", "NormalTree_2", "q_tree_b"),
    ("pp/tree_etFGNvsiFv", "NormalTree_3", "q_tree_c"),
    ("pp/tree_etFGNvsiFv", "NormalTree_4", "q_tree_d"),
    ("pp/tree_etFGNvsiFv", "NormalTree_5", "q_tree_e"),
    ("pp/tree_qZtx0AHhcy", "CommonTree_1", "q_tree_common"),
    ("pp2/bush_tX1aT9IB1P", "Bush_2", "q_bush_leafy"),
    ("pp2/bush_U1ymDy8tbY", "Bush_Common_Flowers", "q_bush_flowers"),
    ("pp2/bush_J2h3HrO356", "Bush", "q_bush_small"),
    ("pp2/bush_J2h3HrO356", "Bush_Flowers", "q_bush_small_flowers"),
    ("pp2/bush_J2h3HrO356", "Plant_1", "q_plant"),
    ("pp2/fern_jqcanvH7D6", "Fern_1", "q_fern"),
    ("pp2/grass_JSIYtscPmP", "Grass_Common_Tall", "q_grass_tall"),
]

# (glb, exported name). Kenney names so VehicleLibrary can point the same entries at them.
VEHICLES = [
    ("pp/sedan_Cz6yDaUcM9", "sedan"),
    ("pp2/taxi_x43lOScTpN", "taxi"),
    ("pp/car_BwwnUrWGmV", "police"),
    ("pp/sedan_xsMtZhBkxL", "suv"),
    ("pp/sedan_unqqkULtRU", "hatchback-sports"),
    ("pp/car_1mkmFkAz5v", "sedan-sports"),
    ("pp/suv_qn4grQgHm8", "truck"),
]
# CarFactory scales every car model by 1.35 (Kenney's cars are small); Quaternius cars are 4.2 m real size.
# Target ~4.0 m long in game: 4.0 / (4.22 * 1.35).
CAR_SCALE = 0.70

report = []


def load(glb):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(S, glb + ".glb"))


def world_bounds(objs):
    lo = mathutils.Vector((1e9,) * 3); hi = -lo
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = mathutils.Vector(map(min, lo, w)); hi = mathutils.Vector(map(max, hi, w))
    return lo, hi


def bake(o):
    """Unparent keeping the world transform and apply it into the mesh."""
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
    o.data.transform(mw)
    o.matrix_world = mathutils.Matrix.Identity(4)


def save_images():
    for img in bpy.data.images:
        if img.name == "Render Result" or img.size[0] == 0:
            continue
        name = img.name if img.name.lower().endswith(".png") else img.name + ".png"
        path = os.path.join(TEX, name)
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        img.filepath = path


def export(path, objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
                             path_mode="RELATIVE", embed_textures=False, mesh_smooth_type="FACE",
                             add_leaf_bones=False)


for glb, obj_name, out in PLANTS:
    load(glb)
    root = bpy.data.objects[obj_name]
    meshes = [o for o in [root] + list(root.children_recursive) if o.type == "MESH"]
    for o in meshes:
        bake(o)
    for o in list(bpy.context.scene.objects):
        if o not in meshes:
            bpy.data.objects.remove(o)
    if len(meshes) > 1:
        bpy.context.view_layer.objects.active = meshes[0]
        for o in meshes:
            o.select_set(True)
        bpy.ops.object.join()
    m = bpy.context.scene.objects[0] if len(meshes) > 1 else meshes[0]
    m.name = out
    lo, hi = world_bounds([m])
    height = hi.z - lo.z
    base = mathutils.Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    # Base centre to the origin and 1 unit tall: placement scales by the height it wants in metres.
    m.data.transform(mathutils.Matrix.Scale(1 / height, 4) @ mathutils.Matrix.Translation(-base))
    m.data.update()
    save_images()
    export(os.path.join(NATURE, out + ".fbx"), [m])
    tris = sum(len(p.vertices) - 2 for p in m.data.polygons)
    report.append(f"{out}: native height {height:.2f} m, width/height {(max(hi.x - lo.x, hi.y - lo.y)) / height:.2f}, tris {tris}, mats {[s.material.name for s in m.material_slots]}")

for glb, out in VEHICLES:
    load(glb)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:
        bake(o)
    body = next(o for o in meshes if "Wheel" not in o.name)
    front = [o for o in meshes if "Front" in o.name and "Wheel" in o.name]
    back = next(o for o in meshes if "BackWheels" in o.name)
    # The rear axle is one mesh with both wheels: split it into its loose parts, then pair by side.
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = back
    back.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    backs = [o for o in bpy.context.selected_objects]
    wheels = front + backs

    def centre(o):
        lo, hi = world_bounds([o]); return (lo + hi) / 2

    # Left/right halves of the rear parts (a wheel may be several loose pieces: tyre, rim).
    groups = [[o for o in backs if centre(o).x * side > 0] for side in (-1, 1)]
    for parts in groups:
        if len(parts) > 1:
            bpy.ops.object.select_all(action="DESELECT")
            for o in parts:
                o.select_set(True)
            bpy.context.view_layer.objects.active = parts[0]
            bpy.ops.object.join()
    backs = [o for o in bpy.context.scene.objects if o.type == "MESH" and o is not body and o not in front]
    lo, hi = world_bounds([body])
    fy = sum(centre(o).y for o in front) / len(front)
    by = sum(centre(o).y for o in backs) / len(backs)
    # Ground at z 0; centred on the wheelbase midpoint.
    # Front towards -Y: Blender's FBX export maps -Y to Unity's +z (checked by VehicleTests.CarMeshes_HaveTheCarKitLayout).
    turn = mathutils.Matrix.Rotation(3.14159265 if fy > by else 0, 4, "Z")
    wl, wh = world_bounds(front + backs)
    shift = mathutils.Matrix.Translation(-mathutils.Vector(((lo.x + hi.x) / 2, (fy + by) / 2, wl.z)))
    xf = mathutils.Matrix.Scale(CAR_SCALE, 4) @ turn @ shift
    for o in [body] + front + backs:
        o.data.transform(xf)
        o.data.update()
    body.name = "body"
    for o in front + backs:
        c = centre(o)
        o.name = ("wheel-front-" if c.y < 0 else "wheel-back-") + ("left" if c.x > 0 else "right")
    rootobj = bpy.data.objects.new(out, None)
    bpy.context.scene.collection.objects.link(rootobj)
    for o in [body] + front + backs:
        o.parent = rootobj
    export(os.path.join(CARS, out + ".fbx"), [rootobj, body] + front + backs)
    lo, hi = world_bounds([body])
    report.append(f"car {out}: {[o.name for o in rootobj.children]} body {tuple(round(v, 2) for v in (hi - lo))}")

open(os.path.join(S, "convert_report.txt"), "w").write("\n".join(report))
print("\n".join(report))
