using UnityEngine;
using UnityEngine.SceneManagement;

public class CheatCodeController : MonoBehaviour
{
    private static CheatCodeController activeInstance;

    [Header("Entry")]
    [SerializeField] private KeyCode firstEntryKey = KeyCode.Alpha6;
    [SerializeField] private KeyCode secondEntryKey = KeyCode.Alpha7;
    [SerializeField] private float entrySequenceTimeout = 1.25f;

    [Header("Scene Cheats")]
    [SerializeField] private string scene2Name = "gamePlay";
    [SerializeField] private string scene3Name = "continueGamePlay";

    [Header("Noclip")]
    [SerializeField] private float noclipSpeed = 8f;
    [SerializeField] private float fastNoclipSpeed = 18f;
    [SerializeField] private KeyCode fastMoveKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode moveUpKey = KeyCode.Space;
    [SerializeField] private KeyCode moveDownKey = KeyCode.LeftControl;

    private FirstPersonController firstPersonController;
    private Rigidbody playerRigidbody;
    private Collider[] playerColliders;
    private bool waitingForSecondEntryKey;
    private float entrySequenceTimer;
    private bool isEnteringCode;
    private int ignoreTypingUntilFrame = -1;
    private string currentCode = string.Empty;
    private bool noclipEnabled;
    private bool previousPlayerCanMove;
    private bool previousJumpEnabled;
    private bool previousCrouchEnabled;
    private bool previousUseGravity;
    private bool previousIsKinematic;
    private CollisionDetectionMode previousCollisionDetectionMode;
    private bool isStandaloneInstance;

    private void Awake()
    {
        isStandaloneInstance = GetComponent<FirstPersonController>() == null;

        if (activeInstance != null && activeInstance != this)
        {
            enabled = false;
            return;
        }

        activeInstance = this;

        if (isStandaloneInstance)
        {
            DontDestroyOnLoad(gameObject);
        }

        ResolvePlayerReferences();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResolvePlayerReferences();
    }

    private void Update()
    {
        if (firstPersonController == null)
        {
            ResolvePlayerReferences();
        }

        HandleEntryKeys();

        if (isEnteringCode)
        {
            HandleCodeTyping();
            return;
        }

        if (noclipEnabled)
        {
            MoveNoclip();
        }
    }

    private void HandleEntryKeys()
    {
        if (Input.GetKeyDown(firstEntryKey))
        {
            waitingForSecondEntryKey = true;
            entrySequenceTimer = entrySequenceTimeout;
        }

        if (waitingForSecondEntryKey)
        {
            entrySequenceTimer -= Time.unscaledDeltaTime;
            if (entrySequenceTimer <= 0f)
            {
                waitingForSecondEntryKey = false;
            }
        }

        if ((waitingForSecondEntryKey && Input.GetKeyDown(secondEntryKey)) ||
            (Input.GetKey(firstEntryKey) && Input.GetKeyDown(secondEntryKey)))
        {
            waitingForSecondEntryKey = false;
            OpenCodeEntry();
        }
    }

    private void OpenCodeEntry()
    {
        isEnteringCode = true;
        ignoreTypingUntilFrame = Time.frameCount + 1;
        currentCode = string.Empty;
    }

    private void HandleCodeTyping()
    {
        if (Time.frameCount <= ignoreTypingUntilFrame)
        {
            return;
        }

        foreach (char typed in Input.inputString)
        {
            if (typed == '\b')
            {
                if (currentCode.Length > 0)
                {
                    currentCode = currentCode.Substring(0, currentCode.Length - 1);
                }
            }
            else if (typed == '\n' || typed == '\r')
            {
                SubmitCode();
            }
            else if (!char.IsControl(typed))
            {
                currentCode += char.ToLowerInvariant(typed);
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            isEnteringCode = false;
            currentCode = string.Empty;
        }
    }

    private void SubmitCode()
    {
        string code = currentCode.Trim().ToLowerInvariant();
        isEnteringCode = false;
        currentCode = string.Empty;

        switch (code)
        {
            case "noclip":
            case "fly":
                SetNoclip(!noclipEnabled);
                break;
            case "clip":
            case "walk":
                SetNoclip(false);
                break;
            case "scene2":
            case "gamescene":
                LoadCheatScene(scene2Name);
                break;
            case "scene3":
            case "continuegamescene":
                LoadCheatScene(scene3Name);
                break;
        }
    }

    private void LoadCheatScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        if (noclipEnabled)
        {
            SetNoclip(false);
        }

        SceneManager.LoadScene(sceneName);
    }

