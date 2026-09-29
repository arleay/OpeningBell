using System;
using System.Collections.Generic;

namespace OpeningBell.Casino
{
    public enum RewardTier { Guest, Silver, Gold, Platinum, Diamond }

    /// <summary>
    /// Meridian Rewards (CASINO_SPEC §41–42). Tier credits count everything ever earned and never go down; points are
    /// the spendable balance (100 points = $1 off food, drinks or a room). Earning: 1 point per $10 bet at the tables,
    /// per $5 on slots, 5 per poker session, 1 per $1 spent in the resort. Perks are comfort and access; nothing
    /// changes the odds.
    /// </summary>
    public sealed class Rewards
    {
        public static readonly int[] Thresholds = { 0, 500, 2_500, 10_000, 40_000 };
        public const int PointsPerDollar = 100;

        private decimal _partial;

        public int Points { get; private set; }
        public int Lifetime { get; private set; }
        /// <summary>Double-points days (the anniversary) set this for the day.</summary>
        public int Multiplier { get; set; } = 1;

        public RewardTier Tier
        {
            get
            {
                for (int t = Thresholds.Length - 1; t > 0; t--)
                    if (Lifetime >= Thresholds[t]) return (RewardTier)t;
                return RewardTier.Guest;
            }
        }

        /// <summary>Tier credits still needed for the next tier (0 at Diamond).</summary>
        public int ToNextTier => Tier == RewardTier.Diamond ? 0 : Thresholds[(int)Tier + 1] - Lifetime;

        public void EarnFromPlay(CasinoGame game, decimal wagered)
        {
            if (game == CasinoGame.Poker)
            {
                Add(5m);
                return;
            }
            Add(wagered / (game == CasinoGame.Slots ? 5m : 10m));
        }

        public void EarnFromSpend(decimal amount) => Add(Math.Max(0m, amount));

        private void Add(decimal points)
        {
            _partial += points * Multiplier;
            int whole = (int)Math.Floor(_partial);
            if (whole <= 0) return;
            _partial -= whole;
            Points += whole;
            Lifetime += whole;
        }

        /// <summary>Spends points for a dollar amount off; returns the dollars covered (whole points only).</summary>
        public decimal Redeem(decimal dollars)
        {
            int need = (int)Math.Ceiling(Math.Max(0m, dollars) * PointsPerDollar);
            int used = Math.Min(need, Points);
            Points -= used;
            return Math.Min(dollars, (decimal)used / PointsPerDollar);
        }

        public decimal PointsValue => (decimal)Points / PointsPerDollar;

        // Perks.
        public bool FreeValet => Tier >= RewardTier.Silver;
        public bool VipAccess => Tier >= RewardTier.Gold;
        public bool FreeSoftDrinks => Tier >= RewardTier.Gold;
        public bool FreeVipBar => Tier >= RewardTier.Platinum;
        public decimal HotelDiscount => Tier switch
        {
            RewardTier.Silver => 0.05m, RewardTier.Gold => 0.10m, RewardTier.Platinum => 0.15m, RewardTier.Diamond => 0.25m, _ => 0m,
        };

        public static string PerksOf(RewardTier t) => t switch
        {
            RewardTier.Silver => "free valet · 5% off rooms",
            RewardTier.Gold => "VIP salon · free soft drinks · 10% off rooms",
            RewardTier.Platinum => "free VIP bar · 15% off rooms",
            RewardTier.Diamond => "25% off rooms · penthouse priority",
            _ => "points on everything",
        };

        public void Restore(int points, int lifetime, string partial)
        {
            Points = Math.Max(0, points);
            Lifetime = Math.Max(Points, lifetime);
            _partial = decimal.TryParse(partial, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal p) ? p : 0m;
        }

        public string PartialText => _partial.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public enum RoomClass { Standard, Suite, LuxurySuite, Penthouse }

    public sealed class HotelStay
    {
        public RoomClass Class;
        public string Room;
        public DateTime CheckIn, CheckOut;
        public decimal Paid;
        public int Nights;
    }

    /// <summary>
    /// The Meridian Hotel (CASINO_SPEC §48–51, §90): four room classes, nightly rates with a Friday/Saturday premium and
    /// the rewards discount, check-out at 11 AM. One stay at a time; only the rooms you can enter exist.
    /// </summary>
    public sealed class Hotel
    {
        public const int CheckOutHour = 11;
        public const decimal WeekendPremium = 0.20m;
        public const int MaxNights = 14;

