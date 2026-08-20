using UnityEditor;
using UnityEngine;

namespace Guandan.EditorTools
{
    public static class GuandanUiTextureSetup
    {
        [MenuItem("掼蛋/刷新 UI 面板纹理")]
        public static void RefreshUiTextures()
        {
            Configure("Assets/Resources/GuandanUI/TombPanel.png", new Vector4(185f, 185f, 185f, 185f));
            Configure("Assets/Resources/GuandanUI/TombButton.png", new Vector4(118f, 58f, 118f, 58f));
            Configure("Assets/Resources/GuandanUI/ScoreRail.png", new Vector4(112f, 92f, 112f, 92f));
            Configure("Assets/Resources/GuandanUI/ScoreRailCapLeft.png", Vector4.zero);
            Configure("Assets/Resources/GuandanUI/ScoreRailCapRight.png", Vector4.zero);
            Configure("Assets/Resources/GuandanUI/SeatPlaque.png", Vector4.zero, true);
            Configure("Assets/Resources/GuandanUI/HandRackPanel.png", new Vector4(185f, 96f, 185f, 96f));
            Configure("Assets/Resources/GuandanUI/CardFace.png", new Vector4(45f, 45f, 45f, 45f));
            AssetDatabase.Refresh();
            Debug.Log("[GuandanUI] 墓穴风格面板与按钮纹理已刷新。");
        }

        private static void Configure(string path, Vector4 border, bool hasTransparency = false)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = hasTransparency;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }
    }
}
