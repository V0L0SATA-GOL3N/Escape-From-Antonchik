using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Spawns huge, scary scalapendras around the yard that screech and charge the
// player like a screamer. The GLB imports with broken glTF materials (invisible
// in HDRP) so each spawn gets a guaranteed-visible dark, glistening, red-glowing
// override material — it reads as a monstrous silhouette regardless of the
// import. The model root's localPosition is garbage from the clip, so the
// container does the moving and the model is pinned/ground-clamped every frame.
public class ScalapendraSpawner : MonoBehaviour
{
    [SerializeField] private GameObject scalapendraPrefab;
    [SerializeField] private RuntimeAnimatorController scalapendraController;
    [SerializeField] private string animationStateName = "CINEMA_4D_Main";
    [SerializeField] private int maxAlive = 2;
    [SerializeField] private float minSpawnDistance = 10f;
    [SerializeField] private float maxSpawnDistance = 22f;
    [SerializeField] private float spawnIntervalMin = 12f;
    [SerializeField] private float spawnIntervalMax = 26f;
    [SerializeField] private float chargeSpeed = 2.6f;
    [SerializeField] private float lifeTime = 22f;
    // How big the creature is. The prefab scale reads as a small bug; this
    // blows it up into a horse-sized horror.
    [SerializeField] private float creatureScale = 4.5f;

    private Transform player;
    private readonly List<GameObject> alive = new List<GameObject>();
    private bool running;

    public int AliveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i] != null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private void Awake()
    {
#if UNITY_EDITOR
        if (scalapendraPrefab == null)
        {
            scalapendraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/scalapendra.prefab");
        }

        if (scalapendraController == null)
        {
            scalapendraController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/3d/scalapendra.controller");
        }
#endif
    }

    public void Activate()
    {
        if (running)
        {
            return;
        }

        running = true;
        FirstPersonController controller = FindObjectOfType<FirstPersonController>();
        player = controller != null ? controller.transform : null;
        StartCoroutine(SpawnLoop());
    }

    public void Deactivate()
    {
        running = false;
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            if (alive[i] != null)
            {
                Destroy(alive[i]);
            }
        }

        alive.Clear();
    }

    private IEnumerator SpawnLoop()
    {
        // first one shows up fairly quickly to set the mood
        yield return new WaitForSeconds(Random.Range(4f, 9f));

        while (running)
        {
            alive.RemoveAll(item => item == null);
            if (alive.Count < maxAlive && player != null && !AntonchikFenceEncounter.SequenceActive)
            {
                TrySpawnOne();
            }

            yield return new WaitForSeconds(Random.Range(spawnIntervalMin, spawnIntervalMax));
        }
    }

    private void TrySpawnOne()
    {
        if (scalapendraPrefab == null)
        {
            return;
        }

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(minSpawnDistance, maxSpawnDistance);
            Vector3 candidate = player.position + new Vector3(ring.x, 0f, ring.y);
            Vector3 origin = candidate + Vector3.up * 6f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            if (Vector3.Distance(hit.point, player.position) < minSpawnDistance * 0.8f)
            {
                continue;
            }

            GameObject container = new GameObject("Scalapendra Screamer");
            container.transform.position = hit.point + Vector3.up * 0.01f;
            container.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            container.transform.localScale = Vector3.one * creatureScale;

            GameObject model = Instantiate(scalapendraPrefab, container.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            BuiltinPipelineCompatibility.PatchSpawnedObject(model);

            ScalapendraScreamer screamer = container.AddComponent<ScalapendraScreamer>();
            screamer.Initialise(model.transform, player, chargeSpeed, lifeTime, scalapendraController, animationStateName);

            alive.Add(container);
            return;
        }
    }
}

public class ScalapendraScreamer : MonoBehaviour
{
    private Transform model;
    private Transform player;
    private float speed;
    private float dieAt;
    private AudioSource voice;
    private Vector3 pinnedLocalPosition;
    private Renderer[] modelRenderers;
    private Light glow;
    private bool retreating;
    private bool scaredThisLunge;

    public void Initialise(Transform modelRoot, Transform playerTransform, float chargeSpeed, float lifeTime,
        RuntimeAnimatorController controller, string stateName)
    {
        model = modelRoot;
        player = playerTransform;
        speed = chargeSpeed;
        dieAt = Time.time + lifeTime;

        // The prefab ships with the model node m_IsActive=0, so the clone spawns
        // disabled (invisible, no animation). Force the whole hierarchy on.
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.SetActive(true);
        }

        pinnedLocalPosition = model.localPosition;
        modelRenderers = model.GetComponentsInChildren<Renderer>(true);

        MakeScary();
        StartAnimation(controller, stateName);

