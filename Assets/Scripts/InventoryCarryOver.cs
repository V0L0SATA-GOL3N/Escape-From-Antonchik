using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Items the player holds when leaving the room must follow them into the
// yard (e.g. the Jameson bottle that can calm Antonchik down). Scene loads
// wipe the player, so item names are stashed here and matching prefabs are
// re-given on the other side.
public static class InventoryCarryOver
{
    private static readonly List<string> carriedNames = new List<string>();

    public static void Capture(DoorRaycastInteractor interactor)
    {
        carriedNames.Clear();
        if (interactor == null)
        {
            return;
        }

        carriedNames.AddRange(interactor.GetCarriedItemNames());
    }

    public static void RestoreInto(DoorRaycastInteractor interactor)
    {
        if (interactor == null || carriedNames.Count == 0)
        {
            return;
        }

        foreach (string itemName in carriedNames)
        {
            GameObject prefab = LoadPrefabByName(itemName);
            if (prefab == null)
            {
                continue;
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = itemName;
            BuiltinPipelineCompatibility.PatchSpawnedObject(instance);
            PickupInteractable pickup = instance.GetComponent<PickupInteractable>();
            if (pickup == null || !interactor.GiveItem(pickup))
            {
                Object.Destroy(instance);
            }
        }

        carriedNames.Clear();
    }

    private static GameObject LoadPrefabByName(string itemName)
    {
        GameObject prefab = Resources.Load<GameObject>(itemName);
#if UNITY_EDITOR
        if (prefab == null)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{itemName}.prefab");
        }
#endif
        return prefab;
    }
}
