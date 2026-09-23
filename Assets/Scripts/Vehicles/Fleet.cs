using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Trading;

namespace OpeningBell.Vehicles
{
    // Serialized by value in saves: append only.
    public enum VehicleState
    {
        /// <summary>Standing somewhere in the world (bikes; a board set down).</summary>
        Parked,
        /// <summary>In the player's hands (a skateboard).</summary>
        Carried,
        /// <summary>Kept at home, out of the way (extra boards).</summary>
        Stored,
        /// <summary>Being ridden. Saved as Parked (bikes) or Carried (boards).</summary>
        Riding,
    }

    public enum ServiceKind
    {
        Purchase,
        TuneUp,
        NewTires,
        PartInstalled,
    }

    public sealed class ServiceRecord
    {
        public DateTime Date;
        public ServiceKind Kind;
        public string Detail;
        public decimal Cost;
    }

    /// <summary>
    /// A vehicle the player owns, with persistent identity (spec §16): never a disposable prefab. Wear, charge,
    /// parts, where it is and its service history all live here and are saved.
    /// </summary>
    public sealed class OwnedVehicle
    {
        public string Id;
        public string ModelId;
        public string Name;
        public VehicleKind Kind;
        public decimal PurchasePrice;
        public DateTime Purchased;
        public double Odometer;             // metres
        public double Condition = 1;        // frame / drivetrain, 0–1
        public double TireCondition = 1;    // tyres or skate wheels, 0–1
        public double BatteryWh;
        public double BatteryCapacityWh;
        public double FuelLiters;
        public double FuelCapacity;
        public readonly List<string> Parts = new List<string>();
        public VehicleState State;
        public double X, Y, Z, Yaw;         // where it is when parked (world metres, degrees)
        /// <summary>A dealer's car out on a test drive: driven like yours, but never saved, sold or listed.</summary>
        public bool TestDrive;
        public readonly List<ServiceRecord> History = new List<ServiceRecord>();

        public double BatteryFraction => BatteryCapacityWh > 0 ? BatteryWh / BatteryCapacityWh : 0;
        public double FuelFraction => FuelCapacity > 0 ? FuelLiters / FuelCapacity : 0;

        /// <summary>
        /// What a buyer would pay: 20% off the lot, then ~1.5%/month and ~3% per 100 km, scaled by condition.
        /// Parts don't add value (spec §55). Floors at 15%.
        /// </summary>
        public decimal ResaleValue(DateTime now)
        {
            double months = Math.Max(0, (now - Purchased).TotalDays / 30.0);
            double factor = 0.8 * Math.Pow(0.985, months) * Math.Max(0.6, 1 - Odometer / 100_000.0 * 3) * (0.5 + 0.5 * Condition);
            return Money.RoundCents(PurchasePrice * (decimal)Math.Max(0.15, factor));
        }
    }

    /// <summary>
    /// The player's vehicles: buying, wear, service, parts, charging and save/restore. Pure; the city shows
    /// and moves them.
    /// </summary>
    public sealed class Fleet
    {
        /// <summary>Tyre / wheel wear per kilometre ridden.</summary>
        public const double TireWearPerKm = 0.01;
        /// <summary>Condition lost per m/s of impact speed above <see cref="SafeImpactSpeed"/>.</summary>
        public const double DamagePerImpactSpeed = 0.03;
        public const double SafeImpactSpeed = 4;

        private readonly VehicleCatalog _catalog;
        private readonly List<OwnedVehicle> _vehicles = new List<OwnedVehicle>();
        private int _nextId = 1;
        private readonly HashSet<string> _sold = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<OwnedVehicle> Vehicles => _vehicles;
        public VehicleCatalog Catalog => _catalog;
        /// <summary>Last vehicle ridden (shops service "your bike").</summary>
        public OwnedVehicle LastRidden { get; private set; }

        public event Action<OwnedVehicle> Changed;

        public Fleet(VehicleCatalog catalog) => _catalog = catalog;

        public OwnedVehicle Find(string id) => _vehicles.Find(v => v.Id == id);