        voice = gameObject.AddComponent<AudioSource>();
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Logarithmic;
        voice.minDistance = 3f;
        voice.maxDistance = 45f;
        voice.volume = 1f;
        voice.pitch = Random.Range(0.7f, 0.9f);
        // a screech announces it, then a low chittering loop underneath
        voice.PlayOneShot(HorrorAudio.Scream(), 1f);
        StartCoroutine(ChitterLoop());
    }

    private IEnumerator ChitterLoop()
    {
        yield return new WaitForSeconds(0.5f);
        AudioSource bed = gameObject.AddComponent<AudioSource>();
        bed.clip = HorrorAudio.Chitter();
        bed.loop = true;
        bed.spatialBlend = 1f;
        bed.rolloffMode = AudioRolloffMode.Logarithmic;
        bed.minDistance = 2f;
        bed.maxDistance = 35f;
        bed.volume = 0.85f;
        bed.pitch = Random.Range(0.8f, 1.05f);
        bed.Play();
    }

    private void MakeScary()
    {
        // keep the model's original textures; just make sure every renderer is
        // actually drawing (the prefab ships some of them disabled)
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            if (modelRenderers[i] == null)
            {
                continue;
            }

            modelRenderers[i].enabled = true;

            // Skinned meshes ship an oversized precomputed AABB; without this the
            // ground clamp reads a "lowest point" far below the body and floats
            // the creature ~1m off the floor. Force tight, live bounds.
            if (modelRenderers[i] is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
            }
        }

        // a dim red rim glow reads as a threat in the dark without washing out
        // the creature's own texture
        GameObject glowObject = new GameObject("Scalapendra Glow");
        glowObject.transform.SetParent(transform, false);
        glowObject.transform.localPosition = Vector3.up * 0.4f;
        glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.95f, 0.2f, 0.12f, 1f);
        glow.range = 5f;
        glow.intensity = 1.1f;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = glowObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(450f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
        }
    }

    private void StartAnimation(RuntimeAnimatorController controller, string stateName)
    {
        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            if (!string.IsNullOrWhiteSpace(stateName) && animator.runtimeAnimatorController != null)
            {
                animator.Play(stateName, 0, Random.value);
            }

            return;
        }

        Animation legacy = model.GetComponentInChildren<Animation>();
        if (legacy != null && legacy.clip != null)
        {
            legacy.wrapMode = WrapMode.Loop;
            legacy.Play();
        }
    }

    private void Update()
    {
        if (player == null || Time.time >= dieAt)
        {
            BurrowAway();
            return;
        }

        if (glow != null)
        {
            glow.intensity = 0.8f + Mathf.PingPong(Time.time * 8f, 0.7f);
        }

        if (retreating)
        {
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (distance <= 1.6f && !scaredThisLunge)
        {
            scaredThisLunge = true;
            StartCoroutine(LungeAndScare());
            return;
        }

        if (distance > 0.05f)
        {
            Quaternion target = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 6f);
            transform.position += transform.forward * (speed * Time.deltaTime);
        }

        StickToGround();
    }

    private void StickToGround()
    {
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 position = transform.position;
            position.y = hit.point.y + 0.01f;
            transform.position = position;
        }
    }

    private IEnumerator LungeAndScare()
    {
        // final pounce: surge in, screech, flash the screen red, then burrow off
        voice.PlayOneShot(HorrorAudio.Scream(), 1f);
        ScreenFlash.Flash(new Color(0.5f, 0f, 0f, 0.7f), 0.18f);

        float t = 0f;
        Vector3 lungeDir = transform.forward;
        while (t < 0.35f)
        {
            t += Time.deltaTime;
            transform.position += lungeDir * (6f * Time.deltaTime);
            StickToGround();
            yield return null;
        }

        retreating = true;
        BurrowAway();
    }

    private void BurrowAway()
    {
        if (this == null)
        {
            return;
        }

        StartCoroutine(BurrowRoutine());
    }

    private IEnumerator BurrowRoutine()
    {
        enabled = false;
        if (voice != null)
        {
            AudioSource.PlayClipAtPoint(HorrorAudio.Squelch(), transform.position, 1f);
        }

        float t = 0f;
        while (t < 1.2f)
        {
            t += Time.deltaTime;
            transform.position += Vector3.down * (1.8f * Time.deltaTime);
            transform.Rotate(0f, 260f * Time.deltaTime, 0f, Space.World);
            if (glow != null)
            {
                glow.intensity = Mathf.Max(0f, glow.intensity - Time.deltaTime * 5f);
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    private void LateUpdate()
    {
        // the imported clip writes garbage into the model root localPosition;
        // pin it, then lift so the centred pivot doesn't bury the creature.
        if (model == null)
        {
            return;
        }

        model.localPosition = pinnedLocalPosition;

        float lowest = LowestRendererY();
        if (!float.IsInfinity(lowest))
        {
            float gap = transform.position.y - lowest;
            // gap is in world units; convert to the container's local Y scale
            float localScaleY = Mathf.Abs(transform.lossyScale.y);
            if (localScaleY > 0.0001f)
            {
                model.localPosition = pinnedLocalPosition + Vector3.up * (gap / localScaleY);
            }
        }
    }

    private float LowestRendererY()
    {
        if (modelRenderers == null)
        {
            return float.PositiveInfinity;
        }

        float lowest = float.PositiveInfinity;
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            if (modelRenderers[i] == null || !modelRenderers[i].enabled)
            {
                continue;
            }

            float min = modelRenderers[i].bounds.min.y;
            if (min < lowest)
            {
                lowest = min;
            }
        }

        return lowest;
    }
}

// Tiny reusable full-screen colour flash for jumpscares.
public static class ScreenFlash
{
    public static void Flash(Color color, float duration)
    {
        GameObject host = new GameObject("Screen Flash");
        ScreenFlashRunner runner = host.AddComponent<ScreenFlashRunner>();
        runner.Begin(color, duration);
    }
}

public class ScreenFlashRunner : MonoBehaviour
{
    public void Begin(Color color, float duration)
    {
        StartCoroutine(Run(color, duration));
    }

    private IEnumerator Run(Color color, float duration)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8000;

        GameObject imageObject = new GameObject("Flash");
        imageObject.transform.SetParent(transform, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Color c = color;
            c.a = Mathf.Lerp(color.a, 0f, t / duration);
            image.color = c;
            yield return null;
        }

        Destroy(gameObject);
    }
}
