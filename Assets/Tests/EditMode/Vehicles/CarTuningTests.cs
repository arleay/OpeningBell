using System;
using System.Linq;
using NUnit.Framework;
using OpeningBell.Vehicles;
using UnityEditor;

namespace OpeningBell.Tests
{
    public class CarTuningTests
    {
        private static VehicleCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset").CreateCatalog();

        [Test]
        public void Upgrades_ChangeTheSpec_AndSlotsReplaceEachOther()
        {
            VehicleCatalog catalog = Catalog();
            catalog.TryGetModel("car_sedan", out VehicleModel sedan);
            var fleet = new Fleet(catalog);
            var now = new DateTime(2026, 3, 2, 10, 0, 0);
            OwnedVehicle car = fleet.Add("car_sedan", 24000m, now, 0, 0, 0, 0);

            Assert.IsNull(CarTuning.Install(fleet, car, "car_intake", now, 2500m));
            Assert.IsNull(CarTuning.Install(fleet, car, "car_turbo", now, 4800m));
            CollectionAssert.DoesNotContain(car.Parts, "car_intake", "the turbo replaces stage 1 (same slot)");
            Assert.IsNotNull(CarTuning.Install(fleet, car, "car_turbo", now, 4800m), "can't fit it twice");
            Assert.IsNull(CarTuning.Install(fleet, car, "car_weight", now, 2200m));
            Assert.IsNull(CarTuning.Install(fleet, car, "car_tint", now, 250m));
            Assert.IsTrue(car.Tinted);

            CarSpec tuned = CarTuning.Apply(sedan.Car, car.Parts);
            Assert.AreEqual(sedan.Car.CurveTorque.Max() * 1.28, tuned.CurveTorque.Max(), 1e-6, "turbo +28%");
            Assert.AreEqual(sedan.Car.Mass * 0.92, tuned.Mass, 1e-6, "weight reduction −8%");
            Assert.AreEqual(sedan.Car.Mass, catalog.TryGetModel("car_sedan", out VehicleModel again) ? again.Car.Mass : 0, 1e-9, "the model itself is untouched");
            Assert.Greater(CarPhysics.PeakPower(tuned), CarPhysics.PeakPower(sedan.Car) * 1.2);
        }

        [Test]
        public void Repairs_ArePricedByDamage_AndPaintIsSaved()
        {
            VehicleCatalog catalog = Catalog();
            var fleet = new Fleet(catalog);
            var now = new DateTime(2026, 3, 2, 10, 0, 0);
            OwnedVehicle car = fleet.Add("car_sedan", 24000m, now, 0, 0, 0, 0);
            Assert.AreEqual(0m, CarTuning.RepairPrice(car, 24000));
            car.Condition = 0.5;
            Assert.AreEqual(720m, CarTuning.RepairPrice(car, 24000), "6% of the price per 100% of damage");
            car.Condition = 0.99;
            Assert.AreEqual(120m, CarTuning.RepairPrice(car, 24000), "shop minimum");
            Assert.AreEqual(396m, CarTuning.TyresPrice(24000));

            fleet.Paint(car, 0.7f, 0.06f, 0.05f, "Racing red", now, CarTuning.PaintPrice);
            CarTuning.Install(fleet, car, "car_brakes", now, 900m);
            FleetSaveData save = fleet.CaptureState();
            var loaded = new Fleet(catalog);
            loaded.RestoreState(UnityEngine.JsonUtility.FromJson<FleetSaveData>(UnityEngine.JsonUtility.ToJson(save)));
            OwnedVehicle back = loaded.Vehicles[0];
            Assert.IsTrue(back.Painted);
            Assert.AreEqual(0.7f, back.PaintR, 1e-6f);
            CollectionAssert.Contains(back.Parts, "car_brakes");
        }
    }
}