        public HotelStay Stay { get; private set; }

        public static decimal Rate(RoomClass c) => c switch
        {
            RoomClass.Standard => 189m, RoomClass.Suite => 420m, RoomClass.LuxurySuite => 850m, _ => 4_500m,
        };

        public static string RoomOf(RoomClass c) => c switch
        {
            RoomClass.Standard => "501", RoomClass.Suite => "502", RoomClass.LuxurySuite => "503", _ => "PH",
        };

        public static string NameOf(RoomClass c) => c switch
        {
            RoomClass.Standard => "Standard King", RoomClass.Suite => "Harbor Suite", RoomClass.LuxurySuite => "Meridian Luxury Suite", _ => "The Penthouse",
        };

        /// <summary>The hotel night you're booking into: before 6 AM it's still last night.</summary>
        public static DateTime NightOf(DateTime now) => (now.Hour < 6 ? now.AddDays(-1) : now).Date;

        public static decimal Quote(RoomClass c, DateTime now, int nights, decimal discount)
        {
            decimal total = 0m;
            DateTime night = NightOf(now);
            for (int i = 0; i < nights; i++, night = night.AddDays(1))
            {
                bool weekend = night.DayOfWeek == DayOfWeek.Friday || night.DayOfWeek == DayOfWeek.Saturday;
                total += Rate(c) * (weekend ? 1m + WeekendPremium : 1m);
            }
            return Math.Round(total * (1m - discount), 2);
        }

        public bool Active(DateTime now) => Stay != null && now < Stay.CheckOut;

        public bool HasKey(string room, DateTime now) => Active(now) && Stay.Room == room;

        public string Book(RoomClass c, int nights, DateTime now, decimal discount, Func<decimal, string, string> charge)
        {
            if (Active(now)) return $"You already have room {Stay.Room} until {Stay.CheckOut:ddd h tt}.";
            if (nights < 1 || nights > MaxNights) return $"Stays are 1–{MaxNights} nights.";
            decimal price = Quote(c, now, nights, discount);
            string error = charge(price, $"{NameOf(c)} · {nights} night{(nights > 1 ? "s" : "")}");
            if (error != null) return error;
            DateTime night = NightOf(now);
            Stay = new HotelStay
            {
                Class = c, Room = RoomOf(c), CheckIn = now, Nights = nights, Paid = price,
                CheckOut = night.AddDays(nights).AddHours(CheckOutHour),
            };
            return null;
        }

        public string Extend(int nights, DateTime now, decimal discount, Func<decimal, string, string> charge)
        {
            if (!Active(now)) return "You don't have a room.";
            if (nights < 1 || Stay.Nights + nights > MaxNights) return $"Stays are up to {MaxNights} nights.";
            decimal price = Quote(Stay.Class, Stay.CheckOut.Date.AddHours(12), nights, discount); // from the check-out day's night
            string error = charge(price, $"{NameOf(Stay.Class)} · {nights} more night{(nights > 1 ? "s" : "")}");
            if (error != null) return error;
            Stay.Nights += nights;
            Stay.Paid += price;
            Stay.CheckOut = Stay.CheckOut.AddDays(nights);
            return null;
        }

        /// <summary>Checks out now (nights not used aren't refunded).</summary>
        public void CheckOut() => Stay = null;

        public void Restore(HotelStay stay) => Stay = stay != null && !string.IsNullOrEmpty(stay.Room) ? stay : null;
    }

    public enum Sobriety { Sober, Tipsy, Drunk, Wasted }

    /// <summary>
    /// Alcohol (CASINO_SPEC §45–46), in standard drinks. The body clears about one an hour of game time. Over two
    /// drinks you shouldn't drive (the car won't start for you); at five the bar stops serving. Water, food and a
    /// shower help a little.
    /// </summary>
    public sealed class Intoxication
    {
        public const double ClearPerHour = 1.0, DriveLimit = 2.0, ServiceLimit = 5.0;

        public double Units { get; private set; }
        public DateTime Updated { get; private set; }

