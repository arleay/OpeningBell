using System.Collections.Generic;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Brings one of your cars to you from the phone: it turns up in the nearest empty kerb or lot spot (step out of a
    /// shop and it's waiting on the street in front). Free, instant, never on top of anything.
    /// </summary>
    public static class Valet
    {
        /// <summary>How far to look for a spot: about a block each way.</summary>
        public const float Reach = 120f;
        /// <summary>Closer than this, it's already here.</summary>
        public const float Here = 12f;

        /// <summary>Your cars, in the order you bought them (a dealer's test car isn't yours).</summary>
        public static List<OwnedVehicle> Cars(Fleet fleet)
        {
            var cars = new List<OwnedVehicle>();
            foreach (OwnedVehicle v in fleet.Vehicles)
                if (v.Kind == VehicleKind.Car && !v.TestDrive) cars.Add(v);
            return cars;
        }

        public static float Distance(OwnedVehicle v, Vector3 from) =>
            Vector2.Distance(new Vector2((float)v.X, (float)v.Z), new Vector2(from.x, from.z));

        /// <summary>Parks the car in the free spot nearest <paramref name="me"/>. An error message, or null.</summary>
        public static string Bring(CityContext c, OwnedVehicle v, Vector3 me)
        {
            if (c.Driver != null && c.Driver.IsDriving) return c.Driver.Vehicle == v ? "You're driving it." : "Get out of the car first.";
            if (v.State != VehicleState.Parked) return $"Your {v.Name} is busy.";
            if (Distance(v, me) < Here && Mathf.Abs((float)v.Y - me.y) < 3f) return $"Your {v.Name} is right here.";
            if (c.Parked == null || !c.Parked.NearestFree(me, Reach, out Vector3 at, out float yaw))
                return "No free parking nearby. Try from a street.";
            c.Game.Vehicles.Park(v, at.x, at.y, at.z, yaw);
            c.FleetView.Rebuild(v);
            return null;
        }
    }
}
