using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Core;
using OpeningBell.Trading;

namespace OpeningBell.Vehicles
{
    /// <summary>
    /// Dealer rules (spec Phase 11): weekly rotating stock, trade-in offers and test-drive bills. Stock is derived
    /// from the world seed and the week, so it needs no save data: the same week always shows the same cars, and a
    /// used car bought from the lot stays gone because its listing id is recorded as sold in the <see cref="Fleet"/>.
    /// </summary>
    public static class Dealership
    {
        /// <summary>A dealer buys at this share of what a private buyer would pay (<see cref="OwnedVehicle.ResaleValue"/>).</summary>
        public const double TradeInShare = 0.85;
        /// <summary>A test car left anywhere but the lot is fetched by the dealer, for a fee.</summary>
        public const decimal RecoveryFee = 250m;
        /// <summary>Damage on a test drive is billed at this share of the car's price per unit of condition lost.</summary>
        public const double DamageBillShare = 0.3;

        private static readonly DateTime FirstMonday = new DateTime(2000, 1, 3);

        /// <summary>Stock turns over on Mondays.</summary>
        public static int Week(DateTime now) => (int)Math.Floor((now.Date - FirstMonday).TotalDays / 7.0);

        /// <summary>
        /// This week's showroom: <paramref name="slots"/> distinct models from the dealer's lineup (all of them when
        /// the lineup is no bigger). New cars are ordered from the factory, so buying one doesn't empty its spot.
        /// </summary>
        public static List<string> NewStock(IReadOnlyList<string> lineup, int slots, int week, SeededRandomService random, string dealer)
        {
            var pool = new List<string>(lineup);
            SeededRandom rng = random.CreateStream($"dealer/{dealer}/new/{week}");
            // Fisher–Yates, then take the first slots.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            if (pool.Count > slots) pool.RemoveRange(slots, pool.Count - slots);
            return pool;
        }

        /// <summary>
        /// This week's used cars: one per slot, a model from the lineup with an age, mileage and wear, priced from
        /// the new price by age and condition. Every flaw is written into the description (spec: readable warning
        /// signs, no hidden scams).
        /// </summary>
        public static List<UsedListing> UsedStock(VehicleCatalog catalog, IReadOnlyList<string> lineup, int slots, int week,
            SeededRandomService random, string dealer, string seller)
        {
            var stock = new List<UsedListing>();
            SeededRandom rng = random.CreateStream($"dealer/{dealer}/used/{week}");
            for (int slot = 0; slot < slots; slot++)
            {
                string modelId = lineup[rng.NextInt(lineup.Count)];
                if (!catalog.TryGetModel(modelId, out VehicleModel model)) continue;
                int years = 2 + rng.NextInt(11);                                   // 2–12 years old
                double km = years * 13_000 * (0.6 + 0.8 * rng.NextDouble());        // around 13,000 km a year
                double condition = Clamp(1.0 - years * 0.035 - rng.NextDouble() * 0.3, 0.3, 0.95);
                double tires = Clamp(0.2 + rng.NextDouble() * 0.75, 0.2, 0.95);
                double fuel = 0.1 + rng.NextDouble() * 0.4;
                // ~12%/year off new, then scaled by condition; ends in 95 like every used lot.
                double price = model.Price * Math.Pow(0.88, years) * (0.55 + 0.45 * condition);
                price = Math.Max(1_000, Math.Round(price / 100.0) * 100 - 5);
                stock.Add(new UsedListing
                {
                    Id = $"{dealer}-{week}-{slot}",
                    ModelId = modelId,
                    Price = price,
                    OdometerKm = Math.Round(km / 100.0) * 100,
                    Condition = Math.Round(condition, 2),
                    TireCondition = Math.Round(tires, 2),
                    FuelFraction = fuel,
                    Seller = seller,
                    Description = Warnings(years, km, condition, tires),
                });
            }
            return stock;
        }

        /// <summary>The window sticker: age plus every flaw a buyer should know about.</summary>
        public static string Warnings(int years, double km, double condition, double tires)
        {
            var notes = new List<string> { $"{years} years old" };
            if (km > 150_000) notes.Add("high mileage");
            if (condition < 0.5) notes.Add("engine runs rough, needs a tune-up soon");
            else if (condition < 0.7) notes.Add("worn: rattles over bumps");
            if (tires < 0.4) notes.Add("tyres near the wear bars");
            if (notes.Count == 1) notes.Add("clean, no known issues");
            string text = string.Join(" · ", notes);
            return char.ToUpper(text[0], CultureInfo.InvariantCulture) + text.Substring(1) + ".";
        }

        /// <summary>What the dealer pays for your car (cash, or off the next one).</summary>
        public static decimal TradeInOffer(OwnedVehicle v, DateTime now) => Money.RoundCents(v.ResaleValue(now) * (decimal)TradeInShare);

        /// <summary>Repair bill for damage done on a test drive (condition lost × a share of the price).</summary>
        public static decimal DamageBill(double conditionBefore, double conditionAfter, decimal carPrice)
        {
            double lost = Math.Max(0, conditionBefore - conditionAfter);
            return lost <= 0.005 ? 0m : Money.RoundCents(carPrice * (decimal)(lost * DamageBillShare));
        }

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    }
}
