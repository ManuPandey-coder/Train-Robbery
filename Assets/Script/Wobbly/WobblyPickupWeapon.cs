using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyPickupWeapon : MonoBehaviour
{
    public enum GripMode
    {
        Auto,
        OneHanded,
        TwoHanded
    }

    public enum FirearmClass
    {
        Revolver,
        Rifle,
        Shotgun,
        Sniper
    }

    [Serializable]
    public struct FingerPose
    {
        [Range(-110f, 110f)] public float baseCurl;
        [Range(-110f, 110f)] public float middleCurl;
        [Range(-110f, 110f)] public float tipCurl;
        [Tooltip("Sideways angle at the finger base (local Z).")]
        [Range(-60f, 60f)] public float spread;
        [Tooltip("Twist at the finger base (local Y), useful for the thumb.")]
        [Range(-90f, 90f)] public float twist;

        public FingerPose(float curl)
        {
            baseCurl = curl;
            middleCurl = tipCurl = curl * 1.18f;
            spread = twist = 0f;
        }

        public Vector3 JointEuler(int joint)
        {
            return new Vector3(joint == 0 ? baseCurl : joint == 1 ? middleCurl : tipCurl,
                joint == 0 ? twist : 0f, joint == 0 ? spread : 0f);
        }
    }

    [Serializable]
    public sealed class HandPose
    {
        [Tooltip("Rotation offset from the automatic palm orientation, in degrees.")]
        public Vector3 wristRotation;
        [Tooltip("Use this gun's individual fingers instead of the player's shared Finger Curl Degrees.")]
        public bool useCustomFingers;
        public FingerPose thumb = new FingerPose(-58f * 0.68f);
        public FingerPose index = new FingerPose(-58f);
        public FingerPose middle = new FingerPose(-58f);
        public FingerPose ring = new FingerPose(-58f);
        public FingerPose pinky = new FingerPose(-58f);

        public FingerPose Finger(int index)
        {
            switch (index)
            {
                case 0: return thumb;
                case 1: return this.index;
                case 2: return middle;
                case 3: return ring;
                default: return pinky;
            }
        }
    }

    [Header("Weapon grip")]
    [SerializeField] private GripMode gripMode = GripMode.Auto;
    [SerializeField] private Vector3 barrelAxisLocal = Vector3.left;
    [SerializeField] private Vector3 upAxisLocal = Vector3.up;
    [SerializeField] private Vector3 primaryGripLocal;
    [SerializeField] private Vector3 secondaryGripLocal;
    [Tooltip("Maximum distance between wrists. Slide the support grip toward the receiver for this short-armed character.")]
    [SerializeField, Range(0.1f, 0.5f)] private float maximumHandSpacing = 0.22f;
    [Tooltip("Turn off when manually authoring an exact support-hand grip. Arm reach is still limited by IK.")]
    [SerializeField] private bool limitHandSpacing = true;
    [SerializeField] private Vector3 muzzleLocal;
    [SerializeField] private bool configured;
    [SerializeField, HideInInspector] private BoxCollider pickupCollider;

    [Header("Independent gun placement")]
    [Tooltip("Manual grip editing separates the gun's holding anchor from its wrist targets.")]
    [SerializeField] private bool independentHoldAnchor;
    [SerializeField, HideInInspector] private Vector3 holdAnchorLocal;
    [SerializeField] private Vector3 holdPositionOffset;
    [SerializeField] private Vector3 holdRotationOffset;

    [Header("Per-gun hand poses")]
    [SerializeField] private HandPose rightHandPose = new HandPose();
    [SerializeField] private HandPose leftHandPose = new HandPose();

    [Header("Dropped weapon physics")]
    [SerializeField, Range(0f, 1f)] private float groundFriction = 0.8f;
    [SerializeField, Range(0f, 2f)] private float airDamping = 0.15f;
    [SerializeField, Range(0f, 8f)] private float rotationDamping = 2.5f;

    [Header("Shooting")]
    [SerializeField] private FirearmClass firearmClass = FirearmClass.Revolver;
    [SerializeField] private GameObject projectileVisualPrefab;
    [SerializeField, Min(1)] private int magazineSize = 6;
    [SerializeField, Min(0)] private int startingReserveAmmo = 48;
    [SerializeField, Min(0.05f)] private float secondsBetweenShots = 0.32f;
    [SerializeField, Min(1f)] private float projectileSpeed = 95f;
    [SerializeField, Range(0f, 20f)] private float projectileGravity = 1.2f;
    [SerializeField, Range(0f, 12f)] private float spreadDegrees = 0.45f;
    [SerializeField, Range(1, 16)] private int pelletsPerShot = 1;
    [SerializeField, Min(0.1f)] private float reloadSeconds = 1.65f;
    [SerializeField, Range(0f, 20f)] private float recoilPitch = 4.8f;
    [SerializeField, Range(0f, 8f)] private float recoilYaw = 0.8f;
    [SerializeField, Range(0f, 0.3f)] private float recoilKickback = 0.065f;
    [SerializeField, Range(0f, 20f)] private float impactImpulse = 4.5f;
    [SerializeField, Range(0.025f, 0.25f)] private float bulletVisualLength = 0.09f;
    [SerializeField, HideInInspector] private bool firearmConfigured;
    [SerializeField, HideInInspector] private int roundsInMagazine = -1;
    [SerializeField, HideInInspector] private int reserveAmmo = -1;

    private Rigidbody weaponBody;
    private Collider[] weaponColliders = Array.Empty<Collider>();
    private bool[] colliderStates = Array.Empty<bool>();
    private Transform originalParent;
    private bool held;
    private PhysicsMaterial runtimeFloorMaterial;
    private Collider[] ignoredOwnerColliders = Array.Empty<Collider>();
    private float ownerCollisionGrace;

    public bool IsHeld => held;
    public bool UsesTwoHands => gripMode == GripMode.TwoHanded ||
                                (gripMode == GripMode.Auto && IsTwoHandedName(name));
    public Vector3 BarrelAxisLocal => barrelAxisLocal.normalized;
    public Vector3 UpAxisLocal => upAxisLocal.normalized;
    public Vector3 PrimaryGripLocal => primaryGripLocal;
    public Vector3 SecondaryGripLocal
    {
        get
        {
            Vector3 offset = secondaryGripLocal - primaryGripLocal;
            float distance = transform.TransformVector(offset).magnitude;
            return primaryGripLocal + offset * (limitHandSpacing && distance > maximumHandSpacing ? maximumHandSpacing / distance : 1f);
        }
    }
    public Vector3 MuzzleLocal => muzzleLocal;
    public HandPose RightHandPose => rightHandPose;
    public HandPose LeftHandPose => leftHandPose;
    public bool IndependentHoldAnchor => independentHoldAnchor;
    public Vector3 HoldAnchorLocal => independentHoldAnchor ? holdAnchorLocal : primaryGripLocal;
    public Vector3 HoldPositionOffset => independentHoldAnchor ? holdPositionOffset : Vector3.zero;
    public Quaternion HoldRotationOffset => independentHoldAnchor
        ? Quaternion.Euler(holdRotationOffset) : Quaternion.identity;
    public FirearmClass WeaponClass => firearmClass;
    public GameObject ProjectileVisualPrefab => projectileVisualPrefab;
    public int MagazineSize => magazineSize;
    public int RoundsInMagazine => Mathf.Max(0, roundsInMagazine);
    public int ReserveAmmo => Mathf.Max(0, reserveAmmo);
    public float SecondsBetweenShots => secondsBetweenShots;
    public float ProjectileSpeed => projectileSpeed;
    public float ProjectileGravity => projectileGravity;
    public float SpreadDegrees => spreadDegrees;
    public int PelletsPerShot => pelletsPerShot;
    public float ReloadSeconds => reloadSeconds;
    public float RecoilPitch => recoilPitch;
    public float RecoilYaw => recoilYaw;
    public float RecoilKickback => recoilKickback;
    public float ImpactImpulse => impactImpulse;
    public float BulletVisualLength => bulletVisualLength;
    public float MuzzleFlashSize => firearmClass == FirearmClass.Shotgun ? 0.24f :
        firearmClass == FirearmClass.Sniper ? 0.2f : firearmClass == FirearmClass.Rifle ? 0.15f : 0.12f;
    public bool CanReload => roundsInMagazine < magazineSize && reserveAmmo > 0;
    public string DisplayName
    {
        get
        {
            string display = name.Replace("FloorPickup_", string.Empty).Replace('_', ' ');
            return display.Replace("Revovler", "Revolver");
        }
    }

    public void SetIndependentHoldAnchor(Vector3 anchor, Vector3 offset)
    {
        holdAnchorLocal = anchor;
        holdPositionOffset = offset;
        independentHoldAnchor = true;
        // Preserve the effective left target when removing the old spacing clamp.
        secondaryGripLocal = SecondaryGripLocal;
        limitHandSpacing = false;
    }

    public void SetIndependentHoldPose(Vector3 anchor, Vector3 positionOffset, Quaternion rotationOffset)
    {
        holdAnchorLocal = anchor;
        holdPositionOffset = positionOffset;
        holdRotationOffset = rotationOffset.eulerAngles;
        independentHoldAnchor = true;
        secondaryGripLocal = SecondaryGripLocal;
        limitHandSpacing = false;
    }

    public Quaternion GripFrame(bool rightHand, bool includeOffset = true)
    {
        Vector3 forward = transform.TransformDirection(BarrelAxisLocal).normalized;
        Vector3 up = transform.TransformDirection(UpAxisLocal).normalized;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        Quaternion frame = Quaternion.LookRotation(forward, rightHand ? -right : up);
        HandPose pose = rightHand ? rightHandPose : leftHandPose;
        return includeOffset && pose != null ? frame * Quaternion.Euler(pose.wristRotation) : frame;
    }

    public void SetGripMode(GripMode mode)
    {
        if (gripMode == mode) return;
        gripMode = mode;
        configured = false;
    }

    private void Awake()
    {
        EnsureFirearmConfiguration();
        ConfigureForFloor();
    }

    private void Start()
    {
        // Repair an initial embedded spawn once. Continuous collision and the
        // contact solver own the weapon from then on, including slopes.
        RecoverFromSurfacePenetration(3f);
    }

    private void FixedUpdate()
    {
        if (held || ignoredOwnerColliders.Length == 0) return;
        ownerCollisionGrace -= Time.fixedDeltaTime;
        if (ownerCollisionGrace > 0f) return;
        RestoreOwnerCollisions(false);
    }

    public void ConfigureForFloor()
    {
        if (held) return;
        EnsureFirearmConfiguration();
        DisableImportedAnimations();
        if (!configured) ConfigureGripFromModel();

        weaponBody = GetComponent<Rigidbody>();
        if (weaponBody == null) weaponBody = gameObject.AddComponent<Rigidbody>();
        weaponBody.mass = UsesTwoHands ? 3.2f : 1.25f;
        weaponBody.interpolation = RigidbodyInterpolation.Interpolate;
        weaponBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        weaponBody.detectCollisions = true;
        weaponBody.useGravity = true;
        weaponBody.isKinematic = false;
        weaponBody.linearDamping = airDamping;
        weaponBody.angularDamping = rotationDamping;
        weaponBody.maxDepenetrationVelocity = 2f;
        weaponBody.solverIterations = 12;
        weaponBody.solverVelocityIterations = 6;
        weaponBody.sleepThreshold = 0.015f;

        Bounds localBounds = CalculateLocalBounds();
        if (pickupCollider == null)
        {
            BoxCollider[] rootBoxes = GetComponents<BoxCollider>();
            for (int i = 0; i < rootBoxes.Length; i++)
            {
                if (rootBoxes[i] != null && !rootBoxes[i].isTrigger)
                {
                    pickupCollider = rootBoxes[i];
                    break;
                }
            }
            if (pickupCollider == null) pickupCollider = gameObject.AddComponent<BoxCollider>();
        }

        // Some weapon packs only contain trigger colliders for hit detection.
        // This root box is the dependable physical shell used against terrain.
        pickupCollider.isTrigger = false;
        pickupCollider.enabled = true;
        pickupCollider.center = localBounds.center;
        pickupCollider.size = EnsureMinimumSize(localBounds.size);
        pickupCollider.contactOffset = 0.005f;

        // A single body and shell prevent imported trigger/mesh colliders and
        // nested rigidbodies from fighting the pickup's contact solver.
        Rigidbody[] childBodies = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < childBodies.Length; i++)
        {
            if (childBodies[i] == weaponBody) continue;
            childBodies[i].isKinematic = true;
            childBodies[i].useGravity = false;
            childBodies[i].detectCollisions = false;
        }

        weaponColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < weaponColliders.Length; i++)
            weaponColliders[i].enabled = weaponColliders[i] == pickupCollider;

        if (Application.isPlaying)
        {
            if (runtimeFloorMaterial == null)
                runtimeFloorMaterial = new PhysicsMaterial("Pickup friction (runtime)");
            runtimeFloorMaterial.staticFriction = groundFriction;
            runtimeFloorMaterial.dynamicFriction = groundFriction * 0.85f;
            runtimeFloorMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
            runtimeFloorMaterial.bounciness = 0f;
            runtimeFloorMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
            pickupCollider.sharedMaterial = runtimeFloorMaterial;
        }
        weaponBody.ResetCenterOfMass();
        weaponBody.ResetInertiaTensor();

        colliderStates = new bool[weaponColliders.Length];
        for (int i = 0; i < weaponColliders.Length; i++)
            colliderStates[i] = weaponColliders[i] != null && weaponColliders[i].enabled;
    }

    public void ConfigureFirearm(GameObject bulletVisual, FirearmClass type)
    {
        bool needsPreset = !firearmConfigured || firearmClass != type;
        firearmClass = type;
        if (bulletVisual != null) projectileVisualPrefab = bulletVisual;
        if (needsPreset) ApplyFirearmPreset(type);
        firearmConfigured = true;
        InitializeAmmunitionIfNeeded();
    }

    public bool TryConsumeRound()
    {
        InitializeAmmunitionIfNeeded();
        if (roundsInMagazine <= 0) return false;
        roundsInMagazine--;
        return true;
    }

    public int FinishReload()
    {
        InitializeAmmunitionIfNeeded();
        int amount = Mathf.Min(magazineSize - roundsInMagazine, reserveAmmo);
        if (amount <= 0) return 0;
        roundsInMagazine += amount;
        reserveAmmo -= amount;
        return amount;
    }

    public void SetAmmunition(int magazine, int reserve)
    {
        roundsInMagazine = Mathf.Clamp(magazine, 0, magazineSize);
        reserveAmmo = Mathf.Max(0, reserve);
    }

    private void EnsureFirearmConfiguration()
    {
        if (!firearmConfigured)
        {
            string lower = name.ToLowerInvariant();
            FirearmClass inferred = lower.Contains("shot") ? FirearmClass.Shotgun :
                lower.Contains("sniper") ? FirearmClass.Sniper :
                lower.Contains("rifle") || lower.Contains("musket") ? FirearmClass.Rifle :
                FirearmClass.Revolver;
            firearmClass = inferred;
            ApplyFirearmPreset(inferred);
            firearmConfigured = true;
        }
#if UNITY_EDITOR
        // Keep the real downloaded bullet meshes available even before the
        // scene setup pass serializes their asset references.
        if (projectileVisualPrefab == null)
        {
            string assetPath = firearmClass == FirearmClass.Revolver
                ? "Assets/Third Party/RyanNein/MidPolyGunWeapons/Revolver/RevolverBullet.fbx"
                : "Assets/Third Party/RyanNein/MidPolyGunWeapons/Rifle/RifleBullet.fbx";
            projectileVisualPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }
#endif
        InitializeAmmunitionIfNeeded();
    }

    private void InitializeAmmunitionIfNeeded()
    {
        magazineSize = Mathf.Max(1, magazineSize);
        if (roundsInMagazine < 0) roundsInMagazine = magazineSize;
        if (reserveAmmo < 0) reserveAmmo = Mathf.Max(0, startingReserveAmmo);
        roundsInMagazine = Mathf.Clamp(roundsInMagazine, 0, magazineSize);
        reserveAmmo = Mathf.Max(0, reserveAmmo);
    }

    private void ApplyFirearmPreset(FirearmClass type)
    {
        switch (type)
        {
            case FirearmClass.Rifle:
                magazineSize = 8;
                startingReserveAmmo = 64;
                secondsBetweenShots = 0.42f;
                projectileSpeed = 135f;
                projectileGravity = 0.75f;
                spreadDegrees = 0.28f;
                pelletsPerShot = 1;
                reloadSeconds = 2.15f;
                recoilPitch = 4.2f;
                recoilYaw = 0.55f;
                recoilKickback = 0.075f;
                impactImpulse = 6f;
                bulletVisualLength = 0.11f;
                break;
            case FirearmClass.Shotgun:
                magazineSize = 2;
                startingReserveAmmo = 30;
                secondsBetweenShots = 0.72f;
                projectileSpeed = 88f;
                projectileGravity = 1.6f;
                spreadDegrees = 4.2f;
                pelletsPerShot = 8;
                reloadSeconds = 2.45f;
                recoilPitch = 9f;
                recoilYaw = 1.35f;
                recoilKickback = 0.14f;
                impactImpulse = 2.1f;
                bulletVisualLength = 0.055f;
                break;
            case FirearmClass.Sniper:
                magazineSize = 5;
                startingReserveAmmo = 25;
                secondsBetweenShots = 0.95f;
                projectileSpeed = 175f;
                projectileGravity = 0.28f;
                spreadDegrees = 0.08f;
                pelletsPerShot = 1;
                reloadSeconds = 2.9f;
                recoilPitch = 8f;
                recoilYaw = 0.85f;
                recoilKickback = 0.12f;
                impactImpulse = 9f;
                bulletVisualLength = 0.13f;
                break;
            default:
                magazineSize = 6;
                startingReserveAmmo = 48;
                secondsBetweenShots = 0.32f;
                projectileSpeed = 100f;
                projectileGravity = 1.05f;
                spreadDegrees = 0.48f;
                pelletsPerShot = 1;
                reloadSeconds = 1.65f;
                recoilPitch = 5f;
                recoilYaw = 0.85f;
                recoilKickback = 0.07f;
                impactImpulse = 4.8f;
                bulletVisualLength = 0.09f;
                break;
        }
        roundsInMagazine = magazineSize;
        reserveAmmo = startingReserveAmmo;
    }

    private void DisableImportedAnimations()
    {
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] == null) continue;
            animators[i].enabled = false;
        }

        Animation[] legacyAnimations = GetComponentsInChildren<Animation>(true);
        for (int i = 0; i < legacyAnimations.Length; i++)
        {
            if (legacyAnimations[i] == null) continue;
            legacyAnimations[i].Stop();
            legacyAnimations[i].playAutomatically = false;
            legacyAnimations[i].enabled = false;
        }
    }

    public void BeginHold(Transform holder)
    {
        if (held) return;
        RestoreOwnerCollisions(true);
        ConfigureForFloor();
        held = true;
        originalParent = transform.parent;
        WobblyActiveRagdoll owner = holder.GetComponentInParent<WobblyActiveRagdoll>();
        ignoredOwnerColliders = owner != null
            ? owner.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();
        transform.SetParent(holder, true);

        weaponBody.linearVelocity = Vector3.zero;
        weaponBody.angularVelocity = Vector3.zero;
        weaponBody.useGravity = false;
        weaponBody.isKinematic = true;
        weaponBody.interpolation = RigidbodyInterpolation.None;
        weaponBody.detectCollisions = false;
        for (int i = 0; i < weaponColliders.Length; i++)
            if (weaponColliders[i] != null) weaponColliders[i].enabled = false;
    }

    public void Drop(Vector3 position, Vector3 velocity)
    {
        if (!held) return;
        held = false;
        transform.SetParent(originalParent, true);
        weaponBody.position = position;
        weaponBody.rotation = transform.rotation;

        weaponBody.isKinematic = false;
        weaponBody.interpolation = RigidbodyInterpolation.Interpolate;
        weaponBody.useGravity = true;
        weaponBody.detectCollisions = true;
        weaponBody.linearVelocity = velocity;
        weaponBody.angularVelocity = Vector3.zero;
        for (int i = 0; i < weaponColliders.Length; i++)
        {
            if (weaponColliders[i] != null)
                weaponColliders[i].enabled = i < colliderStates.Length ? colliderStates[i] : true;
        }
        for (int i = 0; i < ignoredOwnerColliders.Length; i++)
            if (ignoredOwnerColliders[i] != null)
                Physics.IgnoreCollision(pickupCollider, ignoredOwnerColliders[i], true);
        ownerCollisionGrace = 0.2f;
        weaponBody.WakeUp();
    }

    private void RestoreOwnerCollisions(bool force)
    {
        bool pending = false;
        for (int i = 0; i < ignoredOwnerColliders.Length; i++)
        {
            Collider other = ignoredOwnerColliders[i];
            if (other == null || pickupCollider == null) continue;
            if (!force && other.enabled && Physics.ComputePenetration(
                    pickupCollider, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation, out _, out _))
            {
                pending = true;
                continue;
            }
            Physics.IgnoreCollision(pickupCollider, other, false);
            ignoredOwnerColliders[i] = null;
        }
        if (!pending) ignoredOwnerColliders = Array.Empty<Collider>();
    }

    private void OnDestroy()
    {
        RestoreOwnerCollisions(true);
        if (runtimeFloorMaterial != null) Destroy(runtimeFloorMaterial);
    }

    public Vector3 TransformGripPoint(Vector3 localPoint)
    {
        return transform.TransformPoint(localPoint);
    }

    private void RecoverFromSurfacePenetration(float probeHeight)
    {
        if (held || weaponBody == null || pickupCollider == null || !pickupCollider.enabled) return;

        Physics.SyncTransforms();
        Bounds bounds = pickupCollider.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.max.y + probeHeight, bounds.center.z);
        float distance = probeHeight + bounds.size.y + 0.35f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float surfaceHeight = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform) ||
                hitCollider.GetComponentInParent<WobblyActiveRagdoll>() != null ||
                hitCollider.GetComponentInParent<WobblyPickupWeapon>() != null ||
                hits[i].normal.y < 0.35f)
                continue;
            if (hits[i].point.y > surfaceHeight) surfaceHeight = hits[i].point.y;
        }

        if (float.IsNegativeInfinity(surfaceHeight) || bounds.min.y >= surfaceHeight - 0.008f) return;

        float lift = surfaceHeight + 0.025f - bounds.min.y;
        Vector3 correctedPosition = weaponBody.position + Vector3.up * lift;
        weaponBody.position = correctedPosition;
        Vector3 velocity = weaponBody.linearVelocity;
        if (velocity.y < 0f) velocity.y = 0f;
        weaponBody.linearVelocity = velocity;
        Physics.SyncTransforms();
    }

    public static void PrepareSceneWeapons()
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (!IsLikelyWeaponName(candidate.name)) continue;
            if (candidate.GetComponentInParent<WobblyActiveRagdoll>() != null) continue;
            if (candidate.GetComponentInParent<WobblyPickupWeapon>() != null) continue;
            if (candidate.GetComponentsInChildren<Renderer>(true).Length == 0) continue;

            WobblyPickupWeapon weapon = candidate.gameObject.AddComponent<WobblyPickupWeapon>();
            weapon.ConfigureForFloor();
        }
    }

    public static bool IsLikelyWeaponName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return false;
        string lower = objectName.ToLowerInvariant();
        if (lower.Contains("bullet") || lower.Contains("shell") || lower.Contains("ammo") ||
            lower.Contains("magazine") || lower.Contains("flash") || lower.Contains("spawn") ||
            lower.Contains("muzzle") || lower.Contains("knife") || lower.Contains("knive") ||
            lower.Contains("shop") || lower.Contains("store"))
            return false;

        return lower.Contains("gun") || lower.Contains("rifle") || lower.Contains("shotgun") ||
               lower.Contains("shot gun") || lower.Contains("sniper") || lower.Contains("revolver") ||
               lower.Contains("revovler") || lower.Contains("pistol") || lower.Contains("firearm") ||
               lower.Contains("musket");
    }

    private static bool IsTwoHandedName(string objectName)
    {
        string lower = objectName.ToLowerInvariant();
        return lower.Contains("rifle") || lower.Contains("shotgun") || lower.Contains("shot gun") ||
               lower.Contains("sniper") || lower.Contains("musket");
    }

    private void ConfigureGripFromModel()
    {
        Bounds bounds = CalculateLocalBounds();
        Vector3 size = EnsureMinimumSize(bounds.size);
        int longestAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

        Transform muzzle = FindNamedChild("muzzle", "bulletspawn", "bulletspwan", "firepoint", "barrelend");
        if (muzzle != null)
        {
            muzzleLocal = transform.InverseTransformPoint(muzzle.position);
            barrelAxisLocal = muzzleLocal - bounds.center;
        }
        else
        {
            float centerAlongAxis = longestAxis == 0 ? bounds.center.x : longestAxis == 1 ? bounds.center.y : bounds.center.z;
            float sign = Mathf.Abs(centerAlongAxis) > 0.01f ? Mathf.Sign(centerAlongAxis) : (longestAxis == 0 ? -1f : 1f);
            barrelAxisLocal = longestAxis == 0 ? Vector3.right * sign :
                              longestAxis == 1 ? Vector3.up * sign : Vector3.forward * sign;
            float halfLength = longestAxis == 0 ? size.x * 0.5f : longestAxis == 1 ? size.y * 0.5f : size.z * 0.5f;
            muzzleLocal = bounds.center + barrelAxisLocal * halfLength;
        }

        if (barrelAxisLocal.sqrMagnitude < 0.001f) barrelAxisLocal = Vector3.left;
        barrelAxisLocal.Normalize();
        upAxisLocal = Mathf.Abs(Vector3.Dot(barrelAxisLocal, Vector3.up)) < 0.85f
            ? Vector3.ProjectOnPlane(Vector3.up, barrelAxisLocal).normalized
            : Vector3.ProjectOnPlane(Vector3.forward, barrelAxisLocal).normalized;

        float length = Mathf.Abs(Vector3.Dot(size, Abs(barrelAxisLocal)));
        float height = Mathf.Max(0.05f, Mathf.Abs(Vector3.Dot(size, Abs(upAxisLocal))));
        bool twoHands = gripMode == GripMode.TwoHanded ||
                        (gripMode == GripMode.Auto && IsTwoHandedName(name));
        primaryGripLocal = bounds.center - barrelAxisLocal * length * (twoHands ? 0.31f : 0.22f) -
                           upAxisLocal * height * 0.18f;
        secondaryGripLocal = bounds.center + barrelAxisLocal * length * 0.08f -
                             upAxisLocal * height * 0.12f;
        configured = true;
    }

    private Transform FindNamedChild(params string[] fragments)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            string lower = children[i].name.ToLowerInvariant().Replace(" ", string.Empty);
            for (int f = 0; f < fragments.Length; f++)
                if (lower.Contains(fragments[f])) return children[i];
        }
        return null;
    }

    private Bounds CalculateLocalBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(Vector3.zero, new Vector3(0.5f, 0.18f, 0.12f));

        bool initialized = false;
        Bounds result = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            // Transform mesh-local bounds directly. Inverting a world AABB
            // inflated the shell every time a rotated gun was picked up.
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Bounds meshBounds = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds : renderer.localBounds;
            Matrix4x4 toRoot = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 meshCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 localCorner = toRoot.MultiplyPoint3x4(meshCorner);
                if (!initialized)
                {
                    result = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else result.Encapsulate(localCorner);
            }
        }
        return initialized ? result : new Bounds(Vector3.zero, new Vector3(0.5f, 0.18f, 0.12f));
    }

    private static Vector3 EnsureMinimumSize(Vector3 size)
    {
        return new Vector3(Mathf.Max(0.04f, Mathf.Abs(size.x)),
                           Mathf.Max(0.04f, Mathf.Abs(size.y)),
                           Mathf.Max(0.04f, Mathf.Abs(size.z)));
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
