using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guandan.EditorTools
{
    public static class GuandanAvatarAudit
    {
        private const string ScenePath = "Assets/Scenes/GuandanTomb.unity";

        public static void WriteBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var report = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects())
                Visit(root.transform, root.name, report);
            var authoredGuids = new[]
            {
                "7fddb7b1c127647fd8c30d7f818d7ed8",
                "27c460cdd1db740a8ab70f6d9a69116f",
                "1e94a5f00dbac4efd9cd81ca91f8d57b",
            };
            foreach (var root in scene.GetRootGameObjects())
            foreach (var instance in root.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(instance.gameObject)) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject);
                var path = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
                if (!authoredGuids.Contains(AssetDatabase.AssetPathToGUID(path))) continue;
                report.AppendLine($"AUTHORED name={instance.name} path={path} pos={instance.position} rot={instance.rotation.eulerAngles} scale={instance.localScale}");
            }
            foreach (var index in new[] { 1, 2, 3, 4, 5 })
            {
                var modelPath = AssetDatabase.FindAssets("t:Model", new[] { $"Assets/中国风墓穴考古/{index}" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(modelPath)) continue;
                var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                var clips = assets.OfType<AnimationClip>().ToArray();
                report.AppendLine($"ASSET model={modelPath} avatar={assets.OfType<Avatar>().Any()} clips={clips.Length}");
                foreach (var clip in clips)
                    report.AppendLine($"  assetClip={clip.name} len={clip.length:F3} wrap={clip.wrapMode} legacy={clip.legacy}");
            }
            var output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GuandanAvatarAudit.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, report.ToString());
            Debug.Log($"[GuandanAvatarAudit] {report.Length} chars written to {output}");
        }

        [MenuItem("掼蛋/审计当前人物动画与摆放")]
        public static void WriteCurrent()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var report = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects()) Visit(root.transform, root.name, report);
            var output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GuandanAvatarAudit.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, report.ToString());
            Debug.Log($"[GuandanAvatarAudit] {report.Length} chars written to {output}");
        }

        private static void Visit(Transform item, string path, StringBuilder report)
        {
            if (item == null) return;
            var nextPath = path + "/" + item.name;
            var animator = item.GetComponent<Animator>();
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            var source = PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject);
            var sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            if (animator != null || renderers.Length > 0 && (item.name.Contains("avatar", StringComparison.OrdinalIgnoreCase)
                || item.name.Contains("人物", StringComparison.Ordinal)
                || item.name.Contains("角色", StringComparison.Ordinal)
                || item.name.Contains("人", StringComparison.Ordinal)))
            {
                report.AppendLine($"GO path={nextPath}");
                report.AppendLine($"  pos={item.position} localPos={item.localPosition} rot={item.rotation.eulerAngles} localRot={item.localRotation.eulerAngles} scale={item.localScale} source={sourcePath}");
                if (animator != null)
                {
                    var controller = animator.runtimeAnimatorController;
                    report.AppendLine($"  animator controller={(controller != null ? controller.name : "null")} enabled={animator.enabled} rootMotion={animator.applyRootMotion} isHuman={animator.isHuman}");
                    if (controller != null)
                        foreach (var clip in controller.animationClips ?? Array.Empty<AnimationClip>())
                            report.AppendLine($"  clip={clip.name} len={clip.length:F3} wrap={clip.wrapMode} legacy={clip.legacy}");
                }
                report.AppendLine($"  renderers={renderers.Length}");
            }
            foreach (Transform child in item) Visit(child, nextPath, report);
        }
    }
}
