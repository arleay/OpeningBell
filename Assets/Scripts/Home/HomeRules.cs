using System;
using System.Collections.Generic;
using OpeningBell.Trading;

namespace OpeningBell.Home
{
    /// <summary>What the placement ray found under an item.</summary>
    public enum Under { Floor, Ground, Wall, Ceiling, Item, Nothing }

    /// <summary>
    /// The support half of placement (the world checks overlaps, floating, walls and doorways): what each kind of item
    /// may stand on. Null means fine; otherwise the reason to show.
    /// </summary>
    public static class PlacementRules
    {
        /// <param name="onto">The item underneath when <paramref name="under"/> is <see cref="Under.Item"/>.</param>
        public static string Check(HomeItem item, Under under, HomeItem onto, bool indoors)
        {
            if (!indoors && !item.Outdoor) return "That stays indoors.";
            switch (item.Support)
            {
                case Support.Floor:
                    if (under == Under.Floor || under == Under.Ground) return null;
                    if (under == Under.Item && item.OnTops && onto != null && onto.Surface > 0f) return null;
                    if (under == Under.Item) return item.Seat ? "Seats go on the floor." : $"The {item.Name.ToLowerInvariant()} goes on the floor.";
                    return "Stand it on the floor.";
                case Support.Surface:
                    if (under == Under.Item && onto != null && (onto.Surface > 0f || (item.IsMonitor && onto.IsArm))) return null;
                    return "Needs a desk or a table.";
                case Support.Wall:
                    return under == Under.Wall ? null : "Hang it on a wall.";
                case Support.Ceiling:
                    return under == Under.Ceiling ? null : "It hangs from the ceiling.";
                case Support.DeskMount:
                    return under == Under.Item && onto != null && onto.IsDesk ? null : "Clamps to the back of a desk.";
            }
            return null;
        }
    }

    [Serializable]
    public sealed class RentalSaveData
    {
        public bool Active;
        public long Start, Due;
        public double ConditionAtStart = 1;
        public int Reminded;
        public decimal Deposit;
        /// <summary>Where the loaner truck was last parked (the trailer follows it).</summary>
        public double X, Y, Z, Yaw;
    }

    /// <summary>
    /// The furniture store's loaner pickup and trailer: a deposit up front, back within <see cref="Loan"/> of game time,
    /// reminders an hour, half an hour and ten minutes before, a grace period, then a late fee by the half hour,
    /// and a charge for damage. Cargo still aboard at the return is the renter's problem (the bay warns).
    /// </summary>
    public sealed class Rental
    {
        public const decimal DepositAmount = 150m;
        public const decimal LateFeePerHalfHour = 20m;
        /// <summary>What a totally wrecked loaner costs you.</summary>
        public const decimal WreckCharge = 2500m;
        public static readonly TimeSpan Loan = TimeSpan.FromHours(2);
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
        public static readonly int[] ReminderMinutes = { 60, 30, 10 };

        private RentalSaveData _s = new RentalSaveData();

        public bool Active => _s.Active;
        public DateTime Due => new DateTime(_s.Due);
        public decimal Deposit => _s.Deposit;
        public (double X, double Y, double Z, double Yaw) Pose => (_s.X, _s.Y, _s.Z, _s.Yaw);

        public void SetPose(double x, double y, double z, double yaw)
        {
            _s.X = x; _s.Y = y; _s.Z = z; _s.Yaw = yaw;
        }

        public void Start(DateTime now, double condition)
        {
            _s = new RentalSaveData
            {
                Active = true, Start = now.Ticks, Due = (now + Loan).Ticks, ConditionAtStart = condition, Deposit = DepositAmount,
            };
        }

        /// <summary>Reminders that just came due (minutes before the deadline), each once.</summary>
        public List<int> RemindersDue(DateTime now)
        {
            var due = new List<int>();
            if (!_s.Active) return due;
            for (int i = 0; i < ReminderMinutes.Length; i++)
            {
                if ((_s.Reminded & (1 << i)) != 0) continue;
                if (now >= Due - TimeSpan.FromMinutes(ReminderMinutes[i]))
                {
                    _s.Reminded |= 1 << i;
                    due.Add(ReminderMinutes[i]);
                }
            }
            return due;
        }

        public static decimal LateFee(DateTime due, DateTime returned)
        {
            TimeSpan late = returned - due - Grace;
            if (late <= TimeSpan.Zero) return 0m;
            return LateFeePerHalfHour * (decimal)Math.Ceiling(late.TotalMinutes / 30.0);
        }

        public static decimal DamageCharge(double conditionAtStart, double conditionNow) =>
            Money.RoundCents(WreckCharge * (decimal)Math.Max(0, conditionAtStart - conditionNow));

