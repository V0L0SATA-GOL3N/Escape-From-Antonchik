using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FirstPersonHandsAnimatorDriver : MonoBehaviour
{
    private static readonly int MoveAmountHash = Animator.StringToHash("MoveAmount");

    [SerializeField] private float inputDeadZone = 0.08f;
    [SerializeField] private float animatorDampTime = 0.16f;
    [SerializeField] private float walkFrequency = 7.5f;
    [SerializeField] private float armBackSwingDegrees = -10f;
    [SerializeField] private float armForwardSwingDegrees = 0f;
    [SerializeField] private float fingerOpenDegrees = 18f;
    [SerializeField] private float pickupWristStopDistance = 0.05f;
    [SerializeField] private float pickupMaxWristWorldMove = 0.55f;
    [SerializeField] private Vector3 heldGunWristEulerOffset = new Vector3(0f, 0f, 24f);
    [SerializeField] private Vector3 heldGunThumbEulerOffset = new Vector3(0f, -40f, 0f);
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float cameraVerticalInfluence = 0.5f;

    private Animator animator;
    private float smoothedMoveAmount;
    private float walkTimer;
    private float pickupReachBlend;
    private float pickupReachTarget;
    private float pickupReachSpeed = 6f;
    private Vector3 pickupLocalDirection = Vector3.forward;
    private Vector3 pickupWorldTarget;
    private float pickupDistance01;
    private float pickupHandForwardOffset;
    private bool keepPickupFingersClosed;
    private bool keepGunPose;
    private bool moveFingersWhileWalking = true;

    private Transform chest;
    private Transform leftArm;
    private Transform leftElbow;
    private Transform leftWrist;
    private Transform rightArm;
    private Transform rightElbow;
    private Transform rightWrist;
    private Transform rightPalm;

    private Quaternion chestBaseRotation;
    private Vector3 chestBasePosition;
    private BonePose leftArmBase;
    private BonePose leftElbowBase;
    private BonePose leftWristBase;
    private BonePose rightArmBase;
    private BonePose rightElbowBase;
    private BonePose rightWristBase;
    private BonePose[] leftFingerBases;
    private BonePose[] rightFingerBases;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        CacheBones();
    }

    public Transform RightWristTransform => rightWrist;
    public Transform RightPalmTransform => rightPalm != null ? rightPalm : rightWrist;
    public bool IsPickupReachActive => pickupReachTarget > 0f || pickupReachBlend > 0.001f;

    private void Update()
    {
        Vector2 movementInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        float moveAmount = movementInput.magnitude > inputDeadZone ? Mathf.Clamp01(movementInput.magnitude) : 0f;
        smoothedMoveAmount = Mathf.MoveTowards(smoothedMoveAmount, moveAmount, Time.deltaTime / Mathf.Max(0.01f, animatorDampTime));
        walkTimer += Time.deltaTime * walkFrequency * Mathf.Lerp(0.35f, 1f, smoothedMoveAmount);

        animator.SetFloat(MoveAmountHash, moveAmount, animatorDampTime, Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (chest == null)
        {
            return;
        }

        float walkBlend = Mathf.SmoothStep(0f, 1f, smoothedMoveAmount);
        float idleBreath = Mathf.Sin(Time.time * 1.65f) * (1f - walkBlend);
        float step = Mathf.Sin(walkTimer) * walkBlend;
        float oppositeStep = -step;
        float bounce = Mathf.Abs(Mathf.Sin(walkTimer)) * walkBlend;
        float leftArmX = Mathf.Lerp(armBackSwingDegrees, armForwardSwingDegrees, (step + 1f) * 0.5f) * walkBlend;
        float rightArmX = Mathf.Lerp(armBackSwingDegrees, armForwardSwingDegrees, (oppositeStep + 1f) * 0.5f) * walkBlend;
        float rightHandWalkBlend = keepGunPose ? 0f : walkBlend;
        float rightHandArmX = keepGunPose ? 0f : rightArmX;

        chest.localPosition = chestBasePosition + new Vector3(0f, idleBreath * 0.012f + bounce * 0.035f, -bounce * 0.018f);
        chest.localRotation = chestBaseRotation * Quaternion.Euler(idleBreath * 0.35f + bounce * 0.7f, step * 0.25f, 0f);

        // Get camera pitch (clamped to avoid flipping)
float cameraPitch = 0f;
if (cameraTransform != null)
{
    cameraPitch = cameraTransform.localEulerAngles.x;
    if (cameraPitch > 180f) cameraPitch -= 360f; // convert 270 → -90 etc.
}
float pitchOffset = cameraPitch * cameraVerticalInfluence;

ApplyPose(leftArmBase,  new Vector3(leftArmX  + pitchOffset, 0f, 0f));
ApplyPose(rightArmBase, new Vector3(rightHandArmX + (keepGunPose ? pitchOffset : pitchOffset), 0f, 0f));
ApplyPose(leftWristBase,  new Vector3(rightArmX * 0.35f + pitchOffset * 0.4f, 0f, 0f));
ApplyPose(rightWristBase, new Vector3((keepGunPose ? 0f : leftArmX) * 0.35f + pitchOffset * 0.4f, 0f, 0f));

        ApplyPose(leftElbowBase, Vector3.zero);
        ApplyPose(rightElbowBase, Vector3.zero);

        ApplyPose(leftWristBase, new Vector3(rightArmX * 0.35f, 0f, 0f));
        ApplyPose(rightWristBase, new Vector3((keepGunPose ? 0f : leftArmX) * 0.35f, 0f, 0f));
        ApplyPosition(rightWristBase, Vector3.zero);

        if (moveFingersWhileWalking)
        {
            ApplyFingerOpenPose(leftFingerBases, walkBlend, 1f);
            ApplyFingerOpenPose(rightFingerBases, rightHandWalkBlend, -1f);
        }

        ApplyPickupReachPose();
        ApplyHeldGunPose();
    }

    public void BeginPickupReach(Vector3 worldTarget, Transform cameraTransform, float reachTime, float handForwardOffset)
    {
        Vector3 localTarget = cameraTransform != null
            ? cameraTransform.InverseTransformPoint(worldTarget)
            : transform.InverseTransformPoint(worldTarget);

        pickupLocalDirection = localTarget.sqrMagnitude > 0.0001f ? localTarget.normalized : Vector3.forward;
        pickupWorldTarget = worldTarget;
        pickupDistance01 = Mathf.Clamp01(localTarget.magnitude / 4f);
        pickupReachTarget = 1f;
        pickupReachSpeed = 1f / Mathf.Max(0.05f, reachTime * 0.45f);
        pickupHandForwardOffset = handForwardOffset;
        keepPickupFingersClosed = false;
    }

    public void ReleasePickupReach(float releaseTime = 0.2f, bool keepFingersClosed = false)
    {
        pickupReachTarget = 0f;
        pickupReachSpeed = 1f / Mathf.Max(0.05f, releaseTime);
        keepPickupFingersClosed = keepFingersClosed;
    }

    public void SetHeldGunPose(bool active)
    {
        keepGunPose = active;
        keepPickupFingersClosed = active;
    }

    public void SetMoveFingersWhileWalking(bool enabled)
    {
        moveFingersWhileWalking = enabled;
    }

    private void CacheBones()
    {
        chest = FindDeepChild(transform, "chest_01");
        leftArm = FindDeepChild(transform, "L_arm_02");
        leftElbow = FindDeepChild(transform, "L_elbow_03");
        leftWrist = FindDeepChild(transform, "L_wrist_04");
        rightArm = FindDeepChild(transform, "R_arm_025");
        rightElbow = FindDeepChild(transform, "R_elbow_026");
        rightWrist = FindDeepChild(transform, "R_wrist_027");
        rightPalm = FindDeepChild(transform, "R_palm_040");

        if (chest == null)
        {
            Debug.LogWarning("FirstPersonHandsAnimatorDriver could not find chest_01.", this);
            return;
        }

        chestBaseRotation = chest.localRotation;
        chestBasePosition = chest.localPosition;
        leftArmBase = new BonePose(leftArm);
        leftElbowBase = new BonePose(leftElbow);
        leftWristBase = new BonePose(leftWrist);
        rightArmBase = new BonePose(rightArm);
        rightElbowBase = new BonePose(rightElbow);
        rightWristBase = new BonePose(rightWrist);

        leftFingerBases = new[]
        {
            new BonePose(FindDeepChild(transform, "L_thumb1_05")),
            new BonePose(FindDeepChild(transform, "L_thumb2_06")),
            new BonePose(FindDeepChild(transform, "L_point1_09")),
            new BonePose(FindDeepChild(transform, "L_point2_00")),
            new BonePose(FindDeepChild(transform, "L_middle1_012")),
            new BonePose(FindDeepChild(transform, "L_middle2_013")),
            new BonePose(FindDeepChild(transform, "L_ring1_017")),
            new BonePose(FindDeepChild(transform, "L_ring2_018")),
            new BonePose(FindDeepChild(transform, "L_pink1_021")),
            new BonePose(FindDeepChild(transform, "L_pink2_022")),
        };

        rightFingerBases = new[]
        {
            new BonePose(FindDeepChild(transform, "R_thumb1_028")),
            new BonePose(FindDeepChild(transform, "R_thumb2_029")),
            new BonePose(FindDeepChild(transform, "R_point1_032")),
            new BonePose(FindDeepChild(transform, "R_point2_033")),
            new BonePose(FindDeepChild(transform, "R_middle1_036")),
            new BonePose(FindDeepChild(transform, "R_middle2_037")),
            new BonePose(FindDeepChild(transform, "R_ring1_041")),
            new BonePose(FindDeepChild(transform, "R_ring2_042")),
            new BonePose(FindDeepChild(transform, "R_pink1_045")),
            new BonePose(FindDeepChild(transform, "R_pink2_046")),
        };
    }

    private void ApplyPose(BonePose pose, Vector3 eulerOffset)
    {
        if (!pose.IsValid)
        {
            return;
        }

        pose.Transform.localRotation = pose.BaseRotation * Quaternion.Euler(eulerOffset);
    }

    private void ApplyPosition(BonePose pose, Vector3 localOffset)
    {
        if (!pose.IsValid)
        {
            return;
        }

        pose.Transform.localPosition = pose.BasePosition + localOffset;
    }

    private void ApplyFingerOpenPose(BonePose[] fingers, float openAmount, float side)
    {
        if (fingers == null)
        {
            return;
        }

        for (int i = 0; i < fingers.Length; i++)
        {
            if (!fingers[i].IsValid)
            {
                continue;
            }

            float stagger = 1f + (i % 2) * 0.35f;
            float openDegrees = openAmount * fingerOpenDegrees * stagger;
            Vector3 offset = new Vector3(openDegrees, side * openDegrees * 0.18f, side * openDegrees * 0.12f);
            fingers[i].Transform.localRotation = fingers[i].BaseRotation * Quaternion.Euler(offset);
        }
    }

    private void ApplyPickupReachPose()
    {

        pickupReachBlend = Mathf.MoveTowards(pickupReachBlend, pickupReachTarget, Time.deltaTime * pickupReachSpeed);

        if (pickupReachBlend <= 0.001f)
        {
            return;
        }

        float reach = Mathf.SmoothStep(0f, 1f, pickupReachBlend);
        float horizontalAim = Mathf.Clamp(pickupLocalDirection.x, -0.9f, 0.9f);
        float verticalAim = Mathf.Clamp(pickupLocalDirection.y, -0.7f, 0.7f);
        float distanceReach = Mathf.Lerp(3f, 10f, pickupDistance01);
        float fingerOpen = keepPickupFingersClosed ? 0f : 1f - Mathf.SmoothStep(0.72f, 1f, reach);
        float reachX = Mathf.Clamp(6f + distanceReach - verticalAim * 4f, 0f, 16f) * reach;

        ApplyPose(rightArmBase, new Vector3(
            reachX,
            horizontalAim * 12f * reach,
            -horizontalAim * 6f * reach));
        ApplyPosition(rightArmBase, GetPickupReachLocalOffset(reach));
        ApplyPose(rightElbowBase, Vector3.zero);
        ApplyPose(rightWristBase, new Vector3(
            reachX * 0.25f,
            horizontalAim * 8f * reach,
            -4f * reach));
        ApplyFingerOpenPose(rightFingerBases, fingerOpen, -1f);
    }

    private Vector3 GetPickupReachLocalOffset(float reach)
    {
        if (!rightArmBase.IsValid || rightArmBase.Transform.parent == null || !rightWristBase.IsValid)
        {
            return Vector3.forward * pickupHandForwardOffset * reach;
        }

        Transform armParent = rightArmBase.Transform.parent;
        Vector3 currentWristWorldPosition = rightWristBase.Transform.position;
        Vector3 toTarget = pickupWorldTarget - currentWristWorldPosition;

        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        Vector3 desiredWorldPosition = pickupWorldTarget - toTarget.normalized * pickupWristStopDistance;
        Vector3 worldOffset = desiredWorldPosition - currentWristWorldPosition;

        if (worldOffset.magnitude > pickupMaxWristWorldMove)
        {
            worldOffset = worldOffset.normalized * pickupMaxWristWorldMove;
        }

        return armParent.InverseTransformVector(worldOffset + Vector3.forward * pickupHandForwardOffset) * reach;
    }

    private void ApplyHeldGunPose()
    {
        if (!keepGunPose)
        {
            return;
        }
        ApplyPose(rightArmBase, new Vector3(5f, 0f, 13.3f));
        ApplyPose(rightWristBase, heldGunWristEulerOffset);
        ApplyThumbPoseOffset(rightFingerBases, heldGunThumbEulerOffset);
    }

    private void ApplyThumbPoseOffset(BonePose[] fingers, Vector3 eulerOffset)
    {
        if (fingers == null)
        {
            return;
        }

        int thumbBoneCount = Mathf.Min(2, fingers.Length);
        for (int i = 0; i < thumbBoneCount; i++)
        {
            if (!fingers[i].IsValid)
            {
                continue;
            }

            fingers[i].Transform.localRotation = fingers[i].BaseRotation * Quaternion.Euler(eulerOffset);
        }
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root.name == childName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private readonly struct BonePose
    {
        public readonly Transform Transform;
        public readonly Quaternion BaseRotation;
        public readonly Vector3 BasePosition;

        public bool IsValid => Transform != null;

        public BonePose(Transform transform)
        {
            Transform = transform;
            BaseRotation = transform != null ? transform.localRotation : Quaternion.identity;
            BasePosition = transform != null ? transform.localPosition : Vector3.zero;
        }
    }
}
