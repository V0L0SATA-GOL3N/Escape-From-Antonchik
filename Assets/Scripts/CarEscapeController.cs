using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// The old pickup truck in the yard — the only way out. Locked until
// Antonchik hands over the keys. Getting in plays the escape cutscene:
// engine start, the gate creaks open, the truck rolls out, victory screen.
public class CarEscapeController : SimpleInteractable
{
    public static bool HasCarKeys;

    private Transform gate;
    private AudioSource carAudio;
    private Light leftHeadlight;
    private Light rightHeadlight;
    private bool escaping;

    private void Awake()
    {
        HasCarKeys = false;
        SetInteractionDistance(4f);
        Prompt = "The car is locked. You need the keys";

        carAudio = gameObject.AddComponent<AudioSource>();
        carAudio.playOnAwake = false;
        carAudio.spatialBlend = 1f;
        carAudio.minDistance = 2f;
        carAudio.maxDistance = 40f;

        GameObject gateObject = GameObject.Find("gate");
        gate = gateObject != null ? gateObject.transform : null;

        EnsureCollider();
    }

    private void Update()
    {
        if (!escaping)
        {
            Prompt = HasCarKeys ? "Press E to get in the car" : "The car is locked. You need the keys";
        }
    }

    private void EnsureCollider()
    {
        // The truck is moved by hand during the escape cutscene, so it needs a
        // kinematic body rather than a free-falling one.
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        Bounds bounds = ComputeBounds();
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
        Vector3 lossy = transform.lossyScale;
        Vector3 localSize = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

        // Y is pinned to authored values; X/Z still come from the truck bounds.
        box.center = new Vector3(localCenter.x, 0.93f, localCenter.z);
        box.size = new Vector3(localSize.x, 1.72f, localSize.z);
    }

    private Bounds ComputeBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(transform.position, Vector3.one * 2f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    public override void Interact()
    {
        if (escaping)
        {
            return;
        }

        if (!HasCarKeys)
        {
            carAudio.PlayOneShot(HorrorAudio.Pop(), 0.25f);
            return;
        }

        escaping = true;
        CanInteract = false;
        StartCoroutine(EscapeRoutine());
        base.Interact();
    }

    private IEnumerator EscapeRoutine()
    {
        FirstPersonController controller = FindObjectOfType<FirstPersonController>();
        Camera playerCamera = Camera.main;
        Transform cameraTransform = playerCamera != null ? playerCamera.transform : null;

        if (controller != null)
        {
            controller.playerCanMove = false;
            controller.cameraCanMove = false;
            controller.enabled = false;
            Rigidbody body = controller.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.isKinematic = true;
            }

            foreach (Collider playerCollider in controller.GetComponentsInChildren<Collider>())
            {
                playerCollider.enabled = false;
            }
        }

        TaskHud.ClearObjective();

        // slide the camera into the cab
        Bounds bounds = ComputeBounds();
        Vector3 seatPosition = bounds.center + Vector3.up * (bounds.extents.y * 0.35f) - transform.right * (bounds.extents.x * 0.3f);
        if (cameraTransform != null)
        {
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            Quaternion seatRotation = Quaternion.LookRotation(transform.forward, Vector3.up);

            carAudio.PlayOneShot(HorrorAudio.KeyJingle(), 0.8f);
            for (float elapsed = 0f; elapsed < 1.4f; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / 1.4f);
                cameraTransform.position = Vector3.Lerp(startPosition, seatPosition, t);
                cameraTransform.rotation = Quaternion.Slerp(startRotation, seatRotation, t);
                yield return null;
            }

            cameraTransform.SetParent(transform, true);
        }

        // door slam + engine start
        carAudio.PlayOneShot(HorrorAudio.Pop(), 0.7f);
        yield return new WaitForSeconds(0.5f);

        carAudio.clip = HorrorAudio.EngineLoop();
        carAudio.loop = true;
        carAudio.volume = 0.75f;
        carAudio.pitch = 0.85f;
        carAudio.Play();
        TurnOnHeadlights();
        yield return new WaitForSeconds(1.1f);

        // the gate drags itself open
        if (gate != null)
        {
            AudioSource.PlayClipAtPoint(HorrorAudio.GateCreak(), gate.position, 1f);
            yield return OpenGateRoutine();
        }

        // roll out
        Vector3 driveDirection = gate != null
            ? (StripY(gate.position - transform.position)).normalized
            : transform.forward;

