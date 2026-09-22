using System;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    public enum SaleKind
    {
        Vehicle,
        Part,
        TuneUp,
        NewTires,
    }

    /// <summary>
    /// Something on a shop floor or counter: a bike or board, a part, or a service. Aim at it for the spec card;
    /// [E] buys it with the bank card. Bikes are rolled out front; boards go in your hands; parts go on the board
    /// you're carrying (or the e-bike you brought in); services need your bike parked outside (spec §41–42).
    /// </summary>
    public sealed class ForSaleDisplay : Interactable
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private StaffNpc _staff;
        private SaleKind _kind;
        private string _id;
        private Vector3 _pickup;
        private float _pickupYaw;
        private Vector3 _shop;

        public const float BringItInRadius = 25f;

        public SaleKind Kind => _kind;
        public string Id => _id;

        public void Configure(GameBootstrap game, InteractionHud hud, StaffNpc staff, SaleKind kind, string id, Vector3 pickup, float pickupYaw, Vector3 shop)
        {
            _game = game;
            _hud = hud;
            _staff = staff;
            _kind = kind;
            _id = id;
            _pickup = pickup;
            _pickupYaw = pickupYaw;
            _shop = shop;
        }

        private Fleet Fleet => _game.Vehicles;
        private static string Dollars(decimal d) => "$" + d.ToString("N0", CultureInfo.InvariantCulture);

        public decimal Price
        {
            get
            {
                switch (_kind)
                {
                    case SaleKind.Vehicle: return Fleet.Catalog.TryGetModel(_id, out VehicleModel m) ? (decimal)m.Price : 0m;
                    case SaleKind.Part: return Fleet.Catalog.TryGetPart(_id, out PartSpec p) ? (decimal)p.Price : 0m;
                    case SaleKind.TuneUp: return Fleet.Catalog.TuneUpPrice;
                    default: return Fleet.Catalog.NewTiresPrice;
                }
            }
        }

        public string Name => _kind switch
        {
            SaleKind.Vehicle => Fleet.Catalog.TryGetModel(_id, out VehicleModel m) ? m.Name : _id,
            SaleKind.Part => Fleet.Catalog.TryGetPart(_id, out PartSpec p) ? p.Name : _id,
            SaleKind.TuneUp => "Tune-up",
            _ => "New tyres",
        };

        public override bool CanInteract => base.CanInteract && _staff.AtStation;

        public override string Prompt => _kind == SaleKind.Vehicle || _kind == SaleKind.Part
            ? $"Buy {Name} · {Dollars(Price)}"
            : $"{Name} · {Dollars(Price)}";

        public override string Details
        {
            get
            {
                var c = CultureInfo.InvariantCulture;
                switch (_kind)
                {
                    case SaleKind.Vehicle:
                    {
                        Fleet.Catalog.TryGetModel(_id, out VehicleModel m);
                        RideSpec s = m.Spec;
                        string stats = m.Kind == VehicleKind.Skateboard
                            ? $"{s.DeckLength * 100:0} cm deck · {s.WheelDiameter * 1000:0} mm {s.WheelDurometer:0}A wheels · {(s.TruckLooseness > 0.55 ? "loose" : s.TruckLooseness < 0.35 ? "tight" : "medium")} trucks"
                            : $"{s.Mass.ToString("0.#", c)} kg · {s.Gears} gear{(s.Gears == 1 ? "" : "s")} · {s.WheelDiameter / 0.0254:0}\" wheels";
                        if (m.Kind == VehicleKind.EBike) stats += $" · {s.MotorPower:0} W to {s.AssistCutoff * 2.237:0} mph · {s.BatteryWh:0} Wh";
                        return m.Description + "\n" + stats;
                    }
                    case SaleKind.Part:
                    {
                        Fleet.Catalog.TryGetPart(_id, out PartSpec p);
                        OwnedVehicle target = PartTarget(p, out string why);
                        return p.Description + "\n" + (target != null ? "Fits your " + target.Name : why);
                    }
                    default:
                    {
                        OwnedVehicle bike = BroughtInBike();
                        string what = _kind == SaleKind.TuneUp ? "Restores condition (brakes, gears, chain)." : "Fresh tyres: better grip, less rolling drag.";
                        return what + "\n" + (bike != null ? $"Your {bike.Name}: {ParkedVehicle.Describe(bike, _game).Split('\n')[0]}" : "Park your bike out front first.");
                    }
                }
            }
        }

        private OwnedVehicle PartTarget(PartSpec p, out string why)
        {
            if (p.Fits == VehicleKind.Skateboard)
            {
                OwnedVehicle board = Fleet.Carried;
                why = board == null ? "Bring your skateboard (carry it in)." : null;
                return board;
            }
            OwnedVehicle bike = BroughtInBike(p.Fits);
            why = bike == null ? "Park your e-bike out front first." : null;
            return bike;
        }

        /// <summary>The bike you rode here: last ridden if it's parked nearby, else any of yours parked nearby.</summary>
        private OwnedVehicle BroughtInBike(VehicleKind? kind = null)
        {
            bool Near(OwnedVehicle v) =>
                v.State == VehicleState.Parked && v.Kind != VehicleKind.Skateboard && (kind == null || v.Kind == kind) &&
                new Vector2((float)v.X - _shop.x, (float)v.Z - _shop.z).magnitude < BringItInRadius;
            OwnedVehicle last = Fleet.LastRidden;
            if (last != null && Near(last)) return last;
            foreach (OwnedVehicle v in Fleet.Vehicles)
                if (Near(v)) return v;
            return null;
        }

        public override void Interact()
        {
            if (!CanInteract) return;
            DateTime now = _game.Clock.Now;
            decimal price = Price;
            OwnedVehicle target = null;
            string problem = null;
            if (_kind == SaleKind.Part)
            {
                Fleet.Catalog.TryGetPart(_id, out PartSpec p);
                target = PartTarget(p, out problem);
                if (target != null && target.Parts.Contains(_id)) problem = $"Your {target.Name} already has those.";
            }
            else if (_kind != SaleKind.Vehicle)
            {
                target = BroughtInBike();
                if (target == null) problem = "Park your bike out front and I'll take a look.";
            }
            if (problem != null)
            {
                _staff.Say(problem);
                return;
            }

            string error = _game.Economy.Spend(price, Name, now);
            if (error != null)
            {
                _staff.Say("Card didn't go through.");
                _hud.ShowToast(error);
                return;
            }

            switch (_kind)
            {
                case SaleKind.Vehicle:
                {
                    Fleet.Catalog.TryGetModel(_id, out VehicleModel m);
                    Vector3 spot = FreePickupSpot();
                    OwnedVehicle v = Fleet.Add(_id, price, now, spot.x, spot.y, spot.z, _pickupYaw);
                    _staff.Say(m.Kind == VehicleKind.Skateboard ? "She's all yours. Stay off the grass." : "Rolled it out front for you. Enjoy the ride.");
                    _hud.ShowToast(m.Kind == VehicleKind.Skateboard
                        ? $"Bought the {v.Name}. Press R to ride it."
                        : $"Bought the {v.Name}. It's parked out front: [E] to ride.");
                    break;
                }
                case SaleKind.Part:
                    Fleet.InstallPart(target, _id, now, price);
                    _staff.Say("Fitted. Give it a spin.");
                    break;
                default:
                    Fleet.Service(target, _kind == SaleKind.TuneUp ? ServiceKind.TuneUp : ServiceKind.NewTires, now, price);
                    _staff.Say(_kind == SaleKind.TuneUp ? "Tuned up. Shifts like new." : "New rubber, good to go.");
                    break;
            }
        }

        /// <summary>The pickup spot, shuffled along if bikes are already standing there.</summary>
        private Vector3 FreePickupSpot()
        {
            Vector3 along = Quaternion.Euler(0f, _pickupYaw, 0f) * Vector3.forward;
            for (int i = 0; i < 6; i++)
            {
                Vector3 spot = _pickup + along * (2f * i);
                bool taken = false;
                foreach (OwnedVehicle v in Fleet.Vehicles)
                    if (v.State == VehicleState.Parked && new Vector2((float)v.X - spot.x, (float)v.Z - spot.z).magnitude < 1.2f) taken = true;
                if (!taken) return spot;
            }
            return _pickup;
        }
    }
}
