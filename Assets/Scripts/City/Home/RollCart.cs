using System.Collections.Generic;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpeningBell.City
{
    /// <summary>
    /// Harborview's moving cart: a roll cage parked by the concierge desk in the lobby. [E] on its push end takes hold
    /// ($5 a use, paid when it leaves its spot); it then rolls ahead of you wherever you walk, into the lift and up to
    /// the penthouse. Its rollers are a load bed like a truck's: set boxes and furniture on it, take them off, put them
    /// back. [E] lets go. [F] twice sends it back down to its spot in the lobby, with whatever is still on it.
    /// Local frame (see Resources/Models/RollCart.obj): +z forward (the tow bar), the push end at z 0, casters on y 0.
    /// </summary>
    public sealed class RollCart : MonoBehaviour
    {
        public const string BedId = "harborview_cart";

        // The model's measurements (Tools/Models/stl2obj.py): frame 0.83 m across, 0.1–1.32 m along, rails up to
        // 0.94 m; the rollers' tops (the load deck) 0.196 m up; the tow bar reaches 1.66 m.
        private const float HalfWidth = 0.415f, FrameStart = 0.096f, FrameEnd = 1.32f, Length = 1.66f, Deck = 0.196f, Top = 0.944f;
        /// <summary>m³: two bays of 0.52 × 0.8 m, loaded about as high as the rails.</summary>
        private const float Capacity = 1.1f;
        /// <summary>The rails that keep you from stepping onto the deck: low enough to aim over at what's on it.</summary>
        private const float RailTop = 0.55f;
        /// <summary>A step the cart won't roll over (kerbs, stairs): its floor must be this close to your feet.</summary>
        private const float MaxStep = 0.2f;
        private const float ConfirmSeconds = 4f;

        private HomeWorld _w;
        private FirstPersonController _player;
        private CharacterController _body;
        private PlayerInteractor _interactor;
        private Elevator _lift;
        private Vector3 _home;
        private float _homeYaw;
        private readonly List<Collider> _solids = new List<Collider>();
        private bool _pushing;
        private Vector3 _lastPlayer;
        private Quaternion _lastTurn;
        private float _confirmUntil, _nextSave;

        public bool Pushing => _pushing;
        private MovingCart State => _w.Game.Cart;

        public static RollCart Build(CityContext c, HomeWorld w, Elevator lift, Vector3 home, float yaw)
        {
            var go = new GameObject("Harborview moving cart");
            go.transform.SetParent(c.Dynamic, false);
            go.transform.SetPositionAndRotation(home, Quaternion.Euler(0f, yaw, 0f));
            var cart = go.AddComponent<RollCart>();
            cart._w = w;
            cart._lift = lift;
            cart._home = home;
            cart._homeYaw = yaw;
            cart._player = c.Player.GetComponent<FirstPersonController>();
            cart._body = c.Player.GetComponent<CharacterController>();
            cart._interactor = c.Player.GetComponentInChildren<PlayerInteractor>() ?? FindAnyObjectByType<PlayerInteractor>();
            cart.Dress(c);
            cart.Solids(c);
            if (lift != null) lift.Arrived += cart.LiftArrived;
            w.Cart = cart;
            // Left somewhere in the save: it's still there, with what was on it.
            if (w.Game.Cart.Out)
            {
                var (x, y, z, ry) = w.Game.Cart.Pose;
                go.transform.SetPositionAndRotation(new Vector3((float)x, (float)y, (float)z), Quaternion.Euler(0f, (float)ry, 0f));
            }
            return cart;
        }

        // ------------------------------------------------------------------ the model

        /// <summary>The user's roll-cage model, painted: powder-coated frame, zinc rollers, rubber casters, steel fittings.</summary>
        private void Dress(CityContext c)
        {
            Material frame = c.P.Lit(new Color(0.13f, 0.27f, 0.5f), 0.45f);
            Material zinc = c.P.Lit(new Color(0.78f, 0.79f, 0.8f), 0.7f);
            Material rubber = c.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.2f);
            Material steel = c.P.Lit(new Color(0.5f, 0.51f, 0.53f), 0.6f);
            GameObject source = Resources.Load<GameObject>("Models/RollCart");
            if (source != null)
            {
                GameObject model = Instantiate(source, transform, false);
                model.name = "Model";
                foreach (Collider col in model.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
                {
                    Outward(mf);
                    string part = mf.name;
                    mf.GetComponent<MeshRenderer>().sharedMaterial = part.Contains("Frame") ? frame : part.Contains("Roller") ? zinc : part.Contains("Caster") ? rubber : steel;
                }
            }
            else
            {
                // No model: a plain cage of the same size, so the cart still works.
                Kit k = c.Kit;
                float mid = (FrameStart + FrameEnd) / 2f, run = FrameEnd - FrameStart;
                k.Box(transform, "Deck", new Vector3(0f, Deck - 0.03f, mid), new Vector3(HalfWidth * 2f, 0.06f, run), zinc, collider: false);
                foreach (float x in new[] { -HalfWidth + 0.02f, HalfWidth - 0.02f })
                {
                    k.Box(transform, "Top rail", new Vector3(x, Top - 0.02f, mid), new Vector3(0.04f, 0.04f, run), frame, collider: false);
                    foreach (float z in new[] { FrameStart + 0.02f, mid, FrameEnd - 0.02f })
                        k.Box(transform, "Post", new Vector3(x, (0.12f + Top) / 2f, z), new Vector3(0.04f, Top - 0.12f, 0.04f), frame, collider: false);
                    foreach (float z in new[] { FrameStart + 0.06f, FrameEnd - 0.06f })
                        k.Box(transform, "Caster", new Vector3(x, 0.06f, z), new Vector3(0.05f, 0.12f, 0.1f), rubber, collider: false);
                }
                k.Box(transform, "Tow bar", new Vector3(0f, 0.14f, (FrameEnd + Length) / 2f), new Vector3(0.05f, 0.03f, Length - FrameEnd), steel, collider: false);
            }
        }

        /// <summary>
        /// Makes a part's faces point out. The OBJ's handedness decides which way its triangles wind once Unity has
        /// imported it; a closed mesh with outward faces has a positive signed volume, so a negative one is turned round.
        /// </summary>
        private static void Outward(MeshFilter mf)
        {
            Mesh m = mf.sharedMesh;
            if (m == null || !m.isReadable) return;
            Vector3[] v = m.vertices;
            int[] t = m.triangles;
            double volume = 0;
            for (int i = 0; i < t.Length; i += 3) volume += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            if (volume >= 0) return;
            Mesh turned = Instantiate(m);
            for (int s = 0; s < turned.subMeshCount; s++)
            {
                int[] tri = turned.GetTriangles(s);
                for (int i = 0; i < tri.Length; i += 3) (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);
                turned.SetTriangles(tri, s);
            }
            Vector3[] n = turned.normals;
            for (int i = 0; i < n.Length; i++) n[i] = -n[i];
            turned.normals = n;
            mf.sharedMesh = turned;
        }

        /// <summary>
        /// The deck (a load bed), low rails round it that keep people off (on Ignore Raycast, so you aim over them at
        /// the cargo) and the push end you take hold of.
        /// </summary>
        private void Solids(CityContext c)
        {
            Kit k = c.Kit;
            float mid = (FrameStart + FrameEnd) / 2f, run = FrameEnd - FrameStart;
            GameObject deck = Collider("Deck", new Vector3(0f, (0.12f + Deck) / 2f, mid), new Vector3(HalfWidth * 2f - 0.03f, Deck - 0.12f, run - 0.05f), 0);
            var bed = deck.AddComponent<CargoBed>();
            bed.Configure(BedId, Capacity, transform);
            _w.AddBed(bed);
            float railH = RailTop - 0.12f, railY = (0.12f + RailTop) / 2f;
            foreach (float x in new[] { -HalfWidth, HalfWidth })
                Collider("Rail", new Vector3(x, railY, mid), new Vector3(0.03f, railH, run), 2);
            Collider("Rail", new Vector3(0f, railY, FrameEnd), new Vector3(HalfWidth * 2f, railH, 0.03f), 2);
            GameObject handle = Collider("Push end", new Vector3(0f, (0.12f + Top) / 2f, FrameStart / 2f + 0.03f), new Vector3(HalfWidth * 2f + 0.02f, Top - 0.12f, FrameStart + 0.06f), 0);
            handle.AddComponent<CartHandle>().Configure(this);

            // A plaque on the wall beside its spot (built at the spot, before a saved pose moves the cart away).
            Vector3 wall = transform.TransformPoint(new Vector3(HalfWidth + 0.5f, 1.55f, Length / 2f));
            Quaternion face = transform.rotation * Quaternion.Euler(0f, 90f, 0f); // read from the lobby side
            Transform plaque = Kit.Group(c.Dynamic, "Moving cart plaque");
            plaque.SetPositionAndRotation(wall, face);
            k.Box(plaque, "Plate", new Vector3(0f, 0f, 0.01f), new Vector3(0.7f, 0.32f, 0.015f), c.P.Lit(new Color(0.75f, 0.6f, 0.35f), 0.75f), collider: false);
            k.Text(plaque, "MOVING CART", new Vector3(0f, 0.06f, -0.001f), 0f, 0.06f, new Color(0.12f, 0.1f, 0.08f));
            k.Text(plaque, "$5 A USE · SEND IT BACK WITH [F]", new Vector3(0f, -0.07f, -0.001f), 0f, 0.028f, new Color(0.2f, 0.17f, 0.12f));
        }

        private GameObject Collider(string name, Vector3 centre, Vector3 size, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(transform, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            _solids.Add(box);
            return go;
        }

        // ------------------------------------------------------------------ taking hold

        internal string Prompt => _pushing ? "Let go of the cart" : State.Out ? "Push the moving cart" : $"Push the moving cart · {HomeWorld.Dollars(MovingCart.Fee)} a use";

        internal string Details
        {
            get
            {
                int n = Cargo;
                string load = n == 0 ? "Empty" : $"{n} thing{(n == 1 ? "" : "s")} on it";
                return State.Out ? load + "\n[F] send it back to the lobby" : load + ". Set boxes on the rollers; [F] sends it back down when you're done.";
            }
        }

        internal bool CanGrab => _pushing || (!_w.Hands.Holding && _player.ControlEnabled && !_player.Suspended);

        internal void Toggle()
        {
            if (_pushing) { LetGo(); return; }
            if (!State.Out)
            {
                string error = _w.Game.Economy.Spend(MovingCart.Fee, "Harborview moving cart", _w.Game.Clock.Now);
                if (error != null) { _w.Say(error); return; }
                State.TakeOut();
                _w.Say($"Moving cart: {HomeWorld.Dollars(MovingCart.Fee)}. [F] twice sends it back down when you're done.");
            }
            _pushing = true;
            _player.HandsFull = true;
            foreach (Collider col in _solids) Physics.IgnoreCollision(_body, col, true);
            _lastPlayer = _player.transform.position;
            _lastTurn = _player.transform.rotation;
        }

        private void LetGo()
        {
            _pushing = false;
            _player.HandsFull = false;
            foreach (Collider col in _solids) Physics.IgnoreCollision(_body, col, false);
            _w.City.Hud?.SetStatus(null);
            SavePose();
        }

        private int Cargo
        {
            get
            {
                int n = 0;
                foreach (OwnedItem i in _w.Belongings.Items)
                    if (i.State == ItemState.Loaded && i.Vehicle == BedId) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------ each frame

        private void Update()
        {
            if (_w == null) return;
            Keyboard k = Keyboard.current;
            bool aimed = _interactor != null && _interactor.Current is CartHandle h && h.Cart == this;
            if (State.Out && (_pushing || aimed) && k != null && k.fKey.wasPressedThisFrame && _player.ControlEnabled)
            {
                if (Time.time > _confirmUntil)
                {
                    int n = Cargo;
                    _confirmUntil = Time.time + ConfirmSeconds;
                    _w.Say(n == 0 ? "[F] again to send the cart back down to the lobby." : $"[F] again to send the cart back to the lobby, with the {n} thing{(n == 1 ? "" : "s")} still on it.");
                }
                else SendBack();
                return;
            }
            if (!_pushing) return;
            // [E] with nothing else in view lets go too (the push end is often below the view while you walk).
            if (_interactor != null && _interactor.Current == null && _player.Input.Interact.WasPressedThisFrame()) { LetGo(); return; }
            _w.City.Hud?.SetStatus("Pushing the moving cart   [E] let go   [F] send it back to the lobby");
            if (Time.time >= _nextSave) SavePose();
        }

        /// <summary>
        /// After the player has moved: the cart rolls to just in front of them, turned as they face. If it would run
        /// into something (or off a step), the player goes back to where they were this frame instead: the cart is
        /// what stops you. Something it's already touching doesn't hold it, so it can always be pulled clear.
        /// </summary>
        private void LateUpdate()
        {
            if (!_pushing) return;
            if (!_player.ControlEnabled || _player.Suspended) { LetGo(); return; }
            Physics.SyncTransforms();
            Quaternion turn = Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f);
            Vector3 feet = Feet();
            Vector3 at = feet + turn * Vector3.forward * (_body.radius + 0.12f);
            bool ok = Floor(at, feet.y, out float y);
            at.y = y;
            if (ok && (!Blocked(at, turn) || Blocked(transform.position, transform.rotation)))
            {
                transform.SetPositionAndRotation(at, turn);
                _lastPlayer = _player.transform.position;
                _lastTurn = _player.transform.rotation;
                return;
            }
            _body.enabled = false;
            _player.transform.SetPositionAndRotation(_lastPlayer, _lastTurn);
            _body.enabled = true;
        }

        private Vector3 Feet()
        {
            Vector3 p = _player.transform.position;
            return new Vector3(p.x, p.y + _body.center.y - _body.height / 2f, p.z);
        }

        /// <summary>The floor under the cart's middle, which must be level with your feet (no stairs, no ledges).</summary>
        private bool Floor(Vector3 at, float feet, out float y)
        {
            y = feet;
            Vector3 middle = at + Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f) * Vector3.forward * (Length / 2f);
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(middle.x, feet + 0.6f, middle.z), Vector3.down, 1.4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
                if (!Ignorable(h.collider) && h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y;
                }
            return best < float.MaxValue && Mathf.Abs(y - feet) <= MaxStep;
        }

        /// <summary>Would the cage, 10 cm off the floor up to its rails, overlap anything solid at this pose?</summary>
        private bool Blocked(Vector3 at, Quaternion turn)
        {
            Vector3 centre = at + turn * new Vector3(0f, (0.1f + Top) / 2f, FrameStart + (Length - FrameStart) / 2f);
            var half = new Vector3(HalfWidth - 0.02f, (Top - 0.1f) / 2f, (Length - FrameStart) / 2f - 0.02f);
            foreach (Collider c in Physics.OverlapBox(centre, half, turn, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignorable(c)) return true;
            return false;
        }

        /// <summary>
        /// What the cart passes: itself and its load, people (they step aside), and doors that are open or opening
        /// (automatic ones open for you as you come up behind it; the lift's while they're open).
        /// </summary>
        private bool Ignorable(Collider c)
        {
            if (c.transform.IsChildOf(transform) || c is CharacterController || c.gameObject.layer == CityLayers.Pedestrian) return true;
            if (c.GetComponentInParent<FundPerson>() != null) return true;
            Door door = c.GetComponentInParent<Door>();
            if (door != null) return door.IsOpen || door.WantsOpen;
            Elevator lift = c.GetComponentInParent<Elevator>();
            return lift != null && c.name.StartsWith("Door") && lift.Logic.DoorOpen > 0.5f;
        }

        // ------------------------------------------------------------------ the lift and going back

        /// <summary>Standing in the car when it reaches another stop: it's in that stop's car now (as the player is).</summary>
        private void LiftArrived(int from, int to)
        {
            if (!_lift.InCar(from, transform.TransformPoint(new Vector3(0f, 0.5f, Length / 2f)))) return;
            // Pushing from outside the car (the doors held open by the cart only): it stays with you.
            if (_pushing && !_lift.InCar(from, _lastPlayer)) return;
            Vector3 rise = _lift.transform.up * (_lift.StopY(to) - _lift.StopY(from));
            transform.position += rise;
            if (_pushing) _lastPlayer += rise;
            SavePose();
        }

        private void SendBack()
        {
            if (_pushing) LetGo();
            transform.SetPositionAndRotation(_home, Quaternion.Euler(0f, _homeYaw, 0f));
            State.Return();
            _confirmUntil = 0f;
            _w.Say("The cart's gone back down to the lobby.");
        }

        private void SavePose()
        {
            _nextSave = Time.time + 1f;
            if (!State.Out) return;
            Vector3 p = transform.position;
            State.SetPose(p.x, p.y, p.z, transform.eulerAngles.y);
        }

        private void OnDestroy()
        {
            if (_lift != null) _lift.Arrived -= LiftArrived;
        }
    }

    /// <summary>The cart's push end: [E] takes hold (or lets go).</summary>
    public sealed class CartHandle : Interactable
    {
        public RollCart Cart { get; private set; }

        public void Configure(RollCart cart) => Cart = cart;

        public override string Prompt => Cart.Prompt;
        public override string Details => Cart.Details;
        public override bool CanInteract => base.CanInteract && Cart.CanGrab;
        public override void Interact() => Cart.Toggle();
    }
}
