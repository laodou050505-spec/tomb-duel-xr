using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Guandan.Game;
using Guandan.Scene;

namespace Guandan.EditorTools
{
    /// <summary>
    /// Non-persistent Scene-view preview for the same sitting avatar prefabs used at runtime.
    /// The preview root and its children are DontSave objects, so tuning never alters the
    /// authored tomb scene. Handles write to AvatarLayoutSettings and the runtime reads the
    /// same values after entering Play.
    /// </summary>
    [InitializeOnLoad]
    public static class AvatarScenePreview
    {
        private const string RootName = "人物坐姿预览 · 不保存";
        private const string PlayableScenePath = "Assets/Scenes/GuandanTomb.unity";
        private static readonly Dictionary<PlayerSeat, GameObject> previews = new();
        private static readonly Dictionary<PlayerSeat, Transform> anchors = new();
        private static readonly Dictionary<PlayerSeat, Vector3> sourceScales = new();
        private static AvatarLayoutSettings settings;
        private static bool visible;

        static AvatarScenePreview()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += EnsureDefaultPreview;
        }

        private static void EnsureDefaultPreview()
        {
            // Keep the authored Scene clean. The preview remains available from the
            // 掼蛋 menu when a placement pass is explicitly needed.
            if (Application.isPlaying || visible || EditorSceneManager.GetActiveScene().path != PlayableScenePath) return;
        }

        [MenuItem("掼蛋/场景显示坐姿人物预览")]
        public static void Toggle()
        {
            visible = !visible;
            if (visible) Build(); else Clear();
            SceneView.RepaintAll();
        }

        [MenuItem("掼蛋/场景显示坐姿人物预览", true)]
        private static bool ToggleValidation()
        {
            Menu.SetChecked("掼蛋/场景显示坐姿人物预览", visible);
            return !Application.isPlaying;
        }

        [MenuItem("掼蛋/清理坐姿预览")]
        public static void Clear()
        {
            foreach (var preview in previews.Values)
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            previews.Clear();
            anchors.Clear();
            sourceScales.Clear();
            var root = GameObject.Find(RootName);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            visible = false;
            SceneView.RepaintAll();
        }

