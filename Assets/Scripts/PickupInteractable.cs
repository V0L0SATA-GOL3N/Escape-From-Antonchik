using UnityEngine;

public class PickupInteractable : MonoBehaviour
{
    [SerializeField] private float interactionDistance = 5f;
    [SerializeField] private float dropForwardForce = 1.5f;
    [SerializeField] private bool moveFingersWhileWalking = true;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pickupSound;
    [SerializeField] private AudioClip dropOnFloorSound;
    [SerializeField] private bool useCustomHeldPose;
    [SerializeField] private Vector3 heldLocalPositionOverride;
    [SerializeField] private Vector3 heldLocalEulerOverride;
    [SerializeField] private Vector3 heldLocalScaleOverride = Vector3.one;

    private Rigidbody rb;
    private Collider[] colliders;
    private Transform originalParent;
    private Vector3 originalLocalScale;
    private bool originalKinematic;
    private bool originalUseGravity;
    private Vector3 heldWorldScale;
    private bool waitingForDropFloorSound;
    private bool pickupSoundPlayedThisPickup;
    private Renderer[] renderers;

    public bool IsHeld { get; private set; }
    public bool IsEquipped { get; private set; }
    public float InteractionDistance => interactionDistance;

    // Cross-scene restored pickups get a wider reach so they're easier to grab
    // again in the yard. See InventoryCarryOver.RestoreInto.
    public void SetInteractionDistance(float distance)
    {
        interactionDistance = distance;
    }
    public bool MoveFingersWhileWalking => moveFingersWhileWalking;
    public string DisplayName => string.IsNullOrWhiteSpace(gameObject.name) ? "Object" : gameObject.name;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = audioSource != null ? audioSource : GetComponent<AudioSource>();
        // PlayOneShot plays silence in a build if the clip's PCM data isn't
        // resident yet (unlike clip+Play, it won't block to load). Preload so the
        // first pickup / drop is audible in players, not just the editor.
        PreloadAudio(pickupSound);
        PreloadAudio(dropOnFloorSound);
        RefreshColliders();
        RefreshRenderers();
        originalParent = transform.parent;
        originalLocalScale = transform.localScale;

