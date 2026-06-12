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

    private Transform player;
    private Camera playerCamera;
    private bool screamerActive;

    private void Awake()
    {
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
        bool wasShot = false;
        target.Shot += _ => wasShot = true;

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

        // brief freeze so the player registers it, then the charge
        yield return new WaitForSeconds(0.55f);

        while (!wasShot && figure != null && player != null)
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

            figure.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            figure.transform.position += toPlayer.normalized * (chargeSpeed * Time.deltaTime);
            eyeLight.intensity = 2.5f + Mathf.PingPong(Time.time * 18f, 2.5f);
            yield return null;
        }

        if (figure != null)
        {
            // shot down: squelch, collapse into the ground, fade the light
            AudioSource.PlayClipAtPoint(HorrorAudio.Squelch(), figure.transform.position, 1f);
            float sink = 0f;
            while (sink < 1.4f && figure != null)
            {
                sink += Time.deltaTime;
                figure.transform.position += Vector3.down * (1.6f * Time.deltaTime);
                figure.transform.Rotate(0f, 220f * Time.deltaTime, 0f, Space.World);
                eyeLight.intensity = Mathf.Max(0f, eyeLight.intensity - Time.deltaTime * 6f);
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
        if (figure.GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        CapsuleCollider capsule = figure.AddComponent<CapsuleCollider>();
        capsule.center = Vector3.up * 0.95f;
        capsule.height = 1.9f;
        capsule.radius = 0.35f;
    }
}
