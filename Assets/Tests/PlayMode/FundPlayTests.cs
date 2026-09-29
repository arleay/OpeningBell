using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Fund;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OpeningBell.Tests
{
    /// <summary>
    /// The fund in the real world (FUND_SPEC §7–9, §14, §18–19, §24): a registered fund's staff at their workstations on
    /// Level 26, a hire with no desk waiting in reception and walking over once one is assigned, [E] on someone opening
    /// their panel without standing them up, and their car in the reserved bays on P1. Screenshots: fund-*.png.
    /// </summary>
    public class FundPlayTests : SceneTestBase
    {
        private CityBuilder _city;
        private GameBootstrap _game;
        private FirstPersonController _player;
        private HedgeFund Fund => _game.Fund;

        private static Vector3 Office(float x, float z) => HarborviewOffice.World(x, z);

        private IEnumerator Setup()
        {
            yield return LoadMain();
            _city = Find<CityBuilder>();
            _game = Find<GameBootstrap>();
            _player = Find<FirstPersonController>();
            _game.SkipTo(_game.Clock.Now.Date.AddDays(1).AddHours(11));
            _game.Economy.DevDeposit(3_000_000m, _game.Clock.Now);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsNull(Fund.Register("Kestrel Ridge Capital", 1, 2, OfficeTenure.Leased, 0m, 20, 50_000m, _game.Economy.Bank.Balance), "registered");
            yield return null;
        }

        private OwnedItem Put(string id, Vector3 at, float yaw = 0f, int on = 0)
        {
            OwnedItem i = _game.Belongings.Add(id, 0, ItemState.Placed);
            i.Boxed = false;
            i.Property = HedgeFund.OfficeId;
            i.X = at.x;
            i.Y = at.y;
            i.Z = at.z;
            i.Yaw = yaw;
            i.MountedOn = on;
            return i;
        }

        /// <summary>A complete workstation at plan (x, z): desk (seat side south), chair, tower, monitor, keyboard, mouse.</summary>
        private OwnedItem Station(float x, float z)
        {
            OwnedItem desk = Put("desk_standard", Office(x, z));
            float top = desk.Item.Height;
            Put("chair_office", Office(x, z - 0.9f));
            Put("pc_tower", Office(x + 0.95f, z));
            Put("mon_27", Office(x, z + 0.15f) + Vector3.up * top, 0f, desk.Uid);
            Put("keyboard", Office(x, z - 0.12f) + Vector3.up * top, 0f, desk.Uid);
            Put("mouse", Office(x + 0.3f, z - 0.12f) + Vector3.up * top, 0f, desk.Uid);
            _game.Belongings.Touch();
            return desk;
        }

        private Employee Hire(DateTime starts, string car = "")
        {
            Employee e = Fund.HireDirect(Fund.MakePerson(0.5), Contract.Of(PayStructure.Hourly, 40m, 0m), starts);
            e.Person.Car = car;
            return e;
        }

        private static DateTime NextWeekday(DateTime d)
        {
            d = d.Date.AddDays(1);
            while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }

        private static FundPerson PersonOf(Employee e) => Object.FindObjectsByType<FundPerson>(FindObjectsSortMode.None).FirstOrDefault(p => p.Id == e.Id);

        private static string Where(FundPerson p) => p == null ? "no body" : $"at {p.transform.position}, seated at {p.SeatedAt}";

        [UnityTest]
        public IEnumerator Office_StaffAtTheirDesks_WaitWithoutOne_AndOpenTheirPanel()
        {
            yield return Setup();
            OwnedItem desk = Station(-3f, -5f);
            yield return null;
            DateTime day = NextWeekday(_game.Clock.Now);
            Employee trader = Hire(day, "Kestrel Sedan");
            Employee waiting = Hire(day);
            Assert.IsNull(Fund.Assign(trader, desk.Uid), "a complete workstation: " + Fund.Stations.FirstOrDefault(w => w.Desk == desk.Uid)?.Summary);

            // Mid-morning with nobody on the floor to watch: they're simply where they should be.
            _player.PlaceAt(_city.Anchors["harborview_garage"] + new Vector3(0f, 0.05f, -8f), 0f, 0f);
            _game.SkipTo(day.AddHours(10));
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(Activity.Trading, trader.Activity, "trading at ten");
            Assert.AreEqual(Activity.WaitingForWorkstation, waiting.Activity, "no desk: waiting, and paid for it");
            FundPerson t = null, w = null;
            yield return WaitUntil(() => (t = PersonOf(trader)) != null && t.SeatedAt == desk.Uid, 5f, "the trader at their desk");
            yield return WaitUntil(() => (w = PersonOf(waiting)) != null, 5f, "the waiting hire's body");

            // Their car in the reserved row on P1 (placed while nobody was looking at the bays).
            _player.PlaceAt(_city.Anchors["harborview_garage"] + new Vector3(0f, 0.05f, -8f), 180f, 0f);
            GameObject car = null;
            yield return WaitUntil(() => (car = GameObject.Find("Staff car: Kestrel Sedan")) != null, 6f, "the trader's car in a staff bay");
            Assert.IsTrue(HarborviewTower.StaffBays.Any(b => (new Vector2(b.P.x - car.transform.position.x, b.P.z - car.transform.position.z)).magnitude < 0.5f), "parked in a reserved bay");
            _player.PlaceAt(HarborviewTower.StaffBays[2].P + new Vector3(0f, 0.05f, 7f), 180f, 8f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fund-garage.png");
            // The lift core's signs: from the staff row (north) and the east aisle, pointing round to the doors.
            float floorY = HarborviewTower.Grade + HarborviewTower.GarageFloor + 0.05f;
            _player.PlaceAt(new Vector3(252f, floorY, 129.5f), 180f, 4f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fund-lift-sign-north.png");
            _player.PlaceAt(new Vector3(259f, floorY, 122.5f), 270f, 4f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fund-lift-sign-east.png");

            // Reception: the brand wall sign, from the entrance.
            _player.PlaceAt(Office(-1.2f, 6.3f) + Vector3.up * 0.05f, 165f, -4f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fund-brand-wall.png");

            // On the floor: the trader seated, the new hire standing in reception.
            _player.PlaceAt(Office(-3f, -1.5f) + Vector3.up * 0.05f, 180f, 12f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(desk.Uid, t.SeatedAt, "still seated with someone watching: " + Where(t));
            Vector3 spot = Office(HarborviewOffice.StandingWait[0].x, HarborviewOffice.StandingWait[0].y);
            yield return WaitUntil(() => (w.transform.position - spot).sqrMagnitude < 0.8f * 0.8f, 30f, "the hire at a waiting spot (" + Where(w) + ")");
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fund-office-desk.png");

            // A second workstation: they walk over (navigation mesh round the furniture) and sit down.
            OwnedItem second = Station(1.5f, -5f);
            _game.SkipTo(_game.Clock.Now.AddMinutes(1));
            yield return null;
            Assert.IsNull(Fund.Assign(waiting, second.Uid), "the second desk");
            yield return WaitUntil(() => w.SeatedAt == second.Uid, 45f, "the hire walking to their new desk (" + Where(w) + ")");
            _player.PlaceAt(Office(0f, 1.5f) + Vector3.up * 0.05f, 180f, 14f);
            for (int i = 0; i < 6; i++) yield return null;
            yield return CaptureCamera(_player.GetComponentInChildren<Camera>(), "fund-office-both.png");

            // [E] on the trader: their panel on the HUD; they stay seated and the clock keeps going.
            DateTime before = _game.Clock.Now;
            t.Interact();
            yield return null;
            var hud = Find<InteractionHud>();
            VisualElement window = hud.Root.Q("employee-window");
            Assert.IsNotNull(window, "the employee window");
            Assert.AreEqual(DisplayStyle.Flex, window.resolvedStyle.display);
            Assert.IsTrue(window.Query<Label>().ToList().Any(l => l.text == trader.Name), "their name in the panel");
            Assert.IsFalse(_player.ControlEnabled, "the cursor is free while it's up");
            yield return CaptureWithHud(_player, hud, "fund-employee-panel.png");
            Assert.AreEqual(desk.Uid, t.SeatedAt, "opening the panel doesn't stand them up");
            yield return new WaitForSeconds(1f);
            Assert.Greater(_game.Clock.Now, before, "time keeps running while the panel is open");
            Press(window.Q<Button>("employee-window-close"));
            yield return null;
            Assert.AreEqual(DisplayStyle.None, window.resolvedStyle.display);
            Assert.IsTrue(_player.ControlEnabled, "controls back");
        }

        /// <summary>
        /// Commutes the player can watch (brief §24): from the garage, their car turns in off Harbor Ave, comes down the
        /// ramp and noses into its bay, and they walk to the lift; the next morning, from Level 26, the lift brings them
        /// up and they walk to their desk and sit down.
        /// </summary>
        [UnityTest]
        public IEnumerator Commute_WatchedCarDrivesIn_ThenTheLiftBringsThemUp()
        {
            yield return Setup();
            OwnedItem desk = Station(-3f, -5f);
            yield return null;
            DateTime day = NextWeekday(_game.Clock.Now);
            Employee e = Hire(day, "Kestrel Sedan");
            Assert.IsNull(Fund.Assign(e, desk.Uid));
            Camera cam = _player.GetComponentInChildren<Camera>();

            // Down in P1 before they get in, looking at the reserved row (the street is behind the player).
            _game.SkipTo(day.AddHours(6));
            yield return null;
            DayPlan plan = e.Plan;
            Assert.IsNotNull(plan, "today's plan");
            var bay = HarborviewTower.StaffBays[1];
            _player.PlaceAt(bay.P + new Vector3(-4f, 0.05f, 7.5f), 150f, 6f);
            _game.SkipTo(day.AddMinutes(plan.ReachBuilding - 1));
            for (int i = 0; i < 70; i++) yield return null; // a parking pass sees them still on the road
            Assert.AreEqual(Activity.Commuting, e.Activity);
            Assert.IsNull(GameObject.Find("Staff car: Kestrel Sedan"), "no car before they arrive");
            _game.SkipTo(day.AddMinutes(plan.ReachBuilding));
            GameObject car = null;
            yield return WaitUntil(() => (car = GameObject.Find("Staff car: Kestrel Sedan")) != null, 4f, "their car turning in");
            Assert.Greater(Vector3.Distance(car.transform.position, bay.P), 20f, "it starts up at the street, not in the bay");
            yield return WaitUntil(() => car.transform.position.x > bay.P.x - 6f, 30f, "the car down the ramp and along the lane");
            yield return CaptureCamera(cam, "fund-commute-car.png");
            yield return WaitUntil(() => HarborviewTower.StaffBays.Any(b => (new Vector2(b.P.x - car.transform.position.x, b.P.z - car.transform.position.z)).magnitude < 0.2f), 30f, "parked in a bay");
            yield return WaitUntil(() => GameObject.FindObjectsByType<NpcBody>(FindObjectsSortMode.None).Any(b => b.name == e.Name && b.GetComponent<FundPerson>() == null), 5f, "the driver out of the car");
            yield return CaptureCamera(cam, "fund-commute-walk.png");
            yield return WaitUntil(() => !GameObject.FindObjectsByType<NpcBody>(FindObjectsSortMode.None).Any(b => b.name == e.Name && b.GetComponent<FundPerson>() == null), 30f, "the driver gone up in the lift");

            // The next working day, waiting on Level 26 by the lift.
            DateTime next = NextWeekday(day);
            _game.SkipTo(next.AddHours(6));
            yield return null;
            plan = e.Plan;
            _player.PlaceAt(Office(2.5f, 3.5f) + Vector3.up * 0.05f, 340f, 4f);
            _game.SkipTo(next.AddMinutes(plan.ReachBuilding - 1));
            for (int i = 0; i < 10; i++) yield return null;
            FundPerson body = PersonOf(e);
            Assert.IsNotNull(body);
            Assert.AreEqual(0, body.SeatedAt, "not at the desk before they've arrived");
            _game.SkipTo(next.AddMinutes(plan.ReachBuilding));
            yield return WaitUntil(() => HarborviewOffice.Contains(body.transform.position) && body.transform.GetChild(0).gameObject.activeSelf, 45f, "out of the lift on 26 (" + Where(body) + ")");
            yield return CaptureCamera(cam, "fund-lift-arrival.png");
            yield return WaitUntil(() => body.SeatedAt == desk.Uid, 60f, "walked to the desk and sat (" + Where(body) + ")");
        }
    }
}
