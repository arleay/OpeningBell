using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// An owned item standing in the world. [E] picks it up; aiming at a monitor, [F] switches it on or off, [V] changes
    /// what it shows, [T] the symbol, [P] portrait/landscape (handled by the <see cref="Carrier"/>).
    /// </summary>
    public sealed class ItemView : Interactable
    {
        private HomeWorld _world;
        public OwnedItem Item { get; private set; }
        public HomeItem Spec => Item.Item;
        public BoxCollider Solid { get; private set; }
        /// <summary>The vehicle it rides in, if loaded.</summary>
        public Transform Vehicle { get; set; }

        public void Configure(HomeWorld world, OwnedItem item)
        {
            _world = world;
            Item = item;
            Solid = gameObject.AddComponent<BoxCollider>();
            HomeItem s = item.Item;
            float pad = item.Boxed ? 0.08f : 0f;
            Solid.size = new Vector3(s.Width + pad, Mathf.Max(0.05f, s.Height + pad), Mathf.Max(0.05f, s.Depth + pad));
            Solid.center = new Vector3(0f, Solid.size.y / 2f, 0f);
            if (s.IsDesk && !item.Boxed) FitToTop(s);
        }

        /// <summary>The second box of an L-shaped desk (the short arm), or null.</summary>
        private BoxCollider _wing;

        /// <summary>Solid on or off (off while it's carried), both boxes of an L-shaped desk.</summary>
        public void SetSolid(bool on)
        {
            Solid.enabled = on;
            if (_wing != null) _wing.enabled = on;
        }

        /// <summary>Cargo in a bed is a trigger (still aimable) so it doesn't reshape the vehicle's physics.</summary>
        public void SetTrigger(bool trigger)
        {
            Solid.isTrigger = trigger;
            if (_wing != null) _wing.isTrigger = trigger;
        }

        /// <summary>
        /// An L-shaped desk's top covers two arms of its footprint, not the whole rectangle: one box over the whole
        /// footprint let screens and keyboards stand in mid-air over the gap in the L (their stands hung below the top).
        /// The shape comes from the fitted model itself: the top's vertices span the footprint but miss one corner,
        /// and the vertex nearest that corner is the inside corner of the L. Split there into a full-width arm and a
        /// short one. A plain rectangular top misses no corner and keeps the single box.
        /// </summary>
        private void FitToTop(HomeItem s)
        {
            float top = s.Height - 0.02f;
            var pts = new System.Collections.Generic.List<Vector2>();
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Matrix4x4 toLocal = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in m.vertices)
                {
                    Vector3 p = toLocal.MultiplyPoint3x4(v);
                    if (p.y >= top) pts.Add(new Vector2(p.x, p.z));
                }
            }
            if (pts.Count < 6) return;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Vector2 p in pts)
            {
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                z0 = Mathf.Min(z0, p.y); z1 = Mathf.Max(z1, p.y);
            }
            const float Eps = 0.03f;
            Vector2 missing = default;
            int gaps = 0;
            foreach (Vector2 c in new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x0, z1), new Vector2(x1, z1) })
            {
                bool covered = false;
                foreach (Vector2 p in pts) if ((p - c).sqrMagnitude < Eps * Eps) { covered = true; break; }
                if (!covered) { missing = c; gaps++; }
            }
            if (gaps != 1) return;
            // The inside corner: the top's vertex nearest the missing one.
            Vector2 inner = pts[0];
            foreach (Vector2 p in pts) if ((p - missing).sqrMagnitude < (inner - missing).sqrMagnitude) inner = p;
            if (Mathf.Abs(inner.x - missing.x) < 0.1f || Mathf.Abs(inner.y - missing.y) < 0.1f) return;

            float h = Solid.size.y;
            // The full-width arm on the far side from the gap (in z), the short arm beside the gap (in x).
            float fz0 = Mathf.Approximately(missing.y, z0) ? inner.y : z0, fz1 = Mathf.Approximately(missing.y, z0) ? z1 : inner.y;
            Solid.size = new Vector3(x1 - x0, h, fz1 - fz0);
            Solid.center = new Vector3((x0 + x1) / 2f, h / 2f, (fz0 + fz1) / 2f);
            float sx0 = Mathf.Approximately(missing.x, x0) ? inner.x : x0, sx1 = Mathf.Approximately(missing.x, x0) ? x1 : inner.x;
            float sz0 = Mathf.Approximately(missing.y, z0) ? z0 : inner.y, sz1 = Mathf.Approximately(missing.y, z0) ? inner.y : z1;
            _wing = gameObject.AddComponent<BoxCollider>();
            _wing.size = new Vector3(sx1 - sx0, h, sz1 - sz0);
            _wing.center = new Vector3((sx0 + sx1) / 2f, h / 2f, (sz0 + sz1) / 2f);
        }

        // Not while pushing the moving cart: both hands are on it.
        public override bool CanInteract => base.CanInteract && _world != null && !_world.Hands.Holding && (_world.Cart == null || !_world.Cart.Pushing);

        public override string Prompt
        {
            get
            {
                if (Item.IsBox)
                {
                    if (Item.Closed && OpensHere) return "Open the box";
                    return "Carry the box";
                }
                if (Spec.IsDesk && _world.HasMounted(Item)) return "Clear the desk to move it";
                return (Item.Boxed ? "Pick up boxed " : "Pick up ") + Spec.Name;
            }
        }

        public override string Details
        {
            get
            {
                if (Item.IsBox)
                {
                    int n = _world.Belongings.Contents(Item).Count;
                    string inside = n == 0 ? "Empty" : $"{n} thing{(n == 1 ? "" : "s")} inside, {_world.Belongings.Contents(Item)[n - 1].Item.Name} on top";
                    if (Item.Closed) return inside + "\n[LMB] open the flaps";
                    string keys = (n > 0 ? "[LMB] take the top one out  " : "[F] throw it away  ") + "[Q] fold the flaps shut";
                    return inside + "\n" + keys + "\nHolding something: [LMB] on the box packs it";
                }
                string condition = Item.Condition >= 0.99 ? "new" : $"{Item.Condition * 100:0}% condition";
                string colour = Spec.Variants.Length > 1 ? Spec.Variants[Item.Variant] + " · " : "";
                string line = $"{colour}{Spec.Tier} · {condition}";
                if (Spec.IsDesk) line += $"\n{_world.Belongings.MonitorsOn(Item)} of {Belongings.MaxMonitorsPerDesk} screens · takes {Spec.MonitorSlots} standing";
                if (Spec.HasScreen && !Item.Boxed)
                    line += $"\n{(Item.Power ? "On" : "Off")} · {Item.View}{(Item.View == MonitorView.Chart ? " " + Item.Symbol : "")}{(Item.Portrait ? " · portrait" : "")}\n[F] power  [V] view  [T] symbol" + (Spec.IsMonitor ? "  [P] rotate" : "");
                return line + "\nSells for about $" + Belongings.ResaleValue(Item).ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>A closed box standing in one of your homes other than where it was shut: [E] opens it (unpacking).</summary>
        private bool OpensHere
        {
            get
            {
                if (Item.State != ItemState.Placed) return false;
                HomeSpec home = _world.Find(Item.Property);
                return home != null && _world.Owns(home) && Item.ClosedAt != home.Id;
            }
        }

        public override void Interact()
        {
            if (Item.IsBox && Item.Closed && OpensHere)
            {
                _world.Hands.OpenBox(Item);
                return;
            }
            if (Spec.IsDesk && _world.HasMounted(Item))
            {
                _world.Say("Take the screens and things off it first.");
                return;
            }
            _world.Hands.PickUp(this);
        }
    }
}
