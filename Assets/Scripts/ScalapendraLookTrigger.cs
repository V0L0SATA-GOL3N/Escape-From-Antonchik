using System.Collections;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

public class ScalapendraLookTrigger : MonoBehaviour
{
    [SerializeField] private Camera lookCamera;
    [SerializeField] private Transform scalapendraRoot;
    [SerializeField] private Animator scalapendraAnimator;
    [SerializeField] private RuntimeAnimatorController scalapendraController;
    [SerializeField] private string scalapendraGlbProjectPath = "Assets/3d/scalapendra.glb";
    [SerializeField] private string animationStateName = "CINEMA_4D_Main";
    [SerializeField] private float lookDistance = 30f;
    [SerializeField] private bool triggerWhenAnyPartInCamera = true;
    [SerializeField] private float targetWorldX = -2f;
    private float moveDuration = 5f;
    [SerializeField] private float destroyDelay = 0.1f;
    [SerializeField] private Vector3 spawnWorldOffset = new Vector3(0.25f, -0.03f, 4.01f);
    [SerializeField] private Vector3 spawnEulerAngles = new Vector3(0f, 105.5f, 0f);
    [SerializeField] private Vector3 spawnScale = new Vector3(0.01470784f, 0.01470784f, 0.01470784f);

    private bool triggered;
    private bool scalapendraReady;
    private bool scalapendraLoading;
    private Animation legacyAnimation;

    private async void Start()
    {
        await EnsureScalapendraLoadedAsync();
        SetAnimationEnabled(false);
    }

    private void Update()
    {
        if (triggered)
        {
            return;
        }

        if (!scalapendraReady || lookCamera == null)
        {
            return;
        }

        if (!IsTriggerTargetVisible())
        {
            return;
        }

        triggered = true;
        StartCoroutine(TriggerRoutine());
    }

    private IEnumerator TriggerRoutine()
    {
        if (!scalapendraReady)
        {
            yield break;
        }

        Transform target = scalapendraRoot;
        if (target == null)
        {
            Debug.LogError("ScalapendraLookTrigger has no scalapendra target to move.", this);
            yield break;
        }

        ApplySpawnTransform(target);

        if (scalapendraAnimator != null)
        {
            scalapendraAnimator.enabled = true;
            if (scalapendraController != null)
            {
                scalapendraAnimator.runtimeAnimatorController = scalapendraController;
            }

            if (!string.IsNullOrWhiteSpace(animationStateName))
            {
                scalapendraAnimator.Play(animationStateName, 0, 0f);
            }
        }
        else if (legacyAnimation != null)
        {
            legacyAnimation.Play();
        }

        Vector3 startPosition = target.position;
        Vector3 targetPosition = new Vector3(targetWorldX, startPosition.y, startPosition.z);
        float safeMoveDuration = Mathf.Max(0.01f, moveDuration);

        for (float elapsed = 0f; elapsed < safeMoveDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeMoveDuration));
            target.position = Vector3.Lerp(startPosition, targetPosition, t);
            yield return null;
        }

        target.position = targetPosition;

        if (destroyDelay > 0f)
        {
            yield return new WaitForSeconds(destroyDelay);
        }

        Destroy(target.gameObject);
    }

    private async Task EnsureScalapendraLoadedAsync()
    {
        if (scalapendraReady || scalapendraLoading)
        {
            return;
        }

        scalapendraLoading = true;

        if (lookCamera == null)
        {
            lookCamera = Camera.main;
        }

        if (scalapendraRoot == null)
        {
            GameObject namedTarget = GameObject.Find("scalapendra");
            if (namedTarget != null)
            {
                scalapendraRoot = namedTarget.transform;
            }
        }

        if (scalapendraRoot == null)
        {
            await LoadScalapendraFromGlbAsync();
        }

        if (scalapendraAnimator == null && scalapendraRoot != null)
        {
            scalapendraAnimator = scalapendraRoot.GetComponentInChildren<Animator>();
        }

        if (scalapendraRoot != null)
        {
            ApplySpawnTransform(scalapendraRoot);
        }

        if (scalapendraAnimator != null && scalapendraController != null)
        {
            scalapendraAnimator.runtimeAnimatorController = scalapendraController;
        }

        scalapendraReady = scalapendraRoot != null;
        scalapendraLoading = false;
    }

    private async Task LoadScalapendraFromGlbAsync()
    {
        string absolutePath = ResolveGlbPath();
        if (string.IsNullOrEmpty(absolutePath))
        {
            Debug.LogError($"Scalapendra GLB not found in StreamingAssets ('{Path.GetFileName(scalapendraGlbProjectPath)}') or project path '{scalapendraGlbProjectPath}'.", this);
            return;
        }

        GameObject container = new GameObject("scalapendra");
        Transform containerTransform = container.transform;
        ApplySpawnTransform(containerTransform);

        GltfImport gltf = new GltfImport();
        bool loaded = await gltf.Load(absolutePath);
        if (!loaded)
        {
            Debug.LogError($"Failed to load scalapendra GLB from {absolutePath}", this);
            Destroy(container);
            return;
        }

        GameObjectInstantiator instantiator = new GameObjectInstantiator(gltf, containerTransform);
        bool instantiated = await gltf.InstantiateMainSceneAsync(instantiator);
        if (!instantiated)
        {
            Debug.LogError("Failed to instantiate scalapendra GLB.", this);
            Destroy(container);
            return;
        }

        scalapendraRoot = containerTransform;
        legacyAnimation = instantiator.SceneInstance.LegacyAnimation;
        if (legacyAnimation != null)
        {
            legacyAnimation.Stop();
        }
    }

    private string ResolveGlbPath()
    {
        // In a build the project Assets folder does not ship, so the model is loaded
        // from StreamingAssets (copied into the player). This also works in the editor.
        string fileName = Path.GetFileName(scalapendraGlbProjectPath);
        string streamingPath = Path.Combine(Application.streamingAssetsPath, fileName);
        if (File.Exists(streamingPath))
        {
            return streamingPath;
        }

#if UNITY_EDITOR
        // Editor fallback: load directly from the source asset in the project.
        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", scalapendraGlbProjectPath));
        if (File.Exists(projectPath))
        {
            return projectPath;
        }
#endif

        return null;
    }

    private void SetAnimationEnabled(bool enabled)
    {
        if (scalapendraAnimator != null)
        {
            scalapendraAnimator.enabled = enabled;
        }

        if (!enabled && legacyAnimation != null)
        {
            legacyAnimation.Stop();
        }
    }

    private void ApplySpawnTransform(Transform target)
    {
        target.SetPositionAndRotation(spawnWorldOffset, Quaternion.Euler(spawnEulerAngles));
        target.localScale = spawnScale;
    }

    private bool IsTriggerTargetVisible()
    {
        if (triggerWhenAnyPartInCamera)
        {
            Bounds bounds = GetTriggerBounds();
            if (bounds.size != Vector3.zero)
            {
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(lookCamera);
                return GeometryUtility.TestPlanesAABB(planes, bounds);
            }
        }

        Ray ray = new Ray(lookCamera.transform.position, lookCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, lookDistance, ~0, QueryTriggerInteraction.Collide))
        {
            return false;
        }

        return hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform);
    }

    private Bounds GetTriggerBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds;
        }

        return new Bounds(transform.position, Vector3.zero);
    }
}
