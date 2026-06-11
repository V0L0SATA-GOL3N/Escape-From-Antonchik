using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GunWeaponController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PickupInteractable pickup;
    [SerializeField] private Camera shootCamera;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Transform slide;
    [SerializeField] private Transform magazineInGun;
    [SerializeField] private GameObject magazinePrefab;

    [Header("Shooting")]
    [SerializeField] private int magazineSize = 4;
    [SerializeField] private int maxCarriedMagazines = 4;
    [SerializeField] private int startingCarriedMagazines;
    [SerializeField] private float fireCooldown = 0.18f;
    [SerializeField] private float range = 40f;
    [SerializeField] private float hitForce = 8f;
    [SerializeField] private float recoilDistance = 0.035f;
    [SerializeField] private float recoilRotation = 5f;
    [SerializeField] private float slideKickDistance = 0.045f;

    [Header("Reload")]
    [SerializeField] private KeyCode reloadKey = KeyCode.R;
    [SerializeField] private float reloadDuration = 1.15f;
    [SerializeField] private Vector3 reloadMagDropOffset = new Vector3(0.02f, -0.22f, -0.04f);
    [SerializeField] private Vector3 reloadMagInsertOffset = new Vector3(0.08f, -0.34f, -0.1f);
    [SerializeField] private Vector3 reloadMagEulerOffset = new Vector3(0f, 0f, 10f);
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shot;

    [SerializeField] private AudioClip reloading;
    [SerializeField] private AudioClip emptyGun;

    [Header("HUD")]
    [SerializeField] private TMP_Text ammoLabel;
    [SerializeField] private TMP_Text magazineLabel;
    [SerializeField] private float magazineLabelFadeDuration = 1f;
    [SerializeField] private string ammoFormat = "Bullets: {0}/{1}";
    [SerializeField] private string magazineFormat = "{0}/{1}";

    private int ammoInMagazine;
    private int carriedMagazines;
    private float nextFireTime;
    private bool isReloading;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 slideBaseLocalPosition;
    private Coroutine recoilRoutine;
    private Light muzzleFlashLight;
    private bool heldPoseCached;
    private FirstPersonHandsAnimatorDriver handsAnimatorDriver;
    private bool requestedHandGunPose;
    private Coroutine magazineLabelRoutine;

    private void Awake()
    {
        pickup = pickup != null ? pickup : GetComponent<PickupInteractable>();
        shootCamera = shootCamera != null ? shootCamera : Camera.main;
        muzzle = muzzle != null ? muzzle : FindChildByName(transform, "muzzle");
        slide = slide != null ? slide : FindChildByName(transform, "slide");
        magazineInGun = magazineInGun != null ? magazineInGun : FindChildByName(transform, "mag");

        magazineSize = Mathf.Max(1, magazineSize);
        maxCarriedMagazines = Mathf.Max(0, maxCarriedMagazines);
        ammoInMagazine = magazineSize;
        carriedMagazines = Mathf.Clamp(startingCarriedMagazines, 0, maxCarriedMagazines);
        if (slide != null)
        {
            slideBaseLocalPosition = slide.localPosition;
        }

        if (muzzle != null)
        {
            muzzleFlashLight = muzzle.gameObject.AddComponent<Light>();
            muzzleFlashLight.type = LightType.Point;
            muzzleFlashLight.color = new Color(1f, 0.62f, 0.22f, 1f);
            muzzleFlashLight.range = 2f;
            muzzleFlashLight.intensity = 0f;
        }

        KeepSkinnedMeshesVisible();
        EnsureHud();
        UpdateHud(false);
    }

    private void Update()
    {
        if (pickup == null || !pickup.IsHeld)
        {
            heldPoseCached = false;
            SetHandGunPose(false);
            UpdateHud(false);
            return;
        }

        if (!pickup.IsEquipped)
        {
            heldPoseCached = false;
            UpdateHud(false);
            return;
        }

        SetHandGunPose(true);
        UpdateHud(true);

        if (!heldPoseCached)
        {
            CacheHeldPose();
        }

        if (shootCamera == null)
        {
            shootCamera = Camera.main;
        }

        if (Input.GetButtonDown("Fire1"))
        {
            TryShoot();
        }

        if (Input.GetKeyDown(reloadKey))
        {
            TryReload();
        }
    }

    private void OnDisable()
    {
        SetHandGunPose(false);
    }

    private void OnDestroy()
    {
        SetHandGunPose(false);
    }

    private void TryShoot()
    {
        if (isReloading || Time.time < nextFireTime)
        {
            return;
        }

        if (ammoInMagazine <= 0)
        {
            PlayEmptyGunFeedback();
            return;
        }

        if (audioSource != null && shot != null)
        {
            audioSource.PlayOneShot(shot);
        }

        nextFireTime = Time.time + fireCooldown;
        ammoInMagazine--;
        UpdateHud(true);

        if (shootCamera != null && Physics.Raycast(shootCamera.transform.position, shootCamera.transform.forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform))
            {
                Rigidbody hitBody = hit.rigidbody;
                if (hitBody != null)
                {
                    hitBody.AddForceAtPosition(shootCamera.transform.forward * hitForce, hit.point, ForceMode.Impulse);
                }
            }
        }

        if (recoilRoutine != null)
        {
            StopCoroutine(recoilRoutine);
        }

        recoilRoutine = StartCoroutine(ShootFeedbackRoutine(true));
    }

    private void CacheHeldPose()
    {
        if (pickup != null && pickup.TryGetHeldPose(out Vector3 heldPosition, out Quaternion heldRotation))
        {
            baseLocalPosition = heldPosition;
            baseLocalRotation = heldRotation;
            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
        }
        else
        {
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
        }

        if (slide != null)
        {
            slideBaseLocalPosition = slide.localPosition;
        }

        heldPoseCached = true;
    }

    private void TryReload()
    {
        if (isReloading || ammoInMagazine >= magazineSize)
        {
            return;
        }

        if (carriedMagazines <= 0)
        {
            ShowMagazineLabel();
            return;
        }

        StartCoroutine(ReloadRoutine());
    }

    public bool TryAddMagazine(int amount = 1)
    {
        if (amount <= 0 || carriedMagazines >= maxCarriedMagazines)
        {
            ShowMagazineLabel();
            return false;
        }

        int previousMagazines = carriedMagazines;
        carriedMagazines = Mathf.Clamp(carriedMagazines + amount, 0, maxCarriedMagazines);
        UpdateHud(pickup != null && pickup.IsHeld);
        ShowMagazineLabel();
        return carriedMagazines > previousMagazines;
    }

    public bool CanAddMagazine()
    {
        return carriedMagazines < maxCarriedMagazines;
    }

    private void PlayEmptyGunFeedback()
    {
        nextFireTime = Time.time + fireCooldown;

        if (audioSource != null && emptyGun != null)
        {
            audioSource.PlayOneShot(emptyGun);
        }

        if (recoilRoutine != null)
        {
            StopCoroutine(recoilRoutine);
        }

        recoilRoutine = StartCoroutine(ShootFeedbackRoutine(false));
    }

    private IEnumerator ShootFeedbackRoutine(bool showMuzzleFlash)
    {
        const float kickTime = 0.05f;
        const float settleTime = 0.11f;
        Vector3 kickPosition = baseLocalPosition - Vector3.forward * recoilDistance;
        Quaternion kickRotation = baseLocalRotation * Quaternion.Euler(-recoilRotation, 0f, 0f);
        Vector3 slideKickPosition = slideBaseLocalPosition - Vector3.forward * slideKickDistance;

        if (showMuzzleFlash && muzzleFlashLight != null)
        {
            muzzleFlashLight.intensity = 5f;
        }

        for (float elapsed = 0f; elapsed < kickTime; elapsed += Time.deltaTime)
        {
            float t = Mathf.Clamp01(elapsed / kickTime);
            transform.localPosition = Vector3.Lerp(baseLocalPosition, kickPosition, t);
            transform.localRotation = Quaternion.Slerp(baseLocalRotation, kickRotation, t);
            if (slide != null)
            {
                slide.localPosition = Vector3.Lerp(slideBaseLocalPosition, slideKickPosition, t);
            }
            yield return null;
        }

        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.intensity = 0f;
        }

        for (float elapsed = 0f; elapsed < settleTime; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / settleTime));
            transform.localPosition = Vector3.Lerp(kickPosition, baseLocalPosition, t);
            transform.localRotation = Quaternion.Slerp(kickRotation, baseLocalRotation, t);
            if (slide != null)
            {
                slide.localPosition = Vector3.Lerp(slideKickPosition, slideBaseLocalPosition, t);
            }
            yield return null;
        }

        transform.localPosition = baseLocalPosition;
        transform.localRotation = baseLocalRotation;
        if (slide != null)
        {
            slide.localPosition = slideBaseLocalPosition;
        }

        recoilRoutine = null;
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        carriedMagazines--;
        UpdateHud(true);
        ShowMagazineLabel();

        if (audioSource != null && reloading != null)
        {
            audioSource.PlayOneShot(reloading);
        }

        SetRenderersEnabled(magazineInGun, false);
        GameObject reloadMag = CreateReloadMagazine();
        Transform magTransform = reloadMag != null ? reloadMag.transform : null;
        Vector3 magStart = magazineInGun != null ? magazineInGun.localPosition : Vector3.zero;
        Quaternion magStartRotation = magazineInGun != null ? magazineInGun.localRotation : Quaternion.identity;
        Vector3 dropPosition = magStart + reloadMagDropOffset;
        Vector3 insertPosition = magStart + reloadMagInsertOffset;
        Quaternion tiltedRotation = magStartRotation * Quaternion.Euler(reloadMagEulerOffset);

        float half = reloadDuration * 0.5f;
        for (float elapsed = 0f; elapsed < half; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / half));
            if (magTransform != null)
            {
                magTransform.localPosition = Vector3.Lerp(magStart, dropPosition, t);
                magTransform.localRotation = Quaternion.Slerp(magStartRotation, tiltedRotation, t);
            }
            yield return null;
        }

        if (magTransform != null)
        {
            magTransform.localPosition = insertPosition;
            magTransform.localRotation = tiltedRotation;
        }

        for (float elapsed = 0f; elapsed < half; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / half));
            if (magTransform != null)
            {
                magTransform.localPosition = Vector3.Lerp(insertPosition, magStart, t);
                magTransform.localRotation = Quaternion.Slerp(tiltedRotation, magStartRotation, t);
            }
            yield return null;
        }

        if (reloadMag != null)
        {
            Destroy(reloadMag);
        }

        SetRenderersEnabled(magazineInGun, true);
        ammoInMagazine = magazineSize;
        isReloading = false;
        UpdateHud(true);
    }

    private void EnsureHud()
    {
        if (ammoLabel != null && magazineLabel != null)
        {
            SetTextAlpha(magazineLabel, 0f);
            return;
        }

        GameObject canvasObject = new GameObject("Gameplay Gun HUD Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<GraphicRaycaster>();

        if (ammoLabel == null)
        {
            ammoLabel = CreateHudLabel("Ammo Label", canvas.transform, new Vector2(24f, 58f));
        }

        if (magazineLabel == null)
        {
            magazineLabel = CreateHudLabel("Magazine Label", canvas.transform, new Vector2(24f, 24f));
            SetTextAlpha(magazineLabel, 0f);
        }
    }

    private TMP_Text CreateHudLabel(string objectName, Transform parent, Vector2 anchoredPosition)
    {
        GameObject labelObject = new GameObject(objectName);
        labelObject.transform.SetParent(parent, false);

        RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(0f, 0f);
        rectTransform.pivot = new Vector2(0f, 0f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(360f, 34f);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;
        label.color = Color.white;
        return label;
    }

    private void UpdateHud(bool showHud)
    {
        if (ammoLabel != null)
        {
            ammoLabel.text = string.Format(ammoFormat, ammoInMagazine, magazineSize);
            ammoLabel.gameObject.SetActive(showHud);
        }

        if (magazineLabel != null)
        {
            magazineLabel.text = string.Format(magazineFormat, carriedMagazines, ammoInMagazine);
            if (showHud)
            {
                if (magazineLabelRoutine != null)
                {
                    StopCoroutine(magazineLabelRoutine);
                    magazineLabelRoutine = null;
                }

                magazineLabel.gameObject.SetActive(true);
                SetTextAlpha(magazineLabel, 1f);
            }
            else if (magazineLabelRoutine == null)
            {
                SetTextAlpha(magazineLabel, 0f);
                magazineLabel.gameObject.SetActive(false);
            }
        }
    }

    private void ShowMagazineLabel()
    {
        if (magazineLabel == null)
        {
            return;
        }

        if (pickup != null && pickup.IsHeld)
        {
            UpdateHud(true);
            return;
        }

        if (magazineLabelRoutine != null)
        {
            StopCoroutine(magazineLabelRoutine);
        }

        magazineLabelRoutine = StartCoroutine(MagazineLabelFadeRoutine());
    }

    private IEnumerator MagazineLabelFadeRoutine()
    {
        magazineLabel.gameObject.SetActive(true);
        float halfDuration = Mathf.Max(0.01f, magazineLabelFadeDuration * 0.5f);

        for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
        {
            SetTextAlpha(magazineLabel, Mathf.Clamp01(elapsed / halfDuration));
            yield return null;
        }

        SetTextAlpha(magazineLabel, 1f);

        for (float elapsed = 0f; elapsed < halfDuration; elapsed += Time.deltaTime)
        {
            SetTextAlpha(magazineLabel, 1f - Mathf.Clamp01(elapsed / halfDuration));
            yield return null;
        }

        SetTextAlpha(magazineLabel, 0f);
        magazineLabel.gameObject.SetActive(false);
        magazineLabelRoutine = null;
    }

    private static void SetTextAlpha(TMP_Text label, float alpha)
    {
        if (label == null)
        {
            return;
        }

        Color color = label.color;
        color.a = Mathf.Clamp01(alpha);
        label.color = color;
    }

    private GameObject CreateReloadMagazine()
    {
        Transform parent = magazineInGun != null ? magazineInGun.parent : transform;
        if (magazinePrefab != null)
        {
            GameObject instance = Instantiate(magazinePrefab, parent);
            PrepareReloadMagazineVisual(instance);
            if (magazineInGun != null)
            {
                instance.transform.localPosition = magazineInGun.localPosition;
                instance.transform.localRotation = magazineInGun.localRotation;
                instance.transform.localScale = magazineInGun.localScale;
            }

            return instance;
        }

        if (magazineInGun == null)
        {
            return null;
        }

        GameObject clone = Instantiate(magazineInGun.gameObject, parent);
        clone.name = "Reload Magazine";
        PrepareReloadMagazineVisual(clone);
        SetRenderersEnabled(clone.transform, true);
        return clone;
    }

    private static void PrepareReloadMagazineVisual(GameObject magazine)
    {
        if (magazine == null)
        {
            return;
        }

        GunMagazinePickup[] pickups = magazine.GetComponentsInChildren<GunMagazinePickup>(true);
        for (int i = 0; i < pickups.Length; i++)
        {
            pickups[i].enabled = false;
        }

        Collider[] colliders = magazine.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private static void SetRenderersEnabled(Transform root, bool enabled)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = enabled;
        }
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
            {
                return children[i];
            }
        }

        return null;
    }

    private void KeepSkinnedMeshesVisible()
    {
        SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            skinnedRenderers[i].updateWhenOffscreen = true;
        }
    }

    private void SetHandGunPose(bool active)
    {
        if (requestedHandGunPose == active)
        {
            return;
        }

        if (handsAnimatorDriver == null)
        {
            handsAnimatorDriver = FindFirstObjectByType<FirstPersonHandsAnimatorDriver>();
        }

        if (handsAnimatorDriver != null)
        {
            handsAnimatorDriver.SetHeldGunPose(active);
        }

        requestedHandGunPose = active;
    }
}
