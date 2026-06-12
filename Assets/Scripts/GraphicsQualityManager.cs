using UnityEngine;
using UnityEngine.Rendering;

public static class GraphicsQualityManager
{
    public const string GraphicsPref = "settings.graphicsQuality.v2";

    public const int UltraLowMode = 0;
    public const int LowMode = 1;
    public const int HighMode = 2;

    public static int Mode => Mathf.Clamp(PlayerPrefs.GetInt(GraphicsPref, HighMode), UltraLowMode, HighMode);

    public static bool IsHigh => Mode == HighMode;

    public static void SetMode(int mode)
    {
        PlayerPrefs.SetInt(GraphicsPref, Mathf.Clamp(mode, UltraLowMode, HighMode));
        Apply();
    }

    public static void Apply()
    {
        int mode = Mode;
        switch (mode)
        {
            case UltraLowMode:
                // Built-in pipeline needs both the quality override and the
                // graphics default pipeline to be empty.
                ApplyQualityLevel("Very Low");
                GraphicsSettings.defaultRenderPipeline = null;
                break;
            case LowMode:
                ApplyQualityLevel("Low");
                RestoreDefaultPipelineFromQualityLevel();
                break;
            default:
                ApplyQualityLevel("High");
                RestoreDefaultPipelineFromQualityLevel();
                break;
        }

        BuiltinPipelineCompatibility.Apply();
        ApplyResolution(mode);
    }

    private static void RestoreDefaultPipelineFromQualityLevel()
    {
        RenderPipelineAsset levelPipeline = QualitySettings.renderPipeline;
        if (levelPipeline != null)
        {
            GraphicsSettings.defaultRenderPipeline = levelPipeline;
        }
    }

    private static void ApplyQualityLevel(string levelName)
    {
        string[] names = QualitySettings.names;
        int index = -1;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == levelName)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = levelName == "High" ? names.Length - 1 : 0;
        }

        QualitySettings.SetQualityLevel(index, true);
    }

    private static void ApplyResolution(int mode)
    {
#if !UNITY_EDITOR
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions == null || resolutions.Length == 0)
        {
            return;
        }

        Resolution best = resolutions[resolutions.Length - 1];
        if (mode == HighMode)
        {
            Screen.SetResolution(best.width, best.height, Screen.fullScreenMode);
            return;
        }

        int targetWidth = Mathf.Max(1280, best.width / 2);
        Resolution chosen = resolutions[0];
        int chosenDelta = int.MaxValue;
        for (int i = 0; i < resolutions.Length; i++)
        {
            int delta = Mathf.Abs(resolutions[i].width - targetWidth);
            if (delta < chosenDelta)
            {
                chosenDelta = delta;
                chosen = resolutions[i];
            }
        }

        Screen.SetResolution(chosen.width, chosen.height, Screen.fullScreenMode);
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnBoot()
    {
        Apply();
    }
}
