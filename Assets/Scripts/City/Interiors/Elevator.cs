using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// A working elevator (spec §8): call buttons, sliding doors, floor buttons, travel delay, floor indicators and
    /// an arrival chime. Every floor has its own identical car interior stacked in the shaft; arriving moves the
    /// player from one to the other, so no moving platform and nothing to fall through.
    /// Local space: origin on the ground-floor car's floor centre, doors on the -z side.
    /// </summary>
    public sealed class Elevator : MonoBehaviour
    {
        public sealed class FloorStop
        {
            public string Label;
            public float Y;
            public Transform DoorLeft, DoorRight;
            public TextMesh HallIndicator, CarIndicator;
            public Renderer HallLamp;
            /// <summary>The car panel's lamps at this stop, and the floor each one's request shows.</summary>
            public Renderer[] CarLamps = new Renderer[0];
            public int[] CarLampFloors = new int[0];
            internal Vector3 ClosedLeft, ClosedRight;
        }

        private ElevatorLogic _logic;
        private FloorStop[] _stops;
        private Transform _player;
        private Vector2 _carHalfSize;
        private float _doorWidth;
        private Material _lampOn, _lampOff;
        private AudioSource _sound, _hum;
        private AudioClip _chime;

        public ElevatorLogic Logic => _logic;
        public int CarFloor => _logic.CarFloor;
        public string LabelOf(int floor) => _stops[floor].Label;
        public int FloorCount => _stops.Length;

        public void Configure(FloorStop[] stops, Vector2 carHalfSize, float doorWidth, Transform player, Material lampOn, Material lampOff)
        {
            _stops = stops;
            _carHalfSize = carHalfSize;
            _doorWidth = doorWidth;
            _player = player;
            _lampOn = lampOn;
            _lampOff = lampOff;
            foreach (FloorStop s in stops)
            {
                s.ClosedLeft = s.DoorLeft.localPosition;
                s.ClosedRight = s.DoorRight.localPosition;
            }
            _logic = new ElevatorLogic(stops.Length);
            _logic.Arrived += OnArrived;
            _logic.StateChanged += state =>
            {
                if (state == ElevatorState.Moving) _hum.Play();
                else if (_hum.isPlaying) _hum.Stop();
            };

            _chime = ProceduralSounds.Chime();
            _sound = AddSource("Chime", 0.7f);
            _hum = AddSource("Hum", 0.18f);
            _hum.clip = ProceduralSounds.Noise("elevator-hum", 0.05f);
            _hum.loop = true;
            Refresh();
        }

        private AudioSource AddSource(string name, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.volume = volume;
            source.minDistance = 1f;
            source.maxDistance = 14f;
            source.rolloffMode = AudioRolloffMode.Linear;
            return source;
        }

        public void Request(int floor) => _logic.Request(floor);

        private void Update()
        {
            if (_logic == null) return;
            _logic.Update(Time.deltaTime, DoorwayBlocked());
            Refresh();
        }

        /// <summary>Player standing in the doorway at the car's floor keeps the doors open.</summary>
        private bool DoorwayBlocked()
        {
            Vector3 p = transform.InverseTransformPoint(_player.position);
            float y = _stops[_logic.CarFloor].Y;
            return Mathf.Abs(p.x) < _doorWidth / 2f && p.z > -_carHalfSize.y - 0.45f && p.z < -_carHalfSize.y + 0.35f &&
                   p.y > y - 0.5f && p.y < y + 2f;
        }

        private bool InsideCar(int floor, Vector3 local) =>
            Mathf.Abs(local.x) < _carHalfSize.x && local.z > -_carHalfSize.y && local.z < _carHalfSize.y &&
            local.y > _stops[floor].Y - 0.6f && local.y < _stops[floor].Y + 2.4f;

        private void OnArrived(int from, int to)
        {
            Vector3 local = transform.InverseTransformPoint(_player.position);
            if (InsideCar(from, local))
            {
                // Same spot in the other floor's car. A CharacterController must be off to be moved directly.
                var body = _player.GetComponent<CharacterController>();
                if (body != null) body.enabled = false;
                _player.position += transform.up * (_stops[to].Y - _stops[from].Y);
                if (body != null) body.enabled = true;
            }
            _sound.transform.localPosition = new Vector3(0f, _stops[to].Y + 2f, 0f);
            _sound.PlayOneShot(_chime);
        }

        private void Refresh()
        {
            float k = Mathf.SmoothStep(0f, 1f, _logic.DoorOpen);
            bool moving = _logic.State == ElevatorState.Moving;
            string at = _stops[_logic.CarFloor].Label;
            string shown = moving ? (_logic.TargetFloor > _logic.CarFloor ? "↑" : "↓") + " " + at : at;
            for (int i = 0; i < _stops.Length; i++)
            {
                FloorStop s = _stops[i];
                float open = i == _logic.CarFloor && !moving ? k : 0f;
                s.DoorLeft.localPosition = s.ClosedLeft + Vector3.left * (_doorWidth / 2f * 0.95f * open);
                s.DoorRight.localPosition = s.ClosedRight + Vector3.right * (_doorWidth / 2f * 0.95f * open);
                if (s.HallIndicator.text != shown) s.HallIndicator.text = shown;
                if (s.CarIndicator.text != shown) s.CarIndicator.text = shown;
                Material hall = _logic.IsRequested(i) ? _lampOn : _lampOff;
                if (s.HallLamp.sharedMaterial != hall) s.HallLamp.sharedMaterial = hall;
                for (int j = 0; j < s.CarLamps.Length; j++)
                {
                    int to = s.CarLampFloors[j];
                    Material car = _logic.IsRequested(to) || (moving && _logic.TargetFloor == to) ? _lampOn : _lampOff;
                    if (s.CarLamps[j] != null && s.CarLamps[j].sharedMaterial != car) s.CarLamps[j].sharedMaterial = car;
                }
            }
            _hum.transform.localPosition = new Vector3(0f, _stops[_logic.CarFloor].Y + 1.5f, 0f);
        }
    }

    /// <summary>Hall call button or a floor button inside the car.</summary>
    public sealed class ElevatorButton : Interactable
    {
        private Elevator _elevator;
        private int _floor;
        private bool _inCar;

        public void Configure(Elevator elevator, int floor, bool inCar)
        {
            _elevator = elevator;
            _floor = floor;
            _inCar = inCar;
        }

        public int Floor => _floor;

        /// <summary>Why this button does nothing for the player (e.g. key-card floors), or null.</summary>
        public System.Func<string> LockReason { get; set; }

        public override string Prompt
        {
            get
            {
                string label = _inCar ? "Floor " + _elevator.LabelOf(_floor) : "Call elevator";
                string reason = LockReason?.Invoke();
                return reason == null ? label : $"{label} · {reason}";
            }
        }

        public override void Interact()
        {
            if (LockReason?.Invoke() == null) _elevator.Request(_floor);
        }
    }
}
