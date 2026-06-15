using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Quest director for the yard (continueGamePlay). Antonchik jumps the player
// near the fence and opens a branching dialog driven by an internal trust
// value. Bring him whisky, run his errands, earn the car keys — or catch a
// bullet. Also boots the radio, the scalapendras, the screamer system and
// the truck.
//
// Dialog tree: see DIALOG_DIAGRAM.md in the project root.
public class AntonchikFenceEncounter : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private string fenceNamePrefix = "fence";
    [SerializeField] private float triggerDistance = 4.5f;
    [SerializeField] private float minRandomDelay = 0.8f;
    [SerializeField] private float maxRandomDelay = 5f;

    [Header("Antonchik")]
    [SerializeField] private GameObject antonPrefab;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private float spawnDistance = 2.3f;
    [SerializeField] private float turnDuration = 0.55f;

    [Header("Dialog")]
    [SerializeField] private string speakerName = "АНТОНЧИК";
    [SerializeField] private float lettersPerSecond = 14f;
    [SerializeField] private float replyTimeLimit = 10f;

    [Header("Quest Props")]
    [SerializeField] private GameObject pistolPrefab;
    [SerializeField] private GameObject magazinePrefab;
    [SerializeField] private GameObject snusPrefab;
    [SerializeField] private GameObject truckPrefab;

    [Header("Audio")]
    [SerializeField] private AudioClip jumpscareSound;
    [SerializeField] private AudioClip typingSound;
    [SerializeField] private AudioClip lineFinishedSound;
    [SerializeField] private AudioClip shotSound;

    private FirstPersonController playerController;
    private Transform playerTransform;
    private Camera playerCamera;
    private Rigidbody playerRigidbody;
    private DoorRaycastInteractor interactor;
    private AudioSource uiAudioSource;
    private AudioSource worldAudioSource;
    private readonly List<Collider> fenceColliders = new List<Collider>();
    private readonly List<Transform> fenceTransforms = new List<Transform>();

    private GameObject antonInstance;
    private AntonchikPuppeteer puppeteer;
    private SimpleInteractable antonTalk;
    private Light rimLight;
    private Canvas encounterCanvas;
    private GameObject dialogRoot;
    private TextMeshProUGUI speakerLabel;
    private TextMeshProUGUI dialogLabel;
    private GameObject choicesRoot;
    private Image timerFill;

    private ScalapendraSpawner scalapendraSpawner;
    private ScreamerController screamerController;
    private GameObject snusInstance;
    private GameObject gateKeysInstance;
    private Collider groundFloorCollider;
    private readonly List<Bounds> spawnBlockers = new List<Bounds>();
    private bool spawnBlockersGathered;

    private bool encounterStarted;
    private bool armed;
    private float armedTimer;
    private bool dialogBusy;
    private bool playerWasLocked;

    // --- internal state ---
    private int trust = 30;
    private QuestStage stage = QuestStage.BeforeEncounter;
    private QuestStage pendingTaskStage = QuestStage.FindSnus;
    private int chosenIndex = -1;
    private bool choiceTimedOutOnce;

    private enum QuestStage
    {
        BeforeEncounter,
        IntroDialog,
        FindSnus,
        FindGateKeys,
        GoToCar,
        Dead
    }

    public static bool SequenceActive { get; private set; }

    // One-line quest summary for the cheat "stats" overlay.
    public string CheatStatusLine()
    {
        int bugs = scalapendraSpawner != null ? scalapendraSpawner.AliveCount : 0;
        string keys = CarEscapeController.HasCarKeys ? "yes" : "no";
        return $"Quest: {stage}  Trust: {trust}  CarKeys: {keys}  Scalapendras: {bugs}";
    }

    private void Start()
    {
        SequenceActive = false;
        ResolveSceneReferences();
        EnsureAssetsLoaded();
        SetupYard();
    }

    private void OnDestroy()
    {
        SequenceActive = false;
    }

    private void Update()
    {
        if (encounterStarted || playerTransform == null)
        {
            return;
        }

        if (!armed)
        {
            if (IsPlayerNearFence())
            {
                armed = true;
                armedTimer = UnityEngine.Random.Range(minRandomDelay, maxRandomDelay);
            }

            return;
        }

        armedTimer -= Time.deltaTime;
        if (armedTimer <= 0f)
        {
            encounterStarted = true;
            StartCoroutine(EncounterRoutine());
        }
    }

    // ------------------------------------------------------------------
    // setup
    // ------------------------------------------------------------------

    private void ResolveSceneReferences()
    {
        playerController = FindObjectOfType<FirstPersonController>();
        if (playerController != null)
        {
            playerTransform = playerController.transform;
            playerRigidbody = playerController.GetComponent<Rigidbody>();
        }

        playerCamera = Camera.main;
        if (playerCamera == null && playerController != null)
        {
            playerCamera = playerController.GetComponentInChildren<Camera>(true);
        }

        interactor = FindObjectOfType<DoorRaycastInteractor>();

        fenceColliders.Clear();
        fenceTransforms.Clear();
        foreach (Transform candidate in FindObjectsOfType<Transform>())
        {
            if (!candidate.name.StartsWith(fenceNamePrefix) && candidate.name != "gate")
            {
                continue;
            }

            if (candidate.parent == null || candidate.parent.name != "ground")
            {
                continue;
            }

            fenceTransforms.Add(candidate);
            fenceColliders.AddRange(candidate.GetComponentsInChildren<Collider>());
        }

        uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.spatialBlend = 0f;

        worldAudioSource = gameObject.AddComponent<AudioSource>();
        worldAudioSource.playOnAwake = false;
        worldAudioSource.spatialBlend = 0f;
    }

    private void EnsureAssetsLoaded()
    {
        // Build-safe path: pull from the baked Resources library (works in builds,
        // where AssetDatabase below is compiled out).
        YardAssetLibrary lib = YardAssetLibrary.Instance;
        if (lib != null)
        {
            if (antonPrefab == null) antonPrefab = lib.antonPrefab;
            if (pistolPrefab == null) pistolPrefab = lib.pistolPrefab;
            if (magazinePrefab == null) magazinePrefab = lib.magazinePrefab;
            if (snusPrefab == null) snusPrefab = lib.snusPrefab;
            if (truckPrefab == null) truckPrefab = lib.truckPrefab;
            if (idleClip == null) idleClip = lib.idleClip;
            if (walkClip == null) walkClip = lib.walkClip;
            if (typingSound == null) typingSound = lib.typingSound;
            if (lineFinishedSound == null) lineFinishedSound = lib.lineFinishedSound;
            if (shotSound == null) shotSound = lib.shotSound;
        }

#if UNITY_EDITOR
        if (antonPrefab == null)
        {
            antonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/anton.prefab");
        }

        if (pistolPrefab == null)
        {
            pistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Pistol_00.prefab");
        }

        if (magazinePrefab == null)
        {
            magazinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/mag Variant.prefab");
        }

        if (snusPrefab == null)
        {
            snusPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/snus.prefab");
        }

        if (truckPrefab == null)
        {
            truckPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Pickup/Prefabs/PickUp_Damaged.prefab");
        }

        if (idleClip == null)
        {
            idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3d/Standing Idle.fbx");
        }

        if (walkClip == null)
        {
            walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Walking.fbx");
        }

        if (typingSound == null)
        {
            typingSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/cutscene_typing.wav");
        }

        if (lineFinishedSound == null)
        {
            lineFinishedSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/cutscene_finish.wav");
        }

        if (shotSound == null)
        {
            shotSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/shot.mp3");
        }
#endif

        if (jumpscareSound == null)
        {
            jumpscareSound = ProceduralJumpscareSting.Create();
        }
    }

    // Everything the yard needs besides Antonchik himself.
    private void SetupYard()
    {
        if (interactor != null)
        {
            InventoryCarryOver.RestoreInto(interactor);
        }

        // radio prop
        GameObject radioObject = GameObject.Find("radio");
        if (radioObject != null && radioObject.GetComponent<RadioController>() == null)
        {
            radioObject.AddComponent<RadioController>();
        }

        scalapendraSpawner = gameObject.AddComponent<ScalapendraSpawner>();
        screamerController = gameObject.AddComponent<ScreamerController>();

        SpawnTruck();
        PlacePistolOnBox();

        StartCoroutine(InitialObjectiveRoutine());
    }

    private IEnumerator InitialObjectiveRoutine()
    {
        yield return new WaitForSeconds(2.5f);
        if (stage == QuestStage.BeforeEncounter)
        {
            TaskHud.SetObjective($"Найди способ <color={TaskHud.Highlight}>выбраться со двора</color>");
        }
    }

    private void SpawnTruck()
    {
        if (truckPrefab == null)
        {
            return;
        }

        GameObject gateObject = GameObject.Find("gate");
        Vector3 anchor = gateObject != null ? gateObject.transform.position : transform.position;
        Vector3 inward = playerTransform != null
            ? (playerTransform.position - anchor)
            : Vector3.forward;
        inward.y = 0f;
        inward.Normalize();

        Vector3 carPosition = anchor + inward * 7.5f;
        if (Physics.Raycast(carPosition + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            carPosition = hit.point;
        }

        Quaternion facing = Quaternion.LookRotation(-inward, Vector3.up);
        GameObject truck = Instantiate(truckPrefab, carPosition, facing);
        truck.name = "Машина Антончика";
        BuiltinPipelineCompatibility.PatchSpawnedObject(truck);
        truck.AddComponent<CarEscapeController>();
    }

    private void PlacePistolOnBox()
    {
        if (pistolPrefab == null || ScreamerController.PlayerCarriesGun())
        {
            return;
        }

        GameObject boxObject = GameObject.Find("box");
        Vector3 position;
        if (boxObject != null)
        {
            Renderer boxRenderer = boxObject.GetComponentInChildren<Renderer>();
            position = boxRenderer != null
                ? boxRenderer.bounds.center + Vector3.up * (boxRenderer.bounds.extents.y + 0.05f)
                : boxObject.transform.position + Vector3.up * 1f;
        }
        else if (playerTransform != null)
        {
            position = playerTransform.position + playerTransform.right * 2f;
        }
        else
        {
            return;
        }

        GameObject pistol = Instantiate(pistolPrefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 90f));
        pistol.name = "Pistol_00";
        BuiltinPipelineCompatibility.PatchSpawnedObject(pistol);

        if (magazinePrefab != null)
        {
            GameObject magazine = Instantiate(magazinePrefab, position + Vector3.right * 0.25f, Quaternion.identity);
            magazine.name = "mag Variant";
            BuiltinPipelineCompatibility.PatchSpawnedObject(magazine);

            // PhysX rejects concave mesh colliders on dynamic rigidbodies
            foreach (MeshCollider meshCollider in magazine.GetComponentsInChildren<MeshCollider>(true))
            {
                meshCollider.convex = true;
            }
        }
    }

    private bool IsPlayerNearFence()
    {
        Vector3 playerPosition = playerTransform.position;

        for (int i = 0; i < fenceColliders.Count; i++)
        {
            Collider fence = fenceColliders[i];
            if (fence == null)
            {
                continue;
            }

            Vector3 closest = fence.ClosestPointOnBounds(playerPosition);
            closest.y = playerPosition.y;
            if ((closest - playerPosition).sqrMagnitude <= triggerDistance * triggerDistance)
            {
                return true;
            }
        }

        for (int i = 0; i < fenceTransforms.Count; i++)
        {
            Vector3 fencePosition = fenceTransforms[i].position;
            fencePosition.y = playerPosition.y;
            if ((fencePosition - playerPosition).sqrMagnitude <= triggerDistance * triggerDistance)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // encounter flow
    // ------------------------------------------------------------------

    private IEnumerator EncounterRoutine()
    {
        SequenceActive = true;
        stage = QuestStage.IntroDialog;
        TaskHud.ClearObjective();
        LockPlayer();
        SpawnAntonBehindPlayer();
        PlayJumpscareSound();

        yield return TurnPlayerTowardsAnton();
        yield return new WaitForSeconds(0.45f);

        BuildEncounterCanvas();
        BuildDialogPanel();

        yield return RunDialog("intro");

        if (stage != QuestStage.Dead)
        {
            EndDialogUi();
            BeginTasksPhase();
        }
    }

    private void BeginTasksPhase()
    {
        SequenceActive = false;
        UnlockPlayer();
        scalapendraSpawner.Activate();

        WireAntonInteraction();

        if (pendingTaskStage == QuestStage.FindGateKeys)
        {
            StartGateKeysTask();
        }
        else
        {
            StartSnusTask();
        }
    }

    private void WireAntonInteraction()
    {
        if (antonInstance == null)
        {
            return;
        }

        if (antonTalk == null)
        {
            antonTalk = antonInstance.AddComponent<SimpleInteractable>();
            antonTalk.Prompt = "Press E to talk to Antonchik";
            antonTalk.SetInteractionDistance(4f);
            antonTalk.Interacted += OnTalkToAnton;
        }

        antonTalk.CanInteract = true;
        EnsureAntonCollider();
    }

    // ------------------------------------------------------------------
    // cheat codes — jump straight to a quest stage ("task1"/"task2"/...)
    // ------------------------------------------------------------------

    public void CheatJumpToTask(int task)
    {
        if (playerTransform == null)
        {
            ResolveSceneReferences();
        }

        StopAllCoroutines();
        StartCoroutine(CheatJumpRoutine(task));
    }

    private IEnumerator CheatJumpRoutine(int task)
    {
        SequenceActive = false;
        dialogBusy = false;
        encounterStarted = true;
        armed = false;
        EndDialogUi();
        UnlockPlayer();

        if (antonInstance == null)
        {
            // spawn him right in front so the cheat is immediately usable
            Vector3 inFront = playerTransform != null ? playerTransform.forward : Vector3.forward;
            SpawnAntonInDirection(inFront);
        }

        if (scalapendraSpawner != null)
        {
            scalapendraSpawner.Activate();
        }

        WireAntonInteraction();

        // clear any props from a previous run so the jump is clean
        if (snusInstance != null)
        {
            Destroy(snusInstance);
            snusInstance = null;
        }

        if (gateKeysInstance != null)
        {
            Destroy(gateKeysInstance);
            gateKeysInstance = null;
        }

        switch (task)
        {
            case 1:
                CarEscapeController.HasCarKeys = false;
                StartSnusTask();
                break;
            case 2:
                CarEscapeController.HasCarKeys = false;
                StartGateKeysTask();
                break;
            case 3:
                stage = QuestStage.GoToCar;
                CarEscapeController.HasCarKeys = true;
                TaskHud.SetObjective($"Сядь в <color={TaskHud.Highlight}>машину</color> и уезжай");
                break;
            default:
                CarEscapeController.HasCarKeys = false;
                StartSnusTask();
                break;
        }

        yield break;
    }

    private void StartSnusTask()
    {
        stage = QuestStage.FindSnus;
        SpawnSnusInYard();
        TaskHud.SetObjective($"Найди <color={TaskHud.Highlight}>снюс</color> во дворе и принеси Антончику");
    }

    private void StartGateKeysTask()
    {
        stage = QuestStage.FindGateKeys;
        SpawnGateKeys();
        TaskHud.SetObjective($"Найди <color={TaskHud.Highlight}>ключи от ворот</color> у забора");
        StartCoroutine(ScreamerWatcherRoutine());
    }

    private IEnumerator ScreamerWatcherRoutine()
    {
        float delay = UnityEngine.Random.Range(10f, 18f);
        float waited = 0f;
        while (stage == QuestStage.FindGateKeys && waited < 90f)
        {
            waited += Time.deltaTime;
            if (waited >= delay && ScreamerController.PlayerCarriesGun() && !SequenceActive)
            {
                screamerController.TriggerScreamer();
                yield break;
            }

            yield return null;
        }
    }

    private void OnTalkToAnton()
    {
        if (dialogBusy || stage == QuestStage.Dead || GameOverScreen.IsActive)
        {
            return;
        }

        switch (stage)
        {
            case QuestStage.FindSnus:
                StartCoroutine(interactor != null && interactor.HasItemNamed("snus")
                    ? TalkRoutine("snus_return")
                    : TalkRoutine("snus_reminder"));
                break;
            case QuestStage.FindGateKeys:
                StartCoroutine(interactor != null && interactor.HasItemNamed("Ключи от ворот")
                    ? TalkRoutine("keys_return")
                    : TalkRoutine("keys_reminder"));
                break;
            case QuestStage.GoToCar:
                StartCoroutine(TalkRoutine("go_away"));
                break;
        }
    }

    private IEnumerator TalkRoutine(string nodeId)
    {
        dialogBusy = true;
        SequenceActive = true;
        LockPlayer();
        yield return FacePlayerToAnton();

        BuildEncounterCanvas();
        BuildDialogPanel();

        yield return RunDialog(nodeId);

        if (stage != QuestStage.Dead)
        {
            EndDialogUi();
            SequenceActive = false;
            UnlockPlayer();
        }

        dialogBusy = false;
    }

    // ------------------------------------------------------------------
    // dialog tree
    // ------------------------------------------------------------------

    private class DialogChoice
    {
        public string Text;
        public Func<bool> VisibleIf;
        public Func<IEnumerator> Effect;
        public string NextId;
        public int TrustDelta;
    }

    private class DialogNode
    {
        public string Text;
        public readonly List<DialogChoice> Choices = new List<DialogChoice>();
        public string NextId;
        public Func<IEnumerator> Effect;
    }

    private Dictionary<string, DialogNode> BuildDialogTree()
    {
        var nodes = new Dictionary<string, DialogNode>();
        bool HasJameson() => interactor != null && interactor.HasItemNamed("Jameson");
        bool HasSnus() => interactor != null && interactor.HasItemNamed("snus");

        nodes["intro"] = new DialogNode
        {
            Text = "Стоять. Ты кто такой и что забыл на моей территории?.. Деньги есть? Или пивко?",
            Choices =
            {
                new DialogChoice { Text = "Мужчина, денег нет", TrustDelta = -20, NextId = "angry" },
                new DialogChoice
                {
                    Text = "Денег нет, но есть вискарь. Jameson. Будешь?",
                    VisibleIf = HasJameson,
                    TrustDelta = 40,
                    Effect = () => GiveItemToAnton("Jameson"),
                    NextId = "jameson"
                },
                new DialogChoice { Text = "Я просто хочу уйти. Могу чем-то помочь?", TrustDelta = 10, NextId = "tasks_intro" }
            }
        };

        nodes["angry"] = new DialogNode
        {
            Text = "Денег нет?.. Плохо начинаешь, дружок. У меня тут люди пропадают. Последний шанс: чем ты мне полезен?",
            Choices =
            {
                new DialogChoice { Text = "Ничем. Отойди от меня", TrustDelta = -40, NextId = "execution" },
                new DialogChoice
                {
                    Text = "Стой-стой! Вот, вискарь. Jameson. Держи",
                    VisibleIf = HasJameson,
                    TrustDelta = 35,
                    Effect = () => GiveItemToAnton("Jameson"),
                    NextId = "jameson"
                },
                new DialogChoice { Text = "Могу сделать для тебя что-нибудь", TrustDelta = 15, NextId = "tasks_intro" }
            }
        };

        nodes["jameson"] = new DialogNode
        {
            Text = "Оп-па... Джеймсончик! Уважаю. Сразу видно — человек культурный. Ладно, живи пока.",
            NextId = "tasks_intro_friendly"
        };

        nodes["tasks_intro"] = new DialogNode
        {
            Text = "Слушай сюда. Видишь тачку у ворот? Моя. Хочешь свалить — заработай ключи. Сделаешь пару дел — отпущу. Накосячишь — закопаю за сараем.",
            NextId = "task_snus"
        };

        nodes["tasks_intro_friendly"] = new DialogNode
        {
            Text = "Слушай. Тачка у ворот — моя. Подкину тебя до трассы, но сначала помоги по хозяйству. Я тут кое-что посеял.",
            NextId = "task_snus"
        };

        nodes["task_snus"] = new DialogNode
        {
            Text = "Первое. Я где-то во дворе посеял снюс. Без него я злой, а тебе злой Антончик не нужен. Найди и принеси.",
            Choices =
            {
                new DialogChoice
                {
                    Text = "Держи, у меня как раз есть снюс",
                    VisibleIf = HasSnus,
                    TrustDelta = 20,
                    Effect = () => GiveItemToAnton("snus"),
                    NextId = "snus_thanks_instant"
                },
                new DialogChoice { Text = "Хорошо, я поищу", NextId = "task_snus_go" }
            }
        };

        nodes["task_snus_go"] = new DialogNode
        {
            Text = "Давай. И не вздумай бежать — у меня тут вся территория под присмотром. Кое-кто пострашнее меня по двору ползает.",
            Effect = () => SetStageRoutine(QuestStage.FindSnus)
        };

        nodes["snus_thanks_instant"] = new DialogNode
        {
            Text = "Воо, красавчик! Запасливый. Половина дела сделана.",
            NextId = "task_keys"
        };

        nodes["snus_reminder"] = new DialogNode
        {
            Text = "Где мой снюс? Без снюса не возвращайся."
        };

        nodes["snus_return"] = new DialogNode
        {
            Text = "Нашёл? Ну-ка дай сюда.",
            Effect = () => GiveItemToAnton("snus"),
            NextId = "snus_thanks"
        };

        nodes["snus_thanks"] = new DialogNode
        {
            Text = "Воо, красавчик. Сразу жить легче стало. Половина дела сделана.",
            Effect = () => TrustRoutine(15),
            NextId = "task_keys"
        };

        nodes["task_keys"] = new DialogNode
        {
            Text = "Теперь серьёзное. Я потерял ключи от ворот, когда от... неважно от кого бегал. Где-то у забора валяются. Найди — и поговорим о тачке.",
            Choices =
            {
                new DialogChoice { Text = "Понял. У какого забора?", NextId = "task_keys_hint" },
                new DialogChoice { Text = "Может, сам поищешь?", TrustDelta = -15, NextId = "task_keys_rude" }
            }
        };

        nodes["task_keys_hint"] = new DialogNode
        {
            Text = "Если б я помнил у какого — сам бы сходил. Ищи вдоль сетки. И возьми ствол с ящика... по двору всякое шастает.",
            Effect = () => SetStageRoutine(QuestStage.FindGateKeys)
        };

        nodes["task_keys_rude"] = new DialogNode
        {
            Text = "Чегоо? Ты мне условия ставишь? Ищи давай, пока я добрый.",
            Effect = () => SetStageRoutine(QuestStage.FindGateKeys)
        };

        nodes["keys_reminder"] = new DialogNode
        {
            Text = "Ключи от ворот. У забора. Шевели ногами."
        };

        nodes["keys_return"] = new DialogNode
        {
            Text = "Ну ты жук, нашёл-таки. Дай сюда.",
            Effect = () => GiveItemToAnton("Ключи от ворот"),
            NextId = "keys_handover"
        };

        nodes["keys_handover"] = new DialogNode
        {
            Text = "Уговор есть уговор. Держи ключи от тачки. Бак почти пустой, до трассы дотянешь. Вали отсюда, пока я добрый.",
            Effect = HandOverCarKeys,
            NextId = "farewell"
        };

        nodes["farewell"] = new DialogNode
        {
            Text = trust >= 50
                ? "И вот ещё что... ты нормальный мужик. Заезжай, если что. Снюс с тебя."
                : "И не возвращайся. Второй раз я не такой добрый.",
            Effect = AntonWalksAway
        };

        nodes["go_away"] = new DialogNode
        {
            Text = "Чего стоишь? Ключи у тебя. Тачка вон. Вали, не зли меня."
        };

        nodes["silence"] = new DialogNode
        {
            Text = "Ты чё молчишь?! Я с тобой разговариваю!"
        };

        nodes["execution"] = new DialogNode
        {
            Text = "Зря ты так. Очень зря.",
            Effect = ExecutionRoutine
        };

        return nodes;
    }

    private IEnumerator RunDialog(string startId)
    {
        Dictionary<string, DialogNode> nodes = BuildDialogTree();
        string currentId = startId;
        choiceTimedOutOnce = false;

        while (!string.IsNullOrEmpty(currentId) && nodes.TryGetValue(currentId, out DialogNode node))
        {
            // farewell text depends on final trust, so rebuild lazily
            if (currentId == "farewell")
            {
                node.Text = trust >= 50
                    ? "И вот ещё что... ты нормальный мужик. Заезжай, если что. Снюс с тебя."
                    : "И не возвращайся. Второй раз я не такой добрый.";
            }

            yield return TypeDialogLine(node.Text);

            if (node.Effect != null)
            {
                yield return node.Effect();
                if (stage == QuestStage.Dead)
                {
                    yield break;
                }
            }

            List<DialogChoice> visible = node.Choices.FindAll(choice => choice.VisibleIf == null || choice.VisibleIf());
            if (visible.Count == 0)
            {
                yield return new WaitForSeconds(0.9f);
                currentId = node.NextId;
                continue;
            }

            yield return PresentChoices(visible);

            if (chosenIndex < 0)
            {
                // silence: one warning, then a bullet
                Trust(-15);
                if (choiceTimedOutOnce)
                {
                    currentId = "execution";
                    continue;
                }

                choiceTimedOutOnce = true;
                yield return TypeDialogLine(nodes["silence"].Text);
                yield return PresentChoices(visible);
                if (chosenIndex < 0)
                {
                    currentId = "execution";
                    continue;
                }
            }

            DialogChoice picked = visible[chosenIndex];
            Trust(picked.TrustDelta);

            if (trust <= 0)
            {
                currentId = "execution";
                continue;
            }

            if (picked.Effect != null)
            {
                yield return picked.Effect();
                if (stage == QuestStage.Dead)
                {
                    yield break;
                }
            }

            currentId = picked.NextId;
        }
    }

    private void Trust(int delta)
    {
        trust = Mathf.Clamp(trust + delta, -100, 100);
        UpdateRimLight();
    }

    private void UpdateRimLight()
    {
        if (rimLight == null)
        {
            return;
        }

        // the colder the trust, the redder the light behind him
        float anger = Mathf.InverseLerp(40f, 0f, trust);
        rimLight.color = Color.Lerp(new Color(0.55f, 0.65f, 1f), new Color(1f, 0.12f, 0.06f), anger);
    }

    private IEnumerator SetStageRoutine(QuestStage newStage)
    {
        // During the intro the anton interactable doesn't exist yet — defer
        // the actual task start to BeginTasksPhase.
        if (stage == QuestStage.IntroDialog)
        {
            pendingTaskStage = newStage;
            yield break;
        }

        if (newStage == QuestStage.FindGateKeys)
        {
            StartGateKeysTask();
        }
        else if (newStage == QuestStage.FindSnus)
        {
            StartSnusTask();
        }

        yield break;
    }

    private IEnumerator TrustRoutine(int delta)
    {
        Trust(delta);
        yield break;
    }

    // ------------------------------------------------------------------
    // dialog effects
    // ------------------------------------------------------------------

    private IEnumerator GiveItemToAnton(string itemName)
    {
        if (interactor == null || puppeteer == null)
        {
            yield break;
        }

        PickupInteractable item = interactor.TakeItemNamed(itemName);
        if (item == null)
        {
            yield break;
        }

        puppeteer.SetReceiving(true);
        Transform hand = puppeteer.LeftHand;
        Vector3 start = playerCamera != null
            ? playerCamera.transform.position + playerCamera.transform.forward * 0.4f - Vector3.up * 0.15f
            : item.transform.position;
        item.transform.position = start;

        for (float elapsed = 0f; elapsed < 0.8f; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / 0.8f);
            item.transform.position = Vector3.Lerp(start, hand.position, t);
            yield return null;
        }

        item.transform.SetParent(hand, true);
        if (itemName == "Ключи от ворот")
        {
            uiAudioSource.PlayOneShot(HorrorAudio.KeyJingle(), 0.9f);
        }

        yield return new WaitForSeconds(0.5f);
        puppeteer.SetReceiving(false);

        // he pockets it
        Destroy(item.gameObject, 0.4f);
    }

    private IEnumerator HandOverCarKeys()
    {
        uiAudioSource.PlayOneShot(HorrorAudio.KeyJingle(), 1f);
        CarEscapeController.HasCarKeys = true;
        stage = QuestStage.GoToCar;
        TaskHud.SetObjective($"Сядь в <color={TaskHud.Highlight}>машину</color> и уезжай");
        yield return new WaitForSeconds(0.4f);
    }

    private IEnumerator AntonWalksAway()
    {
        EndDialogUi();
        SequenceActive = false;
        UnlockPlayer();

        if (antonTalk != null)
        {
            antonTalk.CanInteract = false;
        }

        scalapendraSpawner.Deactivate();

        if (puppeteer != null)
        {
            GameObject gateObject = GameObject.Find("gate");
            Vector3 destination = gateObject != null
                ? gateObject.transform.position + Vector3.left * 6f
                : antonInstance.transform.position - antonInstance.transform.forward * 15f;
            yield return puppeteer.WalkAway(destination, 1.25f, walkClip);
        }

        if (antonInstance != null)
        {
            Destroy(antonInstance);
        }
    }

    private IEnumerator ExecutionRoutine()
    {
        stage = QuestStage.Dead;
        HideChoices();

        if (puppeteer != null)
        {
            Transform aimTarget = playerCamera != null ? playerCamera.transform : playerTransform;
            puppeteer.SetSpeaking(false);
            puppeteer.BeginAiming(aimTarget, pistolPrefab);
        }

        yield return new WaitForSeconds(1.35f);

        if (puppeteer != null)
        {
            puppeteer.Recoil();
            FireMuzzleFlash(puppeteer.GunMuzzlePosition());
        }

        PlayUiSound(shotSound);
        yield return ShowBloodFlash();

        GameOverScreen.Show("Не надо было злить Антончика.");
    }

    // ------------------------------------------------------------------
    // quest props
    // ------------------------------------------------------------------

    private void SpawnSnusInYard()
    {
        if (snusPrefab == null || snusInstance != null)
        {
            return;
        }

        Vector3 spot = PickHiddenSpot(new[] { "box", "chair", "trees2", "lamp.004", "trees1.001" });
        snusInstance = Instantiate(snusPrefab, spot, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        snusInstance.name = "snus";
        BuiltinPipelineCompatibility.PatchSpawnedObject(snusInstance);
        RestOnGround(snusInstance, spot.y, 0.05f);
        AddSearchGlow(snusInstance, new Color(0.35f, 0.8f, 0.4f, 1f));
    }

    private void SpawnGateKeys()
    {
        if (gateKeysInstance != null)
        {
            return;
        }

        Vector3 spot = PickHiddenSpot(new[] { "fence", "fence.001", "fence.002", "fence.003" });
        gateKeysInstance = BuildKeysProp();
        gateKeysInstance.transform.position = spot;
        RestOnGround(gateKeysInstance, spot.y, 0.02f);
        AddSearchGlow(gateKeysInstance, new Color(0.95f, 0.75f, 0.25f, 1f));
    }

    // Debug cheat: scatters a pile of key props across the yard floor. Used to
    // eyeball the ground-only spawn placement — every prop should land flat on
    // the ground near a fence, never floating, on a roof, or past the fence line.
    // These are throwaway props (not the quest key), so no search glow is added
    // and picking one up does not advance the quest.
    public void CheatSpawnDebugKeys(int count)
    {
        string[] anchors = { "fence", "fence.001", "fence.002", "fence.003" };
        for (int i = 0; i < count; i++)
        {
            Vector3 spot = PickHiddenSpot(anchors);
            GameObject keys = BuildKeysProp();
            keys.name = "Ключи (debug)";
            keys.transform.position = spot;
            RestOnGround(keys, spot.y, 0.02f);
        }
    }

    // Debug cheat: lays key props out on a grid spanning the whole ground floor so
    // you can see spawn coverage across the entire map. Each cell is validated the
    // same way real spawns are, so a marker only appears where there is actual
    // walkable ground — the gaps map out roofs, the exit platform, object footprints
    // and anything off the platform. Markers are kinematic so they stay put on the
    // grid instead of rolling.
    public void CheatSpawnDebugSpawnPoints(float spacing = 3f)
    {
        Collider ground = ResolveGroundFloor();
        if (ground == null)
        {
            return;
        }

        Bounds bounds = ground.bounds;
        spacing = Mathf.Max(0.5f, spacing);
        for (float x = bounds.min.x + 1f; x <= bounds.max.x - 1f; x += spacing)
        {
            for (float z = bounds.min.z + 1f; z <= bounds.max.z - 1f; z += spacing)
            {
                Vector3 column = new Vector3(x, bounds.max.y + 2f, z);
                if (!TrySnapToGround(column, ground, out Vector3 spot))
                {
                    continue;
                }

                GameObject keys = BuildKeysProp();
                keys.name = "Ключи (debug point)";
                keys.transform.position = spot;
                RestOnGround(keys, spot.y, 0.02f);

                Rigidbody body = keys.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.isKinematic = true;
                }
            }
        }
    }

    private Vector3 PickHiddenSpot(string[] anchorNames)
    {
        string anchorName = anchorNames[UnityEngine.Random.Range(0, anchorNames.Length)];
        GameObject anchor = GameObject.Find(anchorName);
        Vector3 basePoint = anchor != null
            ? anchor.transform.position
            : (playerTransform != null ? playerTransform.position : Vector3.zero);

        Collider ground = ResolveGroundFloor();
        if (ground == null)
        {
            return basePoint;
        }

        Bounds bounds = ground.bounds;
        Vector3 center = bounds.center;

        // Anchors like fence posts sit at the yard's edge, so sample *toward* the
        // floor's interior. Every candidate is then clamped inside the floor
        // footprint, so jitter can never throw a spot past the fence into the
        // void (where the old code left keys floating out of bounds).
        Vector3 inward = center - basePoint;
        inward.y = 0f;
        inward = inward.sqrMagnitude > 0.01f ? inward.normalized : Vector3.forward;
        Vector3 lateral = Vector3.Cross(Vector3.up, inward);

        for (int attempt = 0; attempt < 24; attempt++)
        {
            float forward = UnityEngine.Random.Range(0.8f, 3.2f);
            float side = UnityEngine.Random.Range(-1.6f, 1.6f);
            Vector3 candidate = basePoint + inward * forward + lateral * side;
            candidate.x = Mathf.Clamp(candidate.x, bounds.min.x + 0.4f, bounds.max.x - 0.4f);
            candidate.z = Mathf.Clamp(candidate.z, bounds.min.z + 0.4f, bounds.max.z - 0.4f);

            if (TrySnapToGround(candidate, ground, out Vector3 grounded))
            {
                return grounded;
            }
        }

        // Guaranteed safe spot: straight down onto the floor at the yard centre,
        // which is always clear, solid ground. This never returns a floating or
        // out-of-bounds point.
        Vector3 centreColumn = new Vector3(center.x, bounds.max.y + 2f, center.z);
        if (TrySnapToGround(centreColumn, ground, out Vector3 centreSpot))
        {
            return centreSpot;
        }

        return new Vector3(center.x, bounds.max.y, center.z);
    }

    // Resolves the walkable ground surface once. The yard's floor is an object
    // named "ground" that carries TWO colliders: a large convex mesh (the real
    // walkable surface) and a tiny 2x2 box. We want the big one, so we pick the
    // collider with the largest XZ footprint among every object named "ground".
    // Choosing by footprint also sidesteps GameObject.Find ambiguity (the model
    // root is *also* named "ground") and never latches onto the wrong collider.
    private Collider ResolveGroundFloor()
    {
        if (groundFloorCollider != null)
        {
            return groundFloorCollider;
        }

        float bestFootprint = 0f;
        foreach (Transform candidate in FindObjectsOfType<Transform>())
        {
            if (candidate.name != "ground")
            {
                continue;
            }

            foreach (Collider collider in candidate.GetComponents<Collider>())
            {
                Vector3 size = collider.bounds.size;
                float footprint = size.x * size.z;
                if (footprint > bestFootprint)
                {
                    bestFootprint = footprint;
                    groundFloorCollider = collider;
                }
            }
        }

        if (groundFloorCollider == null)
        {
            // Fallback: whatever the player is standing on, skipping their own
            // capsule so we cache the floor and not the player.
            Transform probe = playerTransform != null ? playerTransform : transform;
            RaycastHit[] hits = Physics.RaycastAll(probe.position + Vector3.up * 5f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsPlayerCollider(hits[i].collider))
                {
                    continue;
                }

                groundFloorCollider = hits[i].collider;
                break;
            }
        }

        return groundFloorCollider;
    }

    // Builds, once, the XZ footprints that props must not spawn under: the house
    // and every lamp. The building has a collider but its roof sits above the
    // sample column, and the lamps have no colliders at all, so neither is caught
    // by the downward raycast — we reject by footprint instead.
    private void EnsureSpawnBlockers()
    {
        if (spawnBlockersGathered)
        {
            return;
        }

        spawnBlockersGathered = true;
        spawnBlockers.Clear();

        foreach (Transform candidate in FindObjectsOfType<Transform>())
        {
            bool isBlocker = candidate.name == "building" || candidate.name.StartsWith("lamp");
            if (!isBlocker)
            {
                continue;
            }

            // Only the yard-level objects (direct children of the model root).
            if (candidate.parent == null || candidate.parent.name != "ground")
            {
                continue;
            }

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                continue;
            }

            Bounds footprint = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                footprint.Encapsulate(renderers[i].bounds);
            }

            // Pad the XZ extent so keys keep a little clearance from walls/poles.
            footprint.Expand(new Vector3(0.6f, 0f, 0.6f));
            spawnBlockers.Add(footprint);
        }
    }

    // True when the point sits under the house or a lamp and should be skipped.
    private bool IsBlockedSpot(Vector3 point)
    {
        EnsureSpawnBlockers();
        for (int i = 0; i < spawnBlockers.Count; i++)
        {
            Bounds footprint = spawnBlockers[i];
            if (point.x >= footprint.min.x && point.x <= footprint.max.x &&
                point.z >= footprint.min.z && point.z <= footprint.max.z)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPlayerCollider(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        if (playerController != null && collider.transform.IsChildOf(playerController.transform))
        {
            return true;
        }

        return playerRigidbody != null && collider.attachedRigidbody == playerRigidbody;
    }

    // Casts a vertical column at the given position and reports where it meets the
    // ground. Fence posts and the gate are stepped over so props rest beside them
    // rather than balanced on a rail; the first solid surface after that must be
    // the ground floor, otherwise the spot is rejected — that keeps props off
    // roofs, the exit platform and out of the insides of yard objects.
    private bool TrySnapToGround(Vector3 column, Collider ground, out Vector3 point)
    {
        point = column;
        RaycastHit[] hits = Physics.RaycastAll(column + Vector3.up * 8f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (fenceColliders.Contains(hit))
            {
                continue;
            }

            // Compare by GameObject, not collider reference: the floor carries
            // both a MeshCollider and a BoxCollider, and the ray may report either.
            if (ground == null || hit == ground || hit.gameObject == ground.gameObject)
            {
                // Even on valid ground, skip spots tucked under the house or a
                // lamp — those structures don't block the column from above.
                if (IsBlockedSpot(hits[i].point))
                {
                    return false;
                }

                point = hits[i].point;
                return true;
            }

            // First real surface under the column is something other than the
            // ground (a roof, the exit platform, a prop). Reject so the caller
            // resamples an open spot instead of burying the item.
            return false;
        }

        return false;
    }

    // Lifts a freshly spawned prop so its lowest visible point sits just above
    // the ground instead of being half-buried by a centred mesh pivot.
    private static void RestOnGround(GameObject obj, float groundY, float clearance)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float lift = (groundY + clearance) - bounds.min.y;
        obj.transform.position += Vector3.up * lift;
    }

    private static void AddSearchGlow(GameObject target, Color color)
    {
        GameObject glowObject = new GameObject("Search Glow");
        glowObject.transform.SetParent(target.transform, false);
        glowObject.transform.localPosition = Vector3.up * 0.25f;
        Light glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 2.2f;
        glow.intensity = 1.1f;
        glowObject.AddComponent<PulsingLight>();
    }

    private GameObject BuildKeysProp()
    {
        GameObject root = new GameObject("Ключи от ворот");

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Ring";
        ring.transform.SetParent(root.transform, false);
        ring.transform.localScale = new Vector3(0.09f, 0.006f, 0.09f);
        TintPrimitive(ring, new Color(0.7f, 0.6f, 0.3f, 1f));

        for (int i = 0; i < 2; i++)
        {
            GameObject key = GameObject.CreatePrimitive(PrimitiveType.Cube);
            key.name = "Key " + i;
            key.transform.SetParent(root.transform, false);
            key.transform.localPosition = new Vector3(0.04f + i * 0.015f, 0.004f, i * 0.02f);
            key.transform.localRotation = Quaternion.Euler(0f, 25f + i * 40f, 0f);
            key.transform.localScale = new Vector3(0.085f, 0.006f, 0.02f);
            TintPrimitive(key, new Color(0.62f, 0.6f, 0.55f, 1f));
        }

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 0.2f;
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0.01f, -0.009f, -0.039f);
        collider.size = new Vector3(0.019f, 0.086f, 0.155f);
        root.AddComponent<PickupInteractable>();
        return root;
    }

    private static void TintPrimitive(GameObject primitive, Color color)
    {
        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------------
    // player / anton plumbing
    // ------------------------------------------------------------------

    private void LockPlayer()
    {
        if (playerWasLocked)
        {
            return;
        }

        playerWasLocked = true;
        if (playerController != null)
        {
            playerController.playerCanMove = false;
            playerController.cameraCanMove = false;
            playerController.enableJump = false;
            playerController.SetCrouchEnabled(false);
            playerController.enabled = false;
        }

        if (playerRigidbody != null)
        {
            playerRigidbody.velocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.isKinematic = true;
        }
    }

    private void UnlockPlayer()
    {
        if (!playerWasLocked)
        {
            return;
        }

        playerWasLocked = false;
        if (playerRigidbody != null)
        {
            playerRigidbody.isKinematic = false;
        }

        if (playerController != null)
        {
            playerController.enabled = true;
            playerController.playerCanMove = true;
            playerController.cameraCanMove = true;
            playerController.enableJump = true;
            playerController.SetCrouchEnabled(true);
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        SoftwareCursor.Show(false);
    }

    private void SpawnAntonBehindPlayer()
    {
        if (playerTransform == null)
        {
            return;
        }

        SpawnAntonInDirection(-playerTransform.forward);
    }

    private void SpawnAntonInDirection(Vector3 dirFromPlayer)
    {
if (antonPrefab == null || playerTransform == null)
{
    return;
}

Vector3 backDirection = dirFromPlayer;
backDirection.y = 0f;
backDirection.Normalize();

// spawn in front/behind player
Vector3 spawnPosition = playerTransform.position + backDirection * spawnDistance;

// start ray high above spawn point
Vector3 rayOrigin = spawnPosition + Vector3.up * 10f;

RaycastHit hit;

if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 100f))
{
    spawnPosition = hit.point;

    antonInstance = Instantiate(
        antonPrefab,
        spawnPosition,
        Quaternion.LookRotation(-backDirection, Vector3.up)
    );

    // --- Rigidbody ---
    Rigidbody rb = antonInstance.GetComponent<Rigidbody>();
    if (rb == null)
        rb = antonInstance.AddComponent<Rigidbody>();

    rb.useGravity = true;
    rb.isKinematic = false;
    rb.mass = 9999f;

    rb.constraints =
        RigidbodyConstraints.FreezePositionX |
        RigidbodyConstraints.FreezePositionZ |
        RigidbodyConstraints.FreezeRotation;

    // --- Collider ---
    CapsuleCollider col = antonInstance.GetComponent<CapsuleCollider>();
    if (col == null)
        col = antonInstance.AddComponent<CapsuleCollider>();

    col.center = new Vector3(-0.03f, 3.11f, 0f);
    col.height = 6.05f;
    col.radius = 1.17f;

    // --- Scale ---
    antonInstance.transform.localScale = new Vector3(0.58f, 0.58f, 0.58f);
}


        Animator animator = antonInstance.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            if (idleClip != null && animator.runtimeAnimatorController != null)
            {
                AnimatorOverrideController overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
                List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(overrides);
                for (int i = 0; i < overrides.Count; i++)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, idleClip);
                }

                overrideController.ApplyOverrides(overrides);
                animator.runtimeAnimatorController = overrideController;
            }
        }

        puppeteer = antonInstance.AddComponent<AntonchikPuppeteer>();
        puppeteer.Initialise();

        CreateAntonRimLight(spawnPosition, backDirection);
        BuiltinPipelineCompatibility.PatchSpawnedObject(antonInstance);
    }

    private void EnsureAntonCollider()
    {
        if (antonInstance == null || antonInstance.GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        CapsuleCollider capsule = antonInstance.AddComponent<CapsuleCollider>();

        // Fit the capsule to the visible mesh so the talk raycast lands on his
        // body regardless of the prefab's scale; fall back to human dimensions.
        Renderer[] renderers = antonInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float height = bounds.size.y;
            capsule.height = height;
            capsule.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
            capsule.center = antonInstance.transform.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y + height * 0.5f, bounds.center.z));
        }
        else
        {
            capsule.center = Vector3.up * 0.9f;
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
        }
    }

    private void CreateAntonRimLight(Vector3 antonPosition, Vector3 awayFromPlayer)
    {
        GameObject rimObject = new GameObject("Antonchik Rim Light");
        rimObject.transform.SetParent(antonInstance.transform, true);
        rimObject.transform.position = antonPosition + awayFromPlayer * 1.4f + Vector3.up * 2.4f;

        rimLight = rimObject.AddComponent<Light>();
        rimLight.type = LightType.Point;
        rimLight.color = new Color(0.55f, 0.65f, 1f, 1f);
        rimLight.intensity = 2.2f;
        rimLight.range = 5f;

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = rimObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(900f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
            hdData.affectsVolumetric = true;
        }
    }

    private float SampleGroundHeight(Vector3 position)
    {
        Vector3 origin = position + Vector3.up * 3f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }

        return playerTransform.position.y - 1.2f;
    }

    private void PlayJumpscareSound()
    {
        if (jumpscareSound != null && worldAudioSource != null)
        {
            worldAudioSource.PlayOneShot(jumpscareSound, 1f);
        }
    }

    private IEnumerator TurnPlayerTowardsAnton()
    {
        if (antonInstance == null || playerTransform == null)
        {
            yield break;
        }

        // Aim the camera at his actual head/face rather than a fixed height —
        // his scale and the ground-snap on spawn move the real face around.
        Vector3 lookTarget = puppeteer != null
            ? puppeteer.FaceWorldPosition()
            : antonInstance.transform.position + Vector3.up * 1.62f;

        Quaternion startBodyRotation = playerTransform.rotation;
        Vector3 flatDirection = antonInstance.transform.position - playerTransform.position;
        flatDirection.y = 0f;
        Quaternion targetBodyRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);

        Transform cameraTransform = playerCamera != null ? playerCamera.transform : null;
        Quaternion startCameraLocalRotation = cameraTransform != null ? cameraTransform.localRotation : Quaternion.identity;

        for (float elapsed = 0f; elapsed < turnDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / turnDuration));
            playerTransform.rotation = Quaternion.Slerp(startBodyRotation, targetBodyRotation, t);

            if (cameraTransform != null)
            {
                Quaternion lookRotation = Quaternion.LookRotation((lookTarget - cameraTransform.position).normalized, Vector3.up);
                Quaternion targetLocalRotation = Quaternion.Inverse(playerTransform.rotation) * lookRotation;
                cameraTransform.localRotation = Quaternion.Slerp(startCameraLocalRotation, targetLocalRotation, t);
            }

            yield return null;
        }

        playerTransform.rotation = targetBodyRotation;

        if (cameraTransform != null)
        {
            Quaternion finalLook = Quaternion.LookRotation((lookTarget - cameraTransform.position).normalized, Vector3.up);
            cameraTransform.localRotation = Quaternion.Inverse(playerTransform.rotation) * finalLook;
        }
    }

    private IEnumerator FacePlayerToAnton()
    {
        yield return TurnPlayerTowardsAnton();

        // anton also turns to the player
        if (antonInstance != null && playerTransform != null)
        {
            Vector3 toPlayer = playerTransform.position - antonInstance.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
            {
                antonInstance.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            }
        }
    }

    // ------------------------------------------------------------------
    // dialog UI
    // ------------------------------------------------------------------

    private void BuildEncounterCanvas()
    {
        if (encounterCanvas != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Antonchik Encounter Canvas");
        encounterCanvas = canvasObject.AddComponent<Canvas>();
        encounterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        encounterCanvas.sortingOrder = 5000;
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }

    private void BuildDialogPanel()
    {
        if (dialogRoot != null)
        {
            dialogRoot.SetActive(true);
            return;
        }

        dialogRoot = CreatePanel("Dialog Root", encounterCanvas.transform, Color.clear);
        RectTransform dialogRect = dialogRoot.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0f, 0f);
        dialogRect.anchorMax = new Vector2(1f, 0f);
        dialogRect.pivot = new Vector2(0.5f, 0f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(0f, 360f);

        GameObject backdrop = CreatePanel("Dialog Backdrop", dialogRoot.transform, new Color(0f, 0f, 0f, 0.86f));
        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        GameObject topLine = CreatePanel("Dialog Top Line", dialogRoot.transform, new Color(0.45f, 0.02f, 0.02f, 0.9f));
        RectTransform topLineRect = topLine.GetComponent<RectTransform>();
        topLineRect.anchorMin = new Vector2(0f, 1f);
        topLineRect.anchorMax = new Vector2(1f, 1f);
        topLineRect.pivot = new Vector2(0.5f, 1f);
        topLineRect.anchoredPosition = Vector2.zero;
        topLineRect.sizeDelta = new Vector2(0f, 3f);

        speakerLabel = CreateLabel("Speaker Label", dialogRoot.transform, speakerName, 40, new Color(0.62f, 0.04f, 0.04f, 1f), TextAlignmentOptions.Left);
        RectTransform speakerRect = speakerLabel.GetComponent<RectTransform>();
        speakerRect.anchorMin = new Vector2(0f, 1f);
        speakerRect.anchorMax = new Vector2(0f, 1f);
        speakerRect.pivot = new Vector2(0f, 1f);
        speakerRect.anchoredPosition = new Vector2(170f, -22f);
        speakerRect.sizeDelta = new Vector2(900f, 52f);

        dialogLabel = CreateLabel("Dialog Label", dialogRoot.transform, string.Empty, 38, new Color(0.92f, 0.9f, 0.88f, 1f), TextAlignmentOptions.TopLeft);
        RectTransform dialogLabelRect = dialogLabel.GetComponent<RectTransform>();
        dialogLabelRect.anchorMin = new Vector2(0f, 1f);
        dialogLabelRect.anchorMax = new Vector2(1f, 1f);
        dialogLabelRect.pivot = new Vector2(0f, 1f);
        dialogLabelRect.anchoredPosition = new Vector2(170f, -80f);
        dialogLabelRect.offsetMax = new Vector2(-170f, -80f);
        dialogLabelRect.sizeDelta = new Vector2(-340f, 110f);
    }

    private void EndDialogUi()
    {
        if (dialogRoot != null)
        {
            dialogRoot.SetActive(false);
        }

        HideChoices();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        SoftwareCursor.Show(false);
    }

    private IEnumerator TypeDialogLine(string line)
    {
        HideChoices();
        if (puppeteer != null)
        {
            puppeteer.SetSpeaking(true);
        }

        float secondsPerLetter = 1f / Mathf.Max(1f, lettersPerSecond);

        dialogLabel.text = string.Empty;
        for (int i = 0; i < line.Length; i++)
        {
            dialogLabel.text = line.Substring(0, i + 1);
            if (!char.IsWhiteSpace(line[i]))
            {
                PlayUiSound(typingSound);
            }

            yield return new WaitForSeconds(secondsPerLetter);
        }

        PlayUiSound(lineFinishedSound);
        if (puppeteer != null)
        {
            puppeteer.SetSpeaking(false);
        }
    }

    private IEnumerator PresentChoices(List<DialogChoice> choices)
    {
        chosenIndex = -1;
        Cursor.lockState = CursorLockMode.None;
        SoftwareCursor.Show(true);

        choicesRoot = CreatePanel("Choices Root", dialogRoot.transform, Color.clear);
        RectTransform choicesRect = choicesRoot.GetComponent<RectTransform>();
        choicesRect.anchorMin = new Vector2(0f, 0f);
        choicesRect.anchorMax = new Vector2(0f, 0f);
        choicesRect.pivot = new Vector2(0f, 0f);
        choicesRect.anchoredPosition = new Vector2(170f, 18f);
        choicesRect.sizeDelta = new Vector2(820f, 70f * choices.Count + 22f);

        GameObject track = CreatePanel("Timer Track", choicesRoot.transform, new Color(0.16f, 0.05f, 0.05f, 0.9f));
        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 1f);
        trackRect.anchorMax = new Vector2(1f, 1f);
        trackRect.pivot = new Vector2(0f, 1f);
        trackRect.anchoredPosition = new Vector2(0f, 0f);
        trackRect.sizeDelta = new Vector2(0f, 8f);

        GameObject fill = CreatePanel("Timer Fill", track.transform, new Color(0.78f, 0.05f, 0.04f, 1f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        timerFill = fill.GetComponent<Image>();

        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            GameObject buttonObject = CreatePanel("Choice " + i, choicesRoot.transform, new Color(0.07f, 0.07f, 0.08f, 0.95f));
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(0f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, (choices.Count - 1 - i) * 70f);
            buttonRect.sizeDelta = new Vector2(0f, 60f);

            GameObject frame = CreatePanel("Choice Frame", buttonObject.transform, new Color(0.5f, 0.08f, 0.08f, 0.85f));
            RectTransform frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0f, 0f);
            frameRect.anchorMax = new Vector2(0f, 1f);
            frameRect.pivot = new Vector2(0f, 0.5f);
            frameRect.anchoredPosition = Vector2.zero;
            frameRect.sizeDelta = new Vector2(5f, 0f);

            TextMeshProUGUI buttonLabel = CreateLabel("Choice Label", buttonObject.transform, "> " + choices[i].Text, 28, new Color(0.9f, 0.86f, 0.82f, 1f), TextAlignmentOptions.MidlineLeft);
            RectTransform buttonLabelRect = buttonLabel.GetComponent<RectTransform>();
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelRect.offsetMin = new Vector2(26f, 0f);
            buttonLabelRect.offsetMax = new Vector2(-12f, 0f);

            Button choiceButton = buttonObject.AddComponent<Button>();
            choiceButton.targetGraphic = buttonObject.GetComponent<Image>();
            ColorBlock colors = choiceButton.colors;
            colors.highlightedColor = new Color(1.6f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(2f, 1.4f, 1.4f, 1f);
            choiceButton.colors = colors;
            choiceButton.onClick.AddListener(() => chosenIndex = index);
        }

        float remaining = replyTimeLimit;
        while (chosenIndex < 0 && remaining > 0f)
        {
            remaining -= Time.deltaTime;
            if (timerFill != null)
            {
                timerFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(remaining / replyTimeLimit), 1f);
            }

            yield return null;
        }

        HideChoices();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        SoftwareCursor.Show(false);

        if (dialogLabel != null)
        {
            dialogLabel.text = string.Empty;
        }

        yield return new WaitForSeconds(0.4f);
    }

    private void HideChoices()
    {
        if (choicesRoot != null)
        {
            Destroy(choicesRoot);
            choicesRoot = null;
        }
    }

    private void FireMuzzleFlash(Vector3 flashPosition)
    {
        GameObject flashObject = new GameObject("Muzzle Flash");
        flashObject.transform.position = flashPosition;
        Light flashLight = flashObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.78f, 0.45f, 1f);
        flashLight.intensity = 9f;
        flashLight.range = 9f;

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            var hdData = flashObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            hdData.SetIntensity(60000f, UnityEngine.Rendering.HighDefinition.LightUnit.Lumen);
            hdData.affectsVolumetric = true;
        }

        Destroy(flashObject, 0.08f);
    }

    private IEnumerator ShowBloodFlash()
    {
        GameObject redFlash = CreatePanel("Blood Flash", encounterCanvas.transform, new Color(0.45f, 0f, 0f, 0.55f));
        RectTransform redFlashRect = redFlash.GetComponent<RectTransform>();
        redFlashRect.anchorMin = Vector2.zero;
        redFlashRect.anchorMax = Vector2.one;
        redFlashRect.offsetMin = Vector2.zero;
        redFlashRect.offsetMax = Vector2.zero;
        yield return new WaitForSeconds(0.14f);
        Destroy(redFlash);
    }

    private void PlayUiSound(AudioClip clip)
    {
        if (clip != null && uiAudioSource != null)
        {
            uiAudioSource.PlayOneShot(clip);
        }
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = color.a > 0.01f;
        return panel;
    }

    private TextMeshProUGUI CreateLabel(string name, Transform parent, string text, int fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }
}

