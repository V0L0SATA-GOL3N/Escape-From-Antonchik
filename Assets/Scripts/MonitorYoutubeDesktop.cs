using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;

public class MonitorYoutubeDesktop : MonoBehaviour
{
    [SerializeField] private Renderer screenRenderer;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private VideoClip[] localVideos = new VideoClip[6];
    [SerializeField] private Texture2D[] thumbnailImages = new Texture2D[6];
    [SerializeField] private Sprite youtubeLogo;
    [SerializeField] private Texture2D cursorTexture;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private string[] videoTitles =
    {
"Хрустим и рисуем в PL - part 1",
"<sprite name=\"car\"> КУПИЛ АККОРД И СРАЗУ ПОЖАЛЕЛ <sprite name=\"skull\"><sprite name=\"wrench\">",
"ОБМЕНЯЛ БУСТ НА ГРЕЧКУ?? <sprite name=\"sob\">",
"<sprite name=\"green_apple\"> САМОЕ ДЕШЁВОЕ ПОЙЛО СНГ?!",
"КУРЮ ЭТО, ЧТОБЫ ВАМ НЕ ПРИШЛОСЬ <sprite name=\"smoking\"><sprite name=\"skull2\"> | тестим сигареты ФЭСТ",
"<sprite name=\"fire\"><sprite name=\"fire\"><sprite name=\"fire\"> ТОНУС НАШЕЛ ДЕВУШКУ?!"
    };
    [SerializeField] private Vector2 desktopSize = new Vector2(960f, 540f);
    [SerializeField] private float mouseSpeed = 28f;

    private const int UiLayer = 5;

    private RenderTexture desktopTexture;
    private RenderTexture videoTexture;
    private Camera desktopCamera;
    private Canvas desktopCanvas;
    private CanvasGroup desktopGroup;
    private GameObject catalogRoot;
    private GameObject playerRoot;
    private RawImage videoImage;
    private TextMeshProUGUI playerTitle;
    private TextMeshProUGUI playPauseLabel;
    private RectTransform virtualCursor;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private bool hasCursorSnapshot;
    private bool desktopInputEnabled;
    private Vector2 cursorPosition;
    private int currentVideoIndex = -1;
    private readonly List<DesktopHitTarget> catalogHitTargets = new List<DesktopHitTarget>();
    private readonly List<DesktopHitTarget> playerHitTargets = new List<DesktopHitTarget>();

    private void Awake()
    {
        ResolveReferences();
        ConfigureVideoPlayer();
        BuildDesktop();
        ShowCatalog();
        SetDesktopInput(false);
    }

    private void OnEnable()
    {
        DoorRaycastInteractor.SeatedStateChanged += HandleSeatedStateChanged;
    }

    private void OnDisable()
    {
        DoorRaycastInteractor.SeatedStateChanged -= HandleSeatedStateChanged;
        RestoreCursor();
        ReleaseDesktopTexture();
    }

    private void Update()
    {
        if (!desktopInputEnabled)
        {
            return;
        }

        cursorPosition.x += Input.GetAxisRaw("Mouse X") * mouseSpeed;
        cursorPosition.y -= Input.GetAxisRaw("Mouse Y") * mouseSpeed;
        cursorPosition.x = Mathf.Clamp(cursorPosition.x, 0f, desktopSize.x);
        cursorPosition.y = Mathf.Clamp(cursorPosition.y, 0f, desktopSize.y);
        UpdateVirtualCursor();

        if (Input.GetMouseButtonDown(0))
        {
            ClickAtCursor();
        }
    }

    private void ResolveReferences()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (screenRenderer == null)
        {
            GameObject screenObject = GameObject.Find("Group_007_screen_0");
            if (screenObject != null)
            {
                screenRenderer = screenObject.GetComponent<Renderer>();
            }
        }

        VideoClip fallbackClip = videoPlayer != null ? videoPlayer.clip : null;
        if (fallbackClip == null)
        {
            return;
        }

