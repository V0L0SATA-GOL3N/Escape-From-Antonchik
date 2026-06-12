using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Procedural animation layer on top of Antonchik's idle clip: head motion
// while he talks, raising the pistol arm, recoil on the shot, holding a hand
// out to take items, and walking off into the dark. Applied in LateUpdate so
// it overrides whatever the Animator wrote this frame.
public class AntonchikPuppeteer : MonoBehaviour
{
    private Animator animator;
    private Transform head;
    private Transform rightUpperArm;
    private Transform rightLowerArm;
    private Transform rightHand;
    private Transform leftUpperArm;
    private Transform leftLowerArm;
    private Transform leftHand;

    private bool speaking;
    private float speakSeed;
    private float aimWeight;
    private float aimTarget;
    private float receiveWeight;
    private float receiveTarget;
    private float recoilPulse;
    private Transform aimAt;
    private GameObject heldGun;

    public bool IsAiming => aimTarget > 0.5f;

    public void Initialise()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }

        speakSeed = Random.value * 100f;
    }

    public void SetSpeaking(bool value)
    {
        speaking = value;
    }

    public void SetReceiving(bool value)
    {
        receiveTarget = value ? 1f : 0f;
    }

    public Transform RightHand => rightHand != null ? rightHand : transform;

    public Transform LeftHand => leftHand != null ? leftHand : transform;

    public void BeginAiming(Transform target, GameObject gunPrefab)
    {
        aimAt = target;
        aimTarget = 1f;

        if (heldGun == null && gunPrefab != null && rightHand != null)
        {
            heldGun = Instantiate(gunPrefab);
            heldGun.name = "Antonchik Gun";
            StripBehaviours(heldGun);
            BuiltinPipelineCompatibility.PatchSpawnedObject(heldGun);
            heldGun.transform.SetParent(rightHand, false);
            heldGun.transform.localPosition = new Vector3(0.04f, 0.06f, 0.02f);
            heldGun.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        }
    }

    public void StopAiming()
    {
        aimTarget = 0f;
        if (heldGun != null)
        {
            Destroy(heldGun, 0.4f);
            heldGun = null;
        }
    }

    public void Recoil()
    {
        recoilPulse = 1f;
    }

    public Vector3 GunMuzzlePosition()
    {
        if (heldGun != null)
        {
            foreach (Transform child in heldGun.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "muzzle")
                {
                    return child.position;
                }
            }

            return heldGun.transform.position;
        }

        return rightHand != null ? rightHand.position : transform.position + Vector3.up * 1.4f;
    }

    public IEnumerator WalkAway(Vector3 destination, float speed, AnimationClip walkClip)
    {
        if (animator != null && walkClip != null && animator.runtimeAnimatorController != null)
        {
            AnimatorOverrideController overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
            List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, walkClip);
            }

            overrideController.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = overrideController;
            animator.applyRootMotion = false;
        }

        Vector3 flatDestination = destination;
        flatDestination.y = transform.position.y;

        while ((flatDestination - transform.position).sqrMagnitude > 0.5f)
        {
            Vector3 direction = (flatDestination - transform.position).normalized;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * 3f);
            transform.position += direction * (speed * Time.deltaTime);
            yield return null;
        }
    }

    private void LateUpdate()
    {
        aimWeight = Mathf.MoveTowards(aimWeight, aimTarget, Time.deltaTime * 2.4f);
        receiveWeight = Mathf.MoveTowards(receiveWeight, receiveTarget, Time.deltaTime * 2.8f);
        recoilPulse = Mathf.MoveTowards(recoilPulse, 0f, Time.deltaTime * 6f);

        ApplySpeaking();
        ApplyAiming();
        ApplyReceiving();
    }

    private void ApplySpeaking()
    {
        if (!speaking || head == null)
        {
            return;
        }

        float t = Time.time;
        float nod = (Mathf.PerlinNoise(speakSeed, t * 1.9f) - 0.5f) * 14f;
        float tilt = (Mathf.PerlinNoise(speakSeed + 7f, t * 1.3f) - 0.5f) * 9f;
        float turn = (Mathf.PerlinNoise(speakSeed + 19f, t * 0.8f) - 0.5f) * 10f;
        head.localRotation = head.localRotation * Quaternion.Euler(nod, turn, tilt);
    }

    private void ApplyAiming()
    {
        if (aimWeight <= 0.001f || rightUpperArm == null || rightHand == null)
        {
            return;
        }

        Vector3 target = aimAt != null ? aimAt.position : transform.position + transform.forward * 2f + Vector3.up * 1.5f;

        // two passes of FromToRotation settle the arm chain onto the target
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 current = (rightHand.position - rightUpperArm.position).normalized;
            Vector3 desired = (target - rightUpperArm.position).normalized;
            Quaternion correction = Quaternion.FromToRotation(current, desired);
            rightUpperArm.rotation = Quaternion.Slerp(Quaternion.identity, correction, aimWeight) * rightUpperArm.rotation;

            if (rightLowerArm != null)
            {
                current = (rightHand.position - rightLowerArm.position).normalized;
                desired = (target - rightLowerArm.position).normalized;
                correction = Quaternion.FromToRotation(current, desired);
                rightLowerArm.rotation = Quaternion.Slerp(Quaternion.identity, correction, aimWeight * 0.6f) * rightLowerArm.rotation;
            }
        }

        if (recoilPulse > 0.001f)
        {
            rightUpperArm.rotation = Quaternion.AngleAxis(recoilPulse * 16f, -transform.right) * rightUpperArm.rotation;
        }

        if (heldGun != null && aimAt != null)
        {
            Vector3 toTarget = (aimAt.position - heldGun.transform.position).normalized;
            heldGun.transform.rotation = Quaternion.Slerp(heldGun.transform.rotation,
                Quaternion.LookRotation(toTarget, Vector3.up), aimWeight);
        }
    }

    private void ApplyReceiving()
    {
        if (receiveWeight <= 0.001f || leftUpperArm == null)
        {
            return;
        }

        // raise the left forearm forward, palm up — "давай сюда"
        leftUpperArm.rotation = Quaternion.AngleAxis(receiveWeight * 38f, transform.right) * leftUpperArm.rotation;
        if (leftLowerArm != null)
        {
            leftLowerArm.rotation = Quaternion.AngleAxis(receiveWeight * 30f, transform.right) * leftLowerArm.rotation;
        }
    }

    private static void StripBehaviours(GameObject gun)
    {
        foreach (MonoBehaviour behaviour in gun.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
        }

        foreach (Rigidbody body in gun.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        foreach (Collider collider in gun.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }
    }
}
