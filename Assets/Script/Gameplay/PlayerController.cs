using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Active-ragdoll player: a stable physics core (pelvis) + ragdoll limbs, driven either by an
/// Animator (default) or by a procedural physics gait. Also handles vehicle seats, external
/// climbing control and train-wheel knockdown.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class PlayerController : MonoBehaviour
{
    // =====================================================================================
    //  INSPECTOR SETTINGS (grouped into foldouts)
    // =====================================================================================

    [Serializable]
    public sealed class MovementSettings
    {
        [Tooltip("Walking target speed (m/s). Can be changed live in Play mode.")]
        [Min(0f)] public float walkSpeed = 5.5f;
        [Tooltip("Running target speed (m/s) while holding Shift.")]
        [Min(0f)] public float runSpeed = 9f;
        [Tooltip("How quickly the whole ragdoll reaches the target speed (m/s²). Not a speed multiplier.")]
        [Range(2f, 30f)] public float acceleration = 24f;
        [Tooltip("Extra acceleration available while running.")]
        [Range(1f, 2f)] public float runAccelerationMultiplier = 1.25f;
        [Tooltip("Upward velocity applied on jump (m/s).")]
        [Min(0f)] public float jumpVelocity = 6.5f;
        [Tooltip("Extra downward acceleration while airborne, for a snappier jump.")]
        [Min(0f)] public float extraGravity = 8f;
        [Tooltip("Degrees per second the body can turn toward the aim direction while aiming.")]
        [Range(90f, 1080f)] public float aimTurnSpeed = 540f;
    }

    [Serializable]
    public sealed class GaitSettings
    {
        [Header("Cadence")]
        [Tooltip("Derive step cadence from real speed and stride length so feet never skate.")]
        public bool matchGaitToMovementSpeed = true;
        [Tooltip("Fine adjustment of automatic cadence. 1 = physically matched.")]
        [Range(0.65f, 1.35f)] public float gaitSpeedMatch = 1f;
        [Tooltip("Gait cycles per second. Used only when Match Gait To Movement Speed is off.")]
        [Min(0.1f)] public float manualCyclesPerSecond = 1.1f;
        [Tooltip("Run cadence multiplier. Used only when Match Gait To Movement Speed is off.")]
        [Range(1f, 2.2f)] public float runCadenceMultiplier = 1.5f;
        [Tooltip("How fast (per second) the legs/arms fade in when starting and out when stopping.")]
        [Range(2f, 20f)] public float gaitBlendRate = 8f;

        [Header("Stride")]
        [Tooltip("Step length relative to leg height.")]
        [Range(0.35f, 0.95f)] public float strideScale = 0.78f;
        [Tooltip("Stride gets this much longer as speed approaches Fast Stride Speed.")]
        [Range(1f, 1.8f)] public float fastStrideMultiplier = 1.5f;
        [Tooltip("Speed (m/s) at which the full fast stride is used.")]
        [Min(1f)] public float fastStrideSpeed = 8f;
        [Tooltip("Run stride relative to the walking stride (blended smoothly).")]
        [Range(1f, 1.6f)] public float runStrideMultiplier = 1.22f;
        [Tooltip("Fraction of the cycle each foot spends in the air while walking. Lower = more double-support, calmer walk.")]
        [Range(0.3f, 0.6f)] public float walkSwingFraction = 0.44f;
        [Tooltip("Fraction of the cycle each foot spends in the air while running. Above 0.5 gives a small flight phase.")]
        [Range(0.4f, 0.7f)] public float runSwingFraction = 0.55f;

        [Header("Body motion")]
        [Tooltip("Foot lift relative to leg height.")]
        [Range(0.1f, 0.5f)] public float stepLiftScale = 0.36f;
        [Tooltip("Small natural rise of the body while walking.")]
        [Range(0f, 0.12f)] public float bodyBobHeight = 0.06f;
        [Range(1f, 1.5f)] public float runBobMultiplier = 1.12f;
        [Tooltip("Lowers the standing pelvis so both feet rest on the surface instead of floating.")]
        [Range(0f, 0.15f)] public float standingHeightReduction = 0.055f;
        [Range(10f, 65f)] public float legSwing = 34f;
        [Range(10f, 80f)] public float kneeBend = 46f;
        [Range(0f, 50f)] public float armSwing = 26f;
        [Tooltip("Arm swing multiplier while running.")]
        [Range(1f, 2f)] public float runArmSwingMultiplier = 1.4f;
        [Range(0f, 12f)] public float idleWobble = 4f;

        [Header("Physical feet (increase damping if legs vibrate)")]
        [Range(3f, 30f)] public float footTargetSharpness = 12f;
        [Range(40f, 220f)] public float footPositionStrength = 125f;
        [Range(10f, 60f)] public float footVelocityDamping = 34f;
        [Range(40f, 180f)] public float footMaxAcceleration = 105f;
        [Tooltip("Only a turn larger than this (degrees) releases planted feet.")]
        [Range(30f, 150f)] public float footReplantTurnAngle = 80f;

        [Header("Standing turn steps")]
        [Range(0.1f, 0.4f)] public float turnStepDuration = 0.18f;
        [Range(0.03f, 0.18f)] public float turnStepDistance = 0.07f;
        [Range(0.02f, 0.15f)] public float turnStepLift = 0.06f;
    }

    [Serializable]
    public sealed class RagdollSettings
    {
        [Header("Muscles")]
        public float lowerBodyStrength = 140f;
        public float armStrength = 38f;
        public float muscleDamping = 10f;
        [Tooltip("Extra angular damping for leg bodies.")]
        [Range(2f, 8f)] public float legDampingMultiplier = 5.2f;
        [Tooltip("How long a hard impact loosens the muscles.")]
        public float impactFlopSeconds = 0.55f;
        [Tooltip("How quickly the head turns toward the camera.")]
        public float headLookSharpness = 8f;

        [Header("Knockdown (train wheel hit)")]
        [Range(0.5f, 3f)] public float knockdownDuration = 1.15f;
        [Tooltip("Angular damping while loose. Lower = more natural fall.")]
        [Range(0f, 2f)] public float ragdollAngularDamping = 0.55f;
    }

    [Serializable]
    public sealed class SeatPose
    {
        [Header("Position (relative to the seat anchor)")]
        [Tooltip("Distance of the player from the steering wheel in metres. Positive = further BACK from the wheel, negative = closer.")]
        [Range(-0.5f, 1f)] public float distanceFromWheel;
        [Tooltip("Raise (+) or lower (-) the player relative to the seat anchor, in metres.")]
        [Range(-0.5f, 0.5f)] public float heightOffset;
        [Tooltip("Move the player right (+) or left (-) relative to the seat anchor, in metres.")]
        [Range(-0.5f, 0.5f)] public float sideOffset;

        [Header("Legs")]
        public Vector3 thighEuler = new Vector3(68f, 0f, 0f);
        public Vector3 shinEuler = new Vector3(82f, 0f, 0f);
        public Vector3 footEuler = new Vector3(-24f, 0f, 0f);

        [Header("Arms (left arm is mirrored automatically)")]
        public Vector3 armEuler = new Vector3(8f, 0f, 10f);
        [Range(0f, 90f)] public float forearmBend = 28f;

        public static SeatPose Driver() => new SeatPose { armEuler = new Vector3(-28f, 0f, 18f), forearmBend = 52f };
    }

    [Header("Movement")]
    [SerializeField] private MovementSettings movement = new MovementSettings();

    [Header("Animation")]
    [Tooltip("Animator on RobberVisualRig. Found automatically when empty.")]
    [SerializeField] private Animator movementAnimator;
    [Tooltip("Yaw correction (degrees) for clips authored facing the opposite axis.")]
    [SerializeField, Range(-180f, 180f)] private float animatedFacingOffset;
    [Tooltip("Start with the procedural physics pose instead of the Animator.")]
    [SerializeField] private bool startWithPhysicsPose;
    [Tooltip("Type this on the keyboard in Play mode to switch to the procedural physics pose.")]
    [SerializeField] private string physicsPoseCode = "pandu";

    [Header("Procedural Walk")]
    [SerializeField] private GaitSettings gait = new GaitSettings();

    [Header("Active Ragdoll")]
    [SerializeField] private RagdollSettings ragdoll = new RagdollSettings();

    [Header("Vehicle Seats")]
    [Tooltip("How fast (m/s) the player slides to a newly changed seat position, e.g. when you start driving.")]
    [SerializeField, Min(0.1f)] private float seatAdjustSpeed = 3f;
    [Tooltip("Pose and position used for passenger seats (and while seated but not driving).")]
    [SerializeField] private SeatPose passengerSeat = new SeatPose();
    [Tooltip("Pose and position used while driving. Use Distance From Wheel to set how far the player sits from the steering wheel.")]
    [SerializeField] private SeatPose driverSeat = SeatPose.Driver();

    // =====================================================================================
    //  PUBLIC API (unchanged for other scripts)
    // =====================================================================================

    public Transform Pelvis => pelvis;
    public Transform Chest => chest;
    public Transform Head => head != null ? head : chest;
    public Transform LeftUpperArm => leftArm;
    public Transform LeftShoulder => leftShoulder;
    public Transform RightUpperArm => rightArm;
    public Transform LeftForearm => leftForearm;
    public Transform RightForearm => rightForearm;
    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;
    public Vector3 FacingDirection => GetCurrentBodyFacing();
    public bool IsGrounded { get; private set; }
    public bool ArmsReserved { get; set; }
    public bool SupportHandReserved { get; set; }
    public Rigidbody PhysicsBody => pelvisBody;
    public float StandingPelvisHeight => Mathf.Max(0.45f, standingPelvisHeight - gait.standingHeightReduction);
    public bool IsExternallyControlled => externalMotionControl;
    public Vector3 MovementVelocity => pelvisBody != null ? pelvisBody.linearVelocity : Vector3.zero;
    public bool IsVehicleSeated => vehicleSeatAnchor != null;
    public bool IsKnockedDown => knockdownActive;
    public bool IsPoseRecoveryLocked => knockdownActive || recoveryPoseLockFrames > 0;
    public Vector3 CameraAnchorPosition =>
        IsVehicleSeated ? GetSeatPosition()
        : externalMotionControl && externalRenderPoseValid ? externalRenderPelvisPosition
        : pelvisBody != null ? pelvisBody.transform.position
        : pelvis != null ? pelvis.position : transform.position;

    /// <summary>Fired when a foot touches down. Argument: true = left foot. Handy for footstep audio.</summary>
    public event Action<bool> FootPlanted;

    // =====================================================================================
    //  INTERNAL TYPES / STATE
    // =====================================================================================

    private sealed class Muscle
    {
        public Transform visualBone, physicsBone;
        public Rigidbody body;
        public Quaternion restRelativeToPelvis, visualRotationOffset;
        public float strength;
        public Vector3 pose;
        public bool isLeg, isRight;
    }

    private sealed class Foot
    {
        public readonly bool isLeft;
        public Transform thigh, shin, foot;
        public Rigidbody body;
        public CapsuleCollider capsule;
        public float sideOffset;
        // Leg IK data
        public bool ikValid;
        public Vector3 thighAxis, shinAxis, thighRef, shinRef, kneePole;
        public float thighLength, shinLength;
        public Quaternion footRest;
        // Stepping state
        public bool wasSwinging, hasPlant, targetValid, renderPrevValid;
        public Transform plantSurface;
        public Vector3 plantLocal, target, renderPrev, swingStart;
        public Foot(bool left) { isLeft = left; }
    }

    private const float TwoPi = Mathf.PI * 2f;
    private static readonly int AnimatorSpeed = Animator.StringToHash("speed");
    private static readonly int AnimatorJumping = Animator.StringToHash("is jumping");
    private static readonly int JumpingState = Animator.StringToHash("Jumping");
    private static readonly int TraversalHoldState = Animator.StringToHash("Hold");
    private static readonly int TraversalMoveState = Animator.StringToHash("Move");
    private static readonly int TraversalTopOutState = Animator.StringToHash("Top Out");

    // Hip and spine are animation bones; only limbs are ragdoll bodies.
    private static readonly string[] PhysicalBoneNames =
    {
        "arm.L", "forearm.L", "hand.L", "arm.R", "forearm.R", "hand.R",
        "thigh.L", "shin.L", "shin.L.001", "thigh.R", "shin.R", "shin.R.001"
    };

    private static readonly string[] VisualBoneNames =
    {
        "hip", "hip.L", "hip.R", "spine", "spine.001", "spine.002", "spine.003",
        "arm.L", "forearm.L", "hand.L", "arm.R", "forearm.R", "hand.R",
        "thigh.L", "shin.L", "shin.L.001", "thigh.R", "shin.R", "shin.R.001"
    };

    private readonly List<Muscle> muscles = new List<Muscle>();
    private readonly Dictionary<Transform, Muscle> muscleByBone = new Dictionary<Transform, Muscle>();
    private readonly Dictionary<Transform, Rigidbody> bodyByVisualBone = new Dictionary<Transform, Rigidbody>();
    private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> visualRestPose =
        new List<(Transform, Vector3, Quaternion, Vector3)>();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[24];
    private readonly Foot[] feet = { new Foot(true), new Foot(false) };
    private Foot LeftLeg => feet[0];
    private Foot RightLeg => feet[1];

    // Bones
    private Transform physicsRoot, visualRoot, pelvis, chest, head, leftHip, rightHip;
    private Transform leftArm, leftShoulder, rightArm, leftForearm, rightForearm, leftHand, rightHand;
    private Rigidbody pelvisBody;
    private readonly Vector3[] hipRestPos = new Vector3[2];
    private readonly Quaternion[] hipRestRot = new Quaternion[2];
    private Quaternion pelvisVisualRotationOffset, headRestRelativeToPelvis, headRestLocalRotation;
    private Quaternion balanceRestWorld, movementFacingWorld, smoothedHeadWorld;
    private Quaternion visualRootRestRotation;
    private Vector3 visualRootRestOffsetFromPelvis, animatedArmatureRestLocalPosition;
    private Transform animatedArmature;
    private bool headSmoothingReady;

    // Input / facing
    private Vector3 restForward, desiredFacing;
    private Vector2 moveInput;
    private bool runInput;
    private Vector3 movementIntegral, previousMoveDirection;
    private int codeIndex;
    private WeaponController weaponController;

    // Pose driver
    private bool dynamicPoseActive, poseDriverInitialized, jumpAnimationActive;

    // Ground / support
    private bool hasSupportSurface;
    private RaycastHit supportHit;
    private Rigidbody trackedSupportBody;
    private Vector3 trackedSupportVelocity, airborneInheritedVelocity;
    private bool supportVelocityReady, hasAirborneInheritedVelocity;
    private float groundProbeDistance = 1.1f, renderedBodyHeight = 1.8f, standingPelvisHeight = 0.9f;
    private float totalRagdollMass = 40f, jumpSupportCooldown;

    // Gait
    private float gaitPhase, gaitAmount, runBlend, swingFraction = 0.5f, currentGaitFrequency;
    private float bodyBobOffset, bodyBobVelocity;
    private bool standingFeetReady;
    private int turningFoot = -1, lastTurningFoot = 1;
    private float turnStepProgress;
    private Vector3 turnStepStartLocal, turnStepEndLocal;

    // Impacts / knockdown
    private float flopTimer, knockdownTimer, pelvisAngularDampingBeforeKnockdown;
    private bool knockdownActive, animatorWasEnabledBeforeKnockdown;
    private int recoveryPoseLockFrames;

    // Vehicle
    private Transform vehicleSeatAnchor;
    private Rigidbody seatedVehicleBody;
    private Quaternion vehicleSeatRotationOffset = Quaternion.identity;
    private Vector3 seatOffsetCurrent;
    private bool vehicleDrivingPose, weaponWasEnabledBeforeSeat, animatorWasEnabledBeforeSeat;
    private RigidbodyInterpolation pelvisInterpolationBeforeSeat;

    // External (climb) control
    private bool externalMotionControl, externalRenderPoseValid, externalClimbAnimatorActive;
    private bool animatorWasEnabledBeforeExternalControl;
    private Vector3 externalRenderPelvisPosition;
    private int externalClimbLayer = -1, externalClimbState;
    private RigidbodyInterpolation pelvisInterpolationBeforeExternalControl;

    private float ActiveMoveSpeed => Mathf.Max(0f, runInput ? movement.runSpeed : movement.walkSpeed);

    // =====================================================================================
    //  UNITY LIFECYCLE
    // =====================================================================================

    private void Awake()
    {
        BuildRagdoll();
        if (pelvisBody == null)
        {
            enabled = false;
            Debug.LogError("PlayerController could not build the physics pelvis.", this);
            return;
        }

        balanceRestWorld = movementFacingWorld = pelvisBody.rotation;
        // The imported character faces opposite Unity's +Z root direction.
        restForward = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
        if (restForward.sqrMagnitude < 0.01f) restForward = Vector3.forward;
        desiredFacing = restForward;

        CacheLegIk();
        CacheFootOffsets();
        float legLength = EstimateLegLength();
        groundProbeDistance = Mathf.Max(0.65f, legLength + 0.25f);
        standingPelvisHeight = Mathf.Clamp(legLength, renderedBodyHeight * 0.35f, renderedBodyHeight * 0.58f);
        totalRagdollMass = pelvisBody.mass;
        foreach (Muscle m in muscles) totalRagdollMass += m.body.mass;

        CacheMovementAnimator();
        CacheVisualRestPose();
        if (head != null) headRestLocalRotation = head.localRotation;
        if (visualRoot != null)
        {
            visualRootRestRotation = visualRoot.rotation;
            visualRootRestOffsetFromPelvis = Quaternion.Inverse(balanceRestWorld) * (visualRoot.position - pelvis.position);
            animatedArmature = visualRoot.Find("Armature");
            if (animatedArmature != null) animatedArmatureRestLocalPosition = animatedArmature.localPosition;
        }
        SetPoseDriver(startWithPhysicsPose);
    }

    private void Update()
    {
        if (MenuManager.BlocksGameplay) { ClearInput(); return; }
        if (weaponController == null) weaponController = GetComponent<WeaponController>();
        if (!knockdownActive && recoveryPoseLockFrames > 0) recoveryPoseLockFrames--;

        if (knockdownActive)
        {
            ClearInput();
            knockdownTimer = Mathf.Max(0f, knockdownTimer - Time.deltaTime);
            flopTimer = knockdownTimer;
            if (knockdownTimer <= 0f) FinishKnockdown();
            return;
        }

        UpdatePoseHotkey();

        if (externalMotionControl || IsVehicleSeated)
        {
            ClearInput();
            flopTimer = 0f;
            jumpSupportCooldown = 0f;
            if (!externalMotionControl) UpdateMovementAnimator();
            return;
        }

        moveInput = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
        runInput = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetButtonDown("Jump") && IsGrounded && pelvisBody != null) Jump();

        flopTimer = Mathf.Max(0f, flopTimer - Time.deltaTime);
        jumpSupportCooldown = Mathf.Max(0f, jumpSupportCooldown - Time.deltaTime);
        UpdateMovementAnimator();
    }

    private void FixedUpdate()
    {
        if (pelvisBody == null || externalMotionControl) return;
        if (IsVehicleSeated) { UpdateVehicleSeatPose(); return; }
        if (knockdownActive)
        {
            pelvisBody.AddForce(Vector3.down * movement.extraGravity, ForceMode.Acceleration);
            return;
        }

        // Keep the previous physics sample for render-time foot IK interpolation.
        foreach (Foot f in feet) { f.renderPrev = f.target; f.renderPrevValid = f.targetValid; }

        Vector3 move = GetCameraRelativeMove();
        UpdateGrounded();
        if (dynamicPoseActive)
        {
            PreventFeetFromEnteringSupport();
            UpdateProceduralPose(move);
        }
        else bodyBobOffset = bodyBobVelocity = 0f;

        DriveLegSupport();
        DriveMovement(move);
        UpdateFacingFromMovement(move);
        if (dynamicPoseActive)
        {
            DriveMuscles();
            DrivePhysicalSteps(move);
        }
        if (!IsGrounded) pelvisBody.AddForce(Vector3.down * movement.extraGravity, ForceMode.Acceleration);
    }

    private void LateUpdate()
    {
        if (pelvisBody == null || pelvis == null) return;

        if (knockdownActive)
        {
            ApplyLooseRagdollVisualPose();
            return;
        }
        if (externalMotionControl)
        {
            if (externalClimbAnimatorActive) AnchorAnimatedVisualAt(pelvisBody.transform.position, movementFacingWorld);
            else ApplyKinematicVisualPose(pelvisBody.transform.position, movementFacingWorld);
            UpdateHeadLook();
            return;
        }
        if (IsVehicleSeated)
        {
            ApplyKinematicVisualPose(GetSeatPosition(), GetVehicleSeatRotation());
            UpdateHeadLook();
            return;
        }
        if (!dynamicPoseActive && movementAnimator != null && movementAnimator.enabled)
        {
            AnchorAnimatedVisualAt(pelvisBody.transform.position, movementFacingWorld);
            UpdateHeadLook();
            return;
        }

        // Procedural / physics pose: visual skeleton follows the core and the limb proxies.
        PlacePelvisVisual(pelvisBody.transform.position, movementFacingWorld);
        foreach (Muscle m in muscles)
        {
            if (m.visualBone == null || m.physicsBone == null || m.isLeg) continue;
            bool gripArm = ArmsReserved && (m.isRight || SupportHandReserved);
            // A gripping wrist needs a coherent body-relative base pose for the arm solver.
            m.visualBone.rotation = gripArm
                ? movementFacingWorld * m.restRelativeToPelvis * m.visualRotationOffset
                : m.physicsBone.rotation * m.visualRotationOffset;
        }
        foreach (Foot f in feet) SolveVisualLegIk(f);
        UpdateHeadLook();
    }

    // =====================================================================================
    //  INPUT / JUMP
    // =====================================================================================

    private void ClearInput() { moveInput = Vector2.zero; runInput = false; }

    private void Jump()
    {
        jumpAnimationActive = true;
        Vector3 jumpDirection = hasSupportSurface ? supportHit.normal.normalized : Vector3.up;
        Vector3 supportVelocity = GetSupportVelocityAtPoint(pelvisBody.position);
        airborneInheritedVelocity = supportVelocity;
        hasAirborneInheritedVelocity = true;
        Vector3 relativeMove = GetCameraRelativeMove();
        Vector3 relativeTakeoff = Vector3.ProjectOnPlane(relativeMove, jumpDirection).normalized *
                                  Mathf.Clamp01(relativeMove.magnitude) * ActiveMoveSpeed;
        ApplyJumpTakeoffVelocity(pelvisBody, jumpDirection, supportVelocity, relativeTakeoff);
        if (dynamicPoseActive)
            foreach (Muscle m in muscles) ApplyJumpTakeoffVelocity(m.body, jumpDirection, supportVelocity, relativeTakeoff);

        jumpSupportCooldown = 0.28f;
        IsGrounded = false;
        hasSupportSurface = false;
        ClearFootPlants();
    }

    private void ApplyJumpTakeoffVelocity(Rigidbody body, Vector3 jumpDirection, Vector3 supportVelocity, Vector3 relativeVelocity)
    {
        if (body == null) return;
        Vector3 normal = jumpDirection.sqrMagnitude > 0.001f ? jumpDirection.normalized : Vector3.up;
        body.linearVelocity = Vector3.ProjectOnPlane(supportVelocity, normal) +
                              Vector3.ProjectOnPlane(relativeVelocity, normal) +
                              normal * (Vector3.Dot(supportVelocity, normal) + movement.jumpVelocity);
    }

    private Vector3 GetCameraRelativeMove()
    {
        Transform view = Camera.main != null ? Camera.main.transform : null;
        Vector3 forward = view != null ? Vector3.ProjectOnPlane(view.forward, Vector3.up) : transform.forward;
        Vector3 right = view != null ? Vector3.ProjectOnPlane(view.right, Vector3.up) : transform.right;
        if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
        if (right.sqrMagnitude < 0.01f) right = transform.right;
        return Vector3.ClampMagnitude(forward.normalized * moveInput.y + right.normalized * moveInput.x, 1f);
    }

    private void UpdatePoseHotkey()
    {
        if (dynamicPoseActive || string.IsNullOrEmpty(physicsPoseCode)) return;
        foreach (char raw in Input.inputString)
        {
            char c = char.ToLowerInvariant(raw);
            if (c == char.ToLowerInvariant(physicsPoseCode[codeIndex]))
            {
                if (++codeIndex < physicsPoseCode.Length) continue;
                codeIndex = 0;
                SetPoseDriver(true);
                return;
            }
            codeIndex = c == char.ToLowerInvariant(physicsPoseCode[0]) ? 1 : 0;
        }
    }

    [ContextMenu("Use Procedural Physics Pose")]
    public void UsePhysicsPose() => SetPoseDriver(true);

    [ContextMenu("Use Animated Pose")]
    public void UseAnimatedPose() => SetPoseDriver(false);

    // =====================================================================================
    //  POSE DRIVER (animator <-> procedural physics)
    // =====================================================================================

    private void SetPoseDriver(bool usePhysicsPose)
    {
        CacheMovementAnimator();
        if (!usePhysicsPose && (movementAnimator == null || movementAnimator.runtimeAnimatorController == null))
            usePhysicsPose = true;
        if (poseDriverInitialized && dynamicPoseActive == usePhysicsPose) return;

        dynamicPoseActive = usePhysicsPose;
        poseDriverInitialized = true;
        headSmoothingReady = false;
        jumpAnimationActive = false;
        if (movementAnimator != null) movementAnimator.speed = 1f;
        ClearFootPlants();

        if (dynamicPoseActive)
        {
            if (movementAnimator != null) movementAnimator.enabled = false;
            SynchronizePhysicsLimbsToVisuals();
            ConfigureLimbSimulation(true);
        }
        else RestoreAnimatedPose(!IsVehicleSeated && !externalMotionControl);
    }

    private void CacheMovementAnimator()
    {
        if (movementAnimator == null && visualRoot != null) movementAnimator = visualRoot.GetComponent<Animator>();
        if (movementAnimator == null) movementAnimator = GetComponentInChildren<Animator>(true);
        if (movementAnimator == null) return;
        movementAnimator.applyRootMotion = false;
        movementAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        movementAnimator.updateMode = AnimatorUpdateMode.Normal;
        movementAnimator.animatePhysics = false;
    }

    private void ResetProceduralPoseOffsets()
    {
        foreach (Muscle m in muscles) m.pose = Vector3.zero;
        bodyBobOffset = bodyBobVelocity = gaitPhase = currentGaitFrequency = gaitAmount = 0f;
        ArmsReserved = SupportHandReserved = false;
        ClearFootPlants();
    }

    private void RestoreAnimatedPose(bool enableAnimator)
    {
        ResetProceduralPoseOffsets();
        ConfigureLimbSimulation(false);
        RestoreVisualRestPose();
        if (movementAnimator != null)
        {
            movementAnimator.enabled = enableAnimator;
            if (enableAnimator)
            {
                movementAnimator.Rebind();
                movementAnimator.Play(0, 0, 0f);
                movementAnimator.Update(0f);
                AnchorAnimatedVisualAt(pelvisBody.transform.position, movementFacingWorld);
                movementAnimator.Update(0f);
            }
        }
        SynchronizePhysicsLimbsToVisuals();
        ConfigureLimbSimulation(false);
        headSmoothingReady = false;
    }

    private void CacheVisualRestPose()
    {
        if (visualRoot == null || visualRestPose.Count > 0) return;
        foreach (Transform t in visualRoot.GetComponentsInChildren<Transform>(true))
            if (t != visualRoot) visualRestPose.Add((t, t.localPosition, t.localRotation, t.localScale));
    }

    private void RestoreVisualRestPose()
    {
        foreach (var e in visualRestPose)
        {
            if (e.t == null) continue;
            e.t.localPosition = e.p;
            e.t.localRotation = e.r;
            e.t.localScale = e.s;
        }
    }

    private void SynchronizePhysicsLimbsToVisuals()
    {
        foreach (Muscle m in muscles)
        {
            if (m.body == null || m.visualBone == null || m.physicsBone == null) continue;
            m.body.isKinematic = true;
            m.physicsBone.SetPositionAndRotation(m.visualBone.position,
                m.visualBone.rotation * Quaternion.Inverse(m.visualRotationOffset));
        }
    }

    private void ConfigureLimbSimulation(bool dynamicLimbs)
    {
        foreach (Muscle m in muscles)
        {
            Rigidbody body = m.body;
            if (body == null) continue;
            Collider col = body.GetComponent<Collider>();
            if (dynamicLimbs)
            {
                Rigidbody connected = FindConnectedBody(m.visualBone);
                if (connected != null) ConfigureJoint(m.physicsBone, connected, m.visualBone.name);
                body.isKinematic = false;
                body.useGravity = !IsVehicleSeated;
                body.detectCollisions = !IsVehicleSeated;
                body.linearVelocity = pelvisBody != null ? pelvisBody.linearVelocity : Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (col != null) col.enabled = !IsVehicleSeated;
            }
            else
            {
                ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
                if (joint != null) joint.connectedBody = null;
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.useGravity = false;
                body.detectCollisions = false;
                body.isKinematic = true;
                if (col != null) col.enabled = false;
            }
        }
    }

    // =====================================================================================
    //  VISUAL POSE HELPERS
    // =====================================================================================

    private void PlacePelvisVisual(Vector3 position, Quaternion facing)
    {
        if (visualRoot != null) visualRoot.position += position - pelvis.position;
        pelvis.rotation = facing * pelvisVisualRotationOffset;
        AlignHip(leftHip, 0);
        AlignHip(rightHip, 1);
    }

    private void AlignHip(Transform hip, int i)
    {
        if (hip == null || pelvis == null) return;
        hip.SetPositionAndRotation(pelvis.TransformPoint(hipRestPos[i]), pelvis.rotation * hipRestRot[i]);
    }

    private void ApplyKinematicVisualPose(Vector3 pelvisPosition, Quaternion facing)
    {
        PlacePelvisVisual(pelvisPosition, facing);
        foreach (Muscle m in muscles)
            if (m.visualBone != null)
                m.visualBone.rotation = facing * m.restRelativeToPelvis * Quaternion.Euler(m.pose) * m.visualRotationOffset;
    }

    private void ApplyLooseRagdollVisualPose()
    {
        // Knockdown: the skeleton follows the free physics bodies; no IK or head tracking.
        PlacePelvisVisual(pelvisBody.transform.position, pelvisBody.transform.rotation);
        foreach (Muscle m in muscles)
            if (m.visualBone != null && m.physicsBone != null)
                m.visualBone.rotation = m.physicsBone.rotation * m.visualRotationOffset;
        if (head != null) head.localRotation = headRestLocalRotation;
        headSmoothingReady = false;
    }

    private void AnchorAnimatedVisualAt(Vector3 pelvisPosition, Quaternion facing)
    {
        if (visualRoot == null) return;
        Quaternion facingDelta = facing * Quaternion.Inverse(balanceRestWorld);
        visualRoot.SetPositionAndRotation(
            pelvisPosition + facingDelta * visualRootRestOffsetFromPelvis,
            facingDelta * Quaternion.AngleAxis(animatedFacingOffset, Vector3.up) * visualRootRestRotation);

        if (animatedArmature != null)
        {
            // Movement is physics driven: strip object-level travel from the clip.
            Vector3 p = animatedArmature.localPosition;
            p.x = animatedArmatureRestLocalPosition.x;
            p.z = animatedArmatureRestLocalPosition.z;
            if (jumpAnimationActive || externalClimbAnimatorActive) p.y = animatedArmatureRestLocalPosition.y;
            animatedArmature.localPosition = p;
        }
    }

    private void UpdateMovementAnimator()
    {
        if (movementAnimator == null || !movementAnimator.enabled || movementAnimator.runtimeAnimatorController == null) return;

        Vector3 platformVelocity = hasSupportSurface && supportHit.rigidbody != null
            ? supportHit.rigidbody.GetPointVelocity(pelvisBody.position) : Vector3.zero;
        float speed = Vector3.ProjectOnPlane(pelvisBody.linearVelocity - platformVelocity,
            hasSupportSurface ? supportHit.normal : Vector3.up).magnitude;

        float blend;
        if (speed < 0.04f || moveInput.sqrMagnitude < 0.0025f) blend = 0f;
        else if (speed <= movement.walkSpeed || movement.runSpeed <= movement.walkSpeed + 0.01f)
            blend = 0.5f * Mathf.Clamp01(speed / Mathf.Max(0.1f, movement.walkSpeed));
        else blend = 0.5f + 0.5f * Mathf.InverseLerp(movement.walkSpeed, movement.runSpeed, speed);

        movementAnimator.speed = 1f;
        movementAnimator.SetFloat(AnimatorSpeed, blend, 0.08f, Time.deltaTime);
        movementAnimator.SetBool(AnimatorJumping, jumpAnimationActive);

        if (!jumpAnimationActive) return;
        AnimatorStateInfo state = movementAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == JumpingState && state.normalizedTime >= 0.98f && !movementAnimator.IsInTransition(0))
        {
            jumpAnimationActive = false;
            movementAnimator.SetBool(AnimatorJumping, false);
        }
    }

    private void UpdateHeadLook()
    {
        if (head == null) return;
        Quaternion target = GetCameraLookTarget();
        if (!headSmoothingReady) { smoothedHeadWorld = target; headSmoothingReady = true; }
        smoothedHeadWorld = Quaternion.Slerp(smoothedHeadWorld, target,
            1f - Mathf.Exp(-ragdoll.headLookSharpness * Time.deltaTime));
        head.rotation = smoothedHeadWorld;
    }

    private Quaternion GetCameraLookTarget()
    {
        // Animator pose already includes the authored-axis correction; use it directly.
        Quaternion restTarget = !dynamicPoseActive && !IsVehicleSeated && movementAnimator != null && movementAnimator.enabled
            ? head.rotation
            : movementFacingWorld * headRestRelativeToPelvis;
        Camera view = Camera.main;
        if (view == null) return restTarget;

        Vector3 bodyFacing = GetCurrentBodyFacing();
        Vector3 toCamera = view.transform.position - head.position;
        float cameraDistance = toCamera.magnitude;
        Vector3 cameraForward = view.transform.forward.normalized;
        Vector3 flatAim = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;
        if (flatAim.sqrMagnitude < 0.01f) flatAim = bodyFacing;
        Vector3 flatToCamera = Vector3.ProjectOnPlane(toCamera, Vector3.up).normalized;
        if (flatToCamera.sqrMagnitude < 0.01f) flatToCamera = bodyFacing;

        // Blend between "look where the camera aims" (camera behind) and "look at the camera" (camera in front).
        bool aiming = weaponController != null && weaponController.IsAiming;
        float frontBlend = !aiming && cameraDistance > 0.7f
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.35f, Vector3.Dot(flatToCamera, bodyFacing)))
            : 0f;

        float aimYaw = Mathf.Clamp(Vector3.SignedAngle(bodyFacing, flatAim, Vector3.up), -80f, 80f);
        float camYaw = Mathf.Clamp(Vector3.SignedAngle(bodyFacing, flatToCamera, Vector3.up), -80f, 80f);
        float yaw = Mathf.LerpAngle(aimYaw, camYaw, frontBlend);
        float aimPitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(cameraForward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        float camPitch = cameraDistance > 0.001f
            ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(toCamera.normalized, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg
            : aimPitch;
        float pitch = Mathf.Clamp(Mathf.LerpAngle(aimPitch, camPitch, frontBlend), -40f, 50f);

        Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyFacing).normalized;
        Quaternion yawRot = Quaternion.AngleAxis(yaw, Vector3.up);
        return Quaternion.AngleAxis(-pitch, yawRot * bodyRight) * yawRot * restTarget;
    }

    private Vector3 GetCurrentBodyFacing() => desiredFacing.sqrMagnitude > 0.01f ? desiredFacing : restForward;

    /// <summary>Converts a world facing direction into the pelvis rotation that faces that way.</summary>
    private Quaternion FacingToRotation(Vector3 facing, out Vector3 flatFacing)
    {
        flatFacing = Vector3.ProjectOnPlane(facing, Vector3.up);
        if (flatFacing.sqrMagnitude < 0.001f) flatFacing = GetCurrentBodyFacing();
        flatFacing.Normalize();
        return Quaternion.AngleAxis(Vector3.SignedAngle(restForward, flatFacing, Vector3.up), Vector3.up) * balanceRestWorld;
    }

    // =====================================================================================
    //  MOVEMENT
    // =====================================================================================

    private void DriveMovement(Vector3 move)
    {
        bool support = IsGrounded && hasSupportSurface;
        Vector3 normal = support ? supportHit.normal.normalized : Vector3.up;
        Rigidbody supportBody = support ? supportHit.rigidbody : null;
        Vector3 platformVelocity = support ? GetSupportVelocityAtPoint(pelvisBody.position) : Vector3.zero;
        if (IsGrounded) { airborneInheritedVelocity = Vector3.zero; hasAirborneInheritedVelocity = false; }
        Vector3 referenceVelocity = IsGrounded ? platformVelocity
            : hasAirborneInheritedVelocity ? airborneInheritedVelocity : Vector3.zero;

        Vector3 platformAcceleration = Vector3.zero;
        if (supportBody != null)
        {
            if (supportVelocityReady && trackedSupportBody == supportBody)
                platformAcceleration = Vector3.ClampMagnitude(
                    (platformVelocity - trackedSupportVelocity) / Mathf.Max(0.001f, Time.fixedDeltaTime), 45f);
            trackedSupportBody = supportBody;
            trackedSupportVelocity = platformVelocity;
            supportVelocityReady = true;
        }
        else { trackedSupportBody = null; trackedSupportVelocity = Vector3.zero; supportVelocityReady = false; }

        Vector3 planarVelocity = Vector3.ProjectOnPlane(pelvisBody.linearVelocity - referenceVelocity, normal);
        float control = IsGrounded ? 1f : 0.28f;
        Vector3 direction = Vector3.ProjectOnPlane(move, normal).normalized;
        Vector3 error = direction * Mathf.Clamp01(move.magnitude) * ActiveMoveSpeed - planarVelocity;
        float maxAccel = movement.acceleration * (runInput ? movement.runAccelerationMultiplier : 1f);

        // Integral term cancels sustained drag; anti-windup prevents a launch after pushing a wall.
        if (!IsGrounded || move.sqrMagnitude < .001f || Vector3.Dot(previousMoveDirection, direction) < .5f)
            movementIntegral = Vector3.zero;
        previousMoveDirection = direction;
        Vector3 proposed = Vector3.ClampMagnitude(movementIntegral + error * Time.fixedDeltaTime * 12f, maxAccel * .65f);
        if ((error * 10f + proposed).magnitude < maxAccel) movementIntegral = proposed;

        Vector3 drive = Vector3.ClampMagnitude(error * 10f + movementIntegral, maxAccel) * control + platformAcceleration;
        // Drive the whole articulated mass so limbs are not dragged behind the pelvis.
        ApplyMovementAcceleration(pelvisBody, drive, normal);
        foreach (Muscle m in muscles) ApplyMovementAcceleration(m.body, drive, normal);
    }

    private static void ApplyMovementAcceleration(Rigidbody body, Vector3 acceleration, Vector3 normal)
    {
        if (body == null || body.isKinematic) return;
        Vector3 dragCompensation = Vector3.ProjectOnPlane(body.linearVelocity, normal) * body.linearDamping;
        body.AddForce(acceleration + dragCompensation, ForceMode.Acceleration);
    }

    private void DriveLegSupport()
    {
        if (!IsGrounded || !hasSupportSurface) return;
        Vector3 normal = supportHit.normal.normalized;
        float currentHeight = Vector3.Dot(pelvisBody.position - supportHit.point, normal);
        float targetHeight = standingPelvisHeight - gait.standingHeightReduction + bodyBobOffset;
        Vector3 surfaceVelocity = supportHit.rigidbody != null
            ? supportHit.rigidbody.GetPointVelocity(pelvisBody.position) : Vector3.zero;
        float normalVelocity = Vector3.Dot(pelvisBody.linearVelocity - surfaceVelocity, normal);

        // Supports the full ragdoll mass, then acts as a damped leg spring that also tracks the bob velocity.
        float supportedMass = dynamicPoseActive ? totalRagdollMass : pelvisBody.mass;
        float gravityComp = Mathf.Abs(Physics.gravity.y) * Mathf.Max(1f, supportedMass / Mathf.Max(0.1f, pelvisBody.mass));
        float accel = gravityComp + (targetHeight - currentHeight) * 135f + (bodyBobVelocity - normalVelocity) * 24f;
        pelvisBody.AddForce(normal * Mathf.Clamp(accel, 0f, 108f), ForceMode.Acceleration);
    }

    private void UpdateFacingFromMovement(Vector3 move)
    {
        bool aiming = weaponController != null && weaponController.IsAiming;
        Vector3 flatAim = aiming ? Vector3.ProjectOnPlane(weaponController.AimForward, Vector3.up) : Vector3.zero;
        if ((aiming && flatAim.sqrMagnitude > 0.001f) || (!aiming && move.sqrMagnitude >= 0.02f))
        {
            Quaternion previous = movementFacingWorld;
            if (aiming)
            {
                float maxTurn = movement.aimTurnSpeed * Time.fixedDeltaTime;
                float turn = Mathf.Clamp(Vector3.SignedAngle(GetCurrentBodyFacing(), flatAim.normalized, Vector3.up), -maxTurn, maxTurn);
                desiredFacing = Quaternion.AngleAxis(turn, Vector3.up) * GetCurrentBodyFacing();
            }
            else desiredFacing = move.normalized;
            movementFacingWorld = Quaternion.AngleAxis(Vector3.SignedAngle(restForward, desiredFacing, Vector3.up), Vector3.up) * balanceRestWorld;

            // Turn the upper-leg bodies by the same delta so they don't lag behind the pelvis.
            Quaternion delta = movementFacingWorld * Quaternion.Inverse(previous);
            float change = Quaternion.Angle(Quaternion.identity, delta);
            if (change > 0.01f)
                foreach (Foot f in feet)
                    if (f.thigh != null && bodyByVisualBone.TryGetValue(f.thigh, out Rigidbody b)) b.MoveRotation(delta * b.rotation);
            if (move.sqrMagnitude >= 0.02f && change >= gait.footReplantTurnAngle) ClearFootPlants();
        }

        pelvisBody.MoveRotation(movementFacingWorld);
        Vector3 angular = pelvisBody.angularVelocity;
        angular.y = 0f;
        pelvisBody.angularVelocity = angular;
    }

    // =====================================================================================
    //  GROUND DETECTION
    // =====================================================================================

    /// <summary>Closest valid walkable surface below origin (ignores this character).</summary>
    private bool CastSupport(Vector3 origin, float radius, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, hitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider == null || h.normal.y < 0.3f || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance >= closest) continue;
            closest = h.distance;
            best = h;
            found = true;
        }
        return found;
    }

    private void UpdateGrounded()
    {
        bool wasGrounded = IsGrounded && hasSupportSurface;
        Vector3 previousSupportVelocity = wasGrounded ? GetSupportVelocityAtPoint(pelvisBody.position) : Vector3.zero;
        IsGrounded = false;
        hasSupportSurface = false;
        if (jumpSupportCooldown > 0f) return;

        if (CastSupport(pelvisBody.position + Vector3.up * 0.1f, 0.16f, groundProbeDistance, out RaycastHit hit))
        {
            supportHit = hit;
            hasSupportSurface = true;
            float height = Vector3.Dot(pelvisBody.position - hit.point, hit.normal);
            float normalSpeed = Vector3.Dot(pelvisBody.linearVelocity, hit.normal);
            float groundedHeight = standingPelvisHeight - gait.standingHeightReduction + bodyBobOffset;
            IsGrounded = height <= groundedHeight + 0.16f && normalSpeed <= 1.5f;
        }
        if (!IsGrounded && wasGrounded)
        {
            airborneInheritedVelocity = previousSupportVelocity;
            hasAirborneInheritedVelocity = true;
        }
    }

    private Vector3 GetSupportVelocityAtPoint(Vector3 worldPoint)
    {
        if (!hasSupportSurface || supportHit.collider == null) return Vector3.zero;
        ClimbableSurface moving = supportHit.collider.GetComponentInParent<ClimbableSurface>();
        if (moving != null) return moving.GetVelocityAtPoint(worldPoint);
        return supportHit.rigidbody != null ? supportHit.rigidbody.GetPointVelocity(worldPoint) : Vector3.zero;
    }

    private void PreventFeetFromEnteringSupport()
    {
        bool leftContact = ProtectFootFromGround(LeftLeg, out RaycastHit leftHit);
        bool rightContact = ProtectFootFromGround(RightLeg, out RaycastHit rightHit);
        if (jumpSupportCooldown > 0f || (!leftContact && !rightContact)) return;

        // A foot contact is a more reliable landing signal than pelvis speed.
        if (!hasSupportSurface || !IsGrounded)
        {
            supportHit = leftContact ? leftHit : rightHit;
            hasSupportSurface = true;
            IsGrounded = true;
        }
    }

    private bool ProtectFootFromGround(Foot f, out RaycastHit ground)
    {
        ground = default;
        if (f.body == null || !CastSupport(f.body.position + Vector3.up * 0.36f, 0.025f, 0.78f, out ground)) return false;

        Vector3 normal = ground.normal.normalized;
        float required = FootClearance(f, normal);
        float current = Vector3.Dot(f.body.position - ground.point, normal);
        Vector3 surfaceVelocity = ground.rigidbody != null ? ground.rigidbody.GetPointVelocity(f.body.position) : Vector3.zero;
        float normalSpeed = Vector3.Dot(f.body.linearVelocity - surfaceVelocity, normal);
        float predicted = current + normalSpeed * Time.fixedDeltaTime;

        if (current < required)
        {
            f.body.position += normal * (required - current);
            current = required;
        }
        // Limit downward velocity before the next step can cross the contact plane.
        if (predicted < required)
        {
            float minSpeed = (required - current) / Mathf.Max(0.001f, Time.fixedDeltaTime);
            if (normalSpeed < minSpeed) f.body.linearVelocity += normal * (minSpeed - normalSpeed);
        }
        return current <= required + 0.035f || predicted <= required + 0.01f;
    }

    private static float FootClearance(Foot f, Vector3 surfaceNormal, float skin = 0.008f)
    {
        CapsuleCollider capsule = f.capsule;
        if (capsule == null) return 0.055f + skin;

        Vector3 s = capsule.transform.lossyScale;
        s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        int dir = capsule.direction;
        Vector3 localAxis = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
        float axisScale = dir == 0 ? s.x : dir == 1 ? s.y : s.z;
        float radiusScale = dir == 0 ? Mathf.Max(s.y, s.z) : dir == 1 ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, s.y);
        float radius = capsule.radius * radiusScale;
        float halfSegment = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
        Vector3 worldAxis = capsule.transform.TransformDirection(localAxis).normalized;
        Vector3 centerOffset = capsule.transform.TransformVector(capsule.center);
        float lowest = Vector3.Dot(centerOffset, surfaceNormal) - Mathf.Abs(Vector3.Dot(worldAxis, surfaceNormal)) * halfSegment - radius;
        return Mathf.Max(0.01f, -lowest + skin);
    }

    // =====================================================================================
    //  PROCEDURAL WALK
    // =====================================================================================

    private float GaitTravelSpeed
    {
        get
        {
            if (pelvisBody == null) return 0f;
            Vector3 surfaceVelocity = hasSupportSurface && supportHit.rigidbody != null
                ? supportHit.rigidbody.GetPointVelocity(pelvisBody.position) : Vector3.zero;
            return Vector3.ProjectOnPlane(pelvisBody.linearVelocity - surfaceVelocity,
                hasSupportSurface ? supportHit.normal : Vector3.up).magnitude;
        }
    }

    private float CalculateActiveStride()
    {
        float strideMul = gait.strideScale * Mathf.Lerp(1f, gait.runStrideMultiplier, runBlend);
        float speedBlend = Mathf.InverseLerp(2f, Mathf.Max(2.1f, gait.fastStrideSpeed), GaitTravelSpeed);
        float reach = Mathf.Min(LeftLeg.thighLength + LeftLeg.shinLength, RightLeg.thighLength + RightLeg.shinLength) * 1.3f;
        if (reach < .3f) reach = 1.05f;
        return Mathf.Clamp(standingPelvisHeight * strideMul * Mathf.Lerp(1f, gait.fastStrideMultiplier, speedBlend),
            .3f, Mathf.Min(1.4f, reach));
    }

    /// <summary>Distance the planted foot travels backward during stance. Matches body speed so feet never skate.</summary>
    private float StanceTravel => 2f * CalculateActiveStride() * (1f - swingFraction);

    private float Cycle01(Foot f) => Mathf.Repeat(gaitPhase / TwoPi + (f.isLeft ? 0f : 0.5f), 1f);

    private void UpdateProceduralPose(Vector3 move)
    {
        float dt = Time.fixedDeltaTime;
        if (!IsGrounded)
        {
            // Keep the last coherent leg pose in the air to avoid a takeoff twitch.
            float previous = bodyBobOffset;
            bodyBobOffset = Mathf.MoveTowards(bodyBobOffset, 0f, dt * 0.3f);
            bodyBobVelocity = (bodyBobOffset - previous) / Mathf.Max(0.001f, dt);
            return;
        }

        // Input drives the gait immediately; velocity keeps it going while sliding to a stop.
        float input01 = Mathf.Clamp01(move.magnitude);
        float velocity01 = Mathf.Clamp01(Vector3.ProjectOnPlane(pelvisBody.linearVelocity, Vector3.up).magnitude / Mathf.Max(0.1f, ActiveMoveSpeed));
        float target01 = Mathf.Max(input01, velocity01 * 0.65f);
        bool moving = target01 > 0.02f;

        // Smoothed blends: legs/arms ease in and out, and walk<->run changes gradually.
        gaitAmount = Mathf.MoveTowards(gaitAmount, moving ? target01 : 0f, dt * gait.gaitBlendRate);
        runBlend = Mathf.MoveTowards(runBlend, runInput ? 1f : 0f, dt * 4f);
        swingFraction = Mathf.Lerp(gait.walkSwingFraction, gait.runSwingFraction, runBlend);

        if (moving)
        {
            float cyclesPerSecond = gait.matchGaitToMovementSpeed
                ? Mathf.Max(0.2f, GaitTravelSpeed / Mathf.Max(0.2f, CalculateActiveStride() * 2f) * gait.gaitSpeedMatch)
                : gait.manualCyclesPerSecond * Mathf.Lerp(1f, gait.runCadenceMultiplier, runBlend) * Mathf.Lerp(0.75f, 1.2f, target01);
            // Keep at least three physics samples per half-step.
            currentGaitFrequency = Mathf.Min(cyclesPerSecond, 1f / (6f * dt));
            gaitPhase = Mathf.Repeat(gaitPhase + dt * currentGaitFrequency * TwoPi, TwoPi);
        }

        // Body bob: low at every foot lift-off, highest mid-stance.
        float halfStep = Mathf.Repeat(gaitPhase / Mathf.PI, 1f);
        float stanceWave = Mathf.SmoothStep(0f, 1f, Mathf.Sin(halfStep * Mathf.PI));
        float cadenceScale = 1f / Mathf.Max(1f, currentGaitFrequency * currentGaitFrequency / 4f);
        float bobScale = Mathf.Lerp(1f, gait.runBobMultiplier, runBlend);
        float previousBob = bodyBobOffset;
        bodyBobOffset = moving
            ? stanceWave * gait.bodyBobHeight * bobScale * target01 * cadenceScale
            : Mathf.MoveTowards(bodyBobOffset, 0f, dt * 0.22f);
        bodyBobVelocity = (bodyBobOffset - previousBob) / Mathf.Max(0.001f, dt);

        // Legs
        float legScale = gaitAmount * Mathf.Lerp(1f, gait.runStrideMultiplier, runBlend);
        foreach (Foot f in feet) ApplyLegPose(f, legScale);

        // Arms swing opposite to the legs; elbows bend more when running.
        float wave = Mathf.Sin(gaitPhase);
        float wobble = Mathf.Sin(Time.time * 3.1f) * gait.idleWobble + Mathf.Sin(Time.time * 5.7f) * gait.idleWobble * 0.35f;
        float swing = gait.armSwing * Mathf.Lerp(1f, gait.runArmSwingMultiplier, runBlend) * gaitAmount;
        float elbow = 8f + runBlend * 22f * gaitAmount + Mathf.Abs(wave) * 14f * gaitAmount;
        if (!ArmsReserved || !SupportHandReserved)
        {
            SetPose(leftArm, new Vector3(-wave * swing, 0f, wobble));
            SetPose(leftForearm, new Vector3(0f, 0f, elbow));
        }
        else ZeroPose(leftArm, leftForearm, leftHand);
        if (!ArmsReserved)
        {
            SetPose(rightArm, new Vector3(wave * swing, 0f, -wobble));
            SetPose(rightForearm, new Vector3(0f, 0f, -elbow));
        }
        else ZeroPose(rightArm, rightForearm, rightHand);
    }

    private void ApplyLegPose(Foot f, float scale)
    {
        float c = Cycle01(f);
        float thighAngle, kneeAngle;
        if (c < swingFraction)
        {
            // Swing: leg travels back -> front with a lifted knee.
            float p = c / swingFraction;
            thighAngle = Mathf.Lerp(-0.5f, 0.6f, Mathf.SmoothStep(0f, 1f, p)) * gait.legSwing;
            kneeAngle = Mathf.Sin(p * Mathf.PI) * gait.kneeBend;
        }
        else
        {
            // Stance: nearly straight leg travels front -> back.
            float p = (c - swingFraction) / Mathf.Max(0.01f, 1f - swingFraction);
            thighAngle = Mathf.Lerp(0.6f, -0.5f, p) * gait.legSwing;
            kneeAngle = Mathf.Sin(p * Mathf.PI) * 7f;
        }
        SetPose(f.thigh, new Vector3(thighAngle * scale, 0f, 0f));
        SetPose(f.shin, new Vector3(kneeAngle * scale, 0f, 0f));
        SetPose(f.foot, new Vector3(-kneeAngle * 0.4f * scale, 0f, 0f));
    }

    private void SetPose(Transform bone, Vector3 euler)
    {
        if (bone != null && muscleByBone.TryGetValue(bone, out Muscle m)) m.pose = euler;
    }

    private void ZeroPose(params Transform[] bonesToZero)
    {
        foreach (Transform b in bonesToZero) SetPose(b, Vector3.zero);
    }

    private void DriveMuscles()
    {
        // Impacts loosen the pose briefly but never turn the controller into a limp ragdoll.
        float flopScale = flopTimer > 0f ? 0.72f : 1f;
        foreach (Muscle m in muscles)
        {
            if (m.body == null) continue;
            // Target the upright reference, not the tilted pelvis, or limbs just follow the fall.
            Quaternion desired = movementFacingWorld * m.restRelativeToPelvis * Quaternion.Euler(m.pose);
            float strength = Mathf.Max(m.strength * 6.5f, 240f) * flopScale;
            float damping = Mathf.Max(ragdoll.muscleDamping * (m.isLeg ? ragdoll.legDampingMultiplier : 3.4f), 34f);
            ApplyPdTorque(m.body, desired, strength, damping);
        }
    }

    private static void ApplyPdTorque(Rigidbody body, Quaternion target, float strength, float damping)
    {
        Quaternion delta = target * Quaternion.Inverse(body.rotation);
        if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (float.IsNaN(axis.x)) return;
        if (angle > 180f) angle -= 360f;
        Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * strength) - body.angularVelocity * damping;
        body.AddTorque(Vector3.ClampMagnitude(torque, strength * 4f), ForceMode.Acceleration);
    }

    // ---------------------------------- Physical foot stepping ----------------------------------

    private void ClearFootPlants()
    {
        foreach (Foot f in feet) { f.hasPlant = false; f.targetValid = false; }
        standingFeetReady = false;
        turningFoot = -1;
    }

    private void DrivePhysicalSteps(Vector3 move)
    {
        if (!hasSupportSurface) { ClearFootPlants(); return; }
        if (move.sqrMagnitude < 0.02f) { DriveStandingFeet(); return; }
        if (standingFeetReady)
        {
            // Restart the swing from the current turn-step target, not a stale stride offset.
            foreach (Foot f in feet) f.wasSwinging = false;
        }
        standingFeetReady = false;
        turningFoot = -1;
        foreach (Foot f in feet) ApplyFootStep(f, move.normalized);
    }

    private void ApplyFootStep(Foot f, Vector3 movementForward)
    {
        if (f.body == null) { f.targetValid = false; return; }

        float c = Cycle01(f);
        bool swinging = c < swingFraction;
        float swingProgress = swinging ? c / swingFraction : 0f;
        float stanceProgress = swinging ? 0f : (c - swingFraction) / Mathf.Max(0.01f, 1f - swingFraction);
        float liftWave = swinging ? Mathf.Sin(swingProgress * Mathf.PI) : 0f;
        float lift = liftWave * liftWave; // zero vertical speed at lift-off and touchdown

        Vector3 normal = supportHit.normal.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(movementForward, normal).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(GetCurrentBodyFacing(), normal).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(restForward, normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward).normalized;
        Transform surface = supportHit.collider.transform;
        float clearance = FootClearance(f, normal);
        // Pelvis projected on the support plane is a stable gait origin (hit points jump between triangle edges).
        Vector3 groundCenter = pelvisBody.position - normal * Vector3.Dot(pelvisBody.position - supportHit.point, normal);

        float travel = StanceTravel;
        Vector3 landingOffset = right * f.sideOffset + forward * (travel * 0.65f);
        Vector3 landing = groundCenter + landingOffset + normal * clearance;
        Vector3 previousTarget = f.target;
        bool hadTarget = f.targetValid;

        if (swinging && (!f.wasSwinging || !f.targetValid || f.plantSurface != surface))
        {
            Vector3 start = f.targetValid ? f.target : f.body.position;
            Vector3 offset = Vector3.ProjectOnPlane(start - groundCenter, normal);
            f.swingStart = right * f.sideOffset +
                           forward * Mathf.Clamp(Vector3.Dot(offset, forward), -travel * 0.65f, travel * 0.5f);
        }

        if (!swinging && (f.wasSwinging || !f.hasPlant || f.plantSurface != surface))
        {
            // Plant the planned landing (not the delayed physical proxy).
            f.plantSurface = surface;
            f.plantLocal = surface.InverseTransformPoint(landing - forward * (travel * stanceProgress));
            f.hasPlant = true;
            if (f.wasSwinging) FootPlanted?.Invoke(f.isLeft);
        }

        Vector3 target;
        if (swinging)
        {
            float liftHeight = Mathf.Clamp(standingPelvisHeight * gait.stepLiftScale, 0.08f, 0.4f);
            float p = Mathf.SmoothStep(0f, 1f, swingProgress);
            target = groundCenter + Vector3.Lerp(f.swingStart, landingOffset, p) + normal * (clearance + lift * liftHeight);
        }
        else target = f.plantSurface != null ? f.plantSurface.TransformPoint(f.plantLocal) : f.body.position;

        // Never let the target sink below the contact plane.
        float height = Vector3.Dot(target - supportHit.point, normal);
        if (height < clearance) target += normal * (clearance - height);
        f.target = target;
        f.targetValid = true;

        Vector3 surfaceVelocity = supportHit.rigidbody != null ? supportHit.rigidbody.GetPointVelocity(target) : Vector3.zero;
        Vector3 targetVelocity = swinging && hadTarget ? (target - previousTarget) / Time.fixedDeltaTime : surfaceVelocity;
        float response = Mathf.Clamp(currentGaitFrequency / 2f * Mathf.Sqrt(gait.footTargetSharpness / 12f), 1f, 2f);
        targetVelocity = Vector3.ClampMagnitude(targetVelocity, Mathf.Max(4f, GaitTravelSpeed * 3f));
        Vector3 accel = (target - f.body.position) * gait.footPositionStrength * response * response +
                        (targetVelocity - f.body.linearVelocity) * gait.footVelocityDamping * response;
        f.body.AddForce(Vector3.ClampMagnitude(accel, gait.footMaxAcceleration * response * response), ForceMode.Acceleration);
        f.wasSwinging = swinging;
    }

    private void DriveStandingFeet()
    {
        Foot l = LeftLeg, r = RightLeg;
        if (l.body == null || r.body == null) return;

        Transform surface = supportHit.collider.transform;
        Vector3 normal = supportHit.normal.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(GetCurrentBodyFacing(), normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward).normalized;
        Vector3 center = pelvisBody.position - normal * Vector3.Dot(pelvisBody.position - supportHit.point, normal);
        Vector3 leftRest = center + right * l.sideOffset + normal * FootClearance(l, normal);
        Vector3 rightRest = center + right * r.sideOffset + normal * FootClearance(r, normal);

        if (!standingFeetReady || l.plantSurface != surface || r.plantSurface != surface)
        {
            l.plantLocal = surface.InverseTransformPoint(l.targetValid ? l.target : l.body.position);
            r.plantLocal = surface.InverseTransformPoint(r.targetValid ? r.target : r.body.position);
            l.plantSurface = r.plantSurface = surface;
            standingFeetReady = true;
            turningFoot = -1;
        }

        Vector3 leftTarget = surface.TransformPoint(l.plantLocal);
        Vector3 rightTarget = surface.TransformPoint(r.plantLocal);

        if (turningFoot < 0)
        {
            bool leftNeeds = NeedsStep(leftTarget, leftRest, normal);
            bool rightNeeds = NeedsStep(rightTarget, rightRest, normal);
            if (leftNeeds || rightNeeds)
            {
                turningFoot = leftNeeds && rightNeeds ? 1 - lastTurningFoot : leftNeeds ? 0 : 1;
                turnStepProgress = 0f;
                turnStepStartLocal = (turningFoot == 0 ? l : r).plantLocal;
                turnStepEndLocal = surface.InverseTransformPoint(turningFoot == 0 ? leftRest : rightRest);
            }
        }

        l.wasSwinging = turningFoot == 0;
        r.wasSwinging = turningFoot == 1;
        if (turningFoot >= 0)
        {
            turnStepProgress = Mathf.Min(1f, turnStepProgress + Time.fixedDeltaTime / Mathf.Max(.1f, gait.turnStepDuration));
            Vector3 stepTarget = surface.TransformPoint(Vector3.Lerp(turnStepStartLocal, turnStepEndLocal,
                                     Mathf.SmoothStep(0f, 1f, turnStepProgress))) +
                                 normal * (Mathf.Sin(turnStepProgress * Mathf.PI) * gait.turnStepLift);
            Foot stepping = turningFoot == 0 ? l : r;
            if (turningFoot == 0) leftTarget = stepTarget; else rightTarget = stepTarget;
            if (turnStepProgress >= 1f)
            {
                stepping.plantLocal = turnStepEndLocal;
                lastTurningFoot = turningFoot;
                turningFoot = -1;
                FootPlanted?.Invoke(stepping.isLeft);
            }
        }

        // Only the lifted foot moves; the other stays anchored in the support's local space.
        TrackStandingFoot(l, leftTarget, normal);
        TrackStandingFoot(r, rightTarget, normal);
        l.hasPlant = !l.wasSwinging;
        r.hasPlant = !r.wasSwinging;
    }

    private bool NeedsStep(Vector3 target, Vector3 rest, Vector3 normal) =>
        Vector3.ProjectOnPlane(rest - target, normal).magnitude > gait.turnStepDistance ||
        Vector3.Dot(target - rest, normal) > .025f;

    private void TrackStandingFoot(Foot f, Vector3 target, Vector3 normal)
    {
        float clearance = FootClearance(f, normal);
        float height = Vector3.Dot(target - supportHit.point, normal);
        if (height < clearance) target += normal * (clearance - height);
        Vector3 surfaceVelocity = supportHit.rigidbody != null ? supportHit.rigidbody.GetPointVelocity(target) : Vector3.zero;
        Vector3 velocity = f.wasSwinging && f.targetValid ? (target - f.target) / Time.fixedDeltaTime : surfaceVelocity;
        Vector3 force = (target - f.body.position) * gait.footPositionStrength +
                        (Vector3.ClampMagnitude(velocity, 4f) - f.body.linearVelocity) * gait.footVelocityDamping;
        f.body.AddForce(Vector3.ClampMagnitude(force, gait.footMaxAcceleration), ForceMode.Acceleration);
        f.target = target;
        f.targetValid = true;
    }

    // ---------------------------------- Visual leg IK ----------------------------------

    private void CacheLegIk()
    {
        foreach (Foot f in feet) CreateLegIk(f);
    }

    private void CreateLegIk(Foot f)
    {
        f.ikValid = false;
        if (f.thigh == null || f.shin == null || f.foot == null || pelvis == null) return;

        Vector3 upper = f.shin.position - f.thigh.position;
        Vector3 lower = f.foot.position - f.shin.position;
        if (upper.sqrMagnitude < 1e-6f || lower.sqrMagnitude < 1e-6f) return;

        Vector3 fullLeg = f.foot.position - f.thigh.position;
        Vector3 line = fullLeg.sqrMagnitude > 1e-6f ? fullLeg.normalized : Vector3.down;
        Vector3 kneeOffset = upper - line * Vector3.Dot(upper, line);
        if (kneeOffset.sqrMagnitude < 1e-6f) kneeOffset = Vector3.ProjectOnPlane(restForward, line);
        if (kneeOffset.sqrMagnitude < 1e-6f) kneeOffset = Vector3.ProjectOnPlane(Vector3.forward, line);
        kneeOffset.Normalize();

        f.ikValid = true;
        f.thighAxis = f.thigh.InverseTransformDirection(upper.normalized);
        f.shinAxis = f.shin.InverseTransformDirection(lower.normalized);
        f.thighRef = f.thigh.InverseTransformDirection(kneeOffset);
        f.shinRef = f.shin.InverseTransformDirection(kneeOffset);
        f.kneePole = Quaternion.Inverse(pelvis.rotation) * kneeOffset;
        f.thighLength = upper.magnitude;
        f.shinLength = lower.magnitude;
        f.footRest = Quaternion.Inverse(f.shin.rotation) * f.foot.rotation;
    }

    private void SolveVisualLegIk(Foot f)
    {
        if (!f.ikValid || f.thigh == null || f.shin == null || f.foot == null || f.body == null) return;

        Vector3 root = f.thigh.position;
        Vector3 desired = f.targetValid ? GetRenderedFootTarget(f) : f.body.transform.position;
        Vector3 toTarget = desired - root;
        float rawDistance = toTarget.magnitude;
        if (rawDistance < 0.0001f) return;

        Vector3 direction = toTarget / rawDistance;
        float distance = Mathf.Clamp(rawDistance, Mathf.Abs(f.thighLength - f.shinLength) + 0.001f, f.thighLength + f.shinLength - 0.001f);
        Vector3 target = root + direction * distance;

        Vector3 pole = Vector3.ProjectOnPlane(pelvis.rotation * f.kneePole, direction).normalized;
        if (pole.sqrMagnitude < 0.001f) pole = Vector3.ProjectOnPlane(desiredFacing, direction).normalized;
        if (pole.sqrMagnitude < 0.001f) pole = Vector3.Cross(direction, Vector3.right).normalized;

        float along = (f.thighLength * f.thighLength - f.shinLength * f.shinLength + distance * distance) / (2f * distance);
        float kneeHeight = Mathf.Sqrt(Mathf.Max(0f, f.thighLength * f.thighLength - along * along));
        Vector3 knee = root + direction * along + pole * kneeHeight;

        AlignBoneFrame(f.thigh, f.thighAxis, f.thighRef, knee - root, pole);
        AlignBoneFrame(f.shin, f.shinAxis, f.shinRef, target - f.shin.position, pole);
        // Keep the authored ankle angle instead of copying foot roll from the ragdoll body.
        f.foot.rotation = f.shin.rotation * f.footRest;
    }

    private Vector3 GetRenderedFootTarget(Foot f)
    {
        // Script-driven physics has no fixed/render interpolation clock.
        if (!f.renderPrevValid || Physics.simulationMode != SimulationMode.FixedUpdate) return f.target;
        float fraction = Mathf.Clamp01((Time.time - Time.fixedTime) / Mathf.Max(.001f, Time.fixedDeltaTime));
        return Vector3.Lerp(f.renderPrev, f.target, fraction);
    }

    private static void AlignBoneFrame(Transform bone, Vector3 localAxis, Vector3 localReference,
        Vector3 worldDirection, Vector3 worldReference)
    {
        if (bone == null || worldDirection.sqrMagnitude < 1e-6f) return;
        Vector3 targetDirection = worldDirection.normalized;
        Vector3 currentDirection = bone.TransformDirection(localAxis).normalized;
        Quaternion aligned = Quaternion.FromToRotation(currentDirection, targetDirection) * bone.rotation;

        // A direction leaves one free roll axis; align a second reference axis to the knee plane.
        Vector3 alignedRef = Vector3.ProjectOnPlane(aligned * localReference, targetDirection).normalized;
        Vector3 targetRef = Vector3.ProjectOnPlane(worldReference, targetDirection).normalized;
        if (alignedRef.sqrMagnitude > 0.001f && targetRef.sqrMagnitude > 0.001f)
            aligned = Quaternion.FromToRotation(alignedRef, targetRef) * aligned;
        bone.rotation = aligned;
    }

    // =====================================================================================
    //  VEHICLE SEATS
    // =====================================================================================

    private static Vector3 SeatLocalOffset(SeatPose s) => new Vector3(s.sideOffset, s.heightOffset, -s.distanceFromWheel);

    /// <summary>World position of the pelvis while seated (seat anchor + distance/height/side offsets).</summary>
    private Vector3 GetSeatPosition() => vehicleSeatAnchor.position + vehicleSeatAnchor.rotation * seatOffsetCurrent;

    private Quaternion GetVehicleSeatRotation() =>
        vehicleSeatAnchor != null ? vehicleSeatAnchor.rotation * vehicleSeatRotationOffset : movementFacingWorld;

    private Quaternion GetUprightVehicleSeatRotation()
    {
        Vector3 seatForward = vehicleSeatAnchor != null ? Vector3.ProjectOnPlane(vehicleSeatAnchor.forward, Vector3.up) : restForward;
        if (seatForward.sqrMagnitude < 0.01f) seatForward = restForward;
        return Quaternion.AngleAxis(Vector3.SignedAngle(restForward, seatForward.normalized, Vector3.up), Vector3.up) * balanceRestWorld;
    }

    public bool EnterVehicleSeat(Transform seatAnchor, Rigidbody vehicleBody)
    {
        if (seatAnchor == null || pelvisBody == null || IsVehicleSeated) return false;
        if (weaponController == null) weaponController = GetComponent<WeaponController>();

        animatorWasEnabledBeforeSeat = movementAnimator != null && movementAnimator.enabled;
        if (movementAnimator != null) movementAnimator.enabled = false;
        if (weaponController != null)
        {
            weaponWasEnabledBeforeSeat = weaponController.enabled;
            weaponController.PrepareForVehicleSeat();
            weaponController.enabled = false;
        }
        ResetArms(false);
        SynchronizePhysicsLimbsToVisuals();
        // A seated character is carried by the seat anchor. Kinematic limbs prevent vibration over suspension.
        ConfigureLimbSimulation(false);

        vehicleSeatAnchor = seatAnchor;
        seatedVehicleBody = vehicleBody;
        vehicleDrivingPose = false;
        seatOffsetCurrent = SeatLocalOffset(passengerSeat);
        vehicleSeatRotationOffset = Quaternion.Inverse(seatAnchor.rotation) * GetUprightVehicleSeatRotation();
        Quaternion rotation = GetVehicleSeatRotation();

        pelvisInterpolationBeforeSeat = pelvisBody.interpolation;
        pelvisBody.linearVelocity = Vector3.zero;
        pelvisBody.angularVelocity = Vector3.zero;
        pelvisBody.useGravity = false;
        pelvisBody.detectCollisions = false;
        pelvisBody.interpolation = RigidbodyInterpolation.None;
        pelvisBody.isKinematic = true;
        pelvisBody.position = GetSeatPosition();
        pelvisBody.rotation = rotation;

        ClearFootPlants();
        bodyBobOffset = bodyBobVelocity = 0f;
        IsGrounded = false;
        hasSupportSurface = false;
        Physics.SyncTransforms(); // otherwise the first steering-hand IK pass sees pre-seat transforms
        return true;
    }

    public void SetVehicleDrivingPose(bool driving)
    {
        if (IsVehicleSeated) vehicleDrivingPose = driving;
    }

    public bool SwitchVehicleSeat(Transform newSeatAnchor, Rigidbody vehicleBody)
    {
        if (!IsVehicleSeated || newSeatAnchor == null || pelvisBody == null) return false;

        vehicleSeatAnchor = newSeatAnchor;
        seatedVehicleBody = vehicleBody;
        vehicleDrivingPose = false;
        seatOffsetCurrent = SeatLocalOffset(passengerSeat);
        vehicleSeatRotationOffset = Quaternion.Inverse(newSeatAnchor.rotation) * GetUprightVehicleSeatRotation();
        Quaternion rotation = GetVehicleSeatRotation();
        Vector3 position = GetSeatPosition();

        pelvisBody.position = position;
        pelvisBody.rotation = rotation;
        pelvisBody.transform.SetPositionAndRotation(position, rotation);
        desiredFacing = Vector3.ProjectOnPlane(newSeatAnchor.forward, Vector3.up).normalized;
        movementFacingWorld = rotation;
        Physics.SyncTransforms();
        return true;
    }

    public void ExitVehicleSeat(Vector3 standPosition, Quaternion standRotation) =>
        ExitVehicleSeat(standPosition, standRotation, Vector3.zero);

    public void ExitVehicleSeat(Vector3 standPosition, Quaternion standRotation, Vector3 additionalVelocity)
    {
        if (!IsVehicleSeated || pelvisBody == null) return;

        Quaternion rotation = FacingToRotation(standRotation * Vector3.forward, out Vector3 standForward);
        Vector3 inherited = (seatedVehicleBody != null ? seatedVehicleBody.GetPointVelocity(standPosition) : Vector3.zero) + additionalVelocity;

        // Move the still-kinematic core before releasing it, so interpolation never shows the old seat pose.
        pelvisBody.position = standPosition;
        pelvisBody.rotation = rotation;
        pelvisBody.transform.SetPositionAndRotation(standPosition, rotation);
        pelvisBody.useGravity = true;
        pelvisBody.detectCollisions = true;
        pelvisBody.interpolation = pelvisInterpolationBeforeSeat;
        pelvisBody.isKinematic = false;
        pelvisBody.linearVelocity = inherited;
        pelvisBody.angularVelocity = Vector3.zero;
        airborneInheritedVelocity = inherited;
        hasAirborneInheritedVelocity = inherited.sqrMagnitude > 0.0001f;
        movementFacingWorld = rotation;
        desiredFacing = standForward;

        vehicleSeatAnchor = null;
        seatedVehicleBody = null;
        vehicleDrivingPose = false;
        ResetProceduralPoseOffsets();
        // Put the visual skeleton at the standing pose first so the limb proxies sync to the right place.
        ApplyKinematicVisualPose(standPosition, rotation);

        if (dynamicPoseActive)
        {
            SynchronizePhysicsLimbsToVisuals();
            ConfigureLimbSimulation(true);
            foreach (Muscle m in muscles)
            {
                if (m.body == null) continue;
                m.body.linearVelocity = inherited;
                m.body.angularVelocity = Vector3.zero;
            }
        }
        else RestoreAnimatedPose(animatorWasEnabledBeforeSeat);

        jumpSupportCooldown = 0f; // leaving a seat is not a jump: ground support must work on the first FixedUpdate
        if (weaponController != null) weaponController.enabled = weaponWasEnabledBeforeSeat;
        Physics.SyncTransforms();
    }

    private void UpdateVehicleSeatPose()
    {
        SeatPose seat = vehicleDrivingPose ? driverSeat : passengerSeat;
        // Glide to the active seat offset so changing Distance From Wheel (or starting to drive) never pops.
        seatOffsetCurrent = Vector3.MoveTowards(seatOffsetCurrent, SeatLocalOffset(seat), seatAdjustSpeed * Time.fixedDeltaTime);

        Vector3 seatForward = Vector3.ProjectOnPlane(vehicleSeatAnchor.forward, Vector3.up);
        if (seatForward.sqrMagnitude < 0.01f) seatForward = restForward;
        desiredFacing = seatForward.normalized;
        movementFacingWorld = GetVehicleSeatRotation();

        Vector3 position = GetSeatPosition();
        pelvisBody.position = position;
        pelvisBody.rotation = movementFacingWorld;
        pelvisBody.transform.SetPositionAndRotation(position, movementFacingWorld);

        SetPose(LeftLeg.thigh, seat.thighEuler);
        SetPose(RightLeg.thigh, seat.thighEuler);
        SetPose(LeftLeg.shin, seat.shinEuler);
        SetPose(RightLeg.shin, seat.shinEuler);
        SetPose(LeftLeg.foot, seat.footEuler);
        SetPose(RightLeg.foot, seat.footEuler);

        Vector3 a = seat.armEuler;
        SetPose(leftArm, new Vector3(a.x, -a.y, a.z));
        SetPose(rightArm, new Vector3(a.x, a.y, -a.z));
        SetPose(leftForearm, new Vector3(0f, 0f, seat.forearmBend));
        SetPose(rightForearm, new Vector3(0f, 0f, -seat.forearmBend));
        SetPose(leftHand, Vector3.zero);
        SetPose(rightHand, Vector3.zero);
    }

    // =====================================================================================
    //  ARMS
    // =====================================================================================

    /// <summary>Clears arm reservations/poses and snaps the arms to their body-relative rest pose.</summary>
    private void ResetArms(bool syncBodies)
    {
        ArmsReserved = SupportHandReserved = false;
        foreach (Muscle m in muscles)
        {
            if (m.isLeg) continue;
            m.pose = Vector3.zero;
            if (m.visualBone == null) continue;
            Quaternion rest = movementFacingWorld * m.restRelativeToPelvis;
            m.visualBone.rotation = rest * m.visualRotationOffset;
            if (syncBodies && m.body != null && !m.body.isKinematic)
            {
                m.body.rotation = rest;
                m.body.angularVelocity = Vector3.zero;
            }
        }
    }

    public void ResetUpperBodyAfterGrip()
    {
        ResetArms(true);
        if (!dynamicPoseActive && movementAnimator != null && movementAnimator.enabled) movementAnimator.Update(0f);
    }

    // =====================================================================================
    //  EXTERNAL (CLIMB) CONTROL
    // =====================================================================================

    public bool BeginExternalClimbControl()
    {
        if (pelvisBody == null || IsVehicleSeated || externalMotionControl) return false;
        externalMotionControl = true;
        externalRenderPoseValid = false;
        animatorWasEnabledBeforeExternalControl = movementAnimator != null && movementAnimator.enabled;
        externalClimbAnimatorActive = BeginExternalClimbAnimation();
        if (!externalClimbAnimatorActive && movementAnimator != null) movementAnimator.enabled = false;
        if (weaponController == null) weaponController = GetComponent<WeaponController>();
        if (weaponController != null) weaponController.PrepareForVehicleSeat();

        ResetProceduralPoseOffsets();
        ConfigureLimbSimulation(false);
        pelvisInterpolationBeforeExternalControl = pelvisBody.interpolation;
        pelvisBody.interpolation = RigidbodyInterpolation.None;
        pelvisBody.linearVelocity = Vector3.zero;
        pelvisBody.angularVelocity = Vector3.zero;
        pelvisBody.useGravity = false;
        pelvisBody.detectCollisions = false;
        pelvisBody.isKinematic = true;
        IsGrounded = false;
        hasSupportSurface = false;
        Physics.SyncTransforms();
        return true;
    }

    private bool BeginExternalClimbAnimation()
    {
        CacheMovementAnimator();
        if (movementAnimator == null || movementAnimator.runtimeAnimatorController == null) return false;
        externalClimbLayer = movementAnimator.GetLayerIndex("Traversal");
        if (externalClimbLayer < 0) return false;

        movementAnimator.enabled = true;
        movementAnimator.SetLayerWeight(externalClimbLayer, 1f);
        externalClimbState = TraversalHoldState;
        movementAnimator.Play(TraversalHoldState, externalClimbLayer, 0f);
        movementAnimator.Update(0f);
        return true;
    }

    public void SetExternalClimbAnimation(bool moving, bool topOut)
    {
        if (!externalMotionControl || !externalClimbAnimatorActive || movementAnimator == null) return;
        int requested = topOut ? TraversalTopOutState : moving ? TraversalMoveState : TraversalHoldState;
        if (externalClimbState == requested) return;
        externalClimbState = requested;
        if (topOut)
        {
            // Start the short top-out clip on its first frame; a crossfade would eat part of it.
            movementAnimator.Play(requested, externalClimbLayer, 0f);
            movementAnimator.Update(0f);
        }
        else movementAnimator.CrossFade(requested, 0.12f, externalClimbLayer, 0f);
    }

    private void EndExternalClimbAnimation()
    {
        if (movementAnimator != null && externalClimbLayer >= 0) movementAnimator.SetLayerWeight(externalClimbLayer, 0f);
        externalClimbAnimatorActive = false;
        externalClimbLayer = -1;
        externalClimbState = 0;
    }

    public void SetExternalClimbPose(Vector3 pelvisPosition, Vector3 facing)
    {
        if (!externalMotionControl || pelvisBody == null) return;
        Quaternion rotation = FacingToRotation(facing, out Vector3 flat);
        pelvisBody.position = pelvisPosition;
        pelvisBody.rotation = rotation;
        pelvisBody.transform.SetPositionAndRotation(pelvisPosition, rotation);
        desiredFacing = flat;
        movementFacingWorld = rotation;
    }

    public void SetExternalClimbRenderPose(Vector3 pelvisPosition, Vector3 facing)
    {
        if (!externalMotionControl || pelvisBody == null) return;
        externalRenderPelvisPosition = pelvisPosition;
        externalRenderPoseValid = true;
        Quaternion rotation = FacingToRotation(facing, out _);
        if (externalClimbAnimatorActive) AnchorAnimatedVisualAt(pelvisPosition, rotation);
        else ApplyKinematicVisualPose(pelvisPosition, rotation);
    }

    public void FinishExternalClimbControl(Vector3 pelvisPosition, Vector3 facing, Vector3 inheritedVelocity)
    {
        if (!externalMotionControl || pelvisBody == null) return;
        SetExternalClimbPose(pelvisPosition, facing);
        externalMotionControl = false;
        externalRenderPoseValid = false;
        EndExternalClimbAnimation();

        ResetProceduralPoseOffsets();
        ApplyKinematicVisualPose(pelvisBody.transform.position, movementFacingWorld);
        SynchronizePhysicsLimbsToVisuals();

        pelvisBody.isKinematic = false;
        pelvisBody.useGravity = true;
        pelvisBody.detectCollisions = true;
        pelvisBody.interpolation = pelvisInterpolationBeforeExternalControl;
        pelvisBody.linearVelocity = inheritedVelocity;
        pelvisBody.angularVelocity = Vector3.zero;
        airborneInheritedVelocity = inheritedVelocity;
        hasAirborneInheritedVelocity = inheritedVelocity.sqrMagnitude > 0.0001f;

        if (dynamicPoseActive)
        {
            if (movementAnimator != null) movementAnimator.enabled = false;
            ConfigureLimbSimulation(true);
            foreach (Muscle m in muscles)
            {
                if (m.body == null) continue;
                m.body.linearVelocity = inheritedVelocity;
                m.body.angularVelocity = Vector3.zero;
            }
        }
        else RestoreAnimatedPose(animatorWasEnabledBeforeExternalControl);

        jumpSupportCooldown = 0f;
        headSmoothingReady = false;
        Physics.SyncTransforms();
    }

    // =====================================================================================
    //  IMPACTS / KNOCKDOWN
    // =====================================================================================

    public void NotifyImpact(float relativeSpeed)
    {
        if (relativeSpeed > 13f) flopTimer = Mathf.Max(flopTimer, ragdoll.impactFlopSeconds);
    }

    public void ApplyTrainWheelKnockdown(Vector3 outwardVelocity, Vector3 trainVelocity, float duration)
    {
        if (pelvisBody == null || IsVehicleSeated || knockdownActive) return;

        animatorWasEnabledBeforeKnockdown = movementAnimator != null && movementAnimator.enabled;
        knockdownActive = true;
        recoveryPoseLockFrames = 0;
        knockdownTimer = Mathf.Max(0.5f, duration > 0f ? duration : ragdoll.knockdownDuration);
        flopTimer = knockdownTimer;
        ClearInput();
        ClearFootPlants();

        if (movementAnimator != null) movementAnimator.enabled = false;
        SynchronizePhysicsLimbsToVisuals();
        ConfigureLimbSimulation(true);
        pelvisBody.isKinematic = false;
        pelvisBody.useGravity = true;
        pelvisBody.detectCollisions = true;
        pelvisBody.constraints = RigidbodyConstraints.None;
        pelvisAngularDampingBeforeKnockdown = pelvisBody.angularDamping;
        pelvisBody.angularDamping = ragdoll.ragdollAngularDamping;
        Vector3 velocity = trainVelocity + outwardVelocity;
        pelvisBody.linearVelocity = velocity;
        pelvisBody.angularVelocity = Vector3.Cross(Vector3.up, outwardVelocity.normalized) * 4f;
        foreach (Muscle m in muscles)
        {
            if (m.body == null) continue;
            m.body.angularDamping = ragdoll.ragdollAngularDamping;
            m.body.linearVelocity = velocity;
            m.body.angularVelocity = UnityEngine.Random.insideUnitSphere * 3f;
        }
        IsGrounded = false;
        hasSupportSurface = false;
        jumpSupportCooldown = 0.35f;
    }

    private void FinishKnockdown()
    {
        if (!knockdownActive || pelvisBody == null) return;
        knockdownActive = false;
        recoveryPoseLockFrames = 3;
        knockdownTimer = flopTimer = 0f;

        Quaternion upright = FacingToRotation(desiredFacing, out _);
        pelvisBody.rotation = upright;
        pelvisBody.angularVelocity = Vector3.zero;
        pelvisBody.angularDamping = pelvisAngularDampingBeforeKnockdown > 0f ? pelvisAngularDampingBeforeKnockdown : 7f;
        pelvisBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        movementFacingWorld = upright;
        airborneInheritedVelocity = Vector3.ProjectOnPlane(pelvisBody.linearVelocity, Vector3.up);
        hasAirborneInheritedVelocity = !IsGrounded && airborneInheritedVelocity.sqrMagnitude > 0.0001f;

        if (dynamicPoseActive)
        {
            if (movementAnimator != null) movementAnimator.enabled = false;
            ResetProceduralPoseOffsets();
            // Restore every imported transform, including helper, shoulder and finger bones.
            RestoreVisualRestPose();
            AnchorAnimatedVisualAt(pelvisBody.position, upright);
            ApplyKinematicVisualPose(pelvisBody.position, upright);
            SynchronizePhysicsLimbsToVisuals();
            ConfigureLimbSimulation(true);
        }
        else RestoreAnimatedPose(animatorWasEnabledBeforeKnockdown);

        foreach (Muscle m in muscles)
            if (m.body != null) m.body.angularDamping = 4.5f;
        headSmoothingReady = false;
        jumpSupportCooldown = 0f;
    }

    // =====================================================================================
    //  RAGDOLL CONSTRUCTION
    // =====================================================================================

    public void BuildRagdoll()
    {
        CacheBones();
        if (pelvis == null) return;
        renderedBodyHeight = MeasureRenderedBodyHeight();
        RemoveLegacyBonePhysics();
        CreateOrResetPhysicsRoot();

        muscles.Clear();
        muscleByBone.Clear();
        bodyByVisualBone.Clear();
        Quaternion pelvisStartRotation = pelvis.rotation;
        Transform[] hips = { leftHip, rightHip };
        for (int i = 0; i < 2; i++)
        {
            hipRestRot[i] = hips[i] != null ? Quaternion.Inverse(pelvisStartRotation) * hips[i].rotation : Quaternion.identity;
            hipRestPos[i] = hips[i] != null ? pelvis.InverseTransformPoint(hips[i].position) : Vector3.zero;
        }

        // One constrained core supplies collision, movement and support; the torso itself never bends.
        Transform core = new GameObject("Physics_Core").transform;
        core.SetParent(physicsRoot, false);
        core.SetPositionAndRotation(pelvis.position, pelvis.rotation);
        pelvisBody = ConfigureBody(core, 12f, true);
        ConfigureCollider(core, pelvis);
        pelvisVisualRotationOffset = Quaternion.Inverse(core.rotation) * pelvis.rotation;
        headRestRelativeToPelvis = head != null ? Quaternion.Inverse(pelvisStartRotation) * head.rotation : Quaternion.identity;

        foreach (string boneName in PhysicalBoneNames)
        {
            Transform visual = Bone(boneName);
            if (visual == null) continue;
            Transform proxy = GetOrCreateProxy(visual);
            Rigidbody body = ConfigureBody(proxy, MassForBone(visual.name), false);
            ConfigureCollider(proxy, visual);
            bodyByVisualBone[visual] = body;

            string n = visual.name.ToLowerInvariant();
            bool isLeg = n.StartsWith("thigh") || n.StartsWith("shin");
            var muscle = new Muscle
            {
                visualBone = visual,
                physicsBone = proxy,
                body = body,
                restRelativeToPelvis = Quaternion.Inverse(pelvisStartRotation) * visual.rotation,
                visualRotationOffset = Quaternion.Inverse(proxy.rotation) * visual.rotation,
                strength = isLeg ? ragdoll.lowerBodyStrength : ragdoll.armStrength,
                isLeg = isLeg,
                isRight = n.Contains(".r")
            };
            muscles.Add(muscle);
            muscleByBone[visual] = muscle;
        }

        foreach (Muscle m in muscles)
        {
            Rigidbody connected = FindConnectedBody(m.visualBone);
            if (connected != null) ConfigureJoint(m.physicsBone, connected, m.visualBone.name);
        }

        foreach (Foot f in feet)
        {
            f.body = f.foot != null && bodyByVisualBone.TryGetValue(f.foot, out Rigidbody b) ? b : null;
            f.capsule = f.body != null ? f.body.GetComponent<CapsuleCollider>() : null;
        }
        IgnoreInternalCollisions();
    }

    private void CacheBones()
    {
        bones.Clear();
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) bones.TryAdd(t.name, t);

        pelvis = Bone("hip");
        visualRoot = pelvis;
        while (visualRoot != null && visualRoot.parent != null && visualRoot.parent != transform) visualRoot = visualRoot.parent;
        if (visualRoot == transform) visualRoot = null;

        chest = Bone("spine.002") ?? Bone("spine.001") ?? Bone("spine");
        head = Bone("spine.003") ?? chest;
        leftHip = Bone("hip.L");
        rightHip = Bone("hip.R");
        leftArm = Bone("arm.L");
        leftShoulder = Bone("sholder.L") ?? Bone("shoulder.L"); // "sholder" matches the model's spelling
        rightArm = Bone("arm.R");
        leftForearm = Bone("forearm.L");
        rightForearm = Bone("forearm.R");
        leftHand = Bone("hand.L");
        rightHand = Bone("hand.R");

        LeftLeg.thigh = Bone("thigh.L"); LeftLeg.shin = Bone("shin.L"); LeftLeg.foot = Bone("shin.L.001");
        RightLeg.thigh = Bone("thigh.R"); RightLeg.shin = Bone("shin.R"); RightLeg.foot = Bone("shin.R.001");
    }

    private Transform Bone(string boneName) => bones.TryGetValue(boneName, out Transform t) ? t : null;

    private void CreateOrResetPhysicsRoot()
    {
        // Remove any previous rig so obsolete bodies never stay active after the layout changes.
        Transform oldCore = transform.Find("PhysicalCore");
        if (oldCore != null) DisposeRig(oldCore.gameObject);
        GameObject oldExternal = GameObject.Find(gameObject.name + "_PhysicsRig");
        if (oldExternal != null) DisposeRig(oldExternal);
        Transform oldNested = transform.Find("Physics");
        if (oldNested != null) DisposeRig(oldNested.gameObject);

        physicsRoot = new GameObject("Physics").transform;
        physicsRoot.SetParent(transform, false);
        physicsRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        physicsRoot.localScale = Vector3.one;
    }

    private static void DisposeRig(GameObject rig)
    {
        // Disable immediately because Destroy is deferred during play.
        foreach (Collider c in rig.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        rig.SetActive(false);
        DestroyObjectSafely(rig);
    }

    private static void DestroyObjectSafely(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }

    private Transform GetOrCreateProxy(Transform visual)
    {
        string proxyName = "Physics_" + visual.name;
        Transform proxy = physicsRoot.Find(proxyName);
        if (proxy == null)
        {
            proxy = new GameObject(proxyName).transform;
            proxy.SetParent(physicsRoot, false);
        }
        proxy.SetPositionAndRotation(visual.position, visual.rotation);
        proxy.localScale = Vector3.one;
        return proxy;
    }

    private Rigidbody ConfigureBody(Transform proxy, float mass, bool isPelvis)
    {
        Rigidbody body = proxy.GetComponent<Rigidbody>();
        if (body == null) body = proxy.gameObject.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = isPelvis ? 1.1f : 0.55f;
        body.angularDamping = isPelvis ? 7f : 4.5f;
        body.useGravity = true;
        body.isKinematic = false;
        body.detectCollisions = true;
        // Locking only pelvis pitch/roll keeps the character standing; yaw, translation and limbs stay simulated.
        body.constraints = isPelvis ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ : RigidbodyConstraints.None;
        body.maxAngularVelocity = isPelvis ? 7f : 11f;
        body.maxDepenetrationVelocity = 15f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.solverIterations = 32;
        body.solverVelocityIterations = 16;
        ImpactRelay relay = proxy.GetComponent<ImpactRelay>();
        if (relay == null) relay = proxy.gameObject.AddComponent<ImpactRelay>();
        relay.owner = this;
        return body;
    }

    private void ConfigureCollider(Transform proxy, Transform visual)
    {
        Transform child = FirstUsefulChild(visual);
        Vector3 worldEnd = visual == pelvis && head != null
            ? head.position
            : child != null ? child.position : visual.position + visual.up * renderedBodyHeight * 0.09f;
        Vector3 localEnd = Quaternion.Inverse(proxy.rotation) * (worldEnd - visual.position);
        float length = Mathf.Max(renderedBodyHeight * 0.055f, Vector3.Distance(visual.position, worldEnd));
        float radius = visual == pelvis ? Mathf.Clamp(renderedBodyHeight * 0.08f, 0.09f, 0.16f) : WorldRadiusForBone(visual.name);

        CapsuleCollider capsule = proxy.GetComponent<CapsuleCollider>();
        if (capsule == null) capsule = proxy.gameObject.AddComponent<CapsuleCollider>();
        Vector3 abs = new Vector3(Mathf.Abs(localEnd.x), Mathf.Abs(localEnd.y), Mathf.Abs(localEnd.z));
        capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : (abs.z > abs.y ? 2 : 1);
        capsule.center = localEnd.sqrMagnitude > 1e-6f ? localEnd.normalized * length * 0.46f : Vector3.zero;
        capsule.radius = radius;
        capsule.height = Mathf.Max(radius * 2.05f, length * (visual == pelvis ? 0.92f : 0.82f));
        capsule.isTrigger = false;
        capsule.contactOffset = Mathf.Clamp(radius * 0.15f, 0.005f, 0.015f);
    }

    private static void ConfigureJoint(Transform proxy, Rigidbody connected, string boneName)
    {
        ConfigurableJoint joint = proxy.GetComponent<ConfigurableJoint>();
        if (joint == null) joint = proxy.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = connected;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = connected.transform.InverseTransformPoint(proxy.position);
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;

        string n = boneName.ToLowerInvariant();
        float xLimit = 24f, yzLimit = 10f;
        if (n.StartsWith("thigh")) { xLimit = 36f; yzLimit = 12f; }
        else if (n.StartsWith("shin")) { bool ankle = n.EndsWith(".001"); xLimit = ankle ? 18f : 48f; yzLimit = ankle ? 6f : 5f; }
        else if (n.StartsWith("arm")) { xLimit = 38f; yzLimit = 30f; }
        else if (n.StartsWith("forearm") || n.StartsWith("hand")) { xLimit = 30f; yzLimit = 12f; }

        joint.lowAngularXLimit = new SoftJointLimit { limit = -xLimit };
        joint.highAngularXLimit = new SoftJointLimit { limit = xLimit };
        joint.angularYLimit = new SoftJointLimit { limit = yzLimit };
        joint.angularZLimit = new SoftJointLimit { limit = yzLimit };
        joint.angularXLimitSpring = new SoftJointLimitSpring { spring = 1800f, damper = 90f };
        joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = 1800f, damper = 90f };
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.008f;
        joint.projectionAngle = 2f;
        joint.enableCollision = false;
        joint.enablePreprocessing = true;
    }

    private void RemoveLegacyBonePhysics()
    {
        foreach (string boneName in VisualBoneNames)
        {
            Transform bone = Bone(boneName);
            if (bone == null) continue;
            ConfigurableJoint joint = bone.GetComponent<ConfigurableJoint>();
            Collider col = bone.GetComponent<Collider>();
            Rigidbody body = bone.GetComponent<Rigidbody>();
            ImpactRelay relay = bone.GetComponent<ImpactRelay>();
            if (col != null) col.enabled = false;
            if (body != null) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
            DestroyObjectSafely(joint);
            DestroyObjectSafely(col);
            DestroyObjectSafely(body);
            DestroyObjectSafely(relay);
        }
    }

    private void IgnoreInternalCollisions()
    {
        if (physicsRoot == null) return;
        Collider[] colliders = physicsRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                Physics.IgnoreCollision(colliders[i], colliders[j], true);
    }

    private Rigidbody FindConnectedBody(Transform visualBone)
    {
        for (Transform cursor = visualBone.parent; cursor != null; cursor = cursor.parent)
            if (bodyByVisualBone.TryGetValue(cursor, out Rigidbody body)) return body;
        return pelvisBody;
    }

    private static Transform FirstUsefulChild(Transform bone)
    {
        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);
            if (!child.name.EndsWith("_end", StringComparison.OrdinalIgnoreCase)) return child;
        }
        return bone.childCount > 0 ? bone.GetChild(0) : null;
    }

    private float WorldRadiusForBone(string boneName)
    {
        string n = boneName.ToLowerInvariant();
        float fraction = n == "hip" ? 0.060f
            : n.StartsWith("spine") ? 0.045f
            : n.StartsWith("thigh") ? 0.040f
            : n.StartsWith("shin") ? 0.032f
            : n.StartsWith("arm") ? 0.027f
            : n.StartsWith("forearm") ? 0.023f
            : 0.025f;
        return Mathf.Clamp(renderedBodyHeight * fraction, 0.035f, 0.11f);
    }

    private static float MassForBone(string boneName)
    {
        string n = boneName.ToLowerInvariant();
        if (n == "hip") return 8f;
        if (n.StartsWith("spine")) return 4f;
        if (n.StartsWith("thigh")) return 3f;
        if (n.StartsWith("shin")) return 2f;
        if (n.StartsWith("arm")) return 1.2f;
        return 0.7f;
    }

    private float MeasureRenderedBodyHeight()
    {
        bool found = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found ? Mathf.Clamp(bounds.size.y, 0.8f, 3f) : 1.8f;
    }

    private float EstimateLegLength()
    {
        Transform foot = LeftLeg.foot != null ? LeftLeg.foot : RightLeg.foot;
        return foot != null && pelvis != null ? Vector3.Distance(pelvis.position, foot.position) : 0.9f;
    }

    private void CacheFootOffsets()
    {
        Vector3 right = Vector3.Cross(Vector3.up, restForward).normalized;
        float fallbackSide = Mathf.Clamp(renderedBodyHeight * 0.07f, 0.07f, 0.16f);
        float rawLeft = LeftLeg.foot != null && pelvis != null ? Vector3.Dot(LeftLeg.foot.position - pelvis.position, right) : -fallbackSide;
        float rawRight = RightLeg.foot != null && pelvis != null ? Vector3.Dot(RightLeg.foot.position - pelvis.position, right) : fallbackSide;

        // Keep the model's real left/right ordering but guarantee a minimum stance width,
        // so the legs can never target the same line and cross through each other.
        float center = (rawLeft + rawRight) * 0.5f;
        float halfWidth = Mathf.Max(Mathf.Abs(rawRight - rawLeft) * 0.5f, fallbackSide);
        float sign = rawLeft <= rawRight ? 1f : -1f;
        LeftLeg.sideOffset = center - sign * halfWidth;
        RightLeg.sideOffset = center + sign * halfWidth;
    }
}

