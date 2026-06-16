using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Items the player holds when leaving the room must follow them into the
// yard (e.g. the Jameson bottle that can calm Antonchik down, or the pistol).
// Scene loads wipe the player, so each carried item's name plus the state that
// can't be re-derived from a fresh prefab (the gun's loaded round count and
// spare magazines, and which slot was in hand) is stashed here. Matching
// prefabs are re-spawned and that state re-applied on the other side.
public static class InventoryCarryOver
{
    private struct CarriedItem
    {
        public string name;
        public bool wasActiveSlot;
        public bool isGun;
        public int gunAmmoInMagazine;
        public int gunSpareMagazines;
        public bool isSpray;
        public AudioClip spraySound;
        public AudioClip sprayRattle;
    }

    private static readonly List<CarriedItem> carried = new List<CarriedItem>();

    public static void Capture(DoorRaycastInteractor interactor)
    {
        carried.Clear();
        if (interactor == null)
        {
            return;
        }

        int activeSlot = interactor.ActiveInventorySlot;
        for (int i = 0; i < interactor.InventorySlotCount; i++)
        {
            PickupInteractable item = interactor.GetInventoryItem(i);
            if (item == null)
            {
                continue;
            }

            CarriedItem record = new CarriedItem
            {
                name = CleanName(item.DisplayName),
                wasActiveSlot = i == activeSlot,
            };

            GunWeaponController gun = item.GetComponent<GunWeaponController>();
            if (gun != null)
            {
                gun.GetCarryState(out record.gunAmmoInMagazine, out record.gunSpareMagazines);
                record.isGun = true;
            }

            // The spray prefab lacks its sound clips (authored on the scene
            // instance), so grab them from the live can to carry them across.
            SprayCanController spray = item.GetComponent<SprayCanController>();
            if (spray != null)
            {
                record.isSpray = true;
                spray.GetCarrySounds(out record.spraySound, out record.sprayRattle);
            }

            carried.Add(record);
            Debug.Log($"[InventoryCarryOver] Captured '{record.name}' (activeSlot={record.wasActiveSlot}, gun={record.isGun}).");
        }

        Debug.Log($"[InventoryCarryOver] Capture complete — {carried.Count} item(s) stashed.");
    }

    public static void RestoreInto(DoorRaycastInteractor interactor)
    {
        if (interactor == null || carried.Count == 0)
        {
            if (interactor == null && carried.Count > 0)
            {
                Debug.LogWarning($"[InventoryCarryOver] RestoreInto called with {carried.Count} stashed item(s) but no interactor — items not restored.");
            }

            return;
        }

        // The yard inventory starts empty, so each successful give lands in the
        // next slot in order; track that to restore which item was in hand.
        int givenCount = 0;
        int activeRestoredSlot = -1;

        foreach (CarriedItem record in carried)
        {
            // Guns resolve by their isGun flag, not by name: there is a single
            // gun prefab and its in-game name varies (e.g. the model was swapped
            // to a "Glock-18"), which would never match "Pistol_00" by name.
            GameObject prefab = record.isGun ? LoadGunPrefab() : LoadPrefabByName(record.name);
            if (prefab == null)
            {
                Debug.LogWarning($"[InventoryCarryOver] No prefab found for carried item '{record.name}' (isGun={record.isGun}) — item dropped.");
                continue;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            BuiltinPipelineCompatibility.PatchSpawnedObject(instance);

            // The pickup script may live on a child (e.g. Jameson's interactable
            // sits on the "Jameson_Atlas_0" mesh, not the prefab root), so search
            // the whole hierarchy rather than just the root.
            PickupInteractable pickup = instance.GetComponentInChildren<PickupInteractable>(true);
            if (pickup == null)
            {
                Debug.LogWarning($"[InventoryCarryOver] Spawned '{prefab.name}' has no PickupInteractable — item dropped.");
                UnityEngine.Object.Destroy(instance);
                continue;
            }

            // Give both the instance root and the interactable's object a single
            // canonical name. The yard quest identifies carried items by name
            // (interactor.HasItemNamed("Jameson"/"snus")), and the interactable's
            // own GameObject is what DisplayName reports.
            // Keep the name the item carried out of the previous scene so in-scene
            // renames survive the transfer (e.g. the spray can shown as "mtn can",
            // or a renamed snus). The gun always keeps its captured name; other
            // items keep theirs too, falling back to the prefab root name only when
            // the captured name is raw mesh-import noise ("Jameson_Atlas_0").
            string canonicalName = record.isGun
                ? CleanName(record.name)
                : ChooseDisplayName(CleanName(record.name), prefab.name);
            instance.name = canonicalName;
            pickup.gameObject.name = canonicalName;

            // Carried-over items get double interaction distance in the new
            // scene (so they're easier to grab if dropped/re-picked there).
            pickup.SetInteractionDistance(pickup.InteractionDistance * 2f);

            if (!interactor.GiveItem(pickup))
            {
                Debug.LogWarning($"[InventoryCarryOver] GiveItem failed for '{canonicalName}' (inventory full?) — item dropped.");
                UnityEngine.Object.Destroy(instance);
                continue;
            }

            if (record.isGun)
            {
                GunWeaponController gun = pickup.GetComponent<GunWeaponController>();
                if (gun != null)
                {
                    gun.ApplyCarryState(record.gunAmmoInMagazine, record.gunSpareMagazines);
                }
            }

            if (record.isSpray)
            {
                SprayCanController spray = pickup.GetComponent<SprayCanController>();
                if (spray != null)
                {
                    // Restore the carried sounds (the prefab has none) and apply
                    // the tuned yard spray settings.
                    spray.ApplyCarryOverSettings(10f, 2f, 0.01f, record.spraySound, record.sprayRattle);
                }
            }

            if (record.wasActiveSlot)
            {
                activeRestoredSlot = givenCount;
            }

            Debug.Log($"[InventoryCarryOver] Restored '{canonicalName}' (from captured '{record.name}', slot {givenCount}).");
            givenCount++;
        }

        if (activeRestoredSlot >= 0)
        {
            interactor.SetActiveInventorySlot(activeRestoredSlot);
        }

        carried.Clear();
    }

    // The one gun prefab, resolved by identity rather than name.
    private static GameObject LoadGunPrefab()
    {
        YardAssetLibrary lib = YardAssetLibrary.Instance;
        if (lib != null && lib.pistolPrefab != null)
        {
            return lib.pistolPrefab;
        }
#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Pistol_00.prefab");
#else
        return null;
#endif
    }

    private static GameObject LoadPrefabByName(string itemName)
    {
        GameObject prefab = Resources.Load<GameObject>(itemName);
        if (prefab == null)
        {
            prefab = ResolveFromLibrary(itemName);
        }
#if UNITY_EDITOR
        if (prefab == null)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{itemName}.prefab");
        }
#endif
        return prefab;
    }