        if (rb != null)
        {
            originalKinematic = rb.isKinematic;
            originalUseGravity = rb.useGravity;
        }
    }

    public void BeginPickup()
    {
        IsHeld = true;
        IsEquipped = true;
        heldWorldScale = transform.lossyScale;
        RefreshColliders();
        RefreshRenderers();

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        SetCollidersEnabled(false);
        SetRenderersEnabled(true);
        pickupSoundPlayedThisPickup = false;
        waitingForDropFloorSound = false;
    }

    public Vector3 GetReachPoint(Vector3 handPosition)
    {
        Collider closestCollider = null;
        float closestDistance = float.MaxValue;

        if (colliders != null)
        {
            foreach (Collider itemCollider in colliders)
            {
                if (itemCollider == null)
                {
                    continue;
                }

                Vector3 candidate = itemCollider.ClosestPoint(handPosition);
                float distance = (candidate - handPosition).sqrMagnitude;

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestCollider = itemCollider;
                }
            }
        }

        if (closestCollider != null)
        {
            return closestCollider.ClosestPoint(handPosition);
        }

        Renderer itemRenderer = GetComponentInChildren<Renderer>();
        return itemRenderer != null ? itemRenderer.bounds.ClosestPoint(handPosition) : transform.position;
    }

    public Vector3 GetCenterPoint()
    {
        Bounds? combinedBounds = null;

        if (colliders != null)
        {
            foreach (Collider itemCollider in colliders)
            {
                if (itemCollider == null)
                {
                    continue;
                }

                combinedBounds = combinedBounds.HasValue
                    ? Encapsulate(combinedBounds.Value, itemCollider.bounds)
                    : itemCollider.bounds;
            }
        }

        if (combinedBounds.HasValue)
        {
            return combinedBounds.Value.center;
        }

        Renderer itemRenderer = GetComponentInChildren<Renderer>();
        return itemRenderer != null ? itemRenderer.bounds.center : transform.position;
    }

    private static Bounds Encapsulate(Bounds current, Bounds other)
    {
        current.Encapsulate(other);
        return current;
    }

    public void AttachTo(Transform holdPoint)
    {
        transform.SetParent(holdPoint, true);
        IsEquipped = true;
        SetRenderersEnabled(true);
        SetCollidersEnabled(false);
        PlayPickupSoundOnce();

        if (useCustomHeldPose)
        {
            transform.localPosition = heldLocalPositionOverride;
            transform.localRotation = Quaternion.Euler(heldLocalEulerOverride);
            transform.localScale = heldLocalScaleOverride;
            return;
        }

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetWorldScale(heldWorldScale);
    }

    private void PlayPickupSoundOnce()
    {
        if (pickupSoundPlayedThisPickup)
        {
            return;
        }

        pickupSoundPlayedThisPickup = true;
        PlaySound(pickupSound);
    }

    // Lets runtime-spawned pickups (e.g. the gate key) define how they sit in the
    // hand without a prefab-authored component.
    public void SetCustomHeldPose(Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
    {
        useCustomHeldPose = true;
        heldLocalPositionOverride = localPosition;
        heldLocalEulerOverride = localEuler;
        heldLocalScaleOverride = localScale;
    }

    public bool TryGetHeldPose(out Vector3 localPosition, out Quaternion localRotation)
    {
        localPosition = heldLocalPositionOverride;
        localRotation = Quaternion.Euler(heldLocalEulerOverride);
        return useCustomHeldPose;
    }

    public void EquipFromInventory(Transform holdPoint)
    {
        AttachTo(holdPoint);
    }

    public void StowInInventory(Transform stowPoint)
    {
        transform.SetParent(stowPoint, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = originalLocalScale;
        IsEquipped = false;
        SetCollidersEnabled(false);
        SetRenderersEnabled(false);
    }

    public void Drop(Vector3 forward)
    {
        transform.SetParent(originalParent, true);
        transform.localScale = originalLocalScale;
        RefreshColliders();
        EnsureDropColliders();
        SetCollidersEnabled(true);

        if (rb != null)
        {
            rb.isKinematic = originalKinematic;
            rb.useGravity = originalUseGravity;
            rb.velocity = forward.normalized * dropForwardForce;
            rb.angularVelocity = Vector3.zero;
        }

        IsHeld = false;
        IsEquipped = false;
        waitingForDropFloorSound = dropOnFloorSound != null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!waitingForDropFloorSound || IsHeld || !IsFloorCollision(collision))
        {
            return;
        }

        waitingForDropFloorSound = false;
        PlaySound(dropOnFloorSound);
    }

    private bool IsFloorCollision(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y >= 0.35f)
            {
                return true;
            }
        }

        return false;
    }

    private void SetWorldScale(Vector3 targetWorldScale)
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            transform.localScale = targetWorldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        transform.localScale = new Vector3(
            SafeDivide(targetWorldScale.x, parentScale.x),
            SafeDivide(targetWorldScale.y, parentScale.y),
            SafeDivide(targetWorldScale.z, parentScale.z)
        );
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        RefreshColliders();

        if (colliders == null)
        {
            return;
        }

        foreach (Collider itemCollider in colliders)
        {
            itemCollider.enabled = enabled;
        }
    }

    private void RefreshColliders()
    {
        colliders = GetComponentsInChildren<Collider>(true);
    }

    private void RefreshRenderers()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void SetRenderersEnabled(bool enabled)
    {
        RefreshRenderers();

        if (renderers == null)
        {
            return;
        }

        foreach (Renderer itemRenderer in renderers)
        {
            itemRenderer.enabled = enabled;
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.dopplerLevel = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        audioSource.PlayOneShot(clip);
    }

    private static void PreloadAudio(AudioClip clip)
    {
        if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
        }
    }

    private void EnsureDropColliders()
    {
        if (colliders != null && colliders.Length > 0)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider meshCollider = colliders[i] as MeshCollider;
                if (meshCollider != null && rb != null)
                {
                    meshCollider.convex = true;
                }
            }

            return;
        }

        MeshFilter meshFilter = GetComponentInChildren<MeshFilter>(true);
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = rb != null;
            RefreshColliders();
            return;
        }

        Renderer itemRenderer = GetComponentInChildren<Renderer>(true);
        if (itemRenderer != null)
        {
            BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
            Bounds localBounds = new Bounds(transform.InverseTransformPoint(itemRenderer.bounds.center), Vector3.zero);
            localBounds.Encapsulate(transform.InverseTransformPoint(itemRenderer.bounds.min));
            localBounds.Encapsulate(transform.InverseTransformPoint(itemRenderer.bounds.max));
            boxCollider.center = localBounds.center;
            boxCollider.size = localBounds.size;
            RefreshColliders();
        }
    }
}
