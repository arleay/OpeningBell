using System;
using System.Collections.Generic;
using OpeningBell.Economy;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    public enum PizzaItem { CheeseSlice, PepperoniSlice, WholePie }

    /// <summary>
    /// The counter job at Sal's Pizza (PROP_SPEC §5). Ask at the time clock to get hired, clock in while the shop is
    /// open. Customers come in, queue at the counter and order; take what they asked for from the warmer, hand it
    /// over, ring it up on the register, and they pay and leave. Tips shrink the longer they waited; a customer who
    /// runs out of patience walks out. Wages and tips go into the bank at clock-out (or when you leave or the shop
    /// closes). All positions are in the shop's local frame (x across, z from the front wall inward).
    /// </summary>
    public sealed class PizzaShop : MonoBehaviour
    {
        public static readonly (string Name, decimal Price, int MaxHeld)[] Menu =
        {
            ("Cheese slice", 3.50m, 3),
            ("Pepperoni slice", 4.00m, 3),
            ("Whole pie", 18.00m, 1),
        };

        /// <summary>Real seconds a customer will wait from walking in.</summary>
        public const float Patience = 90f;
        private const int PoolSize = 4;
        private const int MaxInShop = 3;
        private const float WalkSpeed = 1.3f;
        private const float LeaveDistance = 35f;
        private const string Boss = "Sal";

        private sealed class Customer
        {
            public enum State { Gone, Walking, Waiting, Paying, Leaving }

            public NpcBody Body;
            public Collider Tap;
            public State Now = State.Gone;
            public readonly List<Vector3> Path = new List<Vector3>();
            public PizzaItem Item;
            public int Count;
            public float ArrivedAt;
            public bool Ordered;
            public int Slot = -1;

            public decimal Ticket => Menu[(int)Item].Price * Count;
        }

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Transform _player;
        private Hours _hours;
        private System.Random _rng;
        private readonly List<Customer> _pool = new List<Customer>();
        private readonly List<Customer> _queue = new List<Customer>();
        private Vector3 _door, _aisleFront, _counterSpot;
        private float _slotStep;
        private TextMesh _board, _hiring;
        private Transform _held;
        private GameObject[] _heldSlices;
        private GameObject _heldPie;
        private float _nextSpawn;

        public PizzaItem? Held { get; private set; }
        public int HeldCount { get; private set; }
        public Job Job => _game.Job;
        public int CustomersInShop => _queue.Count;

        /// <summary>Behind the counter facing the customer: where you stand to work (world space).</summary>
        public Vector3 WorkSpot => transform.TransformPoint(_counterSpot + new Vector3(0f, 0.05f, 1.9f));
        public float WorkYaw => transform.eulerAngles.y + 180f;

        /// <summary>A customer at the counter has ordered and is waiting for it.</summary>
        public bool OrderWaiting => AtCounter != null && AtCounter.Now == Customer.State.Waiting;
        public PizzaItem OrderItem => AtCounter?.Item ?? PizzaItem.CheeseSlice;
        public int OrderCount => AtCounter?.Count ?? 0;

        /// <summary>The customer at the counter, if they've ordered.</summary>
        private Customer AtCounter => _queue.Count > 0 && _queue[0].Slot == 0 && _queue[0].Ordered ? _queue[0] : null;

        public static PizzaShop Build(CityContext c, Transform root, Transform dyn, Business b, float doorX, int seed)
        {
            Kit k = c.Kit;
            float hw = b.Width / 2f - Businesses.Wall, d = b.Depth - Businesses.Wall, cz = d - 2.3f;
            // On its own object in the dynamic group: the static shell gets merged into combined meshes at load.
            var shop = new GameObject("Sal's Pizza job").AddComponent<PizzaShop>();
            shop.transform.SetParent(dyn, false);
            shop._game = c.Game;
            shop._hud = c.Hud;
            shop._player = c.Player;
            shop._hours = b.Hours;
            shop._rng = new System.Random(seed);

            // Customers walk in the door, up the aisle between the table columns, and queue back from the counter.
            float aisleX = -hw + 1.6f + 1.8f + 3.6f; // halfway between the 2nd and 3rd table columns
            shop._door = new Vector3(doorX, 0f, 0.5f);
            shop._aisleFront = new Vector3(aisleX, 0f, 1.1f);
            shop._counterSpot = new Vector3(aisleX, 0f, cz - 0.9f);
            shop._slotStep = 0.9f;

            // The warmer on the back wall: cheese, pepperoni, whole pies; a bin; a board above showing the order.
            Material steel = c.P.Lit(new Color(0.62f, 0.64f, 0.66f), 0.6f);
            Material glass = c.P.Glass(new Color(0.8f, 0.85f, 0.9f, 0.25f));
            float wx = aisleX - 1.2f;
            k.Span(root, "Warmer counter", new Vector3(wx - 1.5f, 0f, d - 0.75f), new Vector3(wx + 1.5f, 0.95f, d - 0.05f), steel);
            k.Span(root, "Warmer glass", new Vector3(wx - 1.45f, 0.95f, d - 0.72f), new Vector3(wx + 1.45f, 1.35f, d - 0.1f), glass, collider: false);
            for (int i = 0; i < Menu.Length; i++)
            {
                var at = new Vector3(wx - 1f + i, 0.96f, d - 0.4f);
                if (i < 2)
                    for (int s = 0; s < 3; s++) k.Prop(root, "Pizza_Slice", at + new Vector3(-0.2f + s * 0.2f, 0f, 0f), 0f, 180f + s * 25f, 0.17f);
                else k.Prop(root, "Pizza", at, 0f, 0f, 0.36f);
                k.Text(root, $"{Menu[i].Name.ToUpperInvariant()}  ${Menu[i].Price:0.00}", at + new Vector3(0f, 0.5f, -0.33f), 0f, 0.05f, Color.white);
                var aim = new GameObject(Menu[i].Name + " (warmer)");
                aim.transform.SetParent(dyn, false);
                aim.transform.localPosition = at + new Vector3(0f, 0.15f, 0f);
                var box = aim.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.8f, 0.4f, 0.6f);
                aim.AddComponent<PizzaWarmer>().Configure(shop, (PizzaItem)i);
            }
            var bin = k.Box(dyn, "Bin", new Vector3(wx + 1.9f, 0.4f, d - 0.4f), new Vector3(0.45f, 0.8f, 0.45f), c.P.Lit(new Color(0.15f, 0.15f, 0.16f)));
            bin.AddComponent<PizzaBin>().Configure(shop);
            shop._board = k.Text(dyn, "", new Vector3(wx, 2.25f, d - 0.08f), 0f, 0.09f, new Color(1f, 0.85f, 0.4f));

            // Register: the counter's existing till (ShopStock.Register sits at hw·0.4 + 0.5).
            var till = new GameObject("Register (ring up)");
            till.transform.SetParent(dyn, false);
            till.transform.localPosition = new Vector3(hw * 0.4f + 0.5f, 1.2f, cz);
            var tillBox = till.AddComponent<BoxCollider>();
            tillBox.isTrigger = true;
            tillBox.size = new Vector3(0.6f, 0.4f, 0.6f);
            till.AddComponent<PizzaRegister>().Configure(shop);

            // Time clock on the side wall behind the counter; a hiring sign in the window until you're hired.
            var clock = k.Box(dyn, "Time clock", new Vector3(hw - 0.08f, 1.4f, d - 1.2f), new Vector3(0.1f, 0.35f, 0.25f), c.P.Lit(new Color(0.8f, 0.78f, 0.7f)));
            k.Text(dyn, "TIME CLOCK", new Vector3(hw - 0.14f, 1.7f, d - 1.2f), 90f, 0.035f, new Color(0.1f, 0.1f, 0.1f));
            clock.AddComponent<PizzaClock>().Configure(shop);
            shop._hiring = k.Text(dyn, "NOW HIRING · ASK INSIDE", new Vector3(doorX - 2.3f, 1.55f, -0.03f), 0f, 0.07f, new Color(1f, 0.3f, 0.2f));

            // What you carry: slices on a plate or a boxed pie, low in the view (parented to the camera at run time).
            var held = Kit.Group(dyn, "Held pizza");
            shop._held = held;
            shop._heldSlices = new GameObject[3];
            for (int s = 0; s < 3; s++) shop._heldSlices[s] = k.Prop(held, "Pizza_Slice", new Vector3(-0.05f + s * 0.05f, s * 0.012f, 0f), 0f, s * 30f, 0.12f);
            shop._heldPie = k.Prop(held, "Pizza", Vector3.zero, 0f, 0f, 0.26f);
            foreach (Collider col in held.GetComponentsInChildren<Collider>()) Destroy(col);
            held.gameObject.SetActive(false);

            for (int i = 0; i < PoolSize; i++)
            {
                NpcBody body = NpcBody.Create(k, dyn, "Customer", seed * 7 + i * 131, Color.HSVToRGB((seed * 0.13f + i * 0.27f) % 1f, 0.45f, 0.65f));
                var cap = body.gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; // customers never block you
                cap.center = new Vector3(0f, 0.9f, 0f);
                cap.height = 1.8f;
                cap.radius = 0.35f;
                var cust = new Customer { Body = body, Tap = cap };
                body.gameObject.AddComponent<PizzaCustomerTap>().Configure(shop, () => shop.CustomerPrompt(cust), () => shop.HandOver(cust),
                    () => shop.AtCounter == cust && cust.Now == Customer.State.Waiting);
                shop._pool.Add(cust);
                shop.Hide(cust);
            }
            return shop;
        }

        // ---------------------------------------------------------------- the clock

        public string ClockPrompt =>
            !Job.Hired ? "Ask about the job"
            : Job.OnShift ? "Clock out"
            : _hours.Contains(_game.Clock.Now) ? $"Clock in (${Job.HourlyRate:0}/h + tips)" : "Closed: no shifts now";

        public string ClockDetails
        {
            get
            {
                if (!Job.OnShift) return Job.Hired ? $"{Job.TotalShifts} shifts worked · ${Job.TotalEarned:N2} earned" : null;
                TimeSpan t = _game.Clock.Now - Job.ClockedInAt.Value;
                return $"On shift {(int)t.TotalHours}h {t.Minutes:00}m · {Job.ShiftOrders} orders · wages ${Job.WageSoFar(_game.Clock.Now):N2} · tips ${Job.ShiftTips:N2}";
            }
        }

        public bool ClockUsable => !Job.Hired || Job.OnShift || _hours.Contains(_game.Clock.Now);

        public void UseClock()
        {
            if (!Job.Hired)
            {
                Job.Hire();
                Say(Boss, $"Need work? ${Job.HourlyRate:0} an hour plus whatever tips you earn. Punch in here any time we're open, work the counter, punch out when you're done.");
                return;
            }
            if (Job.OnShift)
            {
                ClockOut("Good shift.");
                return;
            }
            if (!_hours.Contains(_game.Clock.Now)) return;
            Job.ClockIn(_game.Clock.Now);
            _nextSpawn = Time.time + 4f;
            Say(Boss, "Counter's yours. Take the order, grab it from the warmer, hand it over, ring it up.");
        }

        private void ClockOut(string opener)
        {
            ShiftSummary s = Job.ClockOut(_game.Clock.Now);
            DateTime now = _game.Clock.Now;
            if (s.Wage > 0m) _game.Economy.Receive(s.Wage, "Sal's Pizza wages", now);
            if (s.Tips > 0m) _game.Economy.Receive(s.Tips, "Tips · Sal's Pizza", now);
            foreach (Customer c in _pool) Hide(c);
            _queue.Clear();
            Bin();
            Say(Boss, $"{opener} {(int)s.Worked.TotalHours}h {s.Worked.Minutes:00}m, {s.Orders} orders{(s.Missed > 0 ? $", {s.Missed} walked out" : "")}: " +
                      $"${s.Wage:N2} wages and ${s.Tips:N2} in tips, straight to your bank.");
        }

        // ---------------------------------------------------------------- carrying

        public string WarmerPrompt(PizzaItem item)
        {
            if (!Job.OnShift) return "Warmer (staff only)";
            string name = Menu[(int)item].Name.ToLowerInvariant();
            if (Held.HasValue && Held != item) return $"Take a {name} (hands full)";
            return HeldCount > 0 ? $"Take another {name} ({HeldCount} in hand)" : $"Take a {name}";
        }

        public void TakeFromWarmer(PizzaItem item)
        {
            if (!Job.OnShift) return;
            if (Held.HasValue && Held != item)
            {
                _hud.ShowToast("Hands full: hand it over or bin it first.", 3f);
                return;
            }
            if (HeldCount >= Menu[(int)item].MaxHeld)
            {
                _hud.ShowToast(item == PizzaItem.WholePie ? "One pie at a time." : "That's all the plate holds.", 3f);
                return;
            }
            Held = item;
            HeldCount++;
            ShowHeld();
        }

        public void Bin()
        {
            Held = null;
            HeldCount = 0;
            ShowHeld();
        }

        private void ShowHeld()
        {
            _held.gameObject.SetActive(HeldCount > 0);
            for (int s = 0; s < _heldSlices.Length; s++)
                if (_heldSlices[s] != null) _heldSlices[s].SetActive(Held != PizzaItem.WholePie && s < HeldCount);
            if (_heldPie != null) _heldPie.SetActive(Held == PizzaItem.WholePie);
        }

        private static string Describe(PizzaItem item, int count) => item == PizzaItem.WholePie
            ? (count == 1 ? "a whole pie" : $"{count} whole pies")
            : count == 1 ? $"one {Menu[(int)item].Name.ToLowerInvariant()}" : $"{count} {Menu[(int)item].Name.ToLowerInvariant()}s";

        // ---------------------------------------------------------------- customers

        private string CustomerPrompt(Customer c)
        {
            string order = Describe(c.Item, c.Count);
            if (!Held.HasValue) return $"Wants {order}";
            return $"Hand over {Describe(Held.Value, HeldCount)}";
        }

        private void HandOver(Customer c)
        {
            if (!Held.HasValue)
            {
                Say("Customer", $"Just {Describe(c.Item, c.Count)}, please.");
                return;
            }
            if (Held != c.Item || HeldCount != c.Count)
            {
                Say("Customer", $"That's not what I asked for. {Capital(Describe(c.Item, c.Count))}, please.");
                return;
            }
            Bin();
            c.Now = Customer.State.Paying;
            Say("Customer", "Perfect, thanks.");
        }

        public string RegisterPrompt
        {
            get
            {
                Customer c = Paying;
                return c == null ? "Register" : $"Ring up ${c.Ticket:0.00}";
            }
        }

        public bool RegisterUsable => Paying != null;

        private Customer Paying => _queue.Count > 0 && _queue[0].Now == Customer.State.Paying ? _queue[0] : null;

        public void RingUp()
        {
            Customer c = Paying;
            if (c == null) return;
            decimal tip = Job.Served(c.Ticket, Time.time - c.ArrivedAt, Patience);
            Say("Customer", tip >= c.Ticket * 0.15m ? "That was quick. Keep the change." : tip > 0.25m ? "Thanks." : "Finally. Here.");
            if (tip > 0m) _hud.ShowToast($"Tip ${tip:0.00}", 2.5f);
            Leave(c);
        }

        private void Update()
        {
            if (Camera.main != null && _held.parent != Camera.main.transform)
            {
                _held.SetParent(Camera.main.transform, false);
                _held.localPosition = new Vector3(0.24f, -0.26f, 0.5f);
                _held.localRotation = Quaternion.Euler(20f, 0f, 0f);
            }

            UpdateHiringSign();
            if (!Job.OnShift)
            {
                SetBoard("SAL'S PIZZA\nSLICES $3.50 · PIES $18");
                return;
            }

            // Walked off or closing time: the shift ends and gets paid.
            if (Vector3.Distance(_player.position, transform.position) > LeaveDistance)
            {
                ClockOut("You left mid-shift, so I clocked you out.");
                return;
            }
            if (!_hours.Contains(_game.Clock.Now))
            {
                ClockOut("We're closed. Nice work tonight.");
                return;
            }

            SpawnIfDue();
            foreach (Customer c in _pool) Step(c);

            Customer at = _queue.Count > 0 ? _queue[0] : null;
            SetBoard(at == null || !at.Ordered ? "NOW SERVING\n—"
                : at.Now == Customer.State.Paying ? $"RING UP\n${at.Ticket:0.00}"
                : $"NOW SERVING\n{at.Count} × {Menu[(int)at.Item].Name.ToUpperInvariant()}");
        }

        private void SpawnIfDue()
        {
            if (Time.time < _nextSpawn) return;
            int hour = _game.Clock.Now.Hour;
            bool rush = (hour >= 12 && hour < 14) || (hour >= 18 && hour < 21);
            float gap = rush ? 12f + (float)_rng.NextDouble() * 8f : 22f + (float)_rng.NextDouble() * 16f;
            _nextSpawn = Time.time + gap;
            if (_queue.Count >= MaxInShop) return;
            Customer c = _pool.Find(x => x.Now == Customer.State.Gone);
            if (c == null) return;

            double roll = _rng.NextDouble();
            c.Item = roll < 0.2 ? PizzaItem.WholePie : roll < 0.55 ? PizzaItem.CheeseSlice : PizzaItem.PepperoniSlice;
            c.Count = c.Item == PizzaItem.WholePie ? 1 : 1 + _rng.Next(3);
            c.ArrivedAt = Time.time;
            c.Ordered = false;
            c.Now = Customer.State.Walking;
            c.Body.transform.localPosition = _door;
            c.Body.transform.GetChild(0).gameObject.SetActive(true);
            c.Tap.enabled = true;
            _queue.Add(c);
            c.Slot = -1;
            Reslot();
        }

        /// <summary>Everyone in the queue walks to their place: 0 at the counter, then back down the aisle.</summary>
        private void Reslot()
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                Customer c = _queue[i];
                if (c.Slot == i) continue;
                bool fresh = c.Slot < 0;
                c.Slot = i;
                c.Path.Clear();
                if (fresh) c.Path.Add(_aisleFront);
                c.Path.Add(SlotPosition(i));
                if (c.Now == Customer.State.Waiting) c.Now = Customer.State.Walking;
            }
        }

        private Vector3 SlotPosition(int slot) => _counterSpot - new Vector3(0f, 0f, slot * _slotStep);

        private void Step(Customer c)
        {
            if (c.Now == Customer.State.Gone) return;
            if (c.Now == Customer.State.Walking || c.Now == Customer.State.Leaving)
            {
                if (Walk(c)) return;
                if (c.Now == Customer.State.Leaving)
                {
                    Hide(c);
                    return;
                }
                c.Now = Customer.State.Waiting;
                c.Body.transform.localRotation = Quaternion.identity; // face the counter (+z)
            }

            if (c.Now == Customer.State.Waiting || c.Now == Customer.State.Paying)
                c.Body.Animate(NpcPose.Stand, Time.time);

            if (c.Now == Customer.State.Waiting && c.Slot == 0 && !c.Ordered)
            {
                c.Ordered = true;
                Say("Customer", Greeting(c));
            }
            if (c.Now == Customer.State.Waiting && Time.time - c.ArrivedAt > Patience)
            {
                Job.Missed();
                Say("Customer", "Forget it. I'll go somewhere else.");
                Leave(c);
            }
        }

        private string Greeting(Customer c)
        {
            string order = Describe(c.Item, c.Count);
            return _rng.Next(3) switch
            {
                0 => $"Hi, can I get {order}?",
                1 => $"{Capital(order)}, please.",
                _ => $"Hey. {Capital(order)} to go.",
            };
        }

        /// <summary>Moves along the path; true while still walking.</summary>
        private bool Walk(Customer c)
        {
            if (c.Path.Count == 0) return false;
            Transform t = c.Body.transform;
            Vector3 target = c.Path[0];
            Vector3 p = t.localPosition;
            Vector3 dir = target - p;
            if (dir.sqrMagnitude > 1e-4f) t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.LookRotation(dir.normalized), 0.2f);
            t.localPosition = Vector3.MoveTowards(p, target, WalkSpeed * Time.deltaTime);
            c.Body.Animate(NpcPose.Walk, Time.time);
            if ((t.localPosition - target).sqrMagnitude < 0.0004f) c.Path.RemoveAt(0);
            return c.Path.Count > 0;
        }

        private void Leave(Customer c)
        {
            _queue.Remove(c);
            c.Now = Customer.State.Leaving;
            c.Slot = -1;
            c.Path.Clear();
            c.Path.Add(_aisleFront);
            c.Path.Add(_door);
            Reslot();
        }

        private void Hide(Customer c)
        {
            c.Now = Customer.State.Gone;
            c.Path.Clear();
            c.Slot = -1;
            c.Body.transform.GetChild(0).gameObject.SetActive(false);
            c.Tap.enabled = false;
        }

        private void UpdateHiringSign()
        {
            bool show = !Job.Hired;
            if (_hiring.gameObject.activeSelf != show) _hiring.gameObject.SetActive(show);
        }

        private void SetBoard(string text)
        {
            if (_board.text != text) _board.text = text;
        }

        private void Say(string who, string line) => _hud.ShowSubtitle(who, line);

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    public sealed class PizzaClock : Interactable
    {
        private PizzaShop _shop;
        public void Configure(PizzaShop shop) => _shop = shop;
        public override string Prompt => _shop.ClockPrompt;
        public override string Details => _shop.ClockDetails;
        public override bool CanInteract => base.CanInteract && _shop.ClockUsable;
        public override void Interact() => _shop.UseClock();
    }

    public sealed class PizzaWarmer : Interactable
    {
        private PizzaShop _shop;
        private PizzaItem _item;

        public void Configure(PizzaShop shop, PizzaItem item)
        {
            _shop = shop;
            _item = item;
        }

        public override string Prompt => _shop.WarmerPrompt(_item);
        public override bool CanInteract => base.CanInteract && _shop.Job.OnShift;
        public override void Interact() => _shop.TakeFromWarmer(_item);
    }

    public sealed class PizzaBin : Interactable
    {
        private PizzaShop _shop;
        public void Configure(PizzaShop shop) => _shop = shop;
        public override string Prompt => "Bin what you're holding";
        public override bool CanInteract => base.CanInteract && _shop.HeldCount > 0;
        public override void Interact() => _shop.Bin();
    }

    public sealed class PizzaRegister : Interactable
    {
        private PizzaShop _shop;
        public void Configure(PizzaShop shop) => _shop = shop;
        public override string Prompt => _shop.RegisterPrompt;
        public override bool CanInteract => base.CanInteract && _shop.RegisterUsable;
        public override void Interact() => _shop.RingUp();
    }

    public sealed class PizzaCustomerTap : Interactable
    {
        private PizzaShop _shop;
        private Func<string> _prompt;
        private Action _use;
        private Func<bool> _usable;

        public void Configure(PizzaShop shop, Func<string> prompt, Action use, Func<bool> usable)
        {
            _shop = shop;
            _prompt = prompt;
            _use = use;
            _usable = usable;
        }

        public override string Prompt => _prompt();
        public override bool CanInteract => base.CanInteract && _shop.Job.OnShift && _usable();
        public override void Interact() => _use();
    }
}
