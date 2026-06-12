using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class HdrpProjectSetup
{
    private const string SettingsFolder = "Assets/Settings";
    private const string HighAssetPath = "Assets/Settings/HDRP_High.asset";
    private const string LowAssetPath = "Assets/Settings/HDRP_Low.asset";

    [MenuItem("Tools/HDRP Setup/Configure Pipeline Assets")]
    public static void ConfigurePipelineAssets()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        EnsureGlobalSettings();

        HDRenderPipelineAsset highAsset = CreatePipelineAsset(HighAssetPath);
        HDRenderPipelineAsset lowAsset = CreatePipelineAsset(LowAssetPath);

        ApplyHighSettings(highAsset);
        ApplyLowSettings(lowAsset);

        GraphicsSettings.defaultRenderPipeline = highAsset;

        for (int level = 0; level < QualitySettings.names.Length; level++)
        {
            QualitySettings.SetQualityLevel(level, false);
            QualitySettings.renderPipeline = level >= 3 ? highAsset : lowAsset;
        }

        QualitySettings.SetQualityLevel(5, true);
        AssetDatabase.SaveAssets();
        Debug.Log("HDRP setup complete: High -> levels 3-5, Low -> levels 0-2, default High.");
    }

    private static void EnsureGlobalSettings()
    {
        System.Type globalSettingsType = System.Type.GetType(
            "UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");
        if (globalSettingsType == null)
        {
            Debug.LogWarning("HDRP setup: HDRenderPipelineGlobalSettings type not found");
            return;
        }

        MethodInfo ensure = globalSettingsType.GetMethod(
            "Ensure",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (ensure != null)
        {
            ParameterInfo[] parameters = ensure.GetParameters();
            object[] args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }

            ensure.Invoke(null, args);
        }
        else
        {
            Debug.LogWarning("HDRP setup: Ensure method not found on HDRenderPipelineGlobalSettings");
        }
    }

    private static HDRenderPipelineAsset CreatePipelineAsset(string path)
    {
        HDRenderPipelineAsset existing = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
        if (existing != null)
        {
            return existing;
        }

        HDRenderPipelineAsset asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();

        MethodInfo newDefault = typeof(RenderPipelineSettings).GetMethod(
            "NewDefault",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (newDefault != null)
        {
            object defaults = newDefault.Invoke(null, null);
            FieldInfo settingsField = typeof(HDRenderPipelineAsset).GetField(
                "m_RenderPipelineSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (settingsField != null)
            {
                settingsField.SetValue(asset, defaults);
            }
        }

        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void ApplyHighSettings(HDRenderPipelineAsset asset)
    {
        SerializedObject serialized = new SerializedObject(asset);
        SetBool(serialized, "m_RenderPipelineSettings.supportVolumetrics", true);
        SetBool(serialized, "m_RenderPipelineSettings.supportSSAO", true);
        SetBool(serialized, "m_RenderPipelineSettings.supportSSR", true);
        SetBool(serialized, "m_RenderPipelineSettings.supportMotionVectors", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static void ApplyLowSettings(HDRenderPipelineAsset asset)
    {
        SerializedObject serialized = new SerializedObject(asset);
        SetBool(serialized, "m_RenderPipelineSettings.supportVolumetrics", false);
        SetBool(serialized, "m_RenderPipelineSettings.supportSSAO", false);
        SetBool(serialized, "m_RenderPipelineSettings.supportSSR", false);
        SetBool(serialized, "m_RenderPipelineSettings.supportSSGI", false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    private static void SetBool(SerializedObject serialized, string propertyPath, bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyPath);
        if (property == null)
        {
            Debug.LogWarning($"HDRP setup: property not found {propertyPath}");
            return;
        }

        property.boolValue = value;
    }
}
