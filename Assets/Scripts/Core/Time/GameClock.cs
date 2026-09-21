using System;

namespace OpeningBell.Core
{
    /// <summary>
    /// Authoritative in-game time. Real time is converted with <see cref="TimeScale"/>; simulations follow
    /// this clock in fixed steps, so their results do not depend on framerate or the scale in use.
    /// </summary>
    public sealed class GameClock
    {
        public DateTime Now { get; private set; }

        /// <summary>In-game seconds per real second.</summary>
        public double TimeScale { get; set; }

        public bool IsPaused { get; set; }

        public GameClock(DateTime start, double timeScale)
        {
            Now = start;
            TimeScale = timeScale;
        }

        public void Advance(double realSeconds)
        {
            if (IsPaused || realSeconds <= 0 || TimeScale <= 0) return;
            Now = Now.AddTicks((long)(realSeconds * TimeScale * TimeSpan.TicksPerSecond));
        }

        /// <summary>Skips forward (sleeping, debug). Time never runs backwards: simulations cannot rewind.</summary>
        public void JumpTo(DateTime time)
        {
            if (time > Now) Now = time;
        }
    }
}
