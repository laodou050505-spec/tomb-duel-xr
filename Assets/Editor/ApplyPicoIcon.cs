#if UNITY_EDITOR
using UnityEditor; using UnityEditor.Build; using UnityEngine;
public static class ApplyPicoIcon { public static void Run() { var icon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AppIcon/AppIcon.png"); if(icon==null) throw new System.Exception("Missing AppIcon"); PlayerSettings.SetIcons(NamedBuildTarget.Android,new[]{icon},IconKind.Application); AssetDatabase.SaveAssets(); Debug.Log("PICO_ICON_APPLIED"); } }
#endif
