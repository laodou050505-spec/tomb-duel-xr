using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Guandan.EditorTools
{
    /// <summary>Creates runtime-safe prefab references from the user's imported FBX assets.</summary>
    public static class GuandanRuntimeAssetSetup
    {
        private const string OutputFolder = "Assets/Resources/GuandanAvatars";

        [MenuItem("掼蛋/生成运行时人物预制体")]
        public static void BuildRuntimeAssets()
        {
            Directory.CreateDirectory(OutputFolder);
            for (var i = 1; i <= 5; i++)
            {
                var modelPath = AssetDatabase.FindAssets("t:Model", new[] { $"Assets/中国风墓穴考古/{i}" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(path => path.EndsWith(".fbx"));
                if (string.IsNullOrEmpty(modelPath))
                {
                    Debug.LogWarning($"[Guandan] Missing avatar model {i}");
                    continue;
                }
                ConfigureImportedSitLoop(modelPath);
                var outputPath = $"{OutputFolder}/Avatar_{i}.prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                CreateAvatarPrefab(source, modelPath, outputPath, i);
            }

            var chairPath = AssetDatabase.FindAssets("t:Model", new[] { "Assets/中国风墓穴考古/f4" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path => path.EndsWith(".fbx"));
            if (!string.IsNullOrEmpty(chairPath))
                PrefabUtility.SaveAsPrefabAsset(AssetDatabase.LoadAssetAtPath<GameObject>(chairPath), $"{OutputFolder}/Chair_f4.prefab");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Guandan] Runtime prefabs ready in {OutputFolder}");
        }

        private static void ConfigureImportedSitLoop(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return;
            // Runtime smoothing needs readable mesh data. This only affects generated
            // runtime prefabs; the authored Scene models remain untouched.
            var importerChanged = false;
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importerChanged = true;
            }
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                if (importerChanged) importer.SaveAndReimport();
                return;
            }
            var changed = false;
            for (var index = 0; index < clips.Length; index++)
            {
                if (!clips[index].name.Contains("preset:biped:sit", System.StringComparison.OrdinalIgnoreCase)) continue;
                var clip = clips[index];
                if (!clip.loopTime || !clip.loopPose || !clip.lockRootRotation || !clip.lockRootHeightY || !clip.lockRootPositionXZ)
                {
                    clip.loopTime = true;
                    clip.loopPose = true;
                    clip.lockRootRotation = true;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = true;
                    clips[index] = clip;
                    changed = true;
                }
            }
            if (changed || importerChanged)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static void CreateAvatarPrefab(GameObject source, string modelPath, string outputPath, int index)
        {
            var root = new GameObject($"Avatar_{index}");
            var model = Object.Instantiate(source);
            model.name = source.name;
            model.transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            Object.DestroyImmediate(root);
            ConfigureSittingController(outputPath, modelPath, index);
        }

        private static void ConfigureSittingController(string prefabPath, string modelPath, int index)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(item => item.name == "preset:biped:sit")
                ?? AssetDatabase.LoadAllAssetsAtPath(modelPath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(item => item.name.Contains("preset:biped:sit") && !item.name.StartsWith("__"));
            if (clip == null) return;
            var loopPath = $"{OutputFolder}/Avatar_{index}_SitLoop.anim";
            var loopClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(loopPath);
            if (loopClip == null)
            {
                loopClip = Object.Instantiate(clip);
                loopClip.name = $"Avatar_{index}_SitLoop";
                AssetDatabase.CreateAsset(loopClip, loopPath);
            }
            else
            {
                EditorUtility.CopySerialized(clip, loopClip);
                loopClip.name = $"Avatar_{index}_SitLoop";
            }
            loopClip.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(loopClip);
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(loopClip, settings);
            EditorUtility.SetDirty(loopClip);
            var controllerPath = $"{OutputFolder}/Avatar_{index}_Sit.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            var state = controller.layers[0].stateMachine.states
                .Select(item => item.state)
                .FirstOrDefault(item => item.name == "Sit");
            if (state == null) state = controller.layers[0].stateMachine.AddState("Sit");
            state.motion = loopClip;
            state.writeDefaultValues = true;

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            var armature = prefabRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name.Equals("Armature", System.StringComparison.OrdinalIgnoreCase));
            var animatorHost = armature != null && armature.parent != null ? armature.parent : prefabRoot.transform;
            var animator = animatorHost.GetComponent<Animator>() ?? animatorHost.gameObject.AddComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning($"[Guandan] Could not add Animator to Avatar_{index}");
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                return;
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
