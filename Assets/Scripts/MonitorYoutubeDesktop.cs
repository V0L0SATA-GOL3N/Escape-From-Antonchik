using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;

// The PC in the room. Renders a little "AntonOS" desktop to the monitor's
// render texture: wallpaper, taskbar, a YouTube app and a Linux-like
// terminal. The screen material is driven emissively so the panel glows
// evenly on High (no shadowed sides), and the UV flip is chosen per
// pipeline so the Built-in fallback isn't mirrored.
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
    // HDRP emissive colour is in nits; the room is dark, so keep it modest
    // or bloom blows the text/screen out
    [SerializeField] private float screenEmissionNits = 15f;

    // Music app: the tracks live in Assets/Music and are wired here. The
    // display name and the terminal "filename" both come from clip.name unless
    // an explicit override is supplied in musicTitles (parallel array).
    [SerializeField] private AudioClip[] musicTracks = new AudioClip[0];
    [SerializeField] private string[] musicTitles = new string[0];

    private const int UiLayer = 5;
    private const float TitleBarHeight = 30f;
    private const float TaskbarHeight = 36f;
    // Raw HDR multiplier for the screen image (we drive emission with
    // _UseEmissiveIntensity = 0, so this is NOT nits). 1.0 ≈ a normal white
    // monitor; a little over 1 makes it read as "on" in the dark room without
    // blowing white UI (e.g. the YouTube page) into bloom.
    private const float ScreenEmissionStrength = 3f;

    public static bool TerminalCaptureActive { get; private set; }

    private RenderTexture desktopTexture;
    private RenderTexture videoTexture;
    private Camera desktopCamera;
    private Canvas desktopCanvas;
    private CanvasGroup desktopGroup;

    private GameObject iconsRoot;
    private GameObject youtubeWindow;
    private GameObject catalogRoot;
    private GameObject playerRoot;
    private GameObject terminalWindow;
    private GameObject musicWindow;
    private RawImage videoImage;
    private TextMeshProUGUI playerTitle;
    private TextMeshProUGUI playPauseLabel;
    private TextMeshProUGUI taskbarClock;
    private TextMeshProUGUI terminalText;
    private TextMeshProUGUI musicNowPlaying;
    private TextMeshProUGUI musicPlayPauseLabel;
    private TextMeshProUGUI musicTimeLabel;
    private TextMeshProUGUI videoTimeLabel;
    private RectTransform musicSeekFill;
    private RectTransform musicSeekHandle;
    private RectTransform videoSeekFill;
    private RectTransform videoSeekHandle;
    private float musicSeekWidth;
    private float videoSeekWidth;
    private RectTransform virtualCursor;

    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private bool hasCursorSnapshot;
    private bool desktopInputEnabled;
    private Vector2 cursorPosition;
    private int currentVideoIndex = -1;

    private AudioSource musicAudioSource;
    private int currentTrackIndex = -1;
    private bool musicPaused;
    private bool musicWasPlaying;

    private readonly List<DesktopHitTarget> iconHitTargets = new List<DesktopHitTarget>();
    private readonly List<DesktopHitTarget> catalogHitTargets = new List<DesktopHitTarget>();
    private readonly List<DesktopHitTarget> playerHitTargets = new List<DesktopHitTarget>();
    private readonly List<DesktopHitTarget> terminalHitTargets = new List<DesktopHitTarget>();
    private readonly List<DesktopHitTarget> musicHitTargets = new List<DesktopHitTarget>();
    private readonly List<DesktopScrubTarget> musicScrubTargets = new List<DesktopScrubTarget>();
    private readonly List<DesktopScrubTarget> playerScrubTargets = new List<DesktopScrubTarget>();
    private DesktopScrubTarget activeScrubTarget;

    // terminal state
    private readonly List<string> terminalLines = new List<string>();
    private string terminalInput = string.Empty;
    private string terminalCwd = "~";
    private const int TerminalMaxLines = 19;
    private const string MusicDir = "music";
    private static readonly Dictionary<string, string> TerminalFiles = new Dictionary<string, string>
    {
        { "zametka.txt", "Мы жрем. Мы жрали. Мы сожрем\n" },
        { "plan.txt", "1. поиграться с яичками\n2. взять виски\n3. выйти" },
        { "sshd.log", "WARN: 47 неудачных попыток входа с 192.168.0.66\nWARN: пользователь 'server.exe' не существует\nWARN: Его никогда здесь не было." },
    };

    private void Awake()
    {
        ResolveReferences();
        ConfigureVideoPlayer();
        ConfigureMusicSource();
        BuildDesktop();
        ShowDesktop();
        SetDesktopInput(false);
        InvokeRepeating(nameof(ReapplyScreenMaterial), 2f, 2f);
    }

    private void OnEnable()
    {
        DoorRaycastInteractor.SeatedStateChanged += HandleSeatedStateChanged;
    }

    private void OnDisable()
    {
        DoorRaycastInteractor.SeatedStateChanged -= HandleSeatedStateChanged;
        TerminalCaptureActive = false;
        RestoreCursor();
        ReleaseDesktopTexture();
    }

    private void Update()
    {
        UpdateClock();
        UpdateMusicPlayback();
        UpdateTimelines();

        TerminalCaptureActive = desktopInputEnabled && terminalWindow != null && terminalWindow.activeSelf;

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
            // a timeline grab takes priority over button clicks
            if (!TryBeginScrub())
            {
                ClickAtCursor();
            }
        }

        if (activeScrubTarget != null)
        {
            if (Input.GetMouseButton(0))
            {
                activeScrubTarget.Scrub(cursorPosition);
            }
            else
            {
                activeScrubTarget = null;
            }
        }

        if (TerminalCaptureActive)
        {
            HandleTerminalTyping();
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

    // ------------------------------------------------------------------
    // desktop construction
    // ------------------------------------------------------------------

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

        BuildWallpaper(canvasRect);
        BuildDesktopIcons(canvasRect);

        youtubeWindow = BuildWindowFrame(canvasRect, "YouTube", new Color(0.12f, 0.12f, 0.13f, 1f), CloseYoutube);
        catalogRoot = new GameObject("Catalog");
        catalogRoot.transform.SetParent(youtubeWindow.transform, false);
        StretchBelowTitle(catalogRoot.AddComponent<RectTransform>());
        SetLayerRecursive(catalogRoot, UiLayer);

        playerRoot = new GameObject("Player");
        playerRoot.transform.SetParent(youtubeWindow.transform, false);
        StretchBelowTitle(playerRoot.AddComponent<RectTransform>());
        SetLayerRecursive(playerRoot, UiLayer);

        BuildCatalog();
        BuildPlayer();

        terminalWindow = BuildWindowFrame(canvasRect, "anton@antonchik-pc: ~", new Color(0.09f, 0.1f, 0.11f, 1f), CloseTerminal);
        BuildTerminal();

        musicWindow = BuildWindowFrame(canvasRect, "Music — AntonOS", new Color(0.1f, 0.08f, 0.13f, 1f), CloseMusic);
        BuildMusic();

        BuildTaskbar(canvasRect);
        BuildVirtualCursor(canvasRect);
        ApplyDesktopTextureToScreen();
    }

    private void BuildWallpaper(RectTransform canvasRect)
    {
        GameObject background = CreatePanel("Desktop Wallpaper", canvasRect, new Color(0.07f, 0.1f, 0.13f, 1f));
        Stretch(background.GetComponent<RectTransform>());

        GameObject glow = CreatePanel("Wallpaper Glow", background.transform, new Color(0.1f, 0.16f, 0.2f, 1f),
            new Vector2(desktopSize.x * 0.2f, -desktopSize.y * 0.18f), new Vector2(desktopSize.x * 0.6f, desktopSize.y * 0.5f));
        glow.GetComponent<Image>().color = new Color(0.1f, 0.16f, 0.2f, 0.55f);

        TextMeshProUGUI logo = CreateText("AntonOS", background.transform,
            new Vector2(desktopSize.x * 0.5f - 240f, -desktopSize.y * 0.5f + 40f), new Vector2(480f, 60f),
            42, FontStyle.Bold, new Color(1f, 1f, 1f, 0.07f), TextAnchor.MiddleCenter);
        logo.text = "AntonOS 1.0 LTS";
    }

    private void BuildDesktopIcons(RectTransform canvasRect)
    {
        iconsRoot = new GameObject("Desktop Icons");
        iconsRoot.transform.SetParent(canvasRect, false);
        Stretch(iconsRoot.AddComponent<RectTransform>());
        SetLayerRecursive(iconsRoot, UiLayer);

        BuildIcon("YouTube", new Vector2(26f, -28f), new Color(0.78f, 0.06f, 0.05f, 1f), "YT", Color.white, OpenYoutube);
        BuildIcon("Terminal", new Vector2(26f, -134f), new Color(0.13f, 0.14f, 0.15f, 1f), ">_", new Color(0.45f, 0.95f, 0.45f, 1f), OpenTerminal);
        BuildIcon("Music", new Vector2(26f, -240f), new Color(0.16f, 0.1f, 0.22f, 1f), "MP3", new Color(0.78f, 0.6f, 0.98f, 1f), OpenMusic);
    }

    private void BuildIcon(string label, Vector2 position, Color tileColor, string glyph, Color glyphColor, Action onOpen)
    {
        RectTransform parent = iconsRoot.GetComponent<RectTransform>();
        GameObject tile = CreatePanel(label + " Icon", parent, tileColor, position, new Vector2(58f, 58f));

        TextMeshProUGUI glyphText = CreateText(label + " Glyph", tile.transform, Vector2.zero, new Vector2(58f, 58f), 26, FontStyle.Bold, glyphColor, TextAnchor.MiddleCenter);
        glyphText.text = glyph;

        TextMeshProUGUI caption = CreateText(label + " Caption", parent, position + new Vector2(-12f, -60f), new Vector2(82f, 22f), 13, FontStyle.Normal, new Color(0.92f, 0.92f, 0.92f, 1f), TextAnchor.MiddleCenter);
        caption.text = label;

        iconHitTargets.Add(new DesktopHitTarget(new Rect(position.x, -position.y, 58f, 58f), onOpen));
    }

    private GameObject BuildWindowFrame(RectTransform canvasRect, string title, Color bodyColor, Action onClose)
    {
        GameObject window = new GameObject(title + " Window");
        window.transform.SetParent(canvasRect, false);
        Stretch(window.AddComponent<RectTransform>());
        SetLayerRecursive(window, UiLayer);

        GameObject body = CreatePanel("Body", window.transform, bodyColor);
        Stretch(body.GetComponent<RectTransform>());

        GameObject titleBar = CreatePanel("Title Bar", window.transform, new Color(0.16f, 0.17f, 0.19f, 1f),
            Vector2.zero, new Vector2(desktopSize.x, TitleBarHeight));

        TextMeshProUGUI titleText = CreateText("Title", titleBar.transform, new Vector2(36f, -4f), new Vector2(520f, 22f), 14, FontStyle.Bold, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
        titleText.text = title;

        // ubuntu-ish traffic lights on the left
        CreatePanel("Close Dot", titleBar.transform, new Color(0.91f, 0.3f, 0.24f, 1f), new Vector2(10f, -9f), new Vector2(12f, 12f));
        CreatePanel("Min Dot", titleBar.transform, new Color(0.45f, 0.45f, 0.45f, 1f), new Vector2(desktopSize.x - 52f, -9f), new Vector2(12f, 12f));

        GameObject closeButton = CreatePanel("Close Button", titleBar.transform, new Color(0.91f, 0.3f, 0.24f, 0f), new Vector2(0f, 0f), new Vector2(34f, TitleBarHeight));
        TextMeshProUGUI closeGlyph = CreateText("X", closeButton.transform, Vector2.zero, new Vector2(34f, TitleBarHeight), 14, FontStyle.Bold, new Color(1f, 1f, 1f, 0.001f), TextAnchor.MiddleCenter);
        closeGlyph.text = string.Empty;

        // click zone for the red dot
        List<DesktopHitTarget> targets = null;
        if (title.StartsWith("anton@"))
        {
            targets = terminalHitTargets;
        }
        else if (title.StartsWith("Music"))
        {
            targets = musicHitTargets;
        }

        var closeTarget = new DesktopHitTarget(new Rect(4f, 3f, 26f, 26f), onClose);
        if (targets != null)
        {
            targets.Add(closeTarget);
        }
        else
        {
            playerHitTargets.Add(closeTarget);
            catalogHitTargets.Add(new DesktopHitTarget(new Rect(4f, 3f, 26f, 26f), onClose));
        }

        window.SetActive(false);
        return window;
    }

    private static void StretchBelowTitle(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new Vector2(0f, -TitleBarHeight);
    }

    private void BuildTaskbar(RectTransform canvasRect)
    {
        GameObject taskbar = CreatePanel("Taskbar", canvasRect, new Color(0.05f, 0.055f, 0.06f, 0.97f),
            new Vector2(0f, -(desktopSize.y - TaskbarHeight)), new Vector2(desktopSize.x, TaskbarHeight));

        TextMeshProUGUI start = CreateText("Start", taskbar.transform, new Vector2(12f, -6f), new Vector2(160f, 24f), 15, FontStyle.Bold, new Color(0.5f, 0.85f, 0.5f, 1f), TextAnchor.MiddleLeft);
        start.text = "AntonOS";

        taskbarClock = CreateText("Clock", taskbar.transform, new Vector2(desktopSize.x - 110f, -6f), new Vector2(100f, 24f), 14, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleRight);
        taskbarClock.text = "--:--";
    }

    private void UpdateClock()
    {
        if (taskbarClock != null)
        {
            taskbarClock.text = DateTime.Now.ToString("HH:mm");
        }
    }

    // ------------------------------------------------------------------
    // youtube app (catalog + player), mostly the original implementation
    // ------------------------------------------------------------------

    private void BuildCatalog()
    {
        RectTransform root = catalogRoot.GetComponent<RectTransform>();
        GameObject catalogBackground = CreatePanel("Catalog Background", root, Color.white);
        Stretch(catalogBackground.GetComponent<RectTransform>());

        if (youtubeLogo != null)
        {
            CreateLogo("YouTube Logo", root, new Vector2(26f, -8f), new Vector2(34f, 24f), youtubeLogo);
        }

        CreateText("YouTube", root, new Vector2(66f, 0f), new Vector2(240f, 40f), 30, FontStyle.Bold, new Color(0.15f, 0.15f, 0.15f, 1f), TextAnchor.MiddleLeft);
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            int column = i % 3;
            int row = i / 3;
            float x = 32f + column * 306f;
            float y = -64f - row * 212f;
            Rect hitRect = new Rect(x, -y + TitleBarHeight, 278f, 196f);

            Button button = CreateButton("Video " + (i + 1), root, new Vector2(x, y), new Vector2(278f, 196f), string.Empty, Color.white, new Color(1f, 0.96f, 0.78f, 1f), new Color(0.98f, 0.92f, 0.62f, 1f));
            button.onClick.AddListener(() => PlayVideo(index));
            catalogHitTargets.Add(new DesktopHitTarget(hitRect, () => PlayVideo(index)));

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            CreateThumbnail("Thumbnail", buttonRect, GetThumbnail(i), new Vector2(10f, -10f), new Vector2(258f, 118f));
            CreateText(">", buttonRect, new Vector2(111f, -43f), new Vector2(56f, 44f), 32, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            CreatePanel("Meta Background", buttonRect, new Color(1f, 0.985f, 0.92f, 1f), new Vector2(10f, -132f), new Vector2(258f, 56f));
            CreateText(GetTitle(i), buttonRect, new Vector2(16f, -136f), new Vector2(246f, 40f), 14, FontStyle.Bold, Color.black, TextAnchor.UpperLeft);
            CreateText("Janusz the Jew", buttonRect, new Vector2(16f, -166f), new Vector2(120f, 20f), 14, FontStyle.Bold, new Color(0.2f, 0.2f, 0.2f, 1f), TextAnchor.MiddleLeft);
        }
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
        playerHitTargets.Add(new DesktopHitTarget(new Rect(18f, 16f + TitleBarHeight, 116f, 36f), ShowCatalog));

        playerTitle = CreateText("Player Title", root, new Vector2(150f, -18f), new Vector2(600f, 34f), 18, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);

        GameObject bottomBar = CreatePanel("Bottom Bar", root, new Color(0f, 0f, 0f, 0.82f), new Vector2(0f, 0f), new Vector2(desktopSize.x, 76f));
        RectTransform bottomRect = bottomBar.GetComponent<RectTransform>();
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.anchoredPosition = Vector2.zero;

        // transport row mirrors the music app: prev / play-pause / next / stop
        AddPlayerControl("Video Prev", root, new Vector2(28f, 18f), new Vector2(70f, 40f), "<<", PrevVideo);
        Button playPauseButton = AddPlayerControl("Play Pause Button", root, new Vector2(104f, 18f), new Vector2(90f, 40f), "II", TogglePlayback);
        playPauseLabel = playPauseButton.GetComponentInChildren<TextMeshProUGUI>();
        AddPlayerControl("Video Next", root, new Vector2(200f, 18f), new Vector2(70f, 40f), ">>", NextVideo);
        AddPlayerControl("Video Stop", root, new Vector2(276f, 18f), new Vector2(70f, 40f), "[]", StopVideo);

        // draggable scrub bar across the top of the bottom bar
        videoSeekWidth = desktopSize.x - 220f;
        const float videoSeekFromBottom = 72f;
        GameObject videoTrack = CreatePanel("Video Seek Track", root, new Color(0.4f, 0.4f, 0.4f, 0.9f), Vector2.zero, new Vector2(videoSeekWidth, 6f));
        RectTransform videoTrackRect = videoTrack.GetComponent<RectTransform>();
        videoTrackRect.anchorMin = new Vector2(0f, 0f);
        videoTrackRect.anchorMax = new Vector2(0f, 0f);
        videoTrackRect.pivot = new Vector2(0f, 0f);
        videoTrackRect.anchoredPosition = new Vector2(28f, videoSeekFromBottom);
        float videoSeekCursorTop = desktopSize.y - (videoSeekFromBottom + 6f);
        DecorateSeekBar(videoTrack, new Color(0.92f, 0.06f, 0.05f, 1f),
            new Rect(28f, videoSeekCursorTop - 9f, videoSeekWidth, 24f), out videoSeekFill, out videoSeekHandle, playerScrubTargets, SeekVideo);

        videoTimeLabel = CreateText("Video Time", root, Vector2.zero, new Vector2(180f, 22f), 13, FontStyle.Normal, new Color(0.92f, 0.92f, 0.92f, 1f), TextAnchor.MiddleLeft);
        RectTransform videoTimeRect = videoTimeLabel.GetComponent<RectTransform>();
        videoTimeRect.anchorMin = new Vector2(0f, 0f);
        videoTimeRect.anchorMax = new Vector2(0f, 0f);
        videoTimeRect.pivot = new Vector2(0f, 0f);
        videoTimeRect.anchoredPosition = new Vector2(28f + videoSeekWidth + 12f, videoSeekFromBottom - 8f);
        videoTimeLabel.text = "0:00 / 0:00";
    }

    private Button AddPlayerControl(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, string label, Action onClick)
    {
        Button button = CreateButton(name, parent, anchoredPosition, size, label);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = anchoredPosition;
        button.onClick.AddListener(() => onClick());

        float cursorTop = desktopSize.y - (anchoredPosition.y + size.y);
        playerHitTargets.Add(new DesktopHitTarget(new Rect(anchoredPosition.x, cursorTop, size.x, size.y), onClick));
        return button;
    }

    private int VideoCount => localVideos != null && localVideos.Length > 0 ? localVideos.Length : 6;

    private void PrevVideo()
    {
        if (currentVideoIndex < 0)
        {
            PlayVideo(0);
            return;
        }

        int count = VideoCount;
        PlayVideo(currentVideoIndex <= 0 ? count - 1 : currentVideoIndex - 1);
    }

    private void NextVideo()
    {
        int count = VideoCount;
        PlayVideo(currentVideoIndex < 0 ? 0 : (currentVideoIndex + 1) % count);
    }

    private void StopVideo()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Pause();
            videoPlayer.time = 0d;
        }

        if (playPauseLabel != null)
        {
            playPauseLabel.text = ">";
        }

        UpdateVideoTimeline();
    }

    // ------------------------------------------------------------------
    // terminal app
    // ------------------------------------------------------------------

    private void BuildTerminal()
    {
        RectTransform root = terminalWindow.GetComponent<RectTransform>();

        GameObject textObject = new GameObject("Terminal Text");
        textObject.transform.SetParent(root, false);
        terminalText = textObject.AddComponent<TextMeshProUGUI>();
        terminalText.fontSize = 16f;
        terminalText.color = new Color(0.78f, 0.91f, 0.78f, 1f);
        terminalText.alignment = TextAlignmentOptions.TopLeft;
        terminalText.enableWordWrapping = true;
        terminalText.overflowMode = TextOverflowModes.Truncate;
        terminalText.richText = false;
        RectTransform textRect = terminalText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, TaskbarHeight + 4f);
        textRect.offsetMax = new Vector2(-14f, -(TitleBarHeight + 8f));
        SetLayerRecursive(textObject, UiLayer);

        terminalLines.Add("AntonOS 1.0 LTS tty1");
        terminalLines.Add(string.Empty);
        terminalLines.Add("Последний вход: давно. Слишком давно.");
        terminalLines.Add("Наберите 'help' для списка команд.");
        RefreshTerminalText();
    }

    private void HandleTerminalTyping()
    {
        foreach (char typed in Input.inputString)
        {
            if (typed == '\b')
            {
                if (terminalInput.Length > 0)
                {
                    terminalInput = terminalInput.Substring(0, terminalInput.Length - 1);
                }
            }
            else if (typed == '\n' || typed == '\r')
            {
                SubmitTerminalCommand();
            }
            else if (!char.IsControl(typed))
            {
                if (terminalInput.Length < 60)
                {
                    terminalInput += typed;
                }
            }
        }

        RefreshTerminalText();
    }

    private void SubmitTerminalCommand()
    {
        string raw = terminalInput.Trim();
        terminalInput = string.Empty;
        AppendLine(TerminalPrompt + raw);

        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        string[] parts = raw.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();
        string argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (command)
        {
            case "help":
                AppendLine("help, ls, cd <каталог>, cat <файл>, ffplay <трек>, pwd, whoami, neofetch, clear, youtube, music, exit");
                break;
            case "ls":
                if (terminalCwd == "~/" + MusicDir)
                {
                    AppendLine(string.Join("  ", GetTrackFileNames()));
                }
                else
                {
                    AppendLine(MusicDir + "/  " + string.Join("  ", TerminalFiles.Keys));
                }

                break;
            case "cd":
                HandleCdCommand(argument);
                break;
            case "cat":
                if (terminalCwd == "~/" + MusicDir)
                {
                    AppendLine("cat: это бинарь. для музыки есть ffplay.");
                }
                else if (TerminalFiles.TryGetValue(argument, out string content))
                {
                    foreach (string line in content.Split('\n'))
                    {
                        AppendLine(line);
                    }
                }
                else
                {
                    AppendLine($"cat: {argument}: Нет такого файла");
                }

                break;
            case "ffplay":
                HandleFfplayCommand(argument);
                break;
            case "music":
                OpenMusic();
                AppendLine("открываю AntonMusic...");
                break;
            case "pwd":
                AppendLine(terminalCwd == "~/" + MusicDir ? "/home/anton/" + MusicDir : "/home/anton");
                break;
            case "whoami":
                AppendLine("anton... а ты кто?");
                break;
            case "neofetch":
                AppendLine("        OS: AntonOS 1.0 LTS x86_64");
                AppendLine("  /\\__/\\  Host: гараж, дальний угол");
                AppendLine(" ( o.o )  Uptime: 666 дней");
                AppendLine("  > ^ <   Memory: помнит всё");
                break;
            case "clear":
                terminalLines.Clear();
                break;
            case "youtube":
                OpenYoutube();
                break;
            case "sudo":
                AppendLine("anton не входит в файл sudoers. Об этом будет доложено.");
                AppendLine("...он уже знает.");
                break;
            case "exit":
                CloseTerminal();
                break;
            default:
                AppendLine($"{command}: команда не найдена");
                break;
        }
    }

    private void HandleCdCommand(string argument)
    {
        string target = argument.Trim().TrimEnd('/');

        if (string.IsNullOrEmpty(target) || target == "~" || target == ".." || target == "/home/anton")
        {
            terminalCwd = "~";
            return;
        }

        if (target == MusicDir || target == "~/" + MusicDir)
        {
            terminalCwd = "~/" + MusicDir;
            return;
        }

        AppendLine($"cd: {argument}: Нет такого каталога");
    }

    private void HandleFfplayCommand(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            AppendLine("Usage: ffplay <трек>");
            return;
        }

        if (TryResolveTrack(argument, out int index))
        {
            PlayTrack(index);
            AppendLine("ffplay version 4.antonchik  Copyright (c) гараж");
            AppendLine("Сейчас играет: " + GetTrackTitle(index));
            AppendLine("(управление — в приложении Music; 'music' чтобы открыть)");
        }
        else
        {
            AppendLine($"ffplay: {argument}: No such file or directory");
            if (musicTracks != null && musicTracks.Length > 0)
            {
                AppendLine("доступные треки (cd music; ls):");
                foreach (string fileName in GetTrackFileNames())
                {
                    AppendLine("  " + fileName);
                }
            }
        }
    }

    private bool TryResolveTrack(string argument, out int index)
    {
        index = -1;
        if (musicTracks == null)
        {
            return false;
        }

        string needle = argument.Trim();
        if (needle.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            needle = needle.Substring(0, needle.Length - 4);
        }

        if (needle.StartsWith(MusicDir + "/", StringComparison.OrdinalIgnoreCase))
        {
            needle = needle.Substring(MusicDir.Length + 1);
        }

        // exact name first, then a forgiving substring match
        for (int i = 0; i < musicTracks.Length; i++)
        {
            if (musicTracks[i] != null && string.Equals(musicTracks[i].name, needle, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        for (int i = 0; i < musicTracks.Length; i++)
        {
            if (musicTracks[i] != null && musicTracks[i].name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private List<string> GetTrackFileNames()
    {
        var names = new List<string>();
        int count = musicTracks != null ? musicTracks.Length : 0;
        for (int i = 0; i < count; i++)
        {
            names.Add(GetTrackFileName(i));
        }

        return names;
    }

    private void AppendLine(string line)
    {
        terminalLines.Add(line);
        while (terminalLines.Count > 200)
        {
            terminalLines.RemoveAt(0);
        }
    }

    private void RefreshTerminalText()
    {
        if (terminalText == null)
        {
            return;
        }

        int start = Mathf.Max(0, terminalLines.Count - TerminalMaxLines);
        var builder = new System.Text.StringBuilder();
        for (int i = start; i < terminalLines.Count; i++)
        {
            builder.AppendLine(terminalLines[i]);
        }

        bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
        builder.Append(TerminalPrompt).Append(terminalInput).Append(blink ? "_" : " ");
        terminalText.text = builder.ToString();
    }

    private string TerminalPrompt => "anton@antonchik-pc:" + terminalCwd + "$ ";

    // ------------------------------------------------------------------
    // music app (playlist + transport), audio plays via musicAudioSource so
    // it can keep going with the window closed or be driven from the terminal
    // ------------------------------------------------------------------

    private void ConfigureMusicSource()
    {
        if (musicAudioSource == null)
        {
            musicAudioSource = gameObject.AddComponent<AudioSource>();
        }

        musicAudioSource.playOnAwake = false;
        musicAudioSource.loop = false;
        musicAudioSource.spatialBlend = 0f;
        musicAudioSource.Stop();
    }

    private void BuildMusic()
    {
        RectTransform root = musicWindow.GetComponent<RectTransform>();

        CreateText("Music Header", root, new Vector2(20f, -(TitleBarHeight + 8f)), new Vector2(600f, 30f),
            22, FontStyle.Bold, new Color(0.85f, 0.78f, 0.98f, 1f), TextAnchor.MiddleLeft).text = "AntonMusic";
        CreateText("Music Sub", root, new Vector2(20f, -(TitleBarHeight + 38f)), new Vector2(700f, 22f),
            13, FontStyle.Normal, new Color(0.6f, 0.6f, 0.66f, 1f), TextAnchor.MiddleLeft).text = "локальная фонотека";

        int count = musicTracks != null ? musicTracks.Length : 0;
        int columns = count > 7 ? 2 : 1;
        int rowsPerColumn = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
        const float listTop = TitleBarHeight + 72f;
        const float rowHeight = 38f;
        float columnWidth = (desktopSize.x - 40f - (columns - 1) * 16f) / columns;

        for (int i = 0; i < count; i++)
        {
            int index = i;
            int column = i / rowsPerColumn;
            int rowInColumn = i % rowsPerColumn;
            float x = 20f + column * (columnWidth + 16f);
            float top = listTop + rowInColumn * rowHeight;

            Button button = CreateButton("Track " + (i + 1), root, new Vector2(x, -top), new Vector2(columnWidth, rowHeight - 6f),
                string.Empty, new Color(0.16f, 0.14f, 0.2f, 1f), new Color(0.26f, 0.22f, 0.34f, 1f), new Color(0.4f, 0.28f, 0.56f, 1f));
            button.onClick.AddListener(() => PlayTrack(index));
            musicHitTargets.Add(new DesktopHitTarget(new Rect(x, top, columnWidth, rowHeight - 6f), () => PlayTrack(index)));

            RectTransform buttonRect = button.GetComponent<RectTransform>();
            CreateText("Num", buttonRect, new Vector2(10f, 0f), new Vector2(28f, rowHeight - 6f), 14, FontStyle.Bold,
                new Color(0.62f, 0.5f, 0.85f, 1f), TextAnchor.MiddleLeft).text = (i + 1).ToString("00");
            CreateText("Track Title", buttonRect, new Vector2(44f, 0f), new Vector2(columnWidth - 54f, rowHeight - 6f),
                14, FontStyle.Normal, new Color(0.92f, 0.92f, 0.96f, 1f), TextAnchor.MiddleLeft).text = GetTrackTitle(i);
        }

        // transport sits in the band JUST ABOVE the AntonOS taskbar so the
        // controls are never hidden behind it (panel bottom == taskbar top)
        const float barHeight = 100f;
        float taskbarTop = desktopSize.y - TaskbarHeight;
        float barTop = taskbarTop - barHeight;
        CreatePanel("Music Transport", root, new Color(0.06f, 0.05f, 0.09f, 1f), new Vector2(0f, -barTop), new Vector2(desktopSize.x, barHeight));

        musicNowPlaying = CreateText("Music Now Playing", root, new Vector2(20f, -(barTop + 8f)), new Vector2(desktopSize.x - 40f, 22f),
            14, FontStyle.Bold, new Color(0.82f, 0.78f, 0.92f, 1f), TextAnchor.MiddleLeft);

        // controls go ABOVE the timeline
        float btnTop = barTop + 38f;
        AddMusicControl("Music Prev", root, new Vector2(20f, -btnTop), new Vector2(70f, 30f), "<<", PrevTrack);
        Button playPauseButton = AddMusicControl("Music PlayPause", root, new Vector2(96f, -btnTop), new Vector2(90f, 30f), ">", ToggleMusicPlayback);
        musicPlayPauseLabel = playPauseButton.GetComponentInChildren<TextMeshProUGUI>();
        AddMusicControl("Music Next", root, new Vector2(192f, -btnTop), new Vector2(70f, 30f), ">>", NextTrack);
        AddMusicControl("Music Stop", root, new Vector2(268f, -btnTop), new Vector2(70f, 30f), "[]", StopMusic);

        // draggable seek bar BELOW the controls (still clear of the taskbar)
        musicSeekWidth = desktopSize.x - 200f;
        float seekTop = barTop + 82f;
        GameObject musicTrack = CreatePanel("Music Seek Track", root, new Color(0.25f, 0.24f, 0.3f, 1f), new Vector2(20f, -seekTop), new Vector2(musicSeekWidth, 6f));
        DecorateSeekBar(musicTrack, new Color(0.62f, 0.4f, 0.95f, 1f),
            new Rect(20f, seekTop - 9f, musicSeekWidth, 22f), out musicSeekFill, out musicSeekHandle, musicScrubTargets, SeekMusic);

        musicTimeLabel = CreateText("Music Time", root, new Vector2(20f + musicSeekWidth + 12f, -(seekTop - 7f)), new Vector2(168f, 20f),
            12, FontStyle.Normal, new Color(0.8f, 0.78f, 0.86f, 1f), TextAnchor.MiddleLeft);
        musicTimeLabel.text = "0:00 / 0:00";

        UpdateMusicNowPlaying();
        UpdateMusicTimeline();
    }

    private Button AddMusicControl(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, string label, Action onClick)
    {
        Button button = CreateButton(name, parent, anchoredPosition, size, label,
            new Color(0.42f, 0.34f, 0.6f, 1f), new Color(0.56f, 0.46f, 0.78f, 1f), new Color(0.68f, 0.55f, 0.9f, 1f));
        button.onClick.AddListener(() => onClick());
        musicHitTargets.Add(new DesktopHitTarget(new Rect(anchoredPosition.x, -anchoredPosition.y, size.x, size.y), onClick));
        return button;
    }

    private void PlayTrack(int index)
    {
        if (musicTracks == null || index < 0 || index >= musicTracks.Length || musicTracks[index] == null)
        {
            return;
        }

        if (musicAudioSource == null)
        {
            ConfigureMusicSource();
        }

        // don't let the video and the music app talk over each other
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }

        currentTrackIndex = index;
        musicPaused = false;
        musicWasPlaying = false;
        musicAudioSource.clip = musicTracks[index];
        musicAudioSource.loop = false;
        musicAudioSource.Play();

        if (musicPlayPauseLabel != null)
        {
            musicPlayPauseLabel.text = "II";
        }

        UpdateMusicNowPlaying();
    }

    private void ToggleMusicPlayback()
    {
        if (musicAudioSource == null || currentTrackIndex < 0)
        {
            if (musicTracks != null && musicTracks.Length > 0)
            {
                PlayTrack(0);
            }

            return;
        }

        if (musicAudioSource.isPlaying)
        {
            musicAudioSource.Pause();
            musicPaused = true;
            if (musicPlayPauseLabel != null)
            {
                musicPlayPauseLabel.text = ">";
            }
        }
        else
        {
            musicAudioSource.UnPause();
            musicPaused = false;
            if (musicPlayPauseLabel != null)
            {
                musicPlayPauseLabel.text = "II";
            }
        }
    }

    private void StopMusic()
    {
        if (musicAudioSource != null)
        {
            musicAudioSource.Stop();
            musicAudioSource.clip = null;
        }

        currentTrackIndex = -1;
        musicPaused = false;
        musicWasPlaying = false;
        if (musicPlayPauseLabel != null)
        {
            musicPlayPauseLabel.text = ">";
        }

        UpdateMusicNowPlaying();
    }

    private void NextTrack()
    {
        int count = musicTracks != null ? musicTracks.Length : 0;
        if (count == 0)
        {
            return;
        }

        int next = currentTrackIndex < 0 ? 0 : (currentTrackIndex + 1) % count;
        PlayTrack(next);
    }

    private void PrevTrack()
    {
        int count = musicTracks != null ? musicTracks.Length : 0;
        if (count == 0)
        {
            return;
        }

        int prev = currentTrackIndex <= 0 ? count - 1 : currentTrackIndex - 1;
        PlayTrack(prev);
    }

    private void UpdateMusicNowPlaying()
    {
        if (musicNowPlaying == null)
        {
            return;
        }

        musicNowPlaying.text = currentTrackIndex >= 0
            ? GetTrackTitle(currentTrackIndex)
            : "— ничего не играет —";
    }

    // Auto-advance to the next track when the current one finishes. We only
    // treat "stopped" as "ended" once we've actually observed it playing, so
    // the frame right after Play() (before audio starts) doesn't skip a track.
    private void UpdateMusicPlayback()
    {
        if (musicAudioSource == null || currentTrackIndex < 0 || musicPaused)
        {
            return;
        }

        // When the game is paused (Esc menu) the world audio — including this
        // source — is paused externally. Don't mistake that for the track
        // ending, or we'd skip to the next song and play it over the menu.
        if (Time.timeScale == 0f)
        {
            return;
        }

        if (musicAudioSource.isPlaying)
        {
            musicWasPlaying = true;
        }
        else if (musicWasPlaying)
        {
            musicWasPlaying = false;
            NextTrack();
        }
    }

    // ------------------------------------------------------------------
    // draggable timelines (shared by the music player and the video player)
    // ------------------------------------------------------------------

    private void DecorateSeekBar(GameObject track, Color accent, Rect cursorRect, out RectTransform fill, out RectTransform handle, List<DesktopScrubTarget> scrubList, Action<float> onScrub)
    {
        GameObject fillObject = CreatePanel(track.name + " Fill", track.transform, accent);
        fill = fillObject.GetComponent<RectTransform>();
        fill.anchorMin = new Vector2(0f, 0.5f);
        fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = new Vector2(0f, 6f);

        GameObject handleObject = CreatePanel(track.name + " Handle", track.transform, Color.white);
        handle = handleObject.GetComponent<RectTransform>();
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.anchoredPosition = Vector2.zero;
        handle.sizeDelta = new Vector2(12f, 16f);

        scrubList.Add(new DesktopScrubTarget(cursorRect, onScrub));
    }

    private bool TryBeginScrub()
    {
        List<DesktopScrubTarget> list = null;
        if (musicWindow != null && musicWindow.activeSelf)
        {
            list = musicScrubTargets;
        }
        else if (youtubeWindow != null && youtubeWindow.activeSelf && playerRoot != null && playerRoot.activeSelf)
        {
            list = playerScrubTargets;
        }

        if (list == null)
        {
            return false;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Contains(cursorPosition))
            {
                activeScrubTarget = list[i];
                PlayClickSound();
                list[i].Scrub(cursorPosition);
                return true;
            }
        }

        return false;
    }

    private void SeekMusic(float fraction)
    {
        if (musicAudioSource == null || musicAudioSource.clip == null)
        {
            return;
        }

        float length = musicAudioSource.clip.length;
        musicAudioSource.time = Mathf.Clamp(fraction * length, 0f, Mathf.Max(0f, length - 0.05f));
        UpdateMusicTimeline();
    }

    private void SeekVideo(float fraction)
    {
        if (videoPlayer == null || currentVideoIndex < 0)
        {
            return;
        }

        double length = videoPlayer.length;
        if (length <= 0d)
        {
            return;
        }

        videoPlayer.time = Mathf.Clamp01(fraction) * length;
        UpdateVideoTimeline();
    }

    private void UpdateTimelines()
    {
        UpdateMusicTimeline();
        UpdateVideoTimeline();
    }

    private void UpdateMusicTimeline()
    {
        if (musicSeekFill == null)
        {
            return;
        }

        float length = musicAudioSource != null && musicAudioSource.clip != null ? musicAudioSource.clip.length : 0f;
        float time = musicAudioSource != null && length > 0f ? Mathf.Min(musicAudioSource.time, length) : 0f;
        float fraction = length > 0f ? Mathf.Clamp01(time / length) : 0f;
        SetSeekVisual(musicSeekFill, musicSeekHandle, musicSeekWidth, fraction);

        if (musicTimeLabel != null)
        {
            musicTimeLabel.text = FormatTime(time) + " / " + FormatTime(length);
        }
    }

    private void UpdateVideoTimeline()
    {
        if (videoSeekFill == null)
        {
            return;
        }

        double length = videoPlayer != null ? videoPlayer.length : 0d;
        double time = videoPlayer != null && currentVideoIndex >= 0 ? videoPlayer.time : 0d;
        float fraction = length > 0d ? Mathf.Clamp01((float)(time / length)) : 0f;
        SetSeekVisual(videoSeekFill, videoSeekHandle, videoSeekWidth, fraction);

        if (videoTimeLabel != null)
        {
            videoTimeLabel.text = FormatTime((float)time) + " / " + FormatTime((float)length);
        }
    }

    private void SetSeekVisual(RectTransform fill, RectTransform handle, float trackWidth, float fraction)
    {
        float clamped = Mathf.Clamp01(fraction);
        if (fill != null)
        {
            fill.sizeDelta = new Vector2(clamped * trackWidth, fill.sizeDelta.y);
        }

        if (handle != null)
        {
            handle.anchoredPosition = new Vector2(clamped * trackWidth, 0f);
        }
    }

    private static string FormatTime(float seconds)
    {
        if (seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
        {
            seconds = 0f;
        }

        int total = Mathf.FloorToInt(seconds);
        return (total / 60) + ":" + (total % 60).ToString("00");
    }

    private string GetTrackTitle(int index)
    {
        if (musicTitles != null && index >= 0 && index < musicTitles.Length && !string.IsNullOrWhiteSpace(musicTitles[index]))
        {
            return musicTitles[index];
        }

        if (musicTracks != null && index >= 0 && index < musicTracks.Length && musicTracks[index] != null)
        {
            return musicTracks[index].name;
        }

        return "Track " + (index + 1);
    }

    private string GetTrackFileName(int index)
    {
        string title = (musicTracks != null && index >= 0 && index < musicTracks.Length && musicTracks[index] != null)
            ? musicTracks[index].name
            : "track" + (index + 1);
        return title + ".mp3";
    }

    // ------------------------------------------------------------------
    // window switching
    // ------------------------------------------------------------------

    private void ShowDesktop()
    {
        if (youtubeWindow != null)
        {
            youtubeWindow.SetActive(false);
        }

        if (terminalWindow != null)
        {
            terminalWindow.SetActive(false);
        }

        if (musicWindow != null)
        {
            musicWindow.SetActive(false);
        }
    }

    private void OpenYoutube()
    {
        PlayClickSound();
        if (terminalWindow != null)
        {
            terminalWindow.SetActive(false);
        }

        if (musicWindow != null)
        {
            musicWindow.SetActive(false);
        }

        youtubeWindow.SetActive(true);
        ShowCatalog();
    }

    private void CloseYoutube()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        currentVideoIndex = -1;
        youtubeWindow.SetActive(false);
    }

    private void OpenTerminal()
    {
        PlayClickSound();
        if (youtubeWindow != null)
        {
            CloseYoutube();
        }

        if (musicWindow != null)
        {
            musicWindow.SetActive(false);
        }

        terminalWindow.SetActive(true);
        RefreshTerminalText();
    }

    private void CloseTerminal()
    {
        terminalWindow.SetActive(false);
    }

    private void OpenMusic()
    {
        PlayClickSound();
        if (youtubeWindow != null)
        {
            CloseYoutube();
        }

        if (terminalWindow != null)
        {
            terminalWindow.SetActive(false);
        }

        if (musicWindow != null)
        {
            musicWindow.SetActive(true);
        }

        UpdateMusicNowPlaying();
    }

    private void CloseMusic()
    {
        // hide only — leave the track playing so it keeps going in the room
        if (musicWindow != null)
        {
            musicWindow.SetActive(false);
        }
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

        // the music app and the video share the room's speakers
        if (musicAudioSource != null && musicAudioSource.isPlaying)
        {
            musicAudioSource.Pause();
            musicPaused = true;
            if (musicPlayPauseLabel != null)
            {
                musicPlayPauseLabel.text = ">";
            }
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
            playPauseLabel.text = "II";
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
                playPauseLabel.text = ">";
            }
        }
        else
        {
            videoPlayer.Play();

            if (playPauseLabel != null)
            {
                playPauseLabel.text = "II";
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

    // ------------------------------------------------------------------
    // screen material (flip + emissive fixes)
    // ------------------------------------------------------------------

    private void ReapplyScreenMaterial()
    {
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

        // The screen mesh UVs are mirrored, so both pipelines need the flip.
        // The Built-in fallback looked flipped because the pipeline material
        // conversion drops the texture scale/offset — the periodic re-apply
        // restores it on the converted material.
        Vector2 scale = new Vector2(-1f, 1f);
        Vector2 offset = new Vector2(1f, 0f);
        screenMaterial.mainTextureScale = scale;
        screenMaterial.mainTextureOffset = offset;

        // The screen must read as a self-lit panel: the desktop image comes
        // purely from emission, while the surface itself absorbs the room
        // (black albedo, no metallic, zero smoothness) so it neither darkens
        // in shadow nor reflects/glows with the environment light.
        if (screenMaterial.HasProperty("_BaseColor"))
        {
            screenMaterial.SetColor("_BaseColor", Color.black);
        }

        if (screenMaterial.HasProperty("_Color"))
        {
            screenMaterial.SetColor("_Color", Color.black);
        }

        if (screenMaterial.HasProperty("_Metallic"))
        {
            screenMaterial.SetFloat("_Metallic", 0f);
        }

        if (screenMaterial.HasProperty("_Smoothness"))
        {
            screenMaterial.SetFloat("_Smoothness", 0f);
        }

        if (screenMaterial.HasProperty("_Glossiness"))
        {
            screenMaterial.SetFloat("_Glossiness", 0f);
        }

        if (screenMaterial.HasProperty("_EmissiveColorMap"))
        {
            screenMaterial.SetTexture("_EmissiveColorMap", desktopTexture);
            screenMaterial.SetColor("_EmissiveColor", Color.white * ScreenEmissionStrength);
            // emission ignores camera exposure so it stays constant and bright
            // regardless of how dark the room (and its auto-exposure) gets
            if (screenMaterial.HasProperty("_EmissiveExposureWeight"))
            {
                screenMaterial.SetFloat("_EmissiveExposureWeight", 0f);
            }

            if (screenMaterial.HasProperty("_UseEmissiveIntensity"))
            {
                screenMaterial.SetFloat("_UseEmissiveIntensity", 0f);
            }

            screenMaterial.SetTextureScale("_EmissiveColorMap", scale);
            screenMaterial.SetTextureOffset("_EmissiveColorMap", offset);
            screenMaterial.EnableKeyword("_EMISSIVE_COLOR_MAP");
        }
        else if (screenMaterial.HasProperty("_EmissionMap"))
        {
            screenMaterial.EnableKeyword("_EMISSION");
            screenMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            screenMaterial.SetTexture("_EmissionMap", desktopTexture);
            screenMaterial.SetColor("_EmissionColor", Color.white);
            screenMaterial.SetTextureScale("_EmissionMap", scale);
            screenMaterial.SetTextureOffset("_EmissionMap", offset);
        }
    }

    // ------------------------------------------------------------------
    // input plumbing
    // ------------------------------------------------------------------

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
            TerminalCaptureActive = false;
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

        List<DesktopHitTarget> targets;
        if (terminalWindow != null && terminalWindow.activeSelf)
        {
            targets = terminalHitTargets;
        }
        else if (musicWindow != null && musicWindow.activeSelf)
        {
            targets = musicHitTargets;
        }
        else if (youtubeWindow != null && youtubeWindow.activeSelf)
        {
            targets = catalogRoot != null && catalogRoot.activeSelf ? catalogHitTargets : playerHitTargets;
        }
        else
        {
            targets = iconHitTargets;
        }

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

    // ------------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------------

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

        switch (alignment)
        {
            case TextAnchor.UpperLeft:
                text.alignment = TextAlignmentOptions.TopLeft;
                break;
            case TextAnchor.MiddleLeft:
            case TextAnchor.LowerLeft:
                text.alignment = TextAlignmentOptions.Left;
                break;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                text.alignment = TextAlignmentOptions.Right;
                break;
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

    // A draggable timeline region: while the mouse is held inside it, the
    // horizontal cursor position maps to a 0..1 fraction passed to the seek
    // callback — same feel as the YouTube scrubber.
    private sealed class DesktopScrubTarget
    {
        private readonly Rect rect;
        private readonly Action<float> scrub;

        public DesktopScrubTarget(Rect rect, Action<float> scrub)
        {
            this.rect = rect;
            this.scrub = scrub;
        }

        public bool Contains(Vector2 position)
        {
            return position.x >= rect.xMin
                && position.x <= rect.xMax
                && position.y >= rect.yMin
                && position.y <= rect.yMax;
        }

        public void Scrub(Vector2 position)
        {
            float fraction = Mathf.Clamp01((position.x - rect.xMin) / Mathf.Max(1f, rect.width));
            scrub?.Invoke(fraction);
        }
    }
}
