using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guandan.EditorTools
{
    /// <summary>
    /// Removes only authored character/chair prefab instances from the active tomb scene.
    /// The source assets and the four AvatarAnchor objects remain available for manual placement.
    /// </summary>
    public static class GuandanSceneCleanup
    {
        private const string F4Guid = "e68b3638d6bd348fe816571f431922b2";
        private const string ScenePath = "Assets/Scenes/GuandanTomb.unity";
        private static readonly string[] AvatarFolders =
        {
            "Assets/中国风墓穴考古/1/",
            "Assets/中国风墓穴考古/2/",
            "Assets/中国风墓穴考古/3/",
            "Assets/中国风墓穴考古/4/",
            "Assets/中国风墓穴考古/5/",
        };

        [MenuItem("掼蛋/移除场景人物与 f4 箱子")]
        public static void RemoveSceneActors()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Guandan] 请先退出 Play 模式，再移除场景人物与 f4 箱子。");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            RemoveSceneActorsFromScene(scene);
        }

        public static void RemoveSceneActorsBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveSceneActorsFromScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static void RemoveSceneActorsFromScene(UnityEngine.SceneManagement.Scene scene)
        {
            AvatarScenePreview.Clear();
            var rootsToRemove = new HashSet<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(transform.gameObject);
                    if (instanceRoot == null) continue;
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
                    if (source == null) continue;
                    var path = AssetDatabase.GetAssetPath(source).Replace('\\', '/');
                    var isF4 = AssetDatabase.AssetPathToGUID(path) == F4Guid;
                    var isAvatar = AvatarFolders.Any(path.StartsWith);
                    if (isF4 || isAvatar) rootsToRemove.Add(instanceRoot);
                }
            }

            foreach (var root in rootsToRemove)
                Undo.DestroyObjectImmediate(root);

            if (rootsToRemove.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"[Guandan] 已移除场景人物与 f4 箱子：{rootsToRemove.Count} 个实例；座位锚点和人物资产均已保留。运行时默认不再自动生成人物/箱子。");
        }
    }
}
