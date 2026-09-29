using OpeningBell.Fund;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.AI;

namespace OpeningBell.City
{
    /// <summary>
    /// One of the fund's employees in the office (FUND_SPEC §14, §18–19). The simulation decides what they're doing;
    /// this puts the body there: out of the lift, to their desk (sit), to the coffee machine, the break area or the
    /// restrooms, to reception to wait when they have no workstation, and back into the lift. When nobody is on the floor
    /// to see, they're simply placed where they should be. [E] opens their panel without interrupting them.
    /// </summary>
    public sealed class FundPerson : Interactable
    {
        private enum Phase { Away, LiftIn, Walking, Settling, AtSpot, Inside, LiftOut }
        private enum Kind { None, Seat, Stand, Restroom, Exit }

        private struct Spot
        {
            public Kind Kind;
            /// <summary>Where they end up (the chair, for a seat).</summary>
            public Vector3 Pos;
            /// <summary>Where they walk to first (behind the chair); the same as Pos for standing spots.</summary>
            public Vector3 Approach;
            public float Yaw;
            public int Desk;
            public NpcPose Pose;
            /// <summary>Identity of the destination: a change sends them somewhere else.</summary>
            public string Key;
        }

        private const float WalkSpeed = 1.3f;

        private FundWorld _w;
        private Employee _e;
        private NpcBody _body;
        private NavMeshAgent _agent;
        private CapsuleCollider _col;
        private Transform _bubble;
        private TextMesh _bubbleText;
        private Transform _bubbleBack;
        private GameObject _book;
        private Transform _progressFill;
        private System.Random _rng;

        private Phase _phase = Phase.Away;
        private Spot _target;
        private float _settle;
        private Vector3 _settleFrom;
        private Quaternion _settleRotFrom;
        private NpcPose _idle = NpcPose.Stand;
        private float _idleUntil, _bubbleUntil, _nextBubble, _liftWait;
        private bool _shown;

        public int Index { get; set; }
        /// <summary>On the floor and visible (not at home, not in the restroom).</summary>
        public bool Shown => _shown;
        public long Id => _e.Id;
        /// <summary>A departed employee who has walked out: the body can go.</summary>
        public bool Gone { get; private set; }
        /// <summary>The desk (item uid) they're sitting at, or 0.</summary>
        public int SeatedAt => (_phase == Phase.AtSpot || _phase == Phase.Settling) && _target.Kind == Kind.Seat ? _target.Desk : 0;

        /// <summary>Their body as everyone sees it: their build, skin and hair, and their suit (also for the walk to the car).</summary>
        internal static NpcBody Body(FundWorld w, Employee e, Transform parent)
        {
            PersonLook look = e.Person.Look ?? new PersonLook();
            // Suits: mostly black and charcoal (FUND_SPEC §19), some navy and grey.
            Color suit = look.Suit switch
            {
                SuitColour.Black => new Color(0.07f, 0.07f, 0.08f),
                SuitColour.Charcoal => new Color(0.19f, 0.2f, 0.22f),
                SuitColour.Navy => new Color(0.1f, 0.13f, 0.24f),
                _ => new Color(0.4f, 0.41f, 0.43f),
            };
            var style = new OpeningBell.PlayerLook { Skin = look.Skin, Hair = look.Hair };
            return NpcBody.Create(w.City.Kit, parent, e.Name, look.Seed, suit, look.Feminine ? "Suit_Female" : "Suit_Male", style, look.Height);
        }

