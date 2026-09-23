using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A car dealer's lot (spec Phase 11): this week's cars on display, a price board by each, a salesperson and a
    /// trade-in desk. Aim at a car to inspect it and [E] to take it for a test drive (it's brought round to the
    /// exit; bring it back to the lot, or pay to have it fetched). [E] at a board to buy: new cars are ordered in,
    /// used ones leave the lot. Bought cars wait in the delivery bays. Stock turns over every Monday.
    /// </summary>
    public sealed class DealerLot : MonoBehaviour
    {
        public sealed class Display
        {
            public string ModelId;
            public UsedListing Used; // null: new
            public GameObject Car;
            public GameObject Sign;
        }

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Kit _kit;
        private StaffNpc _staff;
        private string _name, _id;
        private bool _used;
        private string[] _lineup;
        private Rect _lot;
        private (Vector3 P, float Yaw)[] _spots;
        private Vector3[] _bays;
        private float _bayYaw;
        private (Vector3 P, float Yaw) _ready;
        private Hours _hours;
        private FleetView _fleetView;
        private DriveController _driver;

        private int _week = int.MinValue;
        private readonly List<Display> _displays = new List<Display>();
        private OwnedVehicle _loaner;
        private Display _loanedFrom;
        private double _conditionBefore;
        /// <summary>Big purchases take a second [E] within a few seconds (a car isn't a coffee).</summary>
        private object _pending;
        private float _pendingUntil;

        public string DealerName => _name;
        public IReadOnlyList<Display> Displays => _displays;
        public OwnedVehicle Loaner => _loaner;
        public Rect Lot => _lot;
        public (Vector3 P, float Yaw) Ready => _ready;

        public void Configure(GameBootstrap game, InteractionHud hud, Kit kit, StaffNpc staff, string id, string name, bool used,
            string[] lineup, Rect lot, (Vector3, float)[] spots, Vector3[] bays, float bayYaw, (Vector3, float) ready, Hours hours)
        {
            _game = game;
            _hud = hud;
            _kit = kit;
            _staff = staff;
            _id = id;
            _name = name;
            _used = used;
            _lineup = lineup;
            _lot = lot;
            _spots = spots;
            _bays = bays;
            _bayYaw = bayYaw;
            _ready = ready;
            _hours = hours;
        }

        /// <summary>The player's driving, which is set up after the city is built.</summary>
        public void Wire(FleetView fleetView, DriveController driver)
        {
            _fleetView = fleetView;
            _driver = driver;
        }

        private Fleet Fleet => _game.Vehicles;
        private static string Dollars(decimal d) => "$" + d.ToString("N0", CultureInfo.InvariantCulture);
        public bool StaffHere => _staff.AtStation;
        private bool Free => _loaner == null && _driver != null && !_driver.IsDriving;

        private void Update()
        {
            if (_game == null) return;
            if (_loaner != null)
            {
                // Out on a test drive until the player gets out (DriveController parks it).
                if (_loaner.State == VehicleState.Parked) FinishTestDrive();
                return;
            }
            int week = Dealership.Week(_game.Clock.Now);
            if (week != _week) Restock(week);
        }

        // ---- stock ----

        private void Restock(int week)
        {
            _week = week;
            foreach (Display d in _displays) Clear(d);
            _displays.Clear();
            var random = new SeededRandomService(_game.Seed);
            if (_used)
            {
                foreach (UsedListing listing in Dealership.UsedStock(Fleet.Catalog, _lineup, _spots.Length, week, random, _id, _name))
                    _displays.Add(new Display { ModelId = listing.ModelId, Used = listing });
            }
            else
            {
                foreach (string model in Dealership.NewStock(_lineup, _spots.Length, week, random, _id))
                    _displays.Add(new Display { ModelId = model });
            }
            for (int i = 0; i < _displays.Count; i++)
            {
                Display d = _displays[i];
                if (d.Used != null && Fleet.IsSold(d.Used.Id)) continue; // bought earlier this week: an empty space
                Show(d, _spots[i].P, _spots[i].Yaw);
            }
        }

        private void Show(Display d, Vector3 at, float yaw)
        {
            if (!Fleet.Catalog.TryGetModel(d.ModelId, out VehicleModel model)) return;
            GameObject mesh = _game.VehicleLibrary != null ? _game.VehicleLibrary.CarMesh(model.Mesh) : null;
            if (mesh == null) return;
            CarController car = CarFactory.BuildDrivable(transform, mesh, model.Car.Clone(), $"{_name}: {model.Name}");
            car.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            car.SetParked(true);
            car.gameObject.AddComponent<DealerCar>().Configure(this, d);
            d.Car = car.gameObject;

            // Price board by the driver's door, facing out from the car's side.
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 boardAt = at + rot * new Vector3(-2.3f, 0f, 1.2f);
            Transform sign = Kit.Group(transform, "Price board " + model.Name, boardAt, yaw + 90f);
            _kit.Box(sign, "Post", new Vector3(0f, 0.5f, 0.05f), new Vector3(0.06f, 1f, 0.06f), _kit.P.Lit(new Color(0.25f, 0.25f, 0.27f), 0.4f), collider: false);
            _kit.Box(sign, "Board", new Vector3(0f, 1.15f, 0f), new Vector3(0.8f, 0.55f, 0.04f), _kit.P.Lit(new Color(0.96f, 0.96f, 0.94f), 0.2f), collider: false);
            // Text on the board's -z face, which looks away from the car.
            _kit.Text(sign, model.Name, new Vector3(0f, 1.3f, -0.025f), 0f, 0.07f, new Color(0.1f, 0.1f, 0.12f));
            _kit.Text(sign, Dollars(PriceOf(d)), new Vector3(0f, 1.15f, -0.025f), 0f, 0.1f, _used ? new Color(0.75f, 0.1f, 0.08f) : new Color(0.08f, 0.3f, 0.6f));
            _kit.Text(sign, d.Used != null ? $"{d.Used.OdometerKm:N0} km · condition {d.Used.Condition:P0}" : "NEW · 0 km", new Vector3(0f, 0.99f, -0.025f), 0f, 0.045f, new Color(0.25f, 0.25f, 0.27f));
            var aim = sign.gameObject.AddComponent<BoxCollider>();
            aim.isTrigger = true;
            aim.center = new Vector3(0f, 1.1f, 0f);
            aim.size = new Vector3(0.9f, 0.8f, 0.4f);
            sign.gameObject.AddComponent<DealerSign>().Configure(this, d);
            d.Sign = sign.gameObject;
        }

        private static void Clear(Display d)
        {
            if (d.Car != null) Destroy(d.Car);
            if (d.Sign != null) Destroy(d.Sign);
            d.Car = d.Sign = null;
        }

        public decimal PriceOf(Display d) =>
            d.Used != null ? (decimal)d.Used.Price : Fleet.Catalog.TryGetModel(d.ModelId, out VehicleModel m) ? (decimal)m.Price : 0m;

        public string NameOf(Display d) => Fleet.Catalog.TryGetModel(d.ModelId, out VehicleModel m) ? m.Name : d.ModelId;

        /// <summary>The spec card: what it is, how it goes, and (used) every known flaw.</summary>
        public string Describe(Display d)
        {
            Fleet.Catalog.TryGetModel(d.ModelId, out VehicleModel m);
            CarSpec s = m.Car;
            double kw = 0;
            for (int i = 0; i < s.CurveRpm.Length && i < s.CurveTorque.Length; i++) kw = Math.Max(kw, s.CurveTorque[i] * s.CurveRpm[i] / 9549.0);
            string drive = s.Drive == Drivetrain.AWD ? "AWD" : s.Drive == Drivetrain.RWD ? "RWD" : "FWD";
            string text = $"{m.Description}\n{kw * 1.341:0} hp · {drive} · {s.GearRatios.Length}-speed · {s.Mass:0} kg · {s.FuelCapacity:0} L tank";
            if (d.Used == null) return text + $"\nNew · {Dollars(PriceOf(d))}";
            return text + $"\n{d.Used.OdometerKm:N0} km · condition {d.Used.Condition:P0} · tyres {d.Used.TireCondition:P0} · {Dollars(PriceOf(d))}\n{d.Used.Description}";
        }

        private bool Confirm(object what, string ask)
        {
            if (_pending == what && Time.unscaledTime < _pendingUntil)
            {
                _pending = null;
                return true;
            }
            _pending = what;
            _pendingUntil = Time.unscaledTime + 6f;
            _staff.Say(ask);
            return false;
        }

        // ---- buying ----

        public bool CanBuy(Display d) => StaffHere && d.Car != null && d.Car.activeSelf;

        public void Buy(Display d)
        {
            if (!CanBuy(d)) return;
            decimal price = PriceOf(d);
            string name = NameOf(d);
            if (!Confirm(d, $"The {name}, {Dollars(price)} on your card? [E] again to sign.")) return;
            DateTime now = _game.Clock.Now;
            string error = _game.Economy.Spend(price, (d.Used != null ? "Used car: " : "New car: ") + name, now);
            if (error != null)
            {
                _staff.Say("Card didn't go through. Sell me your old one, or come back with the money.");
                _hud.ShowToast(error);
                return;
            }
            Vector3 bay = FreeBay();
            if (d.Used != null)
            {
                Fleet.AddUsed(d.Used, now, bay.x, bay.y, bay.z, _bayYaw);
                Clear(d); // sold off the lot
            }
            else Fleet.Add(d.ModelId, price, now, bay.x, bay.y, bay.z, _bayYaw);
            _staff.Say(d.Used != null ? "Sold! No returns, but you knew what you were getting." : "Congratulations! Fresh off the truck, full tank.");
            _hud.ShowToast($"Bought the {name}. It's waiting in the delivery bay: [E] to drive.");
        }

        private Vector3 FreeBay()
        {
            foreach (Vector3 bay in _bays)
                if (!Occupied(bay, 3.5f)) return bay;
            return _bays[_bays.Length - 1];
        }

        private bool Occupied(Vector3 p, float radius)
        {
            foreach (OwnedVehicle v in Fleet.Vehicles)
                if (v.State == VehicleState.Parked && new Vector2((float)v.X - p.x, (float)v.Z - p.z).magnitude < radius) return true;
            return false;
        }

        // ---- test drives ----

        public bool CanTestDrive(Display d) => StaffHere && Free && d.Car != null && d.Car.activeSelf && _fleetView != null;

        public void TestDrive(Display d)
        {
            if (!CanTestDrive(d)) return;
            if (Occupied(_ready.P, 4f))
            {
                _staff.Say("Can't bring it round, something's parked in the exit. Move it and I'll grab the keys.");
                return;
            }
            _loaner = Fleet.Lend(d.ModelId, d.Used, _game.Clock.Now, _ready.P.x, _ready.P.y, _ready.P.z, _ready.Yaw);
            _conditionBefore = _loaner.Condition;
            _loanedFrom = d;
            d.Car.SetActive(false);
            d.Sign.SetActive(false);
            _fleetView.Refresh();
            if (!_driver.Enter(_loaner))
            {
                Fleet.Remove(_loaner);
                ShowAgain();
                return;
            }
            _staff.Say("Brought it round to the exit. Take it for a spin, just bring it back to the lot.");
        }

        private void FinishTestDrive()
        {
            OwnedVehicle car = _loaner;
            decimal price = PriceOf(_loanedFrom);
            bool returned = Grow(_lot, 1.5f).Contains(new Vector2((float)car.X, (float)car.Z));
            decimal damage = Dealership.DamageBill(_conditionBefore, car.Condition, price);
            Fleet.Remove(car);
            ShowAgain();
            DateTime now = _game.Clock.Now;
            if (damage > 0m) _game.Economy.ChargeFee(damage, $"{_name}: test drive damage ({car.Name})", now);
            if (!returned) _game.Economy.ChargeFee(Dealership.RecoveryFee, $"{_name}: test car recovery", now);
            if (returned && damage == 0m)
                _staff.Say($"So, what do you think? The {car.Name} could be yours for {Dollars(price)}.");
            else if (returned)
                _staff.Say($"Ouch. That's {Dollars(damage)} for the bodywork, it's on your card.");
            else
                _hud.ShowToast($"You left the {car.Name} off the lot. {_name} sent someone to fetch it: {Dollars(Dealership.RecoveryFee)}" +
                    (damage > 0m ? $" plus {Dollars(damage)} of damage." : "."));
        }

        private void ShowAgain()
        {
            if (_loanedFrom != null && _loanedFrom.Car != null)
            {
                _loanedFrom.Car.SetActive(true);
                _loanedFrom.Sign.SetActive(true);
            }
            _loaner = null;
            _loanedFrom = null;
        }

        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        // ---- selling / trade-in ----

        /// <summary>Your car parked on (or at the kerb by) the lot: the one you drove here if it's there.</summary>
        public OwnedVehicle TradeIn()
        {
            Rect near = Grow(_lot, 6f);
            bool Here(OwnedVehicle v) => v.Kind == VehicleKind.Car && !v.TestDrive && v.State == VehicleState.Parked &&
                near.Contains(new Vector2((float)v.X, (float)v.Z));
            OwnedVehicle last = Fleet.LastRidden;
            if (last != null && Here(last)) return last;
            foreach (OwnedVehicle v in Fleet.Vehicles)
                if (Here(v)) return v;
            return null;
        }

        public void Sell()
        {
            if (!StaffHere) return;
            OwnedVehicle v = TradeIn();
            if (v == null)
            {
                _staff.Say("Selling? Park it on the lot and I'll take a look.");
                return;
            }
            decimal offer = Dealership.TradeInOffer(v, _game.Clock.Now);
            if (!Confirm(v, $"I can do {Dollars(offer)} for your {v.Name}, cash or off anything here. [E] again to shake on it.")) return;
            Fleet.Remove(v);
            _game.Economy.Receive(offer, $"Sold {v.Name} to {_name}", _game.Clock.Now);
            _staff.Say("Pleasure doing business. Money's in your account.");
            _hud.ShowToast($"Sold the {v.Name} for {Dollars(offer)}.");
        }

        public string SellPrompt
        {
            get
            {
                OwnedVehicle v = TradeIn();
                return v == null ? "Sell or trade in a car" : $"Sell your {v.Name} · {Dollars(Dealership.TradeInOffer(v, _game.Clock.Now))}";
            }
        }

        public string SellDetails
        {
            get
            {
                OwnedVehicle v = TradeIn();
                if (v == null) return "Park the car you want to sell on the lot first.";
                return $"Private buyers would pay about {Dollars(v.ResaleValue(_game.Clock.Now))}; dealers pay {Dealership.TradeInShare:P0}.\n" +
                       ParkedVehicle.Describe(v, _game).Split('\n')[0];
            }
        }

        public string HoursText => "Open " + _hours.Describe();
    }

    /// <summary>A car on display: inspect it, [E] to test drive.</summary>
    public sealed class DealerCar : Interactable
    {
        private DealerLot _lot;
        private DealerLot.Display _d;

        public void Configure(DealerLot lot, DealerLot.Display d)
        {
            _lot = lot;
            _d = d;
        }

        public override bool CanInteract => base.CanInteract && _lot.CanTestDrive(_d);
        public override string Prompt => "Test drive the " + _lot.NameOf(_d);
        public override string Details => _lot.Describe(_d);
        public override void Interact() => _lot.TestDrive(_d);
    }

    /// <summary>The price board by a car: [E] to buy it.</summary>
    public sealed class DealerSign : Interactable
    {
        private DealerLot _lot;
        private DealerLot.Display _d;

        public void Configure(DealerLot lot, DealerLot.Display d)
        {
            _lot = lot;
            _d = d;
        }

        public override bool CanInteract => base.CanInteract && _lot.CanBuy(_d);
        public override string Prompt => $"Buy the {_lot.NameOf(_d)} · ${_lot.PriceOf(_d).ToString("N0", CultureInfo.InvariantCulture)}";
        public override string Details => _lot.Describe(_d);
        public override void Interact() => _lot.Buy(_d);
    }

    /// <summary>The sales desk: sell the car you parked on the lot (or trade it in against one here).</summary>
    public sealed class TradeInDesk : Interactable
    {
        private DealerLot _lot;

        public void Configure(DealerLot lot) => _lot = lot;

        public override bool CanInteract => base.CanInteract && _lot.StaffHere;
        public override string Prompt => _lot.SellPrompt;
        public override string Details => _lot.SellDetails;
        public override void Interact() => _lot.Sell();
    }
}
