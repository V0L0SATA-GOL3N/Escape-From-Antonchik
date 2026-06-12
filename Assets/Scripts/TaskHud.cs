using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// GTA5-style objective display: small caps text at the bottom centre of the
// screen, key words highlighted in yellow, blip sound on change.
public class TaskHud : MonoBehaviour
{
    public const string Highlight = "#FFD24A";

    private static TaskHud instance;

    private Canvas canvas;
    private CanvasGroup objectiveGroup;
    private TextMeshProUGUI objectiveLabel;
    private CanvasGroup bannerGroup;
    private TextMeshProUGUI bannerLabel;
    private TextMeshProUGUI bannerSubLabel;
    private AudioSource uiAudio;
    private Coroutine objectiveRoutine;
    private Coroutine bannerRoutine;

    public static void SetObjective(string richText)
    {
        Ensure();
        instance.ShowObjectiveInternal(richText, true);
    }

    public static void SetObjectiveSilent(string richText)
    {
        Ensure();
        instance.ShowObjectiveInternal(richText, false);
    }

    public static void ClearObjective()
    {
        if (instance != null)
        {
            instance.HideObjectiveInternal();
        }
    }

    public static void ShowBanner(string title, string subtitle)
    {
        Ensure();
        instance.ShowBannerInternal(title, subtitle);
    }

    private static void Ensure()
    {
        if (instance != null)
        {
            return;
        }

        GameObject hudObject = new GameObject("Task HUD");
        instance = hudObject.AddComponent<TaskHud>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        BuildUi();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void BuildUi()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        uiAudio = gameObject.AddComponent<AudioSource>();
        uiAudio.playOnAwake = false;
        uiAudio.spatialBlend = 0f;

        GameObject objectiveObject = new GameObject("Objective");
        objectiveObject.transform.SetParent(transform, false);
        RectTransform objectiveRect = objectiveObject.AddComponent<RectTransform>();
        objectiveGroup = objectiveObject.AddComponent<CanvasGroup>();
        objectiveGroup.alpha = 0f;
        objectiveGroup.blocksRaycasts = false;

        objectiveRect.anchorMin = new Vector2(0.5f, 0f);
        objectiveRect.anchorMax = new Vector2(0.5f, 0f);
        objectiveRect.pivot = new Vector2(0.5f, 0f);
        objectiveRect.anchoredPosition = new Vector2(0f, 64f);
        objectiveRect.sizeDelta = new Vector2(1500f, 88f);

        GameObject shadowObject = new GameObject("Objective Shadow");
        shadowObject.transform.SetParent(objectiveObject.transform, false);
        TextMeshProUGUI shadow = shadowObject.AddComponent<TextMeshProUGUI>();
        ConfigureObjectiveLabel(shadow);
        shadow.color = new Color(0f, 0f, 0f, 0.85f);
        RectTransform shadowRect = shadow.rectTransform;
        shadowRect.anchorMin = Vector2.zero;
        shadowRect.anchorMax = Vector2.one;
        shadowRect.offsetMin = new Vector2(2.5f, -2.5f);
        shadowRect.offsetMax = new Vector2(2.5f, -2.5f);

        GameObject labelObject = new GameObject("Objective Label");
        labelObject.transform.SetParent(objectiveObject.transform, false);
        objectiveLabel = labelObject.AddComponent<TextMeshProUGUI>();
        ConfigureObjectiveLabel(objectiveLabel);
        RectTransform labelRect = objectiveLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        // mirror text into the drop shadow whenever it changes
        objectiveLabel.RegisterDirtyVerticesCallback(() =>
        {
            if (shadow != null && objectiveLabel != null)
            {
                shadow.text = StripColorTags(objectiveLabel.text);
            }
        });

        GameObject bannerObject = new GameObject("Mission Banner");
        bannerObject.transform.SetParent(transform, false);
        RectTransform bannerRect = bannerObject.AddComponent<RectTransform>();
        bannerGroup = bannerObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
        bannerGroup.blocksRaycasts = false;

        bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
        bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.anchoredPosition = new Vector2(0f, 120f);
        bannerRect.sizeDelta = new Vector2(1600f, 220f);

        GameObject bannerTitleObject = new GameObject("Banner Title");
        bannerTitleObject.transform.SetParent(bannerObject.transform, false);
        bannerLabel = bannerTitleObject.AddComponent<TextMeshProUGUI>();
        bannerLabel.fontSize = 74f;
        bannerLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
        bannerLabel.alignment = TextAlignmentOptions.Center;
        bannerLabel.color = new Color(1f, 0.82f, 0.29f, 1f);
        bannerLabel.characterSpacing = 6f;
        bannerLabel.raycastTarget = false;
        RectTransform bannerTitleRect = bannerLabel.rectTransform;
        bannerTitleRect.anchorMin = new Vector2(0f, 0.45f);
        bannerTitleRect.anchorMax = new Vector2(1f, 1f);
        bannerTitleRect.offsetMin = Vector2.zero;
        bannerTitleRect.offsetMax = Vector2.zero;

        GameObject bannerSubObject = new GameObject("Banner Subtitle");
        bannerSubObject.transform.SetParent(bannerObject.transform, false);
        bannerSubLabel = bannerSubObject.AddComponent<TextMeshProUGUI>();
        bannerSubLabel.fontSize = 34f;
        bannerSubLabel.alignment = TextAlignmentOptions.Center;
        bannerSubLabel.color = new Color(0.92f, 0.9f, 0.88f, 1f);
        bannerSubLabel.raycastTarget = false;
        RectTransform bannerSubRect = bannerSubLabel.rectTransform;
        bannerSubRect.anchorMin = new Vector2(0f, 0f);
        bannerSubRect.anchorMax = new Vector2(1f, 0.45f);
        bannerSubRect.offsetMin = Vector2.zero;
        bannerSubRect.offsetMax = Vector2.zero;
    }

