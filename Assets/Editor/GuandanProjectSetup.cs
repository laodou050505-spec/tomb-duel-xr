using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using Guandan.Game;
using Guandan.Scene;
using Guandan.Title;
using Guandan.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

namespace Guandan.Editor
{
    public static class GuandanProjectSetup
    {
        public const string TitleScenePath = "Assets/Scenes/GuandanTitle.unity";
        public const string ScenePath = "Assets/Scenes/GuandanTomb.unity";
        private const string RootName = "掼蛋夺宝 · 可编辑墓室";

        [MenuItem("掼蛋/一键创建或重建可编辑墓穴场景")]
        public static void CreateEditableTombScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.18f, 0.14f, 0.10f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.12f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.025f, 0.02f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.08f, 0.055f, 0.035f);
            RenderSettings.fogDensity = 0.012f;

            var root = new GameObject(RootName);
            var environment = new GameObject("01_Environment · 可自由替换模型");
            environment.transform.SetParent(root.transform, false);
            var builder = environment.AddComponent<TombRoomBuilder>();
            builder.rebuildOnValidate = false;
            builder.Rebuild();
            EditorUtility.SetDirty(builder);

            var gameplay = new GameObject("02_Gameplay · 玩法与锚点");
            gameplay.transform.SetParent(root.transform, false);
            var director = gameplay.AddComponent<GameDirector>();
            director.BuildSceneAnchorsForEditor();

            var xr = new GameObject("03_XR Rig · PICO与桌面共用");
            xr.transform.SetParent(root.transform, false);
            xr.AddComponent<ByteDance.PICO.XR.PXR_Manager>();
            var bootstrap = xr.AddComponent<GuandanXRBootstrap>();
            xr.AddComponent<DesktopPointer>();
            bootstrap.BuildSceneRigForEditor();

            var props = new GameObject("04_Props · 鲜花与西红柿");
            props.transform.SetParent(root.transform, false);
            CreateSocialProp(props.transform, "Flower_鲜花", SocialPropType.Flower, new Vector3(3.35f, 1.82f, -4f), new Color(0.82f, 0.24f, 0.43f));
            CreateSocialProp(props.transform, "Tomato_西红柿", SocialPropType.Tomato, new Vector3(-3.35f, 1.82f, -4f), new Color(0.82f, 0.12f, 0.08f));

            var guide = new GameObject("README · 模型替换说明");
            guide.transform.SetParent(root.transform, false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildScenes();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = root;
            Debug.Log($"[Guandan] 可编辑场景已保存：{ScenePath}。Environment 内物件均可直接替换。\n");
        }

