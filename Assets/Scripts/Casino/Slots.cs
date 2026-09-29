using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Casino
{
    public enum SlotSymbol { Blank, Cherry, Bar, DoubleBar, TripleBar, Seven, Diamond }

    public enum Volatility { Low, Medium, High }

    /// <summary>One stop on a reel strip: the symbol there and how many virtual stops map to it (its weight).</summary>
    [Serializable]
    public struct ReelStop
    {
        public SlotSymbol Symbol;
        public int Weight;

        public ReelStop(SlotSymbol symbol, int weight)
        {
            Symbol = symbol;
            Weight = weight;
        }
    }

    /// <summary>
    /// A slot machine's maths (CASINO_SPEC §31–33): three identical weighted reel strips (the "virtual reel" real
    /// machines use: every physical stop maps to a number of random-number values), the paylines, the pay table,
    /// the bet range and, for the progressive, the share of each bet that feeds the casino-wide jackpot. Return,
    /// hit rate and volatility are computed exactly from these (<see cref="SlotMath"/>), not asserted.
    /// </summary>
    public sealed class SlotDefinition
    {
        public string Id, Name, Theme;
        public Volatility Volatility;
        public ReelStop[] Strip;
        /// <summary>Row offsets per reel for each payline: (0,0,0) is the centre line, (−1,0,1) a diagonal.</summary>
        public int[][] Lines;
        /// <summary>Three of a kind pays, in line bets.</summary>
        public Dictionary<SlotSymbol, int> ThreeOfAKind;
        /// <summary>Any three bars (mixed) pay this.</summary>
        public int AnyBar;
        /// <summary>Pays for 0, 1, 2, 3 cherries anywhere on a line.</summary>
        public int[] Cherries;
        public decimal MinLineBet = 1m, MaxLineBet = 5m;
        /// <summary>Share of every bet added to the progressive pool (0 = fixed pays only).</summary>
        public decimal ProgressiveRate;
        /// <summary>The symbol whose three-of-a-kind on the centre line at max bet wins the progressive.</summary>
        public SlotSymbol TopSymbol = SlotSymbol.Seven;

        public int LineCount => Lines.Length;
        public decimal MinBet => MinLineBet * LineCount;
        public decimal MaxBet => MaxLineBet * LineCount;
        public bool Progressive => ProgressiveRate > 0m;

        /// <summary>What a line of three symbols pays, in line bets (the top award, not the progressive).</summary>
        public int Pay(SlotSymbol a, SlotSymbol b, SlotSymbol c)
        {
            if (a == b && b == c && ThreeOfAKind.TryGetValue(a, out int three)) return three;
            if (IsBar(a) && IsBar(b) && IsBar(c)) return AnyBar;
            int cherries = (a == SlotSymbol.Cherry ? 1 : 0) + (b == SlotSymbol.Cherry ? 1 : 0) + (c == SlotSymbol.Cherry ? 1 : 0);
            return Cherries[cherries];
        }

        private static bool IsBar(SlotSymbol s) => s == SlotSymbol.Bar || s == SlotSymbol.DoubleBar || s == SlotSymbol.TripleBar;

        public SlotSymbol At(int stop) => Strip[((stop % Strip.Length) + Strip.Length) % Strip.Length].Symbol;
    }

    /// <summary>The five machines on the floor. Weights tuned so returns land 92–96% (see <see cref="SlotMath"/>).</summary>
    public static class SlotMachines
    {
        private static readonly int[] Centre = { 0, 0, 0 };
        private static readonly int[][] OneLine = { Centre };
        private static readonly int[][] ThreeLines = { Centre, new[] { -1, -1, -1 }, new[] { 1, 1, 1 } };
        private static readonly int[][] FiveLines = { Centre, new[] { -1, -1, -1 }, new[] { 1, 1, 1 }, new[] { -1, 0, 1 }, new[] { 1, 0, -1 } };

        /// <summary>
        /// 22 stops: a blank between every symbol, as on a real reel. Weights: blank, cherry, bar, double bar,
        /// triple bar; the top symbol always weighs 1 (its two stops are the rarest outcome on each reel).
        /// </summary>
        private static ReelStop[] Strip(int blank, int cherry, int bar, int bar2, int bar3, SlotSymbol top = SlotSymbol.Seven)
        {
            SlotSymbol[] order =
            {
                SlotSymbol.Cherry, SlotSymbol.Bar, SlotSymbol.DoubleBar, SlotSymbol.TripleBar, top, SlotSymbol.Bar,
                SlotSymbol.Cherry, SlotSymbol.DoubleBar, SlotSymbol.Bar, SlotSymbol.TripleBar, top,
            };
            var strip = new List<ReelStop>();
            foreach (SlotSymbol s in order)
            {
                strip.Add(new ReelStop(SlotSymbol.Blank, blank));
                int w = s switch
                {
                    SlotSymbol.Cherry => cherry, SlotSymbol.Bar => bar, SlotSymbol.DoubleBar => bar2, SlotSymbol.TripleBar => bar3, _ => 1,
                };
                strip.Add(new ReelStop(s, w));
            }
            return strip.ToArray();
        }

        private static Dictionary<SlotSymbol, int> Threes(SlotSymbol top, int topPay, int bar3, int bar2, int bar) => new Dictionary<SlotSymbol, int>
        {
            [top] = topPay, [SlotSymbol.TripleBar] = bar3, [SlotSymbol.DoubleBar] = bar2, [SlotSymbol.Bar] = bar,
        };

        public static readonly SlotDefinition Meridian7s = new SlotDefinition
        {
            Id = "meridian-7s", Name = "Meridian 7s", Theme = "Classic red sevens", Volatility = Volatility.Medium,
            Strip = Strip(2, 1, 3, 4, 3), Lines = OneLine, ThreeOfAKind = Threes(SlotSymbol.Seven, 100, 40, 25, 10), AnyBar = 5,
            Cherries = new[] { 0, 2, 5, 10 }, MinLineBet = 1m, MaxLineBet = 5m,
        };

        public static readonly SlotDefinition LuckyHarbor = new SlotDefinition
        {
            Id = "lucky-harbor", Name = "Lucky Harbor", Theme = "Harbour lights, big rare hits", Volatility = Volatility.High,
            Strip = Strip(3, 4, 2, 4, 3), Lines = OneLine, ThreeOfAKind = Threes(SlotSymbol.Seven, 2_500, 200, 80, 30), AnyBar = 8,
            Cherries = new[] { 0, 0, 5, 25 }, MinLineBet = 1m, MaxLineBet = 10m,
        };

        public static readonly SlotDefinition TripleKell = new SlotDefinition
        {
            Id = "triple-kell", Name = "Triple Kell", Theme = "Three lines, steady small wins", Volatility = Volatility.Low,
            Strip = Strip(6, 3, 3, 1, 2), Lines = ThreeLines, ThreeOfAKind = Threes(SlotSymbol.Seven, 80, 30, 20, 10), AnyBar = 4,
            Cherries = new[] { 0, 1, 4, 10 }, MinLineBet = 1m, MaxLineBet = 3m,
        };

        public static readonly SlotDefinition GoldTide = new SlotDefinition
        {
            Id = "gold-tide", Name = "Gold Tide", Theme = "Five lines including the diagonals", Volatility = Volatility.Low,
            Strip = Strip(5, 4, 3, 4, 2), Lines = FiveLines, ThreeOfAKind = Threes(SlotSymbol.Seven, 200, 60, 40, 20), AnyBar = 6,
            Cherries = new[] { 0, 1, 3, 12 }, MinLineBet = 1m, MaxLineBet = 2m,
        };

        /// <summary>The progressive: three diamonds on the line at max bet win the casino-wide pool.</summary>
        public static readonly SlotDefinition DiamondDusk = new SlotDefinition
        {
            Id = "diamond-dusk", Name = "Diamond Dusk", Theme = "Progressive jackpot", Volatility = Volatility.Medium,
            Strip = Strip(3, 3, 3, 4, 3, SlotSymbol.Diamond), Lines = OneLine, ThreeOfAKind = Threes(SlotSymbol.Diamond, 250, 50, 25, 10), AnyBar = 5,
            Cherries = new[] { 0, 2, 5, 10 }, MinLineBet = 1m, MaxLineBet = 5m, ProgressiveRate = 0.015m, TopSymbol = SlotSymbol.Diamond,
        };

        public static readonly SlotDefinition[] All = { Meridian7s, TripleKell, GoldTide, LuckyHarbor, DiamondDusk };

        public static SlotDefinition Find(string id) => Array.Find(All, d => d.Id == id);
    }

    /// <summary>Exact figures for a machine, by enumerating every combination of reel stops with its weight.</summary>
    public static class SlotMath
    {
        public readonly struct Figures
        {
            /// <summary>Fixed-pay return to player (excludes the progressive pool).</summary>
            public readonly double Rtp;
            public readonly double HitFrequency;
            /// <summary>Standard deviation of one spin's return, in total bets (the volatility measure).</summary>
            public readonly double Deviation;
            /// <summary>Odds of the top combination on the centre line: 1 in this many spins.</summary>
            public readonly double TopOdds;

            public Figures(double rtp, double hit, double sd, double top)
            {
                Rtp = rtp;
                HitFrequency = hit;
                Deviation = sd;
                TopOdds = top;
            }
        }

        public static Figures Compute(SlotDefinition d)
        {
            int n = d.Strip.Length;
            double total = 0;
            foreach (ReelStop s in d.Strip) total += s.Weight;
            double all = total * total * total;
            double m1 = 0, m2 = 0, hit = 0, top = 0;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    for (int c = 0; c < n; c++)
                    {
                        double p = d.Strip[a].Weight * (double)d.Strip[b].Weight * d.Strip[c].Weight / all;
                        int pays = 0;
                        foreach (int[] line in d.Lines) pays += d.Pay(d.At(a + line[0]), d.At(b + line[1]), d.At(c + line[2]));
                        double r = pays / (double)d.LineCount;
                        m1 += p * r;
                        m2 += p * r * r;
                        if (pays > 0) hit += p;
                        if (d.At(a) == d.TopSymbol && d.At(b) == d.TopSymbol && d.At(c) == d.TopSymbol) top += p;
                    }
            return new Figures(m1, hit, Math.Sqrt(Math.Max(0, m2 - m1 * m1)), top > 0 ? 1 / top : 0);
        }
    }

    /// <summary>One spin's result: where each reel stopped, what each line paid, and the jackpot if hit.</summary>
    public sealed class SlotSpin
    {
        public int[] Stops = new int[3];
        public int[] LinePays;
        public decimal Bet, Won, Jackpot;

        /// <summary>The visible 3×3 window: [reel, row] with row 0 the top.</summary>
        public SlotSymbol Symbol(SlotDefinition d, int reel, int row) => d.At(Stops[reel] + row - 1);
    }

    /// <summary>
    /// A machine on the floor (CASINO_SPEC §30–33). A spin is one atomic step: the bet comes off the chips, the
    /// stops are drawn (weighted, from the casino's random stream), the lines are paid and the chips credited, all
    /// before the reels start turning on screen. Nothing about the player (wins, losses, wealth) is an input.
    /// </summary>
    public sealed class SlotMachine
    {
        private readonly CasinoAccount _account;
        private readonly SeededRandom _rng;
        private readonly Func<DateTime> _now;
        private readonly SlotJackpot _jackpot;
        private readonly int _totalWeight;

        public SlotDefinition Definition { get; }
        public SlotSpin Last { get; private set; }

        public SlotMachine(SlotDefinition definition, CasinoAccount account, SeededRandom rng, Func<DateTime> now, SlotJackpot jackpot = null)
        {
            Definition = definition;
            _account = account;
            _rng = rng;
            _now = now;
            _jackpot = jackpot;
            foreach (ReelStop s in definition.Strip) _totalWeight += s.Weight;
        }

        private int[] _forced;

        /// <summary>Tests only: the next spin stops at these strip positions.</summary>
        internal void ForceNext(params int[] stops) => _forced = stops;

        /// <summary>A weighted stop: a value in [0, total weight), walked along the strip.</summary>
        private int DrawStop()
        {
            int roll = _rng.NextInt(_totalWeight);
            for (int i = 0; i < Definition.Strip.Length; i++)
            {
                roll -= Definition.Strip[i].Weight;
                if (roll < 0) return i;
            }
            return Definition.Strip.Length - 1;
        }

        /// <summary>Spins at <paramref name="lineBet"/> per line. Null on success (see <see cref="Last"/>), else why not.</summary>
        public string Spin(decimal lineBet)
        {
            SlotDefinition d = Definition;
            if (lineBet < d.MinLineBet || lineBet > d.MaxLineBet || lineBet != Math.Floor(lineBet))
                return $"Bets are {CasinoMoney.Whole(d.MinLineBet)}–{CasinoMoney.Whole(d.MaxLineBet)} a line.";
            decimal bet = lineBet * d.LineCount;
            if (!_account.TryWager(bet, _now(), d.Name)) return _account.Refusal;

            var spin = new SlotSpin { Bet = bet, LinePays = new int[d.LineCount] };
            for (int r = 0; r < 3; r++) spin.Stops[r] = _forced != null ? _forced[r] : DrawStop();
            _forced = null;
            for (int l = 0; l < d.LineCount; l++)
            {
                int[] line = d.Lines[l];
                spin.LinePays[l] = d.Pay(d.At(spin.Stops[0] + line[0]), d.At(spin.Stops[1] + line[1]), d.At(spin.Stops[2] + line[2]));
                spin.Won += spin.LinePays[l] * lineBet;
            }
            if (d.Progressive && _jackpot != null)
            {
                // The pool grows by a fixed share of every bet; three top symbols on the centre line at max bet win it
                // in place of the fixed award.
                _jackpot.Contribute(bet * d.ProgressiveRate);
                bool top = spin.Symbol(d, 0, 1) == d.TopSymbol && spin.Symbol(d, 1, 1) == d.TopSymbol && spin.Symbol(d, 2, 1) == d.TopSymbol;
                if (top && lineBet == d.MaxLineBet)
                {
                    spin.Won -= spin.LinePays[0] * lineBet;
                    spin.Jackpot = _jackpot.Win();
                    spin.Won += spin.Jackpot;
                }
            }
            _account.Settle(CasinoGame.Slots, bet, spin.Won, _now(), d.Name);
            Last = spin;
            return null;
        }
    }

    /// <summary>The casino-wide progressive pool (Diamond Dusk). Reseeds when won. Saved with the casino.</summary>
    public sealed class SlotJackpot
    {
        public const decimal Seed = 5_000m;
        public decimal Pool { get; private set; } = Seed;
        public int TimesWon { get; private set; }

        public void Contribute(decimal amount)
        {
            if (amount > 0m) Pool += amount; // exact: fractions of a cent add up and stay in the pool
        }

        /// <summary>Pays the whole pool (whole dollars; the cents stay in) and reseeds.</summary>
        public decimal Win()
        {
            decimal paid = Math.Floor(Pool);
            Pool = Seed + (Pool - paid);
            TimesWon++;
            return paid;
        }

        public void Restore(decimal pool, int won)
        {
            Pool = Math.Max(Seed, pool);
            TimesWon = Math.Max(0, won);
        }
    }

    public static class CasinoMoney
    {
        public static string Whole(decimal v) => "$" + v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        public static string Cents(decimal v) => (v < 0m ? "-$" : "$") + Math.Abs(v).ToString(v == Math.Floor(v) ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
