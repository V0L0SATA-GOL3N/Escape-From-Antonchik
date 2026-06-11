using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FirstPersonHandsIdleBuilder
{
    private const string ClipPath = "Assets/Animations/FirstPersonHandsIdle.anim";
    private const string WalkClipPath = "Assets/Animations/FirstPersonHandsStartWalking.anim";
    private const string ControllerPath = "Assets/Animations/FirstPersonHands.controller";
    private const string GameplayScenePath = "Assets/Scenes/gamePlay.unity";
    private const string HandsObjectName = "first_person_arms";
    private const string MoveParameter = "MoveAmount";

    private const string ChestPath = "e329aa7501d248a4851458ecba674d6e.fbx/RootNode/Object_3/_rootJoint/chest_01";
    private const string LeftArmPath = ChestPath + "/L_arm_02";
    private const string LeftElbowPath = LeftArmPath + "/L_elbow_03";
    private const string LeftWristPath = LeftElbowPath + "/L_wrist_04";
    private const string RightArmPath = ChestPath + "/R_arm_025";
    private const string RightElbowPath = RightArmPath + "/R_elbow_026";
    private const string RightWristPath = RightElbowPath + "/R_wrist_027";

    [MenuItem("Tools/Animations/Build First Person Hands Idle")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Animations");

        GameObject hands = OpenGameplaySceneAndFindHands();
        if (hands == null)
        {
            return;
        }

        AnimationClip idleClip = CreateIdleClip(hands.transform);
        AssetDatabase.CreateAsset(idleClip, ClipPath);

        AnimationClip walkClip = CreateStartWalkingClip(hands.transform);
        AssetDatabase.CreateAsset(walkClip, WalkClipPath);

        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(MoveParameter, AnimatorControllerParameterType.Float);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = stateMachine.AddState("Idle");
        idleState.motion = idleClip;
        idleState.writeDefaultValues = true;

        AnimatorState walkState = stateMachine.AddState("StartWalking");
        walkState.motion = walkClip;
        walkState.writeDefaultValues = true;

        stateMachine.defaultState = idleState;
        AddSmoothTransition(idleState, walkState, AnimatorConditionMode.Greater, 0.1f, 0.22f);
        AddSmoothTransition(walkState, idleState, AnimatorConditionMode.Less, 0.08f, 0.24f);

        AssetDatabase.SaveAssets();
        AssignControllerToSceneHands(hands, controller);

        Debug.Log("First person hands idle animation created and assigned.");
    }

    private static AnimationClip CreateIdleClip(Transform handsRoot)
    {
        AssetDatabase.DeleteAsset(ClipPath);

        var clip = new AnimationClip
        {
            name = "FirstPersonHandsIdle",
            frameRate = 30f
        };

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalOrientation = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AddBreathingPosition(clip, handsRoot, ChestPath, new Vector3(0f, 0.035f, -0.018f), 3f);
        AddBreathingEuler(clip, handsRoot, ChestPath, new Vector3(0.7f, 0f, 0f), 3f);
        AddBreathingEuler(clip, handsRoot, LeftArmPath, new Vector3(0f, 0f, 0.35f), 3f);
        AddBreathingEuler(clip, handsRoot, RightArmPath, new Vector3(0f, 0f, -0.35f), 3f);
        AddBreathingEuler(clip, handsRoot, LeftWristPath, new Vector3(0.25f, 0f, 0f), 3f);
        AddBreathingEuler(clip, handsRoot, RightWristPath, new Vector3(0.25f, 0f, 0f), 3f);

        return clip;
    }

    private static AnimationClip CreateStartWalkingClip(Transform handsRoot)
    {
        AssetDatabase.DeleteAsset(WalkClipPath);

        var clip = new AnimationClip
        {
            name = "FirstPersonHandsStartWalking",
            frameRate = 30f
        };

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalOrientation = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AddStepPosition(clip, handsRoot, ChestPath, new Vector3(0.018f, 0.075f, -0.045f), 0.82f);
        AddStepEuler(clip, handsRoot, ChestPath, new Vector3(1.3f, 0.65f, 0.9f), 0.82f);
        AddStepEuler(clip, handsRoot, LeftArmPath, new Vector3(0.8f, 0f, 1.45f), 0.82f);
        AddStepEuler(clip, handsRoot, RightArmPath, new Vector3(0.8f, 0f, -1.45f), 0.82f);
        AddStepEuler(clip, handsRoot, LeftElbowPath, new Vector3(-4.2f, 0.8f, 1.1f), 0.82f);
        AddStepEuler(clip, handsRoot, RightElbowPath, new Vector3(-4.2f, -0.8f, -1.1f), 0.82f);
        AddStepEuler(clip, handsRoot, LeftWristPath, new Vector3(2.2f, 1.1f, 1.35f), 0.82f);
        AddStepEuler(clip, handsRoot, RightWristPath, new Vector3(2.2f, -1.1f, -1.35f), 0.82f);
        AddWalkingFingerMovement(clip, handsRoot, 0.82f);

        return clip;
    }

    private static void AddSmoothTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.offset = 0f;
        transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        transition.orderedInterruption = true;
        transition.AddCondition(mode, threshold, MoveParameter);
    }

    private static void AddBreathingPosition(AnimationClip clip, Transform root, string path, Vector3 offset, float duration)
    {
        Transform target = FindRequired(root, path);
        Vector3 basePosition = target.localPosition;

        AddPositionCurve(clip, path, "x", basePosition.x, basePosition.x + offset.x, basePosition.x, duration);
        AddPositionCurve(clip, path, "y", basePosition.y, basePosition.y + offset.y, basePosition.y, duration);
        AddPositionCurve(clip, path, "z", basePosition.z, basePosition.z + offset.z, basePosition.z, duration);
    }

    private static void AddBreathingEuler(AnimationClip clip, Transform root, string path, Vector3 offset, float duration)
    {
        Transform target = FindRequired(root, path);
        Vector3 baseEuler = target.localEulerAngles;

        AddEulerCurve(clip, path, "x", baseEuler.x, baseEuler.x + offset.x, baseEuler.x, duration);
        AddEulerCurve(clip, path, "y", baseEuler.y, baseEuler.y + offset.y, baseEuler.y, duration);
        AddEulerCurve(clip, path, "z", baseEuler.z, baseEuler.z + offset.z, baseEuler.z, duration);
    }

    private static void AddStepPosition(AnimationClip clip, Transform root, string path, Vector3 offset, float duration)
    {
        Transform target = FindRequired(root, path);
        Vector3 basePosition = target.localPosition;

        AddPositionCurve(clip, path, "x", Step(basePosition.x, offset.x, duration));
        AddPositionCurve(clip, path, "y", Step(basePosition.y, offset.y, duration));
        AddPositionCurve(clip, path, "z", Step(basePosition.z, offset.z, duration));
    }

    private static void AddStepEuler(AnimationClip clip, Transform root, string path, Vector3 offset, float duration)
    {
        Transform target = FindRequired(root, path);
        Vector3 baseEuler = target.localEulerAngles;

        AddEulerCurve(clip, path, "x", Step(baseEuler.x, offset.x, duration));
        AddEulerCurve(clip, path, "y", Step(baseEuler.y, offset.y, duration));
        AddEulerCurve(clip, path, "z", Step(baseEuler.z, offset.z, duration));
    }

    private static void AddWalkingFingerMovement(AnimationClip clip, Transform root, float duration)
    {
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_thumb1_05", new Vector3(-3.5f, 1.2f, -1.5f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_thumb1_05/L_thumb2_06", new Vector3(-2.2f, 0.5f, 0f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_point1_09", new Vector3(-6.5f, 0.8f, 1.2f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_point1_09/L_point2_00", new Vector3(-4.5f, 0f, 0f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_middle1_012", new Vector3(-6f, 0f, 0.5f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_middle1_012/L_middle2_013", new Vector3(-4f, 0f, 0f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_palm_016/L_ring1_017", new Vector3(-5f, -0.4f, -0.7f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_palm_016/L_ring1_017/L_ring2_018", new Vector3(-3.5f, 0f, 0f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_palm_016/L_pink1_021", new Vector3(-4f, -0.9f, -1f), duration);
        AddFingerOpenStep(clip, root, LeftWristPath + "/L_palm_016/L_pink1_021/L_pink2_022", new Vector3(-3f, 0f, 0f), duration);

        AddFingerOpenStep(clip, root, RightWristPath + "/R_thumb1_028", new Vector3(-3.5f, -1.2f, 1.5f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_thumb1_028/R_thumb2_029", new Vector3(-2.2f, -0.5f, 0f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_point1_032", new Vector3(-6.5f, -0.8f, -1.2f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_point1_032/R_point2_033", new Vector3(-4.5f, 0f, 0f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_middle1_036", new Vector3(-6f, 0f, -0.5f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_middle1_036/R_middle2_037", new Vector3(-4f, 0f, 0f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_palm_040/R_ring1_041", new Vector3(-5f, 0.4f, 0.7f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_palm_040/R_ring1_041/R_ring2_042", new Vector3(-3.5f, 0f, 0f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_palm_040/R_pink1_045", new Vector3(-4f, 0.9f, 1f), duration);
        AddFingerOpenStep(clip, root, RightWristPath + "/R_palm_040/R_pink1_045/R_pink2_046", new Vector3(-3f, 0f, 0f), duration);
    }

    private static void AddFingerOpenStep(AnimationClip clip, Transform root, string path, Vector3 openOffset, float duration)
    {
        Transform target = FindRequired(root, path);
        Vector3 baseEuler = target.localEulerAngles;

        AddEulerCurve(clip, path, "x", FingerOpenStep(baseEuler.x, openOffset.x, duration));
        AddEulerCurve(clip, path, "y", FingerOpenStep(baseEuler.y, openOffset.y, duration));
        AddEulerCurve(clip, path, "z", FingerOpenStep(baseEuler.z, openOffset.z, duration));
    }

    private static Transform FindRequired(Transform root, string path)
    {
        Transform target = root.Find(path);
        if (target == null)
        {
            throw new System.InvalidOperationException("Could not find first-person hands transform: " + path);
        }

        return target;
    }

    private static void AddPositionCurve(AnimationClip clip, string path, string axis, float a, float b, float c, float duration)
    {
        SetCurve(clip, path, typeof(Transform), "localPosition." + axis, Ease(a, b, c, duration));
    }

    private static void AddPositionCurve(AnimationClip clip, string path, string axis, AnimationCurve curve)
    {
        SetCurve(clip, path, typeof(Transform), "localPosition." + axis, curve);
    }

    private static void AddEulerCurve(AnimationClip clip, string path, string axis, float a, float b, float c, float duration)
    {
        SetCurve(clip, path, typeof(Transform), "localEulerAnglesRaw." + axis, Ease(a, b, c, duration));
    }

    private static void AddEulerCurve(AnimationClip clip, string path, string axis, AnimationCurve curve)
    {
        SetCurve(clip, path, typeof(Transform), "localEulerAnglesRaw." + axis, curve);
    }

    private static void SetCurve(AnimationClip clip, string path, System.Type type, string propertyName, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, propertyName), curve);
    }

    private static AnimationCurve Ease(float a, float b, float c, float duration)
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, a),
            new Keyframe(duration * 0.5f, b),
            new Keyframe(duration, c)
        );

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        }

        return curve;
    }

    private static AnimationCurve Step(float baseValue, float amplitude, float duration)
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, baseValue),
            new Keyframe(duration * 0.25f, baseValue + amplitude),
            new Keyframe(duration * 0.5f, baseValue),
            new Keyframe(duration * 0.75f, baseValue - amplitude),
            new Keyframe(duration, baseValue)
        );

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        }

        return curve;
    }

    private static AnimationCurve FingerOpenStep(float baseValue, float openAmount, float duration)
    {
        var curve = new AnimationCurve(
            new Keyframe(0f, baseValue),
            new Keyframe(duration * 0.2f, baseValue + openAmount),
            new Keyframe(duration * 0.5f, baseValue + openAmount * 0.35f),
            new Keyframe(duration * 0.7f, baseValue + openAmount),
            new Keyframe(duration, baseValue)
        );

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        }

        return curve;
    }

    private static GameObject OpenGameplaySceneAndFindHands()
    {
        if (!File.Exists(GameplayScenePath))
        {
            Debug.LogWarning("Could not find " + GameplayScenePath + ". Idle animation was not created.");
            return null;
        }

        EditorSceneManager.OpenScene(GameplayScenePath);
        GameObject hands = GameObject.Find(HandsObjectName);

        if (hands == null)
        {
            Debug.LogWarning("Could not find " + HandsObjectName + " in " + GameplayScenePath + ". Idle animation was not created.");
            return null;
        }

        return hands;
    }

    private static void AssignControllerToSceneHands(GameObject hands, RuntimeAnimatorController controller)
    {
        var animator = hands.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(hands);
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        if (hands.GetComponent<FirstPersonHandsAnimatorDriver>() == null)
        {
            Undo.AddComponent<FirstPersonHandsAnimatorDriver>(hands);
        }

        EditorSceneManager.MarkSceneDirty(hands.scene);
        EditorSceneManager.SaveScene(hands.scene);
    }
}
