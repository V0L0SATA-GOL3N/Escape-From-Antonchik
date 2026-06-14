using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.UI;

public class MenuController : MonoBehaviour
{
    [System.Serializable]
    private sealed class CutsceneTextEntry
    {
        public string text = "But nobody came.";
        public float lettersPerSecond = 18f;
    }

    private enum MenuMode
    {
        MainMenu,
        PauseMenu
    }

    [SerializeField] private MenuMode mode = MenuMode.PauseMenu;
    [SerializeField] private string gameplaySceneName = "gamePlay";
    [SerializeField] private string mainMenuSceneName = "menu";
    [SerializeField] private Texture2D buttonTexture;
    [SerializeField] private Texture2D focusedButtonTexture;
    [SerializeField] private Texture2D backgroundTexture;
    [SerializeField] private Texture2D pausedTitleTexture;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private List<CutsceneTextEntry> introTexts = new List<CutsceneTextEntry> { new CutsceneTextEntry() };
    [SerializeField] private AudioClip cutsceneTypingSound;
    [SerializeField] private AudioClip cutsceneFinishSound;
    [SerializeField] private string cutsceneContinueText = "Press any button to continue";
    [SerializeField] private GameObject cursor;

    private const string VolumePref = "settings.masterVolume";
    private const string SensitivityPref = "settings.mouseSensitivity";
    private const string FullscreenPref = "settings.fullscreen";
    private const float SensitivityMin = 0.5f;
    private const float SensitivityMax = 8f;

    private Canvas canvas;
    private AudioSource audioSource;
    private GameObject pauseRoot;
    private GameObject pausePanel;
    private GameObject settingsPanel;
    private GameObject loadingPanel;
    private GameObject introRoot;
    private TextMeshProUGUI introLabel;
    private TextMeshProUGUI introContinueLabel;
    private Image loadingFill;
    private GameObject mainRoot;
    private GameObject creditsPage;
    private Texture2D pauseBloodTexture;
    private Sprite pauseBloodSprite;
    private FirstPersonController playerController;
    private Sprite buttonSprite;
    private Sprite focusedButtonSprite;
    private Sprite backgroundSprite;
    private Sprite pausedTitleSprite;
    private bool isPaused;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private bool previousPlayerCanMove;
    private bool previousCameraCanMove;
    private GameObject activeMenuPage;
    private Button ultraLowQualityButton;
    private Button lowQualityButton;
    private Button highQualityButton;
    private Coroutine pageTransitionRoutine;
    private Coroutine pauseOpenRoutine;
    private bool introActive;
    private static bool playIntroOnNextGameplayLoad;
    // Statics survive scene loads but reset when the app launches, so this stays
    // false only until the main menu is built for the very first time this run.
    private static bool startupVideosPlayed;
    private bool startupVideoActive;
    private readonly List<AudioSource> pausedAudioSources = new List<AudioSource>();
    private readonly List<VideoPlayer> pausedVideoPlayers = new List<VideoPlayer>();

    private void Awake()
    {
        audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        ConfigureMenuAudioSource(audioSource);
        buttonSprite = CreateSprite(buttonTexture);
        focusedButtonSprite = CreateSprite(focusedButtonTexture);
        backgroundSprite = CreateSprite(backgroundTexture);
        pausedTitleSprite = CreateSprite(pausedTitleTexture);
        ApplySavedSettings();

        if (mode == MenuMode.MainMenu)
        {
            ConfigureMainMenu();
        }
        else
        {
            ConfigurePauseMenu();
        }
    }

    private void Start()
    {
        // SoftwareCursor registers itself in its own Awake, so show the main-menu
        // cursor here (after all Awakes) rather than from ConfigureMainMenu. While
        // the cold-start intro videos play, the playback coroutine owns the cursor.
        if (mode == MenuMode.MainMenu && !startupVideoActive)
        {
            ShowSoftwareCursor(true);
        }
    }

    private void Update()
    {
        if (introActive)
        {
            return;
        }

        if (AntonchikFenceEncounter.SequenceActive)
        {
            return;
        }

        if (mode == MenuMode.PauseMenu && Input.GetKeyDown(KeyCode.Escape))
        {
            SetPaused(!isPaused);
        }
    }

    private void ConfigureMainMenu()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;

        canvas = FindObjectOfType<Canvas>();
        mainRoot = FindSceneObject("main");
        creditsPage = FindSceneObject("creditsPage");
        ConfigureMainMenuLayout();
        settingsPanel = BuildSettingsPanel("Menu Settings Panel", false);
        loadingPanel = BuildLoadingPanel();

        BindButton("playBtn", LoadGameplay);
        BindButton("settings", ShowSettings);
        BindButton("credits", ShowCredits);
        BindButton("back", ShowMainMenu);
        BindButton("exitBtn", QuitGame);

        activeMenuPage = mainRoot;
        ShowMainMenu(false);

