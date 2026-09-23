using System;
using System.Collections.Generic;

namespace OpeningBell
{
    /// <summary>Chart drawing tools. Saved by value: append only.</summary>
    public enum DrawingKind
    {
        HorizontalLine,
        Trendline,
        Ray,
        Rectangle,
        Fibonacci,
        VerticalLine,
    }

    /// <summary>
    /// One thing the player drew on a chart. Anchored in time and price (not pixels or candle indices), so it stays on
    /// the same prices and moments on every timeframe and survives zooming, panning and reloading.
    /// </summary>
    [Serializable]
    public sealed class ChartDrawing
    {
        public long Id;
        public string Ticker = "";
        public DrawingKind Kind;
        /// <summary>Anchor times (DateTime ticks) and prices. Horizontal lines use only P1; vertical lines only T1.</summary>
        public long T1, T2;
        public double P1, P2;
        /// <summary>Hex colour, e.g. "#5A9CF5".</summary>
        public string Color = "#5A9CF5";
        public float Width = 1.5f;
        /// <summary>Rectangles and trendlines can run on to the right edge (zones that stay relevant).</summary>
        public bool ExtendRight;

        public ChartDrawing Copy() => (ChartDrawing)MemberwiseClone();
    }

    /// <summary>Every drawing, kept per symbol. Part of the save, so a load shows exactly what was drawn.</summary>
    public sealed class ChartDrawings
    {
        private readonly List<ChartDrawing> _items = new List<ChartDrawing>();
        private long _nextId = 1;

        public event Action Changed;

        public IReadOnlyList<ChartDrawing> All => _items;

        public IEnumerable<ChartDrawing> For(string ticker)
        {
            foreach (ChartDrawing d in _items)
                if (d.Ticker == ticker) yield return d;
        }

        public ChartDrawing Add(ChartDrawing drawing)
        {
            drawing.Id = _nextId++;
            _items.Add(drawing);
            Changed?.Invoke();
            return drawing;
        }

        public void Remove(long id)
        {
            if (_items.RemoveAll(d => d.Id == id) > 0) Changed?.Invoke();
        }

        /// <summary>Call after editing a drawing in place (dragging, recolouring).</summary>
        public void Touch() => Changed?.Invoke();

        public ChartDrawingsSaveData CaptureState() => new ChartDrawingsSaveData
        {
            NextId = _nextId,
            Items = _items.ConvertAll(d => d.Copy()),
        };

        public void RestoreState(ChartDrawingsSaveData data)
        {
            _items.Clear();
            if (data?.Items != null) _items.AddRange(data.Items.ConvertAll(d => d.Copy()));
            _nextId = Math.Max(data?.NextId ?? 1, 1);
            foreach (ChartDrawing d in _items) _nextId = Math.Max(_nextId, d.Id + 1);
            Changed?.Invoke();
        }
    }

    [Serializable]
    public sealed class ChartDrawingsSaveData
    {
        public long NextId = 1;
        public List<ChartDrawing> Items = new List<ChartDrawing>();
    }
}