        public static FundPerson Create(FundWorld w, Employee e, Transform parent)
        {
            PersonLook look = e.Person.Look ?? new PersonLook();
            NpcBody body = Body(w, e, parent);
            var p = body.gameObject.AddComponent<FundPerson>();
            p._w = w;
            p._e = e;
            p._body = body;
            p._rng = new System.Random(look.Seed);

            var col = body.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.height = 1.8f;
            col.radius = 0.26f;
            p._col = col;

            var agent = body.gameObject.AddComponent<NavMeshAgent>();
            agent.enabled = false;
            agent.radius = 0.26f;
            agent.height = 1.75f;
            agent.speed = WalkSpeed * (0.92f + 0.16f * (float)p._rng.NextDouble());
            agent.acceleration = 5f;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 0.05f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 30 + (int)(e.Id % 40);
            p._agent = agent;

            // A thought, now and then: a line of text over a dark card above their head.
            p._bubble = Kit.Group(body.transform, "Thought", new Vector3(0f, 2.05f, 0f));
            p._bubbleBack = w.City.Kit.Box(p._bubble, "Card", new Vector3(0f, 0f, 0.01f), new Vector3(1f, 0.16f, 0.005f),
                w.City.P.Lit(new Color(0.08f, 0.09f, 0.11f, 1f), 0.2f), collider: false).transform;
            p._bubbleText = w.City.Kit.Text(p._bubble, "", Vector3.zero, 0f, 0.07f, new Color(0.95f, 0.95f, 0.92f));
            p._bubble.gameObject.SetActive(false);

            p.SetShown(false);
            return p;
        }

        public override string Prompt => "Manage " + _e.Name;
        public override string Details => Describe();
        public override bool CanInteract => base.CanInteract && _shown && !_e.Former;
        public override void Interact() => _w.OpenPanel(_e);

