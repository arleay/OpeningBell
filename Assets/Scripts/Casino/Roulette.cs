using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Core;

namespace OpeningBell.Casino
{
    public enum RouletteBetKind { Straight, Split, Street, Corner, SixLine, Red, Black, Odd, Even, Low, High, Dozen, Column }

    /// <summary>A table's rules (CASINO_SPEC §24): single zero (European) or 0 and 00 (American), and its limits.</summary>
    [Serializable]
    public sealed class RouletteRules
    {
        /// <summary>American wheels add 00 (pocket 37): the house edge rises from 2.70% to 5.26%.</summary>
        public bool DoubleZero;
        /// <summary>Least that must be on the layout for a spin.</summary>
        public decimal TableMin = 5m;
        /// <summary>Most on any one inside bet (straight, split, street, corner, six line).</summary>
        public decimal InsideMax = 500m;
        /// <summary>Most on any one outside bet (colours, odd/even, halves, dozens, columns).</summary>
        public decimal OutsideMax = 5_000m;

        public int Pockets => DoubleZero ? 38 : 37;
        public string Name => DoubleZero ? "Double zero" : "Single zero";
    }

    /// <summary>
    /// One bet on the layout: its kind, the numbers it covers and the stake. Pays (36 / covered) − 1 to one, so every
    /// bet has the same edge, which comes only from the zero(s) (§14).
    /// </summary>
    public sealed class RouletteBet
    {
        public RouletteBetKind Kind;
        /// <summary>Covered pockets; 37 means 00.</summary>
        public int[] Numbers;
        public decimal Amount;

        public int PaysToOne => Kind switch
        {
            RouletteBetKind.Straight => 35, RouletteBetKind.Split => 17, RouletteBetKind.Street => 11, RouletteBetKind.Corner => 8,
            RouletteBetKind.SixLine => 5, RouletteBetKind.Dozen or RouletteBetKind.Column => 2, _ => 1,
        };

        public bool Inside => Kind <= RouletteBetKind.SixLine;
        public bool Wins(int pocket) => Array.IndexOf(Numbers, pocket) >= 0;

        /// <summary>Same spot on the layout (so a second chip there adds to the stake).</summary>
        public string Key => Kind + ":" + string.Join(",", Numbers);

        public string Describe()
        {
            string ns = string.Join("-", Numbers.Select(Roulette.Label));
            return Kind switch
            {
                RouletteBetKind.Straight => ns,
                RouletteBetKind.Split => "Split " + ns,
                RouletteBetKind.Street => "Street " + ns,
                RouletteBetKind.Corner => "Corner " + ns,
                RouletteBetKind.SixLine => "Six line " + Roulette.Label(Numbers[0]) + "–" + Roulette.Label(Numbers[5]),
                RouletteBetKind.Dozen => (Numbers[0] == 1 ? "1st" : Numbers[0] == 13 ? "2nd" : "3rd") + " 12",
                RouletteBetKind.Column => "Column " + (Numbers[0] % 3 == 0 ? 3 : Numbers[0] % 3),
                RouletteBetKind.Low => "1–18",
                RouletteBetKind.High => "19–36",
                _ => Kind.ToString(),
            };
        }
    }

    /// <summary>The wheel and layout: colours, pocket order, and the bet builders (they check the geometry).</summary>
    public static class Roulette
    {
        public const int DoubleZeroPocket = 37;

        private static readonly HashSet<int> Reds = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        /// <summary>Clockwise pocket order on a European wheel, from 0.</summary>
        public static readonly int[] EuropeanWheel =
            { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };

        /// <summary>Clockwise order on an American wheel, from 0 (37 = 00).</summary>
        public static readonly int[] AmericanWheel =
            { 0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1, 37, 27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2 };

        public static bool IsRed(int n) => Reds.Contains(n);
        public static bool IsBlack(int n) => n >= 1 && n <= 36 && !Reds.Contains(n);
        public static bool IsGreen(int n) => n == 0 || n == DoubleZeroPocket;
        public static string Label(int n) => n == DoubleZeroPocket ? "00" : n.ToString();

        /// <summary>"17 Black", "0 Green": colour spelled out (colour-blind friendly, §100).</summary>
        public static string Describe(int n) => Label(n) + (IsGreen(n) ? " Green" : IsRed(n) ? " Red" : " Black");

