using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using TMPro;

// Montana ("mtn") spray can. While the can is held and equipped, holding Fire1
// sprays a continuous graffiti trail onto whatever wall/object the camera is
// aimed at. Paint is drawn as HDRP Decal Projectors, so it sticks to any
// surface without needing per-mesh render textures or UV unwrapping.
//
// Controls (while the can is the active held item):
//   Fire1 (hold)  - spray
//   C             - cycle paint colour
[RequireComponent(typeof(PickupInteractable))]
public class SprayCanController : MonoBehaviour
{
    [System.Serializable]
    public struct PaintColor
    {
        public string name;
        public Color color;
    }

    [Header("References")]
    [SerializeField] private PickupInteractable pickup;
    [SerializeField] private Camera sprayCamera;
    [SerializeField] private Transform nozzle;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Looping hiss played the whole time you hold the spray button.")]
    [SerializeField] private AudioClip paintingSound;
    [Tooltip("One-shot rattle played when cycling colour.")]
    [SerializeField] private AudioClip rattleSound;

    [Header("Spraying")]
    [SerializeField] private float range = 4f;
    [Tooltip("Spray strength. Scales how far the paint reaches and how wide / heavy each pass lays down. 1 = base values above.")]
    [Range(0.1f, 4f)]
    [SerializeField] private float paintForce = 1f;
    [SerializeField] private float sprayInterval = 0.018f;
    [SerializeField] private Vector2 dabSizeRange = new Vector2(0.18f, 0.30f);
    [SerializeField] private float dabDepth = 0.4f;
    [SerializeField] private float spreadRadius = 0.025f;
    [Tooltip("Unused: graffiti is now permanent and never recycled.")]
    [SerializeField] private int maxDecals = 500;

    [Header("Paint")]
    [SerializeField] private bool infinitePaint = true;
    [SerializeField] private float paintCapacity = 100f;
    [SerializeField] private float paintPerDab = 0.12f;

    [Header("Colours (Montana-ish palette)")]
    [SerializeField]
    private PaintColor[] palette = new PaintColor[]
    {
        new PaintColor { name = "Power Black", color = new Color(0.05f, 0.05f, 0.05f) },
        new PaintColor { name = "Shock White", color = new Color(0.95f, 0.95f, 0.95f) },
        new PaintColor { name = "Madrid Red", color = new Color(0.85f, 0.10f, 0.12f) },
        new PaintColor { name = "Genius Blue", color = new Color(0.10f, 0.35f, 0.90f) },
        new PaintColor { name = "UFO Green", color = new Color(0.20f, 0.80f, 0.25f) },
        new PaintColor { name = "Yellow Light", color = new Color(0.98f, 0.85f, 0.10f) },
        new PaintColor { name = "Power Orange", color = new Color(0.98f, 0.45f, 0.05f) },
        new PaintColor { name = "Gleam Pink", color = new Color(0.95f, 0.30f, 0.70f) },
    };
    [SerializeField] private KeyCode cycleColorKey = KeyCode.C;

    [Header("HUD")]
    [SerializeField] private TMP_Text colorLabel;
    [SerializeField] private string colorFormat = "{0}  |  Paint {1:0}%";
    private TMP_Text hintLabel;

    private int colorIndex;
    private float paintRemaining;
    private float nextSprayTime;
    private Texture2D sprayTexture;
    private AudioSource sprayLoopSource;
    private AudioClip rattleClip;
    private readonly Dictionary<int, Material> decalMaterials = new Dictionary<int, Material>();
    private readonly Queue<GameObject> liveDecals = new Queue<GameObject>();
    private Transform decalRoot;
    private Material baseDecalMaterial;
    private bool baseDecalResolved;

    private void Awake()
    {
        pickup = pickup != null ? pickup : GetComponent<PickupInteractable>();
        sprayCamera = sprayCamera != null ? sprayCamera : Camera.main;
        nozzle = nozzle != null ? nozzle : transform;
        paintRemaining = paintCapacity;

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        EnsureSprayAudioSource();
        EnsureHud();
        UpdateHud(false);
    }

    private void Start()
    {
        // Move the first-spray cost to scene load so the initial burst doesn't
        // stall the frame.
        StartCoroutine(WarmUp());
    }