// soft pulsing helper for quest item glows
public class PulsingLight : MonoBehaviour
{
    private Light glow;
    private float baseIntensity;
    private float seed;

    private void Awake()
    {
        glow = GetComponent<Light>();
        baseIntensity = glow != null ? glow.intensity : 1f;
        seed = UnityEngine.Random.value * 10f;
    }

    private void Update()
    {
        if (glow != null)
        {
            glow.intensity = baseIntensity * (0.6f + Mathf.PingPong(Time.time * 0.9f + seed, 0.8f));
        }
    }
}

public static class ProceduralJumpscareSting
{
    public static AudioClip Create()
    {
        const int sampleRate = 44100;
        const float duration = 1.6f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random random = new System.Random(666);

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float progress = time / duration;

            float attackEnvelope = Mathf.Exp(-time * 5.5f);
            float swellEnvelope = Mathf.Sin(Mathf.Clamp01(progress * 1.25f) * Mathf.PI);

            float noise = ((float)random.NextDouble() * 2f - 1f) * 0.55f * attackEnvelope;

            float cluster =
                Mathf.Sin(2f * Mathf.PI * 657f * time) * 0.2f +
                Mathf.Sin(2f * Mathf.PI * 693f * time) * 0.18f +
                Mathf.Sin(2f * Mathf.PI * 712f * time) * 0.16f;
            cluster *= attackEnvelope;

            float lowDrone =
                Mathf.Sin(2f * Mathf.PI * 55f * time) * 0.42f +
                Mathf.Sin(2f * Mathf.PI * 58.3f * time) * 0.38f +
                Mathf.Sin(2f * Mathf.PI * 110.7f * time) * 0.2f;
            lowDrone *= swellEnvelope;

            float sample = noise + cluster + lowDrone;
            samples[i] = Mathf.Clamp(sample * 0.9f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("AntonchikJumpscareSting", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
