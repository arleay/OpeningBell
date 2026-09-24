using System;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpeningBell.City
{
    /// <summary>
    /// Fuel you handle yourself: the pump's nozzle on its hose (take it at the pump, walk it to your car, hold the
    /// left button to fill, hang it up to pay), the jerry can (bought at Tidewater, filled at a pump, [J] to take it out
    /// anywhere and pour), and Tidewater's roadside delivery when you're stranded (called from the phone). Aim at your
    /// car and the fuel pours from the nozzle or spout into the filler flap on the rear quarter facing you.
    /// </summary>
    public sealed class FuelHands : MonoBehaviour
    {
        public enum Tool { None, Nozzle, Can }

        /// <summary>Litres a second: a pump fills a sedan in about 20 s; a can glugs out slower.</summary>
        public const float PumpRate = 2.5f, PourRate = 0.8f;
        public const double CanCapacity = 10;
        public const decimal CanPrice = 24.99m;
        /// <summary>How far from the pump the hose reaches, and how far you can be from what you're filling.</summary>
        public const float HoseReach = 5.5f, FillReach = 2.4f;
        public const decimal RoadsideFee = 35m;
        public const double RoadsideLiters = 10;
        public const int RoadsideMinutes = 20;

        private CityContext _c;
        private FirstPersonController _player;
        private Transform _view;
        private GameObject _nozzle, _can;
        private Transform _tip;
        private LineRenderer _hose, _stream;
        private readonly Transform[] _drops = new Transform[4];
        private float _flowTime;

        public Tool Held { get; private set; }
        /// <summary>The pump the nozzle came from.</summary>
        public FuelPump Pump { get; private set; }
        /// <summary>Litres pumped since the nozzle came off the hook (paid when it goes back).</summary>
        public double Pumped { get; private set; }
        public decimal PumpedCost => Trading.Money.RoundCents((decimal)Pumped * FuelStation.PricePerLiter);
        public bool Flowing { get; private set; }

        private Fleet Fleet => _c.Game.Vehicles;

        public void Configure(CityContext c, FirstPersonController player)
        {
            _c = c;
            _player = player;
            _view = player.GetComponentInChildren<Camera>().transform;
            Material dark = c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.4f);
            Material green = c.P.Lit(new Color(0.15f, 0.55f, 0.3f), 0.4f);
            Material red = c.P.Lit(new Color(0.75f, 0.1f, 0.08f), 0.35f);
            Material fuel = c.P.Glass(new Color(0.85f, 0.65f, 0.2f, 0.7f));

            // The nozzle: a grip with a trigger guard and a spout, held low and to the right.
            _nozzle = new GameObject("Held nozzle");
            _nozzle.transform.SetParent(_view, false);
            _nozzle.transform.localPosition = new Vector3(0.22f, -0.28f, 0.48f);
            _nozzle.transform.localRotation = Quaternion.Euler(12f, -8f, 0f);
            c.Kit.Box(_nozzle.transform, "Grip", new Vector3(0f, -0.04f, -0.05f), new Vector3(0.05f, 0.13f, 0.06f), green, collider: false);
            c.Kit.Box(_nozzle.transform, "Body", new Vector3(0f, 0.03f, 0.03f), new Vector3(0.06f, 0.06f, 0.16f), dark, collider: false);
            GameObject spout = c.Kit.Box(_nozzle.transform, "Spout", new Vector3(0f, 0.01f, 0.16f), new Vector3(0.025f, 0.025f, 0.14f), c.P.Lit(new Color(0.6f, 0.6f, 0.62f), 0.7f), collider: false);
            spout.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            _tip = new GameObject("Tip").transform;
            _tip.SetParent(_nozzle.transform, false);
            _tip.localPosition = new Vector3(0f, -0.01f, 0.23f);

            // The can: red, a handle on top, a spout at the front corner.
            _can = new GameObject("Held jerry can");
            _can.transform.SetParent(_view, false);
            _can.transform.localPosition = new Vector3(0.26f, -0.4f, 0.55f);
            _can.transform.localRotation = Quaternion.Euler(8f, -20f, 0f);
            c.Kit.Box(_can.transform, "Can", Vector3.zero, new Vector3(0.12f, 0.3f, 0.26f), red, collider: false);
            c.Kit.Box(_can.transform, "Handle", new Vector3(0f, 0.17f, -0.03f), new Vector3(0.03f, 0.04f, 0.14f), red, collider: false);
            GameObject canSpout = c.Kit.Box(_can.transform, "Spout", new Vector3(0f, 0.18f, 0.12f), new Vector3(0.03f, 0.03f, 0.12f), dark, collider: false);
            canSpout.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);

            foreach (GameObject held in new[] { _nozzle, _can })
            {
                foreach (Renderer r in held.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                SetLayer(held.transform, 2);
                held.SetActive(false);
            }

            _hose = Line("Hose", c.P.Lit(new Color(0.07f, 0.07f, 0.08f), 0.3f), 0.03f, 16);
            _stream = Line("Fuel stream", fuel, 0.018f, 12);
            for (int i = 0; i < _drops.Length; i++)
            {
                _drops[i] = c.Kit.Sphere(transform, "Fuel drop", Vector3.zero, 0.035f, fuel).transform;
                _drops[i].SetParent(null, true);
                _drops[i].gameObject.SetActive(false);
            }
        }

        private LineRenderer Line(string name, Material m, float width, int points)
        {
            var go = new GameObject(name);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = m;
            line.widthMultiplier = width;
            line.positionCount = points;
            line.useWorldSpace = true;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return line;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }

        // ------------------------------------------------------------------ taking and putting down

        public bool CanTake => Held == Tool.None && !_player.HandsFull && !(_c.Driver != null && _c.Driver.IsDriving);

        public void TakeNozzle(FuelPump pump)
        {
            if (!CanTake) return;
            Held = Tool.Nozzle;
            Pump = pump;
            Pumped = 0;
            _player.HandsFull = true;
            _nozzle.SetActive(true);
            _hose.gameObject.SetActive(true);
        }

        /// <summary>Puts the nozzle back and pays for what went through it. Returns the receipt line.</summary>
        public string HangUp(string why = null)
        {
            if (Held != Tool.Nozzle) return null;
            string receipt = null;
            if (Pumped > 0.05)
            {
                decimal cost = PumpedCost;
                string error = _c.Game.Economy.Spend(cost, "Fuel", _c.Game.Clock.Now);
                decimal bank = _c.Game.Economy.Bank.Balance;
                receipt = error ?? $"{Pumped.ToString("0.0", CultureInfo.InvariantCulture)} L  -${cost.ToString("N2", CultureInfo.InvariantCulture)}   ·   Bank {(bank < 0 ? "-$" : "$")}{System.Math.Abs(bank).ToString("N2", CultureInfo.InvariantCulture)}";
            }
            Drop();
            string text = why != null ? why + (receipt != null ? "   " + receipt : "") : receipt;
            if (text != null) _c.Hud?.ShowToast(text);
            return text;
        }

        public void BuyCan()
        {
            if (Fleet.JerryCan >= 0) return;
            string error = _c.Game.Economy.Spend(CanPrice, "Jerry can", _c.Game.Clock.Now);
            if (error != null) { _c.Hud?.ShowToast(error); return; }
            Fleet.JerryCan = 0;
            TakeCan();
            _c.Hud?.ShowToast("Jerry can bought (10 L). [E] at a pump to fill it, [J] to take it out or put it away.");
        }

        public void TakeCan()
        {
            if (Fleet.JerryCan < 0 || !CanTake) return;
            Held = Tool.Can;
            _player.HandsFull = true;
            _can.SetActive(true);
        }

        public void PutAwayCan()
        {
            if (Held == Tool.Can) Drop();
        }

        /// <summary>Fills the can in your hands from a pump, paid there and then.</summary>
        public string FillCan()
        {
            if (Held != Tool.Can) return null;
            double liters = CanCapacity - Fleet.JerryCan;
            if (liters < 0.05) return "The can's full.";
            decimal cost = Trading.Money.RoundCents((decimal)liters * FuelStation.PricePerLiter);
            string error = _c.Game.Economy.Spend(cost, "Fuel (jerry can)", _c.Game.Clock.Now);
            if (error != null) return error;
            Fleet.JerryCan = CanCapacity;
            return $"Filled the can: {liters.ToString("0.0", CultureInfo.InvariantCulture)} L  -${cost.ToString("N2", CultureInfo.InvariantCulture)}";
        }

        private void Drop()
        {
            Held = Tool.None;
            Pump = null;
            Pumped = 0;
            Flowing = false;
            _player.HandsFull = false;
            _nozzle.SetActive(false);
            _can.SetActive(false);
            _hose.gameObject.SetActive(false);
            ShowStream(false, Vector3.zero, Vector3.zero);
            _c.Hud?.SetStatus(null);
        }

        // ------------------------------------------------------------------ fuel going in

        /// <summary>
        /// Moves fuel from what you're holding into <paramref name="car"/> for <paramref name="seconds"/>: the pump stops
        /// when the tank's full or your card can't cover more; the can stops when it's empty. Returns the litres moved.
        /// </summary>
        public double Flow(OwnedVehicle car, float seconds)
        {
            if (car == null || car.Kind != VehicleKind.Car || seconds <= 0f) return 0;
            double room = car.FuelCapacity - car.FuelLiters;
            double liters;
            if (Held == Tool.Nozzle)
            {
                liters = System.Math.Min(PumpRate * seconds, room);
                // Stop where the card would decline: what's pumped so far plus this must be covered.
                double affordable = (double)(_c.Game.Economy.Bank.Balance / FuelStation.PricePerLiter) - Pumped;
                liters = System.Math.Min(liters, System.Math.Max(0, affordable));
                Pumped += liters;
            }
            else if (Held == Tool.Can)
            {
                liters = System.Math.Min(System.Math.Min(PourRate * seconds, room), Fleet.JerryCan);
                Fleet.JerryCan -= liters;
            }
            else return 0;
            if (liters > 0) Fleet.SetFuel(car, car.FuelLiters + liters);
            return liters;
        }

        /// <summary>The owned, parked car you're aiming at within reach, and where on it.</summary>
        private OwnedVehicle Aimed(out Vector3 at)
        {
            at = default;
            Physics.SyncTransforms();
            if (!Physics.Raycast(new Ray(_view.position, _view.forward), out RaycastHit hit, FillReach + 0.6f, ~0, QueryTriggerInteraction.Ignore)) return null;
            ParkedVehicle parked = hit.collider.GetComponentInParent<ParkedVehicle>();
            if (parked == null || parked.Vehicle.Kind != VehicleKind.Car || parked.Vehicle.State != VehicleState.Parked || parked.Vehicle.TestDrive) return null;
            // The filler flap: rear quarter, on whichever side you're standing, at about hip height.
            Transform car = parked.transform;
            float side = Mathf.Sign(car.InverseTransformPoint(_view.position).x);
            at = car.TransformPoint(new Vector3(side * 0.9f, 0.95f, -1.15f));
            if (Vector3.Distance(at, _view.position) > FillReach + 0.8f) return null;
            return parked.Vehicle;
        }

        private void Update()
        {
            if (_c == null) return;
            RoadsideArrivals();
            if (_c.Driver != null && _c.Driver.IsDriving && Held != Tool.None)
            {
                if (Held == Tool.Nozzle) HangUp("You drove off: the nozzle's back on the pump.");
                else Drop();
                return;
            }
            Keyboard k = Keyboard.current;
            bool control = _player.ControlEnabled && !_player.Suspended;
            if (control && k != null && k.jKey.wasPressedThisFrame)
            {
                if (Held == Tool.Can) PutAwayCan();
                else if (Held == Tool.None)
                {
                    if (Fleet.JerryCan < 0) _c.Hud?.ShowToast("No jerry can: Tidewater Fuel sells them.");
                    else TakeCan();
                }
            }
            if (Held == Tool.None) return;

            Flowing = false;
            if (Held == Tool.Nozzle)
            {
                Vector3 outlet = Outlet();
                if (Vector3.Distance(outlet, _player.transform.position) > HoseReach + 1.5f)
                {
                    HangUp("The hose won't reach: the nozzle's back on the pump.");
                    return;
                }
                DrawHose(outlet, _nozzle.transform.position - _nozzle.transform.forward * 0.1f);
            }

            Vector3 at = default;
            OwnedVehicle car = control ? Aimed(out at) : null;
            bool full = car != null && car.FuelCapacity - car.FuelLiters < 0.01;
            bool hasFuel = Held == Tool.Nozzle || Fleet.JerryCan > 0.01;
            if (control && car != null && !full && hasFuel && _player.Input.Attack.IsPressed())
            {
                double moved = Flow(car, Time.deltaTime);
                Flowing = moved > 0;
                if (Flowing) ShowFlap(at, car);
                if (Flowing) ShowStream(true, Held == Tool.Nozzle ? _tip.position : _can.transform.TransformPoint(new Vector3(0f, 0.22f, 0.18f)), at);
            }
            if (!Flowing) ShowStream(false, Vector3.zero, Vector3.zero);
            if (!Flowing && _flap != null && _flap.activeSelf) _flap.SetActive(false);

            var c = CultureInfo.InvariantCulture;
            string where = car == null ? "aim at your car" : full ? $"{car.Name}: tank full" : $"[hold LMB] fill {car.Name} ({car.FuelFraction.ToString("P0", c)})";
            _c.Hud?.SetStatus(Held == Tool.Nozzle
                ? $"Pump · {Pumped.ToString("0.0", c)} L · ${PumpedCost.ToString("N2", c)}   {where}   [E at the pump] hang up"
                : $"Jerry can · {Fleet.JerryCan.ToString("0.0", c)} / {CanCapacity:0} L   {(Fleet.JerryCan > 0.01 ? where : "empty: fill it at a pump")}   [J] put away");
        }

        /// <summary>Where the hose leaves the pump: the side you're on.</summary>
        private Vector3 Outlet()
        {
            Vector3 p = Pump.transform.position;
            float side = Mathf.Sign(_player.transform.position.x - p.x);
            return p + new Vector3(side * 0.37f, 1f, 0f);
        }

        private void DrawHose(Vector3 from, Vector3 to)
        {
            // A hanging hose: a parabola that sags more the slacker it is.
            float slack = Mathf.Max(0.15f, (HoseReach + 1f - Vector3.Distance(from, to)) * 0.35f);
            for (int i = 0; i < _hose.positionCount; i++)
            {
                float t = i / (_hose.positionCount - 1f);
                Vector3 p = Vector3.Lerp(from, to, t);
                p.y -= slack * 4f * t * (1f - t);
                p.y = Mathf.Max(p.y, from.y - 1.05f); // lies on the ground rather than through it
                _hose.SetPosition(i, p);
            }
        }

        /// <summary>The fuel: an arc from the spout to where it's going, with drops running down it.</summary>
        private void ShowStream(bool on, Vector3 from, Vector3 to)
        {
            if (_stream.gameObject.activeSelf != on) _stream.gameObject.SetActive(on);
            foreach (Transform d in _drops) if (d.gameObject.activeSelf != on) d.gameObject.SetActive(on);
            if (!on) return;
            _flowTime += Time.deltaTime;
            // Falls a little as it goes, like a pour.
            Vector3 Arc(float t) => Vector3.Lerp(from, to, t) - Vector3.up * (0.06f * 4f * t * (1f - t));
            for (int i = 0; i < _stream.positionCount; i++)
            {
                float t = i / (_stream.positionCount - 1f);
                // A little wobble so it reads as liquid, not a rod.
                Vector3 wobble = new Vector3(Mathf.Sin(_flowTime * 23f + t * 9f), 0f, Mathf.Cos(_flowTime * 19f + t * 7f)) * 0.006f * t;
                _stream.SetPosition(i, Arc(t) + wobble);
            }
            _stream.widthMultiplier = 0.016f + 0.004f * Mathf.Sin(_flowTime * 31f);
            for (int i = 0; i < _drops.Length; i++)
                _drops[i].position = Arc((_flowTime * 2.4f + i / (float)_drops.Length) % 1f);
        }

        private GameObject _flap;

        /// <summary>The open filler flap on the car while fuel's going in.</summary>
        private void ShowFlap(Vector3 at, OwnedVehicle car)
        {
            if (_flap == null)
            {
                _flap = _c.Kit.Box(null, "Filler flap", Vector3.zero, new Vector3(0.02f, 0.14f, 0.14f), _c.P.Lit(new Color(0.05f, 0.05f, 0.05f), 0.3f), collider: false);
                _flap.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            GameObject shown = _c.FleetView != null ? _c.FleetView.Shown(car) : null;
            Quaternion yaw = shown != null ? Quaternion.Euler(0f, shown.transform.eulerAngles.y, 0f) : Quaternion.identity;
            _flap.transform.SetPositionAndRotation(at, yaw);
            if (!_flap.activeSelf) _flap.SetActive(true);
        }

        // ------------------------------------------------------------------ roadside

        /// <summary>
        /// What Tidewater says when you call. With a car of yours running on fumes and nothing already coming, it sends a
        /// van: 10 L to the car in 20 minutes, the call-out fee plus the fuel on your card.
        /// </summary>
        public string CallTidewater()
        {
            DateTime now = _c.Game.Clock.Now;
            if (Fleet.RoadsideCar != null)
            {
                int left = Mathf.Max(1, (int)System.Math.Ceiling((Fleet.RoadsideAt - now).TotalMinutes));
                return $"Tidewater Fuel roadside. Your van's on its way, about {left} minute{(left == 1 ? "" : "s")} out. Sit tight.";
            }
            OwnedVehicle dry = null;
            foreach (OwnedVehicle v in Fleet.Vehicles)
                if (v.Kind == VehicleKind.Car && !v.TestDrive && v.FuelLiters < 3 && (dry == null || v.FuelLiters < dry.FuelLiters)) dry = v;
            if (dry == null) return "Tidewater Fuel. Pull up to any pump and pay at the pump. Run dry out there, call us and we'll bring you ten litres.";
            decimal cost = RoadsideFee + Trading.Money.RoundCents((decimal)RoadsideLiters * FuelStation.PricePerLiter);
            string error = _c.Game.Economy.Spend(cost, "Tidewater roadside fuel", now);
            if (error != null) return $"Tidewater Fuel roadside. That's ${cost.ToString("N2", CultureInfo.InvariantCulture)} and your card's been declined, sorry.";
            Fleet.RoadsideCar = dry.Id;
            Fleet.RoadsideAt = now.AddMinutes(RoadsideMinutes);
            return $"Tidewater Fuel roadside. Out of gas in the {dry.Name}? We'll have a van there in about {RoadsideMinutes} minutes with ten litres. ${cost.ToString("N2", CultureInfo.InvariantCulture)} on your card.";
        }

        private void RoadsideArrivals()
        {
            if (Fleet.RoadsideCar == null || _c.Game.Clock.Now < Fleet.RoadsideAt) return;
            OwnedVehicle car = Fleet.Find(Fleet.RoadsideCar);
            Fleet.RoadsideCar = null;
            if (car == null) return;
            Fleet.SetFuel(car, car.FuelLiters + RoadsideLiters);
            _c.Hud?.ShowToast($"Tidewater Fuel: 10 L delivered to your {car.Name}. Enough to reach a pump.", 7f);
        }
    }
}
