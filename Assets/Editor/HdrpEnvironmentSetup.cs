using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class HdrpEnvironmentSetup
{
    private const string VolumesFolder = "Assets/Settings/Volumes";

    [MenuItem("Tools/HDRP Setup/Reimport Model Assets")]
    public static void ReimportModelAssets()
    {
        string[] folders =
        {
            "Assets/3d",
            "Assets/ModularFirstPersonController",
            "Assets/Ishikawa1116",
            "Assets/Rain Particles",
            "Assets/Prefabs"
        };

        foreach (string folder in folders)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.ImportAsset(folder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("HDRP env: model reimport finished");
    }

    [MenuItem("Tools/HDRP Setup/Setup Night Environment")]
    public static void SetupNightEnvironment()
    {
        EnsureVolumesFolder();
        Scene scene = SceneManager.GetActiveScene();

        VolumeProfile profile = LoadOrCreateProfile(scene.name + "_Environment");

        VisualEnvironment visualEnvironment = GetOrAddOverride<VisualEnvironment>(profile);
        visualEnvironment.skyType.Override((int)SkyType.Gradient);
        visualEnvironment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

        GradientSky gradientSky = GetOrAddOverride<GradientSky>(profile);
        gradientSky.top.Override(new Color(0.012f, 0.022f, 0.06f, 1f));
        gradientSky.middle.Override(new Color(0.006f, 0.009f, 0.024f, 1f));
        gradientSky.bottom.Override(Color.black);
        gradientSky.multiplier.Override(0.85f);

        Exposure exposure = GetOrAddOverride<Exposure>(profile);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(-0.2f);

        Fog fog = GetOrAddOverride<Fog>(profile);
        fog.enabled.Override(true);
        fog.enableVolumetricFog.Override(true);
        fog.meanFreePath.Override(38f);
        fog.baseHeight.Override(0f);
        fog.maximumHeight.Override(18f);
        fog.albedo.Override(new Color(0.45f, 0.55f, 0.8f, 1f));
        fog.globalLightProbeDimmer.Override(0.4f);

        Bloom bloom = GetOrAddOverride<Bloom>(profile);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.65f);

        Vignette vignette = GetOrAddOverride<Vignette>(profile);
        vignette.intensity.Override(0.34f);
        vignette.smoothness.Override(0.45f);

        ColorAdjustments colorAdjustments = GetOrAddOverride<ColorAdjustments>(profile);
        colorAdjustments.saturation.Override(-22f);
        colorAdjustments.contrast.Override(14f);
        colorAdjustments.colorFilter.Override(new Color(0.85f, 0.9f, 1f, 1f));

        FilmGrain filmGrain = GetOrAddOverride<FilmGrain>(profile);
        filmGrain.type.Override(FilmGrainLookup.Thin1);
        filmGrain.intensity.Override(0.35f);
        filmGrain.response.Override(0.8f);

        WhiteBalance whiteBalance = GetOrAddOverride<WhiteBalance>(profile);
        whiteBalance.temperature.Override(-12f);

        ScreenSpaceAmbientOcclusion ambientOcclusion = GetOrAddOverride<ScreenSpaceAmbientOcclusion>(profile);
        ambientOcclusion.intensity.Override(1.25f);

        EditorUtility.SetDirty(profile);
        ApplyProfileToSceneVolume(profile);

        ConfigureMoon();
        ConfigureLampSpots();
        EnsureCameraData();

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("HDRP env: night environment configured for " + scene.name);
    }

    [MenuItem("Tools/HDRP Setup/Quality/Set Ultra Low")]
    public static void SetQualityUltraLow()
    {
        GraphicsQualityManager.SetMode(GraphicsQualityManager.UltraLowMode);
    }

    [MenuItem("Tools/HDRP Setup/Quality/Set Low")]
    public static void SetQualityLow()
    {
        GraphicsQualityManager.SetMode(GraphicsQualityManager.LowMode);
    }

    [MenuItem("Tools/HDRP Setup/Quality/Set High")]
    public static void SetQualityHigh()
    {
        GraphicsQualityManager.SetMode(GraphicsQualityManager.HighMode);
    }

    [MenuItem("Tools/HDRP Setup/Fix Particle Materials")]
    public static void FixParticleMaterials()
    {
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        Shader hdrpUnlit = Shader.Find("HDRP/Unlit");
        if (hdrpUnlit == null)
        {
            System.IO.File.WriteAllText("Temp/particle_fix_log.txt", "HDRP/Unlit shader not found");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Rain Particles" });
        report.AppendLine("found " + guids.Length + " materials");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == hdrpUnlit)
            {
                report.AppendLine("skip (already unlit/null): " + path);
                continue;
            }

            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (shaderName.StartsWith("HDRP/"))
            {
                report.AppendLine("skip (" + shaderName + "): " + path);
                continue;
            }

            report.AppendLine("converting (" + shaderName + "): " + path);

            Texture mainTexture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Color tint = material.HasProperty("_TintColor") ? material.GetColor("_TintColor") : Color.white;

            material.shader = hdrpUnlit;
            if (mainTexture != null)
            {
                material.SetTexture("_UnlitColorMap", mainTexture);
            }

            material.SetColor("_UnlitColor", new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(tint.a * 1.6f)));
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 1f);
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZTestTransparent", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ADD");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            ResetHdrpMaterialKeywords(material);
            EditorUtility.SetDirty(material);
        }

        AssetDatabase.SaveAssets();
        System.IO.File.WriteAllText("Temp/particle_fix_log.txt", report.ToString());
    }

    private static void ResetHdrpMaterialKeywords(Material material)
    {
        System.Type utils = System.Type.GetType(
            "UnityEditor.Rendering.HighDefinition.HDShaderUtils, Unity.RenderPipelines.HighDefinition.Editor");
        if (utils == null)
        {
            return;
        }

        foreach (System.Reflection.MethodInfo method in utils.GetMethods(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            if (method.Name != "ResetMaterialKeywords")
            {
                continue;
            }

            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Material))
            {
                method.Invoke(null, new object[] { material });
                return;
            }
        }
    }

    [MenuItem("Tools/HDRP Setup/Setup Indoor Environment")]
    public static void SetupIndoorEnvironment()
    {
        EnsureVolumesFolder();
        Scene scene = SceneManager.GetActiveScene();

        VolumeProfile profile = LoadOrCreateProfile(scene.name + "_Environment");

        VisualEnvironment visualEnvironment = GetOrAddOverride<VisualEnvironment>(profile);
        visualEnvironment.skyType.Override((int)SkyType.Gradient);
        visualEnvironment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

        GradientSky gradientSky = GetOrAddOverride<GradientSky>(profile);
        gradientSky.top.Override(new Color(0.02f, 0.03f, 0.07f, 1f));
        gradientSky.middle.Override(new Color(0.008f, 0.012f, 0.03f, 1f));
        gradientSky.bottom.Override(Color.black);
        gradientSky.multiplier.Override(0.5f);

        Exposure exposure = GetOrAddOverride<Exposure>(profile);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(4.4f);

        Fog fog = GetOrAddOverride<Fog>(profile);
        fog.enabled.Override(true);
        fog.enableVolumetricFog.Override(true);
        fog.meanFreePath.Override(70f);
        fog.baseHeight.Override(0f);
        fog.maximumHeight.Override(12f);
        fog.albedo.Override(new Color(0.5f, 0.55f, 0.7f, 1f));

        Bloom bloom = GetOrAddOverride<Bloom>(profile);
        bloom.intensity.Override(0.3f);

        Vignette vignette = GetOrAddOverride<Vignette>(profile);
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.4f);

        ColorAdjustments colorAdjustments = GetOrAddOverride<ColorAdjustments>(profile);
        colorAdjustments.saturation.Override(-15f);
        colorAdjustments.contrast.Override(10f);

        FilmGrain filmGrain = GetOrAddOverride<FilmGrain>(profile);
        filmGrain.type.Override(FilmGrainLookup.Thin1);
        filmGrain.intensity.Override(0.3f);

        ScreenSpaceAmbientOcclusion ambientOcclusion = GetOrAddOverride<ScreenSpaceAmbientOcclusion>(profile);
        ambientOcclusion.intensity.Override(1.1f);

        EditorUtility.SetDirty(profile);
        ApplyProfileToSceneVolume(profile);

        ConvertAllLightsToHd();
        EnsureCameraData();

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("HDRP env: indoor environment configured for " + scene.name);
    }

    private static void EnsureVolumesFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        if (!AssetDatabase.IsValidFolder(VolumesFolder))
        {
            AssetDatabase.CreateFolder("Assets/Settings", "Volumes");
        }
    }

    private static VolumeProfile LoadOrCreateProfile(string name)
    {
        string path = VolumesFolder + "/" + name + ".asset";
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }

        return profile;
    }

    private static T GetOrAddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
        {
            return component;
        }

        T added = profile.Add<T>(false);
        AssetDatabase.AddObjectToAsset(added, profile);
        return added;
    }

    private static void ApplyProfileToSceneVolume(VolumeProfile profile)
    {
        GameObject volumeObject = GameObject.Find("HDRP Environment Volume");
        if (volumeObject == null)
        {
            volumeObject = new GameObject("HDRP Environment Volume");
        }

        Volume volume = volumeObject.GetComponent<Volume>();
        if (volume == null)
        {
            volume = volumeObject.AddComponent<Volume>();
        }

        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;
    }

    private static void ConfigureMoon()
    {
        foreach (Light light in Object.FindObjectsOfType<Light>())
        {
            if (light.type != UnityEngine.LightType.Directional)
            {
                continue;
            }

            HDAdditionalLightData data = EnsureLightData(light);
            light.color = new Color(0.62f, 0.72f, 1f, 1f);
            data.SetIntensity(4.5f, LightUnit.Lux);
            light.shadows = LightShadows.Soft;
            data.EnableShadows(true);
            data.affectsVolumetric = true;
            data.angularDiameter = 1.2f;
        }
    }

    private static void ConfigureLampSpots()
    {
        foreach (Light light in Object.FindObjectsOfType<Light>())
        {
            if (light.type != UnityEngine.LightType.Spot)
            {
                continue;
            }

            HDAdditionalLightData data = EnsureLightData(light);
            light.color = new Color(1f, 0.78f, 0.5f, 1f);
            data.SetIntensity(28000f, LightUnit.Lumen);
            light.range = Mathf.Max(light.range, 18f);
            light.shadows = LightShadows.Soft;
            data.EnableShadows(true);
            data.affectsVolumetric = true;
            data.volumetricDimmer = 1f;
        }
    }

    private static void ConvertAllLightsToHd()
    {
        foreach (Light light in Object.FindObjectsOfType<Light>())
        {
            HDAdditionalLightData data = EnsureLightData(light);
            data.affectsVolumetric = true;

            switch (light.type)
            {
                case UnityEngine.LightType.Directional:
                    data.SetIntensity(2f, LightUnit.Lux);
                    break;
                case UnityEngine.LightType.Point:
                    data.SetIntensity(750f, LightUnit.Lumen);
                    break;
                case UnityEngine.LightType.Spot:
                    data.SetIntensity(2800f, LightUnit.Lumen);
                    break;
                default:
                    data.SetIntensity(800f, LightUnit.Lumen);
                    break;
            }
        }
    }

    private static HDAdditionalLightData EnsureLightData(Light light)
    {
        HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
        if (data == null)
        {
            data = light.gameObject.AddComponent<HDAdditionalLightData>();
        }

        return data;
    }

    private static void EnsureCameraData()
    {
        foreach (Camera camera in Object.FindObjectsOfType<Camera>(true))
        {
            if (camera.GetComponent<HDAdditionalCameraData>() == null)
            {
                camera.gameObject.AddComponent<HDAdditionalCameraData>();
            }
        }
    }
}
