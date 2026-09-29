using System;
using System.Collections.Generic;
using OpeningBell.Core;

namespace OpeningBell.Casino
{
    public enum Suit { Clubs, Diamonds, Hearts, Spades }

    /// <summary>A playing card. Rank 1 = ace, 11–13 = jack, queen, king.</summary>
    public readonly struct Card : IEquatable<Card>
    {
        public readonly int Rank;
        public readonly Suit Suit;

        public Card(int rank, Suit suit)
        {
            if (rank < 1 || rank > 13) throw new ArgumentOutOfRangeException(nameof(rank));
            Rank = rank;
            Suit = suit;
        }

        public bool IsAce => Rank == 1;

        /// <summary>Blackjack value: faces count 10, an ace 1 (the hand decides when it's 11).</summary>
        public int Points => Rank >= 10 ? 10 : Rank;

        /// <summary>Compact code for saves: suit × 13 + rank − 1 (0..51).</summary>
        public int Code => (int)Suit * 13 + Rank - 1;
        public static Card FromCode(int code) => new Card(code % 13 + 1, (Suit)(code / 13));

        public string RankName => Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => Rank.ToString() };

        public override string ToString() => RankName + "CDHS"[(int)Suit];

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object obj) => obj is Card c && Equals(c);
        public override int GetHashCode() => Code;

        /// <summary>Shorthand for tests and tools: "A", "10", "K" (suit spades unless given).</summary>
        public static Card Of(string rank, Suit suit = Suit.Spades) => new Card(rank switch
        {
            "A" => 1, "J" => 11, "Q" => 12, "K" => 13, _ => int.Parse(rank),
        }, suit);
    }

    /// <summary>
    /// A dealing shoe of several decks, shuffled with the casino's own random stream. When dealing passes the cut
    /// card (a share of the shoe), the next round starts from a fresh shuffle. The outcome is decided here, before
    /// anything is animated.
    /// </summary>
    public sealed class Shoe
    {
        private readonly List<Card> _cards = new List<Card>();
        private readonly Queue<Card> _stacked = new Queue<Card>();
        private readonly SeededRandom _rng;
        private readonly double _penetration;
        private int _next;

        public int Decks { get; }
        public int Remaining => _cards.Count - _next;
        public int Size => _cards.Count;

        /// <summary>Dealing has passed the cut card: shuffle before the next round.</summary>
        public bool PastCut => _next >= (int)(_cards.Count * _penetration);

        public Shoe(int decks, double penetration, SeededRandom rng)
        {
            if (decks < 1) throw new ArgumentOutOfRangeException(nameof(decks));
            Decks = decks;
            _penetration = Math.Max(0.2, Math.Min(0.95, penetration));
            _rng = rng;
            for (int d = 0; d < decks; d++)
                for (int code = 0; code < 52; code++) _cards.Add(Card.FromCode(code));
            Shuffle();
        }

        /// <summary>Fisher–Yates over the whole shoe.</summary>
        public void Shuffle()
        {
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = _rng.NextInt(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
            _next = 0;
        }

        public Card Draw()
        {
            if (_stacked.Count > 0) return _stacked.Dequeue();
            if (_next >= _cards.Count) Shuffle(); // never runs dry mid-round
            return _cards[_next++];
        }

        /// <summary>The shoe's order and position, so a hand saved mid-play finishes with the same cards.</summary>
        public (List<int> order, int next) Capture()
        {
            var order = new List<int>(_cards.Count);
            foreach (Card c in _cards) order.Add(c.Code);
            return (order, _next);
        }

        public void Restore(List<int> order, int next)
        {
            if (order == null || order.Count != _cards.Count) return; // a different shoe size: keep the fresh shuffle
            for (int i = 0; i < order.Count; i++) _cards[i] = Card.FromCode(order[i]);
            _next = Math.Max(0, Math.Min(next, _cards.Count));
        }

        /// <summary>Tests only: these cards come out next, in order.</summary>
        internal void Stack(params Card[] cards)
        {
            foreach (Card c in cards) _stacked.Enqueue(c);
        }
    }
}
