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
            // Only ever lowered, so a smaller cap set by ArtTrialBuild survives the reimport a build's platform switch causes.
            if (t.maxTextureSize > 1024) t.maxTextureSize = 1024;
            if (assetPath.Contains("_Normal")) t.textureType = TextureImporterType.NormalMap;
        }
    }
}
