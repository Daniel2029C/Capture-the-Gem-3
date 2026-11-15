using UnityEditor;
using UnityEngine;

namespace CTG.EditorTools
{
    /// <summary>
    /// Import rules for character art under Assets/Game/Art/Characters and Assets/Resources/ClassArt: sprites at 16
    /// pixels per unit (one art pixel = one game pixel, the same scale as terrain), point filtering so the 2 × 2 pixel
    /// blocks stay crisp, no mipmaps or compression, and readable so sheets can be split into layers and outlined.
    /// Single images (death bursts) get their pivot at the bottom centre; a file switched to Multiple keeps its pivots.
    /// </summary>
    public sealed class CharacterArtImporter : AssetPostprocessor
    {
        public static readonly string[] Folders = { "Assets/Game/Art/Characters/", "Assets/Resources/ClassArt/" };
        public const float PixelsPerUnit = 16f;

        static bool Applies(string path)
        {
            path = path.Replace('\\', '/');
            foreach (string folder in Folders)
                if (path.StartsWith(folder)) return true;
            return false;
        }

        void OnPreprocessTexture()
        {
            if (!Applies(assetPath)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;

            if (importer.importSettingsMissing) importer.spriteImportMode = SpriteImportMode.Single;
            if (importer.spriteImportMode == SpriteImportMode.Single)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
            }
        }
    }
}
