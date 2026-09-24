using System.Collections.Generic;
using OpeningBell.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// My Cars: every car you own with where it is, fuel and condition, and "Bring here", which parks it in the
    /// nearest free spot to you (<see cref="Valet"/>).
    /// </summary>
    internal sealed class GarageApp : PhoneScreen
    {
        public override PhoneAppId Id => PhoneAppId.Garage;

        private readonly Label _status;
        private readonly ScrollView _list;

        public string StatusText => _status.text;

        public GarageApp(Phone phone) : base(phone)
        {
            Header(Root, "My Cars");
            _status = PhoneKit.Label(Root, "", 13f, PhoneKit.Muted);
            _status.name = "garage-status";
            _status.style.marginLeft = _status.style.marginRight = 18f;
            _status.style.marginBottom = 8f;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _list = List(Root);
        }

        public override void Opened() => _status.text = "Tap Bring here and it's parked at the nearest free spot.";

        private Vector3 Me => Phone.Player.transform.position;

        /// <summary>Brings a car to the nearest free spot and puts the phone away (or says why not).</summary>
        public string Bring(OwnedVehicle v)
        {
            string error = Valet.Bring(Phone.City, v, Me);
            if (error != null)
            {
                _status.text = error;
                return error;
            }
            Phone.Close();
            Phone.Hud.ShowToast($"Your {v.Name} is parked {Mathf.RoundToInt(Valet.Distance(v, Me))} m away.", 4f);
            return null;
        }

        public override void Refresh()
        {
            List<OwnedVehicle> cars = Valet.Cars(Phone.Game.Vehicles);
            var ids = new List<string>();
            foreach (OwnedVehicle v in cars) ids.Add(v.Id);
            if (!Rebuild(string.Join(",", ids))) return;
            _list.Clear();
            if (cars.Count == 0)
            {
                var none = PhoneKit.Label(_list.contentContainer, "No cars yet. The dealers and the classifieds have plenty.", 15f, PhoneKit.Muted);
                none.style.whiteSpace = WhiteSpace.Normal;
                none.style.marginTop = 20f;
                return;
            }
            foreach (OwnedVehicle v in cars)
            {
                VisualElement card = Card(_list.contentContainer);
                card.name = "car-" + v.Id;
                PhoneKit.Pad(card, 14f, 12f);
                PhoneKit.Label(card, v.Name, 17f, PhoneKit.Text, true);
                var where = PhoneKit.Label(card, "", 13f, PhoneKit.Muted);
                where.style.marginTop = 2f;
                var stats = PhoneKit.Label(card, "", 13f, PhoneKit.Muted);
                OwnedVehicle car = v;
                Live(() =>
                {
                    bool driving = Phone.City.Driver != null && Phone.City.Driver.Vehicle == car;
                    float d = Valet.Distance(car, Me);
                    where.text = driving ? "You're driving it" : d < 1000f ? $"{Mathf.RoundToInt(d / 5f) * 5} m away" : $"{d / 1000f:0.0} km away";
                    stats.text = $"Fuel {car.FuelFraction:P0}  ·  condition {car.Condition:P0}";
                });
                var bring = PhoneKit.Pill(card, "Bring here", PhoneKit.Blue, Color.white, () => Bring(car));
                bring.name = "bring";
                bring.style.alignSelf = Align.FlexStart;
                bring.style.marginTop = 8f;
            }
        }
    }
}
