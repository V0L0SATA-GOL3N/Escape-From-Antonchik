using UnityEngine;

public class ChairInteractable : MonoBehaviour
{
    [SerializeField] private float interactionDistance = 5f;

    public Collider[] Colliders { get; private set; }
    public float InteractionDistance => interactionDistance;

    private void Awake()
    {
        Colliders = GetComponentsInChildren<Collider>();
    }
}
