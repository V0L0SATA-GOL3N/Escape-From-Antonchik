#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Keeps Assets/Resources/YardAssetLibrary.asset populated with the runtime-spawned
// yard assets, using the same AssetDatabase paths the runtime scripts used to use
// directly. Runs automatically after every domain reload (so the asset is created
// the moment this script compiles) and can be re-run from the menu. Commit the
// generated .asset so player builds can load it via Resources.
[InitializeOnLoad]
public static class YardAssetLibraryBaker
{
    private const string ResourcesDir = "Assets/Resources";
    private const string AssetPath = "Assets/Resources/YardAssetLibrary.asset";

    static YardAssetLibraryBaker()
    {
        // Defer until the AssetDatabase is ready after the domain reload.
        EditorApplication.delayCall += () => Bake(false);
    }

    [MenuItem("Tools/HDRP Setup/Rebake Yard Asset Library")]
    public static void RebakeMenu()
    {
        Bake(true);
        Debug.Log("[YardAssetLibrary] Rebaked " + AssetPath);
    }

    private static void Bake(bool force)
    {
        YardAssetLibrary lib = AssetDatabase.LoadAssetAtPath<YardAssetLibrary>(AssetPath);
        if (lib == null)
        {
            if (!Directory.Exists(ResourcesDir))
            {
                Directory.CreateDirectory(ResourcesDir);
                AssetDatabase.Refresh();
            }

            lib = ScriptableObject.CreateInstance<YardAssetLibrary>();
            AssetDatabase.CreateAsset(lib, AssetPath);
        }

        bool changed = force;
        changed |= Assign(ref lib.antonPrefab, "Assets/Prefabs/anton.prefab", force);
        changed |= Assign(ref lib.pistolPrefab, "Assets/Prefabs/Pistol_00.prefab", force);
        changed |= Assign(ref lib.magazinePrefab, "Assets/Prefabs/mag Variant.prefab", force);
        changed |= Assign(ref lib.snusPrefab, "Assets/Prefabs/snus.prefab", force);
        changed |= Assign(ref lib.truckPrefab, "Assets/Pickup/Prefabs/PickUp_Damaged.prefab", force);
        changed |= Assign(ref lib.jamesonPrefab, "Assets/Prefabs/Jameson.prefab", force);
        changed |= Assign(ref lib.sprayCanPrefab, "Assets/Prefabs/spraypaint_can_mtn_hardcore.prefab", force);
        changed |= Assign(ref lib.idleClip, "Assets/3d/Standing Idle.fbx", force);
        changed |= Assign(ref lib.walkClip, "Assets/Animations/Walking.fbx", force);
        changed |= Assign(ref lib.typingSound, "Assets/SFX/cutscene_typing.wav", force);
        changed |= Assign(ref lib.lineFinishedSound, "Assets/SFX/cutscene_finish.wav", force);
        changed |= Assign(ref lib.shotSound, "Assets/SFX/shot.mp3", force);
        changed |= Assign(ref lib.radioFallbackTrack, "Assets/SFX/track_1.mp3", force);
        changed |= Assign(ref lib.scalapendraPrefab, "Assets/Prefabs/scalapendra.prefab", force);
        changed |= Assign(ref lib.scalapendraController, "Assets/3d/scalapendra.controller", force);
        changed |= Assign(ref lib.nightSky, "Assets/sky/NightSky.mat", force);

        if (changed)
        {
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
        }
    }

    private static bool Assign<T>(ref T field, string path, bool force) where T : Object
    {
        if (!force && field != null)
        {
            return false;
        }

        T loaded = AssetDatabase.LoadAssetAtPath<T>(path);
        if (loaded == null)
        {
            Debug.LogWarning("[YardAssetLibrary] Could not load " + path);
            return false;
        }

        if (ReferenceEquals(field, loaded))
        {
            return false;
        }

        field = loaded;
        return true;
    }
}
#endif