        TryPlayStartupVideos();
    }

    // Plays the studio/branding intro videos, but only the first time the main
    // menu is built this run. Returning to the menu from gameplay re-runs
    // ConfigureMainMenu, yet startupVideosPlayed is already set, so it no-ops.
    private void TryPlayStartupVideos()
    {
        if (startupVideosPlayed || mode != MenuMode.MainMenu || canvas == null)
        {
            return;
        }

        startupVideosPlayed = true;

        List<VideoClip> clips = LoadStartupClips();
        if (clips.Count == 0)
        {
            return;
        }

        startupVideoActive = true;
        StartCoroutine(PlayStartupVideos(clips));
    }

    private List<VideoClip> LoadStartupClips()
    {
        // Order = playback order. Clips live under Assets/Resources/startups so
        // they ship in the build and load by name without inspector wiring.
        List<VideoClip> clips = new List<VideoClip>();
        AddStartupClip(clips, "startups/startup");
        AddStartupClip(clips, "startups/dolby_startup");
        return clips;
    }

    private void AddStartupClip(List<VideoClip> clips, string resourcePath)
    {
        VideoClip clip = Resources.Load<VideoClip>(resourcePath);
        if (clip != null)
        {
            clips.Add(clip);
        }
    }

    private IEnumerator PlayStartupVideos(List<VideoClip> clips)
    {
        ShowSoftwareCursor(false);

        GameObject overlay = CreatePanel("Startup Video Overlay", canvas.transform, Color.black);
        Stretch(overlay.GetComponent<RectTransform>());
        overlay.transform.SetAsLastSibling();
        CanvasGroup group = overlay.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = true;

        GameObject surfaceObject = new GameObject("Startup Video Surface");
        surfaceObject.transform.SetParent(overlay.transform, false);
        RawImage surface = surfaceObject.AddComponent<RawImage>();
        Stretch(surface.rectTransform);
        surface.color = Color.black;

        int width = Mathf.Max(2, Screen.width);
        int height = Mathf.Max(2, Screen.height);
        RenderTexture renderTexture = new RenderTexture(width, height, 0);
        renderTexture.Create();

        GameObject playerObject = new GameObject("Startup Video Player");
        playerObject.transform.SetParent(overlay.transform, false);
        VideoPlayer videoPlayer = playerObject.AddComponent<VideoPlayer>();
        AudioSource videoAudio = playerObject.AddComponent<AudioSource>();
        ConfigureMenuAudioSource(videoAudio);

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = renderTexture;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        surface.texture = renderTexture;

        // Skip the frame on which the menu became active so the player's own
        // first frame isn't read as a "skip" keypress.
        yield return null;

        for (int i = 0; i < clips.Count; i++)
        {
            yield return PlaySingleStartupVideo(videoPlayer, videoAudio, surface, clips[i]);
        }

        surface.texture = null;
        if (videoPlayer.targetTexture == renderTexture)
        {
            videoPlayer.targetTexture = null;
        }

        renderTexture.Release();
        Destroy(renderTexture);
        Destroy(overlay);

        startupVideoActive = false;
        if (mode == MenuMode.MainMenu)
        {
            ShowSoftwareCursor(true);
        }
    }

    private IEnumerator PlaySingleStartupVideo(VideoPlayer videoPlayer, AudioSource videoAudio, RawImage surface, VideoClip clip)
    {
        if (clip == null)
        {
            yield break;
        }

        // Tint black until the first frame lands so we don't flash the previous
        // clip's last frame still sitting in the render texture.
        surface.color = Color.black;

        videoPlayer.clip = clip;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, videoAudio);

        bool reachedEnd = false;
        VideoPlayer.EventHandler endHandler = source => reachedEnd = true;
        videoPlayer.loopPointReached += endHandler;

        videoPlayer.Prepare();
        for (float elapsed = 0f; !videoPlayer.isPrepared && elapsed < 5f; elapsed += Time.unscaledDeltaTime)
        {
            if (StartupSkipRequested())
            {
                videoPlayer.loopPointReached -= endHandler;
                yield break;
            }

            yield return null;
        }

        videoPlayer.Play();
        while (!reachedEnd)
        {
            if (videoPlayer.frame > 0)
            {
                surface.color = Color.white;
            }

            if (StartupSkipRequested())
            {
                break;
            }

            yield return null;
        }

        videoPlayer.loopPointReached -= endHandler;
        videoPlayer.Stop();
    }

    private bool StartupSkipRequested()
    {
        return Input.anyKeyDown;
    }

    private void ConfigureMainMenuLayout()
    {
        if (canvas == null)
        {
            return;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            Stretch(canvasRect);
        }

        ConfigureRootRect(FindSceneObject("BG"));
        ConfigureRootRect(mainRoot);
        ConfigureRootRect(creditsPage);
        ConfigureLogoRect(FindSceneObject("logo"));
        ConfigureMenuButton("playBtn", 230f);
        ConfigureMenuButton("settings", 75f);
        ConfigureMenuButton("credits", -80f);
        ConfigureMenuButton("exitBtn", -330f);
        ConfigureMenuButton("back", -360f);
    }

    private void ConfigureRootRect(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        target.transform.localScale = Vector3.one;
        Stretch(rect);
    }

    private void ConfigureLogoRect(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        target.transform.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(68f, -34f);
        rect.sizeDelta = new Vector2(390f, 180f);
    }

    private void ConfigureMenuButton(string objectName, float y)
    {
        GameObject target = FindSceneObject(objectName);
        if (target == null)
        {
            return;
        }

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        target.transform.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(70f, y);
        rect.sizeDelta = new Vector2(520f, 92f);

        TextMeshProUGUI label = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSize = 56f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 28f;
            label.fontSizeMax = 64f;
            label.alignment = TextAlignmentOptions.Center;
            RectTransform labelRect = label.GetComponent<RectTransform>();
            if (labelRect != null)
            {
                Stretch(labelRect);
            }
        }
    }

    private void ConfigurePauseMenu()
    {
        playerController = FindObjectOfType<FirstPersonController>();
        AttachPauseAudioToPlayerCamera();
        canvas = BuildCanvas("Pause Menu Canvas");
        pauseRoot = CreatePanel("Pause Menu Root", canvas.transform, new Color(0f, 0f, 0f, 0.76f));
        Stretch(pauseRoot.GetComponent<RectTransform>());
        AddPauseBloodSides(pauseRoot.transform);

        CanvasGroup pauseGroup = pauseRoot.AddComponent<CanvasGroup>();
        pauseGroup.alpha = 0f;
        pauseGroup.blocksRaycasts = false;

        pausePanel = CreatePanel("Pause Menu Panel", pauseRoot.transform, Color.clear);
        Stretch(pausePanel.GetComponent<RectTransform>());

        CreatePauseTitle(pausePanel.transform);
        ConfigureRuntimeMenuButton(CreateButton("Resume Button", pausePanel.transform, "Resume", ResumeGame).gameObject, new Vector2(70f, 100f), new Vector2(520f, 92f), Color.white);
        ConfigureRuntimeMenuButton(CreateButton("Settings Button", pausePanel.transform, "Settings", ShowSettings).gameObject, new Vector2(70f, -55f), new Vector2(520f, 92f), Color.white);
        ConfigureRuntimeMenuButton(CreateButton("Main Menu Button", pausePanel.transform, "Main Menu", LoadMainMenu).gameObject, new Vector2(70f, -210f), new Vector2(520f, 92f), Color.white);
        ConfigureRuntimeMenuButton(CreateButton("Exit Button", pausePanel.transform, "Exit", QuitGame).gameObject, new Vector2(70f, -360f), new Vector2(520f, 92f), Color.white);

        settingsPanel = BuildSettingsPanel("Pause Settings Panel", true);
        pauseRoot.SetActive(false);

        if (playIntroOnNextGameplayLoad)
        {
            playIntroOnNextGameplayLoad = false;
            BuildIntroCutscene();
            StartCoroutine(PlayIntroCutscene());
        }
    }

    private void AttachPauseAudioToPlayerCamera()
    {
        Camera playerCamera = Camera.main;
        if (playerCamera == null && playerController != null)
        {
            playerCamera = playerController.GetComponentInChildren<Camera>(true);
        }

        if (playerCamera == null)
        {
            playerCamera = FindObjectOfType<Camera>();
        }

        if (playerCamera == null)
        {
            ConfigureMenuAudioSource(audioSource);
            return;
        }

        AudioSource cameraMenuSource = playerCamera.gameObject.AddComponent<AudioSource>();
        ConfigureMenuAudioSource(cameraMenuSource);
        audioSource = cameraMenuSource;
    }

    private void ConfigureMenuAudioSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.panStereo = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
    }

    private void AddPauseBloodSides(Transform parent)
    {
        pauseBloodTexture = CreatePauseBloodTexture();
        pauseBloodSprite = CreateSprite(pauseBloodTexture);

        GameObject overlay = CreatePanel("Pause Blood Corner Fade", parent, Color.white);
        Image image = overlay.GetComponent<Image>();
        image.sprite = pauseBloodSprite;
        image.preserveAspect = false;

        RectTransform rect = overlay.GetComponent<RectTransform>();
        Stretch(rect);
    }

    private Texture2D CreatePauseBloodTexture()
    {
        const int width = 384;
        const int height = 216;
        const int dotCount = 190;

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "PauseGeneratedBloodCorners";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Vector2[] dotPositions = new Vector2[dotCount];
        float[] dotSizes = new float[dotCount];
        float[] dotStrengths = new float[dotCount];
        System.Random random = new System.Random(27591);

        for (int i = 0; i < dotCount; i++)
        {
            float side = (float)random.NextDouble();
            float x;
            float y;

            if (side < 0.38f)
            {
                x = Mathf.Lerp(0.02f, 0.26f, (float)random.NextDouble());
                y = (float)random.NextDouble();
            }
            else if (side < 0.76f)
            {
                x = Mathf.Lerp(0.74f, 0.98f, (float)random.NextDouble());
                y = (float)random.NextDouble();
            }
            else
            {
                x = (float)random.NextDouble();
                y = Mathf.Lerp(0.0f, 0.22f, (float)random.NextDouble());
            }

            dotPositions[i] = new Vector2(x, y);
            dotSizes[i] = Mathf.Lerp(0.006f, 0.026f, (float)random.NextDouble());
            dotStrengths[i] = Mathf.Lerp(0.18f, 0.62f, (float)random.NextDouble());
        }

        Color32[] pixels = new Color32[width * height];
        Color blood = new Color(0.32f, 0.01f, 0.01f, 1f);

        for (int y = 0; y < height; y++)
        {
            float v = y / (height - 1f);
            for (int x = 0; x < width; x++)
            {
                float u = x / (width - 1f);
                float leftDistance = u / 0.34f;
                float rightDistance = (1f - u) / 0.34f;
                float bottomDistance = v / 0.32f;
                float topDistance = (1f - v) / 0.48f;

                float cornerAlpha = Mathf.Max(
                    Mathf.Clamp01(1f - Mathf.Sqrt(leftDistance * leftDistance + topDistance * topDistance)),
                    Mathf.Clamp01(1f - Mathf.Sqrt(rightDistance * rightDistance + topDistance * topDistance))
                );
                cornerAlpha = Mathf.Max(cornerAlpha, Mathf.Clamp01(1f - Mathf.Sqrt(leftDistance * leftDistance + bottomDistance * bottomDistance)));
                cornerAlpha = Mathf.Max(cornerAlpha, Mathf.Clamp01(1f - Mathf.Sqrt(rightDistance * rightDistance + bottomDistance * bottomDistance)));
                cornerAlpha = Mathf.Pow(cornerAlpha, 1.35f) * 0.78f;

                float sideFade = Mathf.Max(Mathf.Clamp01(1f - u / 0.2f), Mathf.Clamp01(1f - (1f - u) / 0.2f)) * 0.22f;
                float bottomFade = Mathf.Clamp01(1f - v / 0.18f) * 0.2f;
                float alpha = Mathf.Max(cornerAlpha, Mathf.Max(sideFade, bottomFade));

                for (int i = 0; i < dotPositions.Length; i++)
                {
                    Vector2 dot = dotPositions[i];
                    float dx = (u - dot.x) * 1.78f;
                    float dy = v - dot.y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float dotAlpha = Mathf.Clamp01(1f - distance / dotSizes[i]);
                    dotAlpha = dotAlpha * dotAlpha * dotStrengths[i];
                    alpha = Mathf.Max(alpha, dotAlpha);
                }

                Color color = blood;
                color.a = Mathf.Clamp01(alpha);
                pixels[y * width + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void CreatePauseTitle(Transform parent)
    {
        if (pausedTitleSprite != null)
        {
            GameObject titleObject = new GameObject("Paused Title");
            titleObject.transform.SetParent(parent, false);
            Image image = titleObject.AddComponent<Image>();
            image.sprite = pausedTitleSprite;
            image.preserveAspect = true;
            image.color = Color.white;

            RectTransform rect = titleObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -42f);
            rect.sizeDelta = new Vector2(520f, 132f);
            return;
        }

        CreatePageText("Paused", parent, new Vector2(70f, 265f), new Vector2(520f, 90f), 66, TextAlignmentOptions.Left);
    }

    private GameObject BuildSettingsPanel(string panelName, bool parentToPauseRoot)
    {
        Transform parent = parentToPauseRoot && pauseRoot != null ? pauseRoot.transform : canvas.transform;
        GameObject panel = CreatePanel(panelName, parent, Color.clear);
        Stretch(panel.GetComponent<RectTransform>());

        CreatePageText("Settings", panel.transform, new Vector2(70f, 265f), new Vector2(520f, 90f), 66, TextAlignmentOptions.Left);
        CreateSliderRow(panel.transform, "Volume", new Vector2(70f, 110f), VolumePref, 1f, 0f, 1f, value => AudioListener.volume = value);
        CreateSliderRow(panel.transform, "Mouse Sensitivity", new Vector2(70f, 5f), SensitivityPref, 2f, SensitivityMin, SensitivityMax, ApplyMouseSensitivity);
        CreateToggleRow(panel.transform, "Fullscreen", new Vector2(70f, -95f), FullscreenPref, Screen.fullScreen, value => Screen.fullScreen = value);
        CreateQualityRow(panel.transform, new Vector2(70f, -195f));

        Button backButton = CreateButton("Settings Back Button", panel.transform, "Back", BackFromSettings);
        ConfigureRuntimeMenuButton(backButton.gameObject, new Vector2(70f, -330f), new Vector2(520f, 92f), parentToPauseRoot ? Color.white : new Color(0.2f, 0.2f, 0.2f, 1f));

        panel.SetActive(false);
        return panel;
    }

    private GameObject BuildLoadingPanel()
    {
        if (mode != MenuMode.MainMenu || canvas == null)
        {
            return null;
        }

        GameObject panel = CreatePanel("Loading Screen", canvas.transform, new Color(0f, 0f, 0f, 0.96f));
        Stretch(panel.GetComponent<RectTransform>());
        CanvasGroup group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        CreatePageText("Loading", panel.transform, new Vector2(70f, 60f), new Vector2(520f, 90f), 66, TextAlignmentOptions.Left);
        GameObject track = CreatePanel("Loading Track", panel.transform, new Color(0.14f, 0.14f, 0.15f, 1f), new Vector2(70f, -70f), new Vector2(520f, 16f));
        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(0f, 0.5f);
        trackRect.pivot = new Vector2(0f, 0.5f);

        GameObject fill = CreatePanel("Loading Fill", track.transform, new Color(0.88f, 0.06f, 0.05f, 1f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        loadingFill = fill.GetComponent<Image>();

        panel.SetActive(false);
        return panel;
    }

    private void CreateSliderRow(Transform parent, string label, Vector2 position, string prefKey, float fallback, float minValue, float maxValue, System.Action<float> onChanged)
    {
        GameObject row = CreatePanel(label + " Row", parent, Color.clear, position, new Vector2(720f, 96f));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0.5f);
        rowRect.anchorMax = new Vector2(0f, 0.5f);
        rowRect.pivot = new Vector2(0f, 0.5f);
        CreateText(label, row.transform, 28, Color.white, TextAlignmentOptions.Left, new Vector2(300f, 64f));

        Slider slider = CreateSlider(label + " Slider", row.transform, minValue, maxValue, PlayerPrefs.GetFloat(prefKey, fallback));
        RectTransform sliderRect = slider.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(0f, 0.5f);
        sliderRect.pivot = new Vector2(0f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(330f, 0f);
        sliderRect.sizeDelta = new Vector2(300f, 32f);
        slider.onValueChanged.AddListener(value =>
        {
            PlayerPrefs.SetFloat(prefKey, value);
            onChanged(value);
        });
    }

    private void CreateQualityRow(Transform parent, Vector2 position)
    {
        GameObject row = CreatePanel("Graphics Row", parent, Color.clear, position, new Vector2(720f, 96f));
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0.5f);
        rowRect.anchorMax = new Vector2(0f, 0.5f);
        rowRect.pivot = new Vector2(0f, 0.5f);
        CreateText("Graphics", row.transform, 28, Color.white, TextAlignmentOptions.Left, new Vector2(300f, 64f));

        ultraLowQualityButton = CreateButton("Graphics Ultra Low Button", row.transform, "Ultra Low", () => SetGraphicsQuality(GraphicsQualityManager.UltraLowMode));
        ConfigureQualityButton(ultraLowQualityButton, new Vector2(330f, 0f), 168f);
        lowQualityButton = CreateButton("Graphics Low Button", row.transform, "Low", () => SetGraphicsQuality(GraphicsQualityManager.LowMode));
        ConfigureQualityButton(lowQualityButton, new Vector2(508f, 0f), 110f);
        highQualityButton = CreateButton("Graphics High Button", row.transform, "High", () => SetGraphicsQuality(GraphicsQualityManager.HighMode));
        ConfigureQualityButton(highQualityButton, new Vector2(628f, 0f), 110f);

        UpdateQualityButtons();
    }

    private void ConfigureQualityButton(Button button, Vector2 position, float width)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 56f);

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            RectTransform labelRect = label.GetComponent<RectTransform>();
            if (labelRect != null)
            {
                Stretch(labelRect);
            }
        }
    }

    private void SetGraphicsQuality(int mode)
    {
        PlayClick();
        GraphicsQualityManager.SetMode(mode);
        UpdateQualityButtons();
    }

    private void UpdateQualityButtons()
    {
        int mode = GraphicsQualityManager.Mode;
        ApplyQualityButtonState(ultraLowQualityButton, mode == GraphicsQualityManager.UltraLowMode);
        ApplyQualityButtonState(lowQualityButton, mode == GraphicsQualityManager.LowMode);
        ApplyQualityButtonState(highQualityButton, mode == GraphicsQualityManager.HighMode);
    }

    private void ApplyQualityButtonState(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = selected ? new Color(1f, 0.42f, 0.38f, 1f) : new Color(0.62f, 0.62f, 0.62f, 1f);
        }
    }

    private void CreateToggleRow(Transform parent, string label, Vector2 position, string prefKey, bool fallback, System.Action<bool> onChanged)
    {
        Toggle toggle = CreateToggle(label + " Toggle", parent, label, PlayerPrefs.GetInt(prefKey, fallback ? 1 : 0) == 1);
        RectTransform rect = toggle.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(520f, 70f);
        toggle.onValueChanged.AddListener(value =>
        {
            PlayerPrefs.SetInt(prefKey, value ? 1 : 0);
            onChanged(value);
        });
    }

    private void ShowSettings()
    {
        PlayClick();
        ShowPage(settingsPanel);
    }

    private void BackFromSettings()
    {
        PlayClick();
        if (mode == MenuMode.MainMenu)
        {
            ShowPage(mainRoot);
        }
        else
        {
            ShowPage(pausePanel);
        }
    }

    private void ShowMainMenu()
    {
        PlayClick();
        ShowMainMenu(false);
    }

    private void ShowMainMenu(bool playSound)
    {
        if (playSound)
        {
            PlayClick();
        }

        if (mainRoot != null)
        {
            ShowPage(mainRoot);
        }
    }

    private void ShowCredits()
    {
        PlayClick();
        ShowPage(creditsPage);
    }

    private void SetPaused(bool paused)
    {
        if (isPaused == paused)
        {
            return;
        }

        isPaused = paused;
        pauseRoot.SetActive(paused);
        pausePanel.SetActive(paused);
        settingsPanel.SetActive(false);
        activeMenuPage = paused ? pausePanel : null;
        Time.timeScale = paused ? 0f : 1f;

        if (paused)
        {
            EnterGameplayPauseState();
            PlayMenuMusic();
            if (pauseOpenRoutine != null)
            {
                StopCoroutine(pauseOpenRoutine);
            }

            pauseOpenRoutine = StartCoroutine(AnimatePauseOpen());
        }
        else
        {
            if (pauseOpenRoutine != null)
            {
                StopCoroutine(pauseOpenRoutine);
                pauseOpenRoutine = null;
            }

            StopMenuMusic();
            ExitGameplayPauseState();
        }
    }

    private void BuildIntroCutscene()
    {
        if (canvas == null)
        {
            return;
        }

        introRoot = CreatePanel("Intro Cutscene Overlay", canvas.transform, Color.black);
        Stretch(introRoot.GetComponent<RectTransform>());

        CanvasGroup group = introRoot.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = true;

        introLabel = CreateText(string.Empty, introRoot.transform, 52, Color.white, TextAlignmentOptions.Center, new Vector2(1440f, 220f));
        RectTransform labelRect = introLabel.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        introLabel.enableAutoSizing = true;
        introLabel.fontSizeMin = 28f;
        introLabel.fontSizeMax = 52f;

        introContinueLabel = CreateText(cutsceneContinueText, introRoot.transform, 28, Color.white, TextAlignmentOptions.Center, new Vector2(900f, 80f));
        RectTransform continueRect = introContinueLabel.GetComponent<RectTransform>();
        continueRect.anchorMin = new Vector2(0.5f, 0.5f);
        continueRect.anchorMax = new Vector2(0.5f, 0.5f);
        continueRect.pivot = new Vector2(0.5f, 0.5f);
        continueRect.anchoredPosition = new Vector2(0f, -145f);
        introContinueLabel.enableAutoSizing = true;
        introContinueLabel.fontSizeMin = 18f;
        introContinueLabel.fontSizeMax = 28f;
        introContinueLabel.alpha = 0f;
    }

    private IEnumerator PlayIntroCutscene()
    {
        if (introRoot == null || introLabel == null || introContinueLabel == null)
        {
            yield break;
        }

        introActive = true;
        EnterGameplayPauseState();
        SoftwareCursor.Show(false);
        introLabel.text = string.Empty;
        introContinueLabel.alpha = 0f;
        yield return null;
        List<CutsceneTextEntry> entries = introTexts != null && introTexts.Count > 0 ? introTexts : new List<CutsceneTextEntry> { new CutsceneTextEntry() };
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            CutsceneTextEntry entry = entries[entryIndex] ?? new CutsceneTextEntry();
            string line = entry.text ?? string.Empty;
            float secondsPerLetter = 1f / Mathf.Max(1f, entry.lettersPerSecond);

            introLabel.text = string.Empty;
            for (int i = 0; i < line.Length; i++)
            {
                introLabel.text = line.Substring(0, i + 1);
                if (char.IsWhiteSpace(line[i]))
                {
                    continue;
                }

                PlayCutsceneSound(cutsceneTypingSound);
                yield return new WaitForSecondsRealtime(secondsPerLetter);
            }

            PlayCutsceneSound(cutsceneFinishSound);
            yield return null;

            while (!Input.anyKeyDown)
            {
                introContinueLabel.alpha = Mathf.Lerp(0.25f, 1f, Mathf.PingPong(Time.unscaledTime * 1.8f, 1f));
                yield return null;
            }

            introContinueLabel.alpha = 0f;
        }

        introRoot.SetActive(false);
        ExitGameplayPauseState(true);
        introActive = false;
    }

    // The software cursor lives in the scene (a Canvas + Image + SoftwareCursor
    // component); menus just toggle it on/off. See SoftwareCursor.cs.
    private void ShowSoftwareCursor(bool show)
    {
        SoftwareCursor.Show(show);
    }

    private void EnterGameplayPauseState()
    {
        Time.timeScale = 0f;
        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None;
        ShowSoftwareCursor(true);

        if (playerController != null)
        {
            previousPlayerCanMove = playerController.playerCanMove;
            previousCameraCanMove = playerController.cameraCanMove;
            playerController.playerCanMove = false;
            playerController.cameraCanMove = false;
        }

        PauseWorldProducers();
    }

    private void ExitGameplayPauseState(bool forceGameplayCursor = false)
    {
        Time.timeScale = 1f;
        ResumeWorldProducers();
        ShowSoftwareCursor(false);

        if (forceGameplayCursor && playerController != null && playerController.lockCursor)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockMode;
        }

        if (playerController != null)
        {
            playerController.playerCanMove = previousPlayerCanMove;
            playerController.cameraCanMove = previousCameraCanMove;
        }
    }

    private void ResumeGame()
    {
        PlayClick();
        SetPaused(false);
    }

    private void LoadGameplay()
    {
        PlayClick();
        playIntroOnNextGameplayLoad = true;
        SceneLoadingScreen.Load(gameplaySceneName);
    }

    private void LoadMainMenu()
    {
        PlayClick();
        Time.timeScale = 1f;
        SceneLoadingScreen.Load(mainMenuSceneName);
    }

    private void QuitGame()
    {
        PlayClick();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowPage(GameObject targetPage)
    {
        if (targetPage == null)
        {
            return;
        }

        if (activeMenuPage == targetPage)
        {
            targetPage.SetActive(true);
            return;
        }

        if (pageTransitionRoutine != null)
        {
            StopCoroutine(pageTransitionRoutine);
        }

        pageTransitionRoutine = StartCoroutine(AnimatePageTransition(activeMenuPage, targetPage));
        activeMenuPage = targetPage;
    }

    private IEnumerator AnimatePageTransition(GameObject fromPage, GameObject toPage)
    {
        const float duration = 0.28f;
        CanvasGroup fromGroup = EnsureCanvasGroup(fromPage);
        CanvasGroup toGroup = EnsureCanvasGroup(toPage);
        RectTransform fromRect = fromPage != null ? fromPage.GetComponent<RectTransform>() : null;
        RectTransform toRect = toPage.GetComponent<RectTransform>();

        Vector2 fromStart = fromRect != null ? fromRect.anchoredPosition : Vector2.zero;
        Vector2 fromEnd = fromStart + new Vector2(-120f, 0f);
        Vector2 toStart = new Vector2(120f, 0f);

        toPage.SetActive(true);
        toGroup.alpha = 0f;
        toGroup.blocksRaycasts = false;
        toRect.anchoredPosition = toStart;

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            if (fromGroup != null)
            {
                fromGroup.alpha = 1f - t;
            }

            if (fromRect != null)
            {
                fromRect.anchoredPosition = Vector2.Lerp(fromStart, fromEnd, t);
            }

            toGroup.alpha = t;
            toRect.anchoredPosition = Vector2.Lerp(toStart, Vector2.zero, t);
            yield return null;
        }

        if (fromPage != null)
        {
            fromPage.SetActive(false);
            fromGroup.alpha = 1f;
            fromGroup.blocksRaycasts = false;
            if (fromRect != null)
            {
                fromRect.anchoredPosition = Vector2.zero;
            }
        }

        toGroup.alpha = 1f;
        toGroup.blocksRaycasts = true;
        toRect.anchoredPosition = Vector2.zero;
        pageTransitionRoutine = null;
    }

    private CanvasGroup EnsureCanvasGroup(GameObject target)
    {
        if (target == null)
        {
            return null;
        }

        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = target.AddComponent<CanvasGroup>();
        }

        return group;
    }

    private void BindButton(string objectName, UnityEngine.Events.UnityAction action)
    {
        GameObject target = FindSceneObject(objectName);
        if (target == null)
        {
            return;
        }

        Button button = target.GetComponent<Button>();
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void ConfigureRuntimeMenuButton(GameObject target, Vector2 position, Vector2 size)
    {
        ConfigureRuntimeMenuButton(target, position, size, new Color(0.2f, 0.2f, 0.2f, 1f));
    }

    private void ConfigureRuntimeMenuButton(GameObject target, Vector2 position, Vector2 size, Color textColor)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI label = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSize = 56f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 28f;
            label.fontSizeMax = 64f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = textColor;
            RectTransform labelRect = label.GetComponent<RectTransform>();
            if (labelRect != null)
            {
                Stretch(labelRect);
            }
        }
    }

    private void ApplySavedSettings()
    {
        AudioListener.volume = PlayerPrefs.GetFloat(VolumePref, 1f);
        Screen.fullScreen = PlayerPrefs.GetInt(FullscreenPref, Screen.fullScreen ? 1 : 0) == 1;
        ApplyMouseSensitivity(Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityPref, 2f), SensitivityMin, SensitivityMax));
        GraphicsQualityManager.Apply();
    }

    private void ApplyMouseSensitivity(float value)
    {
        FirstPersonController controller = playerController != null ? playerController : FindObjectOfType<FirstPersonController>();
        if (controller != null)
        {
            controller.mouseSensitivity = value;
        }
    }

    private void PauseWorldProducers()
    {
        pausedAudioSources.Clear();
        AudioSource[] sources = FindObjectsOfType<AudioSource>();
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null || source == audioSource || !source.isPlaying)
            {
                continue;
            }

            source.Pause();
            pausedAudioSources.Add(source);
        }

        pausedVideoPlayers.Clear();
        VideoPlayer[] players = FindObjectsOfType<VideoPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            VideoPlayer player = players[i];
            if (player == null || !player.isPlaying)
            {
                continue;
            }

            player.Pause();
            pausedVideoPlayers.Add(player);
        }
    }

    private void ResumeWorldProducers()
    {
        for (int i = 0; i < pausedAudioSources.Count; i++)
        {
            AudioSource source = pausedAudioSources[i];
            if (source != null)
            {
                source.UnPause();
            }
        }

        pausedAudioSources.Clear();

        for (int i = 0; i < pausedVideoPlayers.Count; i++)
        {
            VideoPlayer player = pausedVideoPlayers[i];
            if (player != null)
            {
                player.Play();
            }
        }

        pausedVideoPlayers.Clear();
    }

    private void PlayMenuMusic()
    {
        if (menuMusic == null || audioSource == null)
        {
            return;
        }

        audioSource.clip = menuMusic;
        audioSource.loop = true;
        if (!audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void StopMenuMusic()
    {
        if (audioSource == null || audioSource.clip != menuMusic)
        {
            return;
        }

        audioSource.Stop();
        audioSource.loop = false;
        audioSource.clip = null;
    }

    private IEnumerator AnimatePauseOpen()
    {
        CanvasGroup rootGroup = EnsureCanvasGroup(pauseRoot);
        rootGroup.alpha = 0f;
        rootGroup.blocksRaycasts = false;

        RectTransform[] buttonRects =
        {
            FindPauseChildRect("Resume Button"),
            FindPauseChildRect("Settings Button"),
            FindPauseChildRect("Main Menu Button"),
            FindPauseChildRect("Exit Button")
        };

        Vector2[] finalPositions = new Vector2[buttonRects.Length];
        for (int i = 0; i < buttonRects.Length; i++)
        {
            if (buttonRects[i] == null)
            {
                continue;
            }

            finalPositions[i] = buttonRects[i].anchoredPosition;
            buttonRects[i].anchoredPosition = finalPositions[i] + new Vector2(-180f, 0f);
            CanvasGroup buttonGroup = EnsureCanvasGroup(buttonRects[i].gameObject);
            buttonGroup.alpha = 0f;
        }

        for (float elapsed = 0f; elapsed < 0.22f; elapsed += Time.unscaledDeltaTime)
        {
            rootGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.22f));
            yield return null;
        }

        rootGroup.alpha = 1f;

        for (int i = 0; i < buttonRects.Length; i++)
        {
            if (buttonRects[i] == null)
            {
                continue;
            }

            StartCoroutine(AnimatePauseButton(buttonRects[i], finalPositions[i], i * 0.07f));
        }

        yield return new WaitForSecondsRealtime(0.34f + buttonRects.Length * 0.07f);
        rootGroup.blocksRaycasts = true;
        pauseOpenRoutine = null;
    }

    private IEnumerator AnimatePauseButton(RectTransform rect, Vector2 finalPosition, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        CanvasGroup group = EnsureCanvasGroup(rect.gameObject);
        Vector2 startPosition = finalPosition + new Vector2(-180f, 0f);

        for (float elapsed = 0f; elapsed < 0.28f; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.28f));
            rect.anchoredPosition = Vector2.Lerp(startPosition, finalPosition, t);
            group.alpha = t;
            yield return null;
        }

        rect.anchoredPosition = finalPosition;
        group.alpha = 1f;
    }

    private RectTransform FindPauseChildRect(string childName)
    {
        Transform child = pausePanel != null ? pausePanel.transform.Find(childName) : null;
        return child != null ? child.GetComponent<RectTransform>() : null;
    }

    private Canvas BuildCanvas(string canvasName)
    {
        GameObject canvasObject = new GameObject(canvasName);
        Canvas newCanvas = canvasObject.AddComponent<Canvas>();
        newCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        newCanvas.sortingOrder = 100;
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

        return newCanvas;
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        return CreatePanel(name, parent, color, Vector2.zero, new Vector2(100f, 100f));
    }

    private GameObject CreatePanel(string name, Transform parent, Color color, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = color;
        if (backgroundSprite != null && name.Contains("Root"))
        {
            image.sprite = backgroundSprite;
            image.preserveAspect = false;
        }

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return panel;
    }

    private Button CreateButton(string name, Transform parent, string text, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = CreatePanel(name, parent, Color.white, Vector2.zero, new Vector2(320f, 58f));
        Image image = buttonObject.GetComponent<Image>();
        image.sprite = buttonSprite;
        image.type = buttonSprite != null ? Image.Type.Sliced : Image.Type.Simple;

        Button button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(action);
        if (focusedButtonSprite != null)
        {
            button_menu visual = buttonObject.AddComponent<button_menu>();
            visual.targetImage = image;
            visual.normalSprite = buttonSprite;
            visual.focusedSprite = focusedButtonSprite;
        }

        CreateText(text, buttonObject.transform, 24, new Color(0.2f, 0.2f, 0.2f, 1f), TextAlignmentOptions.Center, new Vector2(320f, 58f));
        return button;
    }

    private Slider CreateSlider(string name, Transform parent, float minValue, float maxValue, float value)
    {
        GameObject sliderObject = new GameObject(name);
        sliderObject.transform.SetParent(parent, false);
        RectTransform rect = sliderObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180f, 32f);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = Mathf.Clamp(value, minValue, maxValue);

        GameObject background = CreatePanel("Background", sliderObject.transform, new Color(0.18f, 0.18f, 0.2f, 1f));
        Stretch(background.GetComponent<RectTransform>());
        GameObject fill = CreatePanel("Fill", sliderObject.transform, new Color(0.88f, 0.15f, 0.12f, 1f));
        Stretch(fill.GetComponent<RectTransform>());
        slider.fillRect = fill.GetComponent<RectTransform>();

        GameObject handle = CreatePanel("Handle", sliderObject.transform, Color.white, Vector2.zero, new Vector2(24f, 34f));
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handle.GetComponent<Image>();
        return slider;
    }

    private Toggle CreateToggle(string name, Transform parent, string text, bool isOn)
    {
        GameObject toggleObject = CreatePanel(name, parent, Color.clear, Vector2.zero, new Vector2(400f, 52f));
        AddHorizontalLayout(toggleObject, 16f, 0f);

        GameObject box = CreatePanel("Box", toggleObject.transform, Color.white, Vector2.zero, new Vector2(42f, 42f));
        GameObject checkmark = CreatePanel("Checkmark", box.transform, new Color(0.88f, 0.15f, 0.12f, 1f), Vector2.zero, new Vector2(26f, 26f));

        Toggle toggle = toggleObject.AddComponent<Toggle>();
        toggle.targetGraphic = box.GetComponent<Image>();
        toggle.graphic = checkmark.GetComponent<Image>();
        toggle.isOn = isOn;
        CreateText(text, toggleObject.transform, 22, Color.white, TextAlignmentOptions.Left, new Vector2(300f, 48f));
        return toggle;
    }

    private TextMeshProUGUI CreateText(string text, Transform parent, int fontSize, Color color, TextAlignmentOptions alignment, Vector2 size)
    {
        GameObject textObject = new GameObject(text + " Text");
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        return label;
    }

    private TextMeshProUGUI CreatePageText(string text, Transform parent, Vector2 position, Vector2 size, int fontSize, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI label = CreateText(text, parent, fontSize, Color.white, alignment, size);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Max(18f, fontSize * 0.55f);
        label.fontSizeMax = fontSize;
        return label;
    }

    private void AddVerticalLayout(GameObject target, float spacing, float padding)
    {
        VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = spacing;
        layout.padding = new RectOffset(Mathf.RoundToInt(padding), Mathf.RoundToInt(padding), Mathf.RoundToInt(padding), Mathf.RoundToInt(padding));
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void AddHorizontalLayout(GameObject target, float spacing, float padding)
    {
        HorizontalLayoutGroup layout = target.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = spacing;
        layout.padding = new RectOffset(Mathf.RoundToInt(padding), Mathf.RoundToInt(padding), Mathf.RoundToInt(padding), Mathf.RoundToInt(padding));
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void PlayClick()
    {
        if (clickSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
    }

    private void PlayCutsceneSound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private GameObject FindSceneObject(string objectName)
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject candidate = objects[i];
            if (candidate.name == objectName && candidate.scene.IsValid())
            {
                return candidate;
            }
        }

        return null;
    }

    private Sprite CreateSprite(Texture2D texture)
    {
        if (texture == null)
        {
            return null;
        }

        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
    }
}