        /// <summary>A classifieds listing that has already been bought (each car exists once).</summary>
        public bool IsSold(string listingId) => _sold.Contains(listingId);

        public OwnedVehicle Carried => _vehicles.Find(v => v.State == VehicleState.Carried);

        /// <summary>Registers a purchased vehicle (payment is the caller's). Boards go into the player's hands.</summary>
        public OwnedVehicle Add(string modelId, decimal pricePaid, DateTime now, double x, double y, double z, double yaw)
        {
            if (!_catalog.TryGetModel(modelId, out VehicleModel model)) throw new ArgumentException($"Unknown model {modelId}.");
            var v = new OwnedVehicle
            {
                Id = "V-" + _nextId++.ToString("0000", CultureInfo.InvariantCulture),
                ModelId = model.Id,
                Name = model.Name,
                Kind = model.Kind,
                PurchasePrice = pricePaid,
                Purchased = now,
                BatteryCapacityWh = model.Spec.BatteryWh,
                BatteryWh = model.Spec.BatteryWh,
                FuelCapacity = model.Kind == VehicleKind.Car ? model.Car.FuelCapacity : 0,
                FuelLiters = model.Kind == VehicleKind.Car ? model.Car.FuelCapacity : 0,
                X = x, Y = y, Z = z, Yaw = yaw,
            };
            if (model.Kind == VehicleKind.Skateboard)
            {
                // One board in hand; any other goes home.
                OwnedVehicle carried = Carried;
                if (carried != null) carried.State = VehicleState.Stored;
                v.State = VehicleState.Carried;
            }
            v.History.Add(new ServiceRecord { Date = now, Kind = ServiceKind.Purchase, Detail = model.Name, Cost = pricePaid });
            _vehicles.Add(v);
            Changed?.Invoke(v);
            return v;
        }

        public void SetState(OwnedVehicle v, VehicleState state)
        {
            v.State = state;
            if (state == VehicleState.Riding) LastRidden = v;
            Changed?.Invoke(v);
        }

        public void Park(OwnedVehicle v, double x, double y, double z, double yaw)
        {
            v.X = x;
            v.Y = y;
            v.Z = z;
            v.Yaw = yaw;
            SetState(v, VehicleState.Parked);
        }

        /// <summary>Distance ridden: odometer and tyre wear.</summary>
        public void Ridden(OwnedVehicle v, double metres)
        {
            if (metres <= 0) return;
            v.Odometer += metres;
            v.TireCondition = Math.Max(0, v.TireCondition - metres / 1000.0 * TireWearPerKm);
        }

        /// <summary>A crash: damage grows with impact speed above a safe bump.</summary>
        public double Impact(OwnedVehicle v, double speed)
        {
            double loss = Math.Max(0, speed - SafeImpactSpeed) * DamagePerImpactSpeed;
            v.Condition = Math.Max(0, v.Condition - loss);
            return loss;
        }

        public void SetBattery(OwnedVehicle v, double wh) => v.BatteryWh = Math.Max(0, Math.Min(v.BatteryCapacityWh, wh));

        public void SetFuel(OwnedVehicle v, double liters) => v.FuelLiters = Math.Max(0, Math.Min(v.FuelCapacity, liters));

        /// <summary>Buys a used vehicle from a listing: it arrives with its mileage, wear and whatever is in the tank.</summary>
        public OwnedVehicle AddUsed(UsedListing listing, DateTime now, double x, double y, double z, double yaw)
        {
            OwnedVehicle v = Add(listing.ModelId, (decimal)listing.Price, now, x, y, z, yaw);
            _sold.Add(listing.Id);
            v.Odometer = listing.OdometerKm * 1000;
            v.Condition = listing.Condition;
            v.TireCondition = listing.TireCondition;
            v.FuelLiters = v.FuelCapacity * listing.FuelFraction;
            v.History[0].Detail = $"{v.Name} (used, {listing.OdometerKm:N0} km, from {listing.Seller})";
            Changed?.Invoke(v);
            return v;
        }

