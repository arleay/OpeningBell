using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The jeweller's floor: lit glass cases along both walls and an island in the middle, every piece in the catalog
    /// on a velvet stand at real size (watches round wrist pillows, rings on cones, chains on neck busts, earrings on a
    /// bar, glasses on a nose stand), a standing mirror to see them on, spotlights and a reflection probe so the metal
    /// has a room to reflect. Aim at a piece: buy it (it goes on straight away), or put on / take off one you own.
    /// Local frame as <see cref="Businesses"/>: x across the shop, z from the storefront (0) to the back.
    /// </summary>
    public static class Jeweler
    {
        /// <summary>Stand-in body measurements for the display pieces (an average adult's, metres).</summary>
        private const float Wrist = 0.03f, Finger = 0.0095f, Neck = 0.058f, EyeSpacing = 0.064f;

        public static void Dress(CityContext c, Transform r, float hw, float d, float front, float back)
        {
            Kit k = c.Kit;
            Material wood = c.P.Lit(new Color(0.16f, 0.1f, 0.07f), 0.55f);
            Material brass = c.P.Metal(new Color(0.85f, 0.68f, 0.38f), 0.8f);
            Material velvet = c.P.Lit(new Color(0.06f, 0.07f, 0.14f), 0.05f);
            Material led = c.P.Glow(new Color(1f, 0.96f, 0.9f), 1.6f);

            var items = new List<JewelryItem>(Jewelry.Catalog);
            // Cases: along each side wall, and a long island down the middle; items dealt out in catalog order.
            // Each case's frame: x along its length, the customer on its -z side. The wall cases run front to back, so
            // yaw -90 puts the left one's customer side toward +x (the middle of the shop), +90 the right one's.
            var cases = new List<(Vector3 At, float Yaw, float Length)>();
            float span = back - front - 0.4f;
            cases.Add((new Vector3(-hw + 0.55f, 0f, front + 0.2f + span / 2f), -90f, span));
            cases.Add((new Vector3(hw - 0.55f, 0f, front + 0.2f + span / 2f), 90f, span));
            float island = Mathf.Min(span - 1.6f, 4f);
            bool hasIsland = hw > 3.5f && island > 1.2f;
            if (hasIsland) cases.Add((new Vector3(0f, 0f, front + 0.2f + span / 2f), -90f, island)); // shown from both sides
            int per = Mathf.CeilToInt(items.Count / (float)cases.Count);
            int next = 0;
            for (int ci = 0; ci < cases.Count; ci++)
            {
                var (at, yaw, length) = cases[ci];
                Transform g = Kit.Group(r, "Case", at, yaw);
                // Base: dark wood with a brass kick and top rail; a glass box over a velvet deck lit by an LED strip.
                float w = length, depth = 0.62f;
                k.Box(g, "Base", new Vector3(0f, 0.46f, 0f), new Vector3(w, 0.92f, depth), wood);
                k.Box(g, "Kick", new Vector3(0f, 0.05f, -depth / 2f - 0.005f), new Vector3(w, 0.1f, 0.01f), brass, collider: false);
                k.Box(g, "Rail", new Vector3(0f, 0.925f, 0f), new Vector3(w + 0.02f, 0.012f, depth + 0.02f), brass, collider: false);
                k.Box(g, "Deck", new Vector3(0f, 0.935f, 0f), new Vector3(w - 0.04f, 0.01f, depth - 0.04f), velvet, collider: false);
                k.Pane(g, "Glass", new Vector3(-w / 2f + 0.01f, 0.94f, -depth / 2f + 0.01f), new Vector3(w / 2f - 0.01f, 1.22f, depth / 2f - 0.01f), new Color(0.85f, 0.9f, 0.95f, 0.12f), collider: false);
                k.Box(g, "LED", new Vector3(0f, 1.215f, depth / 2f - 0.04f), new Vector3(w - 0.08f, 0.008f, 0.012f), led, collider: false);
                bool twoSided = hasIsland && ci == 2;
                int count = twoSided ? per * 2 : per;
                for (int i = 0; i < count && next < items.Count; i++, next++)
                {
                    // Spread along the case; on the island, alternate faces.
                    int slot = twoSided ? i / 2 : i;
                    int slots = twoSided ? Mathf.CeilToInt(count / 2f) : count;
                    float x = -w / 2f + (slot + 0.5f) * (w / slots);
                    float face = twoSided && i % 2 == 1 ? 1f : -1f;
                    Stand(c, g, items[next], new Vector3(x, 0.94f, face * 0.12f), face > 0f ? 180f : 0f, velvet, brass);
                }
            }

            // Standing mirror in the corner by the window, angled toward the cases.
            // Its glass faces the group's -z; yaw 145 turns that toward the back and the middle of the shop.
            Transform mirror = Kit.Group(r, "Standing mirror", new Vector3(hw - 1.1f, 0f, front + 0.6f), 145f);
            k.Box(mirror, "Frame", new Vector3(0f, 1.05f, 0.03f), new Vector3(0.78f, 1.9f, 0.05f), brass);
            k.Box(mirror, "Foot", new Vector3(0f, 0.03f, 0.1f), new Vector3(0.6f, 0.06f, 0.3f), wood);
            Mirror.Create(c, mirror, "Glass", new Vector3(0f, 1.05f, 0f), Vector3.back, new Vector2(0.68f, 1.8f));
            // A second mirror on the back wall behind the counter.
            Mirror.Create(c, r, "Wall mirror", new Vector3(-hw * 0.45f, 1.9f, d - 0.03f), Vector3.back, new Vector2(1.6f, 0.9f));
            k.Box(r, "Wall mirror frame", new Vector3(-hw * 0.45f, 1.9f, d - 0.015f), new Vector3(1.7f, 1.0f, 0.02f), brass, collider: false);

            // Name in brass letters on the back wall, a runner down the middle, plants by the door, a seat to wait on.
            k.Text(r, "LUSTRE", new Vector3(hw * 0.35f, 2.35f, d - 0.03f), 180f, 0.34f, new Color(0.9f, 0.75f, 0.45f));
            k.Text(r, "FINE JEWELLERY · WATCHES", new Vector3(hw * 0.35f, 2.05f, d - 0.03f), 180f, 0.08f, new Color(0.85f, 0.8f, 0.7f));
            k.Box(r, "Runner", new Vector3(0f, 0.006f, (front + back) / 2f), new Vector3(1.4f, 0.012f, back - front), c.P.Lit(new Color(0.35f, 0.08f, 0.1f), 0.02f), collider: false);
            k.Fit(r, "pottedPlant", new Vector3(-hw + 0.5f, 0f, front - 0.9f), new Vector3(0.6f, 0f, 0f));
            k.Fit(r, "pottedPlant", new Vector3(hw - 0.5f, 0f, front - 0.9f), new Vector3(0.6f, 0f, 0f));

            // Light: real counters are lit from inside, under the glass (the LED strip); a small bright light every
            // 1.2 m along each case does that, and a warm ceiling light over each run lifts the room. Plus a probe so
            // metal and stones reflect the room, not the sky.
            foreach (var (at, yaw, length) in cases)
            {
                c.PointLight(r, at + new Vector3(0f, 2.7f, 0f), 4f + length * 0.3f, 0.9f, new Color(1f, 0.95f, 0.86f));
                Transform run = Kit.Group(r, "Case lights", at, yaw);
                for (float x = -length / 2f + 0.6f; x < length / 2f; x += 1.2f)
                    c.PointLight(run, new Vector3(x, 1.18f, 0f), 0.8f, 0.08f, new Color(1f, 0.97f, 0.92f));
            }
            for (float z = front; z < back; z += 3f)
                c.PointLight(r, new Vector3(0f, 3.2f, z), 5f, 0.6f, new Color(1f, 0.93f, 0.82f));
            var probeGo = new GameObject("Jewellery probe");
            probeGo.transform.SetParent(r, false);
            probeGo.transform.localPosition = new Vector3(0f, 1.5f, (front + back) / 2f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.size = new Vector3(hw * 2f, 3.4f, d);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.intensity = 1.1f;
            probeGo.AddComponent<ProbeOnce>();
        }

        /// <summary>A velvet stand for one piece, the piece on it, and the trigger you aim at to buy or wear it.</summary>
        private static void Stand(CityContext c, Transform parent, JewelryItem item, Vector3 at, float yaw, Material velvet, Material brass)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(parent, item.Name, at, yaw);
            GameObject piece;
            switch (item.Slot)
            {
                case JewelrySlot.Watch:
                case JewelrySlot.Bracelet:
                {
                    // A wrist pillow lying across the case (its axis along local x), piece round it, dial up.
                    GameObject pillow = k.Cylinder(g, "Pillow", new Vector3(0f, Wrist + 0.012f, 0f), Wrist * 2f, 0.07f, velvet);
                    pillow.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    // Frame: +x along the pillow, +y up (dial up, 12 o'clock away from the customer so it reads upright).
                    Transform holder = Holder(g, new Vector3(0f, Wrist + 0.012f, 0f), Quaternion.identity);
                    piece = Jewelry.Build(c, holder, item, Wrist);
                    break;
                }
                case JewelrySlot.Ring:
                {
                    // An open ring box on a little riser: the ring stands upright in the slit, stone on top.
                    Material leather = c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.35f);
                    k.Box(g, "Riser", new Vector3(0f, 0.02f, 0f), new Vector3(0.05f, 0.04f, 0.05f), brass, collider: false);
                    k.Box(g, "Ring box", new Vector3(0f, 0.058f, 0f), new Vector3(0.05f, 0.036f, 0.05f), leather, collider: false);
                    k.Box(g, "Ring box cushion", new Vector3(0f, 0.077f, 0f), new Vector3(0.044f, 0.004f, 0.044f), velvet, collider: false);
                    GameObject lid = k.Box(g, "Ring box lid", new Vector3(0f, 0.1f, 0.03f), new Vector3(0.05f, 0.05f, 0.012f), leather, collider: false);
                    lid.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
                    // Frame: +x along the finger (across the box), +y the stone side (up); the band's bottom in the slit.
                    Transform holder = Holder(g, new Vector3(0f, 0.079f + Finger * 0.6f, 0f), Quaternion.identity);
                    piece = Jewelry.Build(c, holder, item, Finger);
                    break;
                }
                case JewelrySlot.Necklace:
                {
                    // Neck bust: a velvet neck on a sloped chest form.
                    k.Cylinder(g, "Neck", new Vector3(0f, 0.2f, 0.02f), Neck * 2f, 0.12f, velvet);
                    k.Box(g, "Chest", new Vector3(0f, 0.08f, 0f), new Vector3(0.22f, 0.16f, 0.1f), velvet);
                    Transform holder = Holder(g, new Vector3(0f, 0.2f, 0.02f), Quaternion.LookRotation(Vector3.back, Vector3.up)); // +z out toward the customer
                    piece = Jewelry.Build(c, holder, item, Neck, 0.09f, 0.075f);
                    break;
                }
                case JewelrySlot.Earrings:
                {
                    k.Box(g, "Bar", new Vector3(0f, 0.07f, 0f), new Vector3(0.09f, 0.008f, 0.008f), brass, collider: false);
                    k.Box(g, "Post", new Vector3(0f, 0.035f, 0f), new Vector3(0.006f, 0.07f, 0.006f), brass, collider: false);
                    piece = null;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        // Frame +x outward = toward the customer here, so the stones face them.
                        Transform holder = Holder(g, new Vector3(side * 0.03f, 0.06f, 0f), Quaternion.LookRotation(Vector3.right, Vector3.up));
                        piece = Jewelry.Build(c, holder, item, EyeSpacing * 0.12f);
                    }
                    break;
                }
                default:
                {
                    // Glasses on a nose stand, facing the customer.
                    k.Box(g, "Stand", new Vector3(0f, 0.05f, 0.04f), new Vector3(0.03f, 0.1f, 0.06f), velvet);
                    Transform holder = Holder(g, new Vector3(0f, 0.09f, 0f), Quaternion.LookRotation(Vector3.back, Vector3.up));
                    piece = Jewelry.Build(c, holder, item, EyeSpacing, 0f, 0.1f);
                    break;
                }
            }
            // Price card in front.
            k.Box(g, "Card", new Vector3(0f, 0.012f, -0.09f), new Vector3(0.07f, 0.024f, 0.002f), c.P.Lit(new Color(0.95f, 0.93f, 0.88f)), collider: false);
            k.Text(g, "$" + item.Price.ToString("N0", CultureInfo.InvariantCulture), new Vector3(0f, 0.012f, -0.0915f), 0f, 0.012f, new Color(0.15f, 0.12f, 0.1f));
            var aim = new GameObject(item.Name + " (buy)");
            aim.transform.SetParent(g, false);
            aim.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var box = aim.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.18f, 0.26f, 0.22f);
            aim.AddComponent<JewelryDisplay>().Configure(c, item);
        }

        private static Transform Holder(Transform parent, Vector3 at, Quaternion rotation)
        {
            var t = new GameObject("Holder").transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            t.localRotation = rotation;
            return t;
        }

        /// <summary>Renders its reflection probe once the shop has been built and lit.</summary>
        private sealed class ProbeOnce : MonoBehaviour
        {
            private int _frames;

            private void Update()
            {
                if (++_frames < 3) return;
                GetComponent<ReflectionProbe>().RenderProbe();
                enabled = false;
            }
        }
    }

    /// <summary>
    /// A piece in the jeweller's case: buy it (from the bank account; it's put on at once, replacing whatever you wore
    /// in that slot), or, once it's yours, put it on or take it off.
    /// </summary>
    public sealed class JewelryDisplay : Interactable
    {
        private CityContext _c;
        private JewelryItem _item;

        public JewelryItem Item => _item;
        public CityContext Context => _c;

        public void Configure(CityContext c, JewelryItem item)
        {
            _c = c;
            _item = item;
        }

        private OpeningBell.PlayerLook Look => _c.Game.Look ??= new OpeningBell.PlayerLook();
        public bool Owned => _c.Game.Look != null && _c.Game.Look.Jewelry.Contains(_item.Id);
        public bool Worn => _c.Game.Look != null && _c.Game.Look.Worn.Contains(_item.Id);

        public override string Prompt => Worn ? $"Take off the {_item.Name}" : Owned ? $"Put on the {_item.Name}"
            : $"Buy {_item.Name} · ${_item.Price.ToString("N0", CultureInfo.InvariantCulture)}";

        public override string Details => _item.Blurb + (Owned ? "\nYours." : "") + $"\nWorn as your {Jewelry.SlotName(_item.Slot)}";

        public override void Interact()
        {
            OpeningBell.PlayerLook look = Look;
            if (!Owned)
            {
                string error = _c.Game.Economy.Spend(_item.Price, _item.Name, _c.Game.Clock.Now);
                if (error != null) { _c.Hud?.ShowToast(error); return; }
                look.Jewelry.Add(_item.Id);
                Wear(look, true);
                _c.Hud?.ShowToast($"{_item.Name}  -${_item.Price.ToString("N0", CultureInfo.InvariantCulture)}   ·   wearing it now");
                return;
            }
            Wear(look, !Worn);
        }

        /// <summary>Puts it on (taking off anything else in the same slot) or takes it off, then rebuilds the body.</summary>
        public void Wear(OpeningBell.PlayerLook look, bool on)
        {
            look.Worn.RemoveAll(id => Jewelry.Get(id)?.Slot == _item.Slot);
            if (on) look.Worn.Add(_item.Id);
            if (_c.Player != null && _c.Player.TryGetComponent(out PlayerBody body)) body.RefreshJewelry(look);
        }
    }
}
