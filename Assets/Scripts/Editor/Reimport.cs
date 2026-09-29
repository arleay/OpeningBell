using UnityEditor;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Force-reimports the third-party models (batch: -executeMethod OpeningBell.EditorTools.Reimport.Models), for when
    /// an import rule changes and Unity doesn't pick it up on its own.
    /// </summary>
    public static class Reimport
    {
        public static void Models()
        {
            string[] folders =
            {
                ArtImportRules.Nature.TrimEnd('/'), ArtImportRules.PolyHaven.TrimEnd('/'), ArtImportRules.Sketchfab.TrimEnd('/'),
                ArtImportRules.Cars.TrimEnd('/'), "Assets/Art/ThirdParty/Quaternius/Cars", "Assets/Art/ThirdParty/Kenney/CarKit", "Assets/Art/ThirdParty/Rgsdev/Vehicles",
            };
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Model", folders))
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
    }
}
