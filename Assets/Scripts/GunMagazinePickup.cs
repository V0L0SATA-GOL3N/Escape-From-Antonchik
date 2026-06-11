using UnityEngine;

public class GunMagazinePickup : MonoBehaviour
{
    [SerializeField] private int magazineAmount = 1;
    [SerializeField] private float interactionDistance = 5f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pickupSound;
    [SerializeField] private bool destroyOnPickup = true;

    public float InteractionDistance => interactionDistance;

    private void Awake()
    {
        audioSource = audioSource != null ? audioSource : GetComponent<AudioSource>();
    }

    public bool TryPickup(GunWeaponController gun)
    {
        if (gun == null || !gun.CanAddMagazine())
        {
            return false;
        }

        if (!gun.TryAddMagazine(magazineAmount))
        {
            return false;
        }

        if (pickupSound != null && destroyOnPickup)
        {
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        }
        else if (audioSource != null && pickupSound != null)
        {
            audioSource.PlayOneShot(pickupSound);
        }

        if (destroyOnPickup)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }

        return true;
    }
}
