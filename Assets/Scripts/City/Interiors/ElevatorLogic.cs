using System;
using System.Collections.Generic;

namespace OpeningBell.City
{
    public enum ElevatorState
    {
        Idle,       // doors closed, waiting
        Opening,
        Open,
        Closing,
        Moving,
    }

    /// <summary>
    /// The elevator's brain, without geometry: calls, floor selection, door timing and travel time. The car
    /// "moves" as a controlled transition (spec §8): each floor has its own car interior and the player is moved
    /// between them on arrival.
    /// </summary>
    public sealed class ElevatorLogic
    {
        public float DoorSeconds = 1.1f;
        public float HoldSeconds = 4.5f;
        public float TravelBase = 2.5f;
        public float TravelPerFloor = 2.2f;

        private readonly HashSet<int> _requests = new HashSet<int>();
        private float _timer;

        public int Floors { get; }
        public int CarFloor { get; private set; }
        public int TargetFloor { get; private set; }
        public ElevatorState State { get; private set; } = ElevatorState.Idle;
        /// <summary>0 = closed, 1 = fully open (for the door panels).</summary>
        public float DoorOpen { get; private set; }

        /// <summary>(from, to): the car reached a floor; move anyone inside it.</summary>
        public event Action<int, int> Arrived;
        public event Action<ElevatorState> StateChanged;

        public ElevatorLogic(int floors, int startFloor = 0)
        {
            Floors = floors;
            CarFloor = TargetFloor = startFloor;
        }

        public bool IsRequested(int floor) => _requests.Contains(floor);

        /// <summary>Hall call or car button: same thing for a single car.</summary>
        public void Request(int floor)
        {
            if (floor < 0 || floor >= Floors) return;
            if (floor == CarFloor && State != ElevatorState.Moving)
            {
                if (State == ElevatorState.Open) _timer = HoldSeconds;
                else if (State != ElevatorState.Opening) SetState(ElevatorState.Opening);
                return;
            }
            _requests.Add(floor);
        }

        /// <param name="doorwayBlocked">Someone standing in the doors keeps them open.</param>
        public void Update(float dt, bool doorwayBlocked)
        {
            switch (State)
            {
                case ElevatorState.Idle:
                    if (_requests.Count > 0) Depart();
                    break;
                case ElevatorState.Opening:
                    DoorOpen = Math.Min(1f, DoorOpen + dt / DoorSeconds);
                    if (DoorOpen >= 1f)
                    {
                        _timer = HoldSeconds;
                        SetState(ElevatorState.Open);
                    }
                    break;
                case ElevatorState.Open:
                    _timer -= doorwayBlocked ? 0f : dt;
                    if (_timer <= 0f) SetState(ElevatorState.Closing);
                    break;
                case ElevatorState.Closing:
                    if (doorwayBlocked)
                    {
                        SetState(ElevatorState.Opening);
                        break;
                    }
                    DoorOpen = Math.Max(0f, DoorOpen - dt / DoorSeconds);
                    if (DoorOpen <= 0f) SetState(ElevatorState.Idle);
                    break;
                case ElevatorState.Moving:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        int from = CarFloor;
                        CarFloor = TargetFloor;
                        _requests.Remove(CarFloor);
                        Arrived?.Invoke(from, CarFloor);
                        SetState(ElevatorState.Opening);
                    }
                    break;
            }
        }

        public float TravelSeconds(int from, int to) => TravelBase + TravelPerFloor * Math.Abs(to - from);

        private void Depart()
        {
            // Nearest request first (two floors today; keeps the rule simple for more).
            int best = -1;
            foreach (int f in _requests)
                if (best < 0 || Math.Abs(f - CarFloor) < Math.Abs(best - CarFloor)) best = f;
            TargetFloor = best;
            _timer = TravelSeconds(CarFloor, best);
            SetState(ElevatorState.Moving);
        }

        private void SetState(ElevatorState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
