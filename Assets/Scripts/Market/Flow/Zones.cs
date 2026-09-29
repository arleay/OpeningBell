using System;
using System.Collections.Generic;

namespace OpeningBell.Market
{
    /// <summary>Where a zone's resting interest came from. Append only: saved by value.</summary>
    public enum ZoneKind
    {
        /// <summary>The base a rally launched from (bullish order block): buyers who missed it wait there.</summary>
        Demand,
        /// <summary>The base a drop launched from (bearish order block).</summary>
        Supply,
        /// <summary>A bullish fair value gap: gap traders bid its midpoint.</summary>
        BullishGap,
        /// <summary>A bearish fair value gap: gap traders offer its midpoint.</summary>
        BearishGap,
        /// <summary>Where an institution soaked up the other side (absorption).</summary>
        Absorption,
    }

    /// <summary>Append only: saved by value.</summary>
    public enum ZoneState
    {
        Untouched,
        PartlyMitigated,
        Mitigated,
        Invalidated,
    }

    /// <summary>
    /// A supply or demand zone: a price band where an imbalance happened and some traders now rest orders, expecting
    /// the other side to show up again. Retests consume the interest and it doesn't come back, so a first touch may
    /// hold and a third usually doesn't. Closing through the far side invalidates it; an invalidated order block
    /// flips once (a breaker) because the traders trapped in it now want out at break-even.
    /// Like levels, a zone never moves price: it only changes what flow meets on the way.
    /// </summary>
    public sealed class Zone
    {
        public ZoneKind Kind;
        /// <summary>Log price band.</summary>
        public double Low, High;
        /// <summary>+1: buyers rest here (demand), −1: sellers (supply).</summary>
        public int Side;
        /// <summary>How much traders care, 0..1+ (size of the displacement, volume behind it).</summary>
        public double Strength;
        /// <summary>Resting push still in the zone, and what it started with.</summary>
        public double Interest, Initial;
        /// <summary>Volume of the creating bar relative to the typical bar.</summary>
        public double CreationVolume;
        public int Retests;
        public ZoneState State;
        /// <summary>Minutes since creation.</summary>
        public double Age;
        /// <summary>Start (ticks) of the candle that made it.</summary>
        public long CreatedTicks;
        /// <summary>Whether price is inside the band (for counting retests).</summary>
        public bool Inside;
        /// <summary>Already flipped once into a breaker.</summary>
        public bool Flipped;

        public bool IsGap => Kind == ZoneKind.BullishGap || Kind == ZoneKind.BearishGap;

        /// <summary>
        /// Where the resting orders sit for flow arriving from the other side: the near edge of an order block, the
        /// midpoint of a gap (its "consequent encroachment", where most gap traders place their limits).
        /// </summary>
        public double Entry => IsGap ? (Low + High) / 2 : Side > 0 ? High : Low;

        public bool Live => State != ZoneState.Invalidated && State != ZoneState.Mitigated;
    }

    /// <summary>A stock's zones, with fixed capacity: the weakest live zone makes way, dead ones go first.</summary>
    public sealed class ZoneBook
    {
        public const int Capacity = 12;
        private readonly List<Zone> _zones = new List<Zone>(Capacity);

        public IReadOnlyList<Zone> All => _zones;

        internal List<Zone> Mutable => _zones;

        /// <summary>Adds a zone unless an overlapping live zone of the same side already covers it (then that one is reinforced).</summary>
        public Zone Add(Zone z)
        {
            foreach (Zone o in _zones)
            {
                if (!o.Live || o.Side != z.Side || o.High < z.Low || o.Low > z.High) continue;
                o.Strength = Math.Min(2.0, Math.Max(o.Strength, z.Strength) + 0.2 * Math.Min(o.Strength, z.Strength));
                o.Interest = Math.Max(o.Interest, z.Interest);
                o.Initial = Math.Max(o.Initial, o.Interest);
                return o;
            }

            if (_zones.Count >= Capacity)
            {
                int victim = -1;
                for (int i = 0; i < _zones.Count; i++)
                {
                    if (!_zones[i].Live) { victim = i; break; }
                    if (victim < 0 || _zones[i].Strength < _zones[victim].Strength) victim = i;
                }
                if (_zones[victim].Live && _zones[victim].Strength >= z.Strength) return null;
                _zones.RemoveAt(victim);
            }
            _zones.Add(z);
            return z;
        }

        public void Clear() => _zones.Clear();
    }
}
