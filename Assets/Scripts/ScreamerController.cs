using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Anything the gun raycast can damage.
public class ShootableTarget : MonoBehaviour
{
    public event System.Action<Vector3> Shot;

    public virtual void OnShot(Vector3 hitPoint)
    {
        Shot?.Invoke(hitPoint);
    }
}

// A shadow figure that bursts out in front of the player with a scream and
// charges. Shoot it before it reaches you or die. Triggered by the quest
// director at scary moments.
public class ScreamerController : MonoBehaviour
{
    [SerializeField] private GameObject figurePrefab;
    [SerializeField] private float spawnDistance = 13f;
    [SerializeField] private float chargeSpeed = 4.2f;
    [SerializeField] private float killDistance = 1.3f;
    // How many rounds it takes to put the screamer down.
    [SerializeField] private int requiredHits = 8;

    private Transform player;
    private Camera playerCamera;
    private bool screamerActive;

    private void Awake()
    {
        // Build-safe path: AssetDatabase below is editor-only.
        if (figurePrefab == null)
        {
            YardAssetLibrary lib = YardAssetLibrary.Instance;
            if (lib != null)
            {
                figurePrefab = lib.antonPrefab;
            }
        }

#if UNITY_EDITOR
        if (figurePrefab == null)
        {
            figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/anton.prefab");
        }
#endif
        FirstPersonController controller = FindObjectOfType<FirstPersonController>();
        player = controller != null ? controller.transform : null;
        playerCamera = Camera.main;
    }

    public bool IsScreamerActive => screamerActive;

    public static bool PlayerCarriesGun()
    {
        foreach (GunWeaponController gun in FindObjectsOfType<GunWeaponController>())
        {
            PickupInteractable pickup = gun.GetComponent<PickupInteractable>();
            if (pickup != null && pickup.IsHeld)
            {
                return true;
            }
        }

        return false;
    }

    public void TriggerScreamer()
    {
        if (screamerActive || player == null || figurePrefab == null ||
            AntonchikFenceEncounter.SequenceActive || GameOverScreen.IsActive)
        {
            return;
        }

        StartCoroutine(ScreamerRoutine());
    }

