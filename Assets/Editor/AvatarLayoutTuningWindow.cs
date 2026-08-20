using System.IO;
using Guandan.Scene;
using UnityEditor;
using UnityEngine;

namespace Guandan.EditorTools
{
    public sealed class AvatarLayoutTuningWindow : EditorWindow
    {
        private const string AssetPath = "Assets/Resources/AvatarLayoutSettings.asset";
        private AvatarLayoutSettings settings;
        private Vector2 scroll;

        [MenuItem("掼蛋/人物位置与大小调节")]
        public static void Open()
        {
            var window = GetWindow<AvatarLayoutTuningWindow>("人物调节");
            window.minSize = new Vector2(420f, 610f);
            window.Show();
        }

        private void OnEnable()
        {
            settings = EnsureSettingsAsset();
        }

        private void OnGUI()
        {
            if (settings == null) settings = EnsureSettingsAsset();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("牌桌人物布局", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "当前为实时预览：修改后会立即应用到三名人物和 f4 凳子。"
                    : "这些参数不会移动或保存墓穴场景。进入 Play 后可实时预览，数值会保留到下次运行。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(settings, "调整掼蛋人物布局");
            settings.globalScale = EditorGUILayout.Slider("整体大小倍率", settings.globalScale, 0.75f, 1.45f);
            settings.chestToTableOffset = EditorGUILayout.Slider("胸口相对桌面高度", settings.chestToTableOffset, -0.45f, 0.45f);
            settings.chairToHipGap = EditorGUILayout.Slider("箱顶与臀部间距", settings.chairToHipGap, 0.02f, 0.28f);
            DrawSeat("东家 · 右侧对手", settings.east);
            DrawSeat("北家 · 对面队友", settings.north);
            DrawSeat("西家 · 左侧对手", settings.west);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                ApplyToRunningAvatars();
            }

            EditorGUILayout.Space(14f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("应用到运行中的人物", GUILayout.Height(34f))) ApplyToRunningAvatars();
                if (GUILayout.Button("恢复推荐值", GUILayout.Height(34f))) ResetRecommended();
            }
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("位置偏移使用场景米制单位；Y 为上下，Yaw 为人物左右转向。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSeat(string label, AvatarSeatLayout seat)
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                seat.positionOffset = EditorGUILayout.Vector3Field("位置偏移", seat.positionOffset);
                seat.scale = EditorGUILayout.Slider("人物大小", seat.scale, 0.65f, 1.65f);
                seat.yawOffset = EditorGUILayout.Slider("朝向微调 Yaw", seat.yawOffset, -45f, 45f);
                seat.chairHeightMultiplier = EditorGUILayout.Slider("f4 箱子高度", seat.chairHeightMultiplier, 0.65f, 1.45f);
            }
        }

        private void ApplyToRunningAvatars()
        {
            if (!Application.isPlaying) return;
            foreach (var bindings in Object.FindObjectsByType<RuntimeGameplayBindings>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                bindings.ApplyLayoutSettings(settings);
            SceneView.RepaintAll();
        }

        private void ResetRecommended()
        {
            Undo.RecordObject(settings, "恢复人物推荐布局");
            settings.globalScale = 1f;
            settings.chestToTableOffset = 0f;
            settings.chairToHipGap = 0.08f;
            ResetSeat(settings.east);
            ResetSeat(settings.north);
            ResetSeat(settings.west);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            ApplyToRunningAvatars();
        }

        private static void ResetSeat(AvatarSeatLayout seat)
        {
            seat.positionOffset = Vector3.zero;
            seat.scale = 1f;
            seat.yawOffset = 0f;
            seat.chairHeightMultiplier = 1f;
        }

        private static AvatarLayoutSettings EnsureSettingsAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AvatarLayoutSettings>(AssetPath);
            if (asset != null) return asset;
            Directory.CreateDirectory("Assets/Resources");
            asset = CreateInstance<AvatarLayoutSettings>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
