using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainDoorSceneTransition : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private float interactionDistance = 5f;
    [SerializeField] private string prompt = "Press E to leave";

    [Header("Door")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private string autoDoorPivotName = "DoorBone";
    [SerializeField] private float openAngle = 70f;
    [SerializeField] private float openSpeed = 3.5f;
    [SerializeField] private float movementStartAngle = 68f;
    [SerializeField] private Animator roomAnimator;
    [SerializeField] private string openAnimationState = "open";
    [SerializeField] private float movementStartNormalizedTime = 0.9f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip mainDoorOpenSound;

    [Header("Camera Pull")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private Transform movementTarget;
    [SerializeField] private string movementTargetTag = "openDoor";
    [SerializeField] private float cameraMoveDuration = 1f;
    [SerializeField] private float cameraForwardDistance = 2.4f;
    [SerializeField] private float exponentialStrength = 4f;
    [SerializeField] private string targetSceneName = "continueGamePlay";

    private Quaternion closedRotation;
    private Quaternion openRotation;
    private bool transitionStarted;
    private bool doorAnimationPaused;
    private CanvasGroup fadeGroup;

    public float InteractionDistance => interactionDistance;
    public string Prompt => prompt;
    public bool IsTransitioning => transitionStarted;

    private void Awake()
    {
        if (doorPivot == null)
        {
            doorPivot = FindChildByName(transform, autoDoorPivotName);
        }

        if (firstPersonController == null)
        {
            firstPersonController = FindObjectOfType<FirstPersonController>();
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (movementTarget == null && !string.IsNullOrWhiteSpace(movementTargetTag))
        {
            GameObject taggedTarget = GameObject.FindGameObjectWithTag(movementTargetTag);
            movementTarget = taggedTarget != null ? taggedTarget.transform : null;
        }

        if (roomAnimator == null)
        {
            roomAnimator = GetComponentInParent<Animator>();
        }

        if (roomAnimator == null)
        {
            GameObject room = GameObject.Find("room");
            roomAnimator = room != null ? room.GetComponent<Animator>() : null;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        if (doorPivot != null)
        {
            closedRotation = doorPivot.localRotation;
            openRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
        }
    }

    public void BeginTransition()
    {
        if (transitionStarted)
        {
            return;
        }

        transitionStarted = true;
        StartCoroutine(TransitionRoutine());
    }

    private IEnumerator TransitionRoutine()
    {
        LockPlayerInput();

        if (roomAnimator != null && roomAnimator.runtimeAnimatorController != null)
        {
            PlayMainDoorOpenSound();
            roomAnimator.speed = 1f;
            roomAnimator.CrossFadeInFixedTime(openAnimationState, 0.05f, 0, 0f);
        }

        while (!HasDoorReachedMovePoint())
        {
            yield return null;
        }

        PauseDoorAnimationAtCurrentFrame();

        Transform movingTransform = firstPersonController != null ? firstPersonController.transform : cameraTransform;
        if (movingTransform == null)
        {
            LoadTargetScene();
            yield break;
        }

        EnsureFadeOverlay();

        Vector3 startPosition = movingTransform.position;
        Quaternion startRotation = movingTransform.rotation;
        Vector3 doorDirection = doorPivot != null ? doorPivot.forward : transform.forward;
        Vector3 targetPosition = movementTarget != null
            ? movementTarget.position
            : startPosition + doorDirection.normalized * cameraForwardDistance;
        Vector3 lookTarget = movementTarget != null
            ? movementTarget.position
            : doorPivot != null ? doorPivot.position + Vector3.up * 1.2f : targetPosition + movingTransform.forward;
        Quaternion targetRotation = Quaternion.LookRotation((lookTarget - startPosition).normalized, Vector3.up);

        for (float elapsed = 0f; elapsed < cameraMoveDuration; elapsed += Time.deltaTime)
        {
            float rawT = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, cameraMoveDuration));
            float t = ExponentialEaseIn(rawT);

            movingTransform.position = Vector3.LerpUnclamped(startPosition, targetPosition, t);
            movingTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, Mathf.SmoothStep(0f, 1f, rawT));
            fadeGroup.alpha = Mathf.SmoothStep(0f, 1f, rawT);

            yield return null;
        }

        fadeGroup.alpha = 1f;
        LoadTargetScene();
    }

    private void LockPlayerInput()
    {
        if (firstPersonController != null)
        {
            firstPersonController.playerCanMove = false;
            firstPersonController.cameraCanMove = false;
            firstPersonController.enableJump = false;
            firstPersonController.SetCrouchEnabled(false);
            firstPersonController.enabled = false;
        }

        Rigidbody playerBody = firstPersonController != null ? firstPersonController.GetComponent<Rigidbody>() : null;
        if (playerBody != null)
        {
            playerBody.velocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.isKinematic = true;
        }
    }

    private float ExponentialEaseIn(float t)
    {
        if (exponentialStrength <= 0.01f)
        {
            return t;
        }

        float denominator = Mathf.Exp(exponentialStrength) - 1f;
        return denominator <= 0f ? t : (Mathf.Exp(exponentialStrength * t) - 1f) / denominator;
    }

    private bool HasDoorReachedMovePoint()
    {
        if (roomAnimator != null && roomAnimator.runtimeAnimatorController != null)
        {
            AnimatorStateInfo state = roomAnimator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextState = roomAnimator.GetNextAnimatorStateInfo(0);

            if ((state.IsName(openAnimationState) && state.normalizedTime >= movementStartNormalizedTime) ||
                (nextState.IsName(openAnimationState) && nextState.normalizedTime >= movementStartNormalizedTime))
            {
                return true;
            }

            return false;
        }

        if (doorPivot == null)
        {
            return true;
        }

        doorPivot.localRotation = Quaternion.RotateTowards(
            doorPivot.localRotation,
            openRotation,
            openSpeed * openAngle * Time.deltaTime
        );

        return Quaternion.Angle(closedRotation, doorPivot.localRotation) >= movementStartAngle;
    }

    private void PauseDoorAnimationAtCurrentFrame()
    {
        if (doorAnimationPaused || roomAnimator == null || roomAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        roomAnimator.Update(0f);
        roomAnimator.speed = 0f;
        doorAnimationPaused = true;
    }

    private void PlayMainDoorOpenSound()
    {
        if (audioSource != null && mainDoorOpenSound != null)
        {
            audioSource.PlayOneShot(mainDoorOpenSound);
        }
    }

    private void EnsureFadeOverlay()
    {
        if (fadeGroup != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Main Door Fade Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;
        fadeGroup = canvasObject.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;

        GameObject panelObject = new GameObject("Blackout");
        panelObject.transform.SetParent(canvasObject.transform, false);
        Image image = panelObject.AddComponent<Image>();
        image.color = Color.black;

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void LoadTargetScene()
    {
        SceneManager.LoadScene(targetSceneName);
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
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
}
