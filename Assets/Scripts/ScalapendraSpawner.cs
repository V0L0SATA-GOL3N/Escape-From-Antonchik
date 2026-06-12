using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Randomly places scalapendras around the yard with skittering sounds.
// The GLB animation writes garbage into the model root's localPosition, so
// each spawn is wrapped in a container and the model is pinned back to local
// zero every LateUpdate — the container does the actual crawling.
public class ScalapendraSpawner : MonoBehaviour
{
    [SerializeField] private GameObject scalapendraPrefab;
    [SerializeField] private RuntimeAnimatorController scalapendraController;
    [SerializeField] private string animationStateName = "CINEMA_4D_Main";
    [SerializeField] private int maxAlive = 3;
    [SerializeField] private float minSpawnDistance = 9f;
    [SerializeField] private float maxSpawnDistance = 24f;
    [SerializeField] private float spawnIntervalMin = 14f;
    [SerializeField] private float spawnIntervalMax = 32f;
    [SerializeField] private float crawlSpeed = 1.1f;
    [SerializeField] private float lifeTime = 55f;

    private Transform player;
    private readonly List<GameObject> alive = new List<GameObject>();
    private bool running;

    private void Awake()
    {
#if UNITY_EDITOR
        if (scalapendraPrefab == null)
        {
            scalapendraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/scalapendra.prefab");
        }

        if (scalapendraController == null)
        {
            scalapendraController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/3d/scalapendra.controller");
        }
#endif
    }

    public void Activate()
    {
        if (running)
        {
            return;
        }

        running = true;
        FirstPersonController controller = FindObjectOfType<FirstPersonController>();
        player = controller != null ? controller.transform : null;
        StartCoroutine(SpawnLoop());
    }

    public void Deactivate()
    {
        running = false;
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            if (alive[i] != null)
            {
                Destroy(alive[i]);
            }
        }

        alive.Clear();
    }

    private IEnumerator SpawnLoop()
    {
        // first one shows up fairly quickly to set the mood
        yield return new WaitForSeconds(Random.Range(4f, 9f));

        while (running)
        {
            alive.RemoveAll(item => item == null);
            if (alive.Count < maxAlive && player != null && !AntonchikFenceEncounter.SequenceActive)
            {
                TrySpawnOne();
            }

            yield return new WaitForSeconds(Random.Range(spawnIntervalMin, spawnIntervalMax));
        }
    }

    private void TrySpawnOne()
    {
        if (scalapendraPrefab == null)
        {
            return;
        }

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(minSpawnDistance, maxSpawnDistance);
            Vector3 candidate = player.position + new Vector3(ring.x, 0f, ring.y);
            Vector3 origin = candidate + Vector3.up * 6f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            if (Vector3.Distance(hit.point, player.position) < minSpawnDistance * 0.8f)
            {
                continue;
            }

            GameObject container = new GameObject("Scalapendra Crawler");
            container.transform.position = hit.point + Vector3.up * 0.01f;
            container.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject model = Instantiate(scalapendraPrefab, container.transform);
            // keep the tiny prefab scale; the model is huge in raw units
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            BuiltinPipelineCompatibility.PatchSpawnedObject(model);

            ScalapendraCrawler crawler = container.AddComponent<ScalapendraCrawler>();
            crawler.Initialise(model.transform, player, crawlSpeed, lifeTime, scalapendraController, animationStateName);

            alive.Add(container);
            return;
        }
    }
}

public class ScalapendraCrawler : MonoBehaviour
{
    private Transform model;
    private Transform player;
    private float speed;
    private float dieAt;
    private Vector3 waypoint;
    private AudioSource chitterSource;
    private Vector3 pinnedLocalPosition;

    public void Initialise(Transform modelRoot, Transform playerTransform, float crawlSpeed, float lifeTime,
        RuntimeAnimatorController controller, string stateName)
    {
        model = modelRoot;
        player = playerTransform;
        speed = crawlSpeed;
        dieAt = Time.time + lifeTime;
        pinnedLocalPosition = model.localPosition;

        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.enabled = true;
            animator.applyRootMotion = false;
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            if (!string.IsNullOrWhiteSpace(stateName) && animator.runtimeAnimatorController != null)
            {
                animator.Play(stateName, 0, Random.value);
            }
        }
        else
        {
            Animation legacy = model.GetComponentInChildren<Animation>();
            if (legacy != null && legacy.clip != null)
            {
                legacy.wrapMode = WrapMode.Loop;
                legacy.Play();
            }
        }

        chitterSource = gameObject.AddComponent<AudioSource>();
        chitterSource.clip = HorrorAudio.Chitter();
        chitterSource.loop = true;
        chitterSource.spatialBlend = 1f;
        chitterSource.rolloffMode = AudioRolloffMode.Logarithmic;
        chitterSource.minDistance = 2f;
        chitterSource.maxDistance = 30f;
        chitterSource.volume = 0.8f;
        chitterSource.pitch = Random.Range(0.85f, 1.15f);
        chitterSource.time = Random.value * chitterSource.clip.length;
        chitterSource.Play();

        PickNewWaypoint();
    }

    private void Update()
    {
        if (Time.time >= dieAt)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 toWaypoint = waypoint - transform.position;
        toWaypoint.y = 0f;
        if (toWaypoint.sqrMagnitude < 0.6f)
        {
            PickNewWaypoint();
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toWaypoint.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 2.2f);
        transform.position += transform.forward * (speed * Time.deltaTime);

        // stick to the ground
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 position = transform.position;
            position.y = hit.point.y + 0.01f;
            transform.position = position;
        }
    }

    private void LateUpdate()
    {
        // the imported clip animates the model root's localPosition with
        // broken values — pin it so only the container moves the creature
        if (model != null)
        {
            model.localPosition = pinnedLocalPosition;
        }
    }

    private void PickNewWaypoint()
    {
        Vector3 anchor = transform.position;
        if (player != null && Random.value < 0.35f)
        {
            // occasionally skitter across the player's general direction
            anchor = Vector3.Lerp(transform.position, player.position, 0.4f);
        }

        Vector2 offset = Random.insideUnitCircle * 7f;
        waypoint = anchor + new Vector3(offset.x, 0f, offset.y);
    }
}
