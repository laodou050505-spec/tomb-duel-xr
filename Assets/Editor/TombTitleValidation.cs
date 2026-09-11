using System;
using System.IO;
using Guandan.Title;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guandan.Editor
{
    public static class TombTitleValidation
    {
        private static double stageTime;
        private static int stage;
        public static void VerifyAndBuildMac()
        {
            ImportCover();
            GuandanProjectSetup.VerifyProject();
            GuandanRuleSmokeTests.Run();
            if (EditorBuildSettings.scenes[0].path != GuandanProjectSetup.TitleScenePath) throw new Exception("Title must be first.");
            GuandanProjectSetup.BuildMacPlayer();
        }
        public static void ImportCover()
        {
            foreach (var result in new[] { "Victory", "Defeat" })
            {
                var resultPath=$"Assets/Resources/GuandanUI/Result{result}.png";
                if (!File.Exists(resultPath)) continue;
                AssetDatabase.ImportAsset(resultPath, ImportAssetOptions.ForceSynchronousImport);
                var resultImporter=(TextureImporter)AssetImporter.GetAtPath(resultPath);
                resultImporter.textureType=TextureImporterType.Default;
                resultImporter.maxTextureSize=2048; resultImporter.mipmapEnabled=false;
                resultImporter.alphaIsTransparency=true;
                resultImporter.textureCompression=TextureImporterCompression.Uncompressed;
                resultImporter.wrapMode=TextureWrapMode.Clamp;
                resultImporter.SaveAndReimport();
            }
            const string path = "Assets/Resources/GuandanUI/TitleCover.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new Exception("Title cover missing");
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = 2048; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        public static void VisualQa()
        {
            ImportCover();
            EditorSceneManager.OpenScene(GuandanProjectSetup.TitleScenePath);
            SessionState.SetBool("TombTitleQa", true);
            EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("TombTitleQa", false))
                { stage = 0; stageTime = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
            };
        }
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup - stageTime < 2.0) return;
            try
            {
                var title = UnityEngine.Object.FindFirstObjectByType<TombTitleBootstrap>();
                var camera = Camera.main;
                if (stage == 0)
                {
                    if (title == null || UnityEngine.Object.FindFirstObjectByType<Guandan.Game.GameDirector>() != null) throw new Exception("Title isolation failed");
                    var cover=title.GetComponentInChildren<UnityEngine.Canvas>();
                    if(Mathf.Abs(cover.transform.position.y-title.DesignFocusPosition.y-0.32f)>0.001f) throw new Exception("Complete cover must move upward by 0.32m");
                    Capture(camera, "01-title", 1920,1080);
                    Capture(camera, "02-title-compact", 1280,960);
                    var button = GameObject.Find("Enter Tomb").GetComponent<TitleActionButton>();
                    // Check the same physical target used by the runtime input.
                    var direction = button.transform.position - camera.transform.position;
                    if (!Physics.Raycast(camera.transform.position, direction, out var hit, 20f) || hit.collider.GetComponent<TitleActionButton>() != button) throw new Exception("Title ray misses Start");
                    button.SetPressed(true);
                    stage = 1;
                }
                else if (stage == 1)
                {
                    Capture(camera,"03-pressed",1920,1080);
                    var button = GameObject.Find("Enter Tomb").GetComponent<TitleActionButton>();
                    button.SetPressed(false);
                    button.Activate();
                    stage = 2;
                }
                else if (stage == 2)
                {
                    if (SceneManager.GetActiveScene().name != "GuandanTomb")
                    { if (EditorApplication.timeSinceStartup - stageTime > 40) throw new Exception("Start transition timeout"); return; }
                    if (title != null || UnityEngine.Object.FindFirstObjectByType<Guandan.Game.GameDirector>() == null) throw new Exception("Gameplay transition failed");
                    Capture(Camera.main,"04-gameplay",1920,1080);
                    Debug.Log("[TombTitleQA] PASS: isolated title, both viewports, collider hit, pressed feedback, Start -> gameplay.");
                    SessionState.SetBool("TombTitleQa", false);
                    EditorApplication.update -= Tick;
                    EditorApplication.playModeStateChanged += ExitWhenStopped;
                    EditorApplication.isPlaying = false;
                }
                stageTime = EditorApplication.timeSinceStartup;
            }
            catch (Exception exception) { Debug.LogException(exception); SessionState.SetBool("TombTitleQa", false); EditorApplication.update -= Tick; EditorApplication.delayCall += () => EditorApplication.Exit(1); }
        }
        private static void ExitWhenStopped(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
        private static void Capture(Camera camera, string name, int width, int height)
        {
            Directory.CreateDirectory("VisualQA/Title-20260908");
            var previous = camera.targetTexture; var active = RenderTexture.active; var fov = camera.fieldOfView;
            var target = new RenderTexture(width,height,24);
            camera.targetTexture = target;
            if (SceneManager.GetActiveScene().name == "GuandanTitle") camera.fieldOfView = Mathf.Max(58f, 2f*Mathf.Atan(3.98f/(4.25f*((float)width/height)))*Mathf.Rad2Deg);
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            File.WriteAllBytes("VisualQA/Title-20260908/"+name+".png",image.EncodeToPNG());
            camera.targetTexture = previous; camera.fieldOfView = fov; RenderTexture.active = active;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
