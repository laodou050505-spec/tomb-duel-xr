#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class ApplyPicoIcon
{
    const string IconPath = "Assets/AppIcon/AppIcon.png";

    public static void Run()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null) throw new Exception("Missing PICO application icon: " + IconPath);

        var assignedSlots = 0;
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
        {
            var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var slot in slots)
            {
                for (var layer = 0; layer < slot.maxLayerCount; layer++)
                    slot.SetTexture(icon, layer);
                assignedSlots++;
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"PICO_ICON_APPLIED: {IconPath} assigned to {assignedSlots} Android/PICO icon slots.");
    }
}
#endif