        GameObject fade = BuildFadeOverlay(out CanvasGroup fadeGroup);
        float driveTime = 6f;
        for (float elapsed = 0f; elapsed < driveTime; elapsed += Time.deltaTime)
        {
            float speed = Mathf.Lerp(0.5f, 9f, Mathf.Clamp01(elapsed / 3f));
            carAudio.pitch = Mathf.Lerp(0.85f, 1.35f, Mathf.Clamp01(elapsed / 4f));
            transform.position += driveDirection * (speed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(driveDirection, Vector3.up), Time.deltaTime * 1.2f);
            fadeGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 3.2f) / 2.4f));
            yield return null;
        }

        fadeGroup.alpha = 1f;
        carAudio.Stop();
        ShowVictoryScreen(fade.transform);
    }

    private IEnumerator OpenGateRoutine()
    {
        Bounds gateBounds = new Bounds(gate.position, Vector3.one);
        Renderer gateRenderer = gate.GetComponentInChildren<Renderer>();
        if (gateRenderer != null)
        {
            gateBounds = gateRenderer.bounds;
        }

        // sliding gate: drag it along the fence line by almost its width
        float width = Mathf.Max(gateBounds.size.x, gateBounds.size.z);
        Vector3 slideDirection = gateBounds.size.x >= gateBounds.size.z ? Vector3.right : Vector3.forward;
        Vector3 start = gate.position;
        Vector3 end = start + slideDirection * (width * 0.92f);

        const float duration = 3.2f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            // stick-slip easing so it judders like rusty metal
            float judder = t + Mathf.Sin(t * 26f) * 0.012f;
            gate.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(judder)));
            yield return null;
        }

        gate.position = end;
    }

    private void TurnOnHeadlights()
    {
        leftHeadlight = CreateHeadlight("Left Headlight", -0.7f);
        rightHeadlight = CreateHeadlight("Right Headlight", 0.7f);
    }

    private Light CreateHeadlight(string name, float lateralOffset)
    {
        Bounds bounds = ComputeBounds();
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.position = bounds.center + transform.forward * bounds.extents.z + transform.right * lateralOffset;
        lightObject.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);

        Light headlight = lightObject.AddComponent<Light>();
        headlight.type = LightType.Spot;
        headlight.spotAngle = 55f;
        headlight.color = new Color(1f, 0.93f, 0.78f, 1f);
        headlight.intensity = 4f;
        headlight.range = 30f;

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = lightObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(9000f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
        }

        return headlight;
    }

    private static Vector3 StripY(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static GameObject BuildFadeOverlay(out CanvasGroup group)
    {
        GameObject canvasObject = new GameObject("Escape Fade Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;
        group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;

        GameObject blackout = new GameObject("Blackout");
        blackout.transform.SetParent(canvasObject.transform, false);
        Image image = blackout.AddComponent<Image>();
        image.color = Color.black;
        RectTransform rect = blackout.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return canvasObject;
    }

    private void ShowVictoryScreen(Transform canvasRoot)
    {
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        if (canvasRoot.GetComponent<GraphicRaycaster>() == null)
        {
            canvasRoot.gameObject.AddComponent<GraphicRaycaster>();
        }

        GameObject titleObject = new GameObject("Victory Title");
        titleObject.transform.SetParent(canvasRoot, false);
        TextMeshProUGUI title = titleObject.AddComponent<TextMeshProUGUI>();
        title.text = "ТЫ СБЕЖАЛ ОТ АНТОНЧИКА";
        title.fontSize = 84f;
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(0.85f, 0.82f, 0.7f, 1f);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 90f);
        titleRect.sizeDelta = new Vector2(1700f, 180f);

        GameObject subObject = new GameObject("Victory Subtitle");
        subObject.transform.SetParent(canvasRoot, false);
        TextMeshProUGUI subtitle = subObject.AddComponent<TextMeshProUGUI>();
        subtitle.text = "Но он запомнил твоё лицо...";
        subtitle.fontSize = 32f;
        subtitle.fontStyle = FontStyles.Italic;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.color = new Color(0.55f, 0.5f, 0.48f, 1f);
        RectTransform subRect = subtitle.rectTransform;
        subRect.anchorMin = new Vector2(0.5f, 0.5f);
        subRect.anchorMax = new Vector2(0.5f, 0.5f);
        subRect.anchoredPosition = new Vector2(0f, -10f);
        subRect.sizeDelta = new Vector2(1400f, 60f);

        GameObject buttonObject = new GameObject("Menu Button");
        buttonObject.transform.SetParent(canvasRoot, false);
        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0.05f, 0.05f, 0.06f, 0.92f);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -140f);
        buttonRect.sizeDelta = new Vector2(420f, 72f);

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = "Главное меню";
        label.fontSize = 34f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.85f, 0.8f, 0.78f, 1f);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(() =>
        {
            Time.timeScale = 1f;
            SceneLoadingScreen.Load("menu");
        });

        Cursor.lockState = CursorLockMode.None;
        SoftwareCursor.Show(true);
    }
}
