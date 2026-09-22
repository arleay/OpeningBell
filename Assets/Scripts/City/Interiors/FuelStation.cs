using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Tidewater Fuel (spec §27, §64): a forecourt behind the Maple shops with a driveway off Exchange St, a
    /// canopy and two pumps. Pull up, get out, [E] at the pump to fill the tank; the Corner Mart next door is
    /// the shop. Open all day and night (card pumps).
    /// </summary>
    public static class FuelStation
    {
        public const decimal PricePerLiter = 1.65m;

        public static void Build(CityContext c)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Tidewater Fuel");
            Transform dyn = Kit.Group(c.Dynamic, "Tidewater Fuel");
            Material slab = c.P.Lit(new Color(0.36f, 0.36f, 0.37f), 0.1f);
            Material white = c.P.Lit(new Color(0.92f, 0.92f, 0.9f), 0.2f);
            Material brand = c.P.Lit(new Color(0.1f, 0.45f, 0.55f), 0.3f);

            // Forecourt and driveway (a ramp up the kerb from Exchange St, like the crosswalk ramps but wide).
            k.Span(root, "Forecourt", new Vector3(106f, -0.05f, 17f), new Vector3(126.5f, 0.005f, 41f), slab).AddComponent<SurfaceTag>().Roughness = 0.3f;
            k.Span(root, "Driveway", new Vector3(126.5f, -0.05f, 25f), new Vector3(130f, 0.005f, 33f), slab).AddComponent<SurfaceTag>().Roughness = 0.3f;
            const float run = 1.4f, rise = -CityPlan.RoadY;
            GameObject ramp = k.Box(root, "Driveway ramp", new Vector3(130f + run / 2f, CityPlan.RoadY + rise / 2f - 0.03f, 29f),
                new Vector3(8f, 0.06f, Mathf.Sqrt(run * run + rise * rise)), slab);
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, -90f, 0f);
            ramp.AddComponent<SurfaceTag>().Roughness = 0.3f;

            // Canopy with lights underneath.
            foreach (float x in new[] { 110.5f, 123.5f })
            foreach (float z in new[] { 22f, 34f })
                k.Box(root, "Column", new Vector3(x, 2.2f, z), new Vector3(0.45f, 4.4f, 0.45f), white);
            k.Span(root, "Canopy", new Vector3(109.5f, 4.4f, 21f), new Vector3(124.5f, 4.85f, 35f), white, collider: false);
            k.Span(root, "Fascia", new Vector3(109.4f, 4.35f, 20.9f), new Vector3(124.6f, 4.9f, 21f), brand, collider: false);
            k.Text(root, "TIDEWATER FUEL", new Vector3(117f, 4.62f, 20.85f), 0f, 0.3f, Color.white);
            foreach (float x in new[] { 113f, 121f })
            {
                k.Box(root, "Canopy light", new Vector3(x, 4.38f, 28f), new Vector3(1.6f, 0.03f, 4f), c.P.Lamp(new Color(0.75f, 0.75f, 0.72f), new Color(1f, 0.98f, 0.95f), 2f), collider: false);
                var lamp = new GameObject("Canopy spot");
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(x, 4.3f, 28f);
                lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 12f;
                light.spotAngle = 120f;
                light.intensity = 26f;
                light.color = new Color(0.95f, 0.97f, 1f);
                light.enabled = false;
                c.NightLights.Add(light);
            }
            k.Box(root, "Price sign post", new Vector3(128.4f, 1.6f, 36f), new Vector3(0.2f, 3.2f, 0.2f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f)));
            k.Box(root, "Price sign", new Vector3(128.4f, 3.4f, 36f), new Vector3(0.15f, 1.3f, 2.2f), brand, collider: false);
            string price = "REGULAR\n$" + PricePerLiter.ToString("0.00", CultureInfo.InvariantCulture) + "/L";
            k.Text(root, price, new Vector3(128.5f, 3.4f, 36f), -90f, 0.2f, Color.white);

            // Islands and pumps.
            foreach (float x in new[] { 114f, 120f })
            {
                k.Span(root, "Island", new Vector3(x - 0.6f, 0f, 25f), new Vector3(x + 0.6f, 0.15f, 31f), c.P.Lit(new Color(0.62f, 0.6f, 0.56f)));
                Transform pump = Kit.Group(dyn, "Pump", new Vector3(x, 0.15f, 28f));
                k.Box(pump, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 1.7f, 0.45f), white);
                k.Box(pump, "Band", new Vector3(0f, 1.45f, 0f), new Vector3(0.72f, 0.35f, 0.47f), brand, collider: false);
                k.Box(pump, "Screen", new Vector3(0.36f, 1.1f, 0f), new Vector3(0.02f, 0.25f, 0.3f), c.P.Unlit(new Color(0.2f, 0.6f, 0.4f)), collider: false);
                k.Box(pump, "Screen", new Vector3(-0.36f, 1.1f, 0f), new Vector3(0.02f, 0.25f, 0.3f), c.P.Unlit(new Color(0.2f, 0.6f, 0.4f)), collider: false);
                pump.gameObject.AddComponent<FuelPump>().Configure(c.Game, c.Hud);
            }

            c.Anchor("fuel_pump_west", new Vector3(114f, 0f, 28f));
            c.Anchor("fuel_bay_west", new Vector3(116.4f, 0f, 28f));
            c.Anchor("fuel_driveway", new Vector3(128f, 0f, 29f));
        }
    }

    /// <summary>Fills up the car you parked next to it, paid by card at the pump.</summary>
    public sealed class FuelPump : Interactable
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        public const float Reach = 5f;

        public void Configure(GameBootstrap game, InteractionHud hud)
        {
            _game = game;
            _hud = hud;
        }

        /// <summary>Your car parked at this pump (the one you drove last, if several are).</summary>
        public OwnedVehicle CarHere()
        {
            OwnedVehicle best = null;
            float bestDistance = Reach;
            foreach (OwnedVehicle v in _game.Vehicles.Vehicles)
            {
                if (v.Kind != VehicleKind.Car || v.State != VehicleState.Parked) continue;
                float d = new Vector2((float)v.X - transform.position.x, (float)v.Z - transform.position.z).magnitude;
                if (d < bestDistance || (d < Reach && v == _game.Vehicles.LastRidden))
                {
                    best = v;
                    bestDistance = d;
                }
            }
            return best;
        }

        private double LitersNeeded(OwnedVehicle v) => Mathf.Max(0f, (float)(v.FuelCapacity - v.FuelLiters));
        private decimal Cost(double liters) => Trading.Money.RoundCents((decimal)liters * FuelStation.PricePerLiter);

        public override string Prompt
        {
            get
            {
                OwnedVehicle v = CarHere();
                if (v == null) return "Fuel pump · park your car here";
                double liters = LitersNeeded(v);
                return liters < 0.5 ? $"{v.Name}: tank is full"
                    : $"Fill up {v.Name} · {liters.ToString("0.0", CultureInfo.InvariantCulture)} L · ${Cost(liters).ToString("N2", CultureInfo.InvariantCulture)}";
            }
        }

        public override string Details => $"Regular ${FuelStation.PricePerLiter.ToString("0.00", CultureInfo.InvariantCulture)}/L · card only";

        public override void Interact()
        {
            OwnedVehicle v = CarHere();
            if (v == null) return;
            double liters = LitersNeeded(v);
            if (liters < 0.5) return;
            decimal cost = Cost(liters);
            string error = _game.Economy.Spend(cost, "Fuel", _game.Clock.Now);
            if (error != null)
            {
                _hud.ShowToast(error);
                return;
            }
            _game.Vehicles.SetFuel(v, v.FuelCapacity);
            decimal bank = _game.Economy.Bank.Balance;
            _hud.ShowToast($"Filled up: {liters.ToString("0.0", CultureInfo.InvariantCulture)} L  -${cost.ToString("N2", CultureInfo.InvariantCulture)}   ·   Bank {(bank < 0 ? "-$" : "$")}{System.Math.Abs(bank).ToString("N2", CultureInfo.InvariantCulture)}");
        }
    }
}
