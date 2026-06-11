using UnityEngine;

public class DoorController : MonoBehaviour
{
    [SerializeField] private float interactionDistance = 5f;

    public float openAngle = 90f;
    public float speed = 3f;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip doorOpenSound;
    [SerializeField] private AudioClip doorCloseSound;

    private bool isOpen;
    private bool closeSoundPending;
    private Quaternion closedRotation;
    private Quaternion openRotation;

    public bool IsOpen => isOpen;
    public float InteractionDistance => interactionDistance;

    void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.dopplerLevel = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 10f;
    }

    void Start()
    {
        closedRotation = transform.localRotation;
        openRotation = closedRotation * Quaternion.Euler(0, openAngle, 0);
    }

    void Update()
    {
        Quaternion target = isOpen ? openRotation : closedRotation;

        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            target,
            Time.deltaTime * speed
        );

        if (closeSoundPending && Quaternion.Angle(transform.localRotation, closedRotation) <= 0.5f)
        {
            closeSoundPending = false;
            PlayDoorSound(doorCloseSound);
        }
    }

    [ContextMenu("Toggle Door")]
    public void ToggleDoor()
    {
        isOpen = !isOpen;
        if (isOpen)
        {
            closeSoundPending = false;
            PlayDoorSound(doorOpenSound);
        }
        else
        {
            closeSoundPending = true;
        }
    }

    private void PlayDoorSound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }
}