// =========================================================================================
//  SCENE BOOTSTRAP
// =========================================================================================
public static class GameBootstrap
{
    private const string GameplaySceneName = "game";
    private const string ResourcePath = "Wobbly/robber mangl";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePlayerIfNeeded()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, GameplaySceneName, StringComparison.OrdinalIgnoreCase)) return;

        PlayerController controller = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (controller == null)
        {
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError("Could not load the robber model from Resources/" + ResourcePath);
                return;
            }

            GameObject player = new GameObject("WobblyPlayer");
            GameObject model = UnityEngine.Object.Instantiate(prefab, player.transform);
            model.name = "RobberVisualRig";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = CalculateBounds(renderers);
            if (bounds.size.y > 0.001f)
            {
                model.transform.localScale = Vector3.one * (1.8f / bounds.size.y);
                bounds = CalculateBounds(renderers);
                model.transform.position += Vector3.up * (0.04f - bounds.min.y);
            }

            controller = player.AddComponent<PlayerController>();
            player.transform.position = Vector3.zero;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        PlayerCamera cameraController = camera.GetComponent<PlayerCamera>();
        if (cameraController == null) cameraController = camera.gameObject.AddComponent<PlayerCamera>();
        cameraController.Target = controller;

        EnsureComponent<WeaponController>(controller);
        EnsureComponent<VehicleInteraction>(controller);
        EnsureComponent<ClimbController>(controller);
        EnsureComponent<CharacterAppearance>(controller).LoadSaved();
        MenuManager.Ensure(controller);
        PickupWeapon.PrepareSceneWeapons();
    }

    private static T EnsureComponent<T>(Component owner) where T : Component
    {
        T component = owner.GetComponent<T>();
        return component != null ? component : owner.gameObject.AddComponent<T>();
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}