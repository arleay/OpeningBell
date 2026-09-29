namespace OpeningBell.Fund
{
    /// <summary>The logo marks and brand colours a fund can register with (shown on the portal, contracts and signage).</summary>
    public static class FundBrand
    {
        public static readonly string[] Logos = { "Pillars", "Ascent", "Compass", "Shield", "Hexagon", "Arc" };

        /// <summary>Name and colour (0–1 RGB) of each brand colour.</summary>
        public static readonly (string Name, float R, float G, float B)[] Colours =
        {
            ("Navy", 0.12f, 0.23f, 0.54f),
            ("Emerald", 0.02f, 0.47f, 0.34f),
            ("Oxblood", 0.55f, 0.12f, 0.13f),
            ("Bronze", 0.64f, 0.42f, 0.15f),
            ("Slate", 0.2f, 0.25f, 0.33f),
            ("Teal", 0.06f, 0.46f, 0.43f),
            ("Plum", 0.42f, 0.13f, 0.66f),
            ("Graphite", 0.07f, 0.09f, 0.15f),
        };

        public static int Clamp(int i, int n) => i < 0 ? 0 : i >= n ? n - 1 : i;
    }
}
