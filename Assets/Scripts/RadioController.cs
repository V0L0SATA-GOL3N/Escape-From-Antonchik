using System.Collections;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Old radio in the yard. Works exactly once: 50/50 it tunes into Polskie
// Radio 24 (audio-only HLS, played through VideoPlayer which handles m3u8
// via the OS media stack) or plays track_1 from SFX. After 30 seconds it
// dies with a pop. Trying to switch it on again only produces smoke.
public class RadioController : SimpleInteractable
{
    [SerializeField] private string streamUrl = "https://stream15.polskieradio.pl/pr24/pr24.sdp/playlist.m3u8";
    [SerializeField] private AudioClip fallbackTrack;
    [SerializeField] private float playDuration = 30f;
    [SerializeField] private float streamPrepareTimeout = 6f;

    private AudioSource speaker;
    private VideoPlayer streamPlayer;
    private ParticleSystem smoke;
    private Light innardsGlow;

    private bool hasPlayedOnce;
    private bool isPlaying;
    private bool isBroken;

    private void Awake()
    {
        Prompt = "Press E to turn on the radio";
        SetInteractionDistance(3.5f);

        speaker = gameObject.AddComponent<AudioSource>();
        speaker.playOnAwake = false;
        speaker.spatialBlend = 1f;
        speaker.rolloffMode = AudioRolloffMode.Logarithmic;
        speaker.minDistance = 1.2f;
        speaker.maxDistance = 25f;

#if UNITY_EDITOR
        if (fallbackTrack == null)
        {
            fallbackTrack = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/track_1.mp3");
        }
#endif

        EnsureCollider();
    }

    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        Renderer bodyRenderer = GetComponentInChildren<Renderer>();
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        if (bodyRenderer != null)
        {
            Bounds world = bodyRenderer.bounds;
            box.center = transform.InverseTransformPoint(world.center);
            Vector3 lossy = transform.lossyScale;
            box.size = new Vector3(
                Mathf.Abs(lossy.x) > 0.0001f ? world.size.x / Mathf.Abs(lossy.x) : world.size.x,
                Mathf.Abs(lossy.y) > 0.0001f ? world.size.y / Mathf.Abs(lossy.y) : world.size.y,
                Mathf.Abs(lossy.z) > 0.0001f ? world.size.z / Mathf.Abs(lossy.z) : world.size.z);
        }
    }

    public override void Interact()
    {
        if (isPlaying)
        {
            return;
        }

        if (isBroken)
        {
            StartCoroutine(BrokenAttemptRoutine());
            return;
        }

        hasPlayedOnce = true;
        StartCoroutine(PlayOnceRoutine());
        base.Interact();
    }

    private IEnumerator PlayOnceRoutine()
    {
        isPlaying = true;
        Prompt = "The radio is playing...";
        CanInteract = false;

        bool useStream = Random.value < 0.5f;
        bool streamStarted = false;

        if (useStream)
        {
            yield return TryStartStream(result => streamStarted = result);
        }

        if (!streamStarted)
        {
            if (fallbackTrack != null)
            {
                speaker.clip = fallbackTrack;
                speaker.loop = true;
                speaker.volume = 0.85f;
                speaker.Play();
            }
            else
            {
                speaker.clip = HorrorAudio.RadioStatic();
                speaker.loop = true;
                speaker.volume = 0.6f;
                speaker.Play();
            }
        }

        yield return new WaitForSeconds(playDuration);

        StopAllPlayback();
        speaker.PlayOneShot(HorrorAudio.Pop(), 1f);
        speaker.PlayOneShot(HorrorAudio.RadioStatic(), 0.35f);
        EmitSmoke(18);
        isBroken = true;
        isPlaying = false;
        Prompt = "Press E to turn on the radio";
        CanInteract = true;
    }

    private IEnumerator TryStartStream(System.Action<bool> onDone)
    {
        if (streamPlayer == null)
        {
            streamPlayer = gameObject.AddComponent<VideoPlayer>();
            streamPlayer.playOnAwake = false;
            streamPlayer.renderMode = VideoRenderMode.APIOnly;
            streamPlayer.source = VideoSource.Url;
            streamPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        }

        bool failed = false;
        bool prepared = false;
        streamPlayer.url = streamUrl;
        streamPlayer.errorReceived += (_, message) => failed = true;
        streamPlayer.prepareCompleted += _ => prepared = true;

        // Audio track count is only known once the URL is prepared, so enabling
        // and routing the track has to wait until after Prepare() completes —
        // doing it beforehand silently no-ops and we'd "succeed" into silence.
        streamPlayer.Prepare();

        float waited = 0f;
        while (!prepared && !failed && waited < streamPrepareTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!prepared || failed || streamPlayer.audioTrackCount == 0)
        {
            streamPlayer.Stop();
            onDone(false);
            yield break;
        }

        streamPlayer.EnableAudioTrack(0, true);
        streamPlayer.SetTargetAudioSource(0, speaker);
        speaker.volume = 0.9f;
        streamPlayer.Play();

        // Give playback a beat to actually spin up; if it stalls, drop to the
        // local fallback track instead of leaving the radio mute.
        float settle = 0f;
        while (settle < 1.5f && !failed)
        {
            if (streamPlayer.isPlaying)
            {
                onDone(true);
                yield break;
            }

            settle += Time.deltaTime;
            yield return null;
        }

        streamPlayer.Stop();
        onDone(false);
    }

    private void StopAllPlayback()
    {
        if (streamPlayer != null && streamPlayer.isPlaying)
        {
            streamPlayer.Stop();
        }

        if (speaker.isPlaying)
        {
            speaker.Stop();
        }

        speaker.loop = false;
        speaker.clip = null;
    }

    private IEnumerator BrokenAttemptRoutine()
    {
        isPlaying = true;
        CanInteract = false;
        Prompt = "The radio is broken";

        speaker.PlayOneShot(HorrorAudio.RadioStatic(), 0.5f);
        EmitSmoke(40);

        if (innardsGlow == null)
        {
            GameObject glowObject = new GameObject("Radio Innards Glow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = Vector3.zero;
            innardsGlow = glowObject.AddComponent<Light>();
            innardsGlow.type = LightType.Point;
            innardsGlow.color = new Color(1f, 0.45f, 0.1f, 1f);
            innardsGlow.range = 2f;
            innardsGlow.intensity = 0f;
        }

        for (float elapsed = 0f; elapsed < 1.6f; elapsed += Time.deltaTime)
        {
            innardsGlow.intensity = Mathf.PerlinNoise(Time.time * 14f, 0.5f) * 2.4f * (1f - elapsed / 1.6f);
            yield return null;
        }

        innardsGlow.intensity = 0f;
        speaker.PlayOneShot(HorrorAudio.Pop(), 0.8f);

        isPlaying = false;
        CanInteract = true;
        Prompt = "The radio is broken";
    }

    private void EmitSmoke(int burst)
    {
        if (smoke == null)
        {
            smoke = BuildSmokeSystem();
        }

        smoke.Emit(burst);
        if (!smoke.isPlaying)
        {
            smoke.Play();
        }
    }

    private ParticleSystem BuildSmokeSystem()
    {
        GameObject smokeObject = new GameObject("Radio Smoke");
        smokeObject.transform.SetParent(transform, false);
        Renderer bodyRenderer = GetComponentInChildren<Renderer>();
        smokeObject.transform.position = bodyRenderer != null
            ? bodyRenderer.bounds.center + Vector3.up * (bodyRenderer.bounds.extents.y * 0.8f)
            : transform.position + Vector3.up * 0.2f;

        ParticleSystem system = smokeObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.16f, 0.16f, 0.16f, 0.55f),
            new Color(0.32f, 0.32f, 0.32f, 0.4f));
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.02f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 7f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.05f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.6f, 1f), new Keyframe(1f, 1.7f)));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        ParticleSystemRenderer renderer = smokeObject.GetComponent<ParticleSystemRenderer>();
        renderer.material = CreateSmokeMaterial();
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return system;
    }

    public static Material CreateSmokeMaterial()
    {
        Texture2D puff = CreatePuffTexture();
        bool hdrpActive = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
        Shader shader = hdrpActive ? Shader.Find("HDRP/Unlit") : Shader.Find("Particles/Standard Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader) { name = "Procedural Smoke" };

        if (hdrpActive && material.HasProperty("_SurfaceType"))
        {
            material.SetFloat("_SurfaceType", 1f); // transparent
            material.SetFloat("_BlendMode", 0f); // alpha
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_UnlitColorMap"))
            {
                material.SetTexture("_UnlitColorMap", puff);
                material.SetColor("_UnlitColor", Color.white);
            }

            try
            {
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material);
            }
            catch
            {
                // validation helper unavailable; keywords below still cover the common case
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            material.mainTexture = puff;
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 2f);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        return material;
    }

    private static Texture2D CreatePuffTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Smoke Puff" };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - size * 0.5f) / (size * 0.5f);
                float dy = (y - size * 0.5f) / (size * 0.5f);
                float radial = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                float wobble = Mathf.PerlinNoise(x * 0.13f, y * 0.13f) * 0.5f + 0.5f;
                float alpha = Mathf.Pow(radial, 1.7f) * wobble;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
