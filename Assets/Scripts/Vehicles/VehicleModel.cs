using System;
using System.Collections.Generic;

namespace OpeningBell.Vehicles
{
    // Serialized by value in assets and saves: append only, never reorder.
    public enum VehicleKind
    {
        Skateboard,
        Bicycle,
        EBike,
    }

    /// <summary>
    /// Physical characteristics of a ridden vehicle (SI units). Behaviour comes from these through
    /// <see cref="RideDynamics"/>; there is deliberately no "top speed" field (spec §18).
    /// </summary>
    [Serializable]
    public sealed class RideSpec
    {
        public double Mass = 12;                 // kg, vehicle only (rider added separately)
        public double WheelDiameter = 0.7;       // m
        public double Wheelbase = 1.05;          // m (skateboard: truck spacing)
        public double RollingResistance = 0.005; // Crr on smooth ground
        /// <summary>How much soft ground (grass) adds to rolling resistance: knobbly tyres barely care, slicks sink.</summary>
        public double RoughnessPenalty = 2;
        public double DragArea = 0.5;            // CdA, m²
        public double BrakeDecel = 6;            // m/s² at full brake on good tyres
        public double MaxLeanDeg = 35;
        public double MaxSteerDeg = 40;

        // Bicycles: gear ratio = chainring / cog (wheel turns per crank turn).
        public double LowestGear = 1.2;
        public double HighestGear = 4;
        public int Gears = 1;

        // E-bikes.
        public double MotorPower = 0;            // W; 0 = no motor
        public double AssistCutoff = 0;          // m/s where assistance stops
        public double BatteryWh = 0;

        // Skateboards.
        public double DeckLength = 0.8;          // m
        public double WheelDurometer = 99;       // A scale: harder rolls faster on smooth ground, worse on rough
        public double BearingFactor = 1;         // rolling-resistance multiplier (precision bearings < 1)
        public double TruckLooseness = 0.5;      // 0 tight (stable) … 1 loose (carves)

        public RideSpec Clone() => (RideSpec)MemberwiseClone();
    }

    /// <summary>Something a shop sells that can be ridden.</summary>
    [Serializable]
    public sealed class VehicleModel
    {
        public string Id = "";
        public string Name = "";
        public VehicleKind Kind;
        public double Price;
        public string Description = "";
        /// <summary>Frame / deck colour (RGB 0–1).</summary>
        public float ColorR = 0.6f, ColorG = 0.6f, ColorB = 0.6f;
        public RideSpec Spec = new RideSpec();
    }

    public enum PartSlot
    {
        Wheels,
        Bearings,
        Trucks,
        Battery,
    }

    /// <summary>
    /// Replacement component. Zero fields mean "unchanged". Installing a part replaces any part in the same slot.
    /// </summary>
    [Serializable]
    public sealed class PartSpec
    {
        public string Id = "";
        public string Name = "";
        public VehicleKind Fits;
        public PartSlot Slot;
        public double Price;
        public string Description = "";
        public double WheelDiameter;
        public double WheelDurometer;
        public double BearingFactor;
        /// <summary>Set explicitly (0 is a valid "tight"), hence the flag.</summary>
        public bool SetsTruckLooseness;
        public double TruckLooseness;
        public double BatteryMultiplier;

        /// <summary>The spec with this part fitted.</summary>
        public void ApplyTo(RideSpec spec)
        {
            if (WheelDiameter > 0) spec.WheelDiameter = WheelDiameter;
            if (WheelDurometer > 0) spec.WheelDurometer = WheelDurometer;
            if (BearingFactor > 0) spec.BearingFactor = BearingFactor;
            if (SetsTruckLooseness) spec.TruckLooseness = TruckLooseness;
            if (BatteryMultiplier > 0) spec.BatteryWh *= BatteryMultiplier;
        }
    }

    /// <summary>Everything shops sell: vehicles, parts and service prices.</summary>
    public sealed class VehicleCatalog
    {
        private readonly Dictionary<string, VehicleModel> _models = new Dictionary<string, VehicleModel>(StringComparer.Ordinal);
        private readonly Dictionary<string, PartSpec> _parts = new Dictionary<string, PartSpec>(StringComparer.Ordinal);

        public IReadOnlyCollection<VehicleModel> Models => _models.Values;
        public IReadOnlyCollection<PartSpec> Parts => _parts.Values;
        public decimal TuneUpPrice { get; }
        public decimal NewTiresPrice { get; }

        public VehicleCatalog(IEnumerable<VehicleModel> models, IEnumerable<PartSpec> parts, decimal tuneUp, decimal newTires)
        {
            foreach (VehicleModel m in models)
                if (!_models.ContainsKey(m.Id)) _models.Add(m.Id, m);
                else throw new ArgumentException($"Duplicate vehicle model {m.Id}.");
            foreach (PartSpec p in parts)
                if (!_parts.ContainsKey(p.Id)) _parts.Add(p.Id, p);
                else throw new ArgumentException($"Duplicate part {p.Id}.");
            TuneUpPrice = tuneUp;
            NewTiresPrice = newTires;
        }

        public bool TryGetModel(string id, out VehicleModel model) => _models.TryGetValue(id, out model);
        public bool TryGetPart(string id, out PartSpec part) => _parts.TryGetValue(id, out part);

        /// <summary>The model's spec with the vehicle's installed parts applied (in install order).</summary>
        public RideSpec EffectiveSpec(OwnedVehicle v)
        {
            if (!_models.TryGetValue(v.ModelId, out VehicleModel model)) throw new ArgumentException($"Unknown model {v.ModelId}.");
            RideSpec spec = model.Spec.Clone();
            foreach (string id in v.Parts)
                if (_parts.TryGetValue(id, out PartSpec part)) part.ApplyTo(spec);
            return spec;
        }
    }
}
