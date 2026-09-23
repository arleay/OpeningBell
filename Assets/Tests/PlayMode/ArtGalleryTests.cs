using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// Art pass check: the city dresses itself with the third-party models (people are animated characters, not
    /// boxes) and a tour of screenshots lands in TestResults/art-*.png for eyeballing.
    /// </summary>
    public class ArtGalleryTests : SceneTestBase
    {
        private FirstPersonController _player;
        private CityBuilder _city;

        private IEnumerator Shot(Vector3 at, float yaw, float pitch, string file)
        {
            _player.PlaceAt(at, yaw, pitch);
            for (int i = 0; i < 4; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), file);
        }

        [UnityTest]
        public IEnumerator City_IsDressed_AndPhotographed()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            _city = Find<CityBuilder>();
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddHours(12.4));
            for (int i = 0; i < 20; i++) yield return null;

            NpcBody[] people = Object.FindObjectsByType<NpcBody>(FindObjectsSortMode.None);
            Assert.Greater(people.Length, 5, "people about at lunchtime");
            Assert.IsTrue(people.All(p => p.IsCharacter), "everyone is an animated character");

            // A walker up close, from the front.
            PedestrianSimulation.Walker w = _city.Pedestrians.Simulation.Walkers
                .Where(x => x.State == PedestrianSimulation.WalkerState.Walking)
                .OrderBy(x => Vector2.Distance(x.Position, new Vector2(20f, -7f))).First();
            Vector2 ahead = w.Position + w.Heading * 3.2f;
            float face = Mathf.Atan2(-w.Heading.x, -w.Heading.y) * Mathf.Rad2Deg;
            yield return Shot(new Vector3(ahead.x, 0f, ahead.y), face, 6f, "art-people.png");

            // A traffic sedan side-on from the kerb, 4.5 m off.
            Transform car = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.name == "Traffic sedan" && t.position.y > -50f)
                .OrderBy(t => Vector3.Distance(t.position, new Vector3(20f, 0f, -7f))).FirstOrDefault();
            Assert.IsNotNull(car, "a sedan in traffic");
            Vector3 side = car.position + car.right * 4.5f - car.forward * 1.5f;
            float look = Mathf.Atan2(car.position.x - side.x, car.position.z - side.z) * Mathf.Rad2Deg;
            yield return Shot(new Vector3(side.x, 0f, side.z), look, 12f, "art-car.png");

            yield return Shot(new Vector3(6f, 0f, -7.25f), 90f, 0f, "art-street.png");
            yield return Shot(new Vector3(4f, 0f, -12.8f), 10f, -8f, "art-home.png");
            yield return Shot(new Vector3(84f, 0f, -12.8f), -25f, -8f, "art-shops.png");
            yield return Shot(new Vector3(58f, 0f, -11.5f), 60f, -4f, "art-commercial.png");
            yield return Shot(new Vector3(138.5f, 0f, -11.5f), 40f, -8f, "art-downtown.png");
            yield return Shot(_city.Anchors["calder_lobby"] + new Vector3(0f, 0f, -1.5f), 0f, 4f, "art-lobby.png");
            yield return Shot(_city.Anchors["coffee_front_out"] + new Vector3(0f, 0f, 3f), 0f, 4f, "art-coffee.png");
            yield return Shot(new Vector3(-20f, 0f, 20f), 45f, 0f, "art-park.png");
            yield return Shot(new Vector3(116.9f, 0f, -1.2f), 40f, 12f, "art-mart.png");
            yield return Shot(new Vector3(74.6f, 0f, 2.3f), 0f, 35f, "art-pastries.png");
            yield return Shot(new Vector3(60f, 45f, -70f), 20f, 32f, "art-aerial.png");
            yield return Shot(new Vector3(-10f, 120f, -230f), 0f, 32f, "art-town-aerial.png");
            yield return Shot(new Vector3(-60f, 0f, -7.5f), -90f, 2f, "art-town-street.png");
            yield return Shot(new Vector3(66f, 0f, -20.5f), 80f, -2f, "art-mainstreet.png");

            game.SkipTo(game.Clock.Now.Date.AddHours(21.2));
            for (int i = 0; i < 10; i++) yield return null;
            yield return Shot(new Vector3(6f, 0f, -7.25f), 90f, 0f, "art-night.png");
        }

        /// <summary>The showroom supercar parked on Main Street: a three-quarter view and the driver's view.</summary>
        [UnityTest]
        public IEnumerator Supercar_IsPhotographed()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddHours(12.4));
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(game.VehicleLibrary.CreateCatalog().TryGetModel("car_super", out OpeningBell.Vehicles.VehicleModel m));
            CarController car = CarFactory.BuildDrivable(null, game.VehicleLibrary.CarMesh(m.Mesh), m.Car.Clone(), "Showroom super");
            var at = new Vector3(40f, 0f, -11.5f);
            car.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
            for (int i = 0; i < 4; i++) yield return null;

            Vector3 eye = at + new Vector3(4.2f, 0f, -3.6f);
            float look = Mathf.Atan2(at.x - eye.x, at.z - eye.z) * Mathf.Rad2Deg;
            yield return Shot(eye, look, 10f, "art-supercar.png");

            Transform seat = car.transform.Find("Seat");
            Camera cam = _player.GetComponentInChildren<Camera>();
            _player.PlaceAt(new Vector3(0f, -200f, 0f), 0f, 0f); // out of the way; the camera is placed by hand
            for (int i = 0; i < 2; i++) yield return null;
            cam.transform.SetPositionAndRotation(seat.position, car.transform.rotation * Quaternion.Euler(8f, 0f, 0f));
            yield return CaptureCamera(cam, "art-supercar-seat.png");
            Object.Destroy(car.gameObject);
        }

        /// <summary>Every buyable car parked in a row on Main Street, photographed three at a time (art-lineup-N.png).</summary>
        [UnityTest]
        public IEnumerator CarLineup_IsPhotographed()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            GameBootstrap game = Find<GameBootstrap>();
            game.SkipTo(game.Clock.Now.Date.AddHours(12.4));
            for (int i = 0; i < 10; i++) yield return null;
            var models = game.VehicleLibrary.Models.Where(m => m.Kind == OpeningBell.Vehicles.VehicleKind.Car).ToList();
            var built = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < models.Count; i++)
            {
                GameObject mesh = game.VehicleLibrary.CarMesh(models[i].Mesh);
                Assert.IsNotNull(mesh, models[i].Id);
                CarController car = CarFactory.BuildDrivable(null, mesh, models[i].Car.Clone(), models[i].Id);
                car.transform.SetPositionAndRotation(new Vector3(20f + i * 6.5f, 0f, -11.5f), Quaternion.Euler(0f, 150f, 0f));
                built.Add(car.gameObject);
            }
            for (int i = 0; i < 4; i++) yield return null;
            // Three cars a frame, from across the street.
            for (int first = 0, n = 0; first < models.Count; first += 3, n++)
                yield return Shot(new Vector3(20f + (first + 1) * 6.5f, 0f, -21f), 0f, 3f, $"art-lineup-{n}.png");
            foreach (GameObject go in built) Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Apartment_IsFurnished_AndPhotographed()
        {
            yield return LoadMain();
            _player = Find<FirstPersonController>();
            GameBootstrap game = Find<GameBootstrap>();
            Transform room = GameObject.Find("Apartment").transform;
            Transform frame = room.Find("Bed/Bed_Basic/Frame");
            Assert.IsNotNull(room.Find("Bed/Bed_Basic/bedSingle"), "the bed is a kit model");
            Assert.IsFalse(frame.GetComponent<MeshRenderer>().enabled, "its primitive is hidden");
            Assert.IsTrue(frame.GetComponent<BoxCollider>().enabled, "but still solid");
            Assert.IsNotNull(room.Find("loungeSofa"), "a sofa was added");

            yield return Shot(new Vector3(-1.9f, 0f, -2f), 40f, 10f, "art-apartment.png");
            yield return Shot(new Vector3(2.05f, 0f, -1.85f), -75f, 12f, "art-apartment-living.png");
            yield return Shot(new Vector3(0.8f, 0f, 0.5f), 0f, 14f, "art-apartment-desk.png");

            // Every upgrade bought: each appears in its slot, dressed.
            System.DateTime now = game.Clock.Now;
            Assert.IsNull(game.Economy.TransferFromBrokerage(9000m, now));
            foreach (OpeningBell.Economy.StoreItem item in game.Economy.Catalog)
                if (!string.IsNullOrEmpty(item.Slot)) Assert.IsNull(game.Economy.Buy(item.Id, now), item.Id);
            yield return null;
            Assert.IsNotNull(room.Find("Bed/Bed_Queen/bedDouble"), "the queen bed is a kit model");
            Assert.IsTrue(room.Find("Bed/Bed_Queen").gameObject.activeInHierarchy, "and it's the one showing");
            yield return Shot(new Vector3(-1.9f, 0f, -2f), 40f, 10f, "art-apartment-upgraded.png");
            yield return Shot(new Vector3(0.8f, 0f, 0.5f), 0f, 14f, "art-apartment-desk-upgraded.png");
        }
    }
}