        /// <summary>Layout row (1–12) and column (1–3) of a number 1–36: row r holds 3r−2, 3r−1, 3r.</summary>
        private static int Row(int n) => (n + 2) / 3;
        private static int Col(int n) => (n - 1) % 3 + 1;

        private static RouletteBet Bet(RouletteBetKind kind, decimal amount, params int[] numbers) =>
            new RouletteBet { Kind = kind, Amount = amount, Numbers = numbers.OrderBy(x => x).ToArray() };

        public static RouletteBet Straight(int n, decimal amount)
        {
            if (n < 0 || n > DoubleZeroPocket) throw new ArgumentOutOfRangeException(nameof(n));
            return Bet(RouletteBetKind.Straight, amount, n);
        }

        /// <summary>Two numbers touching on the layout (side by side or one above the other), or 0 with 1/2/3.</summary>
        public static RouletteBet Split(int a, int b, decimal amount)
        {
            if (!Adjacent(a, b)) throw new ArgumentException($"{Label(a)} and {Label(b)} don't touch on the layout.");
            return Bet(RouletteBetKind.Split, amount, a, b);
        }

        public static bool Adjacent(int a, int b)
        {
            if (a == b) return false;
            if (a > b) (a, b) = (b, a);
            if (a == 0 && b == DoubleZeroPocket) return true;             // 0-00 on American tables
            if (a == 0) return b == 1 || b == 2 || (b == 3);               // single zero touches 1, 2, 3
            if (b == DoubleZeroPocket) return a == 2 || a == 3;            // 00 touches 2 and 3
            if (a < 1 || b > 36) return false;
            if (Row(a) == Row(b)) return b - a == 1;                         // same row, next column
            return b - a == 3;                                                // same column, next row
        }

        /// <summary>A row of three: 1-2-3, 4-5-6 … 34-35-36 (<paramref name="row"/> 1–12).</summary>
        public static RouletteBet Street(int row, decimal amount)
        {
            if (row < 1 || row > 12) throw new ArgumentOutOfRangeException(nameof(row));
            int f = row * 3 - 2;
            return Bet(RouletteBetKind.Street, amount, f, f + 1, f + 2);
        }

        /// <summary>Four numbers in a square, named by the lowest: 1 (1-2-4-5) … 32 (32-33-35-36). Not column 3.</summary>
        public static RouletteBet Corner(int low, decimal amount)
        {
            if (low < 1 || low > 32 || Col(low) == 3) throw new ArgumentException($"No corner starts at {low}.");
            return Bet(RouletteBetKind.Corner, amount, low, low + 1, low + 3, low + 4);
        }

        /// <summary>Two rows (six numbers), <paramref name="row"/> being the first: 1 (1–6) … 11 (31–36).</summary>
        public static RouletteBet SixLine(int row, decimal amount)
        {
            if (row < 1 || row > 11) throw new ArgumentOutOfRangeException(nameof(row));
            int f = row * 3 - 2;
            return Bet(RouletteBetKind.SixLine, amount, f, f + 1, f + 2, f + 3, f + 4, f + 5);
        }

        public static RouletteBet Outside(RouletteBetKind kind, decimal amount, int which = 1)
        {
            IEnumerable<int> all = Enumerable.Range(1, 36);
            IEnumerable<int> ns = kind switch
            {
                RouletteBetKind.Red => all.Where(IsRed),
                RouletteBetKind.Black => all.Where(IsBlack),
                RouletteBetKind.Odd => all.Where(n => n % 2 == 1),
                RouletteBetKind.Even => all.Where(n => n % 2 == 0),
                RouletteBetKind.Low => all.Where(n => n <= 18),
                RouletteBetKind.High => all.Where(n => n >= 19),
                RouletteBetKind.Dozen => all.Where(n => (n - 1) / 12 + 1 == which),
                RouletteBetKind.Column => all.Where(n => Col(n) == which),
                _ => throw new ArgumentException("Not an outside bet: " + kind),
            };
            if ((kind == RouletteBetKind.Dozen || kind == RouletteBetKind.Column) && (which < 1 || which > 3))
                throw new ArgumentOutOfRangeException(nameof(which));
            return Bet(kind, amount, ns.ToArray());
        }
    }

