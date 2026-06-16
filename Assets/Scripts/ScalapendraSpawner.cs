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
    // How many rounds it takes to kill one of these.
    [SerializeField] private int hitsToKill = 3;
    // How big the creature is. The prefab scale reads as a small bug; this
    // blows it up into a horse-sized horror.
    [SerializeField] private float creatureScale = 4.5f;

    private Transform player;
    private AntonchikFenceEncounter yard;
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
        // Build-safe path: pull from the baked Resources library so the creatures
        // still spawn in player builds (AssetDatabase below is editor-only).
        YardAssetLibrary lib = YardAssetLibrary.Instance;
        if (lib != null)
        {
            if (scalapendraPrefab == null) scalapendraPrefab = lib.scalapendraPrefab;
            if (scalapendraController == null) scalapendraController = lib.scalapendraController;
        }

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
        yard = GetComponent<AntonchikFenceEncounter>();
        if (yard == null)
        {
            yard = FindObjectOfType<AntonchikFenceEncounter>();
        }

        StartCoroutine(SpawnLoop());
    }

    // Halt new spawns without disturbing the ones already roaming (cheat "stopsc").
    public void StopSpawning()
    {
        running = false;
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
            if (alive.Count < maxAlive && player != null && !AntonchikFenceEncounter.CreaturesPaused)
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

            Vector3 spot;
            if (yard != null)
            {
                // Use the same yard map the key spawn uses: only land on the real
                // walkable floor, never on a roof, the exit platform or inside a
                // building/lamp footprint.
                if (!yard.TrySnapSpawnToGround(candidate, out spot))
                {
                    continue;
                }
            }
            else
            {
                Vector3 origin = candidate + Vector3.up * 6f;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                spot = hit.point;
            }

            if (Vector3.Distance(spot, player.position) < minSpawnDistance * 0.8f)
            {
                continue;
            }

            GameObject container = new GameObject("Scalapendra Screamer");
            container.transform.position = spot + Vector3.up * 0.01f;
            container.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            container.transform.localScale = Vector3.one * creatureScale;

            GameObject model = Instantiate(scalapendraPrefab, container.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            BuiltinPipelineCompatibility.PatchSpawnedObject(model);

            ScalapendraScreamer screamer = container.AddComponent<ScalapendraScreamer>();
            screamer.Initialise(model.transform, player, chargeSpeed, lifeTime, scalapendraController, animationStateName, hitsToKill, yard);

            alive.Add(container);
            return;
        }
    }
}

public class ScalapendraScreamer : ShootableTarget
{
    private Transform model;
    private Transform player;
    private AntonchikFenceEncounter yard;
    private float speed;
    private float dieAt;
    private AudioSource voice;
    private Vector3 pinnedLocalPosition;
    private Renderer[] modelRenderers;
    private Light glow;
    private Animator animator;
    private BoxCollider hitBox;
    private bool retreating;
    private bool scaredThisLunge;
    private int health;
    private bool dying;

    public void Initialise(Transform modelRoot, Transform playerTransform, float chargeSpeed, float lifeTime,
        RuntimeAnimatorController controller, string stateName, int hitsToKill, AntonchikFenceEncounter yardMap)
    {
        model = modelRoot;
        player = playerTransform;
        yard = yardMap;
        speed = chargeSpeed;
        dieAt = Time.time + lifeTime;
        health = Mathf.Max(1, hitsToKill);

        // The prefab ships with the model node m_IsActive=0, so the clone spawns
        // disabled (invisible, no animation). Force the whole hierarchy on.
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.SetActive(true);
        }

        pinnedLocalPosition = model.localPosition;
        modelRenderers = model.GetComponentsInChildren<Renderer>(true);

        MakeScary();
        AddHitCollider();
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

