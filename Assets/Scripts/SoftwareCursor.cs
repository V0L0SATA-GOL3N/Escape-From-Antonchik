using UnityEngine;
using UnityEngine.UI;

// A software mouse cursor: a UI sprite that follows the OS mouse while the real
// hardware cursor stays hidden. The hardware cursor renders as garbage dots in
// standalone builds, so menus draw this instead.
//
// Setup (created in each scene via the editor): a ScreenSpaceOverlay Canvas with
// a high sortingOrder, this component on the Canvas, and a child Image holding
// the cursor sprite. The component finds that child Image automatically, so no
// inspector references need wiring. MenuController toggles it via Show(bool).
public class SoftwareCursor : MonoBehaviour
{
    public static SoftwareCursor Instance { get; private set; }

    // Pixel offset from the image's top-left to the actual click point. With the
    // image pivot at top-left (0,1) and hotspot (0,0) the tip sits on the mouse.
    [SerializeField] private Vector2 hotspot = Vector2.zero;

    private RectTransform cursorRect;
    private bool shown;

    private void Awake()
    {
        Instance = this;

        Image image = GetComponentInChildren<Image>(true);
        if (image != null)
        {
            cursorRect = image.rectTransform;
        }

        // Hide the real OS cursor from the first frame in every scene; the
        // software cursor is only shown on top of it for menus/pause.
        Cursor.visible = false;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Static entry point so callers don't need a scene reference.
    public static void Show(bool show)
    {
        if (Instance != null)
        {
            Instance.SetVisible(show);
        }
    }

    public void SetVisible(bool visible)
    {
        shown = visible && cursorRect != null;

        if (cursorRect != null)
        {
            cursorRect.gameObject.SetActive(shown);
        }

        if (shown)
        {
            // Keep the broken hardware cursor hidden while we draw our own.
            Cursor.visible = false;
            UpdatePosition();
        }
    }

    private void Update()
    {
        if (shown)
        {
            UpdatePosition();
        }
    }

    private void LateUpdate()
    {
        // Re-assert the hidden OS cursor after every other script's Update has
        // run, so nothing that flipped Cursor.visible = true this frame wins.
        if (shown)
        {
            Cursor.visible = false;
        }
    }

    private void UpdatePosition()
    {
        // On a ScreenSpaceOverlay canvas, world position maps 1:1 to screen
        // pixels regardless of any CanvasScaler, so this avoids scaler math.
        cursorRect.position = new Vector3(
            Input.mousePosition.x + hotspot.x,
            Input.mousePosition.y - hotspot.y,
            0f);
    }
}
