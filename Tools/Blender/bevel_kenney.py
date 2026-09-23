"""Softens the Kenney kits' hard box edges: bevels every sharp edge and hardens the normals, so big faces stay
flat while their corners catch light like Schedule I's props. Run once on the original kit files:

    blender -b -P Tools/Blender/bevel_kenney.py -- <width> <file.fbx> [<file.fbx> ...]

<width> is the bevel in the model's own units (kit buildings are placed at 7.4x, so 0.007 is ~5 cm in game;
furniture is modelled ~5.7 units per metre, so 0.09 is ~1.5 cm). Files are overwritten in place; each one is
listed in Tools/Blender/bevelled.txt and skipped on later runs, so a re-run never bevels twice.

Kenney kits colour faces by sampling a palette texture: a bevel face's interpolated UVs would cross into
neighbouring swatches, so each new face takes the UV point and material of the flat face it grew from.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
DONE = os.path.join(HERE, "bevelled.txt")
SHARP = math.radians(30)   # only edges folded more than this get a bevel (leaves curved surfaces alone)
SEGMENTS = 2

argv = sys.argv[sys.argv.index("--") + 1:]
width = float(argv[0])
files = argv[1:]
done = set(open(DONE).read().split("\n")) if os.path.exists(DONE) else set()


def key(path):
    p = os.path.abspath(path).replace("\\", "/")
    return p[p.index("Assets/"):]


def soften(me, offset):
    bm = bmesh.new()
    bm.from_mesh(me)
    # Kit meshes split vertices along hard edges; weld them so the bevel sees connected corners.
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=offset * 0.01)
    edges = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > SHARP]
    if not edges:
        bm.free()
        return 0
    new = bmesh.ops.bevel(bm, geom=edges, offset=offset, offset_type="OFFSET", segments=SEGMENTS, profile=0.5,
                          affect="EDGES", clamp_overlap=True, loop_slide=True)["faces"]
    new = set(new)
    # The bevel rebuilds the faces it cuts into, so "flat" is everything it didn't create, looked up afterwards.
    flat = {f for f in bm.faces if f not in new}
    uv = bm.loops.layers.uv.active
    # Flood each new face from its flat neighbours: largest neighbour first, then strip faces from each other.
    source = {}
    for f in flat:
        c = Vector((0, 0))
        if uv:
            for l in f.loops:
                c += l[uv].uv
            c /= len(f.loops)
        source[f] = (c, f.material_index, f.normal.copy())
    pending = [f for f in new if f.is_valid]
    while pending:
        rest = []
        for f in pending:
            best = None
            for e in f.edges:
                for g in e.link_faces:
                    if g is not f and g in source and (best is None or g.calc_area() > best.calc_area()):
                        best = g
            if best is None:
                rest.append(f)
                continue
            c, mat, _ = source[best]
            f.material_index = mat
            if uv:
                for l in f.loops:
                    l[uv].uv = c
            source[f] = (c, mat, None)
        if len(rest) == len(pending):
            break
        pending = rest
    bm.normal_update()
    # Hardened normals: a flat face keeps its face normal at every corner; bevel faces average only bevel faces
    # around each vertex, so the rounding reads smooth and the big faces don't shade into gradients.
    # Per face, per corner (bm.to_mesh keeps face order and each face's corner order, not bmesh loop indices).
    face_normals = []
    for f in bm.faces:
        corners = []
        for l in f.loops:
            if f in flat:
                corners.append(f.normal.copy())
            else:
                n = Vector()
                for g in l.vert.link_faces:
                    if g not in flat:
                        n += g.normal
                corners.append(n.normalized() if n.length > 0 else f.normal.copy())
        face_normals.append(corners)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me)
    bm.free()
    normals = [None] * len(me.loops)
    for poly, corners in zip(me.polygons, face_normals):
        for li, n in zip(poly.loop_indices, corners):
            normals[li] = n
    me.normals_split_custom_set(normals)
    return len(edges)


for path in files:
    k = key(path)
    if k in done:
        print("skip (already bevelled)", k)
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    seen = set()
    count = 0
    for o in bpy.context.scene.objects:
        if o.type != "MESH" or o.data in seen:
            continue
        seen.add(o.data)
        scale = max(abs(v) for v in o.matrix_world.to_scale())
        count += soften(o.data, width / scale)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={"EMPTY", "MESH"},
                             apply_scale_options="FBX_SCALE_NONE", bake_space_transform=False,
                             mesh_smooth_type="OFF", use_custom_props=False, add_leaf_bones=False,
                             path_mode="STRIP", embed_textures=False)
    done.add(k)
    open(DONE, "w").write("\n".join(sorted(d for d in done if d)))
    print("bevelled", k, count, "edges")
