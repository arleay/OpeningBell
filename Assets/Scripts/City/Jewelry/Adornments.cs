using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Puts jewellery on a humanoid character: measures the body where each piece goes (wrist, finger line, neck,
    /// chest, eyes, ears) from its skinned mesh, builds the piece to that size (<see cref="Jewelry.Build"/>) and hangs
    /// it on the bone that carries that part, so it moves with the arm or head. Works on any rig the humanoid avatar
    /// maps.
    /// <para>
    /// Measured and attached in the animated idle pose, not the model's imported rest pose: some rigs (the Tiny set's)
    /// turn bones when the humanoid animator takes over, which would carry anything parented in the rest pose round
    /// with them (a chain hanging down the back). The animator is evaluated once first for that reason.
    /// </para>
    /// </summary>
    public static class Adornments
    {
        private const string Holder = "Jewellery";

        /// <summary>Replaces whatever the character wears with <paramref name="worn"/> (catalog ids).</summary>
        public static void Apply(CityContext c, GameObject model, IEnumerable<string> worn)
        {
            if (c == null || model == null) return;
            Animator anim = model.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return;
            // Remove what's on already (each piece sits under its bone in a group named Holder).
            var old = new List<GameObject>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) if (t.name == Holder) old.Add(t.gameObject);
            // Detached first: Destroy is deferred, and a leftover child would be mistaken for a bone's end below.
            foreach (GameObject g in old) { g.transform.SetParent(null); Object.Destroy(g); }
            if (worn == null) return;
            if (anim.runtimeAnimatorController != null && anim.isActiveAndEnabled) anim.Update(0f);
            Body body = null;
            foreach (string id in worn)
            {
                JewelryItem item = Jewelry.Get(id);
                if (item == null) continue;
                body ??= new Body(model, anim);
                try { Attach(c, body, item); }
                catch (System.Exception e) { Debug.LogWarning($"Jewellery {id} on {model.name}: {e.Message}"); }
            }
        }

        private static void Attach(CityContext c, Body b, JewelryItem item)
        {
            Transform model = b.Model.transform;
            Vector3 up = model.up, fwd = model.forward;
            switch (item.Slot)
            {
                case JewelrySlot.Watch:
                case JewelrySlot.Bracelet:
                {
                    // Left wrist for the watch, right for bracelets: just short of the hand, on the forearm.
                    bool left = item.Slot == JewelrySlot.Watch;
                    Transform lower = b.Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                    Transform hand = b.Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    if (lower == null || hand == null) return;
                    Vector3 along = (hand.position - lower.position).normalized;
                    Vector3 at = Vector3.Lerp(lower.position, hand.position, 0.8f);
                    // Measured a little up the forearm, where it's all wrist (right at the joint the mitt's cuff bulges).
                    float r = b.Radius(Vector3.Lerp(lower.position, hand.position, 0.7f), along, 0.025f * b.Scale, 0.2f * b.Scale, 0.6f) * 1.08f;
                    Vector3 back = b.Outward(at, along);
                    // On the hand bone for the watch: reading it (PlayerBody's IK) turns the hand, and the forearm only
                    // partly follows, so the dial comes round with the hand. The IK keeps the wrist straight, so the strap
                    // stays round the arm.
                    Place(c, item, item.Slot == JewelrySlot.Watch ? hand : lower, at, along, back, r);
                    break;
                }
                case JewelrySlot.Ring:
                {
                    // A band round a notional finger along the top of the fist, just behind the knuckles: its top half
                    // and the stone show, the rest is inside the hand. Finger radius ~ a fifth of the fist's.
                    Transform hand = b.Bone(HumanBodyBones.RightHand);
                    Transform lower = b.Bone(HumanBodyBones.RightLowerArm);
                    if (hand == null || lower == null) return;
                    Vector3 along = (hand.position - lower.position).normalized;
                    Vector3 end = hand.childCount > 0 ? hand.GetChild(0).position : hand.position + along * (Vector3.Distance(hand.position, lower.position) * 0.6f);
                    Vector3 mid = Vector3.Lerp(hand.position, end, 0.62f);
                    float fist = b.Radius(mid, along, 0.03f * b.Scale, 0.3f * b.Scale);
                    Vector3 back = b.Outward(mid, along);
                    float finger = fist * 0.24f;
                    Vector3 at = mid + back * (fist * 0.9f - finger);
                    Place(c, item, hand, at, along, back, finger);
                    break;
                }
                case JewelrySlot.Necklace:
                {
                    Transform neck = b.Bone(HumanBodyBones.Neck) ?? b.Bone(HumanBodyBones.Head);
                    Transform chest = b.Bone(HumanBodyBones.UpperChest) ?? b.Bone(HumanBodyBones.Chest) ?? b.Bone(HumanBodyBones.Spine);
                    if (neck == null || chest == null) return;
                    Vector3 at = neck.position;
                    float r = b.Radius(at, up, 0.025f * b.Scale, 0.35f * b.Scale, 0.5f);
                    // Drapes from the back of the neck down to the upper chest, lying on the body all the way round, and
                    // rides on the chest bone (it rests on the torso, not the neck).
                    float drop = Mathf.Max(0.05f * b.Scale, Vector3.Dot(at - chest.position, up) * 0.85f);
                    Transform holder = Group(chest, at, up, fwd);
                    List<Vector3> path = b.Drape(at, up, fwd, drop, r * 0.12f, 96);
                    for (int i = 0; i < path.Count; i++) path[i] = holder.InverseTransformPoint(path[i]);
                    Finish(Jewelry.Build(c, holder, item, r, drop, r, path));
                    break;
                }
                case JewelrySlot.Glasses:
                case JewelrySlot.Earrings:
                {
                    Transform head = b.Bone(HumanBodyBones.Head);
                    if (head == null) return;
                    b.Face(head, up, fwd, out Vector3 eyes, out float spacing, out float halfWidth, out float frontDepth);
                    if (item.Slot == JewelrySlot.Glasses)
                    {
                        // On the face's surface between the eyes; temples reach back to about the middle of the head.
                        Transform holder = Group(head, eyes, up, fwd);
                        Finish(Jewelry.Build(c, holder, item, spacing, 0f, frontDepth * 0.95f));
                    }
                    else
                    {
                        // Lobes: at the sides of the head, a little below eye level, halfway back.
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Vector3 right = Vector3.Cross(up, fwd);
                            Vector3 lobe = eyes - fwd * (frontDepth * 0.95f) + right * (side * halfWidth * 1.01f) - up * (spacing * 0.35f);
                            Transform holder = Group(head, lobe, up, side > 0 ? right : -right);
                            // Earring frame: +x outward; Group points +z along the given forward, so turn it to +x.
                            holder.rotation = Quaternion.LookRotation(Vector3.Cross(side > 0 ? right : -right, up), up);
                            Finish(Jewelry.Build(c, holder, item, spacing * 0.12f));
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>A wrist or finger piece: frame +x along the limb, +y the back of it.</summary>
        private static void Place(CityContext c, JewelryItem item, Transform bone, Vector3 at, Vector3 along, Vector3 back, float radius)
        {
            var holder = new GameObject(Holder).transform;
            holder.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.Cross(along, back), back));
            holder.SetParent(bone, true);
            Finish(Jewelry.Build(c, holder, item, radius));
        }

        private static Transform Group(Transform bone, Vector3 at, Vector3 up, Vector3 fwd)
        {
            var holder = new GameObject(Holder).transform;
            holder.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, up));
            holder.SetParent(bone, true);
            return holder;
        }

        /// <summary>Whatever layer the character is on, its jewellery is too (mirrors and the first-person split rely on it).</summary>
        private static void Finish(GameObject piece)
        {
            if (piece == null) return;
            int layer = piece.transform.root.gameObject.layer;
            Transform p = piece.transform.parent;
            while (p != null) { layer = p.gameObject.layer; if (p.GetComponent<Animator>() != null) break; p = p.parent; }
            foreach (Transform t in piece.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>The character's skin, baked once in its rest pose, for measuring.</summary>
        private sealed class Body
        {
            public readonly GameObject Model;
            private readonly Animator _anim;
            private readonly List<Vector3> _points = new List<Vector3>();
            private readonly List<(Vector3 Min, Vector3 Max)> _faces = new List<(Vector3, Vector3)>();
            /// <summary>World size of the model relative to a 1.8 m person: tolerances scale with it.</summary>
            public readonly float Scale;

            public Body(GameObject model, Animator anim)
            {
                Model = model;
                _anim = anim;
                var baked = new Mesh();
                foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    r.BakeMesh(baked, true);
                    Transform t = r.transform;
                    var verts = baked.vertices;
                    foreach (Vector3 v in verts) _points.Add(t.position + t.rotation * v);
                    // Face material (the Quaternius sets paint the eyes and mouth on it): where the face is.
                    Material[] mats = r.sharedMaterials;
                    for (int s = 0; s < baked.subMeshCount && s < mats.Length; s++)
                    {
                        if (mats[s] == null || mats[s].name.IndexOf("Face", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var tris = baked.GetTriangles(s);
                        if (tris.Length == 0) continue;
                        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                        foreach (int i in tris)
                        {
                            Vector3 w = t.position + t.rotation * verts[i];
                            min = Vector3.Min(min, w);
                            max = Vector3.Max(max, w);
                        }
                        _faces.Add((min, max));
                    }
                }
                Object.Destroy(baked);
                Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
                float height = head != null ? Vector3.Dot(head.position - model.transform.position, model.transform.up) : 1.6f;
                Scale = Mathf.Max(0.2f, height / 1.5f);
            }

            public Transform Bone(HumanBodyBones b) => _anim.GetBoneTransform(b);

            /// <summary>
            /// The back of a hand or wrist in the idle pose: arms hang at the sides with the palms in, so the back of the
            /// hand faces away from the body's centre line. Square to the limb's <paramref name="along"/>.
            /// </summary>
            public Vector3 Outward(Vector3 at, Vector3 along)
            {
                Transform root = Model.transform;
                Vector3 axis = root.position + root.up * Vector3.Dot(at - root.position, root.up);
                Vector3 side = Vector3.ProjectOnPlane(at - axis, along);
                return side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.ProjectOnPlane(root.right, along).normalized;
            }

            /// <summary>
            /// The limb's radius at <paramref name="at"/>: of the skin within <paramref name="band"/> of that cross-section
            /// and <paramref name="maxR"/> of the axis, the 85th-percentile distance (a stray vertex from a cuff or glove
            /// doesn't set the size).
            /// </summary>
            public float Radius(Vector3 at, Vector3 axis, float band, float maxR, float percentile = 0.85f)
            {
                var d = new List<float>();
                foreach (Vector3 p in _points)
                {
                    Vector3 rel = p - at;
                    float along = Vector3.Dot(rel, axis);
                    if (Mathf.Abs(along) > band) continue;
                    float r = (rel - axis * along).magnitude;
                    if (r < maxR) d.Add(r);
                }
                if (d.Count < 3) return maxR * 0.3f;
                d.Sort();
                return d[Mathf.Min(d.Count - 1, (int)(d.Count * percentile))];
            }

            /// <summary>
            /// How far out along <paramref name="dir"/> from <paramref name="origin"/> the skin reaches: the furthest
            /// vertex within <paramref name="band"/> of that ray, or -1 if none.
            /// </summary>
            public float Surface(Vector3 origin, Vector3 dir, float band)
            {
                float best = -1f;
                foreach (Vector3 p in _points)
                {
                    Vector3 rel = p - origin;
                    float along = Vector3.Dot(rel, dir);
                    if (along <= 0f || (rel - dir * along).sqrMagnitude > band * band) continue;
                    best = Mathf.Max(best, along);
                }
                return best;
            }

            /// <summary>
            /// A chain's loop lying on the body, as a smooth U: round the back of the neck at <paramref name="neck"/>,
            /// down the sides of the neck, to hang <paramref name="drop"/> lower on the centre of the chest. Fitted to
            /// three measurements (the back of the neck, its sides a little lower, the chest at the drop), not to every
            /// facet, which kinked it round the shoulders. World space, front-most point first.
            /// </summary>
            public List<Vector3> Drape(Vector3 neck, Vector3 up, Vector3 fwd, float drop, float thickness, int points)
            {
                Vector3 right = Vector3.Cross(up, fwd);
                float r = Radius(neck, up, 0.03f * Scale, 0.35f * Scale, 0.5f);
                float Reach(Vector3 from, Vector3 dir, float fallback)
                {
                    float s = Surface(from, dir, 0.035f * Scale);
                    return s > 0f ? s : fallback;
                }
                float back = Mathf.Clamp(Reach(neck, -fwd, r), r * 0.7f, r * 1.4f);
                Vector3 sideAt = neck - up * (drop * 0.3f);
                float sides = Mathf.Clamp(Mathf.Max(Reach(sideAt, right, r), Reach(sideAt, -right, r)), r * 0.9f, r * 1.5f);
                float front = Mathf.Clamp(Reach(neck - up * drop, fwd, r * 1.2f), r * 0.8f, r * 3f);
                var path = new List<Vector3>(points);
                for (int i = 0; i < points; i++)
                {
                    float a = i / (float)points * Mathf.PI * 2f; // 0 at the front
                    float c = Mathf.Cos(a);
                    float sag = (c + 1f) / 2f;
                    sag *= sag;
                    float z = c >= 0f ? c * Mathf.Lerp(r, front, sag) : c * back;
                    Vector3 radial = right * (Mathf.Sin(a) * sides) + fwd * z;
                    path.Add(neck + radial + radial.normalized * thickness - up * (drop * sag));
                }
                return path;
            }

            /// <summary>
            /// The eyes' centre on the face's surface, their spacing, the head's half-width at eye height and how far the
            /// face is in front of the head's axis. From the face material if there is one, else head proportions.
            /// </summary>
            public void Face(Transform head, Vector3 up, Vector3 fwd, out Vector3 eyes, out float spacing, out float halfWidth, out float frontDepth)
            {
                Vector3 right = Vector3.Cross(up, fwd);
                Vector3 top = head.childCount > 0 ? head.GetChild(0).position : head.position + up * (0.25f * Scale);
                float headLen = Mathf.Max(0.1f * Scale, Vector3.Dot(top - head.position, up));
                if (_faces.Count > 0)
                {
                    Vector3 min = _faces[0].Min, max = _faces[0].Max;
                    // Eyes sit in the upper part of the painted face.
                    Vector3 centre = (min + max) / 2f;
                    float faceH = Vector3.Dot(max - min, up);
                    eyes = centre + up * (faceH * 0.18f);
                    spacing = Mathf.Abs(Vector3.Dot(max - min, right)) * 0.5f;
                }
                else
                {
                    eyes = head.position + up * (headLen * 0.45f);
                    spacing = headLen * 0.34f;
                }
                // Head width and the face's forward reach at eye height, from the skin.
                Vector3 axisPoint = head.position + up * Vector3.Dot(eyes - head.position, up);
                halfWidth = 0f;
                float front = 0f;
                foreach (Vector3 p in _points)
                {
                    Vector3 rel = p - axisPoint;
                    if (Mathf.Abs(Vector3.Dot(rel, up)) > 0.03f * Scale) continue;
                    float x = Mathf.Abs(Vector3.Dot(rel, right));
                    if (x < headLen * 1.2f) halfWidth = Mathf.Max(halfWidth, x);
                    if (x < spacing * 0.5f) front = Mathf.Max(front, Vector3.Dot(rel, fwd));
                }
                if (halfWidth <= 0f) halfWidth = headLen * 0.5f;
                if (front <= 0f) front = headLen * 0.6f;
                frontDepth = front;
                eyes = axisPoint + fwd * (front + 0.004f * Scale) + right * Vector3.Dot(eyes - axisPoint, right);
            }
        }
    }
}
