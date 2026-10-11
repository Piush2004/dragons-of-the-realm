using UnityEditor;
using UnityEngine;

namespace BlockRealm.EditorTools
{
    /// <summary>
    /// Import rules for the art the Blender scripts generate: pixel art stays crisp and uncompressed, models import
    /// without their own materials (the game assigns its shaders in code) and with Blender's units.
    /// </summary>
    public sealed class ImportSettings : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/BlockRealm/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            TextureImporter t = (TextureImporter)assetImporter;
            string path = assetPath.Substring(Root.Length);

            t.textureType = TextureImporterType.Default;
            t.alphaIsTransparency = true;
            t.sRGBTexture = true;
            t.mipmapEnabled = false;
            t.npotScale = TextureImporterNPOTScale.None;

            bool dragonSkin = path.StartsWith("Models/Textures/dragon") || path.Contains("_cinderwing_") || path.Contains("_brazenhide_") || path.Contains("_elderscale_") ||
                              (path.StartsWith("Models/Textures/") && (path.Contains("cinderwing") || path.Contains("brazenhide") || path.Contains("elderscale")));
            if (dragonSkin)
            {
                // Large painted skins: smooth filtering and normal maps stay readable.
                t.filterMode = FilterMode.Bilinear;
                t.mipmapEnabled = true;
                t.maxTextureSize = 2048;
                t.textureCompression = TextureImporterCompression.Compressed;
                if (path.EndsWith("_normal.png")) t.sRGBTexture = false;
                return;
            }

            t.filterMode = FilterMode.Point;
            t.wrapMode = path.StartsWith("Sky/clouds") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.maxTextureSize = 1024;
            t.isReadable = path.StartsWith("Tiles/");                    // the tile array is assembled from these
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            ModelImporter m = (ModelImporter)assetImporter;
            m.importAnimation = false;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            m.isReadable = false;
            m.useFileScale = true;
            m.globalScale = 1f;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.optimizeMeshPolygons = true;
            m.optimizeMeshVertices = true;
        }
    }
}
