using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The car physics on a test track (a big flat plane, no city): launch times, wheelspin, braking distance,
    /// cornering without flipping. Numbers come from the specs through the physics; these check they land in
    /// believable ranges and that drivetrains differ.
    /// </summary>
    public class CarPhysicsPlayTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Far from the city: earlier tests may leave Main loaded, and its buildings sit around the origin.</summary>
        private static readonly Vector3 Origin = new Vector3(0f, 0f, 3000f);

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        private static VehicleLibrary Library() =>
            AssetDatabase.LoadAssetAtPath<VehicleLibrary>("Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset");

        private IEnumerator Track()
        {
            Scene scene = SceneManager.CreateScene("CarTrack" + Random.Range(0, 100000));
            SceneManager.SetActiveScene(scene);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(400f, 1f, 400f); // 4 km square
            ground.transform.position = Origin;
            _spawned.Add(ground);
            yield return null;
        }

        private CarController Car(string id, Vector3 at)
        {
            Assert.IsTrue(Library().CreateCatalog().TryGetModel(id, out VehicleModel m), id);
            CarController car = CarFactory.BuildDrivable(null, Library().CarMesh(m.Mesh), m.Car.Clone(), id);
            car.transform.SetPositionAndRotation(Origin + at, Quaternion.identity);
            _spawned.Add(car.gameObject);
            car.SetParked(false);
            car.EngineOn = true;
            return car;
        }

        private static IEnumerator Settle()
        {
            for (float t = 0f; t < 1.5f; t += Time.fixedDeltaTime) yield return new WaitForFixedUpdate();
        }

        /// <summary>Full throttle from rest: seconds to reach <paramref name="kmh"/> (NaN if not within the limit).</summary>
        private static IEnumerator Launch(CarController car, float kmh, float limit, float[] result, bool[] spun)
        {
            car.Throttle = 1f;
            float t = 0f;
            while (car.SpeedKmh < kmh && t < limit)
            {
                if (t < 1.2f && car.Wheelspin) spun[0] = true;
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
            }
            result[0] = car.SpeedKmh >= kmh ? t : float.NaN;
        }

        [UnityTest]
        public IEnumerator Acceleration_FollowsPowerWeightAndTraction()
        {
            yield return Track();
            var launch = new Dictionary<string, float>();
            var spins = new Dictionary<string, bool>();
            float lane = 0f;
            foreach (string id in new[] { "car_sedan", "car_hatch", "car_suv", "car_pickup", "car_super" })
            {
                CarController car = Car(id, new Vector3(lane, 0.3f, 0f));
                lane += 12f;
                yield return Settle();
                Assert.Greater(car.transform.up.y, 0.99f, $"{id} settles level");
                Assert.Less(car.Body.linearVelocity.magnitude, 0.2f, $"{id} sits still on its suspension");
                var time = new float[1];
                var spun = new bool[1];
                yield return Launch(car, 100f, 25f, time, spun);
                launch[id] = time[0];
                spins[id] = spun[0];
                car.Throttle = 0f;
                car.gameObject.SetActive(false);
            }
            TestContext.WriteLine(string.Join(", ", launch.Keys) + " → " + string.Join(", ", launch.Values));
            Debug.Log("PERF 0-100 km/h: " + string.Join(", ", System.Linq.Enumerable.Select(launch, kv => $"{kv.Key} {kv.Value:F1}s")));

            Assert.That(launch["car_sedan"], Is.InRange(6.5f, 13f), "family sedan");
            Assert.That(launch["car_super"], Is.InRange(2.2f, 4.5f), "supercar");
            Assert.Less(launch["car_hatch"], launch["car_sedan"], "turbo hatch quicker than the sedan");
            Assert.Less(launch["car_pickup"], launch["car_sedan"], "V8 pickup quicker than the sedan");
            Assert.Less(launch["car_super"], launch["car_hatch"]);
            Assert.IsTrue(spins["car_pickup"], "rear-drive V8 lights up its tyres at launch");
        }

        [UnityTest]
        public IEnumerator Braking_AndSlalom_StayBelievable()
        {
            yield return Track();
            CarController car = Car("car_sedan", new Vector3(0f, 0.3f, 0f));
            yield return Settle();
            var time = new float[1];
            var spun = new bool[1];
            yield return Launch(car, 100f, 25f, time, spun);
            car.Throttle = 0f;
            car.Brake = 1f;
            Vector3 start = car.transform.position;
            while (car.ForwardSpeed > 0.3f) yield return new WaitForFixedUpdate();
            float distance = Vector3.Distance(start, car.transform.position);
            Debug.Log($"PERF sedan 100-0 km/h: {distance:F1} m");
            Assert.That(distance, Is.InRange(32f, 55f), "100–0 km/h stopping distance");
            car.Brake = 0f;

            // Slalom at ~60 km/h: sharp alternating steering must not roll it over.
            car.Throttle = 1f;
            while (car.SpeedKmh < 60f) yield return new WaitForFixedUpdate();
            car.Throttle = 0.35f;
            float minUp = 1f, maxYawRate = 0f;
            for (float t = 0f; t < 8f; t += Time.fixedDeltaTime)
            {
                car.Steer = Mathf.Repeat(t, 2.4f) < 1.2f ? 1f : -1f;
                minUp = Mathf.Min(minUp, car.transform.up.y);
                maxYawRate = Mathf.Max(maxYawRate, Mathf.Abs(car.Body.angularVelocity.y));
                yield return new WaitForFixedUpdate();
            }
            Debug.Log($"PERF slalom min up {minUp:F2}, max yaw {maxYawRate:F2} rad/s, speed {car.SpeedKmh:F0}");
            Assert.Greater(minUp, 0.75f, "leans, never rolls");
            Assert.Greater(maxYawRate, 0.3f, "it actually turns");
        }
    }
}