        /// <summary>
        /// Hands it back: the deposit less the late fee and damage. Returns (refund, charged beyond the deposit).
        /// </summary>
        public (decimal Refund, decimal Extra, decimal Late, decimal Damage) Return(DateTime now, double conditionNow)
        {
            decimal late = LateFee(Due, now), damage = DamageCharge(_s.ConditionAtStart, conditionNow);
            decimal owed = late + damage;
            decimal refund = Math.Max(0m, _s.Deposit - owed), extra = Math.Max(0m, owed - _s.Deposit);
            _s = new RentalSaveData();
            return (refund, extra, late, damage);
        }

        public RentalSaveData CaptureState() => new RentalSaveData
        {
            Active = _s.Active, Start = _s.Start, Due = _s.Due, ConditionAtStart = _s.ConditionAtStart, Reminded = _s.Reminded, Deposit = _s.Deposit,
            X = _s.X, Y = _s.Y, Z = _s.Z, Yaw = _s.Yaw,
        };

        public void RestoreState(RentalSaveData data) => _s = data ?? new RentalSaveData();
    }

    [Serializable]
    public sealed class CartSaveData
    {
        /// <summary>Taken from its spot in the lobby (and paid for); false while it's parked there.</summary>
        public bool Out;
        /// <summary>Where it was left (world), while out.</summary>
        public double X, Y, Z, Yaw;
    }

    /// <summary>
    /// Harborview's moving cart: a roll cage by the concierge desk that anyone moving in can push up in the lift.
    /// Each use costs <see cref="Fee"/>, paid when it leaves its spot; sending it back parks it there again.
    /// </summary>
    public sealed class MovingCart
    {
        public const decimal Fee = 5m;

        private CartSaveData _s = new CartSaveData();

        public bool Out => _s.Out;
        public (double X, double Y, double Z, double Yaw) Pose => (_s.X, _s.Y, _s.Z, _s.Yaw);

        public void TakeOut() => _s.Out = true;

        public void SetPose(double x, double y, double z, double yaw)
        {
            _s.X = x; _s.Y = y; _s.Z = z; _s.Yaw = yaw;
        }

        public void Return() => _s = new CartSaveData();

        public CartSaveData CaptureState() => new CartSaveData { Out = _s.Out, X = _s.X, Y = _s.Y, Z = _s.Z, Yaw = _s.Yaw };

        public void RestoreState(CartSaveData data) => _s = data ?? new CartSaveData();
    }

    [Serializable]
    public sealed class EstateSaveData
    {
        public List<string> Owned = new List<string>();
        public List<string> Unlocked = new List<string>();
        public List<string> LightsOn = new List<string>();
        public List<string> GaragesOpen = new List<string>();
    }

    /// <summary>
    /// Homes the player owns (as many as they like) and each one's state: doors locked or not, lights, garage door.
    /// Which homes exist and what they cost is the town's business; this keeps the ids.
    /// </summary>
    public sealed class Estate
    {
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlocked = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _lights = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _garages = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Owned => _owned;
        public int Version { get; private set; }

        public bool Owns(string id) => _owned.Contains(id);

        /// <summary>Takes ownership (money is the caller's job). New keys: unlocked, lights on.</summary>
        public bool Acquire(string id)
        {
            if (!_owned.Add(id)) return false;
            _unlocked.Add(id);
            _lights.Add(id);
            Version++;
            return true;
        }

        public void Release(string id)
        {
            _owned.Remove(id);
            _unlocked.Remove(id);
            _lights.Remove(id);
            _garages.Remove(id);
            Version++;
        }

        public bool Locked(string id) => !_unlocked.Contains(id);
        public void SetLocked(string id, bool locked) { if (locked) _unlocked.Remove(id); else _unlocked.Add(id); Version++; }
        public bool LightsOn(string id) => _lights.Contains(id);
        public void SetLights(string id, bool on) { if (on) _lights.Add(id); else _lights.Remove(id); Version++; }
        public bool GarageOpen(string id) => _garages.Contains(id);
        public void SetGarage(string id, bool open) { if (open) _garages.Add(id); else _garages.Remove(id); Version++; }

        public EstateSaveData CaptureState() => new EstateSaveData
        {
            Owned = new List<string>(_owned), Unlocked = new List<string>(_unlocked), LightsOn = new List<string>(_lights), GaragesOpen = new List<string>(_garages),
        };

        public void RestoreState(EstateSaveData data)
        {
            _owned.Clear(); _unlocked.Clear(); _lights.Clear(); _garages.Clear();
            if (data == null) return;
            _owned.UnionWith(data.Owned);
            _unlocked.UnionWith(data.Unlocked);
            _lights.UnionWith(data.LightsOn);
            _garages.UnionWith(data.GaragesOpen);
            Version++;
        }
    }

    /// <summary>Everything Part B saves, in one block of the save file.</summary>
    [Serializable]
    public sealed class HomeSaveData
    {
        public BelongingsSaveData Belongings = new BelongingsSaveData();
        public RentalSaveData Rental = new RentalSaveData();
        public CartSaveData Cart = new CartSaveData();
        public EstateSaveData Estate = new EstateSaveData();
    }
}
