using UnityEngine;
using TMPro;
using System;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class DoorRaycastInteractor : MonoBehaviour
{
    public static event Action<bool> SeatedStateChanged;

    public float interactDistance = 5f;
    public KeyCode interactKey = KeyCode.E;
    public KeyCode pickupKey = KeyCode.F;
    public KeyCode slotOneKey = KeyCode.Alpha1;
    public KeyCode slotTwoKey = KeyCode.Alpha2;
    public TMP_Text interactLabel;
    public float fadeSpeed = 8f;
    public float pickupAnimationTime = 0.75f;
    public float handReachPortion = 0.45f;
    public float pickupAttachPortion = 0.82f;
    public Vector3 heldLocalPosition = new Vector3(-10f, 1.4f, -3.1f);
    public Vector3 heldLocalEulerAngles = new Vector3(8f, -15f, 0f);
    public Vector3 pickupCameraDip = new Vector3(5f, 0f, 0f);
    public float pickupCameraForwardMove = 0.12f;
    public float pickupCameraArmFollowStrength = 0.35f;
    public float pickupCameraReturnTime = 0.18f;
    public string crouchedPickupMessage = "Stand up to pickup";
    public Color warningLabelColor = Color.red;
    public float warningLabelDuration = 1f;
    [Header("Inventory")]
    public Color inventorySlotColor = new Color(0f, 0f, 0f, 0.55f);
    public Color selectedInventorySlotColor = new Color(0.1f, 0.45f, 0.95f, 0.8f);
    public Color inventoryTextColor = Color.white;
    [FormerlySerializedAs("seatedCameraWorldPosition")]
    public Vector3 seatedCameraLocalPosition = new Vector3(-0.08f, 0.896f, 0.405f);
    public Vector3 seatedCameraLocalEulerAngles = new Vector3(1.92f, -2f, 0f);
    [FormerlySerializedAs("seatedControllerWorldX")]
    public Vector3 seatedControllerWorldPosition = new Vector3(-0.19f, 0.8401712f, 1.371478f);
    public Vector3 seatedControllerWorldEulerAngles = new Vector3(0f, -180f, 0f);
    public float chairSitTime = 1f;

    private float labelAlpha;
    private Color normalLabelColor = Color.white;
    private float warningLabelTimer;
    private string warningLabelText;
    private bool isAnimatingPickup;
    private readonly PickupInteractable[] inventorySlots = new PickupInteractable[2];
    private int activeSlotIndex;
    private Transform holdPoint;
    private Transform inventoryStowPoint;
    private FirstPersonController firstPersonController;
    private FirstPersonHandsAnimatorDriver handsAnimatorDriver;
    private Rigidbody playerRigidbody;
    private bool previousPlayerCanMove;
    private bool previousCameraCanMove;
    private bool previousCrouchEnabled;
    private bool previousJumpEnabled;
    private Vector3 standingControllerWorldPosition;
    private Quaternion standingControllerWorldRotation;
    private Vector3 standingCameraLocalPosition;
    private Quaternion standingCameraLocalRotation;
    private bool isSeated;
    private bool isAnimatingChair;
    private ChairInteractable activeChair;
    private Collider[] playerColliders;
    private TMP_Text[] inventorySlotLabels;
    private Image[] inventorySlotBackgrounds;

    private PickupInteractable HeldObject => inventorySlots[activeSlotIndex];

    void Awake()
    {
        firstPersonController = GetComponentInParent<FirstPersonController>();
        playerRigidbody = firstPersonController != null ? firstPersonController.GetComponent<Rigidbody>() : null;
        playerColliders = firstPersonController != null
            ? firstPersonController.GetComponentsInChildren<Collider>()
            : GetComponentsInParent<Collider>();
        handsAnimatorDriver = firstPersonController != null
            ? firstPersonController.GetComponentInChildren<FirstPersonHandsAnimatorDriver>()
            : null;

        holdPoint = GetExistingPickupPoint();
        if (holdPoint == null)
        {
            GameObject holdPointObject = new GameObject("pickup");
            holdPoint = holdPointObject.transform;
            holdPoint.SetParent(GetPickupAnchor(), false);
        }

        ApplyHoldPointLocalPose();
        inventoryStowPoint = new GameObject("Inventory Stow").transform;
        inventoryStowPoint.SetParent(transform, false);
        inventoryStowPoint.localPosition = Vector3.zero;
        inventoryStowPoint.localRotation = Quaternion.identity;
        inventoryStowPoint.localScale = Vector3.one;
        EnsureInventoryHud();
        UpdateInventoryHud();

        if (interactLabel != null)
        {
            labelAlpha = interactLabel.color.a;
            normalLabelColor = interactLabel.color;
            if (labelAlpha <= 0f)
            {
                interactLabel.gameObject.SetActive(false);
            }
        }

        standingControllerWorldPosition = GetControllerPosition();
        standingControllerWorldRotation = GetControllerRotation();
        standingCameraLocalPosition = transform.localPosition;
    }

    void Update()
    {
        if (warningLabelTimer > 0f)
        {
            warningLabelTimer -= Time.deltaTime;
        }

        HandleInventorySwitchInput();

        bool shouldShowLabel = false;
        string prompt = string.Empty;

        if (isSeated)
        {
            shouldShowLabel = true;
            bool terminalCapturing = MonitorYoutubeDesktop.TerminalCaptureActive;
            prompt = terminalCapturing ? "Type 'exit' to leave the terminal" : "Press Space to stand up";

            if (!isAnimatingChair && !terminalCapturing && Input.GetKeyDown(KeyCode.Space))
            {
                StartCoroutine(StandFromChairRoutine());
            }

            UpdateInteractLabel(shouldShowLabel, prompt);
            return;
        }

        PickupInteractable heldObject = HeldObject;

        Ray ray = new Ray(transform.position, transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, interactDistance);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        DoorController door = null;
        float doorDistance = 0f;
        MainDoorSceneTransition sceneDoor = null;
        float sceneDoorDistance = 0f;
        GunMagazinePickup magazine = null;
        float magazineDistance = 0f;
        PickupInteractable pickup = null;
        ChairInteractable chair = null;
        SimpleInteractable simple = null;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            if (simple == null)
            {
                SimpleInteractable hitSimple = hit.collider.GetComponentInParent<SimpleInteractable>();
                if (hitSimple != null && hitSimple.CanInteract && IsWithinInteractionDistance(hit.distance, hitSimple.InteractionDistance))
                {
                    simple = hitSimple;
                }
            }

            if (door == null)
            {
                DoorController hitDoor = hit.collider.GetComponentInParent<DoorController>();
                if (hitDoor != null && IsWithinInteractionDistance(hit.distance, hitDoor.InteractionDistance))
                {
                    door = hitDoor;
                    doorDistance = hit.distance;
                }
            }

            if (sceneDoor == null)
            {
                MainDoorSceneTransition hitSceneDoor = hit.collider.GetComponentInParent<MainDoorSceneTransition>();
                if (hitSceneDoor != null && IsWithinInteractionDistance(hit.distance, hitSceneDoor.InteractionDistance))
                {
                    sceneDoor = hitSceneDoor;
                    sceneDoorDistance = hit.distance;
                }
            }

            if (magazine == null)
            {
                GunMagazinePickup hitMagazine = hit.collider.GetComponentInParent<GunMagazinePickup>();
                if (hitMagazine != null && IsWithinInteractionDistance(hit.distance, hitMagazine.InteractionDistance))
                {
                    magazine = hitMagazine;
                    magazineDistance = hit.distance;
                }
            }

            if (pickup == null)
            {
                PickupInteractable hitPickup = hit.collider.GetComponentInParent<PickupInteractable>();
                if (hitPickup != null && hitPickup != heldObject && IsWithinInteractionDistance(hit.distance, hitPickup.InteractionDistance))
                {
                    pickup = hitPickup;
                }
            }

            if (chair == null)
            {
                ChairInteractable hitChair = hit.collider.GetComponentInParent<ChairInteractable>();
                if (hitChair != null && IsWithinInteractionDistance(hit.distance, hitChair.InteractionDistance))
                {
                    chair = hitChair;
                }
            }
        }

        if (sceneDoor != null && Input.GetKeyDown(interactKey))
        {
            sceneDoor.BeginTransition();
        }
        else if (door != null && Input.GetKeyDown(interactKey))
        {
            door.ToggleDoor();
        }

        GunWeaponController heldGun = heldObject != null ? heldObject.GetComponent<GunWeaponController>() : null;

        float nearestDoorDistance = sceneDoor != null ? sceneDoorDistance : doorDistance;
        bool hasDoorPrompt = sceneDoor != null || door != null;

        if (magazine != null && (!hasDoorPrompt || magazineDistance <= nearestDoorDistance || !Input.GetKeyDown(interactKey)))
        {
            shouldShowLabel = true;
            prompt = heldGun != null ? "Press F to take magazine" : "Hold gun to take magazine";

            if (!isAnimatingPickup && Input.GetKeyDown(pickupKey))
            {
                if (heldGun != null)
                {
                    if (!magazine.TryPickup(heldGun))
                    {
                        ShowWarning("Magazines full");
                    }
                }
                else
                {
                    ShowWarning("Hold gun to take magazine");
                }
            }
        }
        else if (sceneDoor != null)
        {
            shouldShowLabel = true;
            prompt = sceneDoor.Prompt;
        }
        else if (door != null)
        {
            shouldShowLabel = true;
            prompt = door.IsOpen ? "Press E to close" : "Press E to open";
        }
        else if (simple != null)
        {
            shouldShowLabel = true;
            prompt = simple.Prompt;

            if (!isAnimatingPickup && Input.GetKeyDown(interactKey))
            {
                simple.Interact();
            }
        }
        else if (chair != null && heldObject == null)
        {
            shouldShowLabel = true;
            prompt = "Press E to sit";

            if (!isAnimatingChair && Input.GetKeyDown(interactKey))
            {
                StartCoroutine(SitInChairRoutine(chair));
            }
        }
        else if (pickup != null)
        {
            shouldShowLabel = true;
            int slotIndex = GetPickupSlotIndex();
            prompt = slotIndex < 0 ? "Inventory full" : "Press F to pickup";

            if (!isAnimatingPickup && Input.GetKeyDown(pickupKey))
            {
                if (firstPersonController != null && firstPersonController.IsCrouched)
                {
                    ShowWarning(crouchedPickupMessage);
                }
                else
                {
                    if (slotIndex < 0)
                    {
                        ShowWarning("Inventory full");
                    }
                    else
                    {
                        StartCoroutine(PickupRoutine(pickup, slotIndex));
                    }
                }
            }
        }

        if (heldObject != null && !shouldShowLabel)
        {
            shouldShowLabel = true;
            prompt = "Press F to drop";

            if (!isAnimatingPickup && Input.GetKeyDown(pickupKey))
            {
                DropActiveSlot();
            }
        }

        UpdateInteractLabel(shouldShowLabel, prompt);
    }

    private bool IsWithinInteractionDistance(float hitDistance, float objectInteractionDistance)
    {
        float distance = objectInteractionDistance > 0f ? objectInteractionDistance : interactDistance;
        return hitDistance <= distance;
    }

    private void HandleInventorySwitchInput()
    {
        if (isAnimatingPickup || isAnimatingChair)
        {
            return;
        }

        if (Input.GetKeyDown(slotOneKey))
        {
            SwitchInventorySlot(0);
        }
        else if (Input.GetKeyDown(slotTwoKey))
        {
            SwitchInventorySlot(1);
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            SwitchInventorySlot(scroll > 0f ? PreviousSlotIndex() : NextSlotIndex());
        }
    }

    private int NextSlotIndex()
    {
        return (activeSlotIndex + 1) % inventorySlots.Length;
    }

    private int PreviousSlotIndex()
    {
        return (activeSlotIndex + inventorySlots.Length - 1) % inventorySlots.Length;
    }

    private void SwitchInventorySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= inventorySlots.Length || slotIndex == activeSlotIndex)
        {
            return;
        }

        PickupInteractable previousObject = HeldObject;
        if (previousObject != null)
        {
            previousObject.StowInInventory(inventoryStowPoint);
        }

        activeSlotIndex = slotIndex;
        PickupInteractable nextObject = HeldObject;
        if (nextObject != null)
        {
            nextObject.EquipFromInventory(holdPoint);
            ApplyHeldObjectHandPose(nextObject);
        }
        else if (handsAnimatorDriver != null)
        {
            handsAnimatorDriver.SetHeldGunPose(false);
            handsAnimatorDriver.SetMoveFingersWhileWalking(true);
        }

        UpdateInventoryHud();
    }

    private int GetPickupSlotIndex()
    {
        if (HeldObject == null)
        {
            return activeSlotIndex;
        }

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] == null)
            {
                return i;
            }
        }

        return -1;
    }

    private void DropActiveSlot()
    {
        PickupInteractable heldObject = HeldObject;
        if (heldObject == null)
        {
            return;
        }

        if (handsAnimatorDriver != null)
        {
            handsAnimatorDriver.ReleasePickupReach();
            handsAnimatorDriver.SetHeldGunPose(false);
            handsAnimatorDriver.SetMoveFingersWhileWalking(true);
        }

        heldObject.Drop(transform.forward);
        inventorySlots[activeSlotIndex] = null;
        UpdateInventoryHud();
    }

    private void ApplyHeldObjectHandPose(PickupInteractable pickup)
    {
        if (handsAnimatorDriver == null || pickup == null)
        {
            return;
        }

        bool isGun = pickup.GetComponent<GunWeaponController>() != null;
        handsAnimatorDriver.SetMoveFingersWhileWalking(pickup.MoveFingersWhileWalking);
        handsAnimatorDriver.SetHeldGunPose(isGun);
    }

    private IEnumerator SitInChairRoutine(ChairInteractable chair)
    {
        isAnimatingChair = true;
        activeChair = chair;
        standingControllerWorldPosition = GetControllerPosition();
        standingControllerWorldRotation = GetControllerRotation();
        standingCameraLocalPosition = transform.localPosition;
        standingCameraLocalRotation = transform.localRotation;
        SetPlayerControl(false);
        SetChairPlayerCollisionIgnored(activeChair, true);

        Quaternion seatedRotation = Quaternion.Euler(seatedCameraLocalEulerAngles);
        Quaternion seatedControllerRotation = Quaternion.Euler(seatedControllerWorldEulerAngles);
        SetControllerPose(seatedControllerWorldPosition, seatedControllerRotation);
        yield return MoveCameraLocalPose(standingCameraLocalPosition, seatedCameraLocalPosition, chairSitTime, standingCameraLocalRotation, seatedRotation);

        SetControllerPose(seatedControllerWorldPosition, seatedControllerRotation);
        transform.localPosition = seatedCameraLocalPosition;
        transform.localEulerAngles = seatedCameraLocalEulerAngles;
        isSeated = true;
        SeatedStateChanged?.Invoke(true);
        isAnimatingChair = false;
    }

    private IEnumerator StandFromChairRoutine()
    {
        isAnimatingChair = true;

        yield return MoveCameraLocalPose(transform.localPosition, standingCameraLocalPosition, chairSitTime, transform.localRotation, standingCameraLocalRotation);

        SetControllerPose(standingControllerWorldPosition, standingControllerWorldRotation);
        transform.localPosition = standingCameraLocalPosition;
        transform.localRotation = standingCameraLocalRotation;
        isSeated = false;
        SeatedStateChanged?.Invoke(false);
        SetChairPlayerCollisionIgnored(activeChair, false);
        activeChair = null;
        SetPlayerControl(true);
        isAnimatingChair = false;
    }

    private IEnumerator MoveCameraLocalPose(Vector3 from, Vector3 to, float duration, Quaternion fromRotation, Quaternion toRotation)
    {
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration));
            transform.localPosition = Vector3.Lerp(from, to, t);
            transform.localRotation = Quaternion.Slerp(fromRotation, toRotation, t);
            yield return null;
        }

        transform.localPosition = to;
        transform.localRotation = toRotation;
    }

    private Vector3 GetControllerPosition()
    {
        return firstPersonController != null ? firstPersonController.transform.position : transform.position;
    }

    private Quaternion GetControllerRotation()
    {
        return firstPersonController != null ? firstPersonController.transform.rotation : transform.rotation;
    }

    private void SetControllerPosition(Vector3 position)
    {
        SetControllerPose(position, GetControllerRotation());
    }

    private void SetControllerPose(Vector3 position, Quaternion rotation)
    {
        if (firstPersonController == null)
        {
            transform.position = position;
            transform.rotation = rotation;
            return;
        }

        firstPersonController.transform.position = position;
        firstPersonController.transform.rotation = rotation;
        if (playerRigidbody != null)
        {
            playerRigidbody.position = position;
            playerRigidbody.rotation = rotation;
            playerRigidbody.velocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void SetChairPlayerCollisionIgnored(ChairInteractable chair, bool ignored)
    {
        if (chair == null || chair.Colliders == null || playerColliders == null)
        {
            return;
        }

        foreach (Collider playerCollider in playerColliders)
        {
            if (playerCollider == null)
            {
                continue;
            }

            foreach (Collider chairCollider in chair.Colliders)
            {
                if (chairCollider == null || chairCollider == playerCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(playerCollider, chairCollider, ignored);
            }
        }
    }

    private IEnumerator PickupRoutine(PickupInteractable pickup, int slotIndex)
    {
        isAnimatingPickup = true;
        if (slotIndex != activeSlotIndex)
        {
            SwitchInventorySlot(slotIndex);
        }

        inventorySlots[slotIndex] = pickup;
        UpdateInventoryHud();
        SetPlayerControl(false);

        Vector3 startPosition = pickup.transform.position;
        Vector3 centerPoint = pickup.GetCenterPoint();
        Quaternion startRotation = pickup.transform.rotation;
        Vector3 startCameraPosition = transform.position;
        Quaternion startCameraRotation = transform.localRotation;
        Quaternion dipCameraRotation = startCameraRotation * Quaternion.Euler(pickupCameraDip);
        Vector3 startHandAnchorWorldPosition = Vector3.zero;
        Transform handAnchor = GetPickupAnchor();
        bool isGun = pickup.GetComponent<GunWeaponController>() != null;

        if (handAnchor != null)
        {
            startHandAnchorWorldPosition = handAnchor.position;
            if (holdPoint.parent != handAnchor)
            {
                holdPoint.SetParent(handAnchor, false);
            }

            ApplyHoldPointLocalPose();
        }

        pickup.BeginPickup();

        if (handsAnimatorDriver != null)
        {
            handsAnimatorDriver.SetMoveFingersWhileWalking(pickup.MoveFingersWhileWalking);
            handsAnimatorDriver.SetHeldGunPose(isGun);
            handsAnimatorDriver.BeginPickupReach(centerPoint, transform, pickupAnimationTime, pickupCameraForwardMove * 0.5f);
        }

        float elapsed = 0f;
        bool objectAttached = false;
        while (elapsed < pickupAnimationTime)
        {
            elapsed += Time.deltaTime;
            float rawT = Mathf.Clamp01(elapsed / pickupAnimationTime);
            float objectT = Mathf.InverseLerp(handReachPortion, 1f, rawT);
            float objectSmoothT = Mathf.SmoothStep(0f, 1f, objectT);

            if (!objectAttached)
            {
                pickup.transform.position = Vector3.Lerp(startPosition, holdPoint.position, objectSmoothT);
                pickup.transform.rotation = Quaternion.Slerp(startRotation, holdPoint.rotation, objectSmoothT);
            }

            if (!objectAttached && rawT >= pickupAttachPortion)
            {
                pickup.AttachTo(holdPoint);
                objectAttached = true;
            }

            Vector3 cameraFollowOffset = Vector3.zero;
            if (handAnchor != null)
            {
                Vector3 handAnchorDeltaWorld = handAnchor.position - startHandAnchorWorldPosition;
                cameraFollowOffset = handAnchorDeltaWorld * pickupCameraArmFollowStrength;
            }

            transform.position = startCameraPosition
                + cameraFollowOffset
                + transform.forward * (Mathf.Sin(rawT * Mathf.PI) * pickupCameraForwardMove);
            transform.localRotation = Quaternion.Slerp(startCameraRotation, dipCameraRotation, Mathf.Sin(rawT * Mathf.PI));

            yield return null;
        }

        if (handsAnimatorDriver != null)
        {
            handsAnimatorDriver.ReleasePickupReach(0.18f, true);
            handsAnimatorDriver.SetHeldGunPose(isGun);

            while (handsAnimatorDriver.IsPickupReachActive)
            {
                yield return null;
            }
        }

        Vector3 returnStartPosition = transform.position;
        Quaternion returnStartRotation = transform.localRotation;
        float returnElapsed = 0f;
        float returnDuration = Mathf.Max(0.01f, pickupCameraReturnTime);

        while (returnElapsed < returnDuration)
        {
            returnElapsed += Time.deltaTime;
            float returnT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(returnElapsed / returnDuration));
            transform.position = Vector3.Lerp(returnStartPosition, startCameraPosition, returnT);
            transform.localRotation = Quaternion.Slerp(returnStartRotation, startCameraRotation, returnT);
            yield return null;
        }

        transform.position = startCameraPosition;
        transform.localRotation = startCameraRotation;

        if (!objectAttached)
        {
            pickup.AttachTo(holdPoint);
        }

        SetPlayerControl(true);
        isAnimatingPickup = false;
        UpdateInventoryHud();
    }

    private Transform GetPickupAnchor()
    {
        if (handsAnimatorDriver != null && handsAnimatorDriver.RightWristTransform != null)
        {
            return handsAnimatorDriver.RightWristTransform;
        }

        return transform;
    }

    private void ApplyHoldPointLocalPose()
    {
        if (holdPoint == null)
        {
            return;
        }

        holdPoint.localPosition = heldLocalPosition;
        holdPoint.localEulerAngles = heldLocalEulerAngles;
        holdPoint.localScale = Vector3.one;
    }

    private Transform GetExistingPickupPoint()
    {
        Transform wrist = handsAnimatorDriver != null ? handsAnimatorDriver.RightWristTransform : null;
        if (wrist == null)
        {
            return null;
        }

        Transform existingPickup = FindChildByName(wrist, "pickup");
        return existingPickup != null ? existingPickup : FindChildByName(wrist, "PickupHoldPoint");
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

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

    private void SetPlayerControl(bool enabled)
    {
        if (firstPersonController == null)
        {
            return;
        }

        if (!enabled)
        {
            previousPlayerCanMove = firstPersonController.playerCanMove;
            previousCameraCanMove = firstPersonController.cameraCanMove;
            previousCrouchEnabled = firstPersonController.enableCrouch;
            previousJumpEnabled = firstPersonController.enableJump;

            if (firstPersonController.IsCrouched)
            {
                firstPersonController.ForceUncrouch();
            }

            firstPersonController.SetCrouchEnabled(false);
            firstPersonController.enableJump = false;
            firstPersonController.playerCanMove = false;
            firstPersonController.cameraCanMove = false;

            if (playerRigidbody != null)
            {
                playerRigidbody.velocity = Vector3.zero;
                playerRigidbody.angularVelocity = Vector3.zero;
            }

            return;
        }

        firstPersonController.playerCanMove = previousPlayerCanMove;
        firstPersonController.cameraCanMove = previousCameraCanMove;
        firstPersonController.SetCrouchEnabled(previousCrouchEnabled);
        firstPersonController.enableJump = previousJumpEnabled;
    }

    private void EnsureInventoryHud()
    {
        if (inventorySlotLabels != null && inventorySlotBackgrounds != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Gameplay Inventory Canvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject rootObject = new GameObject("Inventory HUD");
        rootObject.transform.SetParent(canvas.transform, false);

        RectTransform root = rootObject.AddComponent<RectTransform>();
        root.anchorMin = new Vector2(0f, 0.5f);
        root.anchorMax = new Vector2(0f, 0.5f);
        root.pivot = new Vector2(0f, 0.5f);
        root.anchoredPosition = new Vector2(24f, 0f);
        root.sizeDelta = new Vector2(124f, 156f);

        VerticalLayoutGroup layout = rootObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        inventorySlotLabels = new TMP_Text[inventorySlots.Length];
        inventorySlotBackgrounds = new Image[inventorySlots.Length];

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            GameObject slotObject = new GameObject("Inventory Slot " + (i + 1));
            slotObject.transform.SetParent(root, false);

            RectTransform slotRect = slotObject.AddComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(124f, 72f);

            Image background = slotObject.AddComponent<Image>();
            background.color = inventorySlotColor;
            inventorySlotBackgrounds[i] = background;

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(slotObject.transform, false);

            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 6f);
            labelRect.offsetMax = new Vector2(-8f, -6f);

            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            label.color = inventoryTextColor;
            inventorySlotLabels[i] = label;

            GameObject numberObject = new GameObject("Slot Number");
            numberObject.transform.SetParent(slotObject.transform, false);

            RectTransform numberRect = numberObject.AddComponent<RectTransform>();
            numberRect.anchorMin = new Vector2(0f, 1f);
            numberRect.anchorMax = new Vector2(0f, 1f);
            numberRect.pivot = new Vector2(0f, 1f);
            numberRect.anchoredPosition = new Vector2(8f, -5f);
            numberRect.sizeDelta = new Vector2(28f, 22f);

            TextMeshProUGUI numberLabel = numberObject.AddComponent<TextMeshProUGUI>();
            numberLabel.text = (i + 1).ToString();
            numberLabel.fontSize = 14f;
            numberLabel.alignment = TextAlignmentOptions.TopLeft;
            numberLabel.raycastTarget = false;
            numberLabel.color = inventoryTextColor;
        }
    }

    private void UpdateInventoryHud()
    {
        EnsureInventoryHud();

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlotBackgrounds != null && inventorySlotBackgrounds[i] != null)
            {
                inventorySlotBackgrounds[i].color = i == activeSlotIndex ? selectedInventorySlotColor : inventorySlotColor;
            }

            if (inventorySlotLabels == null || inventorySlotLabels[i] == null)
            {
                continue;
            }

            inventorySlotLabels[i].text = inventorySlots[i] != null ? CleanInventoryName(inventorySlots[i].DisplayName) : string.Empty;
        }
    }

    // --- public inventory access for quest logic / cross-scene carry-over ---

    public string[] GetCarriedItemNames()
    {
        var names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] != null)
            {
                names.Add(CleanInventoryName(inventorySlots[i].DisplayName));
            }
        }

        return names.ToArray();
    }

    public bool HasItemNamed(string cleanName)
    {
        return FindSlotByName(cleanName) >= 0;
    }

    // Removes the item from the inventory and returns it re-enabled in the
    // world (parent cleared, renderers on, kinematic) so quest code can hand
    // it to an NPC or animate it.
    public PickupInteractable TakeItemNamed(string cleanName)
    {
        int slot = FindSlotByName(cleanName);
        if (slot < 0)
        {
            return null;
        }

        PickupInteractable item = inventorySlots[slot];
        inventorySlots[slot] = null;

        if (slot == activeSlotIndex && handsAnimatorDriver != null)
        {
            handsAnimatorDriver.ReleasePickupReach();
            handsAnimatorDriver.SetHeldGunPose(false);
            handsAnimatorDriver.SetMoveFingersWhileWalking(true);
        }

        item.Drop(Vector3.zero);
        Rigidbody body = item.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.isKinematic = true;
        }

        UpdateInventoryHud();
        return item;
    }

    public bool GiveItem(PickupInteractable item)
    {
        if (item == null)
        {
            return false;
        }

        int slot = GetPickupSlotIndex();
        if (slot < 0)
        {
            return false;
        }

        inventorySlots[slot] = item;
        item.BeginPickup();

        if (slot == activeSlotIndex)
        {
            item.AttachTo(holdPoint);
            ApplyHeldObjectHandPose(item);
        }
        else
        {
            item.StowInInventory(inventoryStowPoint);
        }

        UpdateInventoryHud();
        return true;
    }

    // Cheat helper: force an item straight into the active hand slot, dropping
    // whatever is there so it's actually equipped (and correctly posed/scaled)
    // instead of left floating in the world.
    public bool ForceEquipItem(PickupInteractable item)
    {
        if (item == null)
        {
            return false;
        }

        if (inventorySlots[activeSlotIndex] != null)
        {
            DropActiveSlot();
        }

        return GiveItem(item);
    }

    private int FindSlotByName(string cleanName)
    {
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            if (inventorySlots[i] != null &&
                CleanInventoryName(inventorySlots[i].DisplayName).Equals(cleanName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string CleanInventoryName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "Object";
        }

        return rawName.Replace("(Clone)", string.Empty).Trim();
    }

    private void ShowWarning(string message)
    {
        warningLabelText = message;
        warningLabelTimer = warningLabelDuration;
        labelAlpha = 1f;

        if (interactLabel == null)
        {
            return;
        }

        interactLabel.gameObject.SetActive(true);
        interactLabel.text = warningLabelText;
        Color color = warningLabelColor;
        color.a = labelAlpha;
        interactLabel.color = color;
    }

    private void UpdateInteractLabel(bool shouldShowLabel, string prompt)
    {
        if (interactLabel != null)
        {
            bool warningActive = warningLabelTimer > 0f;
            if (warningActive)
            {
                shouldShowLabel = true;
                prompt = warningLabelText;
            }

            if (!string.IsNullOrEmpty(prompt))
            {
                interactLabel.text = prompt;
            }

            if (shouldShowLabel && !interactLabel.gameObject.activeSelf)
            {
                interactLabel.gameObject.SetActive(true);
            }

            float targetAlpha = shouldShowLabel ? 1f : 0f;
            labelAlpha = Mathf.MoveTowards(labelAlpha, targetAlpha, fadeSpeed * Time.deltaTime);

            Color color = warningActive ? warningLabelColor : normalLabelColor;
            color.a = labelAlpha;
            interactLabel.color = color;

            if (!shouldShowLabel && labelAlpha <= 0.001f)
            {
                interactLabel.gameObject.SetActive(false);
            }
        }
    }
}
