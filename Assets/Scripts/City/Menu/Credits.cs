namespace OpeningBell.City
{
    /// <summary>
    /// Attribution shown in-game (title screen, CREDITS). CC BY assets must be credited where players can see it;
    /// the CC0 ones are thanked too. CREDITS.md in the repo is the full record (sources, folders, licence files).
    /// </summary>
    public static class Credits
    {
        public static readonly (string Heading, string[] Lines)[] Sections =
        {
            ("3D MODELS (CC BY 4.0)", new[]
            {
                "Food Collection - Game Ready by Melon Polygons",
                "Gas station Props by Elbolillo",
                "Street Asset Pack by vmatthew",
                "Modular Urban Fence Pack by TampaJoey",
                "Chain link fence pack by TampaJoey",
                "Office Props Pack by crazyshroomz",
                "Computer Desk by draakon_4d",
                "Low poly - Computer Mouse by IQINISO",
                "Compact Keyboard (Custom 75%) by Saksham Tale",
                "via sketchfab.com, licensed under creativecommons.org/licenses/by/4.0",
            }),
            ("3D MODELS (CC BY-SA 3.0)", new[]
            {
                "Cavallo Ibrido (LaFerrari restyle) by zenox3d",
                "Kessel RSR (Porsche GT3 RSR) by Neubi, via BlendSwap",
            }),
            ("ALSO USED", new[]
            {
                "Computer Workspace Pack by Manix3D (Sketchfab)",
                "Kenney (kenney.nl): car, city, furniture and nature kits",
                "Quaternius (quaternius.com): characters, animations, food, plants, cars",
                "Poly Haven (polyhaven.com): furniture",
                "ambientCG (ambientcg.com): surface textures",
                "Rgsdev: low poly vehicles",
                "Engine recordings: BxB Studio, Multiversal Vehicle Controller",
            }),
        };
    }
}