        private static void Build()
        {
            Clear();
            settings = LoadSettings();
            var seatsRoot = GameObject.Find("Seats_四家座位与角色锚点");
            if (seatsRoot == null) return;
            var root = new GameObject(RootName);
            root.hideFlags = HideFlags.DontSave;
            var anchors = new[]
            {
                FindAnchor(seatsRoot.transform, "Seat_East_对手"),
                FindAnchor(seatsRoot.transform, "Seat_North_队友"),
                FindAnchor(seatsRoot.transform, "Seat_West_对手"),
            };
            var seats = new[] { PlayerSeat.East, PlayerSeat.North, PlayerSeat.West };
            for (var i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] == null) continue;
                var prefab = Resources.Load<GameObject>($"GuandanAvatars/Avatar_{i + 1}");
                if (prefab == null) continue;
                var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null) continue;
                instance.name = $"坐姿预览_{SeatLabel(seats[i])}";
                instance.transform.SetParent(root.transform, false);
                instance.hideFlags = HideFlags.DontSave;
                foreach (var child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
                var layout = settings.ForSeat(seats[i]);
                var baseRotation = FaceTable(anchors[i].position, FindTableCenter());
                instance.transform.position = anchors[i].position + layout.positionOffset;
                instance.transform.rotation = baseRotation * Quaternion.Euler(0f, layout.yawOffset, 0f);
                var sourceScale = instance.transform.localScale;
                ApplyRuntimeLikeSizing(instance.transform, anchors[i].position.y, FindTableTopY(), settings, layout);
                previews[seats[i]] = instance;
                AvatarScenePreview.anchors[seats[i]] = anchors[i];
                sourceScales[seats[i]] = sourceScale;
            }
            visible = previews.Count > 0;
        }

        private static void ApplyRuntimeLikeSizing(Transform root, float floorY, float tableTopY, AvatarLayoutSettings tuning, AvatarSeatLayout seat)
        {
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.Update(0f);
            }
            if (!TryGetBounds(root, out var bounds)) return;
            root.position += Vector3.up * (floorY - bounds.min.y);
            if (!TryGetBounds(root, out bounds)) return;
            var chestY = ResolveBoneY(animator, root, HumanBodyBones.Chest, HumanBodyBones.UpperChest, "Spine02", "Chest", "UpperChest");
            if (float.IsNaN(chestY)) chestY = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.64f);
            var chestAboveFloor = Mathf.Max(0.25f, chestY - bounds.min.y);
            var calibratedScale = Mathf.Clamp((tableTopY + tuning.chestToTableOffset - floorY) / chestAboveFloor, 0.45f, 4.5f);
            root.localScale *= calibratedScale * tuning.globalScale * seat.scale;
            if (animator != null && animator.enabled) animator.Update(0f);
            if (TryGetBounds(root, out bounds)) root.position += Vector3.up * (floorY - bounds.min.y);
        }

        private static float FindTableTopY()
        {
            var table = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name.StartsWith("tripo_convert_536e6678-3785-43f8-b864-087a847da18f", StringComparison.Ordinal));
            return table != null && TryGetBounds(table, out var bounds) ? bounds.max.y : 2.14f;
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(root.position, Vector3.zero);
                return false;
            }
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return true;
        }

        private static float ResolveBoneY(Animator animator, Transform root, HumanBodyBones preferred, HumanBodyBones alternate, params string[] fallbackNames)
        {
            if (animator != null && animator.isHuman)
            {
                var bone = animator.GetBoneTransform(preferred) ?? animator.GetBoneTransform(alternate);
                if (bone != null) return bone.position.y;
            }
            var fallback = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => fallbackNames.Any(name => item.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || item.name.EndsWith(name, StringComparison.OrdinalIgnoreCase)));
            return fallback != null ? fallback.position.y : float.NaN;
        }

        private static Transform FindAnchor(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child.Find("AvatarAnchor") ?? child;
            return null;
        }

        private static AvatarLayoutSettings LoadSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AvatarLayoutSettings>("Assets/Resources/AvatarLayoutSettings.asset");
            return asset != null ? asset : AvatarLayoutSettings.LoadOrCreateRuntimeDefault();
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!visible || Application.isPlaying) return;
            foreach (var pair in previews)
            {
                var instance = pair.Value;
                if (instance == null || !anchors.TryGetValue(pair.Key, out var anchor) || anchor == null) continue;
                var layout = settings.ForSeat(pair.Key);
                var baseRotation = FaceTable(anchor.position, FindTableCenter());
                EditorGUI.BeginChangeCheck();
                var nextPosition = Handles.PositionHandle(instance.transform.position, instance.transform.rotation);
                var nextRotation = Handles.RotationHandle(instance.transform.rotation, instance.transform.position);
                var nextScale = Handles.ScaleHandle(instance.transform.localScale, instance.transform.position, instance.transform.rotation, HandleUtility.GetHandleSize(instance.transform.position));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(settings, "调整坐姿人物预览");
                    layout.positionOffset = nextPosition - anchor.position;
                    layout.yawOffset = Mathf.DeltaAngle(0f, nextRotation.eulerAngles.y - baseRotation.eulerAngles.y);
                    var baseScale = sourceScales.TryGetValue(pair.Key, out var source) ? source.x : 1f;
                    layout.scale = Mathf.Clamp(nextScale.x / Mathf.Max(0.01f, baseScale * settings.globalScale), 0.65f, 1.65f);
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    instance.transform.SetPositionAndRotation(nextPosition, nextRotation);
                    instance.transform.localScale = nextScale;
                }
                Handles.Label(instance.transform.position + Vector3.up * 2.1f, $"{SeatLabel(pair.Key)} · 坐姿预览");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) Clear();
        }

        private static string SeatLabel(PlayerSeat seat) => seat switch
        {
            PlayerSeat.East => "东家",
            PlayerSeat.North => "北家",
            PlayerSeat.West => "西家",
            _ => "南家",
        };

        private static Vector3 FindTableCenter()
        {
            var table = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name == "Table_夯土探方牌桌"
                    || item.name.StartsWith("tripo_convert_536e6678-3785-43f8-b864-087a847da18f", StringComparison.Ordinal));
            return table != null ? table.position : Vector3.zero;
        }

        private static Quaternion FaceTable(Vector3 position, Vector3 tableCenter)
        {
            var direction = Vector3.ProjectOnPlane(tableCenter - position, Vector3.up);
            return direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;
        }
    }
}