    private void SetNoclip(bool enabled)
    {
        ResolvePlayerReferences();

        if (firstPersonController == null && enabled)
        {
            return;
        }

        if (noclipEnabled == enabled)
        {
            return;
        }

        noclipEnabled = enabled;

        if (enabled)
        {
            previousPlayerCanMove = firstPersonController == null || firstPersonController.playerCanMove;
            previousJumpEnabled = firstPersonController == null || firstPersonController.enableJump;
            previousCrouchEnabled = firstPersonController == null || firstPersonController.enableCrouch;

            if (firstPersonController != null)
            {
                if (firstPersonController.IsCrouched)
                {
                    firstPersonController.ForceUncrouch();
                }

                firstPersonController.playerCanMove = false;
                firstPersonController.enableJump = false;
                firstPersonController.SetCrouchEnabled(false);
            }

            if (playerRigidbody != null)
            {
                previousUseGravity = playerRigidbody.useGravity;
                previousIsKinematic = playerRigidbody.isKinematic;
                previousCollisionDetectionMode = playerRigidbody.collisionDetectionMode;
                playerRigidbody.velocity = Vector3.zero;
                playerRigidbody.angularVelocity = Vector3.zero;
                playerRigidbody.useGravity = false;
                playerRigidbody.isKinematic = true;
                playerRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            SetPlayerColliders(false);
            return;
        }

        if (firstPersonController != null)
        {
            firstPersonController.playerCanMove = previousPlayerCanMove;
            firstPersonController.enableJump = previousJumpEnabled;
            firstPersonController.SetCrouchEnabled(previousCrouchEnabled);
        }

        if (playerRigidbody != null)
        {
            playerRigidbody.isKinematic = previousIsKinematic;
            playerRigidbody.useGravity = previousUseGravity;
            playerRigidbody.collisionDetectionMode = previousCollisionDetectionMode;
            playerRigidbody.velocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }

        SetPlayerColliders(true);
    }

    private void SetPlayerColliders(bool enabled)
    {
        for (int i = 0; i < playerColliders.Length; i++)
        {
            playerColliders[i].enabled = enabled;
        }
    }

    private void MoveNoclip()
    {
        if (firstPersonController == null)
        {
            SetNoclip(false);
            return;
        }

        float speed = Input.GetKey(fastMoveKey) ? fastNoclipSpeed : noclipSpeed;
        Transform playerTransform = firstPersonController.transform;
        Vector3 move = playerTransform.right * Input.GetAxisRaw("Horizontal") + playerTransform.forward * Input.GetAxisRaw("Vertical");

        if (Input.GetKey(moveUpKey))
        {
            move += Vector3.up;
        }

        if (Input.GetKey(moveDownKey))
        {
            move += Vector3.down;
        }

        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        playerTransform.position += move * speed * Time.deltaTime;
    }

    private void ResolvePlayerReferences()
    {
        FirstPersonController resolvedController = GetComponent<FirstPersonController>();
        if (resolvedController == null)
        {
            resolvedController = FindObjectOfType<FirstPersonController>();
        }

        if (firstPersonController == resolvedController && playerRigidbody != null)
        {
            return;
        }

        firstPersonController = resolvedController;
        playerRigidbody = firstPersonController != null ? firstPersonController.GetComponent<Rigidbody>() : null;
        playerColliders = firstPersonController != null
            ? firstPersonController.GetComponentsInChildren<Collider>(true)
            : new Collider[0];
    }

    private void OnGUI()
    {
        if (!isEnteringCode && !noclipEnabled)
        {
            return;
        }

        GUI.Box(new Rect(16f, 16f, 320f, isEnteringCode ? 58f : 34f), string.Empty);

        if (isEnteringCode)
        {
            GUI.Label(new Rect(28f, 24f, 300f, 20f), "Cheat code:");
            GUI.Label(new Rect(28f, 46f, 300f, 20f), currentCode + "_");
            return;
        }

        GUI.Label(new Rect(28f, 24f, 300f, 20f), "NOCLIP ENABLED");
    }
}
