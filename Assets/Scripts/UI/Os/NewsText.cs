using System;
using System.Globalization;
using OpeningBell.Market;

namespace OpeningBell.UI
{
    /// <summary>How a story is labelled and introduced. Shared by the phone and the computer so they read the same.</summary>
    public static class NewsText
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static string Tag(NewsItem item) => item.Scope switch
        {
            NewsScope.Market => item.Type == CatalystType.PresidentTweet ? "PRESIDENT · MARKETS"
                : item.Type == CatalystType.Geopolitical ? "WORLD · MARKETS" : "MARKETS",
            NewsScope.Sector => "SECTOR · " + string.Join(" ", item.Tickers),
            _ => string.Join(" ", item.Tickers),
        };

        public static string Ago(DateTime time, DateTime now)
        {
            TimeSpan d = now - time;
            if (d.TotalMinutes < 1) return "now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
            return time.ToString("MMM d", C);
        }

        /// <summary>A standfirst by kind of story. It sets the scene without saying which way prices will go.</summary>
        public static string Dek(CatalystType type) => type switch
        {
            CatalystType.Earnings => "Results are out, and traders are weighing them against what was expected.",
            CatalystType.Guidance => "The company updated its outlook for the quarters ahead.",
            CatalystType.Management => "A change at the top of the company.",
            CatalystType.Regulatory => "Regulators have weighed in, and the decision could shape the business for years.",
            CatalystType.Product => "A new product is on the way.",
            CatalystType.Lawsuit => "The company is facing legal action.",
            CatalystType.Acquisition => "A deal is on the table.",
            CatalystType.Contract => "A new contract adds to the order book.",
            CatalystType.Investigation => "Investigators are taking a closer look at the company.",
            CatalystType.Analyst => "Wall Street analysts have changed their view.",
            CatalystType.Offering => "The company is raising money by selling new shares.",
            CatalystType.Buyback => "The company plans to buy back its own stock.",
            CatalystType.Bankruptcy => "New warning signs about the company's finances.",
            CatalystType.Sector => "The whole group is moving together on the news.",
            CatalystType.Economic => "Fresh economic news, and traders are rethinking where rates and growth are headed.",
            CatalystType.Geopolitical => "World events are rattling markets. Headlines like this can move whole sectors at once, and not always the same way.",
            CatalystType.PresidentTweet => "The president just posted, and the whole market is reacting. Moves like this come fast and can reverse just as fast.",
            _ => "",
        };
    }
}
