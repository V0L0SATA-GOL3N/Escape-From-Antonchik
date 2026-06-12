using System;
using UnityEngine;

// Generic "press E" target picked up by DoorRaycastInteractor, so new props
// (radio, car, Antonchik himself) don't each need their own raycast loop.
public class SimpleInteractable : MonoBehaviour
{
    [SerializeField] private string prompt = "Press E to interact";
    [SerializeField] private float interactionDistance = 4f;

    public event Action Interacted;

    public bool CanInteract { get; set; } = true;

    public float InteractionDistance => interactionDistance;

    public virtual string Prompt
    {
        get => prompt;
        set => prompt = value;
    }

    public void SetInteractionDistance(float distance)
    {
        interactionDistance = distance;
    }

    public virtual void Interact()
    {
        Interacted?.Invoke();
    }
}