        for (int i = 0; i < localVideos.Length; i++)
        {
            if (localVideos[i] == null)
            {
                localVideos[i] = fallbackClip;
            }
        }
    }

    private void ConfigureVideoPlayer()
    {
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.Stop();
        }

        if (videoPlayer == null)
        {
            return;
        }

        if (videoTexture == null)
        {
            videoTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "Monitor YouTube Video",
                useMipMap = false,
                autoGenerateMips = false
            };
            videoTexture.Create();
        }

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoTexture;
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        if (audioSource != null)
        {
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.SetTargetAudioSource(0, audioSource);
        }

        videoPlayer.Stop();
    }

    private void BuildDesktop()
    {
        int width = Mathf.RoundToInt(desktopSize.x);
        int height = Mathf.RoundToInt(desktopSize.y);
        desktopTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
        {
            name = "Monitor YouTube Desktop",
            useMipMap = false,
            autoGenerateMips = false
        };
        desktopTexture.Create();

        GameObject cameraObject = new GameObject("YouTube Desktop Camera");
        cameraObject.transform.SetParent(transform, false);
        desktopCamera = cameraObject.AddComponent<Camera>();
        desktopCamera.clearFlags = CameraClearFlags.SolidColor;
        desktopCamera.backgroundColor = new Color(0.05f, 0.05f, 0.055f, 1f);
        desktopCamera.orthographic = true;
        desktopCamera.orthographicSize = height * 0.5f;
        desktopCamera.nearClipPlane = 0.1f;
        desktopCamera.farClipPlane = 10f;
        desktopCamera.cullingMask = 1 << UiLayer;
        desktopCamera.targetTexture = desktopTexture;
        desktopCamera.transform.localPosition = new Vector3(0f, 0f, -5f);
        desktopCamera.transform.localRotation = Quaternion.identity;

        GameObject canvasObject = new GameObject("YouTube Desktop Canvas");
        canvasObject.transform.SetParent(transform, false);
        desktopCanvas = canvasObject.AddComponent<Canvas>();
        desktopCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        desktopCanvas.worldCamera = desktopCamera;
        desktopCanvas.planeDistance = 1f;
        desktopCanvas.sortingOrder = 0;
        canvasObject.AddComponent<GraphicRaycaster>();
        desktopGroup = canvasObject.AddComponent<CanvasGroup>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = desktopSize;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = desktopSize;
        SetLayerRecursive(canvasObject, UiLayer);

        GameObject background = CreatePanel("Desktop Background", canvasRect, new Color(0.05f, 0.05f, 0.055f, 1f));
        Stretch(background.GetComponent<RectTransform>());

        catalogRoot = new GameObject("Catalog");
        catalogRoot.transform.SetParent(canvasRect, false);
        Stretch(catalogRoot.AddComponent<RectTransform>());
        SetLayerRecursive(catalogRoot, UiLayer);

        playerRoot = new GameObject("Player");
        playerRoot.transform.SetParent(canvasRect, false);
        Stretch(playerRoot.AddComponent<RectTransform>());
        SetLayerRecursive(playerRoot, UiLayer);

        BuildCatalog();
        BuildPlayer();
        BuildVirtualCursor(canvasRect);
        ApplyDesktopTextureToScreen();
    }

    private void ApplyDesktopTextureToScreen()
    {
        if (screenRenderer == null || desktopTexture == null)
        {
            return;
        }

        Material screenMaterial = screenRenderer.material;
        screenMaterial.mainTexture = desktopTexture;
        screenMaterial.mainTextureScale = new Vector2(-1f, 1f);
        screenMaterial.mainTextureOffset = new Vector2(1f, 0f);
    }

    private void BuildCatalog()
    {
        RectTransform root = catalogRoot.GetComponent<RectTransform>();
        GameObject catalogBackground = CreatePanel("Catalog Background", root, Color.white);
        Stretch(catalogBackground.GetComponent<RectTransform>());

        if (youtubeLogo != null)
        {
            CreateLogo("YouTube Logo", root, new Vector2(26f, -33f), new Vector2(34f, 24f), youtubeLogo);
        }

        CreateText("YouTube", root, new Vector2(66f, -22f), new Vector2(240f, 46f), 34, FontStyle.Bold, new Color(0.15f, 0.15f, 0.15f, 1f), TextAnchor.MiddleLeft);
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            int column = i % 3;
            int row = i / 3;
            float x = 32f + column * 306f;
            float y = -102f - row * 218f;
            Rect hitRect = new Rect(x, -y, 278f, 196f);

            Button button = CreateButton("Video " + (i + 1), root, new Vector2(x, y), new Vector2(278f, 196f), string.Empty, Color.white, new Color(1f, 0.96f, 0.78f, 1f), new Color(0.98f, 0.92f, 0.62f, 1f));
            button.onClick.AddListener(() => PlayVideo(index));
            catalogHitTargets.Add(new DesktopHitTarget(hitRect, () => PlayVideo(index)));

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            CreateThumbnail("Thumbnail", buttonRect, GetThumbnail(i), new Vector2(10f, -10f), new Vector2(258f, 118f));
            CreateText("▶", buttonRect, new Vector2(111f, -43f), new Vector2(56f, 44f), 32, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            CreatePanel("Meta Background", buttonRect, new Color(1f, 0.985f, 0.92f, 1f), new Vector2(10f, -132f), new Vector2(258f, 56f));
          CreateText(
    GetTitle(i),
    buttonRect,
    new Vector2(16f, -136f),
    new Vector2(246f, 40f),
    14,
    FontStyle.Bold,
    Color.black,
    TextAnchor.UpperLeft
);

CreateText(
    "Janusz the Jew",
    buttonRect,
    new Vector2(16f, -166f),
    new Vector2(120f, 20f),
    14,
    FontStyle.Bold,
    new Color(0.2f, 0.2f, 0.2f, 1f), // dark gray
    TextAnchor.MiddleLeft
);        }
    }

    private void BuildPlayer()
    {
        RectTransform root = playerRoot.GetComponent<RectTransform>();

        GameObject videoObject = new GameObject("Video Surface");
        videoObject.transform.SetParent(root, false);
        videoImage = videoObject.AddComponent<RawImage>();
        videoImage.color = Color.white;
        videoImage.texture = videoTexture;
        Stretch(videoObject.GetComponent<RectTransform>());
        SetLayerRecursive(videoObject, UiLayer);

        GameObject topBar = CreatePanel("Top Bar", root, new Color(0f, 0f, 0f, 0.82f), new Vector2(0f, 0f), new Vector2(desktopSize.x, 64f));
        RectTransform topRect = topBar.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = Vector2.zero;

        Button backButton = CreateButton("Catalog Button", root, new Vector2(18f, -16f), new Vector2(116f, 36f), "Catalog");
        backButton.onClick.AddListener(ShowCatalog);
        playerHitTargets.Add(new DesktopHitTarget(new Rect(18f, 16f, 116f, 36f), ShowCatalog));

        playerTitle = CreateText("Player Title", root, new Vector2(150f, -18f), new Vector2(600f, 34f), 18, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);

        GameObject bottomBar = CreatePanel("Bottom Bar", root, new Color(0f, 0f, 0f, 0.82f), new Vector2(0f, 0f), new Vector2(desktopSize.x, 76f));
        RectTransform bottomRect = bottomBar.GetComponent<RectTransform>();
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.anchoredPosition = Vector2.zero;

        Button playPauseButton = CreateButton("Play Pause Button", root, new Vector2(28f, 18f), new Vector2(128f, 40f), "Pause");
        RectTransform playRect = playPauseButton.GetComponent<RectTransform>();
        playRect.anchorMin = new Vector2(0f, 0f);
        playRect.anchorMax = new Vector2(0f, 0f);
        playRect.pivot = new Vector2(0f, 0f);
        playPauseButton.onClick.AddListener(TogglePlayback);
        playPauseLabel = playPauseButton.GetComponentInChildren<TextMeshProUGUI>();
        playerHitTargets.Add(new DesktopHitTarget(new Rect(28f, desktopSize.y - 58f, 128f, 40f), TogglePlayback));
    }

    private void BuildVirtualCursor(RectTransform canvasRect)
    {
        GameObject cursorObject = new GameObject("Virtual Mouse Cursor");
        cursorObject.transform.SetParent(canvasRect, false);
        Image cursorImage = cursorObject.AddComponent<Image>();
        cursorImage.sprite = CreateSprite(cursorTexture);
        cursorImage.color = new Color(1f, 1f, 1f, 0.95f);
        cursorImage.raycastTarget = false;

        virtualCursor = cursorObject.GetComponent<RectTransform>();
        virtualCursor.anchorMin = new Vector2(0f, 1f);
        virtualCursor.anchorMax = new Vector2(0f, 1f);
        virtualCursor.pivot = new Vector2(0.5f, 0.5f);
        virtualCursor.sizeDelta = new Vector2(18f, 18f);
        cursorPosition = desktopSize * 0.5f;
        UpdateVirtualCursor();
        cursorObject.SetActive(false);
        SetLayerRecursive(cursorObject, UiLayer);
    }

    private void PlayVideo(int index)
    {
        if (videoPlayer == null)
        {
            return;
        }

        VideoClip selectedClip = GetClip(index);
        if (selectedClip == null)
        {
            return;
        }

        currentVideoIndex = index;
        catalogRoot.SetActive(false);
        playerRoot.SetActive(true);
        if (playerTitle != null)
        {
            playerTitle.text = GetTitle(index);
        }

        if (videoImage != null)
        {
            videoImage.texture = videoTexture;
        }

        videoPlayer.clip = selectedClip;
        videoPlayer.Play();

        if (playPauseLabel != null)
        {
            playPauseLabel.text = "Pause";
        }
    }

    private void TogglePlayback()
    {
        if (videoPlayer == null || currentVideoIndex < 0)
        {
            return;
        }

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();

            if (playPauseLabel != null)
            {
                playPauseLabel.text = "Play";
            }
        }
        else
        {
            videoPlayer.Play();

            if (playPauseLabel != null)
            {
                playPauseLabel.text = "Pause";
            }
        }
    }

    private void ShowCatalog()
    {
        currentVideoIndex = -1;
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        ApplyDesktopTextureToScreen();

        if (catalogRoot != null)
        {
            catalogRoot.SetActive(true);
        }

        if (playerRoot != null)
        {
            playerRoot.SetActive(false);
        }
    }

    private void UpdatePlayPauseLabel()
    {
        if (playPauseLabel != null && videoPlayer != null)
        {
            playPauseLabel.text = videoPlayer.isPlaying ? "Pause" : "Play";
        }
    }

    private void HandleSeatedStateChanged(bool isSeated)
    {
        SetDesktopInput(isSeated);
    }

    private void SetDesktopInput(bool enabled)
    {
        desktopInputEnabled = enabled;

        if (desktopGroup != null)
        {
            desktopGroup.interactable = true;
            desktopGroup.blocksRaycasts = false;
        }

        if (virtualCursor != null)
        {
            virtualCursor.gameObject.SetActive(enabled);
        }

        if (enabled)
        {
            if (!hasCursorSnapshot)
            {
                previousCursorVisible = Cursor.visible;
                previousCursorLockMode = Cursor.lockState;
                hasCursorSnapshot = true;
            }

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            RestoreCursor();
        }
    }

    private void RestoreCursor()
    {
        if (!hasCursorSnapshot)
        {
            return;
        }

        Cursor.visible = previousCursorVisible;
        Cursor.lockState = previousCursorLockMode;
        hasCursorSnapshot = false;
    }

    private void UpdateVirtualCursor()
    {
        if (virtualCursor != null)
        {
            virtualCursor.anchoredPosition = new Vector2(cursorPosition.x, -cursorPosition.y);
        }
    }

    private void ClickAtCursor()
    {
        PlayClickSound();

        List<DesktopHitTarget> targets = catalogRoot != null && catalogRoot.activeSelf ? catalogHitTargets : playerHitTargets;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].Contains(cursorPosition))
            {
                targets[i].Click();
                return;
            }
        }
    }

    private void PlayClickSound()
    {
        if (clickSound == null || audioSource == null)
        {
            return;
        }

        audioSource.PlayOneShot(clickSound);
    }

    private VideoClip GetClip(int index)
    {
        if (localVideos != null && index >= 0 && index < localVideos.Length && localVideos[index] != null)
        {
            return localVideos[index];
        }

        return videoPlayer != null ? videoPlayer.clip : null;
    }

    private string GetTitle(int index)
    {
        if (videoTitles != null && index >= 0 && index < videoTitles.Length && !string.IsNullOrWhiteSpace(videoTitles[index]))
        {
            return videoTitles[index];
        }

        return "Local video " + (index + 1);
    }

    private Texture2D GetThumbnail(int index)
    {
        if (thumbnailImages != null && index >= 0 && index < thumbnailImages.Length)
        {
            return thumbnailImages[index];
        }

        return null;
    }

    private Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, string label)
    {
        return CreateButton(name, parent, anchoredPosition, size, label, new Color(0.13f, 0.13f, 0.14f, 1f), new Color(0.24f, 0.24f, 0.25f, 1f), new Color(0.72f, 0.05f, 0.04f, 1f));
    }

    private Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, string label, Color normalColor, Color highlightedColor, Color pressedColor)
    {
        GameObject buttonObject = CreatePanel(name, parent, normalColor, anchoredPosition, size);
        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = highlightedColor;
        colors.pressedColor = pressedColor;
        button.colors = colors;

        if (!string.IsNullOrEmpty(label))
        {
            CreateText("Label", buttonObject.transform, Vector2.zero, size, 17, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).text = label;
        }

        return button;
    }

    private Image CreateLogo(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Sprite sprite)
    {
        GameObject logoObject = new GameObject(name);
        logoObject.transform.SetParent(parent, false);
        Image image = logoObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = Color.white;

        RectTransform rect = logoObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        SetLayerRecursive(logoObject, UiLayer);
        return image;
    }

    private Sprite CreateSprite(Texture2D texture)
    {
        if (texture == null)
        {
            return null;
        }

        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        return CreatePanel(name, parent, color, Vector2.zero, desktopSize);
    }

    private GameObject CreatePanel(string name, Transform parent, Color color, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = color;

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        SetLayerRecursive(panel, UiLayer);
        return panel;
    }

    private RawImage CreateThumbnail(string name, Transform parent, Texture texture, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject thumbnailObject = new GameObject(name);
        thumbnailObject.transform.SetParent(parent, false);
        RawImage thumbnail = thumbnailObject.AddComponent<RawImage>();
        thumbnail.texture = texture;
        thumbnail.color = texture != null ? Color.white : new Color(0.12f, 0.12f, 0.13f, 1f);

        RectTransform rect = thumbnailObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        SetLayerRecursive(thumbnailObject, UiLayer);
        return thumbnail;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, int fontSize, FontStyle style, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();

        text.fontSize = fontSize;

        // Map Unity FontStyle to TMPro FontStyles
        switch (style)
        {
            case FontStyle.Bold:
                text.fontStyle = FontStyles.Bold;
                break;
            case FontStyle.Italic:
                text.fontStyle = FontStyles.Italic;
                break;
            case FontStyle.BoldAndItalic:
                text.fontStyle = FontStyles.Bold | FontStyles.Italic;
                break;
            default:
                text.fontStyle = FontStyles.Normal;
                break;
        }

        text.color = color;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;

        // Map TextAnchor to TextAlignmentOptions
        switch (alignment)
        {
            case TextAnchor.UpperLeft:
            case TextAnchor.MiddleLeft:
            case TextAnchor.LowerLeft:
                text.alignment = TextAlignmentOptions.Left;
                break;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                text.alignment = TextAlignmentOptions.Right;
                break;
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter:
            default:
                text.alignment = TextAlignmentOptions.Center;
                break;
        }

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        SetLayerRecursive(textObject, UiLayer);
        text.text = name;
        return text;
    }

    private void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void ReleaseDesktopTexture()
    {
        if (desktopTexture == null)
        {
            return;
        }

        desktopTexture.Release();
        Destroy(desktopTexture);
        desktopTexture = null;

        if (videoTexture != null)
        {
            videoTexture.Release();
            Destroy(videoTexture);
            videoTexture = null;
        }
    }

    private void SetLayerRecursive(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }

    private sealed class DesktopHitTarget
    {
        private readonly Rect rect;
        private readonly Action click;

        public DesktopHitTarget(Rect rect, Action click)
        {
            this.rect = rect;
            this.click = click;
        }

        public bool Contains(Vector2 position)
        {
            return position.x >= rect.xMin
                && position.x <= rect.xMax
                && position.y >= rect.yMin
                && position.y <= rect.yMax;
        }

        public void Click()
        {
            click?.Invoke();
        }
    }
}
