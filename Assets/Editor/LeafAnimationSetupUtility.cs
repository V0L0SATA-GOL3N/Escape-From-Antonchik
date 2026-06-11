using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class LeafAnimationSetupUtility
{
    private const string ModelPath = "Assets/3d/ground.glb";
    private const string OutputFolder = "Assets/Animations";
    private const string MergedClipPath = OutputFolder + "/GroundLeafWind_Merged.anim";
    private const string ControllerPath = OutputFolder + "/GroundLeafWind.controller";
    private const string ReportPath = OutputFolder + "/GroundLeafWind_Bindings.json";

    [MenuItem("Tools/Leaf Animation/Dump Ground GLB Bindings")]
    public static void DumpGroundGlbBindings()
    {
        EnsureOutputFolder();

        var clips = LoadMorphBakeClips();
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"model\": \"" + ModelPath + "\",");
        sb.AppendLine("  \"clips\": [");

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var bindings = AnimationUtility.GetCurveBindings(clip);
            sb.AppendLine("    {");
            sb.AppendLine("      \"name\": \"" + clip.name + "\",");
            sb.AppendLine("      \"length\": " + clip.length.ToString("0.###") + ",");
            sb.AppendLine("      \"curveCount\": " + bindings.Length + ",");
            sb.AppendLine("      \"paths\": [");

            var paths = bindings.Select(b => b.path).Distinct().OrderBy(p => p).ToArray();
            for (var p = 0; p < paths.Length; p++)
            {
                sb.Append("        \"" + paths[p].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
                sb.AppendLine(p == paths.Length - 1 ? "" : ",");
            }

            sb.AppendLine("      ]");
            sb.Append("    }");
            sb.AppendLine(i == clips.Count - 1 ? "" : ",");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText(ReportPath, sb.ToString());
        AssetDatabase.ImportAsset(ReportPath);
        Debug.Log("Wrote leaf animation binding report: " + ReportPath);
    }

    [MenuItem("Tools/Leaf Animation/Setup Ground Leaf Wind")]
    public static void SetupGroundLeafWind()
    {
        EnsureOutputFolder();
        DumpGroundGlbBindings();

        var clips = LoadMorphBakeClips();
        if (clips.Count == 0)
        {
            Debug.LogError("No MorphBake animation clips found inside " + ModelPath);
            return;
        }

        var merged = AssetDatabase.LoadAssetAtPath<AnimationClip>(MergedClipPath);
        if (merged == null)
        {
            merged = new AnimationClip { name = "GroundLeafWind_Merged" };
            AssetDatabase.CreateAsset(merged, MergedClipPath);
        }

        ClearClip(merged);
        merged.frameRate = clips.Max(c => c.frameRate);

        foreach (var source in clips)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                AnimationUtility.SetEditorCurve(merged, binding, curve);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(source, binding);
                AnimationUtility.SetObjectReferenceCurve(merged, binding, curve);
            }
        }

        var settings = AnimationUtility.GetAnimationClipSettings(merged);
        settings.loopTime = true;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(merged, settings);
        EditorUtility.SetDirty(merged);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        var layer = controller.layers[0];
        var stateMachine = layer.stateMachine;
        foreach (var state in stateMachine.states)
        {
            if (state.state.name == "Ground Leaf Wind")
            {
                state.state.motion = merged;
                stateMachine.defaultState = state.state;
                AssignController(controller);
                SaveAssets();
                Debug.Log("Updated leaf animation controller: " + ControllerPath);
                return;
            }
        }

        var newState = stateMachine.AddState("Ground Leaf Wind");
        newState.motion = merged;
        stateMachine.defaultState = newState;
        AssignController(controller);
        SaveAssets();
        Debug.Log("Created leaf animation controller and assigned it to ground: " + ControllerPath);
    }

    private static List<AnimationClip> LoadMorphBakeClips()
    {
        return AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .Where(c => c.name.StartsWith("MorphBake"))
            .OrderBy(c => c.name)
            .ToList();
    }

    private static void AssignController(RuntimeAnimatorController controller)
    {
        var ground = GameObject.Find("ground");
        if (ground == null)
        {
            Debug.LogError("Scene object 'ground' was not found. The imported clip paths are rooted under ground.");
            return;
        }

        var animator = ground.GetComponent<Animator>();
        if (animator == null)
        {
            animator = ground.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(ground);
    }

    private static void ClearClip(AnimationClip clip)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            AnimationUtility.SetEditorCurve(clip, binding, null);
        }

        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
        }
    }

    private static void EnsureOutputFolder()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Animations");
        }
    }

    private static void SaveAssets()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorApplication.ExecuteMenuItem("File/Save");
    }
}
