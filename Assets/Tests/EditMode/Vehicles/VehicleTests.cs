using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpeningBell.Tests
{
    public class VehicleTests
    {
        private static VehicleCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset").CreateCatalog();

        private static VehicleModel Model(string id)
        {
            Assert.IsTrue(Catalog().TryGetModel(id, out VehicleModel m), id);
            return m;
        }

        private static readonly RideGround Asphalt = new RideGround { Roughness = 0.3 };
        private static readonly RideGround Grass = new RideGround { Roughness = 1 };

        /// <summary>Rides for <paramref name="seconds"/> and returns the state.</summary>
        private static RideState Ride(VehicleModel m, double seconds, RideInput input, RideGround ground, RideSpec spec = null,
            double startSpeed = 0, double battery = double.NaN, double gameSpeed = 30)
        {
            var s = new RideState { Speed = startSpeed };
            double wh = double.IsNaN(battery) ? m.Spec.BatteryWh : battery;
            for (double t = 0; t < seconds && !s.Bailed; t += 0.02)
                RideDynamics.Step(m.Kind, spec ?? m.Spec, s, input, ground, 0.02, gameSpeed, 1, 1, ref wh);
            return s;
        }

        private static readonly RideInput Pedal = new RideInput { Throttle = 1 };

        /// <summary>
        /// Every car mesh (traffic's and the showroom's) has CarFactory's layout: a "body" and four named wheels, standing on the
        /// ground, front towards +z (front wheels ahead of the back ones), sized like a car once scaled.
        /// </summary>
        [Test]
        public void CarMeshes_HaveTheCarKitLayout()
        {
            var library = AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset");
            var problems = new System.Collections.Generic.List<string>();
            // Traffic's models and every buyable car's.
            foreach (string name in library.TrafficMix.Concat(library.Models.Where(m => !string.IsNullOrEmpty(m.Mesh)).Select(m => m.Mesh)).Distinct())
            {
                GameObject car = library.CarMesh(name);
                if (car == null) { problems.Add(name + ": missing"); continue; }
                var parts = car.GetComponentsInChildren<Renderer>().ToDictionary(r => r.name, r => r.bounds);
                string[] missing = OpeningBell.City.CarFactory.WheelNames.Append("body").Where(n => !parts.ContainsKey(n)).ToArray();
                if (missing.Length > 0) { problems.Add(name + ": no " + string.Join(", ", missing)); continue; }
                if (parts["wheel-front-left"].center.z <= parts["wheel-back-left"].center.z) problems.Add(name + ": faces -z");
                if (Mathf.Abs(parts["wheel-front-left"].min.y) > 0.05f) problems.Add(name + $": wheels at y {parts["wheel-front-left"].min.y:F2}");
                float length = parts["body"].size.z * OpeningBell.City.CarFactory.Scale;
                if (length < 3f || length > 6f) problems.Add(name + $": {length:F1} m long");
            }
            Assert.IsEmpty(problems, string.Join("; ", problems));
        }

        [Test]
        public void Bikes_TopSpeedsFollowFromPhysics()
        {
            double road = Ride(Model("bike_road"), 90, Pedal, Asphalt).Speed;
            double commuter = Ride(Model("bike_commuter"), 90, Pedal, Asphalt).Speed;
            double mtb = Ride(Model("bike_mtb"), 90, Pedal, Asphalt).Speed;
            double bmx = Ride(Model("bike_bmx"), 90, Pedal, Asphalt).Speed;
            TestContext.WriteLine($"road {road:F2}  commuter {commuter:F2}  mtb {mtb:F2}  bmx {bmx:F2} m/s");

            Assert.That(road, Is.InRange(9.0, 11.5), "road bike at an easy 230 W: ~35 km/h");
            Assert.Greater(road, commuter);
            Assert.Greater(road, mtb);
            Assert.Less(bmx, commuter, "one gear: the BMX spins out");
            foreach (double v in new[] { commuter, mtb, bmx }) Assert.That(v, Is.InRange(6.0, 10.0));
            Assert.Greater(Ride(Model("bike_road"), 90, new RideInput { Throttle = 1, Sprint = true }, Asphalt).Speed, road + 2, "sprinting is faster");
        }

        [Test]
        public void MountainBike_BeatsRoadBike_OnGrass_AndClimbs()
        {
            Assert.Greater(Ride(Model("bike_mtb"), 60, Pedal, Grass).Speed, Ride(Model("bike_road"), 60, Pedal, Grass).Speed);

            // Very steep ramp from a standstill: the lowest gear decides who gets going.
            var ramp = new RideGround { Grade = 0.25, Roughness = 0.2 };
            Assert.Greater(Ride(Model("bike_mtb"), 5, Pedal, ramp).Distance, Ride(Model("bike_bmx"), 5, Pedal, ramp).Distance);
        }

        [Test]
        public void Coasting_Braking_AndSteering()
        {
            VehicleModel road = Model("bike_road");
            RideState coast = Ride(road, 10, default, Asphalt, startSpeed: 8);
            Assert.That(coast.Speed, Is.InRange(5.0, 7.9), "slows gently when coasting");
            RideState brake = Ride(road, 2, new RideInput { Brake = 1 }, Asphalt, startSpeed: 8);
            Assert.AreEqual(0, brake.Speed, 1e-9, "full brakes stop from 29 km/h within 2 s");

            Assert.AreEqual(0, RideDynamics.BikeYawRate(road.Spec, 0, 0.5), "no turning at a standstill");
            double fast = RideDynamics.BikeYawRate(road.Spec, 10, 0.5), slow = RideDynamics.BikeYawRate(road.Spec, 2, 0.5);
            Assert.AreEqual(RideDynamics.G * Math.Tan(0.5) / 10, fast, 1e-9, "at speed: lean decides the turn");
            Assert.Less(slow, RideDynamics.G * Math.Tan(0.5) / 2, "slow: limited by the handlebars");
            Assert.Greater(RideDynamics.BikeYawRate(Model("bike_bmx").Spec, 2, 0.5), slow, "a BMX turns tighter when slow");
        }

        [Test]
        public void EBike_AssistsToItsCutoff_DrainsByGameTime_AndIsHeavyWhenFlat()
        {
            VehicleModel e = Model("ebike_commuter");
            double commuter = Ride(Model("bike_commuter"), 8, Pedal, Asphalt).Speed;
            RideState assisted = Ride(e, 8, Pedal, Asphalt);
            Assert.Greater(assisted.Speed, commuter + 1, "motor helps");
            Assert.That(Ride(e, 90, Pedal, Asphalt).Speed, Is.InRange(8.0, 10.0), "assist fades out at 20 mph");
            Assert.Less(Ride(e, 90, Pedal, Asphalt, battery: 0).Speed, Ride(Model("bike_commuter"), 90, Pedal, Asphalt).Speed + 0.2,
                "flat battery: just a heavy bike");

            // Energy follows game time: a minute of riding at 30× is half an hour of motor use.
            double wh = e.Spec.BatteryWh, whFast = e.Spec.BatteryWh;
            var s = new RideState();
            var s2 = new RideState();
            for (int i = 0; i < 3000; i++)
            {
                RideDynamics.Step(e.Kind, e.Spec, s, Pedal, Asphalt, 0.02, 1, 1, 1, ref wh);
                RideDynamics.Step(e.Kind, e.Spec, s2, Pedal, Asphalt, 0.02, 30, 1, 1, ref whFast);
            }
            double used = e.Spec.BatteryWh - wh, usedFast = e.Spec.BatteryWh - whFast;
            Assert.AreEqual(30, usedFast / used, 0.01);
            Assert.That(usedFast / e.Spec.BatteryWh, Is.InRange(0.08, 0.35), "a minute's ride at game speed: a noticeable, not brutal, bite");
        }

        [Test]
        public void Skateboard_PushCoastGrassBearingsAndTrucks()
        {
            VehicleModel street = Model("skate_street"), longboard = Model("skate_longboard");
            double pushed = Ride(street, 20, Pedal, Asphalt).Speed;
            Assert.That(pushed, Is.InRange(4.0, 6.5), "kicking speed");
            Assert.That(Ride(street, 10, default, Asphalt, startSpeed: 5).Speed, Is.InRange(2.5, 4.9), "rolls on");
            Assert.AreEqual(0, Ride(street, 3, default, Grass, startSpeed: 5).Speed, 1e-9, "grass stops a board dead");

            RideSpec precision = street.Spec.Clone();
            Catalog().TryGetPart("bearings_precision", out PartSpec bearings);
            bearings.ApplyTo(precision);
            Assert.Greater(Ride(street, 20, default, Asphalt, precision, startSpeed: 5).Distance,
                Ride(street, 20, default, Asphalt, startSpeed: 5).Distance, "better bearings coast further");
            Assert.Greater(Ride(longboard, 20, default, Asphalt, startSpeed: 5).Distance,
                Ride(street, 20, default, Asphalt, startSpeed: 5).Distance, "big soft wheels roll further on asphalt");

            RideSpec loose = street.Spec.Clone(), tight = street.Spec.Clone();
            loose.TruckLooseness = 0.85;
            tight.TruckLooseness = 0.15;
            var turn = new RideInput { Steer = 1 };
            Assert.Greater(Ride(street, 0.1, turn, Asphalt, loose, startSpeed: 3).YawRate, Ride(street, 0.1, turn, Asphalt, tight, startSpeed: 3).YawRate);
            Assert.Greater(RideDynamics.StableSpeed(longboard.Spec), RideDynamics.StableSpeed(Model("skate_cruiser").Spec), "long decks stay calm");

            double stable = RideDynamics.StableSpeed(street.Spec);
            Assert.IsFalse(Ride(street, 0.1, default, Asphalt, startSpeed: stable).Bailed);
            Assert.IsTrue(Ride(street, 0.1, default, Asphalt, startSpeed: stable * 1.4).Bailed, "speed wobbles, then a bail");
            Assert.Greater(Ride(street, 6, default, new RideGround { Grade = -0.08, Roughness = 0.3 }, startSpeed: 2).Speed, 4, "rolls away downhill");
        }

        [Test]
        public void Fleet_OwnsWearsServicesChargesAndSaves()
        {
            var fleet = new Fleet(Catalog());
            var now = new DateTime(2030, 1, 7, 10, 0, 0);
            OwnedVehicle board = fleet.Add("skate_street", 95m, now, 0, 0, 0, 0);
            OwnedVehicle bike = fleet.Add("ebike_commuter", 1650m, now, 99.5, 0, -6.2, 90);
            Assert.AreEqual("V-0001", board.Id);
            Assert.AreEqual("V-0002", bike.Id);
            Assert.AreEqual(VehicleState.Carried, board.State, "boards go in your hands");
            Assert.AreEqual(VehicleState.Parked, bike.State);
            OwnedVehicle second = fleet.Add("skate_cruiser", 120m, now, 0, 0, 0, 0);
            Assert.AreEqual(VehicleState.Stored, board.State, "one board in hand; the other goes home");
            Assert.AreSame(second, fleet.Carried);

            fleet.Ridden(bike, 20_000);
            Assert.AreEqual(0.8, bike.TireCondition, 1e-9, "1% per km");
            Assert.AreEqual(0, fleet.Impact(bike, 3.5), "a bump");
            Assert.AreEqual(0.12, fleet.Impact(bike, 8), 1e-9);
            fleet.Service(bike, ServiceKind.TuneUp, now, 60m);
            fleet.Service(bike, ServiceKind.NewTires, now, 45m);
            Assert.AreEqual(1, bike.Condition);
            Assert.AreEqual(1, bike.TireCondition);

            // Charging: 250 W from empty; 80% in 1.6 h, the rest slower. One long step equals many short ones.
            fleet.SetBattery(bike, 0);
            fleet.Charge(bike, 250, 3600 * 1.6);
            Assert.AreEqual(0.8, bike.BatteryFraction, 1e-6);
            OwnedVehicle twin = fleet.Add("ebike_commuter", 1650m, now, 0, 0, 0, 0);
            fleet.SetBattery(twin, 0);
            for (int i = 0; i < 8 * 60; i++) fleet.Charge(twin, 250, 60);
            fleet.SetBattery(bike, 0);
            fleet.Charge(bike, 250, 8 * 3600);
            Assert.AreEqual(twin.BatteryWh, bike.BatteryWh, 1e-6);
            Assert.AreEqual(bike.BatteryCapacityWh, bike.BatteryWh, 1e-6, "full overnight");

            Assert.IsNull(fleet.InstallPart(bike, "battery_extended", now, 420m));
            Assert.AreEqual(750, bike.BatteryCapacityWh, 1e-9);
            Assert.AreEqual(1, bike.BatteryFraction, 1e-9, "charge level kept");
            StringAssert.Contains("doesn't fit", fleet.InstallPart(bike, "trucks_loose", now, 55m));
            Assert.IsNull(fleet.InstallPart(second, "trucks_tight", now, 55m));
            Assert.IsNull(fleet.InstallPart(second, "trucks_loose", now, 55m));
            CollectionAssert.AreEqual(new[] { "trucks_loose" }, second.Parts, "same slot replaces");
            Assert.AreEqual(0.85, fleet.Catalog.EffectiveSpec(second).TruckLooseness, 1e-9);

            Assert.Less(bike.ResaleValue(now.AddMonths(12)), bike.ResaleValue(now), "depreciates");
            Assert.Less(bike.ResaleValue(now), 1650m * 0.81m, "off the lot");

            fleet.SetState(bike, VehicleState.Riding);
            string json = JsonUtility.ToJson(fleet.CaptureState());
            var restored = new Fleet(Catalog());
            restored.RestoreState(JsonUtility.FromJson<FleetSaveData>(json));
            Assert.AreEqual(fleet.Vehicles.Count, restored.Vehicles.Count);
            OwnedVehicle b = restored.Find(bike.Id);
            Assert.AreEqual(VehicleState.Parked, b.State, "a ride in progress saves as parked");
            Assert.AreEqual(bike.BatteryWh, b.BatteryWh, 1e-9);
            Assert.AreEqual(bike.Odometer, b.Odometer, 1e-9);
            Assert.AreEqual(bike.History.Count, b.History.Count);
            Assert.AreEqual(1650m, b.PurchasePrice);
            CollectionAssert.AreEqual(bike.Parts, b.Parts);
            Assert.AreSame(b, restored.LastRidden);
            Assert.AreEqual("V-0005", restored.Add("bike_bmx", 340m, now, 0, 0, 0, 0).Id, "ids keep counting after a load");
        }
    }
}