public static class SceneLoadingScreen
{
    private const float FadeDuration = 0.5f;

    public static void Load(string sceneName)
    {
        GameObject runnerObject = new GameObject("Scene Loading Screen Runner");
        Object.DontDestroyOnLoad(runnerObject);
        SceneLoadingScreenRunner runner = runnerObject.AddComponent<SceneLoadingScreenRunner>();
        runner.Begin(sceneName);
    }

    private sealed class SceneLoadingScreenRunner : MonoBehaviour
    {
        private CanvasGroup group;
        private RectTransform fillRect;
        private GameObject root;

        public void Begin(string sceneName)
        {
            StartCoroutine(LoadRoutine(sceneName));
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            Time.timeScale = 1f;
            BuildOverlay();
            yield return Fade(0f, 1f);

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            if (load == null)
            {
                Destroy(root);
                Destroy(gameObject);
                yield break;
            }

            load.allowSceneActivation = false;
            while (load.progress < 0.9f)
            {
                SetProgress(Mathf.Clamp01(load.progress / 0.9f));
                yield return null;
            }

            SetProgress(1f);
            load.allowSceneActivation = true;

            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return Fade(1f, 0f);
            Destroy(root);
            Destroy(gameObject);
        }

        private void BuildOverlay()
        {
            root = new GameObject("Scene Loading Screen");
            Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10000;
            root.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image background = root.AddComponent<Image>();
            background.color = Color.black;

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = true;

            CreateText("Loading", root.transform, new Vector2(70f, 60f), new Vector2(560f, 90f), 66);

            GameObject track = CreatePanel("Loading Track", root.transform, new Color(0.14f, 0.14f, 0.15f, 1f), new Vector2(70f, -70f), new Vector2(560f, 16f));
            GameObject fill = CreatePanel("Loading Fill", track.transform, new Color(0.88f, 0.06f, 0.05f, 1f), Vector2.zero, Vector2.zero);
            fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }

        private IEnumerator Fade(float from, float to)
        {
            for (float elapsed = 0f; elapsed < FadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / FadeDuration)));
                yield return null;
            }

            group.alpha = to;
        }

        private void SetProgress(float progress)
        {
            if (fillRect != null)
            {
                fillRect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            }
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color, Vector2 position, Vector2 size)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            Image image = panel.AddComponent<Image>();
            image.color = color;

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return panel;
        }

        private static TextMeshProUGUI CreateText(string text, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject textObject = new GameObject(text + " Text");
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Left;
            label.enableAutoSizing = true;
            label.fontSizeMin = 34f;
            label.fontSizeMax = fontSize;

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return label;
        }
    }
}