    // Give the gun raycast something to hit. The prefab ships with no collider,
    // and unlike the (frozen) Antonchik screamer this creature keeps animating
    // and is re-pinned/ground-clamped every frame, so a collider sized once at
    // spawn immediately goes stale. Put the box on the container and refresh it
    // each LateUpdate (UpdateHitBox) so it tracks the live body.
    private void AddHitCollider()
    {
        hitBox = gameObject.AddComponent<BoxCollider>();
        UpdateHitBox();

        // The body is moved by transform every frame, so as a solid collider it
        // would shove the player's rigidbody on contact. Ignore collisions against
        // the player so the creature can never apply any force/толчок — it still
        // takes gun raycasts (which ignore this) and damages via the lunge.
        if (player != null)
        {
            foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>(true))
            {
                if (playerCollider != null)
                {
                    Physics.IgnoreCollision(hitBox, playerCollider, true);
                }
            }
        }
    }

    private void UpdateHitBox()
    {
        if (hitBox == null || modelRenderers == null)
        {
            return;
        }

        bool found = false;
        Bounds worldBounds = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            if (modelRenderers[i] == null || !modelRenderers[i].enabled)
            {
                continue;
            }

            if (!found)
            {
                worldBounds = modelRenderers[i].bounds;
                found = true;
            }
            else
            {
                worldBounds.Encapsulate(modelRenderers[i].bounds);
            }
        }

        if (!found)
        {
            return;
        }

        hitBox.center = transform.InverseTransformPoint(worldBounds.center);
        Vector3 scale = transform.lossyScale;
        hitBox.size = new Vector3(
            worldBounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            worldBounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            worldBounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }

    private void StartAnimation(RuntimeAnimatorController controller, string stateName)
    {
        animator = model.GetComponentInChildren<Animator>();
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

    // Hit by the gun raycast. Soak up rounds; flinch on each, die on the last.
    public override void OnShot(Vector3 hitPoint)
    {
        base.OnShot(hitPoint);
        if (dying)
        {
            return;
        }

        health--;
        if (health <= 0)
        {
            StartCoroutine(DeathRoutine());
        }
        else
        {
            StartCoroutine(Flinch(hitPoint));
        }
    }

    private IEnumerator Flinch(Vector3 hitPoint)
    {
        // pain screech, a flash of the glow and a small shove away from the shot
        if (voice != null)
        {
            voice.PlayOneShot(HorrorAudio.Scream(), 0.7f);
        }

        Vector3 knockback = transform.position - hitPoint;
        knockback.y = 0f;
        knockback = knockback.sqrMagnitude > 0.0001f ? knockback.normalized : -transform.forward;

        float t = 0f;
        while (t < 0.18f && !dying)
        {
            t += Time.deltaTime;
            transform.position += knockback * (2.2f * Time.deltaTime);
            if (glow != null)
            {
                glow.intensity = 4f;
            }

            StickToGround();
            yield return null;
        }
    }

    private IEnumerator DeathRoutine()
    {
        dying = true;
        retreating = true;
        enabled = false;

        // Freeze the walk clip: LateUpdate (which re-pins the root) no longer runs
        // once this behaviour is disabled, and the clip writes garbage into the
        // model root — a frozen pose keeps the corpse where we put it.
        if (animator != null)
        {
            animator.speed = 0f;
        }

        if (voice != null)
        {
            voice.PlayOneShot(HorrorAudio.Scream(), 1f);
        }

        AudioSource.PlayClipAtPoint(HorrorAudio.Squelch(), transform.position, 1f);

        // death throes: violent diminishing writhe, rolling onto its side
        float t = 0f;
        const float writheDuration = 0.9f;
        float roll = 0f;
        while (t < writheDuration)
        {
            t += Time.deltaTime;
            float energy = 1f - (t / writheDuration);
            float twist = Mathf.Sin(t * 38f) * 220f * energy;
            transform.Rotate(0f, twist * Time.deltaTime, 0f, Space.World);
            // tip over onto its side as it dies
            roll = Mathf.Lerp(roll, 80f, Time.deltaTime * 4f);
            if (model != null)
            {
                model.localPosition = pinnedLocalPosition;
                model.localRotation = Quaternion.Euler(0f, 0f, roll);
            }

            if (glow != null)
            {
                glow.intensity = Mathf.Max(0f, glow.intensity - Time.deltaTime * 1.5f);
            }

            yield return null;
        }

        // sink the corpse into the ground and fade out
        float sink = 0f;
        while (sink < 1.2f)
        {
            sink += Time.deltaTime;
            transform.position += Vector3.down * (1.4f * Time.deltaTime);
            if (model != null)
            {
                model.localPosition = pinnedLocalPosition;
            }

            if (glow != null)
            {
                glow.intensity = Mathf.Max(0f, glow.intensity - Time.deltaTime * 4f);
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    private void Update()
    {
        if (dying)
        {
            return;
        }

        // Freeze in place during dialog/cutscenes and the car escape — no moving,
        // lunging or expiring until play resumes.
        if (AntonchikFenceEncounter.CreaturesPaused)
        {
            return;
        }

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
            // Steer around the house/lamps instead of charging straight through
            // them, using the same yard map the key spawn uses.
            Vector3 moveDir = yard != null
                ? yard.SteerAroundBuildings(transform.position, toPlayer.normalized, 3f)
                : toPlayer.normalized;

            if (moveDir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 6f);
                transform.position += moveDir * (speed * Time.deltaTime);
            }
        }

        StickToGround();
    }

    private void StickToGround()
    {
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        // Temporarily drop our own hit box so the downward ray reads the ground,
        // not the creature's body (which would make it stick to itself).
        bool hadBox = hitBox != null && hitBox.enabled;
        if (hadBox)
        {
            hitBox.enabled = false;
        }

        bool grounded = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore);

        if (hadBox)
        {
            hitBox.enabled = true;
        }

        if (grounded)
        {
            Vector3 position = transform.position;
            position.y = hit.point.y + 0.01f;
            transform.position = position;
        }
    }

    private IEnumerator LungeAndScare()
    {
        // final pounce: surge in, screech, bite you for 3 hearts (game over if
        // that empties the bar), then burrow off
        voice.PlayOneShot(HorrorAudio.Scream(), 1f);
        PlayerHealth.Damage(3, "Сколопендра добралась до тебя.");
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

        // While dying the death routine drives the model's roll and the corpse
        // is sinking — don't ground-clamp it back up.
        if (dying)
        {
            return;
        }

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

        // Keep the hit collider wrapped around the now-positioned body so the gun
        // ray can land on it. Done after the ground clamp so it uses final bounds.
        UpdateHitBox();
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
