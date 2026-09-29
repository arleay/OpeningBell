using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Casino;
using OpeningBell.Gameplay;
using OpeningBell.Market;
using OpeningBell.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpeningBell.City
{
    /// <summary>
    /// What makes the Meridian feel lived in, and the rules that follow you out of it (CASINO_SPEC §46, §53–56, §61–65,
    /// §101). A pool of regulars fills more or fewer of their spots by time of day and day of week; a pit boss and
    /// servers walk their rounds; people have something to say (a few talk markets, never tips); a pianist plays the
    /// lounge at night. Drinking sways the view and blurs it, slows your step and keeps you out of the driver's seat.
    /// First visits get a hint, days with an event get a note, and the nightly budget warns you.
    /// </summary>
    public sealed class CasinoLife : MonoBehaviour
    {
        public sealed class Spot
        {
            public Vector3 At;
            public float Yaw;
            public NpcPose Pose;
            public NpcBody Body;
            public bool Filled;
            /// <summary>0–1: spots fill in this order as the room gets busier.</summary>
            public float Rank;
        }

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Transform _player, _root;
        private FirstPersonController _fpc;
        private Bounds _inside;
        private readonly List<Spot> _spots = new List<Spot>();
        private readonly List<Wanderer> _walkers = new List<Wanderer>();
        private NpcBody _pianist;
        private float _nextCrowd, _nextChecks;
        private Volume _drunk;
        private Vignette _vignette;
        private DepthOfField _blur;
        private bool _wasInside;
        private DateTime _eventShown;

        public int Present => _spots.Count(s => s.Filled);
        public int Capacity => _spots.Count;
        public bool PlayerInside => _inside.Contains(_player.position);

        public static CasinoLife Build(CityContext c, Transform root, Transform dyn, Bounds insideWorld)
        {
            var life = dyn.gameObject.AddComponent<CasinoLife>();
            life._game = c.Game;
            life._hud = c.Hud;
            life._player = c.Player;
            life._root = root;
            life._fpc = c.Player.GetComponent<FirstPersonController>();
            life._inside = insideWorld;
            life.BuildDrunkVolume();
            DriveController.Refusal = () =>
            {
                Intoxication d = c.Game.Casino.Drinks;
                d.Update(c.Game.Clock.Now);
                return d.CanDrive ? null : "You've been drinking: you can't drive like this. Call a cab on your phone, or get a room.";
            };
            return life;
        }

        public void AddSpot(Kit k, Transform parent, Vector3 at, float yaw, NpcPose pose, int seed, float rank)
        {
            NpcBody body = NpcBody.Create(k, parent, "Guest", seed, Color.HSVToRGB((seed * 0.137f) % 1f, 0.45f, 0.55f));
            body.transform.localPosition = at;
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var cap = body.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = 0.28f;
            body.gameObject.AddComponent<PatronTalk>().Configure(_game, _hud, seed, null);
            body.gameObject.SetActive(false);
            _spots.Add(new Spot { At = at, Yaw = yaw, Pose = pose, Body = body, Rank = rank });
        }

        /// <summary>A named regular with their own lines (the trader types, §56).</summary>
        public NpcBody AddRegular(Kit k, Transform parent, Vector3 at, float yaw, NpcPose pose, int seed, string name, string look)
        {
            NpcBody body = NpcBody.Create(k, parent, name, seed, null, look);
            body.transform.localPosition = at;
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var cap = body.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = 0.28f;
            body.gameObject.AddComponent<PatronTalk>().Configure(_game, _hud, seed, name);
            _spots.Add(new Spot { At = at, Yaw = yaw, Pose = pose, Body = body, Rank = 0.05f });
            return body;
        }

        public void AddWalker(Wanderer w) => _walkers.Add(w);

        public void SetPianist(NpcBody body) => _pianist = body;

        private void BuildDrunkVolume()
        {
            var go = new GameObject("Drunk vision");
            go.transform.SetParent(transform, false);
            _drunk = go.AddComponent<Volume>();
            _drunk.isGlobal = true;
            _drunk.priority = 50f;
            _drunk.weight = 0f;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.45f);
            _vignette.smoothness.Override(0.8f);
            _blur = profile.Add<DepthOfField>(true);
            _blur.mode.Override(DepthOfFieldMode.Gaussian);
            _blur.gaussianStart.Override(0.5f);
            _blur.gaussianEnd.Override(6f);
            _blur.gaussianMaxRadius.Override(1.2f);
            _drunk.profile = profile;
        }

        private void Update()
        {
            DateTime now = _game.Clock.Now;
            CasinoFloor floor = _game.Casino;
            floor.Rewards.Multiplier = CasinoEvents.DoublePoints(now) ? 2 : 1;

            // Drink: sway and blur scale with how much is in you.
            floor.Drinks.Update(now);
            float impair = floor.Drinks.Impairment;
            if (_fpc != null) _fpc.Impairment = impair;
            _drunk.weight = Mathf.MoveTowards(_drunk.weight, impair, Time.deltaTime);

            if (Time.time >= _nextCrowd)
            {
                _nextCrowd = Time.time + 20f;
                UpdateCrowd(now);
            }
            foreach (Spot s in _spots)
                if (s.Filled && s.Body.gameObject.activeSelf && (s.Body.transform.position - _player.position).sqrMagnitude < 50f * 50f)
                    s.Body.Animate(s.Pose, Time.time + s.Rank * 10f);
            if (_pianist != null)
            {
                bool playing = PianoHours(now);
                if (_pianist.gameObject.activeSelf != playing) _pianist.gameObject.SetActive(playing);
                if (playing) _pianist.Animate(NpcPose.Typing, Time.time);
            }

            if (Time.time >= _nextChecks)
            {
                _nextChecks = Time.time + 1f;
                Checks(now, floor);
            }
        }

        public static bool PianoHours(DateTime now) => now.Hour >= 19 || now.Hour < 2;

        /// <summary>Fills spots up to the time-of-day crowd (§63–64), changing only ones out of sight so nobody pops in.</summary>
        private void UpdateCrowd(DateTime now)
        {
            float crowd = CasinoEvents.Crowd(now);
            foreach (Spot s in _spots)
            {
                bool want = s.Rank <= crowd;
                if (want == s.Filled) continue;
                Renderer r = s.Body.GetComponentInChildren<Renderer>(true);
                bool seen = s.Filled && r != null && r.isVisible && (s.Body.transform.position - _player.position).sqrMagnitude < 40f * 40f;
                if (seen) continue;
                s.Filled = want;
                s.Body.gameObject.SetActive(want);
            }
        }

        private void Checks(DateTime now, CasinoFloor floor)
        {
            bool inside = PlayerInside;
            if (inside && !_wasInside)
            {
                decimal restored = floor.SettleRestoredPoker(now);
                if (restored > 0m) _hud.ShowToast($"Your poker stack from last time ({CasinoMoney.Whole(restored)}) was cashed back into chips.", 6f);
                if (floor.FirstTime("welcome"))
                    _hud.ShowToast("Welcome to The Meridian. Buy chips at the cashier on your left, then pick a game: blackjack and roulette at the back, " +
                                   "slots either side, poker in the room back left. The hotel desk and elevators are on the right.", 10f);
                else if (_eventShown.Date != now.Date && CasinoEvents.Today(now) is string ev)
                    _hud.ShowToast(ev, 5f);
                _eventShown = now;
            }
            _wasInside = inside;

            // The nightly budget: a word at 80% and at the limit (§61).
            GamblingBudget b = floor.Budget;
            if (b.ActiveOn(now))
            {
                decimal lost = b.Lost(floor.Net);
                if (!b.Warned80 && lost >= b.Limit * 0.8m)
                {
                    b.Warned80 = true;
                    _hud.ShowToast($"Heads up: you're down {CasinoMoney.Whole(lost)} of tonight's {CasinoMoney.Whole(b.Limit)} limit.", 6f);
                }
                if (!b.WarnedLimit && lost >= b.Limit)
                {
                    b.WarnedLimit = true;
                    _hud.ShowToast($"You've reached tonight's limit of {CasinoMoney.Whole(b.Limit)}." + (b.Hard ? " New bets are off until 6 AM." : ""), 7f);
                }
            }
        }
    }

    /// <summary>Someone who walks a loop of points and pauses at each (pit boss, servers, security).</summary>
    public sealed class Wanderer : MonoBehaviour
    {
        private NpcBody _body;
        private List<Vector3> _route;
        private int _next;
        private float _pauseUntil, _speed;
        private Transform _player;
        private float[] _pauses;

        public static Wanderer Create(Kit k, Transform parent, string name, int seed, Color outfit, string look, List<Vector3> route, float pause, float speed, Transform player,
            Func<string> talk = null, GameBootstrap game = null, InteractionHud hud = null)
        {
            NpcBody body = NpcBody.Create(k, parent, name, seed, outfit, look);
            body.transform.localPosition = route[0];
            var w = body.gameObject.AddComponent<Wanderer>();
            w._body = body;
            w._route = route;
            w._speed = speed;
            w._player = player;
            w._pauses = route.Select((_, i) => pause * (0.6f + 0.8f * (float)((seed * 31 + i * 17) % 100) / 100f)).ToArray();
            var cap = body.gameObject.AddComponent<CapsuleCollider>();
            cap.isTrigger = true; // they step round you, not into you
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = 0.3f;
            if (talk != null) body.gameObject.AddComponent<PatronTalk>().Configure(game, hud, seed, name, talk);
            return w;
        }

        private void Update()
        {
            if (_player != null && (_player.position - transform.position).sqrMagnitude > 70f * 70f) return;
            if (Time.time < _pauseUntil)
            {
                _body.Animate(NpcPose.Stand, Time.time);
                return;
            }
            Vector3 target = _route[_next];
            Vector3 p = transform.localPosition;
            Vector3 dir = target - p;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                _pauseUntil = Time.time + _pauses[_next];
                _next = (_next + 1) % _route.Count;
                return;
            }
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(dir.normalized), 0.15f);
            transform.localPosition = Vector3.MoveTowards(p, target, _speed * Time.deltaTime);
            _body.Animate(NpcPose.Walk, Time.time);
        }
    }

    /// <summary>
    /// Casino small talk (CASINO_SPEC §55–56, §93): wins and losses, cars, the town, the market (with today's real
    /// numbers), never a tip and never "you can't lose". Named regulars have their own voice.
    /// </summary>
    public sealed class PatronTalk : Interactable
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private System.Random _rng;
        private string _name;
        private Func<string> _custom;
        private int _said;

        private static readonly string[] Wins = { "Hit three sevens an hour ago. Buying the next round, then I'm done.", "Up forty bucks. That's dinner.", "Blackjack three times in a row. I'm quitting while I'm ahead.", "Won on red, lost on black, won on red. I'm even. I think." };
        private static readonly string[] Losses = { "Down two hundred. Should've stopped at the buffet.", "That slot hasn't paid all night. They never do when you watch them.", "The dealer had twenty-one every hand. Every hand.", "I set a limit. I'm at it. Going home." };
        private static readonly string[] Town = { "Did you see the car someone parked out front? Didn't know people bought those.", "The piano guy's good on Fridays.", "Room service here does a great burger.", "They're opening a high-limit room upstairs, VIPs only.", "Parking's easier if you let the valet do it." };
        private static readonly string[] Caution = { "It's fun until it isn't. I bring cash for the night and that's it.", "House always wins in the end. I'm here for the drinks and the noise.", "My brother thinks he's got a system. He doesn't have a car anymore." };

        public void Configure(GameBootstrap game, InteractionHud hud, int seed, string name, Func<string> custom = null)
        {
            _game = game;
            _hud = hud;
            _rng = new System.Random(seed);
            _name = name;
            _custom = custom;
        }

        public override string Prompt => _name != null ? "Talk to " + _name : "Talk";

        public override void Interact()
        {
            string line = _custom?.Invoke() ?? Line();
            _hud.ShowSubtitle(_name ?? "Guest", line);
            _said++;
        }

        private string Line()
        {
            int pick = (_rng.Next(5) + _said) % 5;
            return pick switch
            {
                0 => Wins[_rng.Next(Wins.Length)],
                1 => Losses[_rng.Next(Losses.Length)],
                2 => Town[_rng.Next(Town.Length)],
                3 => MarketLine(_game, _rng),
                _ => Caution[_rng.Next(Caution.Length)],
            };
        }

        /// <summary>A remark about a real stock's day or the latest headline (§56): colour, not advice.</summary>
        public static string MarketLine(GameBootstrap game, System.Random rng)
        {
            if (game == null) return "Markets, huh.";
            var securities = game.Market.Securities;
            if (securities.Count > 0 && rng.Next(2) == 0)
            {
                SecurityRuntimeState s = securities[rng.Next(securities.Count)];
                if (s.PreviousClose > 0m)
                {
                    decimal change = (s.Last - s.PreviousClose) / s.PreviousClose;
                    string how = change >= 0 ? $"up {change:P1}" : $"down {-change:P1}";
                    return change >= 0
                        ? $"{s.Ticker}'s {how} today. I sold it last week, naturally."
                        : $"{s.Ticker}'s {how} today. I'm not looking at my account until Monday.";
                }
            }
            var news = game.Market.News;
            if (news.Count > 0) return $"Did you see this? \"{news[news.Count - 1].Headline}\". Who knows what it means for anything.";
            return "Quiet day on the markets. Quieter than this place, anyway.";
        }
    }

    /// <summary>
    /// The valet (CASINO_SPEC §67–68): leave the car you drove up in by the entrance and they park it; ask for it back
    /// and it's at the door a little later. $20, free for Silver members and up. The car isn't driven on screen: it's
    /// moved to the lot and brought back.
    /// </summary>
    public sealed class ValetStand : Interactable
    {
        public const decimal Fee = 20m;
        private const float Reach = 22f, Delay = 12f;

        private GameBootstrap _game;
        private InteractionHud _hud;
        private StaffNpc _valet;
        private Transform _pickup;
        private Vector3 _lot;
        private float _lotYaw, _readyAt = -1f;

        public void Configure(CityContext c, StaffNpc valet, Transform pickup, Vector3 lotSpot, float lotYaw)
        {
            _game = c.Game;
            _hud = c.Hud;
            _valet = valet;
            _pickup = pickup;
            _lot = lotSpot;
            _lotYaw = lotYaw;
        }

        private FleetView Fleet => FindAnyObjectByType<FleetView>();

        private OwnedVehicle Held => _game.Casino.ValetCar != null ? _game.Vehicles.Find(_game.Casino.ValetCar) : null;

        /// <summary>The player's car parked nearest the stand, within reach.</summary>
        public OwnedVehicle NearbyCar()
        {
            OwnedVehicle best = null;
            float bestD = Reach * Reach;
            foreach (OwnedVehicle v in _game.Vehicles.Vehicles)
            {
                if (v.Kind != VehicleKind.Car || v.State != VehicleState.Parked || v.TestDrive) continue;
                float d = (new Vector3((float)v.X, (float)v.Y, (float)v.Z) - transform.position).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = v;
                }
            }
            return best;
        }

        public bool Waiting => _readyAt > 0f;

        public override string Prompt
        {
            get
            {
                string fee = _game.Casino.Rewards.FreeValet ? "free for members" : CasinoMoney.Whole(Fee);
                if (Waiting) return "Valet: your car's on its way";
                if (Held != null) return $"Valet: bring my {Held.Name}";
                OwnedVehicle near = NearbyCar();
                return near != null ? $"Valet: park my {near.Name} ({fee})" : "Valet (drive up to the entrance first)";
            }
        }

        public override bool CanInteract => base.CanInteract && !Waiting && (Held != null || NearbyCar() != null);

        public override void Interact()
        {
            if (Held != null)
            {
                _readyAt = Time.time + Delay;
                Say("Be right back with it. Just a minute.");
                return;
            }
            OwnedVehicle car = NearbyCar();
            if (car == null) return;
            DateTime now = _game.Clock.Now;
            decimal fee = _game.Casino.Rewards.FreeValet ? 0m : Fee;
            string error = _game.Casino.Spend(SpendCategory.Valet, fee, "Valet parking", now, (a, w) => _game.Economy.Spend(a, w, now));
            if (error != null)
            {
                _hud.ShowToast(error, 3f);
                return;
            }
            Move(car, _lot, _lotYaw);
            _game.Casino.ValetCar = car.Id;
            Say($"We'll take good care of the {car.Name}. Ask for it here any time.");
        }

        public void Collect()
        {
            OwnedVehicle car = Held;
            if (car == null) return;
            Move(car, _pickup.position, _pickup.eulerAngles.y);
            _game.Casino.ValetCar = null;
            _readyAt = -1f;
            Say($"Here's your {car.Name}. Drive safe.");
            _hud.ShowToast($"Your {car.Name} is at the entrance.", 4f);
        }

        private void Move(OwnedVehicle car, Vector3 at, float yaw)
        {
            _game.Vehicles.Park(car, at.x, at.y, at.z, yaw);
            FleetView fleet = Fleet;
            if (fleet != null) fleet.Rebuild(car);
        }

        private void Say(string line)
        {
            if (_valet != null) _valet.Say(line);
            else _hud.ShowSubtitle("Valet", line);
        }

        private void Update()
        {
            if (_readyAt > 0f && Time.time >= _readyAt) Collect();
        }
    }
}
