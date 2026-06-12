using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Ultra Low quality renders with the built-in pipeline while the project assets
// are authored for HDRP. This patcher swaps HDRP materials for Standard/legacy
// equivalents at runtime and clamps HDRP physical light intensities back to
// built-in scale. Switching back to an HDRP quality restores the originals.
public static class BuiltinPipelineCompatibility
{
    private sealed class RendererState
    {
        public Renderer renderer;
        public Material[] originalMaterials;
    }

    private sealed class LightState
    {
        public Light light;
        public float originalIntensity;
    }

    private static readonly List<RendererState> patchedRenderers = new List<RendererState>();
    private static readonly List<LightState> patchedLights = new List<LightState>();
    private static readonly Dictionary<Material, Material> conversionCache = new Dictionary<Material, Material>();
    private static bool sceneHookInstalled;
    private static bool patched;

    public static void Apply()
    {
        InstallSceneHook();

        if (GraphicsSettings.currentRenderPipeline == null)
        {
            PatchScene();
        }
        else
        {
            RestoreScene();
        }
    }

    private static void InstallSceneHook()
    {
        if (sceneHookInstalled)
        {
            return;
        }

        sceneHookInstalled = true;
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            ClearTracking();
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                PatchScene();
            }
        };
    }

    private static void ClearTracking()
    {
        patchedRenderers.Clear();
        patchedLights.Clear();
        patched = false;
    }

    private static void PatchScene()
    {
        if (patched)
        {
            return;
        }

        patched = true;

        foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>(true))
        {
            PatchRenderer(renderer);
        }

        foreach (Light light in Object.FindObjectsOfType<Light>(true))
        {
            PatchLight(light);
        }
    }

    public static void PatchSpawnedObject(GameObject root)
    {
        if (GraphicsSettings.currentRenderPipeline != null || root == null)
        {
            return;
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            PatchRenderer(renderer);
        }
    }

    private static void RestoreScene()
    {
        for (int i = 0; i < patchedRenderers.Count; i++)
        {
            RendererState state = patchedRenderers[i];
            if (state.renderer != null)
            {
                state.renderer.sharedMaterials = state.originalMaterials;
            }
        }

        for (int i = 0; i < patchedLights.Count; i++)
        {
            LightState state = patchedLights[i];
            if (state.light != null)
            {
                state.light.intensity = state.originalIntensity;
            }
        }

        ClearTracking();
    }

    private static void PatchRenderer(Renderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        bool changed = false;
        Material[] replacement = new Material[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            replacement[i] = materials[i];
            Material converted = ConvertMaterial(materials[i]);
            if (converted != null)
            {
                replacement[i] = converted;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        patchedRenderers.Add(new RendererState { renderer = renderer, originalMaterials = materials });
        renderer.sharedMaterials = replacement;
    }

    private static Material ConvertMaterial(Material source)
    {
        if (source == null || source.shader == null)
        {
            return null;
        }

        string shaderName = source.shader.name;
        bool isHdrpLit = shaderName.StartsWith("HDRP/") && shaderName != "HDRP/Unlit";
        bool isHdrpUnlit = shaderName == "HDRP/Unlit";
        bool isGltf = shaderName.Contains("glTF") && shaderName.Contains("Graph");
        bool isErrorShader = shaderName == "Hidden/InternalErrorShader";

        if (!isHdrpLit && !isHdrpUnlit && !isGltf && !isErrorShader)
        {
            return null;
        }

        if (conversionCache.TryGetValue(source, out Material cached) && cached != null)
        {
            return cached;
        }

        Texture baseTexture = FindTexture(source, "_BaseColorMap", "_UnlitColorMap", "baseColorTexture", "_MainTex", "_BaseMap");
        Color baseColor = FindColor(source, "_BaseColor", "_UnlitColor", "baseColorFactor", "_Color");

        Material converted;
        if (isHdrpUnlit && source.renderQueue >= (int)RenderQueue.Transparent)
        {
            Shader particleShader = Shader.Find("Legacy Shaders/Particles/Additive");
            converted = new Material(particleShader != null ? particleShader : Shader.Find("Standard"));
            if (converted.HasProperty("_TintColor"))
            {
                converted.SetColor("_TintColor", baseColor);
            }
        }
        else if (isHdrpUnlit)
        {
            Shader unlitShader = Shader.Find("Unlit/Texture");
            converted = new Material(unlitShader != null && baseTexture != null ? unlitShader : Shader.Find("Standard"));
            if (converted.HasProperty("_Color"))
            {
                converted.SetColor("_Color", baseColor);
            }
        }
        else
        {
            converted = new Material(Shader.Find("Standard"));
            converted.SetColor("_Color", baseColor);
            converted.SetFloat("_Glossiness", 0.2f);
        }

        converted.name = source.name + " (Builtin)";
        if (baseTexture != null && converted.HasProperty("_MainTex"))
        {
            converted.SetTexture("_MainTex", baseTexture);
        }

        conversionCache[source] = converted;
        return converted;
    }

    private static Texture FindTexture(Material material, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            if (material.HasProperty(propertyNames[i]))
            {
                Texture texture = material.GetTexture(propertyNames[i]);
                if (texture != null)
                {
                    return texture;
                }
            }
        }

        return null;
    }

    private static Color FindColor(Material material, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            if (material.HasProperty(propertyNames[i]))
            {
                return material.GetColor(propertyNames[i]);
            }
        }

        return Color.white;
    }

    private static void PatchLight(Light light)
    {
        // HDRP stores physical units in Light.intensity (lux/candela), which are
        // far too bright for the built-in pipeline. Clamp to built-in scale.
        float maxIntensity = light.type == UnityEngine.LightType.Directional ? 0.3f : 2.2f;
        if (light.intensity <= maxIntensity)
        {
            return;
        }

        patchedLights.Add(new LightState { light = light, originalIntensity = light.intensity });
        light.intensity = maxIntensity;
    }
}