        /// <summary>
        /// A dealer's car for a test drive: new, or as the used listing describes it. It lives in the fleet only so
        /// it can be driven like any car; <see cref="Remove"/> it when it's handed back.
        /// </summary>
        public OwnedVehicle Lend(string modelId, UsedListing used, DateTime now, double x, double y, double z, double yaw)
        {
            OwnedVehicle v = Add(modelId, 0m, now, x, y, z, yaw);
            v.TestDrive = true;
            if (used != null)
            {
                v.Odometer = used.OdometerKm * 1000;
                v.Condition = used.Condition;
                v.TireCondition = used.TireCondition;
                v.FuelLiters = v.FuelCapacity * used.FuelFraction;
            }
            Changed?.Invoke(v);
            return v;
        }

        /// <summary>Takes a vehicle out of the fleet (sold, or a test car handed back). Payment is the caller's.</summary>
        public void Remove(OwnedVehicle v)
        {
            if (!_vehicles.Remove(v)) return;
            if (LastRidden == v) LastRidden = null;
            Changed?.Invoke(v);
        }

        /// <summary>Charges for <paramref name="gameSeconds"/> at <paramref name="watts"/>, tapering above 80% like real packs.</summary>
        public void Charge(OwnedVehicle v, double watts, double gameSeconds)
        {
            if (v.BatteryCapacityWh <= 0 || gameSeconds <= 0) return;
            double remaining = gameSeconds;
            // Two stages keep the taper exact for long jumps (a night's sleep) as well as frame steps.
            while (remaining > 0 && v.BatteryWh < v.BatteryCapacityWh)
            {
                double rate = v.BatteryFraction < 0.8 ? watts : watts * 0.35;
                double stageEnd = v.BatteryFraction < 0.8 ? 0.8 * v.BatteryCapacityWh : v.BatteryCapacityWh;
                double secondsToStageEnd = (stageEnd - v.BatteryWh) / rate * 3600.0;
                double step = Math.Min(remaining, Math.Max(secondsToStageEnd, 1e-6));
                v.BatteryWh = Math.Min(v.BatteryCapacityWh, v.BatteryWh + rate * step / 3600.0);
                remaining -= step;
            }
        }

        public void Service(OwnedVehicle v, ServiceKind kind, DateTime now, decimal cost, string detail = null)
        {
            if (kind == ServiceKind.TuneUp) v.Condition = 1;
            if (kind == ServiceKind.NewTires) v.TireCondition = 1;
            v.History.Add(new ServiceRecord { Date = now, Kind = kind, Detail = detail ?? kind.ToString(), Cost = cost });
            Changed?.Invoke(v);
        }

        /// <summary>Fits a part (replacing any in the same slot). New wheels also reset wheel wear.</summary>
        public string InstallPart(OwnedVehicle v, string partId, DateTime now, decimal cost)
        {
            if (!_catalog.TryGetPart(partId, out PartSpec part)) return "Unknown part.";
            if (part.Fits != v.Kind) return $"The {part.Name} doesn't fit a {v.Name}.";
            if (v.Parts.Contains(partId)) return $"Your {v.Name} already has {part.Name}.";
            v.Parts.RemoveAll(id => _catalog.TryGetPart(id, out PartSpec other) && other.Slot == part.Slot);
            v.Parts.Add(partId);
            if (part.Slot == PartSlot.Wheels) v.TireCondition = 1;
            if (part.Slot == PartSlot.Battery)
            {
                double fraction = v.BatteryFraction;
                v.BatteryCapacityWh = _catalog.EffectiveSpec(v).BatteryWh;
                v.BatteryWh = fraction * v.BatteryCapacityWh;
            }
            v.History.Add(new ServiceRecord { Date = now, Kind = ServiceKind.PartInstalled, Detail = part.Name, Cost = cost });
            Changed?.Invoke(v);
            return null;
        }

        // ---- save ----