    private static void ConfigureObjectiveLabel(TextMeshProUGUI label)
    {
        label.fontSize = 32f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.94f, 0.94f, 0.94f, 1f);
        label.raycastTarget = false;
        label.enableWordWrapping = true;
    }

    private static string StripColorTags(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, "</?color[^>]*>", string.Empty);
    }

    private void ShowObjectiveInternal(string richText, bool playBlip)
    {
        if (objectiveRoutine != null)
        {
            StopCoroutine(objectiveRoutine);
        }

        objectiveLabel.text = richText;
        if (playBlip)
        {
            uiAudio.PlayOneShot(HorrorAudio.ObjectiveBlip(), 0.7f);
        }

        objectiveRoutine = StartCoroutine(FadeGroup(objectiveGroup, objectiveGroup.alpha, 1f, 0.45f));
    }

    private void HideObjectiveInternal()
    {
        if (objectiveRoutine != null)
        {
            StopCoroutine(objectiveRoutine);
        }

        objectiveRoutine = StartCoroutine(FadeGroup(objectiveGroup, objectiveGroup.alpha, 0f, 0.45f));
    }

    private void ShowBannerInternal(string title, string subtitle)
    {
        bannerLabel.text = title;
        bannerSubLabel.text = subtitle;
        uiAudio.PlayOneShot(HorrorAudio.MissionSting(), 0.85f);

        if (bannerRoutine != null)
        {
            StopCoroutine(bannerRoutine);
        }

        bannerRoutine = StartCoroutine(BannerRoutine());
    }

    private IEnumerator BannerRoutine()
    {
        yield return FadeGroup(bannerGroup, 0f, 1f, 0.6f);
        yield return new WaitForSeconds(2.6f);
        yield return FadeGroup(bannerGroup, 1f, 0f, 0.8f);
    }

    private IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        group.alpha = to;
    }
}

// Shared death screen so screamers and the car sequence can kill the player
// with the same look as the Antonchik encounter.
public static class GameOverScreen
{
    private static bool active;

    public static bool IsActive => active;

    public static void Show(string reason)
    {
        if (active)
        {
            return;
        }

        active = true;
        GameObject runnerObject = new GameObject("Game Over Screen");
        runnerObject.AddComponent<GameOverRunner>().Begin(reason);
    }

    private sealed class GameOverRunner : MonoBehaviour
    {
        public void Begin(string reason)
        {
            FirstPersonController controller = Object.FindObjectOfType<FirstPersonController>();
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
            }

            StartCoroutine(ShowRoutine(reason));
        }

        private void OnDestroy()
        {
            active = false;
        }

        private System.Collections.IEnumerator ShowRoutine(string reason)
        {
            GameObject canvasObject = new GameObject("Game Over Canvas");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6000;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            GameObject blackout = new GameObject("Blackout");
            blackout.transform.SetParent(canvasObject.transform, false);
            Image blackImage = blackout.AddComponent<Image>();
            blackImage.color = Color.black;
            StretchFull(blackout.GetComponent<RectTransform>());
            CanvasGroup group = blackout.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            GameObject titleObject = new GameObject("Title");
            titleObject.transform.SetParent(blackout.transform, false);
            TextMeshProUGUI title = titleObject.AddComponent<TextMeshProUGUI>();
            title.text = "ТЫ УМЕР";
            title.fontSize = 132f;
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 16f;
            title.alignment = TextAlignmentOptions.Center;
            title.color = new Color(0.55f, 0.02f, 0.02f, 1f);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 90f);
            titleRect.sizeDelta = new Vector2(1500f, 200f);

            GameObject reasonObject = new GameObject("Reason");
            reasonObject.transform.SetParent(blackout.transform, false);
            TextMeshProUGUI reasonLabel = reasonObject.AddComponent<TextMeshProUGUI>();
            reasonLabel.text = reason;
            reasonLabel.fontSize = 32f;
            reasonLabel.alignment = TextAlignmentOptions.Center;
            reasonLabel.color = new Color(0.7f, 0.65f, 0.62f, 1f);
            RectTransform reasonRect = reasonLabel.rectTransform;
            reasonRect.anchorMin = new Vector2(0.5f, 0.5f);
            reasonRect.anchorMax = new Vector2(0.5f, 0.5f);
            reasonRect.anchoredPosition = new Vector2(0f, -20f);
            reasonRect.sizeDelta = new Vector2(1500f, 60f);

            CreateButton(blackout.transform, "Начать заново", new Vector2(0f, -130f), () =>
            {
                Time.timeScale = 1f;
                SceneLoadingScreen.Load(SceneManager.GetActiveScene().name);
            });
            CreateButton(blackout.transform, "Главное меню", new Vector2(0f, -220f), () =>
            {
                Time.timeScale = 1f;
                SceneLoadingScreen.Load("menu");
            });

            for (float elapsed = 0f; elapsed < 1.4f; elapsed += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.SmoothStep(0f, 1f, elapsed / 1.4f);
                yield return null;
            }

            group.alpha = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void CreateButton(Transform parent, string text, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(text);
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.05f, 0.05f, 0.06f, 0.92f);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(420f, 72f);

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 34f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.85f, 0.8f, 0.78f, 1f);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.8f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(2.2f, 1.5f, 1.5f, 1f);
            button.colors = colors;
            button.onClick.AddListener(action);
        }
    }
}
