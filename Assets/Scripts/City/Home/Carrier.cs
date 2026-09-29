using System.Collections.Generic;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpeningBell.City
{
    /// <summary>
    /// Carrying and placing (TOWN_SPEC B). [E] on an item picks it up: small things in your hands, big ones
    /// out of view, at full pace either way. While carrying, a see-through copy shows where it would go, green if it can, red with the reason
    /// if not: [LMB] place, [R] turn 15° ([Shift]+[R] 5°), [G] grid snap on/off, [RMB] cancel (back where it was),
    /// [Ctrl]+[Z] undo the last move, [B] put it away in the home's storage. Things go down at home, in a vehicle's bed or at the store's pickup yard; boxes
    /// are unpacked when they're set down at home. Aiming at an item: [X] sells it (twice to confirm).
    /// Moving boxes: holding something, [LMB] on an open box packs it (any amount; last in, first out). Empty-handed,
    /// [LMB] on an open box takes the top thing out into your hands, [Q] folds the flaps shut, [LMB] on a closed box
    /// opens it, [F] throws an empty box away. [E] carries a box, or opens a closed one set down in another home.
    /// </summary>
    public sealed class Carrier : MonoBehaviour
    {
        public const float Reach = 4.5f;
        private const float Grid = 0.25f;

        /// <summary>Where the held item would go, and whether it can.</summary>
        public sealed class Target
        {
            public bool Valid;
            public string Why;
            public Vector3 At;
            public float Yaw;
            public Under Under;
            public ItemView Onto;
            public HomeSpec Home;
            public CargoBed Bed;
            public bool Indoors, Yard;
            public int MountOn;
        }

        private struct Record
        {
            public int Uid, Mounted, InBox, PackOrder;
            public ItemState State;
            public string Property, Vehicle;
            public Vector3 At;
            public float Yaw;
            public bool Boxed;
        }

        private HomeWorld _w;
        private FirstPersonController _player;
        private PlayerInteractor _interactor;
        private ItemView _held;
        private Vector3 _heldScale = Vector3.one;
        /// <summary>Camera-space centre of a held item, and the largest it's drawn (m): the bottom-right corner of the view.</summary>
        private static readonly Vector3 HeldAt = new Vector3(0.3f, -0.24f, 0.6f);
        private const float HeldSize = 0.22f;
        private Record? _from; // where the held item was picked up from
        private readonly Stack<Record> _undo = new Stack<Record>();
        private GameObject _ghost;
        private bool _ghostBoxed;
        private Material _ok, _bad;
        private float _yaw;
        private int _sellArmed;
        private float _sellUntil;

        public bool Holding => _held != null;
        public ItemView Held => _held;
        public bool Snap { get; private set; } = true;
        public Target Aim { get; private set; }

        public void Configure(HomeWorld world, FirstPersonController player, PlayerInteractor interactor)
        {
            _w = world;
            _player = player;
            _interactor = interactor;
            _ok = world.City.P.Glass(new Color(0.3f, 1f, 0.45f, 0.35f));
            _bad = world.City.P.Glass(new Color(1f, 0.3f, 0.25f, 0.35f));
        }

        private Transform View => _player.CameraPivot;

        // ---- picking up ----

        public void PickUp(ItemView v)
        {
            if (Holding || v == null) return;
            OwnedItem i = v.Item;
            _from = Snapshot(i);
            // Whatever it stood on or hung from, it's off it now.
            i.MountedOn = 0;
            i.State = ItemState.Carried;
            _w.Belongings.Touch();
            Hold(v);
        }

        /// <summary>Straight into your hands (a purchase at the counter): nowhere to put it back.</summary>
        public ItemView TakeNew(OwnedItem item)
        {
            item.State = ItemState.Carried;
            _w.Belongings.Touch();
            _from = null;
            ItemView v = _w.View(item.Uid) ?? _w.Show(item);
            Hold(v);
            return v;
        }

        public void Hold(ItemView v)
        {
            _held = v;
            v.Solid.enabled = false;
            SetLayer(v.transform, 2); // Ignore Raycast: the aim looks past what you're holding
            HomeItem spec = v.Spec;
            bool large = spec.Large && !v.Item.Boxed;
            // Big furniture is carried out of view (the preview shows where it's going); anything else sits small in
            // the bottom-right corner, shrunk to fit a HeldSize cube, so it never blocks where you're walking or aiming.
            _heldScale = v.transform.localScale;
            v.transform.SetParent(View, false);
            if (large) v.transform.localPosition = new Vector3(0f, -3f, 0f);
            else
            {
                float pad = v.Item.Boxed ? 0.08f : 0f;
                float h = spec.Height + pad;
                float k = Mathf.Min(1f, HeldSize / Mathf.Max(spec.Width + pad, h, spec.Depth + pad));
                v.transform.localScale = _heldScale * k;
                v.transform.localPosition = HeldAt - new Vector3(0f, h * k / 2f, 0f); // the pivot is the item's base
            }
            v.transform.localRotation = Quaternion.Euler(0f, -20f, 0f); // turned a little towards the middle of the view
            foreach (Renderer r in v.GetComponentsInChildren<Renderer>()) r.enabled = !large;
            _player.HandsFull = true;
            _yaw = _player.transform.eulerAngles.y + 180f; // facing you
            BuildGhost(v.Item.Boxed);
        }

        private void Release()
        {
            if (_held == null) return;
            _held.Solid.enabled = true;
            _held.transform.localScale = _heldScale;
            SetLayer(_held.transform, 0);
            foreach (Renderer r in _held.GetComponentsInChildren<Renderer>()) r.enabled = true;
            _held = null;
            _player.HandsFull = false;
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _w.City.Hud?.SetStatus(null);
        }

        private void BuildGhost(bool boxed)
        {
            if (_ghost != null) Destroy(_ghost);
            _ghostBoxed = boxed;
            _ghost = HomeModels.Build(_w.City, null, _held.Spec, _held.Item.Variant, boxed);
            _ghost.name = "Placement preview";
            SetLayer(_ghost.transform, 2);
            foreach (Collider col in _ghost.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (Renderer r in _ghost.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private static Record Snapshot(OwnedItem i) => new Record
        {
            Uid = i.Uid, State = i.State, Property = i.Property, Vehicle = i.Vehicle, At = new Vector3(i.X, i.Y, i.Z), Yaw = i.Yaw, Mounted = i.MountedOn, Boxed = i.Boxed,
            InBox = i.InBox, PackOrder = i.PackOrder,
        };

        private void Restore(Record r)
        {
            OwnedItem i = _w.Belongings.Get(r.Uid);
            if (i == null) return;
            bool reboxed = i.Boxed != r.Boxed;
            i.State = r.State; i.Property = r.Property; i.Vehicle = r.Vehicle;
            i.X = r.At.x; i.Y = r.At.y; i.Z = r.At.z; i.Yaw = r.Yaw; i.MountedOn = r.Mounted; i.Boxed = r.Boxed;
            i.InBox = r.InBox; i.PackOrder = r.PackOrder;
            _w.Belongings.Touch();
            if (i.State == ItemState.Packed) { _w.Hide(i.Uid); return; } // back in its box
            if (reboxed || _w.View(i.Uid) == null) _w.Show(i);
            else _w.Pose(_w.View(i.Uid));
        }

        // ---- each frame ----

        private void Update()
        {
            Keyboard k = Keyboard.current;
            Mouse m = Mouse.current;
            if (!Holding)
            {
                ItemView aimedBox = _interactor != null && _interactor.Current is ItemView av && av.Item != null && av.Item.IsBox ? av : null;
                _player.ClickClaimed = aimedBox != null;
                if (_player.ControlEnabled && !_player.Suspended)
                {
                    AimedItemKeys(k);
                    if (aimedBox != null) BoxKeys(aimedBox, k);
                }
                if (k != null && k.zKey.wasPressedThisFrame && k.ctrlKey.isPressed) Undo();
                return;
            }
            if (!_player.ControlEnabled || _player.Suspended)
            {
                if (_ghost != null) _ghost.SetActive(false);
                return;
            }
            if (k != null)
            {
                if (k.rKey.wasPressedThisFrame) _yaw += k.shiftKey.isPressed ? 5f : 15f;
                if (k.gKey.wasPressedThisFrame) Snap = !Snap;
                if (k.bKey.wasPressedThisFrame)
                {
                    HomeSpec home = _w.HomeAt(_player.transform.position);
                    if (home != null) Stow(home);
                    else _w.Say("Storage is at home.");
                    return;
                }
            }
            Aim = Evaluate(new Ray(View.position, View.forward));
            // Aiming at an open moving box: the click packs what you're holding instead of setting it down.
            if (Aim.Onto != null && Aim.Onto.Item.IsBox && !_held.Item.IsBox)
            {
                if (_ghost != null) _ghost.SetActive(false);
                string why = _w.Belongings.CanPack(_held.Item, Aim.Onto.Item);
                _w.City.Hud?.SetStatus($"Carrying {_held.Spec.Name}   " + (why ?? "[LMB] pack it in the box") + "   [RMB] cancel");
                if (m != null && m.rightButton.wasPressedThisFrame) Cancel();
                else if (_player.Input.Attack.WasPressedThisFrame())
                {
                    if (why != null) _w.Say(why);
                    else PackInto(Aim.Onto.Item);
                }
                return;
            }
            ShowGhost(Aim);
            _w.City.Hud?.SetStatus($"Carrying {_held.Spec.Name}{(_held.Spec.Large ? " (heavy)" : "")}   " +
                (Aim.Valid ? "[LMB] place" : Aim.Why) + $"   [R] turn  [G] snap {(Snap ? "on" : "off")}  [RMB] cancel");
            if (m != null && m.rightButton.wasPressedThisFrame) Cancel();
            else if (_player.Input.Attack.WasPressedThisFrame()) TryPlace();
        }

        private void ShowGhost(Target t)
        {
            if (_ghost == null) return;
            bool boxed = _held.Item.Boxed && t.Home == null;
            if (boxed != _ghostBoxed) BuildGhost(boxed);
            _ghost.SetActive(t.Under != Under.Nothing);
            _ghost.transform.SetPositionAndRotation(t.At, Quaternion.Euler(0f, t.Yaw, 0f));
            Material look = t.Valid ? _ok : _bad;
            foreach (Renderer r in _ghost.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = look;
                r.sharedMaterials = mats;
            }
        }

        // ---- where it would go ----

        /// <summary>Works out where the held item would land along <paramref name="ray"/> and whether that's allowed.</summary>
        public Target Evaluate(Ray ray)
        {
            var t = new Target { Under = Under.Nothing, Why = "Point at the floor, a wall or a surface." };
            if (!Holding) return t;
            OwnedItem item = _held.Item;
            HomeItem spec = _held.Spec;
            Physics.SyncTransforms(); // something set down this frame must already be there to stand on or bump into
            RaycastHit[] hits = Physics.RaycastAll(ray, Reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            RaycastHit hit = default;
            bool any = false;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.gameObject.layer == 2 || h.collider is CharacterController) continue;
                hit = h;
                any = true;
                break;
            }
            // A screen in hand snaps to the free arm slot nearest the aim (see ArmSnap), whatever the ray hit first.
            int armSlot = -1;
            Vector3 slotAt = default;
            ItemView snapArm = spec.IsMonitor ? ArmSnap(ray, any ? hit.distance : Reach, out armSlot, out slotAt) : null;
            if (snapArm != null)
            {
                hit.point = slotAt;
                any = true;
            }
            if (!any) return t;

            Vector3 n = hit.normal;
            t.Onto = snapArm != null ? snapArm : hit.collider.GetComponentInParent<ItemView>();
            t.Bed = snapArm != null ? null : hit.collider.GetComponentInParent<CargoBed>();
            if (snapArm != null) n = Vector3.up;
            if (t.Onto != null && t.Onto == _held) t.Onto = null;
            // Aiming at a screen already on an arm means that arm.
            if (spec.IsMonitor && t.Onto != null && t.Onto.Spec.IsMonitor && _w.Belongings.Get(t.Onto.Item.MountedOn) is OwnedItem holder
                && holder.Item.IsArm && _w.View(holder.Uid) is ItemView armView)
            {
                t.Onto = armView;
                n = Vector3.up;
            }
            t.Under = n.y > 0.7f ? (t.Onto != null ? Under.Item : Under.Floor) : n.y < -0.7f ? Under.Ceiling : Mathf.Abs(n.y) < 0.3f ? Under.Wall : Under.Nothing;
            t.Home = t.Bed == null ? _w.HomeAt(hit.point) : null;
            t.Yard = t.Bed == null && t.Home == null && _w.PickupYard.Contains(new Vector2(hit.point.x, hit.point.z));
            t.Indoors = t.Home != null && t.Home.Indoors(hit.point);
            if (t.Under == Under.Floor && t.Home != null && !t.Indoors) t.Under = Under.Ground;

            // Pose: on a floor or a top at the aim point; flat against a wall; hanging from a ceiling.
            t.Yaw = Snap ? Mathf.Round(_yaw / 15f) * 15f : _yaw;
            Vector3 at = hit.point;
            if (t.Under == Under.Wall)
            {
                t.Yaw = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;
                at = hit.point + n * (spec.Depth / 2f) - Vector3.up * (spec.Height / 2f);
            }
            else if (t.Under == Under.Ceiling) at = hit.point - Vector3.up * spec.Height;
            else if (Snap && t.Under != Under.Item && t.Bed == null)
                at = new Vector3(Mathf.Round(at.x / Grid) * Grid, at.y, Mathf.Round(at.z / Grid) * Grid);
            t.At = at;

            // Where: yours, a vehicle, or the yard.
            if (t.Home == null && t.Bed == null && !t.Yard)
            {
                // Standing in a home for sale (open house) reads like being at home: say whose it is.
                HomeSpec other = _w.AnyHomeAt(hit.point);
                t.Why = other != null
                    ? $"{other.Name} isn't yours yet: buy it at the for-sale sign, or take this to {_w.MainHome.Name}."
                    : "Set it down at home, in a vehicle's bed or at the pickup yard.";
                return t;
            }
            bool unbox = item.Boxed && t.Home != null;
            if (t.Bed != null || t.Yard)
            {
                // Loading and staging: anything goes on the floor of the bed or the yard.
                if (t.Under != Under.Floor) { t.Why = t.Bed != null ? "Set it down on the floor of the bed." : "Put it on the ground."; return t; }
            }
            else
            {
                t.Why = PlacementRules.Check(spec, t.Under, t.Onto?.Spec, t.Indoors);
                if (t.Why != null) return t;
            }

            // Desks, arms and screens.
            if (t.Onto != null && t.Under == Under.Item)
            {
                OwnedItem onto = t.Onto.Item;
                if (spec.IsMonitor)
                {
                    t.Why = _w.Belongings.CanMountMonitor(item, onto);
                    if (t.Why != null) return t;
                    if (t.Onto.Spec.IsArm)
                    {
                        int slot = armSlot >= 0 ? armSlot : NearestFreeSlot(t.Onto, hit.point);
                        if (slot < 0) { t.Why = $"The {t.Onto.Spec.Name.ToLowerInvariant()} is full."; return t; }
                        t.At = t.Onto.transform.TransformPoint(HomeModels.ArmSlot(t.Onto.Spec.Arms, slot));
                        t.Yaw = t.Onto.transform.eulerAngles.y;
                    }
                    t.MountOn = onto.Uid;
                }
                else if (spec.IsArm)
                {
                    t.Why = _w.Belongings.CanMountArm(item, onto);
                    if (t.Why != null) return t;
                    t.At = t.Onto.transform.TransformPoint(new Vector3(0f, t.Onto.Spec.Surface, t.Onto.Spec.Depth / 2f - 0.08f));
                    t.Yaw = t.Onto.transform.eulerAngles.y;
                    t.MountOn = onto.Uid;
                }
                else if (t.Onto.Spec.IsDesk) t.MountOn = onto.Uid;
            }

            // Room for it: nothing solid where it would stand (the thing it stands on doesn't count), no doorways.
            if (t.Under != Under.Wall && t.Under != Under.Ceiling && !(t.Onto != null && t.Onto.Spec.IsArm) && !spec.IsArm)
            {
                bool boxed = item.Boxed && !unbox;
                float pad = boxed ? 0.08f : 0f;
                var half = new Vector3(Mathf.Max(0.01f, (spec.Width + pad) / 2f - 0.03f), Mathf.Max(0.01f, (spec.Height + pad) / 2f - 0.03f),
                    Mathf.Max(0.01f, (spec.Depth + pad) / 2f - 0.03f));
                Quaternion rot = Quaternion.Euler(0f, t.Yaw, 0f);
                Vector3 centre = t.At + Vector3.up * (half.y + 0.05f);
                foreach (Collider c in Physics.OverlapBox(centre, half, rot, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c == hit.collider || c.gameObject.layer == 2 || c is CharacterController || c is TerrainCollider) continue;
                    // In a bed: the vehicle's own body and sides don't count, only other cargo.
                    if (t.Bed != null && c.transform.IsChildOf(t.Bed.Vehicle) && c.GetComponentInParent<ItemView>() == null) continue;
                    t.Why = "Something's in the way.";
                    return t;
                }
                if (BlocksDoor(new Bounds(centre, rot * half * 2f)))
                {
                    t.Why = "That would block a door.";
                    return t;
                }
            }
            if (t.Bed != null)
            {
                if (!t.Bed.Open) { t.Why = "Roll the back door up first."; return t; }
                if (_w.Loaded(t.Bed) + spec.Volume > t.Bed.Capacity) { t.Why = "No room left in there."; return t; }
            }
            t.Valid = true;
            t.Why = null;
            return t;
        }

        /// <summary>Screens within this far of the aim ray snap to an arm slot (slots are 0.66 m apart).</summary>
        private const float SnapRadius = 0.4f;

        /// <summary>
        /// The arm slot a held screen should go to: of every free slot on every arm in reach, the one whose screen
        /// centre lies closest to the aim ray, within <see cref="SnapRadius"/>, and not behind whatever the ray hit
        /// (a wall between you and the arm wins). Null if none.
        /// </summary>
        private ItemView ArmSnap(Ray ray, float hitDistance, out int slot, out Vector3 at)
        {
            slot = -1;
            at = default;
            ItemView best = null;
            float bestOff = SnapRadius;
            float centre = _held.Spec.Height / 2f;
            foreach (ItemView arm in _w.Views)
            {
                if (arm == null || arm == _held || !arm.Spec.IsArm || arm.Item.State != ItemState.Placed || arm.Item.MountedOn == 0) continue;
                foreach (int s in FreeSlots(arm))
                {
                    Vector3 p = arm.transform.TransformPoint(HomeModels.ArmSlot(arm.Spec.Arms, s)) + Vector3.up * centre;
                    Vector3 v = p - ray.origin;
                    float along = Vector3.Dot(v, ray.direction);
                    if (along < 0f || along > Reach || along > hitDistance + 0.6f) continue;
                    float off = (v - ray.direction * along).magnitude;
                    if (off >= bestOff) continue;
                    bestOff = off;
                    best = arm;
                    slot = s;
                    at = p;
                }
            }
            return best;
        }

        /// <summary>
        /// Slots on an arm with no screen hanging in them. Screens don't store their slot, so a screen claims whichever
        /// slot it sits nearest; that way a gap left by taking one screen down is filled first, not doubled up.
        /// </summary>
        private List<int> FreeSlots(ItemView arm)
        {
            int arms = arm.Spec.Arms;
            var taken = new bool[arms];
            foreach (OwnedItem m in _w.Belongings.MountedOn(arm.Item.Uid))
            {
                if (m.Item?.IsMonitor != true) continue;
                Vector3 local = arm.transform.InverseTransformPoint(new Vector3(m.X, m.Y, m.Z));
                int nearest = 0;
                for (int s = 1; s < arms; s++)
                    if ((HomeModels.ArmSlot(arms, s) - local).sqrMagnitude < (HomeModels.ArmSlot(arms, nearest) - local).sqrMagnitude) nearest = s;
                taken[nearest] = true;
            }
            var free = new List<int>();
            for (int s = 0; s < arms; s++) if (!taken[s]) free.Add(s);
            return free;
        }

        /// <summary>The free slot on <paramref name="arm"/> nearest a world point, or -1 when it's full.</summary>
        private int NearestFreeSlot(ItemView arm, Vector3 point)
        {
            int best = -1;
            float bestD = float.MaxValue;
            foreach (int s in FreeSlots(arm))
            {
                float d = (arm.transform.TransformPoint(HomeModels.ArmSlot(arm.Spec.Arms, s)) - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        private static bool BlocksDoor(Bounds item)
        {
            item.size = new Vector3(Mathf.Abs(item.size.x), Mathf.Abs(item.size.y), Mathf.Abs(item.size.z));
            foreach (Collider c in Physics.OverlapSphere(item.center, 4f, ~0, QueryTriggerInteraction.Collide))
            {
                Door d = c.GetComponentInParent<Door>();
                if (d == null) continue;
                // The swing and the step through: a metre and a bit either side of the opening.
                Vector3 mid = d.transform.TransformPoint(new Vector3(0.5f, 1f, 0f));
                var zone = new Bounds(mid, new Vector3(1.6f, 2f, 1.6f));
                if (zone.Intersects(item)) return true;
            }
            return false;
        }

        // ---- putting it down ----

        private void TryPlace()
        {
            if (Aim == null || !Aim.Valid)
            {
                if (Aim?.Why != null) _w.Say(Aim.Why);
                return;
            }
            Place(Aim);
        }

        /// <summary>Puts the held item at <paramref name="t"/> (which must be valid).</summary>
        public void Place(Target t)
        {
            ItemView v = _held;
            OwnedItem i = v.Item;
            if (_from is Record from) _undo.Push(from);
            _from = null;
            bool unbox = i.Boxed && t.Home != null;
            if (t.Bed != null)
            {
                Vector3 local = t.Bed.Vehicle.InverseTransformPoint(t.At);
                i.State = ItemState.Loaded;
                i.Vehicle = t.Bed.Id;
                i.Property = "";
                i.X = local.x; i.Y = local.y; i.Z = local.z;
                i.Yaw = t.Yaw - t.Bed.Vehicle.eulerAngles.y;
            }
            else
            {
                i.State = ItemState.Placed;
                i.Vehicle = "";
                i.Property = t.Home?.Id ?? "yard";
                i.X = t.At.x; i.Y = t.At.y; i.Z = t.At.z;
                i.Yaw = t.Yaw;
            }
            i.MountedOn = t.MountOn;
            if (unbox) i.Boxed = false;
            Release();
            _w.Belongings.Touch();
            if (unbox) _w.Show(i);
            else _w.Pose(v);
        }

        // ---- moving boxes ----

        /// <summary>Packs the held item into <paramref name="box"/> (on top of whatever's in it).</summary>
        public void PackInto(OwnedItem box)
        {
            if (!Holding) return;
            OwnedItem i = _held.Item;
            if (_w.Belongings.CanPack(i, box) is string why) { _w.Say(why); return; }
            if (_from is Record from) _undo.Push(from);
            _from = null;
            Release();
            _w.Belongings.Pack(i, box);
            _w.Hide(i.Uid);
            int n = _w.Belongings.Contents(box).Count;
            _w.Say($"{i.Item.Name} packed. {n} thing{(n == 1 ? "" : "s")} in the box.");
        }

        /// <summary>Takes the top thing out of an open box into your hands ([RMB] puts it back).</summary>
        public void TakeOut(OwnedItem box)
        {
            if (Holding) return;
            List<OwnedItem> contents = _w.Belongings.Contents(box);
            if (contents.Count == 0) return;
            OwnedItem top = contents[contents.Count - 1];
            Record packed = Snapshot(top);
            _w.Belongings.Unpack(box);
            _from = packed;
            ItemView v = _w.View(top.Uid) ?? _w.Show(top);
            Hold(v);
        }

        /// <summary>Folds an open box's flaps shut, remembering where (so it opens with [E] in a different home).</summary>
        public void CloseBox(OwnedItem box)
        {
            string where = box.State == ItemState.Placed ? box.Property : "";
            _w.Belongings.SetClosed(box, true, where);
            _w.Show(box);
        }

        /// <summary>Opens a closed box's flaps.</summary>
        public void OpenBox(OwnedItem box)
        {
            _w.Belongings.SetClosed(box, false, null);
            _w.Show(box);
        }

        /// <summary>Throws an empty box away (one with things in it stays).</summary>
        public bool Discard(OwnedItem box)
        {
            if (_w.Belongings.Contents(box).Count > 0) { _w.Say("Empty it first."); return false; }
            _w.Belongings.Remove(box);
            _w.Say("Box thrown away.");
            return true;
        }

        private void BoxKeys(ItemView v, Keyboard k)
        {
            OwnedItem box = v.Item;
            int n = _w.Belongings.Contents(box).Count;
            if (k != null && k.fKey.wasPressedThisFrame)
            {
                Discard(box);
                return;
            }
            if (k != null && k.qKey.wasPressedThisFrame && !box.Closed)
            {
                CloseBox(box);
                return;
            }
            if (!_player.Input.Attack.WasPressedThisFrame()) return;
            if (box.Closed) OpenBox(box);
            else if (n > 0) TakeOut(box);
            else _w.Say("It's empty. [Q] folds it shut, [F] throws it away.");
        }

        /// <summary>Puts the held item away in a home's storage (the cupboard by the front door gives it back).</summary>
        public void Stow(HomeSpec home)
        {
            if (!Holding) return;
            OwnedItem i = _held.Item;
            _from = null;
            Release();
            i.State = ItemState.Stored;
            i.Property = home.Id;
            i.MountedOn = 0;
            _w.Belongings.Touch();
            _w.Say($"{i.Item.Name} put away at {home.Name}.");
        }

        private void Cancel()
        {
            if (_from is Record from)
            {
                Release();
                _from = null;
                Restore(from);
                return;
            }
            _w.Say("Nowhere to put it back: set it down somewhere.");
        }

        /// <summary>Takes back the last move (the item goes back where it was, even into a vehicle).</summary>
        public void Undo()
        {
            if (Holding || _undo.Count == 0) return;
            Record r = _undo.Pop();
            if (_w.Belongings.Get(r.Uid) == null) return;
            Restore(r);
        }

        // ---- aiming at a placed item ----

        private void AimedItemKeys(Keyboard k)
        {
            if (k == null || !(_interactor != null && _interactor.Current is ItemView v) || v.Item == null) return;
            OwnedItem i = v.Item;
            if (k.xKey.wasPressedThisFrame) Sell(v);
            if (!v.Spec.IsMonitor || i.Boxed) return;
            bool changed = false;
            if (k.fKey.wasPressedThisFrame) { i.Power = !i.Power; changed = true; }
            if (k.vKey.wasPressedThisFrame) { i.View = (MonitorView)(((int)i.View + 1) % System.Enum.GetValues(typeof(MonitorView)).Length); changed = true; }
            if (k.tKey.wasPressedThisFrame) { i.Symbol = NextSymbol(i.Symbol); changed = true; }
            if (k.pKey.wasPressedThisFrame) { i.Portrait = !i.Portrait; _w.Pose(v); changed = true; }
            if (changed) _w.Belongings.Touch();
        }

        private string NextSymbol(string current)
        {
            var list = _w.Game.Market.Securities;
            if (list.Count == 0) return current;
            int at = -1;
            for (int j = 0; j < list.Count; j++) if (list[j].Ticker == current) at = j;
            return list[(at + 1) % list.Count].Ticker;
        }

        /// <summary>Sells an item for 40–70% of its price (a second press within a few seconds confirms).</summary>
        public void Sell(ItemView v)
        {
            if (_w.HasMounted(v.Item)) { _w.Say("Take everything off it first."); return; }
            if (v.Item.IsBox) { _w.Say(_w.Belongings.Contents(v.Item).Count > 0 ? "Empty it first." : "Nobody buys a used box: [F] throws it away."); return; }
            decimal price = Belongings.ResaleValue(v.Item);
            if (_sellArmed != v.Item.Uid || Time.unscaledTime > _sellUntil)
            {
                _sellArmed = v.Item.Uid;
                _sellUntil = Time.unscaledTime + 5f;
                _w.Say($"Sell the {v.Spec.Name} for {HomeWorld.Dollars(price)}? [X] again to confirm.");
                return;
            }
            _sellArmed = 0;
            // Company equipment is the company's: the money goes back to it.
            bool company = v.Item.Owner == "fund" && _w.Game.Fund.Exists;
            if (company) _w.Game.Fund.SoldEquipment(price, "Sold: " + v.Spec.Name);
            else _w.Game.Economy.Receive(price, "Sold: " + v.Spec.Name, _w.Game.Clock.Now);
            _w.Belongings.Remove(v.Item);
            _w.Say(company ? $"Sold for {HomeWorld.Dollars(price)}, paid to {_w.Game.Fund.Name}." : $"Sold for {HomeWorld.Dollars(price)}.");
        }
    }
}