        private string Describe()
        {
            switch (_e.Activity)
            {
                case Activity.Trading: return $"Trading · {_e.OpenPositionCount} open";
                case Activity.Preparing: return "Preparing for the open";
                case Activity.WaitingForWorkstation: return "Waiting: " + _w.Fund.StationProblem(_e);
                case Activity.Training: return _w.Fund.CurrentTrainingLabel(_e);
                case Activity.OnBreak: return "On a break";
                case Activity.RiskLocked: return "Daily loss limit reached";
                case Activity.WrappingUp: return "Wrapping up the day";
                case Activity.Leaving: return "Heading home";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (_w == null || _e == null) return;
            Spot want = Want();
            if (!_w.Watched)
            {
                // Nobody on the floor to see: they're simply where they should be.
                if (want.Kind == Kind.None || want.Kind == Kind.Exit || want.Kind == Kind.Restroom)
                {
                    if (_e.Former) Gone = true;
                    GoAway();
                }
                else Place(want);
                return;
            }

            switch (_phase)
            {
                case Phase.Away:
                    if (want.Kind == Kind.None || want.Kind == Kind.Exit) { if (_e.Former) Gone = true; break; }
                    if (want.Kind == Kind.Restroom) break;
                    if (_e.Activity != Activity.Arriving)
                    {
                        // Already in the building (hidden while the player was elsewhere): out of the restrooms.
                        SetShown(true);
                        Warp(Plan(HarborviewOffice.Restrooms[Index % HarborviewOffice.Restrooms.Length]), 180f);
                        Go(want);
                        break;
                    }
                    _phase = Phase.LiftIn;
                    _liftWait = 0f;
                    Elevator lift = HarborviewTower.ResidentsLift;
                    if (lift != null) lift.Request(HarborviewTower.StopOffice);
                    break;
                case Phase.LiftIn:
                    _liftWait += Time.deltaTime;
                    if (LiftOpenHere() || _liftWait > 25f || HarborviewTower.ResidentsLift == null)
                    {
                        Vector3 from = LiftOpenHere() ? CarPoint() : Plan(HarborviewOffice.LiftLanding);
                        SetShown(true);
                        Warp(from, 180f);
                        Go(want);
                    }
                    break;
                case Phase.Walking:
                    if (want.Key != _target.Key) Go(want);
                    Walk();
                    break;
                case Phase.Settling:
                    Settle();
                    break;
                case Phase.AtSpot:
                    if (want.Key != _target.Key) { StandUp(); Go(want); break; }
                    Idle();
                    break;
                case Phase.Inside:
                    if (want.Key != _target.Key)
                    {
                        SetShown(true);
                        Warp(_target.Pos, 180f);
                        Go(want);
                    }
                    break;
                case Phase.LiftOut:
                    LiftOut();
                    break;
            }
            Thoughts();
        }

        // ------------------------------------------------------------------ where they should be

        private Spot Want()
        {
            switch (_e.Activity)
            {
                case Activity.OffDuty:
                case Activity.AwaitingStart:
                case Activity.Commuting:
                    return default;
                case Activity.Leaving:
                case Activity.Former:
                    return _phase == Phase.Away ? default : Exit();
                case Activity.OnBreak:
                    return Break();
            }
            Spot seat = DeskSeat();
            return seat.Kind == Kind.Seat ? seat : Waiting();
        }

        private static Vector3 Plan(Vector2 p) => HarborviewOffice.World(p.x, p.y);

        private Spot Stand(string key, Vector3 at, float yaw, NpcPose pose) =>
            new Spot { Kind = Kind.Stand, Pos = at, Approach = at, Yaw = yaw, Pose = pose, Key = key };

        private Spot Exit()
        {
            Vector3 at = Plan(HarborviewOffice.LiftLanding);
            return new Spot { Kind = Kind.Exit, Pos = at, Approach = at, Yaw = 0f, Key = "exit" };
        }

        /// <summary>Their workstation's chair, if the desk they're assigned has one (working, or waiting for it to be fixed).</summary>
        private Spot DeskSeat()
        {
            if (_e.Desk == 0) return default;
            Workstation w = null;
            foreach (Workstation s in _w.Fund.Stations) if (s.Desk == _e.Desk) w = s;
            if (w == null || w.Chair == 0) return default;
            ItemView chair = _w.Home.View(w.Chair), desk = _w.Home.View(w.Desk);
            if (chair == null || desk == null) return default;
            Vector3 seat = chair.transform.position;
            Vector3 away = seat - desk.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : desk.transform.rotation * Vector3.back;
            NpcPose pose = _e.Activity switch
            {
                Activity.Training => NpcPose.Sit,
                Activity.RiskLocked => NpcPose.SitTalk,
                _ => NpcPose.Sit,
            };
            return new Spot
            {
                Kind = Kind.Seat, Pos = seat, Approach = seat + away * 0.65f, Yaw = Quaternion.LookRotation(-away).eulerAngles.y,
                Desk = w.Desk, Pose = pose, Key = "desk" + w.Desk,
            };
        }

        /// <summary>No desk to go to: a seat in reception if there's a bench free, else a marked spot to stand.</summary>
        private Spot Waiting()
        {
            int rank = Rank(e => e.Activity != Activity.OnBreak && e.Activity != Activity.Leaving && !e.Former && e.Activity > Activity.Commuting && !HasSeat(e));
            int k = 0;
            foreach (OwnedItem i in _w.Home.Belongings.Items)
            {
                if (i.ItemId != "waiting_bench" || i.State != ItemState.Placed || i.Property != HedgeFund.OfficeId) continue;
                ItemView v = _w.Home.View(i.Uid);
                if (v == null) continue;
                // A bench seats two; they sit facing out of its front (-z, as every seat here).
                for (int s = 0; s < 2; s++, k++)
                    if (k == rank)
                    {
                        Vector3 at = v.transform.TransformPoint(new Vector3(s == 0 ? -0.42f : 0.42f, 0f, 0f));
                        Vector3 front = v.transform.rotation * Vector3.back;
                        return new Spot { Kind = Kind.Seat, Pos = at, Approach = at + front * 0.6f, Yaw = Quaternion.LookRotation(front).eulerAngles.y,
                            Pose = NpcPose.Sit, Key = $"bench{i.Uid}.{s}" };
                    }
            }
            Vector2[] spots = HarborviewOffice.StandingWait;
            int j = rank - k;
            Vector3 p = Plan(spots[j % spots.Length]) + Vector3.right * (0.5f * (j / spots.Length));
            return Stand("wait" + j, p, 180f, NpcPose.Stand);
        }

        private bool HasSeat(Employee e)
        {
            if (e.Desk == 0) return false;
            foreach (Workstation s in _w.Fund.Stations) if (s.Desk == e.Desk) return s.Chair != 0;
            return false;
        }

        private Spot Break()
        {
            switch (_e.Break)
            {
                case BreakKind.Coffee:
                {
                    // A queue at the machine: first in line at the counter, the rest behind.
                    int rank = Rank(e => e.Activity == Activity.OnBreak && e.Break == BreakKind.Coffee);
                    Vector3 at = Plan(HarborviewOffice.CoffeePoint) + Vector3.right * (0.75f * rank);
                    return Stand("coffee" + rank, at, 270f, rank == 0 ? NpcPose.Drink : NpcPose.Stand);
                }
                case BreakKind.Restroom:
                {
                    int rank = Rank(e => e.Activity == Activity.OnBreak && e.Break == BreakKind.Restroom);
                    Vector3 at = Plan(HarborviewOffice.Restrooms[rank % HarborviewOffice.Restrooms.Length]);
                    return new Spot { Kind = Kind.Restroom, Pos = at, Approach = at, Yaw = 0f, Key = "restroom" + rank };
                }
                default:
                {
                    int rank = Rank(e => e.Activity == Activity.OnBreak && e.Break != BreakKind.Coffee && e.Break != BreakKind.Restroom);
                    Vector2[] spots = HarborviewOffice.BreakSpots;
                    Vector3 at = Plan(spots[rank % spots.Length]) + Vector3.forward * (0.6f * (rank / spots.Length));
                    Vector3 middle = Plan(new Vector2(-11f, -0.2f));
                    Vector3 look = middle - at;
                    look.y = 0f;
                    float yaw = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f;
                    NpcPose pose = _e.Break == BreakKind.Chat ? NpcPose.Phone : _e.Break == BreakKind.Lunch ? NpcPose.Drink : NpcPose.Stand;
                    return Stand("break" + rank, at, yaw, pose);
                }
            }
        }

        /// <summary>Their place in line among the people matching <paramref name="match"/> (in hiring order).</summary>
        private int Rank(System.Predicate<Employee> match)
        {
            int n = 0;
            foreach (Employee e in _w.Fund.Employees)
            {
                if (e == _e) return n;
                if (match(e)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ moving

        private void Go(Spot s)
        {
            _target = s;
            _phase = Phase.Walking;
            if (AgentReady(transform.position)) _agent.SetDestination(s.Approach);
        }

        private bool AgentReady(Vector3 at)
        {
            if (!_w.NavReady) return false;
            if (_agent.enabled && _agent.isOnNavMesh) return true;
            if (!NavMesh.SamplePosition(at, out NavMeshHit hit, 1.2f, NavMesh.AllAreas)) return false;
            _agent.enabled = true;
            _agent.Warp(hit.position);
            return _agent.isOnNavMesh;
        }

        private void Walk()
        {
            OpenDoors();
            bool arrived;
            if (_agent.enabled && _agent.isOnNavMesh)
            {
                if (!_agent.hasPath && !_agent.pathPending) _agent.SetDestination(_target.Approach);
                arrived = !_agent.pathPending && _agent.remainingDistance <= 0.2f;
                // A destination off the mesh (a chair boxed in): the nearest reachable point will do.
                if (!arrived && !_agent.pathPending && _agent.pathStatus == NavMeshPathStatus.PathInvalid) arrived = true;
                _body.Animate(_agent.velocity.sqrMagnitude > 0.04f ? NpcPose.Walk : NpcPose.Stand, Time.time, _agent.velocity.magnitude / 1.35f);
            }
            else
            {
                // No navigation mesh yet: straight there.
                if (_w.NavReady && AgentReady(transform.position)) { _agent.SetDestination(_target.Approach); return; }
                Vector3 p = transform.position, to = _target.Approach;
                Vector3 step = Vector3.MoveTowards(p, to, WalkSpeed * Time.deltaTime);
                Vector3 dir = to - p;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), 0.2f);
                transform.position = step;
                _body.Animate(NpcPose.Walk, Time.time);
                arrived = (step - to).sqrMagnitude < 0.01f;
            }
            if (!arrived) return;
            switch (_target.Kind)
            {
                case Kind.Seat:
                    _phase = Phase.Settling;
                    _settle = 0f;
                    StopAgent();
                    _settleFrom = transform.position;
                    _settleRotFrom = transform.rotation;
                    break;
                case Kind.Restroom:
                    _phase = Phase.Inside;
                    SetShown(false);
                    break;
                case Kind.Exit:
                    _phase = Phase.LiftOut;
                    _liftWait = 0f;
                    HarborviewTower.ResidentsLift?.Request(HarborviewTower.StopOffice);
                    break;
                default:
                    _phase = Phase.AtSpot;
                    break;
            }
        }

        /// <summary>From behind the chair into it: a short slide and turn, then seated.</summary>
        private void Settle()
        {
            _settle = Mathf.MoveTowards(_settle, 1f, Time.deltaTime / 0.8f);
            float k = Mathf.SmoothStep(0f, 1f, _settle);
            transform.position = Vector3.Lerp(_settleFrom, _target.Pos, k);
            transform.rotation = Quaternion.Slerp(_settleRotFrom, Quaternion.Euler(0f, _target.Yaw, 0f), k);
            _body.Animate(_settle > 0.5f ? NpcPose.Sit : NpcPose.Walk, Time.time, 0.4f);
            if (_settle >= 1f) _phase = Phase.AtSpot;
        }

        private void StandUp()
        {
            if (_target.Kind == Kind.Seat) transform.position = _target.Approach;
            _book?.SetActive(false);
        }

        private void StopAgent()
        {
            if (_agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.ResetPath();
                _agent.enabled = false;
            }
        }

        private void Warp(Vector3 at, float yaw)
        {
            StopAgent();
            transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Swing doors (restrooms, meeting room) opened as they reach them; automatic ones open by themselves.</summary>
        private void OpenDoors()
        {
            foreach (Door d in _w.Doors)
                if (d != null && (d.transform.position - transform.position).sqrMagnitude < 1.8f * 1.8f) d.OpenFor(transform.position, key: true);
        }

        // ------------------------------------------------------------------ the lift

        private Vector3 CarPoint()
        {
            Elevator lift = HarborviewTower.ResidentsLift;
            Vector3 floor = Plan(HarborviewOffice.LiftLanding);
            return lift != null ? new Vector3(lift.transform.position.x, floor.y, lift.transform.position.z) : floor;
        }

        private static bool LiftOpenHere()
        {
            Elevator lift = HarborviewTower.ResidentsLift;
            return lift != null && lift.CarFloor == HarborviewTower.StopOffice && lift.Logic.State != ElevatorState.Moving && lift.Logic.DoorOpen > 0.7f;
        }

        /// <summary>Wait at the landing for the car, step in, and they're gone when the doors close behind them.</summary>
        private void LiftOut()
        {
            _liftWait += Time.deltaTime;
            Elevator lift = HarborviewTower.ResidentsLift;
            bool inCar = lift != null && (new Vector2(transform.position.x - CarPoint().x, transform.position.z - CarPoint().z)).sqrMagnitude < 0.3f;
            if (lift == null || _liftWait > 30f || (inCar && !LiftOpenHere()))
            {
                if (_e.Former) Gone = true;
                GoAway();
                return;
            }
            if (!inCar && LiftOpenHere())
            {
                StopAgent();
                Vector3 to = CarPoint();
                transform.position = Vector3.MoveTowards(transform.position, to, WalkSpeed * Time.deltaTime);
                Vector3 dir = to - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir.normalized);
                _body.Animate(NpcPose.Walk, Time.time);
            }
            else _body.Animate(NpcPose.Stand, Time.time);
        }

        private void GoAway()
        {
            _phase = Phase.Away;
            _target = default;
            SetShown(false);
        }

        /// <summary>Unwatched: straight to the spot, in its pose.</summary>
        private void Place(Spot s)
        {
            if (_phase == Phase.AtSpot && s.Key == _target.Key) { Idle(); return; }
            _target = s;
            SetShown(true);
            Warp(s.Pos, s.Yaw);
            _phase = Phase.AtSpot;
            Idle();
        }

        // ------------------------------------------------------------------ at their spot

        private void Idle()
        {
            if (Time.time >= _idleUntil)
            {
                // Mostly the spot's own pose; now and then a call (seated) or a glance at the phone (standing).
                bool aside = _rng.NextDouble() < 0.18;
                _idle = _target.Kind == Kind.Seat ? (aside ? NpcPose.SitTalk : _target.Pose) : aside && _target.Pose == NpcPose.Stand ? NpcPose.Phone : _target.Pose;
                _idleUntil = Time.time + (aside ? 6f : 14f) + (float)_rng.NextDouble() * 16f;
            }
            _body.Animate(_idle, Time.time);
            bool studying = _e.Activity == Activity.Training && _target.Kind == Kind.Seat;
            if (studying && _book == null) _book = Book();
            if (_book != null && _book.activeSelf != studying) _book.SetActive(studying);
            if (studying) PlaceBook();
        }

        /// <summary>An open textbook on the desk in front of them while they study.</summary>
        private GameObject Book()
        {
            Kit k = _w.City.Kit;
            Transform b = Kit.Group(transform.parent, _e.Name + " book");
            k.Box(b, "Cover", new Vector3(0f, 0.008f, 0f), new Vector3(0.34f, 0.016f, 0.24f), _w.City.P.Lit(new Color(0.45f, 0.12f, 0.1f), 0.3f), collider: false);
            k.Box(b, "Pages", new Vector3(0f, 0.02f, 0f), new Vector3(0.32f, 0.012f, 0.22f), _w.City.P.Lit(new Color(0.95f, 0.93f, 0.88f), 0.1f), collider: false);
            k.Box(b, "Spine", new Vector3(0f, 0.027f, 0f), new Vector3(0.006f, 0.004f, 0.22f), _w.City.P.Lit(new Color(0.3f, 0.08f, 0.07f), 0.3f), collider: false);
            // The progress marker: a slim bar standing behind the book, filling as the course goes.
            Transform bar = Kit.Group(b, "Progress", new Vector3(0f, 0.34f, 0.2f));
            k.Box(bar, "Track", Vector3.zero, new Vector3(0.4f, 0.035f, 0.01f), _w.City.P.Lit(new Color(0.1f, 0.11f, 0.13f), 0.2f), collider: false);
            Transform fill = Kit.Group(bar, "Fill pivot", new Vector3(-0.19f, 0f, -0.006f));
            k.Box(fill, "Fill", new Vector3(0.19f, 0f, 0f), new Vector3(0.38f, 0.022f, 0.004f), _w.City.P.Glow(new Color(0.25f, 0.8f, 0.45f), 1.2f), collider: false);
            _progressFill = fill;
            return b.gameObject;
        }

        private void PlaceBook()
        {
            ItemView desk = _w.Home.View(_target.Desk);
            if (desk == null) return;
            float top = desk.Spec.Height + desk.transform.position.y;
            Vector3 toDesk = Quaternion.Euler(0f, _target.Yaw, 0f) * Vector3.forward;
            _book.transform.SetPositionAndRotation(new Vector3(transform.position.x, top, transform.position.z) + toDesk * 0.55f, Quaternion.Euler(0f, _target.Yaw, 0f));
            TrainingJob job = _e.CurrentTraining;
            float done = job != null && job.Minutes > 0 ? Mathf.Clamp01((float)(job.MinutesDone / job.Minutes)) : 0f;
            _progressFill.localScale = new Vector3(Mathf.Max(0.001f, done), 1f, 1f);
            // Readable up close only: the bar is for someone standing at the desk.
            bool near = _w.City.Player != null && (_w.City.Player.position - transform.position).sqrMagnitude < 8f * 8f;
            if (_progressFill.parent.gameObject.activeSelf != near) _progressFill.parent.gameObject.SetActive(near);
        }

        // ------------------------------------------------------------------ thoughts

        /// <summary>
        /// Sparingly (FUND_SPEC §18): a short thought over someone's head when it tells the player something (no desk,
        /// locked out, studying, unhappy), only close up, at most two on the floor at once.
        /// </summary>
        private void Thoughts()
        {
            if (_bubble.gameObject.activeSelf)
            {
                Camera cam = Camera.main;
                if (cam != null) _bubble.rotation = Quaternion.LookRotation(_bubble.position - cam.transform.position);
                if (Time.time < _bubbleUntil && _shown) return;
                _bubble.gameObject.SetActive(false);
                _w.BubblesShown = Mathf.Max(0, _w.BubblesShown - 1);
                return;
            }
            if (!_shown || Time.time < _nextBubble || _w.BubblesShown >= 2 || _w.City.Player == null) return;
            if ((_w.City.Player.position - transform.position).sqrMagnitude > 9f * 9f) return;
            _nextBubble = Time.time + 70f + (float)_rng.NextDouble() * 80f;
            string thought = Thought();
            if (string.IsNullOrEmpty(thought)) return;
            _bubbleText.text = thought;
            // The card fits the text: its longest line across, a line's height per line.
            string[] lines = thought.Split('\n');
            int longest = 0;
            foreach (string l in lines) longest = Mathf.Max(longest, l.Length);
            _bubbleBack.localScale = new Vector3(0.05f * longest + 0.12f, 0.1f * lines.Length + 0.06f, 0.005f);
            _bubble.localPosition = new Vector3(0f, _phase == Phase.AtSpot && _target.Kind == Kind.Seat ? 1.6f : 2.05f, 0f);
            _bubble.gameObject.SetActive(true);
            _bubbleUntil = Time.time + 4.5f;
            _w.BubblesShown++;
        }

        private static readonly string[] NoCapital =
        {
            "When's the boss giving me\ncapital to trade with?",
            "Can't trade on zero dollars...",
            "Nice desk. Now I just\nneed some capital",
            "Watching setups I can't take.\nNeed an allocation",
        };

        private string Thought()
        {
            switch (_e.Activity)
            {
                case Activity.WaitingForWorkstation:
                    string why = _w.Fund.StationProblem(_e);
                    return string.IsNullOrEmpty(why) ? "Waiting for a desk" : why.Split('.')[0];
                case Activity.RiskLocked: return "Hit my loss limit for today";
                case Activity.Training: return _w.Fund.CurrentTrainingLabel(_e);
                case Activity.Trading:
                case Activity.Preparing:
                    // At the desk with nothing allocated: nothing to trade with.
                    if (_e.Base <= 0m && _e.DeskEquity <= 0m) return NoCapital[_rng.Next(NoCapital.Length)];
                    break;
            }
            if (_e.Satisfaction < 40)
            {
                string reason = HedgeFund.WorstReason(_e);
                return string.IsNullOrEmpty(reason) ? null : reason.Split('.')[0];
            }
            return null;
        }

        // ------------------------------------------------------------------ visibility

        private void SetShown(bool shown)
        {
            _shown = shown;
            _body.transform.GetChild(0).gameObject.SetActive(shown);
            _col.enabled = shown;
            if (!shown)
            {
                StopAgent();
                if (_bubble.gameObject.activeSelf)
                {
                    _bubble.gameObject.SetActive(false);
                    _w.BubblesShown = Mathf.Max(0, _w.BubblesShown - 1);
                }
                _book?.SetActive(false);
                Door.Walkers.Remove(transform);
            }
            else if (!Door.Walkers.Contains(transform)) Door.Walkers.Add(transform);
        }

        private void OnDestroy()
        {
            Door.Walkers.Remove(transform);
            if (_book != null) Destroy(_book);
        }
    }
}
