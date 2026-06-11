using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class button_menu : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Image targetImage;
    public Sprite normalSprite;
    public Sprite focusedSprite;
    [SerializeField] private Vector3 focusedScale = new Vector3(1.04f, 1.04f, 1.04f);
    [SerializeField] private float transitionDuration = 0.15f;

    private Vector3 normalScale;
    private Coroutine transitionRoutine;
    private bool isFocused;

    private void Awake()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        normalScale = transform.localScale;
        if (targetImage != null && normalSprite == null)
        {
            normalSprite = targetImage.sprite;
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        SetFocused(true);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        SetFocused(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetFocused(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetFocused(false);
    }

    private void SetFocused(bool isFocused)
    {
        if (this.isFocused == isFocused && transitionRoutine != null)
        {
            return;
        }

        this.isFocused = isFocused;

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }

        transitionRoutine = StartCoroutine(AnimateFocusChange(isFocused));
    }

    private System.Collections.IEnumerator AnimateFocusChange(bool targetFocused)
    {
        if (targetImage == null)
        {
            transform.localScale = targetFocused ? focusedScale : normalScale;
            yield break;
        }

        Sprite targetSprite = targetFocused && focusedSprite != null ? focusedSprite : normalSprite;
        Vector3 startScale = transform.localScale;
        Vector3 endScale = targetFocused ? focusedScale : normalScale;
        float duration = Mathf.Max(0.01f, transitionDuration);

        if (targetSprite != null)
        {
            targetImage.sprite = targetSprite;
        }

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        transform.localScale = endScale;
        transitionRoutine = null;
    }
}