        [MenuItem("掼蛋/创建或更新独立启动场景")]
        public static void CreateTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.050f, 0.045f);
            RenderSettings.fog = false;

            var root = new GameObject("00_TitleStage · 独立不透明封面");
            root.AddComponent<TombTitleBootstrap>();
            EditorSceneManager.SaveScene(scene, TitleScenePath);
            EnsureBuildScenes();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = root;
            Debug.Log($"[Guandan] 独立启动场景已保存：{TitleScenePath}。它不加载任何 GameplayRoot，START 会 Single 模式切入 {ScenePath}。\n");
        }

        [MenuItem("掼蛋/配置 PICO Android 项目")]
        public static void ConfigurePicoProject()
        {
            PlayerSettings.companyName = "GuandanTomb";
            PlayerSettings.productName = "地宫争锋";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.guandan.tombxr");
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 1000;
            // The PICO runtime owns the stereoscopic display.  Keeping the Android
            // activity landscape prevents the emulator from creating a portrait
            // fallback panel if it needs to present the Unity surface before XR starts.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.minifyRelease = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            SetInputHandler(1);
            ApplyPicoIcon.Run();

            var pxrSettings = AssetDatabase.LoadAssetAtPath<PXR_Settings>("Assets/XR/Settings/PXR_Settings.asset");
            if (pxrSettings != null)
            {
                pxrSettings.appMode = PXR_Settings.AppMode.XR;
                pxrSettings.stereoRenderingModeAndroid = PXR_Settings.StereoRenderingModeAndroid.Multiview;
                EditorUtility.SetDirty(pxrSettings);
            }

            var settingsStore = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                .Select(guid => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(item => item != null);
            if (settingsStore == null)
            {
                settingsStore = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settingsStore, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settingsStore, true);
            }
            if (!settingsStore.HasSettingsForBuildTarget(BuildTargetGroup.Android)) settingsStore.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!settingsStore.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) settingsStore.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var manager = settingsStore.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            XRPackageMetadataStore.AssignLoader(manager, "ByteDance.PICO.XR.PXR_Loader", BuildTargetGroup.Android);
            var generalSettings = settingsStore.SettingsForBuildTarget(BuildTargetGroup.Android);
            generalSettings.InitManagerOnStart = true;
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(settingsStore);

            AssetDatabase.SaveAssets();
            Debug.Log("[Guandan] PICO 配置完成：Android / ARM64 / IL2CPP / Activity / Multiview / PXR Loader / 应用图标。\n");
        }

        [MenuItem("掼蛋/验证项目")]
        public static void VerifyProject()
        {
            var failures = new List<string>();
            EnsureTitleSceneExists();
            if (!File.Exists(TitleScenePath)) failures.Add("缺少独立启动场景 GuandanTitle.unity");
            if (!File.Exists(ScenePath)) failures.Add("缺少 GuandanTomb.unity");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == TitleScenePath)) failures.Add("启动场景未加入 Build Settings");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == ScenePath)) failures.Add("场景未加入 Build Settings");
            var sceneText = File.Exists(ScenePath) ? File.ReadAllText(ScenePath) : string.Empty;
            if (!sceneText.Contains("useScreenUi: 1", StringComparison.Ordinal)) failures.Add("场景未启用世界空间平面牌局 UI");
            var titleText = File.Exists(TitleScenePath) ? File.ReadAllText(TitleScenePath) : string.Empty;
            if (titleText.Contains("GameDirector", StringComparison.Ordinal)
                || titleText.Contains("TombRoomBuilder", StringComparison.Ordinal)
                || titleText.Contains("TreasureScenePresenter", StringComparison.Ordinal))
                failures.Add("标题场景混入了 GameplayRoot/玩法组件");
            var manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
            if (!File.Exists(manifestPath)) failures.Add("缺少 PICO AndroidManifest.xml");
            else
            {
                var manifest = File.ReadAllText(manifestPath);
                if (!manifest.Contains("pvr.app.type\" android:value=\"vr", StringComparison.Ordinal)) failures.Add("AndroidManifest 未声明 pvr.app.type=vr");
                if (!manifest.Contains("use.pxr.sdk", StringComparison.Ordinal)) failures.Add("AndroidManifest 未声明 use.pxr.sdk");
            }
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64) failures.Add("Android 架构不是 ARM64");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP) failures.Add("Android 未使用 IL2CPP");
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel29) failures.Add("最低 Android API 低于 29");
            if (ReadInputHandler() != 1) failures.Add("输入后端不是 Input System-only");
            var pxrSettings = AssetDatabase.LoadAssetAtPath<PXR_Settings>("Assets/XR/Settings/PXR_Settings.asset");
            if (pxrSettings == null) failures.Add("缺少 PXR_Settings");
            else if (pxrSettings.stereoRenderingModeAndroid != PXR_Settings.StereoRenderingModeAndroid.Multiview) failures.Add("PICO 未设置 Multiview");
            var settingsStore = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                .Select(guid => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(item => item != null);
            var loaderSettings = settingsStore?.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (loaderSettings?.AssignedSettings == null || loaderSettings.AssignedSettings.activeLoaders.All(loader => loader is not PXR_Loader))
                failures.Add("Android 未分配 PXR Loader");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<GuandanXRBootstrap>();
            if (bootstrap == null) failures.Add("场景缺少桌面/XR 相机控制器 GuandanXRBootstrap");
            var director = UnityEngine.Object.FindFirstObjectByType<GameDirector>();
            if (director == null) failures.Add("场景缺少地宫牌局的 GameDirector");
            if (failures.Count > 0) throw new BuildFailedException("掼蛋项目验证失败：" + string.Join("；", failures));
            Debug.Log("[Guandan] 项目验证通过：世界空间平面牌局 UI、PICO Loader、Multiview、ARM64、IL2CPP 与最低 API 均已配置。\n");
        }

        public static void SetupAndVerify()
        {
            CreateTitleScene();
            CreateEditableTombScene();
            ConfigurePicoProject();
            VerifyProject();
        }

        [MenuItem("掼蛋/构建/macOS 桌面测试包")]
        public static void BuildMacPlayer()
        {
            EnsureTitleSceneExists();
            Directory.CreateDirectory("Builds/macOS");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { TitleScenePath, ScenePath },
                target = BuildTarget.StandaloneOSX,
                locationPathName = "Builds/macOS/掼蛋.app",
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("macOS 测试包构建失败");
        }

        [MenuItem("掼蛋/构建/PICO Android APK")]
        public static void BuildPicoApk()
        {
            // The title and existing world-space card table ship together in one APK.
            BuildPicoNativeScreenUiApk();
        }

        [MenuItem("掼蛋/构建/PICO 原生VR平面牌UI APK")]
        public static void BuildPicoNativeScreenUiApk()
        {
            BuildPicoFlatUiApkAtPath("Builds/Android/地宫争锋-PICO.apk");
        }

        private static void BuildPicoFlatUiApkAtPath(string outputPath)
        {
            EnsureTitleSceneExists();
            ConfigurePicoProject();
            VerifyPicoFlatUiBuild();
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("无法切换到 Android 构建目标");
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
            Directory.CreateDirectory("Builds/Android");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                // Enter Tomb unloads the title and opens the unchanged authored card table.
                scenes = new[] { TitleScenePath, ScenePath },
                target = BuildTarget.Android,
                locationPathName = outputPath,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("PICO APK 构建失败");
        }

        private static void VerifyPicoFlatUiBuild()
        {
            var failures = new List<string>();
            if (!File.Exists(ScenePath)) failures.Add("缺少 GuandanTomb.unity");
            var sceneText = File.Exists(ScenePath) ? File.ReadAllText(ScenePath) : string.Empty;
            if (!sceneText.Contains("useScreenUi: 1", StringComparison.Ordinal)) failures.Add("场景未启用世界空间平面牌局 UI");
            if (!sceneText.Contains("Guandan.Game.GameDirector", StringComparison.Ordinal)) failures.Add("场景缺少地宫牌局的 GameDirector");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP) failures.Add("Android 未使用 IL2CPP");
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0) failures.Add("Android 未启用 ARM64");
            if (failures.Count > 0) throw new BuildFailedException("PICO 平面牌 UI 构建验证失败：" + string.Join("；", failures));
            Debug.Log("[Guandan] PICO 世界空间平面牌 UI 构建验证通过：原生 VR 将直接进入地宫空间抽签面板。\n");
        }

        private static void EnsureTitleSceneExists()
        {
            if (!File.Exists(TitleScenePath)) CreateTitleScene();
            else EnsureBuildScenes();
        }

        private static void EnsureBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(TitleScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
        }

        private static void CreateSocialProp(Transform parent, string name, SocialPropType type, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(type == SocialPropType.Flower ? PrimitiveType.Sphere : PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = type == SocialPropType.Flower ? new Vector3(0.35f, 0.22f, 0.35f) : Vector3.one * 0.28f;
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
            go.AddComponent<SocialProp>().Configure(type);
        }

        private static void SetInputHandler(int value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets.Length == 0) throw new InvalidOperationException("无法读取 Input System 项目设置");
            var serialized = new SerializedObject(assets[0]);
            var property = serialized.FindProperty("activeInputHandler") ?? throw new InvalidOperationException("缺少 activeInputHandler 设置");
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int ReadInputHandler()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            return assets.Length == 0 ? -1 : new SerializedObject(assets[0]).FindProperty("activeInputHandler")?.intValue ?? -1;
        }
    }

}