    // Pre-builds everything the first spray would otherwise create lazily:
    // the procedural coverage texture, every palette decal material, the HDRP
    // DBuffer shader variant (compiled the first time a decal is drawn), and the
    // audio clips' PCM data. Spread over a couple of frames so the warm-up itself
    // doesn't spike.
    private System.Collections.IEnumerator WarmUp()
    {
        // 128x128 Perlin texture + mipmaps — the biggest single CPU cost.
        GetSprayTexture();
        yield return null;

        // Material creation + HDRP keyword/pass setup for each colour.
        int count = palette != null ? palette.Length : 0;
        for (int i = 0; i < count; i++)
        {
            GetDecalMaterial(i);
        }
        yield return null;

        // Draw one throwaway decal far below the world so HDRP compiles the
        // DBuffer projector shader variant now instead of hitching on the first
        // visible dab.
        Material warm = GetDecalMaterial(0);
        if (warm != null)
        {
            GameObject probe = new GameObject("GraffitiWarmup");
            probe.transform.position = new Vector3(0f, -1000f, 0f);
            DecalProjector dp = probe.AddComponent<DecalProjector>();
            dp.material = warm;
            dp.size = new Vector3(0.001f, 0.001f, 0.001f);
            dp.fadeFactor = 1f;
            yield return null;
            yield return null;
            Destroy(probe);
        }

        // Preload audio so the rattle / hiss actually play in built players.
        // Streamed or Load-In-Background clips have no PCM data resident on
        // first use, so the first PlayOneShot is silent in a build even though
        // it works in the editor (where data is already loaded).
        PreloadAudio(paintingSound);
        PreloadAudio(rattleSound);
    }

