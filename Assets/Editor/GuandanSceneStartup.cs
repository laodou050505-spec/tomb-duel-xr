using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Guandan.Editor
{
    /// <summary>
    /// Restores the playable scene on the first editor load when Unity has no saved scene setup.
    /// It runs once per editor session and never replaces a named or dirty scene.
    /// </summary>
    [InitializeOnLoad]
    public static class GuandanSceneStartup
    {
        private const string SessionKey = "Guandan.SceneStartupChecked";

        static GuandanSceneStartup()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += OpenPlayableSceneWhenEditorIsEmpty;
        }

        private static void OpenPlayableSceneWhenEditorIsEmpty()
        {
            SessionState.SetBool(SessionKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var activeScene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(activeScene.path) || activeScene.isDirty) return;
            if (!File.Exists(GuandanProjectSetup.ScenePath)) return;

            EditorSceneManager.OpenScene(GuandanProjectSetup.ScenePath, OpenSceneMode.Single);
        }
    }
}