        public void Update(DateTime now)
        {
            if (Updated == default || now < Updated)
            {
                Updated = now;
                return;
            }
            Units = Math.Max(0, Units - (now - Updated).TotalHours * ClearPerHour);
            Updated = now;
        }

        public void Drink(double units, DateTime now)
        {
            Update(now);
            Units = Math.Min(10, Units + units);
        }

        public void Recover(double units, DateTime now)
        {
            Update(now);
            Units = Math.Max(0, Units - units);
        }

        public Sobriety Level => Units < 0.5 ? Sobriety.Sober : Units < 2.5 ? Sobriety.Tipsy : Units < 5 ? Sobriety.Drunk : Sobriety.Wasted;
        public bool CanDrive => Units < DriveLimit;
        public bool Refused => Units >= ServiceLimit;
        /// <summary>0 sober … 1 wasted: how strongly the view sways and blurs.</summary>
        public float Impairment => (float)Math.Max(0, Math.Min(1, (Units - 0.5) / 5.0));

        public void Restore(double units, long updatedTicks)
        {
            Units = Math.Max(0, Math.Min(10, units));
            Updated = updatedTicks > 0 ? new DateTime(updatedTicks) : default;
        }
    }

    /// <summary>
    /// An optional nightly limit (CASINO_SPEC §61): losses since it was set are tracked; you're warned at 80% and at
    /// the limit. It only stops you if you ask it to (hard limit).
    /// </summary>
    public sealed class GamblingBudget
    {
        public decimal Limit { get; private set; }
        public bool Hard { get; private set; }
        public DateTime Night { get; private set; }
        public decimal NetAtStart { get; private set; }
        public bool Warned80 { get; set; }
        public bool WarnedLimit { get; set; }

        public bool Set => Limit > 0m;

        public void Start(decimal limit, bool hard, DateTime now, decimal net)
        {
            Limit = Math.Max(0m, Math.Floor(limit));
            Hard = hard;
            Night = Hotel.NightOf(now);
            NetAtStart = net;
            Warned80 = WarnedLimit = false;
        }

        public void Clear() => Limit = 0m;

        /// <summary>The budget lasts the night it was set (to 6 AM).</summary>
        public bool ActiveOn(DateTime now) => Set && Hotel.NightOf(now) == Night;

        public decimal Lost(decimal net) => Math.Max(0m, NetAtStart - net);

        public void Restore(decimal limit, bool hard, DateTime night, decimal net)
        {
            Limit = limit;
            Hard = hard;
            Night = night;
            NetAtStart = net;
        }
    }

    public enum SpendCategory { Food, Drinks, Hotel, Entertainment, Valet }

    [Serializable]
    public struct CasinoSpend
    {
        public DateTime Time;
        public SpendCategory Category;
        public decimal Amount, Comped;
        public string Memo;
    }

    /// <summary>What's on around the resort by day (CASINO_SPEC §62–65): quiet by design, one thing a day at most.</summary>
    public static class CasinoEvents
    {
        public static string Today(DateTime now)
        {
            if (now.Day == 15) return "Anniversary: double rewards points today";
            return now.DayOfWeek switch
            {
                DayOfWeek.Friday => "VIP Night: the salon's open to Silver members tonight",
                DayOfWeek.Saturday => "Tournament Night: Sit & Go prize pools guaranteed $1,000",
                DayOfWeek.Sunday => "Luxury auto display at the entrance",
                _ => null,
            };
        }

        public static bool DoublePoints(DateTime now) => now.Day == 15;
        public static bool VipNight(DateTime now) => now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 18;
        public static bool TournamentNight(DateTime now) => now.DayOfWeek == DayOfWeek.Saturday;
        public static bool AutoDisplay(DateTime now) => now.DayOfWeek == DayOfWeek.Friday || now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday;

        /// <summary>How busy the floor is, 0–1: quiet mornings, busy evenings, busier Friday and Saturday (§63–64).</summary>
        public static float Crowd(DateTime now)
        {
            float h = now.Hour + now.Minute / 60f;
            float c = h < 6 ? 0.35f : h < 11 ? 0.15f : h < 17 ? 0.35f : h < 20 ? 0.7f : 0.9f;
            if (now.DayOfWeek == DayOfWeek.Friday || now.DayOfWeek == DayOfWeek.Saturday) c = Math.Min(1f, c + 0.2f);
            return c;
        }
    }
}