    private IEnumerator ScreamerRoutine()
    {
        screamerActive = true;

        Vector3 forward = playerCamera != null ? playerCamera.transform.forward : player.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 spawnPoint = player.position + forward * spawnDistance;
        if (Physics.Raycast(spawnPoint + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            spawnPoint = hit.point;
        }

        GameObject figure = Instantiate(figurePrefab, spawnPoint, Quaternion.LookRotation(-forward, Vector3.up));
        figure.name = "Тень";
        figure.transform.localScale *= 1.12f;
        BuiltinPipelineCompatibility.PatchSpawnedObject(figure);
        DarkenFigure(figure);
        FreezeAnimation(figure);
        EnsureFigureCollider(figure);

        ShootableTarget target = figure.AddComponent<ShootableTarget>();

        AudioSource screamSource = figure.AddComponent<AudioSource>();
        screamSource.spatialBlend = 0.6f;
        screamSource.minDistance = 4f;
        screamSource.maxDistance = 60f;
        screamSource.PlayOneShot(HorrorAudio.Scream(), 1f);

        GameObject eyeLightObject = new GameObject("Screamer Glow");
        eyeLightObject.transform.SetParent(figure.transform, false);
        eyeLightObject.transform.localPosition = Vector3.up * 1.7f;
        Light eyeLight = eyeLightObject.AddComponent<Light>();
        eyeLight.type = LightType.Point;
        eyeLight.color = new Color(0.9f, 0.06f, 0.04f, 1f);
        eyeLight.intensity = 3.5f;
        eyeLight.range = 7f;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = eyeLightObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(2200f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
        }

        // Each round staggers it; it only goes down once it has soaked up
        // requiredHits. Track hits taken and knock it back on every hit.
        int hits = requiredHits < 1 ? 1 : requiredHits;
        int hitsTaken = 0;
        Vector3 knockback = Vector3.zero;
        target.Shot += hitPoint =>
        {
            hitsTaken++;
            Vector3 away = figure != null && player != null
                ? (figure.transform.position - player.position)
                : Vector3.zero;
            away.y = 0f;
            knockback += away.normalized * 0.45f;
            screamSource.PlayOneShot(HorrorAudio.Scream(), 0.55f);
            eyeLight.intensity = 6.5f;
        };

        // brief freeze so the player registers it, then the charge
        yield return new WaitForSeconds(0.55f);

        while (hitsTaken < hits && figure != null && player != null)
        {
            Vector3 toPlayer = player.position - figure.transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            if (distance <= killDistance)
            {
                screamSource.PlayOneShot(HorrorAudio.Scream(), 1f);
                yield return new WaitForSeconds(0.15f);
                GameOverScreen.Show("Тень добралась до тебя. Надо было стрелять.");
                Destroy(figure, 2f);
                screamerActive = false;
                yield break;
            }

            // ease any pending knockback into the position so hits visibly jolt it
            Vector3 step = toPlayer.normalized * (chargeSpeed * Time.deltaTime);
            if (knockback.sqrMagnitude > 0.0001f)
            {
                Vector3 kb = Vector3.ClampMagnitude(knockback, 6f * Time.deltaTime);
                step += kb;
                knockback -= kb;
            }

            figure.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            figure.transform.position += step;
            eyeLight.intensity = 2.5f + Mathf.PingPong(Time.time * 18f, 2.5f);
            yield return null;
        }

        if (figure != null)
        {
            // killed: shriek, topple backwards onto the ground while sinking and
            // fading the eye glow — a deliberate death rather than a clean vanish
            screamSource.PlayOneShot(HorrorAudio.Scream(), 1f);
            AudioSource.PlayClipAtPoint(HorrorAudio.Squelch(), figure.transform.position, 1f);

            Vector3 fallAxis = figure.transform.right;
            float death = 0f;
            const float deathDuration = 1.6f;
            while (death < deathDuration && figure != null)
            {
                death += Time.deltaTime;
                float t = death / deathDuration;
                // topple backward over the first ~0.6s, then keep sinking
                if (t < 0.45f)
                {
                    figure.transform.Rotate(fallAxis, 200f * Time.deltaTime, Space.World);
                }

                figure.transform.position += Vector3.down * (1.5f * Time.deltaTime * Mathf.Clamp01(t * 2f));
                eyeLight.intensity = Mathf.Max(0f, eyeLight.intensity - Time.deltaTime * 5f);
                yield return null;
            }

            if (figure != null)
            {
                Destroy(figure);
            }
        }

        screamerActive = false;
    }

    private static void DarkenFigure(GameObject figure)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer renderer in figure.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(0.04f, 0.02f, 0.02f, 1f));
            block.SetColor("_Color", new Color(0.04f, 0.02f, 0.02f, 1f));
            renderer.SetPropertyBlock(block);
        }
    }

    private static void FreezeAnimation(GameObject figure)
    {
        Animator animator = figure.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            // frozen mid-pose reads as deeply wrong, which is the point
            animator.speed = 0f;
        }
    }

    private static void EnsureFigureCollider(GameObject figure)
    {
        // anton's model root carries a big authored offset (~10 units), so a
        // capsule at the figure origin sits metres away from the visible body and
        // the gun ray never hits it — shots wouldn't register and it could never
        // be killed. Wrap the actual renderer bounds so the collider overlaps the
        // body the player is aiming at. The animator is frozen, so the pose (and
        // therefore the bounds) is static.
        Renderer[] renderers = figure.GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds bounds = new Bounds(figure.transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderers[i].bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (!found)
        {
            // no renderers to measure: fall back to a body-sized capsule
            CapsuleCollider capsule = figure.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up * 0.95f;
            capsule.height = 1.9f;
            capsule.radius = 0.35f;
            return;
        }

        BoxCollider box = figure.AddComponent<BoxCollider>();
        box.center = figure.transform.InverseTransformPoint(bounds.center);
        Vector3 scale = figure.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }
}
