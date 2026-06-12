using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NightMode : MonoBehaviour
{
    public Material nightSkybox;
    public Light sun;

    void Start()
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            // HDRP drives sky, ambient and the moon through volume profiles.
            return;
        }

        // Change skybox
        RenderSettings.skybox = nightSkybox;

        // Optional: dark ambient lighting
        RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.08f);

        // Optional: moonlight instead of sun
        if (sun != null)
        {
            sun.intensity = 0.05f;
            sun.color = new Color(0.2f, 0.2f, 0.4f);
        }

        DynamicGI.UpdateEnvironment();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyNightToContinueGameplay()
    {
        if (SceneManager.GetActiveScene().name != "continueGamePlay")
        {
            return;
        }

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            return;
        }

        Material skybox = null;
#if UNITY_EDITOR
        skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/sky/NightSky.mat");
#endif
        if (skybox != null)
        {
            RenderSettings.skybox = skybox;
        }

        RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.08f);

        Light sceneSun = RenderSettings.sun;
        if (sceneSun == null)
        {
            foreach (Light light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    sceneSun = light;
                    break;
                }
            }
        }

        if (sceneSun != null)
        {
            sceneSun.intensity = 0.05f;
            sceneSun.color = new Color(0.2f, 0.2f, 0.4f);
            RenderSettings.sun = sceneSun;
        }

        DynamicGI.UpdateEnvironment();
    }
}