    // Build-safe fallback: AssetDatabase is compiled out of player builds, so the
    // pistol/snus/magazine come from the baked Resources library instead.
    private static GameObject ResolveFromLibrary(string itemName)
    {
        YardAssetLibrary lib = YardAssetLibrary.Instance;
        if (lib == null)
        {
            return null;
        }

        if (Matches(lib.pistolPrefab, itemName)) return lib.pistolPrefab;
        if (Matches(lib.snusPrefab, itemName)) return lib.snusPrefab;
        if (Matches(lib.magazinePrefab, itemName)) return lib.magazinePrefab;
        if (Matches(lib.jamesonPrefab, itemName)) return lib.jamesonPrefab;
        if (Matches(lib.sprayCanPrefab, itemName)) return lib.sprayCanPrefab;
        return null;
    }

    // The captured name is the live interactable's GameObject name, which often
    // carries mesh-import noise the prefab root doesn't ("Jameson_Atlas_0" vs
    // "Jameson", a "(Clone)" suffix, numeric variant tails). Compare on a
    // normalized form so those still resolve to the right prefab.
    private static bool Matches(GameObject prefab, string itemName)
    {
        if (prefab == null)
        {
            return false;
        }

        string p = Normalize(prefab.name);
        string c = Normalize(itemName);
        if (p.Length == 0 || c.Length == 0)
        {
            return false;
        }

        // Bidirectional substring: the captured name may be a mesh-noise
        // superset ("jamesonatlas0" ⊃ "jameson") or a scene-renamed subset
        // ("mtnhardcore" ⊂ "spraypaintcanmtnhardcore").
        return p == c || c.Contains(p) || p.Contains(c);
    }

    // Lowercase, drop "(Clone)" and every non-alphanumeric character so
    // "Jameson_Atlas_0" -> "jamesonatlas0" still starts with "jameson".
    private static string Normalize(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        string stripped = rawName.Replace("(Clone)", string.Empty);
        var sb = new System.Text.StringBuilder(stripped.Length);
        foreach (char ch in stripped)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    // Prefer the name the item carried out of the previous scene so deliberate
    // in-scene renames (e.g. the spray can shown as "mtn can") survive the
    // transfer. Only fall back to the prefab root name when the captured name is
    // raw mesh-import noise — a strict superset of the prefab name with extra
    // junk, like "Jameson_Atlas_0" wrapping "Jameson" — which would otherwise
    // read badly and break quest HasItemNamed checks.
    private static string ChooseDisplayName(string captured, string prefabName)
    {
        if (string.IsNullOrWhiteSpace(captured))
        {
            return CleanName(prefabName);
        }

        string c = Normalize(captured);
        string p = Normalize(prefabName);
        bool capturedIsMeshNoise = p.Length > 0 && c.Length > p.Length && c.Contains(p);
        return capturedIsMeshNoise ? CleanName(prefabName) : captured;
    }

    private static string CleanName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "Object";
        }

        return rawName.Replace("(Clone)", string.Empty).Trim();
    }
}
