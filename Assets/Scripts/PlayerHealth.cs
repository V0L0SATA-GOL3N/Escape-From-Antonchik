using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Player hit points shown as a row of 10 hearts at the top-left of the screen.
// The screamers chip away at this instead of insta-killing: the Antonchik
// screamer takes 7 hearts when it reaches you, each scalapendra takes 3. When
// the last heart goes the shared GameOverScreen shows.
//
// State lives in a static field so it carries across the gamePlay ->
// continueGamePlay transition (scene loads keep statics within one play session,
// the same trick InventoryCarryOver uses). Loading gamePlay always refills,
// because that scene is the start of a run.
public class PlayerHealth : MonoBehaviour
{
    public const int MaxHearts = 10;

    private const int FilledHeartSortOrder = 3900;

    // -1 means "not initialised yet" so a direct scene load can default to full.
    private static int currentHearts = -1;
    private static PlayerHealth instance;
    private static Sprite heartSprite;

    private readonly Color filledColor = new Color(0.86f, 0.12f, 0.12f, 1f);
    private readonly Color emptyColor = new Color(0.16f, 0.05f, 0.05f, 0.65f);

    private Image[] hearts;

    public static int Current => Mathf.Clamp(currentHearts, 0, MaxHearts);

    // Register the per-scene spawn hook once, before the first scene loads (so it
    // also fires for that first scene).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "gamePlay")
        {
            // a fresh run always starts on full health
            currentHearts = MaxHearts;
        }
        else if (scene.name == "continueGamePlay")
        {
            // keep the count carried from gamePlay; default to full if this scene
            // was loaded directly (e.g. testing in the editor)
            if (currentHearts < 0)
            {
                currentHearts = MaxHearts;
            }
        }
        else
        {
            // not a gameplay scene — no health bar
            return;
        }

        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }

        GameObject hudObject = new GameObject("Player Health HUD");
        instance = hudObject.AddComponent<PlayerHealth>();
    }

    // Take `hearts` damage. If it empties the bar, show game over with `deathReason`.
    public static void Damage(int hearts, string deathReason)
    {
        if (hearts <= 0 || GameOverScreen.IsActive)
        {
            return;
        }

        if (currentHearts < 0)
        {
            currentHearts = MaxHearts;
        }

        currentHearts = Mathf.Max(0, currentHearts - hearts);

        EnsureInstance();
        if (instance != null)
        {
            instance.Refresh();
            instance.PunchFlash();
        }

        // a red wash sells the hit
        ScreenFlash.Flash(new Color(0.5f, 0f, 0f, 0.55f), 0.3f);

        if (currentHearts <= 0)
        {
            GameOverScreen.Show(deathReason);
        }
    }

    public static void ResetToFull()
    {
        currentHearts = MaxHearts;
        if (instance != null)
        {
            instance.Refresh();
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        if (currentHearts < 0)
        {
            currentHearts = MaxHearts;
        }

        BuildUi();
        Refresh();
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
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = FilledHeartSortOrder;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject rowObject = new GameObject("Hearts");
        rowObject.transform.SetParent(transform, false);
        RectTransform row = rowObject.AddComponent<RectTransform>();
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.anchoredPosition = new Vector2(34f, -28f);

        const float size = 40f;
        const float spacing = 46f;

        Sprite sprite = GetHeartSprite();
        hearts = new Image[MaxHearts];
        for (int i = 0; i < MaxHearts; i++)
        {
            GameObject heartObject = new GameObject("Heart " + i);
            heartObject.transform.SetParent(rowObject.transform, false);
            Image image = heartObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(i * spacing, 0f);
            hearts[i] = image;
        }
    }

    private void Refresh()
    {
        if (hearts == null)
        {
            return;
        }

        int filled = Mathf.Clamp(currentHearts, 0, MaxHearts);
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] != null)
            {
                hearts[i].color = i < filled ? filledColor : emptyColor;
            }
        }
    }

    private void PunchFlash()
    {
        StopAllCoroutines();
        StartCoroutine(PunchRoutine());
    }

    // brief scale-up pop on the hearts so taking damage is felt
    private IEnumerator PunchRoutine()
    {
        if (hearts == null)
        {
            yield break;
        }

        for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
        {
            float s = 1f + 0.25f * Mathf.Sin((t / 0.18f) * Mathf.PI);
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] != null)
                {
                    hearts[i].rectTransform.localScale = Vector3.one * s;
                }
            }

            yield return null;
        }

        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] != null)
            {
                hearts[i].rectTransform.localScale = Vector3.one;
            }
        }
    }

    private static Sprite GetHeartSprite()
    {
        if (heartSprite != null)
        {
            return heartSprite;
        }

        const int size = 96;
        const float half = size * 0.5f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 2x2 supersample for a smooth edge
                int inside = 0;
                for (int sy = 0; sy < 2; sy++)
                {
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float px = (x + (sx + 0.5f) * 0.5f - half) / half * 1.35f;
                        float py = (y + (sy + 0.5f) * 0.5f - half) / half * 1.35f;
                        if (IsInsideHeart(px, py))
                        {
                            inside++;
                        }
                    }
                }

                byte a = (byte)(255 * inside / 4);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        heartSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return heartSprite;
    }

    // Classic implicit heart curve: (x^2 + y^2 - 1)^3 - x^2 * y^3 < 0, with the
    // lobes at the top (y up) and the point at the bottom.
    private static bool IsInsideHeart(float x, float y)
    {
        float a = x * x + y * y - 1f;
        return a * a * a - x * x * y * y * y < 0f;
    }
}
