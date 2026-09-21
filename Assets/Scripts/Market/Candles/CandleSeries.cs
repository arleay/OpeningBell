using System;
using System.Collections.Generic;

namespace OpeningBell.Market
{
    /// <summary>Completed candles plus the one still forming. Index Count-1 is the forming candle, if any.</summary>
    public sealed class CandleSeries
    {
        private readonly List<Candle> _completed = new List<Candle>();
        private readonly int _maxCompleted;
        private Candle _current;

        public Timeframe Timeframe { get; }
        public IReadOnlyList<Candle> Completed => _completed;
        public bool HasCurrent { get; private set; }
        public Candle Current => _current;
        public int Count => _completed.Count + (HasCurrent ? 1 : 0);

        public Candle this[int index] => index == _completed.Count && HasCurrent ? _current : _completed[index];

        internal CandleSeries(Timeframe timeframe, int maxCompleted)
        {
            Timeframe = timeframe;
            _maxCompleted = maxCompleted;
        }

        /// <summary>Replaces history; the last candle becomes the forming one (it was when saved).</summary>
        internal void Restore(IReadOnlyList<Candle> candles)
        {
            _completed.Clear();
            HasCurrent = candles.Count > 0;
            for (int i = 0; i < candles.Count - 1; i++) _completed.Add(candles[i]);
            if (HasCurrent) _current = candles[candles.Count - 1];
        }

        /// <summary>Folds a finer candle into this timeframe. Equivalent to recording each of its prints in order.</summary>
        internal void Merge(DateTime bucketStart, Candle finer)
        {
            if (HasCurrent && bucketStart == _current.Start)
            {
                _current = new Candle(_current.Start, _current.Open, Math.Max(_current.High, finer.High), Math.Min(_current.Low, finer.Low),
                    finer.Close, _current.Volume + finer.Volume, _current.Notional + finer.Notional);
                return;
            }
            if (HasCurrent) _completed.Add(_current);
            _current = new Candle(bucketStart, finer.Open, finer.High, finer.Low, finer.Close, finer.Volume, finer.Notional);
            HasCurrent = true;
        }

        internal void Record(DateTime bucketStart, decimal price, long volume)
        {
            if (HasCurrent && bucketStart == _current.Start)
            {
                _current = _current.Include(price, volume);
                return;
            }

            if (HasCurrent)
            {
                if (bucketStart < _current.Start)
                    throw new InvalidOperationException($"Out-of-order candle data: {bucketStart:O} before {_current.Start:O}.");
                _completed.Add(_current);
                // Trim in chunks so the O(n) shift happens rarely.
                if (_completed.Count > _maxCompleted + _maxCompleted / 4)
                    _completed.RemoveRange(0, _completed.Count - _maxCompleted);
            }

            _current = new Candle(bucketStart, price, price, price, price, volume, price * volume);
            HasCurrent = true;
        }
    }
}
