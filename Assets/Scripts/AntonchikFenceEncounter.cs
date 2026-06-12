using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class AntonchikFenceEncounter : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private string fenceNamePrefix = "fence";
    [SerializeField] private float triggerDistance = 4.5f;
    [SerializeField] private float minRandomDelay = 0.8f;
    [SerializeField] private float maxRandomDelay = 5f;

    [Header("Antonchik")]
    [SerializeField] private GameObject antonPrefab;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private float spawnDistance = 2.3f;
    [SerializeField] private float turnDuration = 0.55f;

    [Header("Dialog")]
    [SerializeField] private string speakerName = "АНТОНЧИК";
    [SerializeField] private string dialogLine = "do you have money... or beer";
    [SerializeField] private float lettersPerSecond = 14f;
    [SerializeField] private string replyText = "Мужчина денег нет";
    [SerializeField] private float replyTimeLimit = 8f;

    [Header("Gun")]
    [SerializeField] private GameObject pistolPrefab;
    [SerializeField] private float gunRaiseDuration = 0.8f;

    [Header("Audio")]
    [SerializeField] private AudioClip jumpscareSound;
    [SerializeField] private AudioClip typingSound;
    [SerializeField] private AudioClip lineFinishedSound;
    [SerializeField] private AudioClip shotSound;

    private FirstPersonController playerController;
    private Transform playerTransform;
    private Camera playerCamera;
    private Rigidbody playerRigidbody;
    private AudioSource uiAudioSource;
    private AudioSource worldAudioSource;
    private readonly List<Collider> fenceColliders = new List<Collider>();
    private readonly List<Transform> fenceTransforms = new List<Transform>();

    private GameObject antonInstance;
    private GameObject gunInstance;
    private Canvas encounterCanvas;
    private GameObject dialogRoot;
    private TextMeshProUGUI speakerLabel;
    private TextMeshProUGUI dialogLabel;
    private GameObject replyRoot;
    private Image timerFill;
    private GameObject deathRoot;
    private CanvasGroup deathGroup;
    private TextMeshProUGUI deathTitle;

    private bool encounterStarted;
    private bool armed;
    private float armedTimer;
    private bool replied;

    public static bool SequenceActive { get; private set; }

    private void Start()
    {
        SequenceActive = false;
        ResolveSceneReferences();
        EnsureAssetsLoaded();
    }

    private void OnDestroy()
    {
        SequenceActive = false;
    }

    private void Update()
    {
        if (encounterStarted || playerTransform == null)
        {
            return;
        }

        if (!armed)
        {
            if (IsPlayerNearFence())
            {
                armed = true;
                armedTimer = Random.Range(minRandomDelay, maxRandomDelay);
            }

            return;
        }

        armedTimer -= Time.deltaTime;
        if (armedTimer <= 0f)
        {
            encounterStarted = true;
            StartCoroutine(EncounterRoutine());
        }
    }

    private void ResolveSceneReferences()
    {
        playerController = FindObjectOfType<FirstPersonController>();
        if (playerController != null)
        {
            playerTransform = playerController.transform;
            playerRigidbody = playerController.GetComponent<Rigidbody>();
        }

        playerCamera = Camera.main;
        if (playerCamera == null && playerController != null)
        {
            playerCamera = playerController.GetComponentInChildren<Camera>(true);
        }

        fenceColliders.Clear();
        fenceTransforms.Clear();
        foreach (Transform candidate in FindObjectsOfType<Transform>())
        {
            if (!candidate.name.StartsWith(fenceNamePrefix) && candidate.name != "gate")
            {
                continue;
            }

            if (candidate.parent == null || candidate.parent.name != "ground")
            {
                continue;
            }

            fenceTransforms.Add(candidate);
            fenceColliders.AddRange(candidate.GetComponentsInChildren<Collider>());
        }

        uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.spatialBlend = 0f;

        worldAudioSource = gameObject.AddComponent<AudioSource>();
        worldAudioSource.playOnAwake = false;
        worldAudioSource.spatialBlend = 0f;
    }

    private void EnsureAssetsLoaded()
    {
#if UNITY_EDITOR
        if (antonPrefab == null)
        {
            antonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/anton.prefab");
        }

        if (pistolPrefab == null)
        {
            pistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Pistol_00.prefab");
        }

        if (idleClip == null)
        {
            idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3d/Standing Idle.fbx");
        }

        if (typingSound == null)
        {
            typingSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/cutscene_typing.wav");
        }

        if (lineFinishedSound == null)
        {
            lineFinishedSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/cutscene_finish.wav");
        }

        if (shotSound == null)
        {
            shotSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/shot.mp3");
        }
#endif

        if (jumpscareSound == null)
        {
            jumpscareSound = ProceduralJumpscareSting.Create();
        }
    }

    private bool IsPlayerNearFence()
    {
        Vector3 playerPosition = playerTransform.position;

        for (int i = 0; i < fenceColliders.Count; i++)
        {
            Collider fence = fenceColliders[i];
            if (fence == null)
            {
                continue;
            }

            Vector3 closest = fence.ClosestPointOnBounds(playerPosition);
            closest.y = playerPosition.y;
            if ((closest - playerPosition).sqrMagnitude <= triggerDistance * triggerDistance)
            {
                return true;
            }
        }

        for (int i = 0; i < fenceTransforms.Count; i++)
        {
            Vector3 fencePosition = fenceTransforms[i].position;
            fencePosition.y = playerPosition.y;
            if ((fencePosition - playerPosition).sqrMagnitude <= triggerDistance * triggerDistance)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator EncounterRoutine()
    {
        SequenceActive = true;
        LockPlayer();
        SpawnAntonBehindPlayer();
        PlayJumpscareSound();

        yield return TurnPlayerTowardsAnton();
        yield return new WaitForSeconds(0.45f);

        BuildEncounterCanvas();
        BuildDialogPanel();
        yield return TypeDialogLine();
        yield return ReplyPhase();
        yield return GunSequence();
        yield return ShowDeathScreen();
    }

    private void LockPlayer()
    {
        if (playerController != null)
        {
            playerController.playerCanMove = false;
            playerController.cameraCanMove = false;
            playerController.enableJump = false;
            playerController.SetCrouchEnabled(false);
            playerController.enabled = false;
        }

        if (playerRigidbody != null)
        {
            playerRigidbody.velocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.isKinematic = true;
        }
    }

    private void SpawnAntonBehindPlayer()
    {
        if (antonPrefab == null || playerTransform == null)
        {
            return;
        }

        Vector3 backDirection = -playerTransform.forward;
        backDirection.y = 0f;
        backDirection.Normalize();

        Vector3 spawnPosition = playerTransform.position + backDirection * spawnDistance;
        spawnPosition.y = SampleGroundHeight(spawnPosition);

        antonInstance = Instantiate(antonPrefab, spawnPosition, Quaternion.LookRotation(-backDirection, Vector3.up));

        Animator animator = antonInstance.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            if (idleClip != null && animator.runtimeAnimatorController != null)
            {
                AnimatorOverrideController overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
                List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(overrides);
                for (int i = 0; i < overrides.Count; i++)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, idleClip);
                }

                overrideController.ApplyOverrides(overrides);
                animator.runtimeAnimatorController = overrideController;
            }
        }

        CreateAntonRimLight(spawnPosition, backDirection);
        BuiltinPipelineCompatibility.PatchSpawnedObject(antonInstance);
    }

    private void CreateAntonRimLight(Vector3 antonPosition, Vector3 awayFromPlayer)
    {
        GameObject rimObject = new GameObject("Antonchik Rim Light");
        rimObject.transform.SetParent(antonInstance.transform, true);
        rimObject.transform.position = antonPosition + awayFromPlayer * 1.4f + Vector3.up * 2.4f;

        Light rimLight = rimObject.AddComponent<Light>();
        rimLight.type = LightType.Point;
        rimLight.color = new Color(0.55f, 0.65f, 1f, 1f);
        rimLight.intensity = 2.2f;
        rimLight.range = 5f;

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = rimObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(900f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
            hdData.affectsVolumetric = true;
        }
    }

    private float SampleGroundHeight(Vector3 position)
    {
        Vector3 origin = position + Vector3.up * 3f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }

        return playerTransform.position.y - 1.2f;
    }

    private void PlayJumpscareSound()
    {
        if (jumpscareSound != null && worldAudioSource != null)
        {
            worldAudioSource.PlayOneShot(jumpscareSound, 1f);
        }
    }

    private IEnumerator TurnPlayerTowardsAnton()
    {
        if (antonInstance == null || playerTransform == null)
        {
            yield break;
        }

        Vector3 lookTarget = antonInstance.transform.position + Vector3.up * 1.62f;

        Quaternion startBodyRotation = playerTransform.rotation;
        Vector3 flatDirection = antonInstance.transform.position - playerTransform.position;
        flatDirection.y = 0f;
        Quaternion targetBodyRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);

        Transform cameraTransform = playerCamera != null ? playerCamera.transform : null;
        Quaternion startCameraLocalRotation = cameraTransform != null ? cameraTransform.localRotation : Quaternion.identity;

        for (float elapsed = 0f; elapsed < turnDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / turnDuration));
            playerTransform.rotation = Quaternion.Slerp(startBodyRotation, targetBodyRotation, t);

            if (cameraTransform != null)
            {
                Quaternion lookRotation = Quaternion.LookRotation((lookTarget - cameraTransform.position).normalized, Vector3.up);
                Quaternion targetLocalRotation = Quaternion.Inverse(playerTransform.rotation) * lookRotation;
                cameraTransform.localRotation = Quaternion.Slerp(startCameraLocalRotation, targetLocalRotation, t);
            }

            yield return null;
        }

        playerTransform.rotation = targetBodyRotation;

        if (cameraTransform != null)
        {
            Quaternion finalLook = Quaternion.LookRotation((lookTarget - cameraTransform.position).normalized, Vector3.up);
            cameraTransform.localRotation = Quaternion.Inverse(playerTransform.rotation) * finalLook;
        }
    }

    private void BuildEncounterCanvas()
    {
        GameObject canvasObject = new GameObject("Antonchik Encounter Canvas");
        encounterCanvas = canvasObject.AddComponent<Canvas>();
        encounterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        encounterCanvas.sortingOrder = 5000;
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }

    private void BuildDialogPanel()
    {
        dialogRoot = CreatePanel("Dialog Root", encounterCanvas.transform, Color.clear);
        RectTransform dialogRect = dialogRoot.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0f, 0f);
        dialogRect.anchorMax = new Vector2(1f, 0f);
        dialogRect.pivot = new Vector2(0.5f, 0f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(0f, 320f);

        GameObject backdrop = CreatePanel("Dialog Backdrop", dialogRoot.transform, new Color(0f, 0f, 0f, 0.86f));
        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        GameObject topLine = CreatePanel("Dialog Top Line", dialogRoot.transform, new Color(0.45f, 0.02f, 0.02f, 0.9f));
        RectTransform topLineRect = topLine.GetComponent<RectTransform>();
        topLineRect.anchorMin = new Vector2(0f, 1f);
        topLineRect.anchorMax = new Vector2(1f, 1f);
        topLineRect.pivot = new Vector2(0.5f, 1f);
        topLineRect.anchoredPosition = Vector2.zero;
        topLineRect.sizeDelta = new Vector2(0f, 3f);

        speakerLabel = CreateLabel("Speaker Label", dialogRoot.transform, speakerName, 40, new Color(0.62f, 0.04f, 0.04f, 1f), TextAlignmentOptions.Left);
        RectTransform speakerRect = speakerLabel.GetComponent<RectTransform>();
        speakerRect.anchorMin = new Vector2(0f, 1f);
        speakerRect.anchorMax = new Vector2(0f, 1f);
        speakerRect.pivot = new Vector2(0f, 1f);
        speakerRect.anchoredPosition = new Vector2(170f, -26f);
        speakerRect.sizeDelta = new Vector2(900f, 52f);

        dialogLabel = CreateLabel("Dialog Label", dialogRoot.transform, string.Empty, 44, new Color(0.92f, 0.9f, 0.88f, 1f), TextAlignmentOptions.TopLeft);
        RectTransform dialogLabelRect = dialogLabel.GetComponent<RectTransform>();
        dialogLabelRect.anchorMin = new Vector2(0f, 1f);
        dialogLabelRect.anchorMax = new Vector2(1f, 1f);
        dialogLabelRect.pivot = new Vector2(0f, 1f);
        dialogLabelRect.anchoredPosition = new Vector2(170f, -88f);
        dialogLabelRect.offsetMax = new Vector2(-170f, -88f);
        dialogLabelRect.sizeDelta = new Vector2(-340f, 120f);
    }

    private IEnumerator TypeDialogLine()
    {
        float secondsPerLetter = 1f / Mathf.Max(1f, lettersPerSecond);

        dialogLabel.text = string.Empty;
        for (int i = 0; i < dialogLine.Length; i++)
        {
            dialogLabel.text = dialogLine.Substring(0, i + 1);
            if (!char.IsWhiteSpace(dialogLine[i]))
            {
                PlayUiSound(typingSound);
            }

            yield return new WaitForSeconds(secondsPerLetter);
        }

        PlayUiSound(lineFinishedSound);
    }

    private IEnumerator ReplyPhase()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        replyRoot = CreatePanel("Reply Root", dialogRoot.transform, Color.clear);
        RectTransform replyRect = replyRoot.GetComponent<RectTransform>();
        replyRect.anchorMin = new Vector2(0f, 0f);
        replyRect.anchorMax = new Vector2(0f, 0f);
        replyRect.pivot = new Vector2(0f, 0f);
        replyRect.anchoredPosition = new Vector2(170f, 28f);
        replyRect.sizeDelta = new Vector2(640f, 96f);

        GameObject track = CreatePanel("Timer Track", replyRoot.transform, new Color(0.16f, 0.05f, 0.05f, 0.9f));
        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 1f);
        trackRect.anchorMax = new Vector2(1f, 1f);
        trackRect.pivot = new Vector2(0f, 1f);
        trackRect.anchoredPosition = new Vector2(0f, 18f);
        trackRect.sizeDelta = new Vector2(0f, 10f);

        GameObject fill = CreatePanel("Timer Fill", track.transform, new Color(0.78f, 0.05f, 0.04f, 1f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        timerFill = fill.GetComponent<Image>();

        GameObject buttonObject = CreatePanel("Reply Button", replyRoot.transform, new Color(0.07f, 0.07f, 0.08f, 0.95f));
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(0f, 0f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(0f, 70f);

        GameObject frame = CreatePanel("Reply Frame", buttonObject.transform, new Color(0.5f, 0.08f, 0.08f, 0.85f));
        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0f, 0f);
        frameRect.anchorMax = new Vector2(0f, 1f);
        frameRect.pivot = new Vector2(0f, 0.5f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = new Vector2(5f, 0f);

        TextMeshProUGUI buttonLabel = CreateLabel("Reply Label", buttonObject.transform, "> " + replyText, 34, new Color(0.9f, 0.86f, 0.82f, 1f), TextAlignmentOptions.Left);
        RectTransform buttonLabelRect = buttonLabel.GetComponent<RectTransform>();
        buttonLabelRect.anchorMin = Vector2.zero;
        buttonLabelRect.anchorMax = Vector2.one;
        buttonLabelRect.offsetMin = new Vector2(26f, 0f);
        buttonLabelRect.offsetMax = new Vector2(-12f, 0f);
        buttonLabel.alignment = TextAlignmentOptions.MidlineLeft;

        replied = false;
        Button replyButton = buttonObject.AddComponent<Button>();
        replyButton.targetGraphic = buttonObject.GetComponent<Image>();
        ColorBlock colors = replyButton.colors;
        colors.highlightedColor = new Color(1.6f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(2f, 1.4f, 1.4f, 1f);
        replyButton.colors = colors;
        replyButton.onClick.AddListener(() => replied = true);

        float remaining = replyTimeLimit;
        while (!replied && remaining > 0f)
        {
            remaining -= Time.deltaTime;
            if (timerFill != null)
            {
                RectTransform rect = timerFill.rectTransform;
                rect.anchorMax = new Vector2(Mathf.Clamp01(remaining / replyTimeLimit), 1f);
            }

            yield return null;
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        replyRoot.SetActive(false);

        if (dialogLabel != null)
        {
            dialogLabel.text = string.Empty;
        }

        yield return new WaitForSeconds(0.6f);
    }

    private IEnumerator GunSequence()
    {
        if (dialogRoot != null)
        {
            dialogRoot.SetActive(false);
        }

        Transform gunParent = ResolveGunHand();
        Vector3 cameraPosition = playerCamera != null ? playerCamera.transform.position : playerTransform.position + Vector3.up * 1.5f;
        Vector3 antonChest = antonInstance != null
            ? antonInstance.transform.position + Vector3.up * 1.45f
            : playerTransform.position - playerTransform.forward * 2f + Vector3.up * 1.45f;

        if (pistolPrefab != null)
        {
            gunInstance = Instantiate(pistolPrefab);
            StripGunComponents(gunInstance);
            BuiltinPipelineCompatibility.PatchSpawnedObject(gunInstance);

            Vector3 loweredPosition = gunParent.position + Vector3.down * 0.55f;
            Vector3 aimPosition = Vector3.Lerp(cameraPosition, antonChest, 0.45f);

            gunInstance.transform.position = loweredPosition;

            for (float elapsed = 0f; elapsed < gunRaiseDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / gunRaiseDuration));
                gunInstance.transform.position = Vector3.Lerp(loweredPosition, aimPosition, t);
                Vector3 toCamera = (cameraPosition - gunInstance.transform.position).normalized;
                gunInstance.transform.rotation = Quaternion.Slerp(gunInstance.transform.rotation, Quaternion.LookRotation(toCamera, Vector3.up), t);
                yield return null;
            }

            gunInstance.transform.position = aimPosition;
            gunInstance.transform.rotation = Quaternion.LookRotation((cameraPosition - aimPosition).normalized, Vector3.up);
        }

        yield return new WaitForSeconds(0.35f);

        FireMuzzleFlash();
        PlayUiSound(shotSound);
    }

    private Transform ResolveGunHand()
    {
        if (antonInstance != null)
        {
            Animator animator = antonInstance.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand != null)
                {
                    return hand;
                }
            }

            return antonInstance.transform;
        }

        return playerTransform;
    }

    private void StripGunComponents(GameObject gun)
    {
        foreach (MonoBehaviour behaviour in gun.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
        }

        foreach (Rigidbody body in gun.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        foreach (Collider collider in gun.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (AudioSource source in gun.GetComponentsInChildren<AudioSource>(true))
        {
            source.enabled = false;
        }
    }

    private void FireMuzzleFlash()
    {
        Transform muzzle = null;
        if (gunInstance != null)
        {
            foreach (Transform child in gunInstance.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "muzzle")
                {
                    muzzle = child;
                    break;
                }
            }
        }

        Vector3 flashPosition = muzzle != null
            ? muzzle.position
            : (gunInstance != null ? gunInstance.transform.position : playerTransform.position + Vector3.up * 1.5f);

        GameObject flashObject = new GameObject("Muzzle Flash");
        flashObject.transform.position = flashPosition;
        Light flashLight = flashObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.78f, 0.45f, 1f);
        flashLight.intensity = 9f;
        flashLight.range = 9f;

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = flashObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(60000f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
            hdData.affectsVolumetric = true;
        }

        Destroy(flashObject, 0.08f);
    }

    private IEnumerator ShowDeathScreen()
    {
        deathRoot = CreatePanel("You Died Screen", encounterCanvas.transform, Color.black);
        RectTransform deathRect = deathRoot.GetComponent<RectTransform>();
        deathRect.anchorMin = Vector2.zero;
        deathRect.anchorMax = Vector2.one;
        deathRect.offsetMin = Vector2.zero;
        deathRect.offsetMax = Vector2.zero;

        deathGroup = deathRoot.AddComponent<CanvasGroup>();
        deathGroup.alpha = 0f;
        deathGroup.blocksRaycasts = true;

        GameObject redFlash = CreatePanel("Blood Flash", encounterCanvas.transform, new Color(0.45f, 0f, 0f, 0.55f));
        RectTransform redFlashRect = redFlash.GetComponent<RectTransform>();
        redFlashRect.anchorMin = Vector2.zero;
        redFlashRect.anchorMax = Vector2.one;
        redFlashRect.offsetMin = Vector2.zero;
        redFlashRect.offsetMax = Vector2.zero;
        Destroy(redFlash, 0.14f);

        deathTitle = CreateLabel("You Died Title", deathRoot.transform, "YOU DIED", 132, new Color(0.55f, 0.02f, 0.02f, 1f), TextAlignmentOptions.Center);
        RectTransform titleRect = deathTitle.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 70f);
        titleRect.sizeDelta = new Vector2(1400f, 220f);
        deathTitle.characterSpacing = 18f;

        Button startOverButton = CreateDeathButton("Start Over Button", "Start Over", new Vector2(0f, -120f), RestartScene);
        Button mainMenuButton = CreateDeathButton("Main Menu Button", "Main Menu", new Vector2(0f, -210f), GoToMainMenu);
        CanvasGroup startOverGroup = startOverButton.gameObject.AddComponent<CanvasGroup>();
        CanvasGroup mainMenuGroup = mainMenuButton.gameObject.AddComponent<CanvasGroup>();
        startOverGroup.alpha = 0f;
        mainMenuGroup.alpha = 0f;

        const float fadeDuration = 1.7f;
        Vector3 titleStartScale = Vector3.one * 0.88f;
        for (float elapsed = 0f; elapsed < fadeDuration; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDuration));
            deathGroup.alpha = t;
            deathTitle.transform.localScale = Vector3.Lerp(titleStartScale, Vector3.one, t);
            yield return null;
        }

        deathGroup.alpha = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        const float buttonFadeDuration = 0.7f;
        for (float elapsed = 0f; elapsed < buttonFadeDuration; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / buttonFadeDuration);
            startOverGroup.alpha = t;
            mainMenuGroup.alpha = Mathf.Clamp01(t - 0.25f) / 0.75f;
            yield return null;
        }

        startOverGroup.alpha = 1f;
        mainMenuGroup.alpha = 1f;
    }

    private Button CreateDeathButton(string name, string text, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = CreatePanel(name, deathRoot.transform, new Color(0.05f, 0.05f, 0.06f, 0.9f));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(420f, 72f);

        GameObject frame = CreatePanel("Frame", buttonObject.transform, new Color(0.45f, 0.04f, 0.04f, 0.8f));
        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0f, 0f);
        frameRect.anchorMax = new Vector2(1f, 0f);
        frameRect.pivot = new Vector2(0.5f, 0f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = new Vector2(0f, 3f);

        TextMeshProUGUI label = CreateLabel(name + " Label", buttonObject.transform, text, 36, new Color(0.85f, 0.8f, 0.78f, 1f), TextAlignmentOptions.Center);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.8f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(2.2f, 1.5f, 1.5f, 1f);
        button.colors = colors;
        button.onClick.AddListener(action);
        return button;
    }

    private void RestartScene()
    {
        Time.timeScale = 1f;
        SceneLoadingScreen.Load(SceneManager.GetActiveScene().name);
    }

    private void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneLoadingScreen.Load("menu");
    }

    private void PlayUiSound(AudioClip clip)
    {
        if (clip != null && uiAudioSource != null)
        {
            uiAudioSource.PlayOneShot(clip);
        }
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = color.a > 0.01f;
        return panel;
    }

    private TextMeshProUGUI CreateLabel(string name, Transform parent, string text, int fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }
}

