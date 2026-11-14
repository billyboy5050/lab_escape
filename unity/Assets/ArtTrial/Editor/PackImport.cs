#nullable enable
using UnityEditor;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>
    /// Import settings for the trial's packs: textures at most 1024 pixels, as a web build would carry them, and normal maps
    /// marked as such.
    /// </summary>
    public sealed class PackImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtTrial.Packs)) return;
            var t = (TextureImporter)assetImporter;
            t.maxTextureSize = 1024; // ArtTrialBuild.MaxTexture, which also catches textures imported before this compiled
            if (assetPath.Contains("_Normal")) t.textureType = TextureImporterType.NormalMap;
        }
    }
}
