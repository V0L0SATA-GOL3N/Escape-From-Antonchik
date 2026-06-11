using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class RoomDoorAnimationSetupUtility
{
    private const string RoomModelPath = "Assets/3d/room.glb";
    private const string OutputFolder = "Assets/Animations";
    private const string ControllerPath = OutputFolder + "/RoomDoor.controller";
    private const string ReportPath = OutputFolder + "/RoomDoor_Clips.txt";

    [MenuItem("Tools/Room Door/Setup Open Close Animator")]
    public static void SetupOpenCloseAnimator()
    {
        EnsureOutputFolder();

        AnimationClip openClip = FindClip("open");
        AnimationClip closeClip = FindClip("close");
        WriteClipReport();

        if (openClip == null)
        {
            Debug.LogError("Could not find an 'open' animation clip in " + RoomModelPath);
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState closedState = GetOrCreateState(stateMachine, "closed");
        AnimatorState openState = GetOrCreateState(stateMachine, "open");
        AnimatorState closeState = closeClip != null ? GetOrCreateState(stateMachine, "close") : null;

        closedState.motion = null;
        openState.motion = openClip;
        openState.speed = 1f;

        if (closeState != null)
        {
            closeState.motion = closeClip;
            closeState.speed = 1f;
        }

        stateMachine.defaultState = closedState;
        EditorUtility.SetDirty(controller);

        GameObject room = GameObject.Find("room");
        if (room == null)
        {
            Debug.LogError("Could not find scene object named 'room'.");
            AssetDatabase.SaveAssets();
            return;
        }

        Animator animator = room.GetComponent<Animator>();
        if (animator == null)
        {
            animator = room.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(room);

        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("File/Save");

        Debug.Log("Assigned " + ControllerPath + " to room. Open clip: " + openClip.name +
                  (closeClip != null ? ", close clip: " + closeClip.name : ", no close clip found"));
    }

    private static AnimationClip FindClip(string clipName)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(RoomModelPath)
            .OfType<AnimationClip>()
            .ToArray();

        return clips.FirstOrDefault(clip => clip.name == clipName || clip.name.ToLowerInvariant() == clipName) ??
               clips.FirstOrDefault(clip => clip.name.ToLowerInvariant().Contains(clipName));
    }

    private static void WriteClipReport()
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(RoomModelPath)
            .OfType<AnimationClip>()
            .OrderBy(clip => clip.name)
            .ToArray();

        StringBuilder report = new StringBuilder();
        report.AppendLine("Animation clips in " + RoomModelPath + ":");
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            report.AppendLine("- " + clip.name + " length=" + clip.length.ToString("0.###") + "s");
        }

        File.WriteAllText(ReportPath, report.ToString());
        AssetDatabase.ImportAsset(ReportPath);
    }

    private static AnimatorState GetOrCreateState(AnimatorStateMachine stateMachine, string stateName)
    {
        foreach (ChildAnimatorState childState in stateMachine.states)
        {
            if (childState.state.name == stateName)
            {
                return childState.state;
            }
        }

        return stateMachine.AddState(stateName);
    }

    private static void EnsureOutputFolder()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Animations");
        }
    }
}
