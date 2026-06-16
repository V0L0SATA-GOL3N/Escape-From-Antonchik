using UnityEngine;

// Build-safe registry of the assets the yard / Antonchik encounter spawns at
// runtime. In the editor these were loaded on demand via AssetDatabase, but that
// API is compiled out of player builds — which left the radio audio, scalapendras,
// snus, truck, pistol and cutscene sounds null (and therefore completely absent)
// in the Mac / standalone builds while everything looked fine in the editor.
//
// This ScriptableObject lives under a Resources folder so the very same asset
// references resolve in both the editor and builds. It is created and kept up to
// date automatically in the editor by YardAssetLibraryBaker; just commit the
// generated Assets/Resources/YardAssetLibrary.asset.
public class YardAssetLibrary : ScriptableObject
{
    [Header("Encounter prefabs")]
    public GameObject antonPrefab;
    public GameObject pistolPrefab;
    public GameObject magazinePrefab;
    public GameObject snusPrefab;
    public GameObject truckPrefab;
    public GameObject gateKeyPrefab;

    [Header("Carry-over pickups")]
    // Items the player can pick up in the room and carry into the yard. Listed
    // here so InventoryCarryOver can re-spawn them in player builds, where
    // AssetDatabase is unavailable. See InventoryCarryOver.ResolveFromLibrary.
    public GameObject jamesonPrefab;
    public GameObject sprayCanPrefab;

    [Header("Animation")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;

    [Header("Audio")]
    public AudioClip typingSound;
    public AudioClip lineFinishedSound;
    public AudioClip shotSound;
    public AudioClip radioFallbackTrack;
    public AudioClip rainSound;

    [Header("Scalapendra")]
    public GameObject scalapendraPrefab;
    public RuntimeAnimatorController scalapendraController;

    [Header("Sky")]
    public Material nightSky;

    public const string ResourcePath = "YardAssetLibrary";

    private static YardAssetLibrary cached;
    private static bool lookedUp;

    // Cached Resources lookup. Returns null if the asset has not been baked yet
    // (callers fall back to AssetDatabase in the editor).
    public static YardAssetLibrary Instance
    {
        get
        {
            if (!lookedUp)
            {
                cached = Resources.Load<YardAssetLibrary>(ResourcePath);
                lookedUp = true;
            }

            return cached;
        }
    }
}