        public FleetSaveData CaptureState()
        {
            var data = new FleetSaveData { NextId = _nextId };
            data.Sold.AddRange(_sold);
            foreach (OwnedVehicle v in _vehicles)
            {
                if (v.TestDrive) continue; // the dealer's, not yours
                var d = new OwnedVehicleSaveData
                {
                    Id = v.Id, ModelId = v.ModelId, Name = v.Name, Kind = (int)v.Kind,
                    PurchasePrice = v.PurchasePrice.ToString(CultureInfo.InvariantCulture), Purchased = v.Purchased.Ticks,
                    Odometer = v.Odometer, Condition = v.Condition, TireCondition = v.TireCondition,
                    BatteryWh = v.BatteryWh, BatteryCapacityWh = v.BatteryCapacityWh,
                    FuelLiters = v.FuelLiters, FuelCapacity = v.FuelCapacity,
                    // A ride in progress is saved where it stands: boards back in hand, bikes parked under the rider.
                    State = (int)(v.State == VehicleState.Riding ? (v.Kind == VehicleKind.Skateboard ? VehicleState.Carried : VehicleState.Parked) : v.State),
                    X = v.X, Y = v.Y, Z = v.Z, Yaw = v.Yaw,
                    LastRidden = v == LastRidden,
                };
                d.Parts.AddRange(v.Parts);
                foreach (ServiceRecord r in v.History)
                    d.History.Add(new ServiceRecordSaveData { Date = r.Date.Ticks, Kind = (int)r.Kind, Detail = r.Detail, Cost = r.Cost.ToString(CultureInfo.InvariantCulture) });
                data.Vehicles.Add(d);
            }
            return data;
        }

        public void RestoreState(FleetSaveData data)
        {
            _vehicles.Clear();
            LastRidden = null;
            _nextId = Math.Max(1, data.NextId);
            _sold.Clear();
            foreach (string id in data.Sold) _sold.Add(id);
            foreach (OwnedVehicleSaveData d in data.Vehicles)
            {
                if (!_catalog.TryGetModel(d.ModelId, out _)) continue; // model retired from the catalog
                var v = new OwnedVehicle
                {
                    Id = d.Id, ModelId = d.ModelId, Name = d.Name, Kind = (VehicleKind)d.Kind,
                    PurchasePrice = decimal.Parse(d.PurchasePrice, CultureInfo.InvariantCulture), Purchased = new DateTime(d.Purchased),
                    Odometer = d.Odometer, Condition = d.Condition, TireCondition = d.TireCondition,
                    BatteryWh = d.BatteryWh, BatteryCapacityWh = d.BatteryCapacityWh,
                    FuelLiters = d.FuelLiters, FuelCapacity = d.FuelCapacity,
                    State = (VehicleState)d.State, X = d.X, Y = d.Y, Z = d.Z, Yaw = d.Yaw,
                };
                v.Parts.AddRange(d.Parts);
                foreach (ServiceRecordSaveData r in d.History)
                    v.History.Add(new ServiceRecord { Date = new DateTime(r.Date), Kind = (ServiceKind)r.Kind, Detail = r.Detail, Cost = decimal.Parse(r.Cost, CultureInfo.InvariantCulture) });
                _vehicles.Add(v);
                if (d.LastRidden) LastRidden = v;
            }
        }
    }

    [Serializable]
    public sealed class FleetSaveData
    {
        public int NextId = 1;
        public List<OwnedVehicleSaveData> Vehicles = new List<OwnedVehicleSaveData>();
        public List<string> Sold = new List<string>();
    }

    [Serializable]
    public sealed class OwnedVehicleSaveData
    {
        public string Id, ModelId, Name, PurchasePrice;
        public int Kind, State;
        public long Purchased;
        public double Odometer, Condition, TireCondition, BatteryWh, BatteryCapacityWh, FuelLiters, FuelCapacity, X, Y, Z, Yaw;
        public bool LastRidden;
        public List<string> Parts = new List<string>();
        public List<ServiceRecordSaveData> History = new List<ServiceRecordSaveData>();
    }

    [Serializable]
    public sealed class ServiceRecordSaveData
    {
        public long Date;
        public int Kind;
        public string Detail, Cost;
    }
}
