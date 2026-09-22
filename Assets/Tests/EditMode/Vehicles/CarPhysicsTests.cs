using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpeningBell.Tests
{
    public class CarPhysicsTests
    {
        private static VehicleLibrary Library() =>
            AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset");

        private static CarSpec Spec(string id)
        {
            Assert.IsTrue(Library().CreateCatalog().TryGetModel(id, out VehicleModel m), id);
            return m.Car;
        }

        [Test]
        public void Engines_TorqueCurvesAndPower_MatchTheirCharacter()
        {
            CarSpec sedan = Spec("car_sedan");
            Assert.AreEqual(175, CarPhysics.Torque(sedan, 4000), 1e-9);
            Assert.AreEqual((168 + 175) / 2.0, CarPhysics.Torque(sedan, 3500), 1e-9, "linear between points");
            Assert.AreEqual(0, CarPhysics.Torque(sedan, sedan.RedlineRpm + 1), "past the limiter");

            double kw(string id) => CarPhysics.PeakPower(Spec(id)) / 1000;
            Assert.That(kw("car_sedan"), Is.InRange(85, 110), "a 1.8 L family car");
            Assert.That(kw("car_pickup"), Is.InRange(220, 300), "a 5.0 L V8");
            Assert.That(kw("car_super"), Is.InRange(420, 560), "a V10 supercar");
            Assert.Greater(CarPhysics.Torque(Spec("car_pickup"), 1500), CarPhysics.Torque(Spec("car_hatch"), 1500), "big V8: low-end grunt");
            Assert.Greater(CarPhysics.Torque(Spec("car_hatch"), 2500), CarPhysics.Torque(sedan, 2500) * 1.6, "turbo: fat mid-range");

            foreach (VehicleModel m in Library().CreateCatalog().Models.Where(x => x.Kind == VehicleKind.Car))
            {
                Assert.AreEqual(m.Car.CurveRpm.Length, m.Car.CurveTorque.Length, m.Id);
                Assert.NotNull(Library().CarMesh(m.Mesh), $"{m.Id} has a 3D model ({m.Mesh})");
            }
        }

        [Test]
        public void AutomaticGearbox_ShiftsWithRevsAndThrottle()
        {
            CarSpec s = Spec("car_sedan");
            double WheelRpmAt(double engineRpm, int gear) => engineRpm / (s.GearRatios[gear] * s.FinalDrive);
            Assert.AreEqual(1, CarPhysics.AutoShift(s, 0, WheelRpmAt(6100, 0), 1), "full throttle: up near the redline");
            Assert.AreEqual(0, CarPhysics.AutoShift(s, 0, WheelRpmAt(4500, 0), 1), "…not before");
            Assert.AreEqual(1, CarPhysics.AutoShift(s, 0, WheelRpmAt(3500, 0), 0.2), "gentle: short-shift (~3,300 rpm)");
            Assert.AreEqual(1, CarPhysics.AutoShift(s, 2, WheelRpmAt(1300, 2), 0.3), "slowing: down a gear");
            Assert.AreEqual(2, CarPhysics.AutoShift(s, 3, WheelRpmAt(2600, 3), 1), "kickdown");
        }

        [Test]
        public void TorqueConverter_MultipliesAtAStandstill_AndCouplesUp()
        {
            CarSpec s = Spec("car_sedan");
            double stall = CarPhysics.WheelTorque(s, 0, s.GearRatios[0], 1, out double stallRpm);
            Assert.AreEqual(s.StallRpm, stallRpm, 1e-6, "engine flares to the stall speed");
            double direct = CarPhysics.Torque(s, s.StallRpm) * s.GearRatios[0] * s.FinalDrive * s.DrivetrainEfficiency;
            Assert.AreEqual(direct * s.StallMultiplier, stall, 1e-6, "converter multiplication at stall");

            double wheelRpm = 3000 / (s.GearRatios[0] * s.FinalDrive);
            double coupled = CarPhysics.WheelTorque(s, wheelRpm, s.GearRatios[0], 1, out double rpm);
            Assert.AreEqual(3000, rpm, 1e-6);
            Assert.AreEqual(CarPhysics.Torque(s, 3000) * s.GearRatios[0] * s.FinalDrive * s.DrivetrainEfficiency, coupled, 1e-6, "locked up: no multiplication");
            Assert.Less(CarPhysics.WheelTorque(s, wheelRpm, s.GearRatios[0], 0, out _), 0, "off throttle: engine braking");
        }

        [Test]
        public void Tyres_PeakThenSlide_AndShareGripInACircle()
        {
            const double peak = 0.14;
            Assert.AreEqual(1, CarPhysics.LateralCurve(peak, peak), 1e-9, "full grip at the peak slip angle");
            Assert.That(CarPhysics.LateralCurve(peak * 3, peak), Is.InRange(0.8, 0.97), "past the peak: sliding, less grip");
            Assert.AreEqual(-CarPhysics.LateralCurve(0.05, peak), CarPhysics.LateralCurve(-0.05, peak), 1e-12);
            Assert.Less(CarPhysics.LateralCurve(0.02, peak), CarPhysics.LateralCurve(0.04, peak));

            Assert.AreEqual(1, CarPhysics.FrictionCircle(3000, 4000, 1, 6000), 1e-9, "5 kN of 6 kN available");
            Assert.AreEqual(0.6, CarPhysics.FrictionCircle(6000, 8000, 1, 6000), 1e-9, "10 kN asked, 6 kN given");
            Assert.AreEqual(0, CarPhysics.FrictionCircle(100, 0, 1, 0), "wheel in the air: no grip");
        }

        [Test]
        public void Fuel_FollowsWorkAndGameTime()
        {
            CarSpec s = Spec("car_sedan");
            Assert.AreEqual(0.3 * 30 + 0.8, CarPhysics.Fuel(s, 30_000, 3600), 1e-9, "30 kW for an hour plus idle");
            Assert.AreEqual(0.8 / 60, CarPhysics.Fuel(s, 0, 60), 1e-9, "idling a minute");
            Assert.Greater(CarPhysics.Fuel(Spec("car_pickup"), 30_000, 3600), CarPhysics.Fuel(Spec("car_van"), 30_000, 3600), "V8 thirstier than the diesel");
        }

        [Test]
        public void UsedCars_ArriveWithTheirHistory_AndSellOnce()
        {
            VehicleCatalog catalog = Library().CreateCatalog();
            UsedListing listing = catalog.Listings.First(l => l.Id == "used_sedan_1");
            var fleet = new Fleet(catalog);
            var now = new DateTime(2030, 1, 8, 9, 0, 0);
            OwnedVehicle car = fleet.AddUsed(listing, now, 14, -0.15, -10.15, 270);
            Assert.AreEqual(VehicleKind.Car, car.Kind);
            Assert.AreEqual(VehicleState.Parked, car.State);
            Assert.AreEqual(listing.OdometerKm * 1000, car.Odometer, 1e-6);
            Assert.AreEqual(listing.Condition, car.Condition, 1e-9);
            Assert.AreEqual(50 * listing.FuelFraction, car.FuelLiters, 1e-9, "what's left in the tank");
            Assert.IsTrue(fleet.IsSold(listing.Id));
            StringAssert.Contains("Dale", car.History[0].Detail);

            var reloaded = new Fleet(catalog);
            reloaded.RestoreState(JsonUtility.FromJson<FleetSaveData>(JsonUtility.ToJson(fleet.CaptureState())));
            Assert.IsTrue(reloaded.IsSold(listing.Id), "sold listings stay sold");
            Assert.AreEqual(car.FuelLiters, reloaded.Vehicles.Single().FuelLiters, 1e-9);
            Assert.AreEqual(car.FuelCapacity, reloaded.Vehicles.Single().FuelCapacity, 1e-9);
        }
    }
}
