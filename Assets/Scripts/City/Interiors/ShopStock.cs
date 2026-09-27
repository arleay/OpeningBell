using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// What's in the shops: real models (Poly Haven CC0 scans at their measured size, the Quaternius food and the
    /// Sketchfab grocery pack) on the shelves, in the cases and on the counters, and a few fittings modelled here where
    /// no scan exists (a pool table, a barbell, arcade cabinets, a safe). Local frame as <see cref="Businesses"/>: x across
    /// the shop, z from the storefront (0) to the back.
    /// <para>
    /// Heavy scans (tens of thousands of triangles: the book set, the tea set) go in once or twice, not by the shelf.
    /// Everything static is merged per material by <see cref="MeshMerge"/> and culled with the interior.
    /// </para>
    /// </summary>
    public static class ShopStock
    {
        /// <summary>A real-size model, or a Quaternius/kit prop scaled to <c>height</c> (negative: real size).</summary>
        private static GameObject Put(Kit k, Transform r, string model, Vector3 at, float yaw, float height = -1f)
        {
            if (height < 0f) return k.Real(r, model, at, yaw);
            return k.Prop(r, model, at, height, yaw);
        }

        // ---------------------------------------------------------------- counter

        /// <summary>The till: a real register on the counter, its keys toward the clerk (+z, behind the counter).</summary>
        public static void Register(Kit k, Transform r, Vector3 at) => k.Real(r, "CashRegister_01", at, 180f, 0.8f);

        /// <summary>
        /// What the counter shows for something sold there: the thing itself when there's a model of it, else a little
        /// menu card (services: a haircut, a day pass). <paramref name="at"/> is the counter top.
        /// </summary>
        public static void CounterItem(CityContext c, Transform r, Vector3 at, string item)
        {
            Kit k = c.Kit;
            (string model, float height) = item switch
            {
                "Old wristwatch" => ("vintage_pocket_watch", -1f),
                "Used guitar" => ("Ukulele_01", -1f),
                "Six-pack" => ("food_sm_beer_can", -1f),
                "Bottle of red" => ("food_sm_wine_bottle", -1f),
                "Wash & dry" => ("wicker_basket_02", -1f),
                "Old jacket" => ("fishermans_hat", -1f),
                "Paperback" or "Used book" => ("book_encyclopedia_set_01", -1f),
                "Burger & fries" or "Burger combo" or "Sandwich" => ("Cheeseburger", 0.1f),
                "Painkillers" => ("food_sm_pills_box", -1f),
                "Vitamins" => ("food_sm_protein_jar", -1f),
                "Toolbox" => ("metal_toolbox", -1f),
                "Duct tape" => ("medical_tape", -1f),
                "Loaf of bread" => ("Bread", 0.12f),
                "Donut" => ("Donut1", 0.05f),
                "Motor oil" => ("oil_tin", -1f),
                "Lighter" => ("vintage_lighter", -1f),
                "Lamp" => ("lampRoundTable", 0.4f),
                "Beer" => ("food_sm_beer_bottle", -1f),
                "Whiskey" or "Drink" => ("food_sm_bottle", -1f),
                "Snacks" => ("food_sm_snack", -1f),
                "Bouquet" => ("potted_plant_04", -1f),
                "Pizza slice" => ("Pizza_Slice", 0.04f),
                "Whole pizza" => ("Pizza", 0.05f),
                "Vending snack" => ("food_sm_chocolate_bar", -1f),
                "Groceries" => ("wicker_basket_01", -1f),
                "Riding gloves" => ("garden_gloves_01", -1f),
                "Fresh salmon" or "Crab" => ("Fish", 0.08f),
                "Pastry" => ("Croissant", 0.06f),
                "Energy drink" => ("Soda", 0.14f),
                "Dinner" => ("Steak", 0.05f),
                "Soup" => ("CookingPot", 0.14f),
                _ => (null, 0f),
            };
            if (item == "Coffee") { Mug(c, r, at); return; }
            if (item == "Car battery") { Battery(c, r, at); return; }
            if (item == "Phone charger") { Charger(c, r, at); return; }
            if (model != null && Put(k, r, model, at, 160f, height) != null)
            {
                if (item == "Dinner") k.Prop(r, "Plate", at - Vector3.up * 0.005f, 0.02f, 0f, 0.26f);
                return;
            }
            // A standing card for a service.
            k.Box(r, "Menu card", at + new Vector3(0f, 0.07f, 0f), new Vector3(0.14f, 0.14f, 0.01f), c.P.Lit(new Color(0.96f, 0.95f, 0.9f)), collider: false, yaw: 180f);
            k.Text(r, item, at + new Vector3(0f, 0.09f, -0.006f), 0f, 0.018f, new Color(0.15f, 0.12f, 0.1f));
        }

        /// <summary>A diner mug of coffee: lathed stoneware, a handle, dark coffee just below the rim.</summary>
        private static void Mug(CityContext c, Transform r, Vector3 at)
        {
            var f = new Forge(2);
            f.Use(0).Lathe(at, Vector3.up, new[]
            {
                new Vector2(0f, 0.002f), new Vector2(0.036f, 0.002f), new Vector2(0.04f, 0.01f), new Vector2(0.041f, 0.095f),
                new Vector2(0.036f, 0.095f), new Vector2(0.035f, 0.012f), new Vector2(0f, 0.012f),
            }, 28);
            f.Use(0).Sweep(Forge.Circle(at + new Vector3(0.05f, 0.05f, 0f), Vector3.forward, 0.022f, 18, -1f, -110f, 110f), 0.006f, 0.006f, false, 6);
            f.Use(1).Disc(at + Vector3.up * 0.085f, Vector3.up, 0.035f, 24);
            f.Build(r, "Coffee", c.P.Lit(new Color(0.93f, 0.92f, 0.88f), 0.7f), c.P.Lit(new Color(0.16f, 0.09f, 0.05f), 0.9f));
        }

        /// <summary>A car battery: black case with a lid lip, red and black terminals, a carry strap.</summary>
        private static void Battery(CityContext c, Transform r, Vector3 at)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(r, "Car battery", at);
            Material black = c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.35f);
            k.Box(g, "Case", new Vector3(0f, 0.085f, 0f), new Vector3(0.27f, 0.17f, 0.17f), black, collider: false);
            k.Box(g, "Lid", new Vector3(0f, 0.175f, 0f), new Vector3(0.275f, 0.012f, 0.175f), black, collider: false);
            k.Cylinder(g, "Plus", new Vector3(-0.09f, 0.19f, 0.05f), 0.022f, 0.02f, c.P.Lit(new Color(0.75f, 0.12f, 0.1f), 0.5f));
            k.Cylinder(g, "Minus", new Vector3(0.09f, 0.19f, 0.05f), 0.022f, 0.02f, c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f));
            k.Box(g, "Label", new Vector3(0f, 0.1f, -0.0855f), new Vector3(0.2f, 0.07f, 0.002f), c.P.Lit(new Color(0.9f, 0.75f, 0.2f)), collider: false);
            var f = new Forge(1);
            f.Sweep(new List<Vector3> { new Vector3(-0.12f, 0.18f, 0f), new Vector3(-0.08f, 0.24f, 0f), new Vector3(0.08f, 0.24f, 0f), new Vector3(0.12f, 0.18f, 0f) }, 0.012f, 0.003f, false, 6, Vector3.forward);
            f.Build(g, "Strap", black);
        }

        /// <summary>A phone charger: a white plug block, its prongs, and a coiled cable.</summary>
        private static void Charger(CityContext c, Transform r, Vector3 at)
        {
            var f = new Forge(2);
            f.Use(0).Block(at + new Vector3(0f, 0.013f, 0f), Vector3.right, Vector3.up, new Vector3(0.045f, 0.026f, 0.045f));
            foreach (float s in new[] { -0.009f, 0.009f })
                f.Use(1).Block(at + new Vector3(s, 0.013f, -0.03f), Vector3.right, Vector3.up, new Vector3(0.003f, 0.012f, 0.016f));
            var coil = new List<Vector3>();
            for (int i = 0; i <= 90; i++)
            {
                float t = i / 90f * Mathf.PI * 6f;
                coil.Add(at + new Vector3(0.08f + Mathf.Cos(t) * 0.035f, 0.004f + i * 0.0001f, Mathf.Sin(t) * 0.035f));
            }
            f.Use(0).Sweep(coil, 0.0022f, 0.0022f, false, 5);
            f.Build(r, "Phone charger", c.P.Lit(new Color(0.95f, 0.95f, 0.94f), 0.5f), c.P.Metal(new Color(0.8f, 0.8f, 0.8f)));
        }

        /// <summary>
        /// A two-group espresso machine, its working side toward -z (where the barista stands): polished steel body
        /// with a cup warmer and cups on top, two group heads with portafilters, pressure gauges, a steam wand, a drip
        /// tray, and the grinder beside it.
        /// </summary>
        public static void EspressoMachine(CityContext c, Transform r, Vector3 at, float yaw)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(r, "Espresso machine", at, yaw);
            Material steel = c.P.Metal(new Color(0.82f, 0.83f, 0.85f), 0.85f);
            Material black = c.P.Lit(new Color(0.05f, 0.05f, 0.06f), 0.5f);
            k.Box(g, "Body", new Vector3(0f, 0.22f, 0f), new Vector3(0.78f, 0.44f, 0.52f), steel, collider: false);
            k.Box(g, "Side panels", new Vector3(0f, 0.22f, 0.01f), new Vector3(0.8f, 0.36f, 0.48f), c.P.Lit(new Color(0.55f, 0.1f, 0.08f), 0.7f), collider: false);
            k.Box(g, "Cup rail", new Vector3(0f, 0.46f, 0f), new Vector3(0.76f, 0.02f, 0.5f), steel, collider: false);
            k.Box(g, "Drip tray", new Vector3(0f, 0.02f, -0.3f), new Vector3(0.7f, 0.04f, 0.14f), steel, collider: false);
            var f = new Forge(3); // 0 steel, 1 black, 2 cup
            foreach (float x in new[] { -0.18f, 0.18f })
            {
                // Group head and its portafilter handle sticking out toward the customer.
                f.Use(0).Lathe(new Vector3(x, 0.2f, -0.28f), Vector3.up, new[] { new Vector2(0.045f, 0f), new Vector2(0.05f, 0.03f), new Vector2(0.04f, 0.07f), new Vector2(0f, 0.07f) }, 20);
                f.Use(0).Lathe(new Vector3(x, 0.16f, -0.28f), Vector3.up, new[] { new Vector2(0f, 0f), new Vector2(0.038f, 0.005f), new Vector2(0.04f, 0.04f), new Vector2(0f, 0.04f) }, 20);
                f.Use(1).Sweep(new System.Collections.Generic.List<Vector3> { new Vector3(x, 0.18f, -0.32f), new Vector3(x, 0.17f, -0.46f) }, 0.014f, 0.014f, false, 8);
                // A cup under it.
                f.Use(2).Lathe(new Vector3(x, 0.04f, -0.3f), Vector3.up, new[] { new Vector2(0f, 0f), new Vector2(0.025f, 0f), new Vector2(0.032f, 0.055f), new Vector2(0.028f, 0.055f), new Vector2(0.022f, 0.006f), new Vector2(0f, 0.006f) }, 18);
                // Gauges on the front panel.
                f.Use(0).Lathe(new Vector3(x * 0.5f, 0.36f, -0.262f), Vector3.back, new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.03f, 0.008f), new Vector2(0f, 0.008f) }, 18);
            }
            // Steam wand from the side, bent down.
            f.Use(0).Sweep(new System.Collections.Generic.List<Vector3> { new Vector3(0.36f, 0.3f, -0.2f), new Vector3(0.42f, 0.28f, -0.27f), new Vector3(0.44f, 0.12f, -0.3f) }, 0.006f, 0.006f, false, 8);
            // Cups warming on top.
            for (int i = 0; i < 5; i++)
                f.Use(2).Lathe(new Vector3(-0.28f + i * 0.14f, 0.47f, 0.05f), Vector3.up, new[] { new Vector2(0f, 0f), new Vector2(0.025f, 0f), new Vector2(0.032f, 0.055f), new Vector2(0f, 0.055f) }, 16);
            f.Build(g, "Details", steel, black, c.P.Lit(new Color(0.95f, 0.94f, 0.9f), 0.7f));
            // The grinder beside it: black body, a smoked hopper full of beans.
            Transform grinder = Kit.Group(g, "Grinder", new Vector3(0.62f, 0f, 0f));
            k.Box(grinder, "Body", new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 0.4f, 0.28f), black, collider: false);
            var h = new Forge(2);
            h.Use(0).Lathe(new Vector3(0f, 0.4f, 0f), Vector3.up, new[] { new Vector2(0.03f, 0f), new Vector2(0.1f, 0.2f), new Vector2(0.105f, 0.2f), new Vector2(0.035f, 0f) }, 20);
            h.Use(1).Lathe(new Vector3(0f, 0.41f, 0f), Vector3.up, new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.085f, 0.15f), new Vector2(0f, 0.16f) }, 20);
            h.Build(grinder, "Hopper", c.P.Glass(new Color(0.3f, 0.25f, 0.2f, 0.45f)), c.P.Lit(new Color(0.24f, 0.13f, 0.06f), 0.6f));
        }

        /// <summary>A bicycle wheel standing on its edge: knobbly tyre, alloy rim, hub and 32 spokes. Faces ±z.</summary>
        public static void BikeWheel(CityContext c, Transform r, Vector3 bottom)
        {
            const float R = 0.33f;
            Vector3 hub = bottom + Vector3.up * R;
            var f = new Forge(3); // 0 tyre, 1 rim and hub, 2 spokes
            f.Use(0).Torus(hub, Vector3.forward, R - 0.022f, 0.024f, 48, 10);
            f.Use(1).Sweep(Forge.Circle(hub, Vector3.forward, R - 0.05f, 48), 0.008f, 0.013f, true, 6, Vector3.forward);
            f.Use(1).Lathe(hub - Vector3.forward * 0.05f, Vector3.forward, new[] { new Vector2(0.02f, 0f), new Vector2(0.02f, 0.1f) }, 12);
            for (int i = 0; i < 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                float side = i % 2 == 0 ? 0.045f : -0.045f;
                Vector3 rim = hub + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (R - 0.055f);
                f.Use(2).Sweep(new List<Vector3> { hub + Vector3.forward * side, rim }, 0.0012f, 0.0012f, false, 3);
            }
            f.Build(r, "Bike wheel", c.P.Lit(new Color(0.06f, 0.06f, 0.06f), 0.2f), c.P.Metal(new Color(0.7f, 0.71f, 0.73f), 0.7f), c.P.Metal(new Color(0.85f, 0.85f, 0.85f), 0.8f));
        }

        /// <summary>An e-bike frame battery: a long rounded pack, a charge port and a row of level LEDs.</summary>
        public static void BikeBattery(CityContext c, Transform r, Vector3 bottom)
        {
            var f = new Forge(3);
            f.Use(0).Lathe(bottom + new Vector3(-0.19f, 0.045f, 0f), Vector3.right, new[]
            {
                new Vector2(0f, 0f), new Vector2(0.03f, 0.004f), new Vector2(0.045f, 0.02f), new Vector2(0.045f, 0.36f), new Vector2(0.03f, 0.376f), new Vector2(0f, 0.38f),
            }, 20, 1f, 0.75f);
            for (int i = 0; i < 4; i++)
                f.Use(i < 3 ? 1 : 2).Block(bottom + new Vector3(0.1f + i * 0.018f, 0.09f, 0f), Vector3.right, Vector3.up, new Vector3(0.01f, 0.003f, 0.006f));
            f.Use(2).Block(bottom + new Vector3(-0.12f, 0.09f, 0f), Vector3.right, Vector3.up, new Vector3(0.02f, 0.004f, 0.02f));
            f.Build(r, "E-bike battery", c.P.Lit(new Color(0.12f, 0.13f, 0.14f), 0.55f), c.P.Glow(new Color(0.2f, 1f, 0.4f), 1.5f), c.P.Lit(new Color(0.05f, 0.05f, 0.05f), 0.4f));
        }

        /// <summary>A bike repair stand: tripod base, a mast, an arm with a clamp.</summary>
        public static void RepairStand(CityContext c, Transform r, Vector3 bottom)
        {
            var f = new Forge(2);
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2f;
                f.Use(0).Sweep(new List<Vector3> { bottom + new Vector3(0f, 0.25f, 0f), bottom + new Vector3(Mathf.Cos(a) * 0.4f, 0.01f, Mathf.Sin(a) * 0.4f) }, 0.012f, 0.012f, false, 6);
            }
            f.Use(0).Sweep(new List<Vector3> { bottom + Vector3.up * 0.2f, bottom + Vector3.up * 1.35f }, 0.018f, 0.018f, false, 8);
            f.Use(0).Sweep(new List<Vector3> { bottom + Vector3.up * 1.3f, bottom + new Vector3(0f, 1.38f, -0.3f) }, 0.014f, 0.014f, false, 8);
            f.Use(1).Block(bottom + new Vector3(0f, 1.38f, -0.33f), Vector3.right, Vector3.up, new Vector3(0.08f, 0.1f, 0.08f));
            f.Build(r, "Repair stand", c.P.Lit(new Color(0.8f, 0.2f, 0.15f), 0.5f), c.P.Lit(new Color(0.08f, 0.08f, 0.08f), 0.4f));
        }

        // ---------------------------------------------------------------- trades

        public static readonly string[] Tools =
        {
            "adjustable_wrench", "combination_wrench", "cross_pein_hammer", "screwdriver", "screwdrivers_02", "pliers", "tongue_groove_pliers",
            "handsaw_wood", "hatchet", "pipe_wrench", "measuring_tape_01", "Drill_01", "spray_paint_bottles", "bolt_cutters_01",
            "crowbar_01", "ratchet_wrench", "flathead_screwdriver", "lightbulb_01", "garden_gloves_01", "trowel_01", "dustpan",
        };

        public static readonly string[] CarCare =
        {
            "oil_tin", "small_oil_can_01", "lubricant_spray", "plastic_jerrycan", "tire_pump", "leather_cleaner_can", "multi_cleaner_5_litre",
            "oil_tin", "lubricant_spray", "spray_paint_bottles",
        };

        public static readonly string[] Cleaning = { "bleach_bottle", "all_purpose_cleaner", "multi_cleaner_bottle", "drain_cleaner", "cleaner_tin_01" };

        /// <summary>
        /// Stocks one level of a gondola: items along its length on one side, each turned to face the aisle, spaced by
        /// their own width (plus a finger's gap) so long saws and small tapes both sit right.
        /// </summary>
        public static void Level(Kit k, Transform r, string[] goods, float x, float y, float zFrom, float zTo, float side, System.Random rng, float maxHeight = 0.42f)
        {
            float z = zFrom;
            int misses = 0;
            while (z < zTo && misses < 50)
            {
                string m = goods[rng.Next(goods.Length)];
                if (k.Art == null || k.Art.Model(m) == null) { z += 0.3f; continue; }
                Bounds b = k.RealBounds(m);
                if (b.size.y > maxHeight) { misses++; continue; } // wouldn't fit under the shelf above
                // Turned 90° to face the aisle: the model's x runs along the shelf.
                float along = Mathf.Max(0.06f, b.size.x) + 0.04f;
                if (z + along > zTo) break;
                k.Real(r, m, new Vector3(x + side * (0.3f - Mathf.Min(0.12f, b.size.z / 2f)), y, z + along / 2f), side > 0f ? 90f : 270f);
                z += along;
            }
        }

        public static void Hardware(CityContext c, Transform r, float hw, float front, float back, System.Random rng)
        {
            Kit k = c.Kit;
            // Paint and bags by the window, a ladder against the side wall, brooms in a bucket.
            for (int i = 0; i < 3; i++)
                k.Real(r, "cement_bag", new Vector3(-hw + 0.55f, 0.18f * i, front - 0.6f), 90f + (i % 2) * 6f);
            k.Real(r, "wooden_ladder", new Vector3(hw - 0.3f, 0f, front - 0.9f), 90f);
            k.Real(r, "plastic_broom", new Vector3(hw - 0.35f, 0f, front + 0.2f), 30f);
            k.Real(r, "watering_can_metal_01", new Vector3(-hw + 1.3f, 0f, front - 0.8f), 200f);
        }

        public static void AutoParts(CityContext c, Transform r, float hw, float front, float back)
        {
            Kit k = c.Kit;
            // A tyre rack along the right wall: tyres standing on edge in a steel frame.
            for (float z = front; z < back; z += 0.24f)
            {
                k.Real(r, "old_tyre", new Vector3(hw - 0.4f, 0f, z), 90f);
            }
            k.Box(r, "Tyre rack rail", new Vector3(hw - 0.4f, 0.02f, (front + back) / 2f), new Vector3(0.3f, 0.04f, back - front), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.5f), collider: false);
            k.Real(r, "metal_jerrycan", new Vector3(-hw + 0.5f, 0f, front - 0.7f), 90f);
            k.Real(r, "metal_jerrycan", new Vector3(-hw + 0.5f, 0f, front - 0.45f), 90f);
        }

        /// <summary>Pawn: second-hand electronics on shelves, guitars on the back wall, a proper safe.</summary>
        public static void Pawn(CityContext c, Transform r, float hw, float d, float back, System.Random rng)
        {
            Kit k = c.Kit;
            // Guitars (the ukulele scan, scaled up to guitar size) hung along the back wall.
            for (float x = -hw + 1f; x < hw - 1f; x += 0.9f)
            {
                GameObject g = k.Real(r, "Ukulele_01", new Vector3(x, 1.5f, d - 0.12f), 180f + (float)(rng.NextDouble() * 10 - 5), 1.8f);
            }
            // Electronics shelving by the left wall.
            string[] kit = { "boombox", "Television_01", "television_02", "gaming_console", "classic_laptop", "cassette_player", "vintage_video_camera", "Camera_01" };
            for (int i = 0; i < 2; i++)
            {
                float z = back - 0.4f - i * 1.2f;
                k.Real(r, "steel_frame_shelves_01", new Vector3(-hw + 0.3f, 0f, z), 90f);
                for (int level = 0; level < 3; level++)
                    k.Real(r, kit[(i * 3 + level) % kit.Length], new Vector3(-hw + 0.3f, 0.05f + level * 0.68f, z), 90f);
            }
            k.Real(r, "metal_detector", new Vector3(hw - 0.35f, 0.92f, back - 0.5f), 270f);
            Safe(c, r, new Vector3(hw - 0.6f, 0f, d - 0.6f));
        }

        /// <summary>What a pawn shop's glass cases hold (watches, cameras, binoculars, a controller, clocks).</summary>
        public static readonly string[] PawnCase = { "vintage_pocket_watch", "binoculars", "Camera_01", "gamepad", "alarm_clock_01", "vintage_lighter", "vintage_video_camera", "retro_multimeter" };
        public static readonly string[] SmokeCase = { "cigarette_pack", "cigarette_case", "vintage_lighter", "cigarette_pack", "vintage_lighter" };

        /// <summary>A floor safe: heavy body, a recessed door, combination dial, handle and hinges.</summary>
        private static void Safe(CityContext c, Transform r, Vector3 at)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(r, "Safe", at, 180f);
            Material paint = c.P.Lit(new Color(0.16f, 0.2f, 0.17f), 0.45f);
            Material chrome = c.P.Metal(new Color(0.82f, 0.82f, 0.8f));
            k.Box(g, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(0.8f, 1f, 0.75f), paint);
            k.Box(g, "Door", new Vector3(0f, 0.5f, -0.38f), new Vector3(0.66f, 0.86f, 0.03f), paint, collider: false);
            k.Box(g, "Trim", new Vector3(0f, 0.95f, -0.4f), new Vector3(0.62f, 0.02f, 0.005f), c.P.Metal(new Color(0.85f, 0.7f, 0.4f)), collider: false);
            GameObject dial = k.Cylinder(g, "Dial", new Vector3(-0.1f, 0.62f, -0.41f), 0.12f, 0.03f, chrome);
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GameObject hub = k.Cylinder(g, "Handle hub", new Vector3(0.15f, 0.55f, -0.41f), 0.05f, 0.04f, chrome);
            hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 3; i++)
                k.Box(g, "Spoke", new Vector3(0.15f, 0.55f, -0.43f), new Vector3(0.18f, 0.02f, 0.02f), chrome, collider: false).transform.localRotation = Quaternion.Euler(0f, 0f, i * 60f);
            foreach (float y in new[] { 0.25f, 0.75f })
                k.Box(g, "Hinge", new Vector3(0.345f, y, -0.39f), new Vector3(0.03f, 0.1f, 0.04f), chrome, collider: false);
        }

        /// <summary>Supermarket produce: crates by the door heaped with fruit and veg.</summary>
        public static void Produce(CityContext c, Transform r, float hw, float front, System.Random rng)
        {
            Kit k = c.Kit;
            string[] fruit = { "food_apple_01", "lemon", "yellow_onion", "sweet_potato", "food_avocado_01", "food_lime_01", "food_kiwi_01", "food_pears_asian_01", "food_pomegranate_01" };
            int crates = Mathf.Min(fruit.Length, Mathf.FloorToInt((hw * 2f - 3f) / 0.5f));
            for (int i = 0; i < crates; i++)
            {
                Vector3 at = new Vector3(-hw + 1.5f + i * 0.5f, 0.5f, front - 1.1f);
                k.Box(r, "Produce stand", new Vector3(at.x, 0.25f, at.z), new Vector3(0.46f, 0.5f, 0.6f), c.P.Lit(new Color(0.42f, 0.3f, 0.18f), 0.2f));
                k.Real(r, "plastic_crate_01", at, 0f);
                string f = fruit[i];
                Bounds b = k.Art != null && k.Art.Model(f) != null ? k.RealBounds(f) : new Bounds(Vector3.zero, Vector3.one * 0.08f);
                float step = Mathf.Max(0.06f, Mathf.Max(b.size.x, b.size.z)) * 0.95f;
                // A heap: a bottom layer filling the crate, a smaller layer on top.
                for (int layer = 0; layer < 2; layer++)
                for (float x = -0.1f + layer * step / 2f; x <= 0.1f - layer * step / 2f; x += step)
                for (float z = -0.16f + layer * step / 2f; z <= 0.16f - layer * step / 2f; z += step)
                    k.Real(r, f, at + new Vector3(x, 0.22f + layer * b.size.y * 0.8f, z), (float)rng.NextDouble() * 360f);
            }
            if (k.Art != null && k.Art.Model("bananas") != null)
                k.Real(r, "bananas", new Vector3(-hw + 1f, 0.5f, front - 1.1f), 90f);
        }

        /// <summary>Florist: plants and vases on the tables and shelves.</summary>
        public static readonly string[] Flowers = { "potted_plant_04", "planter_pot_clay", "ceramic_vase_01", "ceramic_vase_02", "ceramic_vase_03", "pottedPlant", "plantSmall1", "plantSmall2", "watering_can_metal_01" };
        /// <summary>Thrift: odds and ends on the tables and shelves.</summary>
        public static readonly string[] Thrift = { "fishermans_hat", "alarm_clock_01", "mantel_clock_01", "brass_vase_01", "antique_ceramic_vase_01", "wicker_basket_01", "fancy_picture_frame_01", "ceramic_vase_02", "cassette_player" };
        /// <summary>Repair: gadgets being fixed and parts.</summary>
        public static readonly string[] Repair = { "classic_laptop", "circuit_board", "retro_multimeter", "television_02", "gamepad", "Drill_01", "screwdrivers_02" };

        /// <summary>A few items on a table or shelf top, spread along <paramref name="width"/>.</summary>
        public static void Spread(Kit k, Transform r, string[] goods, Vector3 centre, float width, float yaw, System.Random rng, int count = 3)
        {
            for (int i = 0; i < count; i++)
            {
                string m = goods[rng.Next(goods.Length)];
                float x = (i + 0.5f) / count * width - width / 2f;
                Vector3 at = centre + Quaternion.Euler(0f, yaw, 0f) * new Vector3(x, 0f, 0f);
                GameObject g = m == "pottedPlant" || m.StartsWith("plantSmall") ? k.Prop(r, m, at, 0.35f, yaw) : k.Real(r, m, at, yaw + (float)(rng.NextDouble() * 30 - 15));
                // Scans of whole sets (a row of suitcases, a tea service) are cut down to a table's worth.
                if (g != null && k.Art != null && k.Art.Model(m) != null)
                {
                    Bounds b = k.RealBounds(m);
                    float big = Mathf.Max(b.size.x, b.size.z);
                    float room = width / count * 0.9f;
                    if (big > room) g.transform.localScale *= room / big;
                }
            }
        }

        /// <summary>Books on every level of an open bookcase (the Poly Haven shelves: four levels, 1.08 m wide).</summary>
        public static void FillBookcase(Kit k, Transform r, Vector3 bottom, float yaw, System.Random rng)
        {
            float[] levels = { 0.04f, 0.42f, 0.8f, 1.18f };
            foreach (float y in levels)
            {
                if (rng.NextDouble() < 0.25) continue;
                // The encyclopaedia set is heavy (67k triangles): one run per level at most, the Kenney books elsewhere.
                Vector3 at = bottom + Quaternion.Euler(0f, yaw, 0f) * new Vector3((float)(rng.NextDouble() * 0.3 - 0.15), y, 0f);
                if (rng.NextDouble() < 0.35) k.Real(r, "book_encyclopedia_set_01", at, yaw + 180f);
                else k.Fit(r, "books", at, new Vector3(0.9f, 0f, 0f), Mathf.Round((yaw + 180f) / 90f) * 90f);
            }
        }

        // ---------------------------------------------------------------- modelled fittings

        /// <summary>
        /// A pool table: slate bed in green baize, cushioned rails in dark wood with pocket cut-outs, turned legs, and
        /// the fifteen balls racked with the cue ball at the other end. 2.54 × 1.42 m (an eight-foot table's footprint).
        /// </summary>
        public static void PoolTable(CityContext c, Transform r, Vector3 centre, float yaw)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(r, "Pool table", centre, yaw);
            Material baize = c.P.Lit(new Color(0.05f, 0.35f, 0.18f), 0.05f);
            Material wood = c.P.Lit(new Color(0.22f, 0.11f, 0.06f), 0.6f);
            Material black = c.P.Lit(new Color(0.03f, 0.03f, 0.03f), 0.2f);
            float L = 2.54f, W = 1.42f, h = 0.79f;
            k.Box(g, "Bed", new Vector3(0f, h - 0.05f, 0f), new Vector3(L - 0.24f, 0.1f, W - 0.24f), baize);
            k.Box(g, "Apron", new Vector3(0f, h - 0.16f, 0f), new Vector3(L - 0.1f, 0.18f, W - 0.1f), wood, collider: false);
            foreach (float s in new[] { -1f, 1f })
            {
                k.Box(g, "Rail", new Vector3(0f, h + 0.02f, s * (W / 2f - 0.06f)), new Vector3(L, 0.08f, 0.12f), wood, collider: false);
                k.Box(g, "Rail", new Vector3(s * (L / 2f - 0.06f), h + 0.02f, 0f), new Vector3(0.12f, 0.08f, W), wood, collider: false);
                k.Box(g, "Cushion", new Vector3(0f, h + 0.01f, s * (W / 2f - 0.135f)), new Vector3(L - 0.3f, 0.04f, 0.03f), baize, collider: false);
                k.Box(g, "Cushion", new Vector3(s * (L / 2f - 0.135f), h + 0.01f, 0f), new Vector3(0.03f, 0.04f, W - 0.3f), baize, collider: false);
            }
            // Pockets: four corners and the middle of each long side.
            foreach (float px in new[] { -1f, 0f, 1f })
            foreach (float pz in new[] { -1f, 1f })
                k.Cylinder(g, "Pocket", new Vector3(px * (L / 2f - 0.13f), h + 0.005f, pz * (W / 2f - 0.13f)), 0.12f, 0.02f, black);
            foreach (float lx in new[] { -1f, 1f })
            foreach (float lz in new[] { -1f, 1f })
                k.Cylinder(g, "Leg", new Vector3(lx * (L / 2f - 0.2f), (h - 0.25f) / 2f, lz * (W / 2f - 0.2f)), 0.12f, h - 0.25f, wood);
            // Balls: 57 mm, racked in a triangle at the foot spot, the cue ball at the head.
            Color[] colours =
            {
                new Color(0.95f, 0.8f, 0.1f), new Color(0.1f, 0.2f, 0.7f), new Color(0.8f, 0.1f, 0.1f), new Color(0.35f, 0.1f, 0.5f),
                new Color(0.95f, 0.45f, 0.1f), new Color(0.1f, 0.45f, 0.2f), new Color(0.5f, 0.12f, 0.08f), new Color(0.05f, 0.05f, 0.05f),
            };
            const float ball = 0.057f;
            var balls = new Forge(9);
            int n = 0;
            for (int row = 0; row < 5; row++)
            for (int i = 0; i <= row; i++, n++)
            {
                Vector3 at = new Vector3(L / 4f + row * ball * 0.87f, h + ball / 2f, (i - row / 2f) * ball);
                balls.Use(n == 4 ? 7 : n % 7).Lathe(at - Vector3.up * (ball / 2f), Vector3.up, SphereProfile(ball / 2f), 12);
            }
            balls.Use(8).Lathe(new Vector3(-L / 4f, h, 0f), Vector3.up, SphereProfile(ball / 2f), 12);
            var mats = new Material[9];
            for (int i = 0; i < 8; i++) mats[i] = c.P.Lit(colours[i], 0.9f);
            mats[8] = c.P.Lit(new Color(0.95f, 0.94f, 0.9f), 0.9f);
            balls.Build(g, "Balls", mats);
        }

        private static Vector2[] SphereProfile(float r)
        {
            var p = new Vector2[9];
            for (int i = 0; i <= 8; i++)
            {
                float t = -Mathf.PI / 2f + i / 8f * Mathf.PI;
                p[i] = new Vector2(Mathf.Cos(t) * r, r + Mathf.Sin(t) * r);
            }
            return p;
        }

        /// <summary>A weight bench with a loaded barbell on its uprights, and a dumbbell pair on the floor beside it.</summary>
        public static void WeightBench(CityContext c, Transform r, Vector3 at, float yaw)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(r, "Weight bench", at, yaw);
            Material pad = c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.35f);
            Material frame = c.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.6f);
            Material steel = c.P.Metal(new Color(0.75f, 0.76f, 0.78f), 0.75f);
            Material plate = c.P.Lit(new Color(0.06f, 0.06f, 0.07f), 0.4f);
            k.Box(g, "Pad", new Vector3(0f, 0.44f, 0f), new Vector3(0.28f, 0.07f, 1.2f), pad);
            k.Box(g, "Frame", new Vector3(0f, 0.2f, 0f), new Vector3(0.08f, 0.4f, 1.0f), frame, collider: false);
            foreach (float s in new[] { -1f, 1f })
                k.Box(g, "Upright", new Vector3(s * 0.55f, 0.6f, 0.55f), new Vector3(0.06f, 1.2f, 0.06f), frame, collider: false);
            var bar = new Forge(2);
            bar.Use(0).Lathe(new Vector3(-1.1f, 1.12f, 0.55f), Vector3.right, new[] { new Vector2(0.014f, 0f), new Vector2(0.014f, 2.2f) }, 10);
            foreach (float s in new[] { -1f, 1f })
            foreach (float off in new[] { 0.72f, 0.78f })
                bar.Use(1).Lathe(new Vector3(s * off - 0.02f, 1.12f, 0.55f), Vector3.right, new[] { new Vector2(0.016f, 0f), new Vector2(0.225f, 0f), new Vector2(0.225f, 0.04f), new Vector2(0.016f, 0.04f) }, 28);
            bar.Build(g, "Barbell", steel, plate);
            var bells = new Forge(2);
            foreach (float s in new[] { -0.12f, 0.12f })
            {
                Vector3 d0 = new Vector3(0.45f + s, 0.06f, -0.3f);
                bells.Use(0).Lathe(d0 - Vector3.forward * 0.16f, Vector3.forward, new[] { new Vector2(0.016f, 0f), new Vector2(0.016f, 0.32f) }, 8);
                foreach (float e in new[] { -0.12f, 0.12f })
                    bells.Use(1).Lathe(d0 + Vector3.forward * (e - 0.03f), Vector3.forward, new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0f), new Vector2(0.06f, 0.06f), new Vector2(0f, 0.06f) }, 6);
            }
            bells.Build(g, "Dumbbells", steel, plate);
        }

        /// <summary>
        /// An upright arcade cabinet: sloped sides, a lit marquee, the screen set back in a bezel, a control panel with a
        /// joystick and buttons, a coin door. Faces -z.
        /// </summary>
        public static void ArcadeCabinet(CityContext c, Transform r, Vector3 at, float yaw, System.Random rng)
        {
            Kit k = c.Kit;
            Transform g = Kit.Group(r, "Arcade cabinet", at, yaw);
            Color hue = Color.HSVToRGB((float)rng.NextDouble(), 0.75f, 0.55f);
            Material body = c.P.Lit(hue, 0.35f);
            Material black = c.P.Lit(new Color(0.04f, 0.04f, 0.05f), 0.4f);
            k.Box(g, "Body", new Vector3(0f, 0.85f, 0.08f), new Vector3(0.66f, 1.7f, 0.6f), body);
            k.Box(g, "Base", new Vector3(0f, 0.45f, -0.18f), new Vector3(0.62f, 0.9f, 0.1f), black, collider: false);
            k.Box(g, "Coin door", new Vector3(0f, 0.55f, -0.235f), new Vector3(0.22f, 0.2f, 0.01f), c.P.Metal(new Color(0.7f, 0.7f, 0.72f), 0.6f), collider: false);
            k.Box(g, "Coin slot", new Vector3(0f, 0.59f, -0.242f), new Vector3(0.04f, 0.05f, 0.005f), c.P.Glow(new Color(1f, 0.4f, 0.2f), 1.2f), collider: false);
            GameObject panel = k.Box(g, "Control panel", new Vector3(0f, 0.98f, -0.3f), new Vector3(0.62f, 0.06f, 0.26f), black, collider: false);
            panel.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            k.Cylinder(g, "Joystick", new Vector3(-0.15f, 1.06f, -0.32f), 0.012f, 0.08f, c.P.Lit(new Color(0.1f, 0.1f, 0.1f), 0.8f));
            k.Box(g, "Ball top", new Vector3(-0.15f, 1.1f, -0.32f), new Vector3(0.035f, 0.035f, 0.035f), c.P.Lit(new Color(0.85f, 0.1f, 0.1f), 0.8f), collider: false);
            for (int i = 0; i < 4; i++)
                k.Cylinder(g, "Button", new Vector3(0.02f + (i % 2) * 0.07f + (i / 2) * 0.03f, 1.02f, -0.3f - (i / 2) * 0.05f), 0.03f, 0.015f,
                    c.P.Lit(Color.HSVToRGB(i / 4f, 0.8f, 0.9f), 0.8f));
            // The body's front face is at z -0.22: the bezel sits on it, the screen just proud of the bezel.
            k.Box(g, "Bezel", new Vector3(0f, 1.32f, -0.23f), new Vector3(0.6f, 0.5f, 0.02f), black, collider: false).transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            GameObject screen = k.Box(g, "Screen", new Vector3(0f, 1.32f, -0.245f), new Vector3(0.5f, 0.4f, 0.01f), c.P.Glow(Color.HSVToRGB((float)rng.NextDouble(), 0.6f, 1f), 1.4f), collider: false);
            screen.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            k.Box(g, "Marquee", new Vector3(0f, 1.62f, -0.235f), new Vector3(0.62f, 0.14f, 0.03f), c.P.Glow(Color.Lerp(hue, Color.white, 0.4f), 1.8f), collider: false);
        }
    }
}