public static class ProceduralJumpscareSting
{
    public static AudioClip Create()
    {
        const int sampleRate = 44100;
        const float duration = 1.6f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random random = new System.Random(666);

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float progress = time / duration;

            float attackEnvelope = Mathf.Exp(-time * 5.5f);
            float swellEnvelope = Mathf.Sin(Mathf.Clamp01(progress * 1.25f) * Mathf.PI);

            float noise = ((float)random.NextDouble() * 2f - 1f) * 0.55f * attackEnvelope;

            float cluster =
                Mathf.Sin(2f * Mathf.PI * 657f * time) * 0.2f +
                Mathf.Sin(2f * Mathf.PI * 693f * time) * 0.18f +
                Mathf.Sin(2f * Mathf.PI * 712f * time) * 0.16f;
            cluster *= attackEnvelope;

            float lowDrone =
                Mathf.Sin(2f * Mathf.PI * 55f * time) * 0.42f +
                Mathf.Sin(2f * Mathf.PI * 58.3f * time) * 0.38f +
                Mathf.Sin(2f * Mathf.PI * 110.7f * time) * 0.2f;
            lowDrone *= swellEnvelope;

            float sample = noise + cluster + lowDrone;
            samples[i] = Mathf.Clamp(sample * 0.9f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("AntonchikJumpscareSting", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