    /// <summary>
    /// A spin at a table (CASINO_SPEC §24–27). Bets sit on the layout until the spin; <see cref="Spin"/> then takes
    /// them all off the chips, draws the pocket from the casino's random stream, and pays every winner, in one step.
    /// The wheel on screen is animated to land where this already decided (§26).
    /// </summary>
    public sealed class RouletteTable
    {
        private readonly CasinoAccount _account;
        private readonly SeededRandom _rng;
        private readonly Func<DateTime> _now;
        private readonly List<RouletteBet> _bets = new List<RouletteBet>();
        private readonly List<RouletteBet> _lastBets = new List<RouletteBet>();
        private readonly List<int> _history = new List<int>();

        public RouletteRules Rules { get; }
        public IReadOnlyList<RouletteBet> Bets => _bets;
        /// <summary>Recent results, newest last (the marquee board).</summary>
        public IReadOnlyList<int> History => _history;
        public decimal Staked => _bets.Sum(b => b.Amount);

        public int LastPocket { get; private set; } = -1;
        public decimal LastWagered { get; private set; }
        public decimal LastReturned { get; private set; }
        /// <summary>The winning bets of the last spin.</summary>
        public List<RouletteBet> LastWinners { get; } = new List<RouletteBet>();

        public RouletteTable(RouletteRules rules, CasinoAccount account, SeededRandom rng, Func<DateTime> now)
        {
            Rules = rules;
            _account = account;
            _rng = rng;
            _now = now;
        }

        /// <summary>Adds a chip to the layout (stacking on a bet already there). Null, or why not.</summary>
        public string Place(RouletteBet bet)
        {
            if (bet == null) return "No bet.";
            string invalid = CasinoAccount.Validate(bet.Amount);
            if (invalid != null) return invalid;
            if (!Rules.DoubleZero && bet.Numbers.Contains(Roulette.DoubleZeroPocket)) return "There's no 00 on this wheel.";
            RouletteBet same = _bets.Find(b => b.Key == bet.Key);
            decimal total = (same?.Amount ?? 0m) + bet.Amount;
            decimal max = bet.Inside ? Rules.InsideMax : Rules.OutsideMax;
            if (total > max) return $"Table max on that bet is {CasinoMoney.Whole(max)}.";
            if (Staked + bet.Amount > _account.Chips) return "Not enough chips.";
            if (same != null) same.Amount = total;
            else _bets.Add(new RouletteBet { Kind = bet.Kind, Numbers = bet.Numbers, Amount = bet.Amount });
            return null;
        }

        public void Clear() => _bets.Clear();

        private int? _forced;

        /// <summary>Tests only: the next spin lands on this pocket.</summary>
        internal void ForceNext(int pocket) => _forced = pocket;

        /// <summary>Puts last spin's bets back down, if the chips allow.</summary>
        public string Rebet()
        {
            if (_lastBets.Count == 0) return "No previous bets.";
            _bets.Clear();
            foreach (RouletteBet b in _lastBets)
            {
                string error = Place(new RouletteBet { Kind = b.Kind, Numbers = b.Numbers, Amount = b.Amount });
                if (error != null) return error;
            }
            return null;
        }

        /// <summary>Spins: all bets settle at once. Null on success, else why not (nothing taken).</summary>
        public string Spin()
        {
            decimal staked = Staked;
            if (_bets.Count == 0) return "Place a bet first.";
            if (staked < Rules.TableMin) return $"Table minimum is {CasinoMoney.Whole(Rules.TableMin)} a spin.";
            if (!_account.TryWager(staked, _now(), "Roulette")) return _account.Refusal;

            int pocket = _forced ?? _rng.NextInt(Rules.Pockets); // 0–36, and 37 (00) on double-zero wheels
            _forced = null;
            LastWinners.Clear();
            decimal returned = 0m;
            foreach (RouletteBet b in _bets)
                if (b.Wins(pocket))
                {
                    returned += b.Amount * (b.PaysToOne + 1);
                    LastWinners.Add(b);
                }
            _account.Settle(CasinoGame.Roulette, staked, returned, _now(), "Roulette " + Roulette.Describe(pocket));

            LastPocket = pocket;
            LastWagered = staked;
            LastReturned = returned;
            _history.Add(pocket);
            if (_history.Count > 16) _history.RemoveAt(0);
            _lastBets.Clear();
            _lastBets.AddRange(_bets);
            _bets.Clear();
            return null;
        }
    }
}