    private static void PreloadAudio(AudioClip clip)
    {
        if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
        }
    }

    private void Update()
    {
        bool active = pickup != null && pickup.IsHeld && pickup.IsEquipped;
        if (!active)
        {
            StopSprayAudio();
            UpdateHud(false);
            return;
        }

        // Don't spray or cycle colour while the game is paused.
        if (Time.timeScale <= 0f)
        {
            StopSprayAudio();
            return;
        }

        UpdateHud(true);

        if (sprayCamera == null)
        {
            sprayCamera = Camera.main;
        }

        if (Input.GetKeyDown(cycleColorKey))
        {
            CycleColor();
        }

        bool spraying = Input.GetButton("Fire1") && HasPaint();
        if (spraying)
        {
            StartSprayAudio();
            if (Time.time >= nextSprayTime)
            {
                nextSprayTime = Time.time + sprayInterval;
                SprayDab();
            }
        }
        else
        {
            StopSprayAudio();
        }
    }

    private void OnDisable()
    {
        StopSprayAudio();
    }

    private bool HasPaint()
    {
        return infinitePaint || paintRemaining > 0f;
    }

    // --- cross-scene carry-over ---
    // The spray prefab ships without its sound clips and tuned spray values
    // (those were authored on the scene instance), so InventoryCarryOver copies
    // the live sounds across the scene load and re-applies settings on restore.
    public void GetCarrySounds(out AudioClip painting, out AudioClip rattle)
    {
        painting = paintingSound;
        rattle = rattleSound;
    }

    public void ApplyCarryOverSettings(float newRange, float newForce, float newInterval, AudioClip painting, AudioClip rattle)
    {
        range = newRange;
        paintForce = Mathf.Clamp(newForce, 0.1f, 4f);
        sprayInterval = newInterval;
        if (painting != null)
        {
            paintingSound = painting;
            PreloadAudio(paintingSound);
        }

        if (rattle != null)
        {
            rattleSound = rattle;
            PreloadAudio(rattleSound);
        }
    }

    private void SprayDab()
    {
        if (sprayCamera == null)
        {
            return;
        }

        Vector3 origin = sprayCamera.transform.position;
        Vector3 dir = sprayCamera.transform.forward;

        float force = Mathf.Max(0.05f, paintForce);
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, range * force, ~0, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        // Don't paint the can itself or the player.
        if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<PickupInteractable>() == pickup)
        {
            return;
        }

        Vector3 normal = hit.normal;
        Vector3 right = Vector3.Cross(normal, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.Cross(normal, Vector3.forward);
        }
        right.Normalize();
        Vector3 up = Vector3.Cross(right, normal);

        // Scatter the dab a little around the aim point so the trail looks like
        // an aerosol cone rather than a perfect line.
        Vector2 jitter = Random.insideUnitCircle * (spreadRadius * force);
        Vector3 point = hit.point + right * jitter.x + up * jitter.y;

        Material decalMaterial = GetDecalMaterial(colorIndex);
        if (decalMaterial == null)
        {
            return;
        }

        GameObject go = AcquireDecal();
        go.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, up));
        go.transform.Rotate(0f, 0f, Random.Range(0f, 360f), Space.Self);

        DecalProjector projector = go.GetComponent<DecalProjector>();
        float dabSize = Random.Range(dabSizeRange.x, dabSizeRange.y) * force;
        projector.size = new Vector3(dabSize, dabSize, dabDepth);
        projector.pivot = Vector3.zero;
        projector.material = decalMaterial;
        projector.fadeFactor = 1f;
        go.SetActive(true);

        if (!infinitePaint)
        {
            paintRemaining = Mathf.Max(0f, paintRemaining - paintPerDab);
        }
    }

    private GameObject AcquireDecal()
    {
        if (decalRoot == null)
        {
            decalRoot = new GameObject("GraffitiDecals").transform;
        }

        // Graffiti is permanent: always create a fresh decal so existing paint is
        // never relocated/recycled away.
        GameObject go = new GameObject("Graffiti");
        go.transform.SetParent(decalRoot, true);
        go.AddComponent<DecalProjector>();
        go.SetActive(false);
        liveDecals.Enqueue(go);
        return go;
    }

    private void CycleColor()
    {
        if (palette == null || palette.Length == 0)
        {
            return;
        }

        colorIndex = (colorIndex + 1) % palette.Length;
        UpdateHud(true);

        if (audioSource != null)
        {
            audioSource.PlayOneShot(rattleSound != null ? rattleSound : GetRattleClip());
        }
    }

    // Resolves the shared HDRP/Decal source material once. Loads the build-safe
    // asset from Resources first (keeps the shader + decal variants in players);
    // falls back to a runtime Shader.Find for safety in the editor.
    private Material GetBaseDecalMaterial()
    {
        if (baseDecalResolved)
        {
            return baseDecalMaterial;
        }

        baseDecalResolved = true;
        baseDecalMaterial = Resources.Load<Material>("GraffitiDecal");
        if (baseDecalMaterial == null)
        {
            Shader decalShader = Shader.Find("HDRP/Decal");
            if (decalShader != null)
            {
                baseDecalMaterial = new Material(decalShader) { name = "GraffitiDecal (runtime)" };
            }
        }

        return baseDecalMaterial;
    }

    private Material GetDecalMaterial(int index)
    {
        if (decalMaterials.TryGetValue(index, out Material existing) && existing != null)
        {
            return existing;
        }

        // Prefer instancing the shipped Resources material: it keeps the
        // HDRP/Decal shader AND its DBuffer variants alive through the build's
        // shader stripper. Shader.Find alone returns a shader whose decal
        // variants HDRP strips when no decal *material asset* is referenced in a
        // built scene, so the projector renders nothing in players.
        Material baseDecal = GetBaseDecalMaterial();
        if (baseDecal == null)
        {
            // No base asset and the shader was stripped too — skip painting
            // rather than crashing on new Material(null).
            Debug.LogWarning("SprayCanController: HDRP/Decal material/shader not found — graffiti disabled.");
            return null;
        }

        Material mat = new Material(baseDecal) { name = "Graffiti_" + index };
        mat.SetTexture("_BaseColorMap", GetSprayTexture());
        mat.SetColor("_BaseColor", GetColor(index));
        // Paint albedo only (no normal / metal / smoothness influence).
        mat.SetFloat("_AffectAlbedo", 1f);
        mat.SetFloat("_AffectNormal", 0f);
        mat.SetFloat("_AffectMetal", 0f);
        mat.SetFloat("_AffectAO", 0f);
        mat.SetFloat("_AffectSmoothness", 0f);
        if (mat.HasProperty("_AffectEmission"))
        {
            mat.SetFloat("_AffectEmission", 0f);
        }

        ConfigureDecalMaterial(mat);
        decalMaterials[index] = mat;
        return mat;
    }

    // Replicates HDRP's internal DecalAPI.SetupCommonDecalMaterialKeywordsAndPass,
    // which the editor runs on decal materials but is not callable from runtime
    // scripts. Without it the DBuffer colour mask stays 0 and the _COLORMAP
    // keyword is off, so the decal writes nothing to the screen.
    private static void ConfigureDecalMaterial(Material mat)
    {
        bool affectsAlbedo = GetAffect(mat, "_AffectAlbedo");
        bool affectsNormal = GetAffect(mat, "_AffectNormal");
        bool affectsMetal = GetAffect(mat, "_AffectMetal");
        bool affectsAO = GetAffect(mat, "_AffectAO");
        bool affectsSmoothness = GetAffect(mat, "_AffectSmoothness");
        bool affectsMaskmap = affectsMetal || affectsAO || affectsSmoothness;
        bool affectsEmission = GetAffect(mat, "_AffectEmission");

        SetKeyword(mat, "_MATERIAL_AFFECTS_ALBEDO", affectsAlbedo);
        SetKeyword(mat, "_MATERIAL_AFFECTS_NORMAL", affectsNormal);
        SetKeyword(mat, "_MATERIAL_AFFECTS_MASKMAP", affectsMaskmap);
        SetKeyword(mat, "_COLORMAP", mat.GetTexture("_BaseColorMap") != null);

        const int red = 1, green = 2, blue = 4, alpha = 8, all = 15;
        int mask0 = 0, mask1 = 0, mask2 = 0, mask3 = 0;
        if (affectsAlbedo) mask0 |= all;
        if (affectsNormal) mask1 |= all;
        if (affectsMetal) { mask2 |= red; mask3 |= red; }
        if (affectsAO) { mask2 |= green; mask3 |= green; }
        if (affectsSmoothness) mask2 |= blue | alpha;

        mat.SetInt("_DecalColorMask0", mask0);
        mat.SetInt("_DecalColorMask1", mask1);
        mat.SetInt("_DecalColorMask2", mask2);
        mat.SetInt("_DecalColorMask3", mask3);

        bool enableDBuffer = (mask0 + mask1 + mask2 + mask3) != 0;
        SetPass(mat, "DBufferProjector", enableDBuffer);
        SetPass(mat, "DBufferMesh", enableDBuffer);
        SetPass(mat, "DecalProjectorForwardEmissive", affectsEmission);
        SetPass(mat, "DecalMeshForwardEmissive", affectsEmission);

        int drawOrder = mat.HasProperty("_DrawOrder") ? mat.GetInt("_DrawOrder") : 0;
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + drawOrder;
        // HDRP draws decals with DrawMeshInstanced, which requires instancing.
        mat.enableInstancing = true;
    }

    private static bool GetAffect(Material mat, string prop)
    {
        return mat.HasProperty(prop) && mat.GetFloat(prop) == 1f;
    }

    private static void SetKeyword(Material mat, string keyword, bool on)
    {
        if (on) mat.EnableKeyword(keyword);
        else mat.DisableKeyword(keyword);
    }

    private static void SetPass(Material mat, string passName, bool on)
    {
        if (mat.FindPass(passName) != -1)
        {
            mat.SetShaderPassEnabled(passName, on);
        }
    }

    private Color GetColor(int index)
    {
        if (palette == null || palette.Length == 0)
        {
            return Color.magenta;
        }

        return palette[Mathf.Clamp(index, 0, palette.Length - 1)].color;
    }

    // Soft, slightly noisy aerosol blob used as the decal's coverage (alpha)
    // map. RGB is white so the per-colour material tint shows through.
    private Texture2D GetSprayTexture()
    {
        if (sprayTexture != null)
        {
            return sprayTexture;
        }

        const int size = 128;
        sprayTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp
        };

        float center = (size - 1) * 0.5f;
        float maxDist = center;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / maxDist;
                float dy = (y - center) / maxDist;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                // Dense core, soft edge.
                float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 1f, dist));
                // Speckle so the paint looks sprayed, not stamped.
                float speckle = Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                alpha *= Mathf.Lerp(0.55f, 1f, speckle);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        sprayTexture.SetPixels(pixels);
        sprayTexture.Apply(true);
        return sprayTexture;
    }

    private void EnsureSprayAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        // Dedicated source for the looping hiss, on a child so PickupInteractable's
        // GetComponent<AudioSource>() never grabs it. Keeping the loop off the
        // shared source means StartSprayAudio/StopSprayAudio's Play()/Stop() can't
        // clobber the rattle / pickup / drop one-shots that run on `audioSource`.
        if (sprayLoopSource == null)
        {
            GameObject loopGo = new GameObject("SprayLoopAudio");
            loopGo.transform.SetParent(transform, false);
            sprayLoopSource = loopGo.AddComponent<AudioSource>();
        }

        sprayLoopSource.playOnAwake = false;
        sprayLoopSource.spatialBlend = 1f;
        sprayLoopSource.rolloffMode = AudioRolloffMode.Logarithmic;
        sprayLoopSource.loop = true;
    }

    // Spray-can ball rattle synthesized at runtime, used when no Rattle Sound
    // clip is assigned. A handful of short metallic ticks (decaying tone + noise)
    // that approximate shaking the can.
    private AudioClip GetRattleClip()
    {
        if (rattleClip != null)
        {
            return rattleClip;
        }

        const int sampleRate = 44100;
        const float duration = 0.5f;
        int sampleCount = (int)(sampleRate * duration);
        float[] data = new float[sampleCount];
        System.Random rng = new System.Random(8721);

        const int ticks = 8;
        for (int t = 0; t < ticks; t++)
        {
            float tickTime = (t / (float)ticks) * duration + (float)(rng.NextDouble() * 0.025);
            int start = (int)(tickTime * sampleRate);
            float freq = 2200f + (float)rng.NextDouble() * 2000f;
            int tickLen = (int)(sampleRate * 0.04f);
            for (int i = 0; i < tickLen && start + i < sampleCount; i++)
            {
                float env = Mathf.Exp(-i / (sampleRate * 0.006f));
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * i / sampleRate);
                data[start + i] = Mathf.Clamp(data[start + i] + env * (0.5f * tone + 0.5f * noise) * 0.6f, -1f, 1f);
            }
        }

        rattleClip = AudioClip.Create("SprayRattle", sampleCount, 1, sampleRate, false);
        rattleClip.SetData(data, 0);
        return rattleClip;
    }

    private void StartSprayAudio()
    {
        if (sprayLoopSource == null || paintingSound == null)
        {
            return;
        }

        if (!sprayLoopSource.isPlaying || sprayLoopSource.clip != paintingSound)
        {
            sprayLoopSource.clip = paintingSound;
            sprayLoopSource.loop = true;
            sprayLoopSource.Play();
        }
    }

    private void StopSprayAudio()
    {
        if (sprayLoopSource != null && sprayLoopSource.isPlaying)
        {
            sprayLoopSource.Stop();
        }
    }

    private void EnsureHud()
    {
        if (colorLabel != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Spray Can HUD Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10;
        UnityEngine.UI.CanvasScaler scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject labelObject = new GameObject("Spray Color Label");
        labelObject.transform.SetParent(canvas.transform, false);
        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 96f);
        rect.sizeDelta = new Vector2(560f, 40f);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        colorLabel = label;

        GameObject hintObject = new GameObject("Spray Hint Label");
        hintObject.transform.SetParent(canvas.transform, false);
        RectTransform hintRect = hintObject.AddComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.5f, 0f);
        hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 66f);
        hintRect.sizeDelta = new Vector2(560f, 30f);

        TextMeshProUGUI hint = hintObject.AddComponent<TextMeshProUGUI>();
        hint.fontSize = 18f;
        hint.alignment = TextAlignmentOptions.Center;
        hint.raycastTarget = false;
        hint.color = new Color(1f, 1f, 1f, 0.7f);
        hint.text = $"Press [{cycleColorKey}] to change colour";
        hintLabel = hint;
    }

    private void UpdateHud(bool show)
    {
        if (colorLabel == null)
        {
            return;
        }

        colorLabel.gameObject.SetActive(show);
        if (hintLabel != null)
        {
            hintLabel.gameObject.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        string colorName = (palette != null && palette.Length > 0)
            ? palette[Mathf.Clamp(colorIndex, 0, palette.Length - 1)].name
            : "Paint";
        float percent = infinitePaint ? 100f : (paintCapacity > 0f ? paintRemaining / paintCapacity * 100f : 0f);
        colorLabel.text = string.Format(colorFormat, colorName, percent);
        colorLabel.color = GetColor(colorIndex);
    }
}
