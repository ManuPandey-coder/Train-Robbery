using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(WobblyActiveRagdoll))]
public sealed class WobblyWeaponController : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField, Range(0.5f, 4f)] private float pickupRadius = 2.1f;
    [SerializeField, Range(0.1f, 1.2f)] private float pickupDuration = 0.42f;
    [SerializeField] private KeyCode pickupKey = KeyCode.E;
    [SerializeField] private KeyCode dropKey = KeyCode.G;

    [Header("Procedural weapon pose")]
    [SerializeField, Range(2f, 30f)] private float weaponPositionSharpness = 15f;
    [SerializeField, Range(2f, 30f)] private float weaponRotationSharpness = 18f;
    [SerializeField, Range(2f, 30f)] private float aimBlendSharpness = 12f;
    [SerializeField, Range(5f, 200f)] private float aimDistance = 80f;
    [SerializeField] private Vector3 hipHoldOffset = new Vector3(0.25f, -0.18f, 0.28f);
    [SerializeField] private Vector3 thirdPersonAimOffset = new Vector3(0.19f, 0.02f, 0.42f);
    [SerializeField] private Vector3 firstPersonAimOffset = new Vector3(0.18f, -0.14f, 0.44f);
    [Tooltip("Maximum gun yaw relative to the chest while the body catches up with camera aim.")]
    [SerializeField, Range(10f, 60f)] private float maximumArmAimYaw = 35f;
    [Tooltip("How much the real left shoulder bone joins the support-hand IK.")]
    [SerializeField, Range(0f, 0.65f)] private float leftShoulderInfluence = 0.35f;
    [Tooltip("Maximum rotation of the left shoulder away from its authored pose.")]
    [SerializeField, Range(0f, 35f)] private float leftShoulderMaxAngle = 18f;

    [Header("Finger grip")]
    [Tooltip("Curl applied to each finger joint while its hand is gripping a weapon.")]
    [SerializeField, Range(-90f, 90f)] private float fingerCurlDegrees = -58f;
    [Tooltip("How quickly fingers close around and release the weapon.")]
    [SerializeField, Range(2f, 30f)] private float fingerCurlSharpness = 14f;

    [Header("Shooting")]
    [SerializeField] private KeyCode reloadKey = KeyCode.R;
    [SerializeField, Range(2f, 30f)] private float visualRecoilRecovery = 14f;

    private WobblyActiveRagdoll ragdoll;
    private Transform weaponHolder;
    private WobblyPickupWeapon heldWeapon;
    private Camera viewCamera;
    private float pickupProgress;
    private float aimBlend;
    private Vector3 pickupStartPosition;
    private Quaternion pickupStartRotation;
    private ArmIkData leftArmIk;
    private ArmIkData rightArmIk;
    private FingerBone[] leftFingerBones = Array.Empty<FingerBone>();
    private FingerBone[] rightFingerBones = Array.Empty<FingerBone>();
    private float leftFingerBlend;
    private float rightFingerBlend;
    private Vector3 smoothedGripLocal;
    private Quaternion smoothedWeaponLocal;
    private bool holdPoseReady;
    private bool aimInput;
    private WobblyPickupWeapon.HandPose lastRightHandPose;
    private WobblyPickupWeapon.HandPose lastLeftHandPose;
    private bool gripEditing;
    private Vector3 gripEditingPosition;
    private Quaternion gripEditingRotation;
    private float nextShotTime;
    private bool reloading;
    private float reloadStartTime;
    private float reloadEndTime;
    private WobblyPickupWeapon reloadingWeapon;
    private float visualRecoilPitch;
    private float visualRecoilYaw;
    private float visualRecoilBack;

    public WobblyPickupWeapon HeldWeapon => heldWeapon;
    public bool IsGripEditing => gripEditing && heldWeapon != null;
    public bool CanEditGrip => heldWeapon != null && pickupProgress >= 0.999f;
    public bool IsReloading => reloading && heldWeapon != null && reloadingWeapon == heldWeapon;
    public float ReloadProgress => !IsReloading ? 0f : Mathf.InverseLerp(
        reloadStartTime, reloadEndTime, Time.time);

    public bool BeginGripEditing()
    {
        if (!CanEditGrip || ragdoll == null || ragdoll.Pelvis == null) return false;
        if (!heldWeapon.IndependentHoldAnchor)
        {
            Transform chest = ragdoll.Chest != null ? ragdoll.Chest : ragdoll.Pelvis;
            Quaternion bodyFrame = Quaternion.LookRotation(ragdoll.FacingDirection, Vector3.up);
            Vector3 anchor = heldWeapon.PrimaryGripLocal;
            Vector3 offset = Quaternion.Inverse(bodyFrame) *
                (heldWeapon.TransformGripPoint(anchor) - chest.position) - smoothedGripLocal;
            heldWeapon.SetIndependentHoldAnchor(anchor, offset);
        }
        gripEditingPosition = heldWeapon.transform.position;
        gripEditingRotation = heldWeapon.transform.rotation;
        gripEditing = true;
        RefreshGripPreview();
        return true;
    }

    public void EndGripEditing() { gripEditing = false; }

    public void SetGripEditingGunPose(Vector3 position, Quaternion rotation)
    {
        if (!IsGripEditing) return;
        Transform chest = ragdoll.Chest != null ? ragdoll.Chest : ragdoll.Pelvis;
        Quaternion bodyFrame = Quaternion.LookRotation(ragdoll.FacingDirection, Vector3.up);
        Vector3 scale = heldWeapon.transform.lossyScale;
        Vector3 scaledAnchor = Vector3.Scale(heldWeapon.HoldAnchorLocal, scale);

        // Recover the unedited procedural pose from the previous lock and its
        // saved offsets, then express the newly authored transform relative to
        // that pose. This makes Transform edits survive Finish editing.
        Vector3 previousAnchor = gripEditingPosition + gripEditingRotation * scaledAnchor;
        Vector3 baseAnchor = previousAnchor - bodyFrame * heldWeapon.HoldPositionOffset;
        Quaternion baseRotation = gripEditingRotation * Quaternion.Inverse(heldWeapon.HoldRotationOffset);
        Vector3 newAnchor = position + rotation * scaledAnchor;
        Vector3 positionOffset = Quaternion.Inverse(bodyFrame) * (newAnchor - baseAnchor);
        Quaternion rotationOffset = Quaternion.Inverse(baseRotation) * rotation;
        heldWeapon.SetIndependentHoldPose(heldWeapon.HoldAnchorLocal, positionOffset, rotationOffset);
        gripEditingPosition = position;
        gripEditingRotation = rotation;
        RefreshGripPreview();
    }

    // Explicitly called by the custom Inspector too: paused Play mode does not
    // run LateUpdate, so changes must solve the rig immediately instead.
    public void RefreshGripPreview()
    {
        if (!IsGripEditing || ragdoll == null) return;
        ResetLeftShoulder();
        heldWeapon.transform.SetPositionAndRotation(gripEditingPosition, gripEditingRotation);
        SolveHeldHands(ragdoll.FacingDirection, 1f, false);
        PoseFingers(rightFingerBones, 1f, heldWeapon.RightHandPose);
        PoseFingers(leftFingerBones, heldWeapon.UsesTwoHands ? 1f : 0f, heldWeapon.LeftHandPose);
    }

    public Transform GetFingerBone(bool right, int finger, int joint)
    {
        FingerBone[] bones = right ? rightFingerBones : leftFingerBones;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].fingerIndex == finger && bones[i].jointIndex == joint) return bones[i].transform;
        return null;
    }

    public float GripReachError(bool right)
    {
        if (heldWeapon == null) return 0f;
        Transform hand = right ? rightArmIk.hand : leftArmIk.hand;
        return hand == null ? 0f : Vector3.Distance(hand.position, heldWeapon.TransformGripPoint(
            right ? heldWeapon.PrimaryGripLocal : heldWeapon.SecondaryGripLocal));
    }

    public bool IsAiming => isActiveAndEnabled && heldWeapon != null && aimInput;
    public Vector3 AimForward => viewCamera != null ? viewCamera.transform.forward : transform.forward;

    private struct ArmIkData
    {
        public bool valid;
        public Transform upper;
        public Transform shoulder;
        public Transform lower;
        public Transform hand;
        public Vector3 upperAxisLocal;
        public Vector3 lowerAxisLocal;
        public Vector3 upperReferenceLocal;
        public Vector3 lowerReferenceLocal;
        public Quaternion handRestRelativeToLower;
        public Quaternion handBasisLocal;
        public float upperLength;
        public float lowerLength;
        public float sideSign;
        public Quaternion shoulderRestLocalRotation;
        public Vector3 shoulderAxisLocal;
    }

    private struct FingerBone
    {
        public Transform transform;
        public Quaternion restLocalRotation;
        public float curlScale;
        public int fingerIndex;
        public int jointIndex;
    }

    private void Awake()
    {
        ragdoll = GetComponent<WobblyActiveRagdoll>();
        GameObject holderObject = new GameObject("HeldWeaponMount");
        weaponHolder = holderObject.transform;
        weaponHolder.SetParent(transform, false);
        WobblyAmmoHud hud = GetComponent<WobblyAmmoHud>();
        if (hud == null) hud = gameObject.AddComponent<WobblyAmmoHud>();
        hud.Controller = this;
    }

    private void Start()
    {
        CacheArmIk();
        CacheFingerBones();
        viewCamera = Camera.main;
    }

    private void Update()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        aimInput = Input.GetMouseButton(1);

        if (Input.GetKeyDown(pickupKey))
            TryPickupNearest();
        if (Input.GetKeyDown(dropKey))
            DropHeldWeapon();

        if (IsReloading && Time.time >= reloadEndTime)
        {
            reloadingWeapon.FinishReload();
            CancelReload();
        }
        if (heldWeapon != null && Input.GetKeyDown(reloadKey)) BeginReload();
        if (heldWeapon != null && Input.GetMouseButtonDown(0)) TryFireHeldWeapon();

        float recoilStep = 1f - Mathf.Exp(-visualRecoilRecovery * Time.deltaTime);
        visualRecoilPitch = Mathf.Lerp(visualRecoilPitch, 0f, recoilStep);
        visualRecoilYaw = Mathf.Lerp(visualRecoilYaw, 0f, recoilStep);
        visualRecoilBack = Mathf.Lerp(visualRecoilBack, 0f, recoilStep);

        float desiredAim = IsAiming ? 1f : 0f;
        float aimStep = 1f - Mathf.Exp(-aimBlendSharpness * Time.deltaTime);
        aimBlend = Mathf.Lerp(aimBlend, desiredAim, aimStep);
    }

    private void LateUpdate()
    {
        if (heldWeapon != null)
        {
            lastRightHandPose = heldWeapon.RightHandPose;
            lastLeftHandPose = heldWeapon.LeftHandPose;
            ragdoll.SupportHandReserved = heldWeapon.UsesTwoHands;
        }
        float fingerStep = 1f - Mathf.Exp(-fingerCurlSharpness * Time.deltaTime);
        float heldBlend = heldWeapon != null ? Mathf.SmoothStep(0f, 1f, pickupProgress) : 0f;
        rightFingerBlend = Mathf.Lerp(rightFingerBlend, heldBlend, fingerStep);
        float desiredLeftGrip = heldWeapon != null && heldWeapon.UsesTwoHands ? heldBlend : 0f;
        leftFingerBlend = Mathf.Lerp(leftFingerBlend, desiredLeftGrip, fingerStep);

        if (heldWeapon == null || ragdoll == null || ragdoll.Pelvis == null)
        {
            ResetLeftShoulder();
            PoseFingers(rightFingerBones, rightFingerBlend, lastRightHandPose);
            PoseFingers(leftFingerBones, leftFingerBlend, lastLeftHandPose);
            return;
        }
        if (!leftArmIk.valid || !rightArmIk.valid) CacheArmIk();
        if (leftFingerBones.Length == 0 || rightFingerBones.Length == 0) CacheFingerBones();
        // Reach limits and gun placement always start from the authored
        // shoulder pose. Shoulder IK is applied afterward by SolveHeldHands,
        // preventing a frame-to-frame feedback loop into the gun/right hand.
        ResetLeftShoulder();
        if (IsGripEditing) { RefreshGripPreview(); return; }

        Transform chest = ragdoll.Chest != null ? ragdoll.Chest : ragdoll.Pelvis;
        Vector3 bodyForward = ragdoll.FacingDirection;
        if (bodyForward.sqrMagnitude < 0.01f) bodyForward = -transform.forward;
        bodyForward = Vector3.ProjectOnPlane(bodyForward, Vector3.up).normalized;
        Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyForward).normalized;

        Vector3 cameraForward = viewCamera != null ? viewCamera.transform.forward.normalized : bodyForward;
        WobblyCameraController cameraController = viewCamera != null
            ? viewCamera.GetComponent<WobblyCameraController>() : null;
        bool firstPerson = cameraController != null && cameraController.IsFirstPerson;

        // Bound the arms to the chest while the complete body turns toward aim.
        // Orbiting the camera in hip-fire mode does not enter the arm solution.
        Vector3 safeAim = ClampAimToBody(cameraForward, bodyForward, maximumArmAimYaw);
        Quaternion aimFrame = Quaternion.LookRotation(safeAim, Vector3.up);
        Vector3 aimRight = aimFrame * Vector3.right;
        Vector3 aimUp = aimFrame * Vector3.up;

        Vector3 hipGrip = chest.position + bodyRight * hipHoldOffset.x +
                          Vector3.up * hipHoldOffset.y + bodyForward * hipHoldOffset.z;
        Vector3 aimOffset = firstPerson ? firstPersonAimOffset : thirdPersonAimOffset;
        Vector3 aimOrigin = firstPerson
            ? ragdoll.Pelvis.position + Vector3.up * Mathf.Clamp(viewCamera.transform.position.y - ragdoll.Pelvis.position.y, 0.4f, 1.2f)
            : chest.position;
        Vector3 aimedGrip = aimOrigin + aimRight * aimOffset.x + aimUp * aimOffset.y + safeAim * aimOffset.z;
        Vector3 desiredGrip = Vector3.Lerp(hipGrip, aimedGrip, aimBlend);

        Vector3 hipDirection = (bodyForward + Vector3.down * 0.16f).normalized;
        Vector3 aimPoint = viewCamera != null
            ? viewCamera.transform.position + cameraForward * aimDistance
            : chest.position + bodyForward * aimDistance;
        Vector3 aimedDirection = ClampAimToBody((aimPoint - desiredGrip).normalized, bodyForward, maximumArmAimYaw);
        Vector3 desiredDirection = Vector3.Slerp(hipDirection, aimedDirection, aimBlend).normalized;
        Vector3 desiredUp = Vector3.up;

        Quaternion desiredRotation = CalculateWeaponRotation(heldWeapon, desiredDirection, desiredUp);
        Quaternion bodyFrame = Quaternion.LookRotation(bodyForward, Vector3.up);
        Vector3 localGrip = Quaternion.Inverse(bodyFrame) * (desiredGrip - chest.position);
        Quaternion localWeapon = Quaternion.Inverse(bodyFrame) * desiredRotation;
        if (!holdPoseReady)
        {
            smoothedGripLocal = localGrip;
            smoothedWeaponLocal = localWeapon;
            holdPoseReady = true;
        }
        smoothedGripLocal = Vector3.Lerp(smoothedGripLocal, localGrip,
            1f - Mathf.Exp(-weaponPositionSharpness * Time.deltaTime));
        smoothedWeaponLocal = Quaternion.Slerp(smoothedWeaponLocal, localWeapon,
            1f - Mathf.Exp(-weaponRotationSharpness * Time.deltaTime));
        desiredRotation = bodyFrame * smoothedWeaponLocal * heldWeapon.HoldRotationOffset;
        desiredRotation = Quaternion.AngleAxis(-visualRecoilPitch, aimRight) *
                          Quaternion.AngleAxis(visualRecoilYaw, Vector3.up) * desiredRotation;
        desiredGrip = chest.position + bodyFrame * (smoothedGripLocal + heldWeapon.HoldPositionOffset) -
                      desiredDirection * visualRecoilBack;
        Vector3 scale = heldWeapon.transform.lossyScale;
        Vector3 secondaryOffset = desiredRotation * Vector3.Scale(
            heldWeapon.SecondaryGripLocal - heldWeapon.PrimaryGripLocal, scale);
        for (int reachPass = 0; !heldWeapon.IndependentHoldAnchor && reachPass < 8; reachPass++)
        {
            if (heldWeapon.UsesTwoHands)
                desiredGrip = ClampHandReach(leftArmIk, desiredGrip + secondaryOffset) - secondaryOffset;
            desiredGrip = ClampHandReach(rightArmIk, desiredGrip);
        }
        Vector3 scaledGrip = Vector3.Scale(heldWeapon.HoldAnchorLocal, scale);
        Vector3 desiredPosition = desiredGrip - desiredRotation * scaledGrip;

        pickupProgress = Mathf.MoveTowards(pickupProgress, 1f,
            Time.deltaTime / Mathf.Max(0.01f, pickupDuration));
        float pickupEase = pickupProgress * pickupProgress * (3f - 2f * pickupProgress);
        if (pickupProgress < 1f)
        {
            heldWeapon.transform.SetPositionAndRotation(
                Vector3.Lerp(pickupStartPosition, desiredPosition, pickupEase),
                Quaternion.Slerp(pickupStartRotation, desiredRotation, pickupEase));
        }
        else
        {
            heldWeapon.transform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }

        float armWeight = Mathf.SmoothStep(0f, 1f, pickupProgress);
        SolveHeldHands(bodyForward, armWeight, !heldWeapon.IndependentHoldAnchor);
        PoseFingers(rightFingerBones, rightFingerBlend, lastRightHandPose);
        PoseFingers(leftFingerBones, leftFingerBlend, lastLeftHandPose);
    }

    private void SolveHeldHands(Vector3 bodyForward, float armWeight, bool moveGunToWrist)
    {
        Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyForward).normalized;
        Vector3 rightGrip = heldWeapon.TransformGripPoint(heldWeapon.PrimaryGripLocal);
        SolveArmIk(rightArmIk, rightGrip, bodyForward, bodyRight,
            heldWeapon.GripFrame(true), armWeight);
        if (moveGunToWrist && rightArmIk.valid && rightArmIk.hand != null)
        {
            // Keep the gun's primary grip physically connected to hand.R even
            // if the requested camera pose is slightly beyond arm reach.
            Vector3 gripError = rightArmIk.hand.position -
                                heldWeapon.TransformGripPoint(heldWeapon.PrimaryGripLocal);
            heldWeapon.transform.position += gripError * armWeight;
        }

        if (heldWeapon.UsesTwoHands)
        {
            Vector3 leftGrip = heldWeapon.TransformGripPoint(heldWeapon.SecondaryGripLocal);
            SolveArmIk(leftArmIk, leftGrip, bodyForward, bodyRight,
                heldWeapon.GripFrame(false), armWeight);
        }

    }

    private void TryPickupNearest()
    {
        if (ragdoll == null || ragdoll.Pelvis == null) return;
        WobblyPickupWeapon[] weapons = FindObjectsByType<WobblyPickupWeapon>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        WobblyPickupWeapon nearest = null;
        float nearestSqrDistance = pickupRadius * pickupRadius;
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] == null || weapons[i].IsHeld) continue;
            float sqrDistance = (weapons[i].transform.position - ragdoll.Pelvis.position).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance) continue;
            nearestSqrDistance = sqrDistance;
            nearest = weapons[i];
        }
        if (nearest == null) return;

        if (heldWeapon != null) DropHeldWeapon();
        heldWeapon = nearest;
        pickupStartPosition = nearest.transform.position;
        pickupStartRotation = nearest.transform.rotation;
        pickupProgress = 0f;
        aimBlend = 0f;
        holdPoseReady = false;
        nearest.BeginHold(weaponHolder);
        CancelReload();
        nextShotTime = Time.time;
        ragdoll.ArmsReserved = true;
        ragdoll.SupportHandReserved = nearest.UsesTwoHands;
    }

    private void DropHeldWeapon()
    {
        EndGripEditing();
        if (heldWeapon == null) return;
        CancelReload();
        ResetLeftShoulder();
        Vector3 inheritedVelocity = ragdoll != null ? ragdoll.MovementVelocity : Vector3.zero;
        // Release from the hands, with only the player's existing movement.
        // A drop is not a throw and must not receive artificial spin or lift.
        heldWeapon.Drop(heldWeapon.transform.position, Vector3.ClampMagnitude(inheritedVelocity, 6f));
        heldWeapon = null;
        pickupProgress = 0f;
        aimBlend = 0f;
        holdPoseReady = false;
        if (ragdoll != null) { ragdoll.ArmsReserved = false; ragdoll.SupportHandReserved = false; }
    }

    public bool BeginReload()
    {
        if (heldWeapon == null || IsReloading || !heldWeapon.CanReload) return false;
        reloading = true;
        reloadingWeapon = heldWeapon;
        reloadStartTime = Time.time;
        reloadEndTime = reloadStartTime + Mathf.Max(0.1f, heldWeapon.ReloadSeconds);
        return true;
    }

    public bool CompleteReloadImmediately()
    {
        if (heldWeapon == null || !heldWeapon.CanReload) return false;
        int loaded = heldWeapon.FinishReload();
        CancelReload();
        return loaded > 0;
    }

    public bool TryFireHeldWeapon()
    {
        if (heldWeapon == null || gripEditing || IsReloading || pickupProgress < 0.72f ||
            Time.time < nextShotTime) return false;
        if (!heldWeapon.TryConsumeRound()) return false;

        nextShotTime = Time.time + Mathf.Max(0.05f, heldWeapon.SecondsBetweenShots);
        Vector3 muzzle = heldWeapon.transform.TransformPoint(heldWeapon.MuzzleLocal);
        Vector3 barrelDirection = heldWeapon.transform.TransformDirection(heldWeapon.BarrelAxisLocal).normalized;
        Vector3 fireDirection = barrelDirection;
        if (IsAiming && viewCamera != null)
        {
            Vector3 aimPoint = FindCameraAimPoint();
            Vector3 towardAim = aimPoint - muzzle;
            if (towardAim.sqrMagnitude > 0.001f) fireDirection = towardAim.normalized;
        }

        int pelletCount = Mathf.Max(1, heldWeapon.PelletsPerShot);
        for (int pellet = 0; pellet < pelletCount; pellet++)
        {
            Vector3 direction = ApplySpread(fireDirection, heldWeapon.SpreadDegrees);
            WobblyBullet.Spawn(heldWeapon.ProjectileVisualPrefab,
                muzzle + direction * 0.025f, direction, heldWeapon.ProjectileSpeed,
                heldWeapon.ProjectileGravity, heldWeapon.ImpactImpulse,
                heldWeapon.BulletVisualLength, transform, heldWeapon.transform);
        }
        WobblyMuzzleFlash.Spawn(muzzle + fireDirection * 0.018f, fireDirection,
            heldWeapon.MuzzleFlashSize);

        visualRecoilPitch = Mathf.Min(14f, visualRecoilPitch + heldWeapon.RecoilPitch * 0.48f);
        visualRecoilYaw = Mathf.Clamp(visualRecoilYaw + UnityEngine.Random.Range(
            -heldWeapon.RecoilYaw, heldWeapon.RecoilYaw) * 0.45f, -6f, 6f);
        visualRecoilBack = Mathf.Min(0.24f, visualRecoilBack + heldWeapon.RecoilKickback);
        if (viewCamera != null)
        {
            WobblyCameraController cameraController = viewCamera.GetComponent<WobblyCameraController>();
            if (cameraController != null)
                cameraController.AddRecoil(heldWeapon.RecoilPitch,
                    UnityEngine.Random.Range(-heldWeapon.RecoilYaw, heldWeapon.RecoilYaw));
        }
        return true;
    }

    private Vector3 FindCameraAimPoint()
    {
        Vector3 origin = viewCamera.transform.position;
        Vector3 direction = viewCamera.transform.forward;
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, aimDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closest = aimDistance;
        Vector3 point = origin + direction * aimDistance;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform candidate = hits[i].collider != null ? hits[i].collider.transform : null;
            if (candidate == null || candidate == transform || candidate.IsChildOf(transform) ||
                (heldWeapon != null && (candidate == heldWeapon.transform || candidate.IsChildOf(heldWeapon.transform))))
                continue;
            if (hits[i].distance >= closest) continue;
            closest = hits[i].distance;
            point = hits[i].point;
        }
        return point;
    }

    private static Vector3 ApplySpread(Vector3 direction, float degrees)
    {
        if (degrees <= 0.001f) return direction.normalized;
        Quaternion frame = Quaternion.LookRotation(direction.normalized, Vector3.up);
        Vector2 offset = UnityEngine.Random.insideUnitCircle * degrees;
        return (frame * Quaternion.Euler(-offset.y, offset.x, 0f) * Vector3.forward).normalized;
    }

    private void CancelReload()
    {
        reloading = false;
        reloadingWeapon = null;
        reloadStartTime = reloadEndTime = 0f;
    }

    private void OnDisable()
    {
        if (heldWeapon != null) DropHeldWeapon();
        if (ragdoll != null) { ragdoll.ArmsReserved = false; ragdoll.SupportHandReserved = false; }
    }

    private void CacheArmIk()
    {
        if (ragdoll == null) return;
        leftArmIk = CreateArmIk(ragdoll.LeftShoulder, ragdoll.LeftUpperArm,
            ragdoll.LeftForearm, ragdoll.LeftHand, -1f);
        rightArmIk = CreateArmIk(null, ragdoll.RightUpperArm,
            ragdoll.RightForearm, ragdoll.RightHand, 1f);
    }

    private void CacheFingerBones()
    {
        if (ragdoll == null) return;
        Transform[] allBones = ragdoll.GetComponentsInChildren<Transform>(true);
        leftFingerBones = CreateFingerBones(allBones, "L");
        rightFingerBones = CreateFingerBones(allBones, "R");
    }

    private static FingerBone[] CreateFingerBones(Transform[] allBones, string side)
    {
        string[] roots = { "thumb", "index", "middle", "ring", "pinky" };
        List<FingerBone> fingers = new List<FingerBone>(15);
        for (int finger = 0; finger < roots.Length; finger++)
        {
            for (int joint = 0; joint < 3; joint++)
            {
                string name = roots[finger] + "." + side + (joint == 0 ? string.Empty : joint == 1 ? ".001" : ".002");
                Transform bone = FindExactBone(allBones, name);
                // The imported robber rig spells this chain "middel".
                // Keep the standard spelling supported without renaming skin bones.
                if (bone == null && finger == 2)
                    bone = FindExactBone(allBones, name.Replace("middle", "middel"));
                if (bone == null) continue;
                fingers.Add(new FingerBone
                {
                    transform = bone,
                    restLocalRotation = bone.localRotation,
                    curlScale = (roots[finger] == "thumb" ? 0.68f : 1f) * (joint == 0 ? 1f : 1.18f),
                    fingerIndex = finger,
                    jointIndex = joint
                });
            }
        }
        return fingers.ToArray();
    }

    private static Transform FindExactBone(Transform[] bones, string name)
    {
        for (int i = 0; i < bones.Length; i++)
            if (string.Equals(bones[i].name, name, StringComparison.OrdinalIgnoreCase)) return bones[i];
        return null;
    }

    private void PoseFingers(FingerBone[] fingers, float blend, WobblyPickupWeapon.HandPose pose)
    {
        for (int i = 0; i < fingers.Length; i++)
        {
            FingerBone finger = fingers[i];
            if (finger.transform == null) continue;
            Vector3 angles = pose != null && pose.useCustomFingers
                ? pose.Finger(finger.fingerIndex).JointEuler(finger.jointIndex)
                : new Vector3(fingerCurlDegrees * finger.curlScale, 0f, 0f);
            Quaternion curled = finger.restLocalRotation * Quaternion.Euler(angles);
            finger.transform.localRotation = Quaternion.Slerp(finger.restLocalRotation, curled, blend);
        }
    }

    private ArmIkData CreateArmIk(Transform shoulder, Transform upper, Transform lower, Transform hand, float sideSign)
    {
        ArmIkData data = new ArmIkData
        {
            upper = upper,
            shoulder = shoulder,
            lower = lower,
            hand = hand,
            sideSign = sideSign
        };
        if (upper == null || lower == null || hand == null) return data;
        if (shoulder != null)
        {
            Vector3 shoulderToUpper = upper.position - shoulder.position;
            data.shoulderRestLocalRotation = shoulder.localRotation;
            if (shoulderToUpper.sqrMagnitude > 0.000001f)
                data.shoulderAxisLocal = shoulder.InverseTransformDirection(shoulderToUpper.normalized);
        }

        Vector3 upperVector = lower.position - upper.position;
        Vector3 lowerVector = hand.position - lower.position;
        if (upperVector.sqrMagnitude < 0.000001f || lowerVector.sqrMagnitude < 0.000001f) return data;

        Vector3 line = hand.position - upper.position;
        Vector3 elbowOffset = upperVector - line.normalized * Vector3.Dot(upperVector, line.normalized);
        if (elbowOffset.sqrMagnitude < 0.000001f)
            elbowOffset = Vector3.right * sideSign + Vector3.down * 0.3f;

        data.upperAxisLocal = upper.InverseTransformDirection(upperVector.normalized);
        data.lowerAxisLocal = lower.InverseTransformDirection(lowerVector.normalized);
        data.upperReferenceLocal = upper.InverseTransformDirection(elbowOffset.normalized);
        data.lowerReferenceLocal = lower.InverseTransformDirection(elbowOffset.normalized);
        data.handRestRelativeToLower = Quaternion.Inverse(lower.rotation) * hand.rotation;
        Transform index = FindExactBone(hand.GetComponentsInChildren<Transform>(true), "index." + (sideSign < 0f ? "L" : "R"));
        Transform pinky = FindExactBone(hand.GetComponentsInChildren<Transform>(true), "pinky." + (sideSign < 0f ? "L" : "R"));
        Vector3 fingerForward = index != null && pinky != null
            ? (index.position + pinky.position) * 0.5f - hand.position : lowerVector;
        Vector3 acrossPalm = index != null && pinky != null ? index.position - pinky.position : hand.right;
        Vector3 palmNormal = Vector3.Cross(fingerForward, acrossPalm) * sideSign;
        if (fingerForward.sqrMagnitude < 0.000001f) fingerForward = hand.up;
        if (palmNormal.sqrMagnitude < 0.000001f) palmNormal = hand.forward;
        data.handBasisLocal = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(fingerForward, palmNormal);
        data.upperLength = upperVector.magnitude;
        data.lowerLength = lowerVector.magnitude;
        data.valid = true;
        return data;
    }

    private void SolveArmIk(
        ArmIkData data,
        Vector3 handTarget,
        Vector3 aimDirection,
        Vector3 bodyRight,
        Quaternion gripFrame,
        float weight)
    {
        if (!data.valid || weight <= 0f) return;

        if (data.shoulder != null)
        {
            data.shoulder.localRotation = data.shoulderRestLocalRotation;
            Vector3 current = data.shoulder.TransformDirection(data.shoulderAxisLocal).normalized;
            Vector3 towardTarget = (handTarget - data.shoulder.position).normalized;
            if (current.sqrMagnitude > .001f && towardTarget.sqrMagnitude > .001f)
            {
                Vector3 desired = Vector3.Slerp(current, towardTarget, leftShoulderInfluence).normalized;
                Quaternion delta = Quaternion.FromToRotation(current, desired);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                angle = Mathf.Clamp(angle, -leftShoulderMaxAngle, leftShoulderMaxAngle);
                if (!float.IsNaN(axis.x))
                    data.shoulder.rotation = Quaternion.AngleAxis(angle * weight, axis) * data.shoulder.rotation;
            }
        }

        Vector3 root = data.upper.position;
        Vector3 toTarget = handTarget - root;
        float rawDistance = toTarget.magnitude;
        if (rawDistance < 0.0001f) return;

        Vector3 direction = toTarget / rawDistance;
        float minimum = Mathf.Abs(data.upperLength - data.lowerLength) + 0.001f;
        float maximum = (data.upperLength + data.lowerLength) * 0.96f;
        float distance = Mathf.Clamp(rawDistance, minimum, maximum);

        Vector3 side = bodyRight * data.sideSign;
        Vector3 pole = Vector3.ProjectOnPlane(side * 0.8f + Vector3.down * 0.35f - aimDirection * 0.1f,
            direction).normalized;
        if (pole.sqrMagnitude < 0.001f)
            pole = Vector3.ProjectOnPlane(Vector3.down, direction).normalized;

        float along = (data.upperLength * data.upperLength - data.lowerLength * data.lowerLength +
                       distance * distance) / (2f * distance);
        float elbowHeight = Mathf.Sqrt(Mathf.Max(0f,
            data.upperLength * data.upperLength - along * along));
        Vector3 elbow = root + direction * along + pole * elbowHeight;
        Vector3 clampedTarget = root + direction * distance;

        Quaternion upperStart = data.upper.rotation;
        Quaternion lowerStart = data.lower.rotation;
        Quaternion handStart = data.hand.rotation;

        AlignBoneFrame(data.upper, data.upperAxisLocal, data.upperReferenceLocal, elbow - root, pole);
        AlignBoneFrame(data.lower, data.lowerAxisLocal, data.lowerReferenceLocal,
            clampedTarget - data.lower.position, pole);
        data.hand.rotation = gripFrame * Quaternion.Inverse(data.handBasisLocal);

        Quaternion upperSolved = data.upper.rotation;
        Quaternion lowerSolved = data.lower.rotation;
        Quaternion handSolved = data.hand.rotation;
        data.upper.rotation = upperStart;
        data.lower.rotation = lowerStart;
        data.hand.rotation = handStart;

        data.upper.rotation = Quaternion.Slerp(upperStart, upperSolved, weight);
        data.lower.rotation = Quaternion.Slerp(lowerStart, lowerSolved, weight);
        data.hand.rotation = Quaternion.Slerp(handStart, handSolved, weight);
    }

    private void ResetLeftShoulder()
    {
        if (leftArmIk.shoulder != null)
            leftArmIk.shoulder.localRotation = leftArmIk.shoulderRestLocalRotation;
    }

    private static Vector3 ClampHandReach(ArmIkData arm, Vector3 target)
    {
        if (!arm.valid) return target;
        return arm.upper.position + Vector3.ClampMagnitude(target - arm.upper.position,
            (arm.upperLength + arm.lowerLength) * 0.94f);
    }

    private static Vector3 ClampAimToBody(Vector3 aim, Vector3 bodyForward, float yawLimit)
    {
        Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up);
        float yaw = flat.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(bodyForward, flat, Vector3.up) : 0f;
        yaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);
        float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -60f, 60f);
        Vector3 facing = Quaternion.AngleAxis(yaw, Vector3.up) * bodyForward;
        return (facing * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
    }

    private static void AlignBoneFrame(
        Transform bone,
        Vector3 localAxis,
        Vector3 localReference,
        Vector3 worldDirection,
        Vector3 worldReference)
    {
        if (worldDirection.sqrMagnitude < 0.000001f) return;
        Vector3 direction = worldDirection.normalized;
        Vector3 reference = Vector3.ProjectOnPlane(worldReference, direction).normalized;
        if (reference.sqrMagnitude < 0.001f)
            reference = Vector3.ProjectOnPlane(Vector3.up, direction).normalized;
        if (reference.sqrMagnitude < 0.001f)
            reference = Vector3.Cross(direction, Vector3.right).normalized;

        Vector3 currentDirection = bone.TransformDirection(localAxis).normalized;
        bone.rotation = Quaternion.FromToRotation(currentDirection, direction) * bone.rotation;
        Vector3 currentReference = Vector3.ProjectOnPlane(
            bone.TransformDirection(localReference), direction).normalized;
        if (currentReference.sqrMagnitude > 0.001f)
        {
            float roll = Vector3.SignedAngle(currentReference, reference, direction);
            bone.rotation = Quaternion.AngleAxis(roll, direction) * bone.rotation;
        }
    }

    private static Quaternion CalculateWeaponRotation(
        WobblyPickupWeapon weapon,
        Vector3 worldForward,
        Vector3 worldUp)
    {
        Vector3 localForward = weapon.BarrelAxisLocal;
        Vector3 localUp = Vector3.ProjectOnPlane(weapon.UpAxisLocal, localForward).normalized;
        Vector3 correctedWorldUp = Vector3.ProjectOnPlane(worldUp, worldForward).normalized;
        if (localUp.sqrMagnitude < 0.001f) localUp = Vector3.up;
        if (correctedWorldUp.sqrMagnitude < 0.001f) correctedWorldUp = Vector3.up;
        Quaternion localBasis = Quaternion.LookRotation(localForward, localUp);
        Quaternion worldBasis = Quaternion.LookRotation(worldForward, correctedWorldUp);
        return worldBasis * Quaternion.Inverse(localBasis);
    }
}
