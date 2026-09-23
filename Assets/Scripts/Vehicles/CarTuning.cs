using System;
using System.Collections.Generic;
using OpeningBell.Trading;

namespace OpeningBell.Vehicles
{
    /// <summary>What a car upgrade changes (multipliers on the <see cref="CarSpec"/>; 1 = unchanged).</summary>
    public sealed class CarUpgrade
    {
        public string Id, Name, Description;
        public decimal Price;
        public double Torque = 1, Mass = 1, Grip = 1, Brakes = 1, Springs = 1, Damping = 1, AntiRoll = 1, FinalDrive = 1, Fuel = 1;
        public double RedlineAdd, RideHeightAdd;
        /// <summary>Upgrades in the same slot replace each other.</summary>
        public string Slot;
    }

    /// <summary>
    /// The mechanic's menu for cars (WORLD_SPEC Phase 12, TOWN_SPEC A6): repairs priced by damage and car value,
    /// and performance upgrades stored as parts on the <see cref="OwnedVehicle"/>, applied on top of the model's
    /// spec whenever the car is built. Modest, believable gains (a turbo is +25% torque, not a rocket).
    /// </summary>
    public static class CarTuning
    {
        public static readonly CarUpgrade[] Upgrades =
        {
            new CarUpgrade { Id = "car_ecu", Slot = "ecu", Name = "ECU tune", Price = 650m, Torque = 1.06, RedlineAdd = 300, Description = "Remapped fuel and timing: +6% torque, +300 rpm redline." },
            new CarUpgrade { Id = "car_exhaust", Slot = "exhaust", Name = "Sport exhaust", Price = 1200m, Torque = 1.04, Description = "Freer-flowing, louder: +4% torque." },
            new CarUpgrade { Id = "car_intake", Slot = "engine", Name = "Engine stage 1", Price = 2500m, Torque = 1.12, Fuel = 1.05, Description = "Intake, cams and a rebuild: +12% torque." },
            new CarUpgrade { Id = "car_turbo", Slot = "engine", Name = "Turbo kit", Price = 4800m, Torque = 1.28, Fuel = 1.15, Description = "Bolt-on turbocharger: +28% torque, thirstier." },
            new CarUpgrade { Id = "car_gearbox", Slot = "gearbox", Name = "Short-ratio gearbox", Price = 1800m, FinalDrive = 1.1, Description = "Quicker off the line, lower top speed." },
            new CarUpgrade { Id = "car_brakes", Slot = "brakes", Name = "Big brake kit", Price = 900m, Brakes = 1.3, Description = "Bigger rotors and calipers: +30% braking." },
            new CarUpgrade { Id = "car_suspension", Slot = "suspension", Name = "Sport suspension", Price = 1400m, Springs = 1.35, Damping = 1.3, AntiRoll = 1.5, RideHeightAdd = -0.03, Description = "Stiffer, lower, flatter in corners." },
            new CarUpgrade { Id = "car_tyres", Slot = "tyres", Name = "Performance tyres", Price = 1100m, Grip = 1.12, Description = "Stickier compound: +12% grip." },
            new CarUpgrade { Id = "car_weight", Slot = "weight", Name = "Weight reduction", Price = 2200m, Mass = 0.92, Description = "Lighter panels and seats: -8% weight." },
            new CarUpgrade { Id = "car_wheels", Slot = "wheels", Name = "Alloy wheels", Price = 850m, Mass = 0.99, Description = "Light alloys: a little less weight, a lot more style." },
            new CarUpgrade { Id = "car_tint", Slot = "tint", Name = "Window tint", Price = 250m, Description = "Dark glass all round." },
        };

        /// <summary>Paint colours the body shop offers (RGB 0–1).</summary>
        public static readonly (string Name, float R, float G, float B)[] Paints =
        {
            ("Midnight blue", 0.08f, 0.12f, 0.3f), ("Racing red", 0.7f, 0.06f, 0.05f), ("Gloss black", 0.04f, 0.04f, 0.045f),
            ("Arctic white", 0.9f, 0.9f, 0.88f), ("Forest green", 0.1f, 0.3f, 0.16f), ("Sunburst orange", 0.9f, 0.42f, 0.08f),
        };
        public const decimal PaintPrice = 1500m;

        public static bool TryGet(string id, out CarUpgrade upgrade)
        {
            upgrade = Array.Find(Upgrades, u => u.Id == id);
            return upgrade != null;
        }

        /// <summary>The spec with the owned upgrades applied (a new copy; the model's spec is untouched).</summary>
        public static CarSpec Apply(CarSpec model, IEnumerable<string> parts)
        {
            CarSpec s = model.Clone();
            if (parts == null) return s;
            foreach (string id in parts)
            {
                if (!TryGet(id, out CarUpgrade u)) continue;
                for (int i = 0; i < s.CurveTorque.Length; i++) s.CurveTorque[i] *= u.Torque;
                s.Mass *= u.Mass;
                s.TireGrip *= u.Grip;
                s.BrakeTorque *= u.Brakes;
                s.SpringRate *= u.Springs;
                s.Damping *= u.Damping;
                s.AntiRoll *= u.AntiRoll;
                s.FinalDrive *= u.FinalDrive;
                s.FuelPerKwh *= u.Fuel;
                s.RedlineRpm += u.RedlineAdd;
                s.RestLength += u.RideHeightAdd;
            }
            return s;
        }

        /// <summary>
        /// Engine and body repair back to full condition: 6% of the car's price per 100% of damage, never less than
        /// a $120 shop minimum.
        /// </summary>
        public static decimal RepairPrice(OwnedVehicle v, double modelPrice) =>
            v.Condition >= 0.995 ? 0m : Money.RoundCents(Math.Max(120m, (decimal)(modelPrice * 0.06 * (1 - v.Condition))));

        /// <summary>A set of tyres: dearer on dearer cars ($400 on a $24k sedan, capped at $2,400).</summary>
        public static decimal TyresPrice(double modelPrice) => Money.RoundCents((decimal)Math.Min(2400, Math.Max(360, modelPrice * 0.0165)));

        /// <summary>Installs an upgrade (replacing any in the same slot). Error message, or null.</summary>
        public static string Install(Fleet fleet, OwnedVehicle v, string id, DateTime now, decimal paid)
        {
            if (!TryGet(id, out CarUpgrade u)) return "Unknown upgrade.";
            if (v.Kind != VehicleKind.Car) return "That's for cars.";
            if (v.Parts.Contains(id)) return $"Your {v.Name} already has the {u.Name.ToLowerInvariant()}.";
            v.Parts.RemoveAll(p => TryGet(p, out CarUpgrade other) && other.Slot == u.Slot);
            v.Parts.Add(id);
            if (u.Slot == "tint") v.Tinted = true;
            fleet.Service(v, ServiceKind.PartInstalled, now, paid, u.Name);
            return null;
        }
    }
}
