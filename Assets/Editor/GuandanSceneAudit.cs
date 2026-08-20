using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Guandan.Game;
using Guandan.Scene;

namespace Guandan.EditorTools
{
    /// <summary>Read-only scene audit used to bind runtime gameplay to a user-authored scene.</summary>
    public static class GuandanSceneAudit
    {
        private static bool runtimeBatchMode;
        private static string runtimeBatchResultPath;
        [MenuItem("掼蛋/审计当前墓室场景")]
        public static void AuditCurrentScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var report = new StringBuilder();
            report.AppendLine($"[GuandanAudit] Scene={scene.path} roots={scene.rootCount}");
            foreach (var root in scene.GetRootGameObjects())
            {
                AuditTransform(root.transform, 0, report);
            }

            var clips = AssetDatabase.FindAssets("t:Model", new[] { "Assets/中国风墓穴考古/1", "Assets/中国风墓穴考古/2", "Assets/中国风墓穴考古/3", "Assets/中国风墓穴考古/4", "Assets/中国风墓穴考古/5", "Assets/中国风墓穴考古/f4" });
            foreach (var guid in clips)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animationClips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
                report.AppendLine($"[GuandanAudit][Asset] {path} model={(model != null ? model.name : "null")} clips={string.Join(",", animationClips.Select(item => item.name))}");
            }
            var outputPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GuandanSceneAudit.txt");
            File.WriteAllText(outputPath, report.ToString());
            Debug.Log($"[GuandanAudit] Wrote {outputPath} ({report.Length} chars)");
        }

        [MenuItem("掼蛋/验证运行时交互")]
        public static async void ValidateRuntimeInteractions()
        {
            Require(Application.isPlaying, "必须先进入 Play 模式再验证运行时交互");
            var props = UnityEngine.Object.FindObjectsByType<SocialProp>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Require(props.Length == 4, $"篮内应绑定 4 个道具，实际为 {props.Length}");
            Require(props.Count(item => item.PropType == SocialPropType.Tomato) == 2, "应只绑定 2 个篮内番茄");
            Require(props.Count(item => item.PropType == SocialPropType.Flower) == 2, "应只绑定 2 个篮内鲜花");
            Require(props.All(item => !item.name.Contains("f4", StringComparison.OrdinalIgnoreCase)), "f4 箱子不应成为社交道具");
            var pickupProbe = props.First();
            var pickupHome = pickupProbe.transform.position;
            pickupProbe.Interact(PointerSource.Desktop);
            Require(!pickupProbe.ReadyToThrow, "道具刚拿起时不应直接允许命中人物");
            pickupProbe.DragTo(pickupHome + Vector3.up * 0.85f);
            Require(!pickupProbe.ReadyToThrow, "道具还在提篮动画中，不应提前允许命中人物");
            await Task.Delay(520);
            Require(pickupProbe.ReadyToThrow, "道具完成提篮动画后没有进入可投掷状态");
            pickupProbe.ReturnHome();

            var director = UnityEngine.Object.FindAnyObjectByType<GameDirector>();
            Require(director != null, "运行时 GameDirector 不存在");
            var validateGeneratedActors = director.SpawnRuntimeAvatars;
            var motions = UnityEngine.Object.FindObjectsByType<SeatedAvatarMotion>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var authoredPresentations = UnityEngine.Object.FindObjectsByType<AuthoredAvatarPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (validateGeneratedActors)
                Require(motions.Length == 3, $"应有 3 个运行时 AI 人物，实际为 {motions.Length}");
            else
                Require(authoredPresentations.Length == 3, $"应绑定 3 个用户摆放人物，实际为 {authoredPresentations.Length}");
            var table = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name.StartsWith("tripo_convert_536e6678-3785-43f8-b864-087a847da18f", StringComparison.Ordinal));
            var tableCenter = table != null ? table.position : Vector3.zero;
            var tableRenderers = table != null ? table.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            var tableBounds = tableRenderers.Length > 0 ? tableRenderers[0].bounds : new Bounds(tableCenter, Vector3.one * 2f);
            for (var index = 1; index < tableRenderers.Length; index++) tableBounds.Encapsulate(tableRenderers[index].bounds);
            foreach (var motion in validateGeneratedActors ? motions : Array.Empty<SeatedAvatarMotion>())
            {
                var animator = motion.GetComponentsInChildren<Animator>(true)
                    .FirstOrDefault(item => item != null && item.enabled && item.runtimeAnimatorController != null)
                    ?? motion.GetComponentInChildren<Animator>(true);
                Require(animator != null && animator.enabled && animator.runtimeAnimatorController != null,
                    $"{motion.name} 的坐姿 Animator 未运行");
                Require(animator.runtimeAnimatorController.animationClips.Any(item => item.name.Contains("sit", StringComparison.OrdinalIgnoreCase)),
                    $"{motion.name} 当前控制器没有坐姿片段");
                var direction = Vector3.ProjectOnPlane(tableCenter - motion.transform.position, Vector3.up).normalized;
                Require(Vector3.Dot(-motion.transform.forward, direction) > 0.92f, $"{motion.name} 没有朝向牌桌");
                var renderers = motion.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0, $"{motion.name} 没有可见模型");
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                var chest = animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    : motion.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                        item.name.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                        || item.name.EndsWith("Spine02", StringComparison.OrdinalIgnoreCase));
                var chestY = chest != null ? chest.position.y : Mathf.Lerp(bounds.min.y, bounds.max.y, 0.64f);
                Require(Mathf.Abs(chestY - tableBounds.max.y) <= 0.24f,
                    $"{motion.name} 胸口没有对齐桌面：chest={chestY:F2}, table={tableBounds.max.y:F2}");
                foreach (var renderer in renderers)
                {
                    Require(renderer.reflectionProbeUsage == UnityEngine.Rendering.ReflectionProbeUsage.Off,
                        $"{motion.name} 仍在使用反射探针");
                    foreach (var material in renderer.materials)
                    {
                        if (material.HasProperty("_Metallic")) Require(material.GetFloat("_Metallic") <= 0.02f, $"{motion.name} 材质金属度过高");
                        if (material.HasProperty("_Smoothness")) Require(material.GetFloat("_Smoothness") <= 0.18f, $"{motion.name} 材质过于反光");
                    }
                }
            }

            foreach (var presentation in validateGeneratedActors ? Array.Empty<AuthoredAvatarPresentation>() : authoredPresentations)
            {
                var animator = presentation.ActiveAnimator
                    ?? presentation.GetComponentsInChildren<Animator>(true)
                        .FirstOrDefault(item => item != null && item.enabled && item.runtimeAnimatorController != null);
                Require(animator != null && animator.enabled && animator.runtimeAnimatorController != null,
                    $"{presentation.name} 的坐姿 Animator 未运行");
                Require(!animator.applyRootMotion, $"{presentation.name} 不应启用 Root Motion");
                Require(animator.runtimeAnimatorController.animationClips.Any(item => item.name.Contains("sit", StringComparison.OrdinalIgnoreCase)),
                    $"{presentation.name} 当前控制器没有坐姿片段");
            }

            var chairs = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item.name.StartsWith("Chair_", StringComparison.Ordinal) && item.name.Contains("f4", StringComparison.Ordinal))
                .ToArray();
            if (validateGeneratedActors)
                Require(chairs.Length == 3, $"应有 3 个 f4 凳子，实际为 {chairs.Length}");
            foreach (var chair in validateGeneratedActors ? chairs : Array.Empty<Transform>())
            {
                var renderers = chair.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0, $"{chair.name} 没有可见模型");
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                Require(Mathf.Abs(bounds.min.y) < 0.04f, $"{chair.name} 没有落到地面");
                var seatName = chair.name.Contains("东家", StringComparison.Ordinal) ? "东家"
                    : chair.name.Contains("北家", StringComparison.Ordinal) ? "北家" : "西家";
                var avatar = motions.FirstOrDefault(item => item.name.Contains(seatName, StringComparison.Ordinal));
                var avatarAnimator = avatar != null
                    ? avatar.GetComponentsInChildren<Animator>(true)
                        .FirstOrDefault(item => item != null && item.enabled && item.runtimeAnimatorController != null)
                    : null;
                var hips = avatarAnimator != null && avatarAnimator.isHuman
                    ? avatarAnimator.GetBoneTransform(HumanBodyBones.Hips)
                    : avatar != null
                        ? avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(item =>
                            item.name.Contains("Hips", StringComparison.OrdinalIgnoreCase)
                            || item.name.EndsWith("Pelvis", StringComparison.OrdinalIgnoreCase))
                        : null;
                Require(hips == null || Mathf.Abs(hips.position.y - bounds.max.y) <= 0.30f,
                    $"{chair.name} 箱顶没有贴近人物臀部");
            }

            var cameraRig = UnityEngine.Object.FindAnyObjectByType<Guandan.XR.GuandanXRBootstrap>();
            var headCamera = Camera.main;
            Require(cameraRig != null && headCamera != null, "玩家头部相机不存在");
            var treasurePresenter = UnityEngine.Object.FindAnyObjectByType<TreasureScenePresenter>();
            Require(treasurePresenter != null, "夺宝场景表现组件不存在");
            ValidateTreasurePieceBases(treasurePresenter);
            cameraRig.RecenterDesktop();
            var cameraStart = headCamera.transform.position;
            Require(Vector3.Distance(cameraStart, new Vector3(0f, 3.12f, -5.17f)) < 0.03f,
                $"初始相机没有回到参考图机位：{cameraStart}");
            var yawField = typeof(Guandan.XR.GuandanXRBootstrap).GetField("desktopYaw", BindingFlags.Instance | BindingFlags.NonPublic);
            var applyCamera = typeof(Guandan.XR.GuandanXRBootstrap).GetMethod("ApplyDesktopCamera", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(yawField != null && applyCamera != null, "无法验证桌面原地转头逻辑");
            var rotationStart = headCamera.transform.rotation;
            yawField.SetValue(cameraRig, 24f);
            applyCamera.Invoke(cameraRig, null);
            Require(Vector3.Distance(cameraStart, headCamera.transform.position) < 0.001f, "转头时相机发生了绕场景公转");
            Require(Quaternion.Angle(rotationStart, headCamera.transform.rotation) > 20f, "转头没有改变玩家视线");
            cameraRig.RecenterDesktop();

            director.UseSocialProp(SocialPropType.Tomato, PlayerSeat.East);
            director.UseSocialProp(SocialPropType.Flower, PlayerSeat.West);
            var gameplayBindings = typeof(GameDirector).GetField("gameplayBindings", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(director) as RuntimeGameplayBindings;
            Require(gameplayBindings != null, "运行时人物绑定组件不存在");
            gameplayBindings.PlaySocialReaction(PlayerSeat.East, SocialPropType.Tomato);
            await Task.Delay(120);
            var sauce = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item != null && item.name == "Reaction · 番茄酱");
            Require(sauce != null, "番茄酱反馈对象没有生成");
            var cameraDirection = Vector3.ProjectOnPlane(headCamera.transform.position - sauce.position, Vector3.up).normalized;
            Require(cameraDirection.sqrMagnitude < 0.0001f || Vector3.Dot(sauce.forward, cameraDirection) > 0.94f,
                "番茄酱正面没有朝向当前摄像机");
            var bell = UnityEngine.Object.FindAnyObjectByType<BellReminder>();
            Require(bell != null, "没有绑定场景中用户摆放的真实吊钟");
            bell.Interact(PointerSource.Desktop);
            Debug.Log(validateGeneratedActors
                ? "[GuandanRuntime] 运行时交互验收通过：2 番茄、2 鲜花、3 动态坐姿人物、3 个 f4 凳子、番茄酱/花环/钟声反馈均已触发。"
                : "[GuandanRuntime] 运行时交互验收通过：当前为用户自行摆放人物/箱子模式；道具提篮、相机原地转头与真实吊钟均已验证。");
        }


        public static void ValidateRuntimeBatch()
        {
            // Play-mode entry is intentionally editor-only. Batchmode cannot advance a
            // frame after EnterPlaymode, so report that limitation instead of leaving an
            // apparently successful command with no result file.
            runtimeBatchResultPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GuandanRuntimeValidation.txt");
            File.WriteAllText(runtimeBatchResultPath,
                Application.isBatchMode
                    ? "SKIPPED: Unity batchmode cannot advance Play mode. Use 掼蛋/验证运行时交互 while the editor is playing.\n"
                    : "READY: Enter Play mode to run the authored-avatar validation.\n");
            Debug.Log(File.ReadAllText(runtimeBatchResultPath));
            if (Application.isBatchMode) return;
            runtimeBatchMode = true;
            EditorSceneManager.OpenScene("Assets/Scenes/GuandanTomb.unity", OpenSceneMode.Single);
            EditorApplication.playModeStateChanged += RuntimeBatchStateChanged;
            EditorApplication.EnterPlaymode();
        }

        private static async void RuntimeBatchStateChanged(PlayModeStateChange state)
        {
            if (!runtimeBatchMode || state != PlayModeStateChange.EnteredPlayMode) return;
            try
            {
                await Task.Delay(1800);
                var authored = UnityEngine.Object.FindObjectsByType<AuthoredAvatarPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Require(authored.Length == 3, $"应绑定 3 个用户摆放人物，实际为 {authored.Length}");
                var expected = new Dictionary<int, (Vector3 pos, Vector3 rot, Vector3 scale)>
                {
                    [1] = (new Vector3(3.518f, 2.252f, -0.177f), Vector3.zero, Vector3.one * 4.3f),
                    [2] = (new Vector3(0.048f, 0.04f, 3.565f), new Vector3(0f, 270f, 0f), Vector3.one * 4.3f),
                    [4] = (new Vector3(-3.243f, 2.133f, 0.342f), new Vector3(0f, 180f, 0f), Vector3.one * 4.3f),
                };
                foreach (var presentation in authored)
                {
                    Require(expected.TryGetValue(presentation.AvatarIndex, out var baseline), $"未预期的人物编号 {presentation.AvatarIndex}");
                    Require(Vector3.Distance(presentation.transform.position, baseline.pos) < 0.002f, $"Avatar_{presentation.AvatarIndex} 位置被修改");
                    Require(Quaternion.Angle(presentation.transform.rotation, Quaternion.Euler(baseline.rot)) < 0.1f, $"Avatar_{presentation.AvatarIndex} 方向被修改");
                    Require(Vector3.Distance(presentation.transform.localScale, baseline.scale) < 0.002f, $"Avatar_{presentation.AvatarIndex} 大小被修改");
                    var animator = presentation.GetComponentsInChildren<Animator>(true)
                        .FirstOrDefault(item => item != null && item.enabled && item.runtimeAnimatorController != null)
                        ?? presentation.GetComponentInChildren<Animator>(true);
                    Require(animator != null && animator.enabled && animator.runtimeAnimatorController != null, $"Avatar_{presentation.AvatarIndex} 动画未运行");
                    Require(!animator.applyRootMotion, $"Avatar_{presentation.AvatarIndex} 不应启用 Root Motion");
                    Require(animator.runtimeAnimatorController.animationClips.Any(clip => clip.name.Contains("sit", StringComparison.OrdinalIgnoreCase)), $"Avatar_{presentation.AvatarIndex} 没有坐姿动画");
                    foreach (var renderer in presentation.GetComponentsInChildren<Renderer>(true))
                    {
                        Require(renderer.reflectionProbeUsage == UnityEngine.Rendering.ReflectionProbeUsage.Off, $"Avatar_{presentation.AvatarIndex} 仍使用反射探针");
                        foreach (var material in renderer.materials)
                        {
                            if (material.HasProperty("_Metallic")) Require(material.GetFloat("_Metallic") <= 0.02f, $"Avatar_{presentation.AvatarIndex} 金属度过高");
                            if (material.HasProperty("_Smoothness")) Require(material.GetFloat("_Smoothness") <= 0.15f, $"Avatar_{presentation.AvatarIndex} 光滑度过高");
                        }
                    }
                }
                File.WriteAllText(runtimeBatchResultPath, "PASS: 3 authored avatars preserve position/rotation/scale, loop sitting animation, and matte materials.\n");
            }
            catch (Exception exception)
            {
                File.WriteAllText(runtimeBatchResultPath, "FAIL: " + exception + "\n");
                Debug.LogException(exception);
            }
            finally
            {
                runtimeBatchMode = false;
                EditorApplication.playModeStateChanged -= RuntimeBatchStateChanged;
                EditorApplication.ExitPlaymode();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void ValidateTreasurePieceBases(TreasureScenePresenter presenter)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var pieces = typeof(TreasureScenePresenter).GetField("pieces", flags)?.GetValue(presenter) as Transform[];
            var tileTops = typeof(TreasureScenePresenter).GetField("trackTileTopY", flags)?.GetValue(presenter) as float[][];
            Require(pieces != null && pieces.Length == 2, "夺宝棋子没有绑定两支队伍");
            Require(tileTops != null && tileTops.Length == 2, "夺宝地砖顶部高度没有解析");
            for (var team = 0; team < 2; team++)
            {
                Require(pieces[team] != null, $"第 {team + 1} 支夺宝棋子不存在");
                Require(tileTops[team] != null && tileTops[team].Length >= 13, $"第 {team + 1} 条夺宝路线不是 12 格");
                Require(!float.IsNaN(tileTops[team][0]), $"第 {team + 1} 条路线未识别起始地砖");
                var renderers = pieces[team].GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0, $"第 {team + 1} 支夺宝棋子没有可见模型");
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
                var gap = bounds.min.y - tileTops[team][0];
                Require(Mathf.Abs(gap - 0.012f) <= 0.035f,
                    $"第 {team + 1} 支棋子没有落在地砖上：底部与地砖顶部差 {gap:F3}m");
            }
        }

        private static void AuditTransform(Transform item, int depth, StringBuilder report)
        {
            if (item == null) return;
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length == 0 ? new Bounds(item.position, Vector3.zero) : renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var components = string.Join(",", item.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().Name));
            var source = PrefabUtility.GetCorrespondingObjectFromSource(item.gameObject);
            var sourcePath = source == null ? string.Empty : AssetDatabase.GetAssetPath(source);
            report.AppendLine($"[GuandanAudit][{depth}] {item.name} pos={item.position} scale={item.lossyScale} boundsMin={bounds.min} boundsMax={bounds.max} comps={components} source={sourcePath}");
            foreach (Transform child in item) AuditTransform(child, depth + 1, report);
        }
    }
}
