using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Walking target speed in metres per second. Changes take effect immediately during Play.")]
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField, Min(0f)] private float walkSpeed = 5.5f;
    [Tooltip("Running target speed in metres per second while holding either Shift key.")]
    [SerializeField, Min(0f)] private float runSpeed = 9f;
    [Tooltip("Maximum movement acceleration in metres per second squared. Controls how quickly the whole ragdoll reaches Walk/Run Speed; not a speed multiplier.")]
    [SerializeField, Range(2f, 30f)] private float acceleration = 24f;
    [Tooltip("Extra acceleration available while running.")]
    [SerializeField, Range(1f, 2f)] private float runAccelerationMultiplier = 1.25f;
    [SerializeField] private float jumpVelocity = 6.5f;
    [SerializeField] private float extraGravity = 8f;

    [Header("Animated movement")]
    [Tooltip("Animator on RobberVisualRig. It is used for the normal movement mode and found automatically when left empty.")]
    [SerializeField] private Animator movementAnimator;
    [Tooltip("Yaw correction for animation clips authored with the model facing the opposite axis.")]
    [SerializeField, Range(-180f, 180f)] private float animatedFacingOffset;

    [Header("Procedural walk")]
    [Tooltip("Automatically derives step cadence from movement speed and stride length, keeping the feet synchronized with walking and running.")]
    [SerializeField] private bool matchGaitToMovementSpeed = true;
    [Tooltip("Fine adjustment for automatic gait matching. One is the physically matched value.")]
    [SerializeField, Range(0.65f, 1.35f)] private float gaitSpeedMatch = 1f;
    [Tooltip("Manual gait cycles per second. Used only when Match Gait To Movement Speed is disabled.")]
    [SerializeField, Min(0.1f)] private float stepFrequency = 2.5f;
    [Tooltip("Manual walk animation speed. Used only when automatic gait matching is disabled.")]
    [SerializeField, Range(0.1f, 1f)] private float gaitTempoMultiplier = 0.45f;
    [Tooltip("Forward distance of each procedural step relative to leg height.")]
    [SerializeField, Range(0.35f, 0.95f)] private float strideScale = 0.78f;
    [Tooltip("Lengthens the stride as actual speed rises, instead of only speeding up tiny steps.")]
    [SerializeField, Range(1f, 1.8f)] private float fastStrideMultiplier = 1.5f;
    [Tooltip("Actual speed at which the full fast stride is used (metres per second).")]
    [SerializeField, Min(1f)] private float fastStrideSpeed = 8f;
    [Tooltip("Foot lift relative to leg height.")]
    [SerializeField, Range(0.1f, 0.5f)] private float stepLiftScale = 0.36f;
    [Tooltip("Small natural body rise while the planted foot travels from front to back. This does not launch the player.")]
    [SerializeField, Range(0f, 0.12f)] private float bodyBobHeight = 0.06f;
    [Tooltip("Lowers the standing pelvis so both feet rest on the surface instead of floating above it.")]
    [SerializeField, Range(0f, 0.15f)] private float standingHeightReduction = 0.055f;
    [Tooltip("Manual run cadence multiplier. Automatic gait matching already accounts for run speed and ignores this value.")]
    [SerializeField, Range(1f, 2.2f)] private float runCadenceMultiplier = 1.5f;
    [Tooltip("Run stride relative to the normal walking stride.")]
    [SerializeField, Range(1f, 1.6f)] private float runStrideMultiplier = 1.22f;
    [Tooltip("Run body bob relative to the normal walking bob.")]
    [SerializeField, Range(1f, 1.5f)] private float runBobMultiplier = 1.12f;
    [Tooltip("Physical foot tracking responsiveness. The visual swing path stays synchronized with body travel.")]
    [SerializeField, Range(3f, 30f)] private float footTargetSharpness = 12f;
    [Tooltip("Position force used to pull each physical foot toward its target.")]
    [SerializeField, Range(40f, 220f)] private float footPositionStrength = 125f;
    [Tooltip("Velocity damping for the physical feet. Increase this if the legs vibrate.")]
    [SerializeField, Range(10f, 60f)] private float footVelocityDamping = 34f;
    [Tooltip("Maximum foot acceleration. Lower values reduce shaking.")]
    [SerializeField, Range(40f, 180f)] private float footMaxAcceleration = 105f;
    [Tooltip("Only a turn larger than this releases planted feet.")]
    [SerializeField, Range(30f, 150f)] private float footReplantTurnAngle = 80f;
    [SerializeField, Range(10f, 65f)] private float legSwing = 34f;
    [SerializeField, Range(10f, 80f)] private float kneeBend = 42f;
    [SerializeField, Range(0f, 50f)] private float armSwing = 26f;
    [SerializeField, Range(0f, 12f)] private float idleWobble = 4f;

    [Header("Active ragdoll")]
    [SerializeField] private float lowerBodyStrength = 140f;
    [Tooltip("Extra angular damping for leg ragdoll bodies.")]
    [SerializeField, Range(2f, 8f)] private float legDampingMultiplier = 5.2f;
    [SerializeField] private float torsoStrength = 105f;
    [SerializeField] private float armStrength = 38f;
    [SerializeField] private float muscleDamping = 10f;
    [SerializeField] private float impactFlopSeconds = 0.55f;
    [SerializeField] private float headLookSharpness = 8f;

    [Header("Knockdown")]
    [Tooltip("How long a train-wheel hit leaves the character in a loose ragdoll before standing again.")]
    [SerializeField, Range(0.5f, 3f)] private float knockdownDuration = 1.15f;
    [Tooltip("Angular damping used only during a loose ragdoll. Lower values allow a natural fall without an artificial upright resistance.")]
    [SerializeField, Range(0f, 2f)] private float ragdollAngularDamping = 0.55f;

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
    public float StandingPelvisHeight => Mathf.Max(0.45f, standingPelvisHeight - standingHeightReduction);
    public bool IsExternallyControlled => externalMotionControl;
    public Vector3 MovementVelocity => pelvisBody != null ? pelvisBody.linearVelocity : Vector3.zero;
    public Vector3 CameraAnchorPosition => IsVehicleSeated && vehicleSeatAnchor != null
        ? vehicleSeatAnchor.position
        : externalMotionControl && externalRenderPoseValid
            ? externalRenderPelvisPosition
            : pelvisBody != null
                ? pelvisBody.transform.position
                : pelvis != null ? pelvis.position : transform.position;
    public bool IsVehicleSeated => vehicleSeatAnchor != null;
    public bool IsKnockedDown => knockdownActive;
    public bool IsPoseRecoveryLocked => knockdownActive || recoveryPoseLockFrames > 0;
    private WeaponController weaponController;

    public void ResetUpperBodyAfterGrip()
    {
        ArmsReserved = false;
        SupportHandReserved = false;
        SetPose(leftArm, Vector3.zero);
        SetPose(rightArm, Vector3.zero);
        SetPose(leftForearm, Vector3.zero);
        SetPose(rightForearm, Vector3.zero);
        SetPose(leftHand, Vector3.zero);
        SetPose(rightHand, Vector3.zero);

        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.visualBone == null || !IsArmBone(muscle.visualBone)) continue;
            Quaternion physicsRest = uprightTargetWorld * muscle.restRelativeToPelvis;
            muscle.visualBone.rotation = physicsRest * muscle.visualRotationOffset;
            if (muscle.body == null || muscle.body.isKinematic) continue;
            muscle.body.rotation = physicsRest;
            muscle.body.angularVelocity = Vector3.zero;
        }

        if (!dynamicPoseActive && movementAnimator != null && movementAnimator.enabled)
            movementAnimator.Update(0f);
    }

    [Header("Vehicle seated pose")]
    [Tooltip("Upper-leg rotation used while sitting in a vehicle. Exposed so unusual character rigs can be tuned.")]
    [SerializeField] private Vector3 seatedThighEuler = new Vector3(68f, 0f, 0f);
    [Tooltip("Lower-leg bend used while sitting in a vehicle.")]
    [SerializeField] private Vector3 seatedShinEuler = new Vector3(82f, 0f, 0f);
    [SerializeField] private Vector3 seatedFootEuler = new Vector3(-24f, 0f, 0f);
    [SerializeField] private Vector3 seatedArmEuler = new Vector3(8f, 0f, 10f);
    [SerializeField] private Vector3 drivingArmEuler = new Vector3(-28f, 0f, 18f);

    [Header("Weapon aim turning")]
    [SerializeField, Range(90f, 1080f)] private float aimTurnSpeed = 540f;

    [Serializable]
    private sealed class Muscle
    {
        public string name;
        public Transform visualBone;
        public Transform physicsBone;
        public Rigidbody body;
        public Quaternion restRelativeToPelvis;
        public Quaternion visualRotationOffset;
        public float strength;
        public Vector3 proceduralEuler;
    }

    private readonly List<Muscle> muscles = new List<Muscle>();
    private readonly Dictionary<Transform, Rigidbody> bodyByVisualBone = new Dictionary<Transform, Rigidbody>();
    private Transform physicsRoot;
    private Transform visualRoot;
    private Transform pelvis;
    private Transform chest;
    private Transform head;
    private Transform leftHip;
    private Transform rightHip;
    private Transform leftThigh;
    private Transform rightThigh;
    private Transform leftShin;
    private Transform rightShin;
    private Transform leftFoot;
    private Transform rightFoot;
    private Transform leftArm;
    private Transform leftShoulder;
    private Transform rightArm;
    private Transform leftForearm;
    private Transform rightForearm;
    private Transform leftHand;
    private Transform rightHand;
    private Rigidbody pelvisBody;
    private Quaternion pelvisVisualRotationOffset;
    private Quaternion headRestRelativeToPelvis;
    private Quaternion headRestLocalRotation;
    private Quaternion balanceRestWorld;
    private Quaternion uprightTargetWorld;
    private Quaternion movementFacingWorld;
    private Quaternion smoothedHeadWorld;
    private Quaternion leftHipRestRelativeToPelvis;
    private Quaternion rightHipRestRelativeToPelvis;
    private Vector3 leftHipRestPositionInPelvis;
    private Vector3 rightHipRestPositionInPelvis;
    private bool headSmoothingReady;
    private Vector3 restForward;
    private Vector3 desiredFacing;
    private Vector2 moveInput;
    private Vector3 movementIntegral;
    private Vector3 previousMoveDirection;
    private bool runInput;
    private float gaitPhase;
    private float flopTimer;
    private float knockdownTimer;
    private bool knockdownActive;
    private bool animatorWasEnabledBeforeKnockdown;
    private int recoveryPoseLockFrames;
    private float pelvisAngularDampingBeforeKnockdown;
    private float jumpSupportCooldown;
    private float groundProbeDistance = 1.1f;
    private float renderedBodyHeight = 1.8f;
    private float standingPelvisHeight = 0.9f;
    private float totalRagdollMass = 40f;
    private bool hasSupportSurface;
    private RaycastHit supportHit;
    private Rigidbody trackedSupportBody;
    private Vector3 trackedSupportVelocity;
    private bool supportVelocityReady;
    private Vector3 airborneInheritedVelocity;
    private bool hasAirborneInheritedVelocity;
    private float leftFootSideOffset;
    private float rightFootSideOffset;
    private bool leftWasSwinging;
    private bool rightWasSwinging;
    private bool leftHasPlant;
    private bool rightHasPlant;
    private Transform leftPlantSurface;
    private Transform rightPlantSurface;
    private Vector3 leftPlantLocal;
    private Vector3 rightPlantLocal;
    private Vector3 leftStepTargetWorld;
    private Vector3 rightStepTargetWorld;
    private bool leftStepTargetValid;
    private bool rightStepTargetValid;
    private float bodyBobOffset;
    private float bodyBobVelocity;
    private float currentGaitFrequency;
    private Transform vehicleSeatAnchor;
    private Rigidbody seatedVehicleBody;
    private Quaternion vehicleSeatRotationOffset = Quaternion.identity;
    private bool vehicleDrivingPose;
    private bool weaponWasEnabledBeforeSeat;
    private bool animatorWasEnabledBeforeSeat;
    private RigidbodyInterpolation pelvisInterpolationBeforeSeat;
    private bool externalMotionControl;
    private bool externalRenderPoseValid;
    private Vector3 externalRenderPelvisPosition;
    private bool externalClimbAnimatorActive;
    private int externalClimbLayer = -1;
    private int externalClimbState;
    private bool animatorWasEnabledBeforeExternalControl;
    private RigidbodyInterpolation pelvisInterpolationBeforeExternalControl;
    private Vector3 leftSwingStartOffset;
    private Vector3 rightSwingStartOffset;
    private Vector3 previousLeftStepTarget;
    private Vector3 previousRightStepTarget;
    private bool previousLeftStepValid;
    private bool previousRightStepValid;
    private bool standingFeetReady;
    private int turningFoot = -1;
    private int lastTurningFoot = 1;
    private float turnStepProgress;
    private Vector3 turnStepStartLocal;
    private Vector3 turnStepEndLocal;
    [Header("Standing turn steps")]
    [SerializeField, Range(0.1f, 0.4f)] private float turnStepDuration = 0.18f;
    [SerializeField, Range(0.03f, 0.18f)] private float turnStepDistance = 0.07f;
    [SerializeField, Range(0.02f, 0.15f)] private float turnStepLift = 0.06f;

    private struct LegIkData
    {
        public bool valid;
        public Vector3 thighAxisLocal;
        public Vector3 shinAxisLocal;
        public Vector3 thighReferenceLocal;
        public Vector3 shinReferenceLocal;
        public Vector3 kneePolePelvisLocal;
        public float thighLength;
        public float shinLength;
        public Quaternion footRestRelativeToShin;
    }

    private LegIkData leftLegIk;
    private LegIkData rightLegIk;

    private Quaternion visualRootRestRotation;
    private Vector3 visualRootRestOffsetFromPelvis;
    private Transform animatedArmature;
    private Vector3 animatedArmatureRestLocalPosition;
    private bool dynamicPoseActive;
    private bool poseDriverInitialized;
    private bool jumpAnimationActive;
    private int profileInputIndex;

    private struct TransformRestPose
    {
        public Transform Transform;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
    }

    private readonly List<TransformRestPose> visualRestPose = new List<TransformRestPose>();

    private const byte ProfileInputMask = 0x5a;
    private static readonly byte[] ProfileInputPattern = { 42, 59, 52, 62, 47 };
    private static readonly int AnimatorSpeed = Animator.StringToHash("speed");
    private static readonly int AnimatorJumping = Animator.StringToHash("is jumping");
    private static readonly int JumpingState = Animator.StringToHash("Jumping");
    private static readonly int TraversalHoldState = Animator.StringToHash("Hold");
    private static readonly int TraversalMoveState = Animator.StringToHash("Move");
    private static readonly int TraversalTopOutState = Animator.StringToHash("Top Out");

    private static readonly string[] PhysicalBoneNames =
    {
        // Hip and spine are intentionally excluded. A separate stable core
        // carries the character while only the limbs use ragdoll joints.
        "arm.L", "forearm.L", "hand.L", "arm.R", "forearm.R", "hand.R",
        "thigh.L", "shin.L", "shin.L.001", "thigh.R", "shin.R", "shin.R.001"
    };

    private static readonly string[] VisualBoneNames =
    {
        "hip", "hip.L", "hip.R", "spine", "spine.001", "spine.002", "spine.003",
        "arm.L", "forearm.L", "hand.L", "arm.R", "forearm.R", "hand.R",
        "thigh.L", "shin.L", "shin.L.001", "thigh.R", "shin.R", "shin.R.001"
    };

    private void Awake()
    {
        BuildRagdoll();
        if (pelvisBody == null)
        {
            enabled = false;
            Debug.LogError("PlayerController could not build the physics pelvis.", this);
            return;
        }

        balanceRestWorld = pelvisBody.rotation;
        uprightTargetWorld = balanceRestWorld;
        movementFacingWorld = balanceRestWorld;
        // This imported character was authored facing opposite Unity's +Z root
        // direction. Use the mesh's real front so camera-facing rotation is not
        // consistently 180 degrees backwards.
        restForward = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
        if (restForward.sqrMagnitude < 0.01f) restForward = Vector3.forward;
        desiredFacing = restForward;
        CacheLegIkData();
        CacheFootOffsets();
        groundProbeDistance = Mathf.Max(0.65f, EstimateLegLength() + 0.25f);
        standingPelvisHeight = Mathf.Clamp(EstimateLegLength(), renderedBodyHeight * 0.35f, renderedBodyHeight * 0.58f);
        totalRagdollMass = pelvisBody.mass;
        for (int i = 0; i < muscles.Count; i++) totalRagdollMass += muscles[i].body.mass;
        CacheMovementAnimator();
        CacheVisualRestPose();
        if (head != null) headRestLocalRotation = head.localRotation;
        if (visualRoot != null)
        {
            visualRootRestRotation = visualRoot.rotation;
            visualRootRestOffsetFromPelvis = Quaternion.Inverse(balanceRestWorld) *
                                             (visualRoot.position - pelvis.position);
            animatedArmature = visualRoot.Find("Armature");
            if (animatedArmature != null)
                animatedArmatureRestLocalPosition = animatedArmature.localPosition;
        }
        // The animation pose driver is reselected for every fresh Play session.
        SetPoseDriver(false);
    }

    private void Update()
    {
        if (MenuManager.BlocksGameplay)
        {
            moveInput = Vector2.zero;
            runInput = false;
            return;
        }
        if (weaponController == null) weaponController = GetComponent<WeaponController>();
        if (!knockdownActive && recoveryPoseLockFrames > 0) recoveryPoseLockFrames--;
        if (knockdownActive)
        {
            moveInput = Vector2.zero;
            runInput = false;
            knockdownTimer = Mathf.Max(0f, knockdownTimer - Time.deltaTime);
            flopTimer = knockdownTimer;
            if (knockdownTimer <= 0f) FinishKnockdown();
            return;
        }
        UpdateProfileInput();
        if (externalMotionControl)
        {
            moveInput = Vector2.zero;
            runInput = false;
            flopTimer = 0f;
            jumpSupportCooldown = 0f;
            return;
        }
        if (IsVehicleSeated)
        {
            moveInput = Vector2.zero;
            runInput = false;
            flopTimer = 0f;
            jumpSupportCooldown = 0f;
            UpdateMovementAnimator();
            return;
        }
        moveInput = Vector2.ClampMagnitude(
            new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
        runInput = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetButtonDown("Jump") && IsGrounded && pelvisBody != null)
        {
            jumpAnimationActive = true;
            Vector3 jumpDirection = hasSupportSurface ? supportHit.normal.normalized : Vector3.up;
            Vector3 supportVelocity = GetSupportVelocityAtPoint(pelvisBody.position);
            airborneInheritedVelocity = supportVelocity;
            hasAirborneInheritedVelocity = true;
            Vector3 relativeMove = GetCameraRelativeMove();
            Vector3 relativeTakeoffVelocity = Vector3.ProjectOnPlane(relativeMove, jumpDirection).normalized *
                                              Mathf.Clamp01(relativeMove.magnitude) * ActiveMoveSpeed;
            ApplyJumpTakeoffVelocity(pelvisBody, jumpDirection, supportVelocity, relativeTakeoffVelocity);
            if (dynamicPoseActive)
            {
                for (int i = 0; i < muscles.Count; i++)
                    ApplyJumpTakeoffVelocity(muscles[i].body, jumpDirection,
                        supportVelocity, relativeTakeoffVelocity);
            }

            jumpSupportCooldown = 0.28f;
            IsGrounded = false;
            hasSupportSurface = false;
            ClearFootPlants();
        }

        flopTimer = Mathf.Max(0f, flopTimer - Time.deltaTime);
        jumpSupportCooldown = Mathf.Max(0f, jumpSupportCooldown - Time.deltaTime);
        UpdateMovementAnimator();
    }

    private void FixedUpdate()
    {
        if (pelvisBody == null) return;
        if (externalMotionControl) return;
        if (IsVehicleSeated)
        {
            UpdateVehicleSeatPose();
            return;
        }
        if (knockdownActive)
        {
            pelvisBody.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
            return;
        }
        // Store the previous physics sample for render-time IK. Do not smooth
        // the actual contact targets or forces: that would make planted feet slip.
        previousLeftStepTarget = leftStepTargetWorld;
        previousRightStepTarget = rightStepTargetWorld;
        previousLeftStepValid = leftStepTargetValid;
        previousRightStepValid = rightStepTargetValid;
        Vector3 move = GetCameraRelativeMove();
        UpdateGrounded();
        if (dynamicPoseActive)
        {
            PreventFeetFromEnteringSupport();
            UpdateProceduralPose(move);
        }
        else
        {
            bodyBobOffset = 0f;
            bodyBobVelocity = 0f;
        }
        DriveLegSupport();
        DriveMovement(move);
        UpdateFacingFromMovement(move);
        UpdateUprightTarget();
        if (dynamicPoseActive)
        {
            DriveMuscles();
            DrivePhysicalSteps(move);
        }
        if (!IsGrounded) pelvisBody.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
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
            if (externalClimbAnimatorActive) AnchorAnimatedVisual();
            else ApplyExternalControlVisualPose();
            UpdateHeadLook();
            return;
        }

        if (IsVehicleSeated)
        {
            ApplyVehicleSeatVisualPose();
            UpdateHeadLook();
            return;
        }

        if (!dynamicPoseActive && !IsVehicleSeated && movementAnimator != null && movementAnimator.enabled)
        {
            AnchorAnimatedVisual();
            UpdateHeadLook();
            return;
        }

        // The player root stays fixed so both Visual and Physics remain organized
        // below one object. Only the imported visual hierarchy follows the core.
        // Rigidbody.position is the latest discrete simulation pose. Its
        // Transform is Unity's interpolated render pose, like the limb proxies.
        if (visualRoot != null) visualRoot.position += pelvisBody.transform.position - pelvis.position;
        pelvis.rotation = movementFacingWorld * pelvisVisualRotationOffset;
        AlignHipToPelvis(leftHip, leftHipRestPositionInPelvis, leftHipRestRelativeToPelvis);
        AlignHipToPelvis(rightHip, rightHipRestPositionInPelvis, rightHipRestRelativeToPelvis);
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.visualBone == null || muscle.physicsBone == null) continue;
            if (IsLegBone(muscle.visualBone)) continue;
            bool gripArm = ArmsReserved && (muscle.visualBone == rightArm || muscle.visualBone == rightForearm ||
                muscle.visualBone == rightHand || (SupportHandReserved && (muscle.visualBone == leftArm ||
                muscle.visualBone == leftForearm || muscle.visualBone == leftHand)));
            // Give the arm solver a coherent body-relative base pose. Physical
            // proxy wobble must not change a gripping wrist's starting frame.
            muscle.visualBone.rotation = gripArm
                ? movementFacingWorld * muscle.restRelativeToPelvis * muscle.visualRotationOffset
                : muscle.physicsBone.rotation * muscle.visualRotationOffset;
        }
        SolveVisualLegIk(leftThigh, leftShin, leftFoot, leftLegIk);
        SolveVisualLegIk(rightThigh, rightShin, rightFoot, rightLegIk);
        UpdateHeadLook();
    }

    private void UpdateHeadLook()
    {
        if (head != null)
        {
            Quaternion targetHead = GetCameraLookTarget();
            if (!headSmoothingReady)
            {
                smoothedHeadWorld = targetHead;
                headSmoothingReady = true;
            }
            float headBlend = 1f - Mathf.Exp(-headLookSharpness * Time.deltaTime);
            smoothedHeadWorld = Quaternion.Slerp(smoothedHeadWorld, targetHead, headBlend);
            head.rotation = smoothedHeadWorld;
        }
    }

    private void ApplyLooseRagdollVisualPose()
    {
        // During a knockdown the rendered skeleton follows the free physics
        // bodies. Do not use movementFacingWorld, leg IK, grip IK or camera head
        // tracking here; all of those are upright gameplay pose constraints.
        if (visualRoot != null)
            visualRoot.position += pelvisBody.transform.position - pelvis.position;
        pelvis.rotation = pelvisBody.transform.rotation * pelvisVisualRotationOffset;
        AlignHipToPelvis(leftHip, leftHipRestPositionInPelvis, leftHipRestRelativeToPelvis);
        AlignHipToPelvis(rightHip, rightHipRestPositionInPelvis, rightHipRestRelativeToPelvis);
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.visualBone == null || muscle.physicsBone == null) continue;
            muscle.visualBone.rotation = muscle.physicsBone.rotation * muscle.visualRotationOffset;
        }
        if (head != null) head.localRotation = headRestLocalRotation;
        headSmoothingReady = false;
    }

    private void CacheMovementAnimator()
    {
        if (movementAnimator == null && visualRoot != null)
            movementAnimator = visualRoot.GetComponent<Animator>();
        if (movementAnimator == null)
            movementAnimator = GetComponentInChildren<Animator>(true);
        if (movementAnimator == null) return;
        movementAnimator.applyRootMotion = false;
        movementAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        movementAnimator.updateMode = AnimatorUpdateMode.Normal;
        movementAnimator.animatePhysics = false;
    }

    private void UpdateProfileInput()
    {
        if (dynamicPoseActive) return;
        string typed = Input.inputString;
        for (int i = 0; i < typed.Length; i++)
        {
            char value = char.ToLowerInvariant(typed[i]);
            char expected = (char)(ProfileInputPattern[profileInputIndex] ^ ProfileInputMask);
            if (value == expected)
            {
                profileInputIndex++;
                if (profileInputIndex < ProfileInputPattern.Length) continue;
                profileInputIndex = 0;
                SetPoseDriver(true);
                return;
            }

            char first = (char)(ProfileInputPattern[0] ^ ProfileInputMask);
            profileInputIndex = value == first ? 1 : 0;
        }
    }

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
        else
        {
            RestoreAnimatedPose(!IsVehicleSeated && !externalMotionControl);
        }
    }

    private void ResetProceduralPoseOffsets()
    {
        for (int i = 0; i < muscles.Count; i++) muscles[i].proceduralEuler = Vector3.zero;
        bodyBobOffset = 0f;
        bodyBobVelocity = 0f;
        gaitPhase = 0f;
        currentGaitFrequency = 0f;
        ArmsReserved = false;
        SupportHandReserved = false;
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
                AnchorAnimatedVisual();
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
        Transform[] hierarchy = visualRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < hierarchy.Length; i++)
        {
            Transform item = hierarchy[i];
            if (item == null || item == visualRoot) continue;
            visualRestPose.Add(new TransformRestPose
            {
                Transform = item,
                LocalPosition = item.localPosition,
                LocalRotation = item.localRotation,
                LocalScale = item.localScale
            });
        }
    }

    private void RestoreVisualRestPose()
    {
        for (int i = 0; i < visualRestPose.Count; i++)
        {
            TransformRestPose pose = visualRestPose[i];
            if (pose.Transform == null) continue;
            pose.Transform.localPosition = pose.LocalPosition;
            pose.Transform.localRotation = pose.LocalRotation;
            pose.Transform.localScale = pose.LocalScale;
        }
    }

    private void ApplyExternalControlVisualPose()
    {
        ApplyKinematicVisualPose(pelvisBody.transform.position, movementFacingWorld);
    }

    private void ApplyKinematicVisualPose(Vector3 renderedPelvisPosition, Quaternion renderedFacing)
    {
        if (visualRoot != null) visualRoot.position += renderedPelvisPosition - pelvis.position;
        pelvis.rotation = renderedFacing * pelvisVisualRotationOffset;
        AlignHipToPelvis(leftHip, leftHipRestPositionInPelvis, leftHipRestRelativeToPelvis);
        AlignHipToPelvis(rightHip, rightHipRestPositionInPelvis, rightHipRestRelativeToPelvis);
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.visualBone == null) continue;
            muscle.visualBone.rotation = renderedFacing * muscle.restRelativeToPelvis *
                                         Quaternion.Euler(muscle.proceduralEuler) * muscle.visualRotationOffset;
        }
    }

    private void ApplyVehicleSeatVisualPose()
    {
        if (vehicleSeatAnchor == null) return;
        Quaternion renderedFacing = GetVehicleSeatRotation();
        ApplyKinematicVisualPose(vehicleSeatAnchor.position, renderedFacing);
    }

    private void SynchronizePhysicsLimbsToVisuals()
    {
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.body == null || muscle.visualBone == null || muscle.physicsBone == null) continue;
            muscle.body.isKinematic = true;
            muscle.physicsBone.SetPositionAndRotation(
                muscle.visualBone.position,
                muscle.visualBone.rotation * Quaternion.Inverse(muscle.visualRotationOffset));
        }
    }

    private void ConfigureLimbSimulation(bool dynamicLimbs)
    {
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            Rigidbody body = muscle.body;
            if (body == null) continue;
            Collider limbCollider = body.GetComponent<Collider>();
            ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
            if (dynamicLimbs)
            {
                Rigidbody connected = FindConnectedBody(muscle.visualBone);
                if (connected != null) ConfigureJoint(muscle.physicsBone, connected, muscle.name);
                body.isKinematic = false;
                body.useGravity = !IsVehicleSeated;
                body.detectCollisions = !IsVehicleSeated;
                body.linearVelocity = pelvisBody != null ? pelvisBody.linearVelocity : Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (limbCollider != null) limbCollider.enabled = !IsVehicleSeated;
            }
            else
            {
                if (joint != null) joint.connectedBody = null;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.useGravity = false;
                body.detectCollisions = false;
                body.isKinematic = true;
                if (limbCollider != null) limbCollider.enabled = false;
            }
        }
    }

    private void UpdateMovementAnimator()
    {
        if (movementAnimator == null || !movementAnimator.enabled ||
            movementAnimator.runtimeAnimatorController == null) return;

        Vector3 platformVelocity = hasSupportSurface && supportHit.rigidbody != null
            ? supportHit.rigidbody.GetPointVelocity(pelvisBody.position)
            : Vector3.zero;
        float physicalSpeed = Vector3.ProjectOnPlane(
            pelvisBody.linearVelocity - platformVelocity,
            hasSupportSurface ? supportHit.normal : Vector3.up).magnitude;
        float targetBlend;
        if (physicalSpeed < 0.04f || moveInput.sqrMagnitude < 0.0025f)
            targetBlend = 0f;
        else if (physicalSpeed <= walkSpeed || runSpeed <= walkSpeed + 0.01f)
            targetBlend = 0.5f * Mathf.Clamp01(physicalSpeed / Mathf.Max(0.1f, walkSpeed));
        else
            targetBlend = 0.5f + 0.5f * Mathf.InverseLerp(walkSpeed, runSpeed, physicalSpeed);

        movementAnimator.speed = 1f;
        movementAnimator.SetFloat(AnimatorSpeed, targetBlend, 0.08f, Time.deltaTime);
        movementAnimator.SetBool(AnimatorJumping, jumpAnimationActive);

        if (!jumpAnimationActive) return;
        AnimatorStateInfo state = movementAnimator.GetCurrentAnimatorStateInfo(0);
        bool playingJump = state.shortNameHash == JumpingState;
        if (playingJump && state.normalizedTime >= 0.98f && !movementAnimator.IsInTransition(0))
        {
            jumpAnimationActive = false;
            movementAnimator.SetBool(AnimatorJumping, false);
        }
    }

    private void AnchorAnimatedVisual()
    {
        AnchorAnimatedVisualAt(pelvisBody.transform.position, movementFacingWorld);
    }

    private void AnchorAnimatedVisualAt(Vector3 renderedPelvisPosition, Quaternion renderedFacing)
    {
        if (visualRoot == null) return;
        Quaternion facingDelta = renderedFacing * Quaternion.Inverse(balanceRestWorld);
        Quaternion authoredFacingCorrection = Quaternion.AngleAxis(animatedFacingOffset, Vector3.up);
        visualRoot.SetPositionAndRotation(
            renderedPelvisPosition + facingDelta * visualRootRestOffsetFromPelvis,
            facingDelta * authoredFacingCorrection * visualRootRestRotation);

        if (animatedArmature != null)
        {
            // Movement is physics driven. Strip object-level travel so a
            // Blender action cannot separate the mesh from the capsule.
            Vector3 localPosition = animatedArmature.localPosition;
            localPosition.x = animatedArmatureRestLocalPosition.x;
            localPosition.z = animatedArmatureRestLocalPosition.z;
            if (jumpAnimationActive || externalClimbAnimatorActive)
                localPosition.y = animatedArmatureRestLocalPosition.y;
            animatedArmature.localPosition = localPosition;
        }
    }

    private void ClearFootPlants()
    {
        leftHasPlant = false;
        rightHasPlant = false;
        leftStepTargetValid = false;
        rightStepTargetValid = false;
        standingFeetReady = false;
        turningFoot = -1;
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

    private void DriveMovement(Vector3 move)
    {
        Vector3 normal = IsGrounded && hasSupportSurface ? supportHit.normal.normalized : Vector3.up;
        Rigidbody supportBody = IsGrounded && hasSupportSurface ? supportHit.rigidbody : null;
        Vector3 platformVelocity = IsGrounded && hasSupportSurface
            ? GetSupportVelocityAtPoint(pelvisBody.position) : Vector3.zero;
        if (IsGrounded)
        {
            airborneInheritedVelocity = Vector3.zero;
            hasAirborneInheritedVelocity = false;
        }
        Vector3 movementReferenceVelocity = IsGrounded
            ? platformVelocity
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
        else
        {
            trackedSupportBody = null;
            trackedSupportVelocity = Vector3.zero;
            supportVelocityReady = false;
        }
        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            pelvisBody.linearVelocity - movementReferenceVelocity, normal);
        float control = IsGrounded ? 1f : 0.28f;
        Vector3 direction = Vector3.ProjectOnPlane(move, normal).normalized;
        Vector3 targetVelocity = direction * Mathf.Clamp01(move.magnitude) * ActiveMoveSpeed;
        Vector3 velocityError = targetVelocity - planarVelocity;
        float maximumAcceleration = acceleration * (runInput ? runAccelerationMultiplier : 1f);
        // Cancel sustained drag/contact resistance without increasing the speed
        // target. Anti-windup prevents a stored launch after pushing a wall.
        if (!IsGrounded || move.sqrMagnitude < .001f || Vector3.Dot(previousMoveDirection, direction) < .5f)
            movementIntegral = Vector3.zero;
        previousMoveDirection = direction;
        Vector3 proposedIntegral = Vector3.ClampMagnitude(movementIntegral + velocityError * Time.fixedDeltaTime * 12f,
            maximumAcceleration * .65f);
        if ((velocityError * 10f + proposedIntegral).magnitude < maximumAcceleration)
            movementIntegral = proposedIntegral;
        Vector3 driveAcceleration = Vector3.ClampMagnitude(velocityError * 10f + movementIntegral, maximumAcceleration) * control;
        // A dynamic floor can accelerate beneath the player faster than contact
        // friction can move an articulated ragdoll. Feed that platform
        // acceleration to every body so walking in vehicles stays stable.
        driveAcceleration += platformAcceleration;
        // Drive the whole articulated mass, not just the pelvis dragging twelve
        // damped limbs. Contacts and joint constraints still run in the solver.
        ApplyMovementAcceleration(pelvisBody, driveAcceleration, normal);
        for (int i = 0; i < muscles.Count; i++)
            ApplyMovementAcceleration(muscles[i].body, driveAcceleration, normal);
    }

    private float ActiveMoveSpeed => Mathf.Max(0f, runInput ? runSpeed : walkSpeed);

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
            // Start the authored top-out clip on its first frame. A crossfade
            // consumed part of this short clip and made it finish early.
            movementAnimator.Play(requested, externalClimbLayer, 0f);
            movementAnimator.Update(0f);
        }
        else movementAnimator.CrossFade(requested, 0.12f, externalClimbLayer, 0f);
    }

    private void EndExternalClimbAnimation()
    {
        if (movementAnimator != null && externalClimbLayer >= 0)
            movementAnimator.SetLayerWeight(externalClimbLayer, 0f);
        externalClimbAnimatorActive = false;
        externalClimbLayer = -1;
        externalClimbState = 0;
    }

    public void SetExternalClimbPose(Vector3 pelvisPosition, Vector3 facing)
    {
        if (!externalMotionControl || pelvisBody == null) return;
        Vector3 flatFacing = Vector3.ProjectOnPlane(facing, Vector3.up);
        if (flatFacing.sqrMagnitude < 0.001f) flatFacing = GetCurrentBodyFacing();
        flatFacing.Normalize();
        float yaw = Vector3.SignedAngle(restForward, flatFacing, Vector3.up);
        Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * balanceRestWorld;
        pelvisBody.position = pelvisPosition;
        pelvisBody.rotation = rotation;
        pelvisBody.transform.SetPositionAndRotation(pelvisPosition, rotation);
        desiredFacing = flatFacing;
        movementFacingWorld = rotation;
        uprightTargetWorld = rotation;
    }

    public void SetExternalClimbRenderPose(Vector3 pelvisPosition, Vector3 facing)
    {
        if (!externalMotionControl || pelvisBody == null) return;
        externalRenderPelvisPosition = pelvisPosition;
        externalRenderPoseValid = true;
        Vector3 flatFacing = Vector3.ProjectOnPlane(facing, Vector3.up);
        if (flatFacing.sqrMagnitude < 0.001f) flatFacing = GetCurrentBodyFacing();
        flatFacing.Normalize();
        float yaw = Vector3.SignedAngle(restForward, flatFacing, Vector3.up);
        Quaternion renderedFacing = Quaternion.AngleAxis(yaw, Vector3.up) * balanceRestWorld;
        if (externalClimbAnimatorActive) AnchorAnimatedVisualAt(pelvisPosition, renderedFacing);
        else ApplyKinematicVisualPose(pelvisPosition, renderedFacing);
    }

    public void FinishExternalClimbControl(
        Vector3 pelvisPosition, Vector3 facing, Vector3 inheritedVelocity)
    {
        if (!externalMotionControl || pelvisBody == null) return;
        SetExternalClimbPose(pelvisPosition, facing);
        externalMotionControl = false;
        externalRenderPoseValid = false;
        EndExternalClimbAnimation();

        ResetProceduralPoseOffsets();
        ApplyExternalControlVisualPose();
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
            for (int i = 0; i < muscles.Count; i++)
            {
                if (muscles[i].body == null) continue;
                muscles[i].body.linearVelocity = inheritedVelocity;
                muscles[i].body.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            RestoreAnimatedPose(animatorWasEnabledBeforeExternalControl);
        }

        jumpSupportCooldown = 0f;
        headSmoothingReady = false;
        Physics.SyncTransforms();
    }

    public bool EnterVehicleSeat(Transform seatAnchor, Rigidbody vehicleBody)
    {
        if (seatAnchor == null || pelvisBody == null || IsVehicleSeated) return false;

        animatorWasEnabledBeforeSeat = movementAnimator != null && movementAnimator.enabled;
        if (movementAnimator != null) movementAnimator.enabled = false;
        if (weaponController != null)
        {
            weaponWasEnabledBeforeSeat = weaponController.enabled;
            weaponController.PrepareForVehicleSeat();
            weaponController.enabled = false;
        }
        ResetUpperBodyForVehicleSeat();
        SynchronizePhysicsLimbsToVisuals();
        // A seated character is carried by the seat anchor, not by joint
        // forces. Kinematic limb proxies prevent the skeleton from compressing
        // or vibrating while the vehicle suspension moves beneath it.
        ConfigureLimbSimulation(false);

        vehicleSeatAnchor = seatAnchor;
        seatedVehicleBody = vehicleBody;
        vehicleDrivingPose = false;
        Vector3 oldPelvisPosition = pelvisBody.position;
        Quaternion uprightSeatRotation = GetUprightVehicleSeatRotation();
        vehicleSeatRotationOffset = Quaternion.Inverse(seatAnchor.rotation) * uprightSeatRotation;
        Quaternion targetRotation = GetVehicleSeatRotation();
        Quaternion rotationDelta = targetRotation * Quaternion.Inverse(pelvisBody.rotation);

        for (int i = 0; i < muscles.Count; i++)
        {
            Rigidbody limb = muscles[i].body;
            if (limb == null) continue;
            limb.position = seatAnchor.position + rotationDelta * (limb.position - oldPelvisPosition);
            limb.rotation = rotationDelta * limb.rotation;
            limb.linearVelocity = Vector3.zero;
            limb.angularVelocity = Vector3.zero;
            limb.useGravity = false;
            limb.detectCollisions = false;
            Collider limbCollider = limb.GetComponent<Collider>();
            if (limbCollider != null) limbCollider.enabled = false;
        }

        pelvisBody.position = seatAnchor.position;
        pelvisBody.rotation = targetRotation;
        pelvisBody.linearVelocity = Vector3.zero;
        pelvisBody.angularVelocity = Vector3.zero;
        pelvisBody.useGravity = false;
        pelvisBody.detectCollisions = false;
        pelvisInterpolationBeforeSeat = pelvisBody.interpolation;
        pelvisBody.interpolation = RigidbodyInterpolation.None;
        pelvisBody.isKinematic = true;

        ClearFootPlants();
        bodyBobOffset = 0f;
        bodyBobVelocity = 0f;
        IsGrounded = false;
        hasSupportSurface = false;
        ArmsReserved = false;
        SupportHandReserved = false;
        // Rigidbody interpolation otherwise exposes the pre-seat transforms to
        // the first steering-hand IK pass, making the arms grip empty space.
        Physics.SyncTransforms();
        return true;
    }

    public void SetVehicleDrivingPose(bool driving)
    {
        if (IsVehicleSeated) vehicleDrivingPose = driving;
    }

    public bool SwitchVehicleSeat(Transform newSeatAnchor, Rigidbody vehicleBody)
    {
        if (!IsVehicleSeated || newSeatAnchor == null || pelvisBody == null) return false;

        Vector3 oldPelvisPosition = pelvisBody.position;
        Quaternion oldPelvisRotation = pelvisBody.rotation;
        vehicleSeatAnchor = newSeatAnchor;
        seatedVehicleBody = vehicleBody;
        vehicleDrivingPose = false;

        Quaternion uprightSeatRotation = GetUprightVehicleSeatRotation();
        vehicleSeatRotationOffset = Quaternion.Inverse(newSeatAnchor.rotation) * uprightSeatRotation;
        Quaternion targetRotation = GetVehicleSeatRotation();
        Quaternion rotationDelta = targetRotation * Quaternion.Inverse(oldPelvisRotation);

        for (int i = 0; i < muscles.Count; i++)
        {
            Rigidbody limb = muscles[i].body;
            if (limb == null) continue;
            limb.position = newSeatAnchor.position + rotationDelta * (limb.position - oldPelvisPosition);
            limb.rotation = rotationDelta * limb.rotation;
            limb.linearVelocity = Vector3.zero;
            limb.angularVelocity = Vector3.zero;
        }

        pelvisBody.position = newSeatAnchor.position;
        pelvisBody.rotation = targetRotation;
        pelvisBody.transform.SetPositionAndRotation(newSeatAnchor.position, targetRotation);
        pelvisBody.linearVelocity = Vector3.zero;
        pelvisBody.angularVelocity = Vector3.zero;
        desiredFacing = Vector3.ProjectOnPlane(newSeatAnchor.forward, Vector3.up).normalized;
        movementFacingWorld = targetRotation;
        uprightTargetWorld = targetRotation;
        Physics.SyncTransforms();
        return true;
    }

    public void ExitVehicleSeat(Vector3 standPosition, Quaternion standRotation)
    {
        ExitVehicleSeat(standPosition, standRotation, Vector3.zero);
    }

    public void ExitVehicleSeat(
        Vector3 standPosition, Quaternion standRotation, Vector3 additionalVelocity)
    {
        if (!IsVehicleSeated || pelvisBody == null) return;

        Vector3 oldPelvisPosition = pelvisBody.position;
        Vector3 standForward = Vector3.ProjectOnPlane(standRotation * Vector3.forward, Vector3.up);
        if (standForward.sqrMagnitude < 0.01f) standForward = GetCurrentBodyFacing();
        standForward.Normalize();
        float yaw = Vector3.SignedAngle(restForward, standForward, Vector3.up);
        Quaternion targetRotation = Quaternion.AngleAxis(yaw, Vector3.up) * balanceRestWorld;
        Quaternion rotationDelta = targetRotation * Quaternion.Inverse(pelvisBody.rotation);
        Vector3 inheritedVelocity = seatedVehicleBody != null
            ? seatedVehicleBody.GetPointVelocity(standPosition)
            : Vector3.zero;
        inheritedVelocity += additionalVelocity;

        for (int i = 0; i < muscles.Count; i++)
        {
            Rigidbody limb = muscles[i].body;
            if (limb == null) continue;
            limb.position = standPosition + rotationDelta * (limb.position - oldPelvisPosition);
            limb.rotation = rotationDelta * limb.rotation;
            limb.useGravity = true;
            limb.detectCollisions = true;
            limb.linearVelocity = inheritedVelocity;
            limb.angularVelocity = Vector3.zero;
        }

        // Move the still-kinematic core before restoring simulation. With
        // interpolation enabled, releasing it first leaves Transform at the
        // old seat pose for a frame, which visually drops the player into the
        // cabin and can begin the next contact step from an overlap.
        pelvisBody.position = standPosition;
        pelvisBody.rotation = targetRotation;
        pelvisBody.transform.SetPositionAndRotation(standPosition, targetRotation);
        pelvisBody.useGravity = true;
        pelvisBody.detectCollisions = true;
        pelvisBody.interpolation = pelvisInterpolationBeforeSeat;
        pelvisBody.isKinematic = false;
        pelvisBody.linearVelocity = inheritedVelocity;
        pelvisBody.angularVelocity = Vector3.zero;
        airborneInheritedVelocity = inheritedVelocity;
        hasAirborneInheritedVelocity = inheritedVelocity.sqrMagnitude > 0.0001f;
        movementFacingWorld = targetRotation;
        uprightTargetWorld = targetRotation;
        desiredFacing = standForward;

        vehicleSeatAnchor = null;
        seatedVehicleBody = null;
        vehicleDrivingPose = false;
        ResetProceduralPoseOffsets();
        if (dynamicPoseActive)
        {
            SynchronizePhysicsLimbsToVisuals();
            ConfigureLimbSimulation(true);
            for (int i = 0; i < muscles.Count; i++)
            {
                if (muscles[i].body == null) continue;
                muscles[i].body.linearVelocity = inheritedVelocity;
                muscles[i].body.angularVelocity = Vector3.zero;
            }
        }
        else RestoreAnimatedPose(animatorWasEnabledBeforeSeat);
        // Leaving a seat is not a jump. Ground support must be available on the
        // first FixedUpdate or a moving cabin can pass beneath the player.
        jumpSupportCooldown = 0f;
        if (weaponController != null) weaponController.enabled = weaponWasEnabledBeforeSeat;
        Physics.SyncTransforms();
    }

    private void UpdateVehicleSeatPose()
    {
        if (vehicleSeatAnchor == null)
        {
            ExitVehicleSeat(pelvisBody.position, pelvisBody.rotation);
            return;
        }

        Vector3 seatForward = Vector3.ProjectOnPlane(vehicleSeatAnchor.forward, Vector3.up);
        if (seatForward.sqrMagnitude < 0.01f) seatForward = restForward;
        desiredFacing = seatForward.normalized;
        Quaternion targetRotation = GetVehicleSeatRotation();

        movementFacingWorld = targetRotation;
        uprightTargetWorld = movementFacingWorld;
        pelvisBody.position = vehicleSeatAnchor.position;
        pelvisBody.rotation = movementFacingWorld;
        pelvisBody.transform.SetPositionAndRotation(vehicleSeatAnchor.position, movementFacingWorld);

        SetPose(leftThigh, seatedThighEuler);
        SetPose(rightThigh, seatedThighEuler);
        SetPose(leftShin, seatedShinEuler);
        SetPose(rightShin, seatedShinEuler);
        SetPose(leftFoot, seatedFootEuler);
        SetPose(rightFoot, seatedFootEuler);
        Vector3 leftArmPose = vehicleDrivingPose
            ? new Vector3(drivingArmEuler.x, -drivingArmEuler.y, drivingArmEuler.z)
            : new Vector3(seatedArmEuler.x, -seatedArmEuler.y, seatedArmEuler.z);
        Vector3 rightArmPose = vehicleDrivingPose
            ? new Vector3(drivingArmEuler.x, drivingArmEuler.y, -drivingArmEuler.z)
            : new Vector3(seatedArmEuler.x, seatedArmEuler.y, -seatedArmEuler.z);
        SetPose(leftArm, leftArmPose);
        SetPose(rightArm, rightArmPose);
        SetPose(leftForearm, vehicleDrivingPose ? new Vector3(0f, 0f, 52f) : new Vector3(0f, 0f, 28f));
        SetPose(rightForearm, vehicleDrivingPose ? new Vector3(0f, 0f, -52f) : new Vector3(0f, 0f, -28f));
        SetPose(leftHand, Vector3.zero);
        SetPose(rightHand, Vector3.zero);
    }

    private void ResetUpperBodyForVehicleSeat()
    {
        ArmsReserved = false;
        SupportHandReserved = false;
        SetPose(leftArm, Vector3.zero);
        SetPose(rightArm, Vector3.zero);
        SetPose(leftForearm, Vector3.zero);
        SetPose(rightForearm, Vector3.zero);
        SetPose(leftHand, Vector3.zero);
        SetPose(rightHand, Vector3.zero);

        // Restore the authored body-relative arm pose immediately. Waiting for
        // muscle torque is too late because SynchronizePhysicsLimbsToVisuals
        // runs during this same seat-entry frame.
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.visualBone == null || IsLegBone(muscle.visualBone)) continue;
            muscle.visualBone.rotation = movementFacingWorld * muscle.restRelativeToPelvis *
                                         muscle.visualRotationOffset;
        }
    }

    private void ConfigureSeatedJointLimits()
    {
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.body == null || muscle.visualBone == null) continue;
            string boneName = muscle.visualBone.name.ToLowerInvariant();
            if (!boneName.StartsWith("thigh") && !boneName.StartsWith("shin")) continue;
            ConfigurableJoint joint = muscle.body.GetComponent<ConfigurableJoint>();
            if (joint == null) continue;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -115f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 115f };
            joint.angularYLimit = new SoftJointLimit { limit = 18f };
            joint.angularZLimit = new SoftJointLimit { limit = 18f };
        }
    }

    private Quaternion GetVehicleSeatRotation()
    {
        return vehicleSeatAnchor != null
            ? vehicleSeatAnchor.rotation * vehicleSeatRotationOffset
            : movementFacingWorld;
    }

    private Quaternion GetUprightVehicleSeatRotation()
    {
        Vector3 seatForward = vehicleSeatAnchor != null
            ? Vector3.ProjectOnPlane(vehicleSeatAnchor.forward, Vector3.up)
            : restForward;
        if (seatForward.sqrMagnitude < 0.01f) seatForward = restForward;
        float yaw = Vector3.SignedAngle(restForward, seatForward.normalized, Vector3.up);
        return Quaternion.AngleAxis(yaw, Vector3.up) * balanceRestWorld;
    }

    private static void ApplyMovementAcceleration(Rigidbody body, Vector3 acceleration, Vector3 normal)
    {
        if (body == null || body.isKinematic) return;
        Vector3 dragCompensation = Vector3.ProjectOnPlane(body.linearVelocity, normal) * body.linearDamping;
        body.AddForce(acceleration + dragCompensation, ForceMode.Acceleration);
    }

    private void DriveLegSupport()
    {
        if (!IsGrounded || !hasSupportSurface || pelvisBody == null) return;

        Vector3 normal = supportHit.normal.normalized;
        float currentHeight = Vector3.Dot(pelvisBody.position - supportHit.point, normal);
        float targetHeight = standingPelvisHeight - standingHeightReduction + bodyBobOffset;
        float heightError = targetHeight - currentHeight;
        Vector3 surfaceVelocity = supportHit.rigidbody != null
            ? supportHit.rigidbody.GetPointVelocity(pelvisBody.position)
            : Vector3.zero;
        float normalVelocity = Vector3.Dot(pelvisBody.linearVelocity - surfaceVelocity, normal);

        // This force supports the mass of the full ragdoll, then behaves like a
        // damped leg spring. It is ordinary physics and works on any collider.
        float supportedMass = dynamicPoseActive ? totalRagdollMass : pelvisBody.mass;
        float gravityCompensation = Mathf.Abs(Physics.gravity.y) *
                                    Mathf.Max(1f, supportedMass / Mathf.Max(0.1f, pelvisBody.mass));
        // Follow the bob's target velocity as well as its height. A height-only
        // spring damped almost the entire small cycle before it reached the body.
        float supportAcceleration = gravityCompensation + heightError * 135f +
                                    (bodyBobVelocity - normalVelocity) * 24f;
        supportAcceleration = Mathf.Clamp(supportAcceleration, 0f, 108f);
        pelvisBody.AddForce(normal * supportAcceleration, ForceMode.Acceleration);
    }

    private void UpdateProceduralPose(Vector3 move)
    {
        // Keep the last coherent leg pose in the air. Snapping both legs back
        // to their rest pose on takeoff produces a strong whole-body twitch.
        if (!IsGrounded)
        {
            float previousBob = bodyBobOffset;
            bodyBobOffset = Mathf.MoveTowards(bodyBobOffset, 0f, Time.fixedDeltaTime * 0.3f);
            bodyBobVelocity = (bodyBobOffset - previousBob) / Mathf.Max(0.001f, Time.fixedDeltaTime);
            return;
        }

        // Input drives the gait immediately. Using velocity alone made a fallen
        // ragdoll wait for its body to slide before the legs began stepping.
        float input01 = Mathf.Clamp01(move.magnitude);
        float activeMoveSpeed = ActiveMoveSpeed;
        float velocity01 = Mathf.Clamp01(Vector3.ProjectOnPlane(pelvisBody.linearVelocity, Vector3.up).magnitude /
                                         Mathf.Max(0.1f, activeMoveSpeed));
        float speed01 = Mathf.Max(input01, velocity01 * 0.65f);
        if (speed01 > 0.02f)
        {
            float gaitCyclesPerSecond;
            if (matchGaitToMovementSpeed)
            {
                // A complete cycle contains two steps. Deriving cadence from
                // velocity and the same stride used by foot IK prevents skating
                // when switching between walking and running.
                float stride = CalculateActiveStride();
                float requestedSpeed = GaitTravelSpeed;
                gaitCyclesPerSecond = requestedSpeed / Mathf.Max(0.2f, stride * 2f) * gaitSpeedMatch;
                gaitCyclesPerSecond = Mathf.Max(0.2f, gaitCyclesPerSecond);
            }
            else
            {
                float manualCadence = runInput ? runCadenceMultiplier : 1f;
                gaitCyclesPerSecond = stepFrequency * gaitTempoMultiplier * manualCadence *
                                      Mathf.Lerp(0.75f, 1.2f, speed01);
            }

            // Keep at least three physics samples per half-step. Beyond this
            // limit short legs cannot produce a resolved, believable gait.
            currentGaitFrequency = Mathf.Min(gaitCyclesPerSecond, 1f / (6f * Time.fixedDeltaTime));
            gaitPhase += Time.fixedDeltaTime * currentGaitFrequency * Mathf.PI * 2f;
        }

        // At the start/end of every half-step the rear foot lifts, so the body
        // is at its low point. It rises while the planted foot travels from the
        // front toward the back, then lowers into the next rear-foot lift.
        float halfStepProgress = Mathf.Repeat(gaitPhase / Mathf.PI, 1f);
        float stanceWave = Mathf.SmoothStep(0f, 1f, Mathf.Sin(halfStepProgress * Mathf.PI));
        float bobScale = runInput ? runBobMultiplier : 1f;
        // A full-height bob at sprint cadence injected large vertical impulses.
        float cadenceBobScale = 1f / Mathf.Max(1f, currentGaitFrequency * currentGaitFrequency / 4f);
        float desiredBob = speed01 > 0.02f ? stanceWave * bodyBobHeight * bobScale * speed01 * cadenceBobScale : 0f;
        float previousBodyBob = bodyBobOffset;
        bodyBobOffset = speed01 > 0.02f
            ? desiredBob
            : Mathf.MoveTowards(bodyBobOffset, 0f, Time.fixedDeltaTime * 0.22f);
        bodyBobVelocity = (bodyBobOffset - previousBodyBob) /
                          Mathf.Max(0.001f, Time.fixedDeltaTime);
        float leftWave = Mathf.Sin(gaitPhase);
        float rightWave = -leftWave;
        float wobble = Mathf.Sin(Time.time * 3.1f) * idleWobble + Mathf.Sin(Time.time * 5.7f) * idleWobble * 0.35f;

        // A real two-phase gait: one leg lifts, bends and travels forward while
        // the other stays nearly straight and moves back through its stance.
        float poseScale = runInput ? runStrideMultiplier : 1f;
        ApplyProceduralLegPose(leftThigh, leftShin, leftFoot, gaitPhase, speed01, poseScale);
        ApplyProceduralLegPose(rightThigh, rightShin, rightFoot, gaitPhase + Mathf.PI, speed01, poseScale);
        if (!ArmsReserved || !SupportHandReserved)
        {
            SetPose(leftArm, new Vector3(rightWave * armSwing * speed01, 0f, wobble));
            SetPose(leftForearm, new Vector3(0f, 0f, 8f + Mathf.Abs(rightWave) * 14f * speed01));
        }
        else
        {
            SetPose(leftArm, Vector3.zero);
            SetPose(leftForearm, Vector3.zero);
            SetPose(leftHand, Vector3.zero);
        }
        if (!ArmsReserved)
        {
            SetPose(rightArm, new Vector3(leftWave * armSwing * speed01, 0f, -wobble));
            SetPose(rightForearm, new Vector3(0f, 0f, -8f - Mathf.Abs(leftWave) * 14f * speed01));
        }
        else
        {
            SetPose(rightArm, Vector3.zero);
            SetPose(rightForearm, Vector3.zero);
            SetPose(rightHand, Vector3.zero);
        }
    }

    private void ApplyProceduralLegPose(
        Transform thigh,
        Transform shin,
        Transform foot,
        float phase,
        float speed01,
        float poseScale)
    {
        float wrapped = Mathf.Repeat(phase, Mathf.PI * 2f);
        bool swing = wrapped < Mathf.PI;
        float progress = swing ? wrapped / Mathf.PI : (wrapped - Mathf.PI) / Mathf.PI;
        float thighAngle;
        float kneeAngle;
        float footAngle;

        if (swing)
        {
            thighAngle = Mathf.Lerp(-legSwing * 0.45f, legSwing * 0.8f, progress);
            kneeAngle = 8f + Mathf.Sin(progress * Mathf.PI) * Mathf.Max(kneeBend, 52f);
            footAngle = -kneeAngle * 0.5f;
        }
        else
        {
            thighAngle = Mathf.Lerp(legSwing * 0.45f, -legSwing * 0.5f, progress);
            kneeAngle = Mathf.Sin(progress * Mathf.PI) * 7f;
            footAngle = -kneeAngle * 0.25f;
        }

        SetPose(thigh, new Vector3(thighAngle * speed01 * poseScale, 0f, 0f));
        SetPose(shin, new Vector3(kneeAngle * speed01 * poseScale, 0f, 0f));
        SetPose(foot, new Vector3(footAngle * speed01, 0f, 0f));
    }

    private void DriveMuscles()
    {
        // Impacts can loosen the pose briefly, but never turn the controller
        // into a limp ragdoll that keeps folding into itself.
        float flopScale = flopTimer > 0f ? 0.72f : 1f;
        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            if (muscle.body == null) continue;
            // Target the upright reference, not the current pelvis rotation. If
            // the tilted pelvis is used here, every limb faithfully follows the
            // fall and the controller behaves like a supported dead body.
            Quaternion desired = uprightTargetWorld * muscle.restRelativeToPelvis * Quaternion.Euler(muscle.proceduralEuler);
            float activeStrength = Mathf.Max(muscle.strength * 6.5f, 240f) * flopScale;
            float dampingMultiplier = IsLegBone(muscle.visualBone) ? legDampingMultiplier : 3.4f;
            ApplyPdTorque(muscle.body, desired, activeStrength,
                Mathf.Max(muscleDamping * dampingMultiplier, 34f));
        }
    }

    private void UpdateFacingFromMovement(Vector3 move)
    {
        bool aiming = weaponController != null && weaponController.IsAiming;
        Vector3 aimForward = aiming ? weaponController.AimForward : Vector3.zero;
        Vector3 flatAim = Vector3.ProjectOnPlane(aimForward, Vector3.up);
        if ((aiming && flatAim.sqrMagnitude > 0.001f) || (!aiming && move.sqrMagnitude >= 0.02f))
        {
            Quaternion previousFacing = movementFacingWorld;
            if (aiming)
            {
                float turn = Vector3.SignedAngle(GetCurrentBodyFacing(), flatAim.normalized, Vector3.up);
                turn = Mathf.Clamp(turn, -aimTurnSpeed * Time.fixedDeltaTime, aimTurnSpeed * Time.fixedDeltaTime);
                desiredFacing = Quaternion.AngleAxis(turn, Vector3.up) * GetCurrentBodyFacing();
            }
            else desiredFacing = move.normalized;
            float yaw = Vector3.SignedAngle(restForward, desiredFacing, Vector3.up);
            movementFacingWorld = Quaternion.AngleAxis(yaw, Vector3.up) * balanceRestWorld;

            // Turn the physical limbs by the same world-space delta as the
            // pelvis. Without this, both thigh bodies remain in their previous
            // heading and can only catch up through the hip joint limits.
            Quaternion facingDelta = movementFacingWorld * Quaternion.Inverse(previousFacing);
            float facingChange = Quaternion.Angle(Quaternion.identity, facingDelta);
            if (facingChange > 0.01f)
                RotatePhysicalRigAroundPelvis(facingDelta);
            if (move.sqrMagnitude >= 0.02f && facingChange >= footReplantTurnAngle)
            {
                leftHasPlant = false;
                rightHasPlant = false;
                leftStepTargetValid = false;
                rightStepTargetValid = false;
            }
        }

        pelvisBody.MoveRotation(movementFacingWorld);
        Vector3 angular = pelvisBody.angularVelocity;
        angular.y = 0f;
        pelvisBody.angularVelocity = angular;
    }

    private void UpdateUprightTarget()
    {
        uprightTargetWorld = movementFacingWorld;
    }

    private Quaternion GetCameraLookTarget()
    {
        // The Animator pose already inherits the visual root's authored-axis
        // correction. Use that evaluated pose directly so the idle head is not
        // flipped a second time before adding the smooth camera-look delta.
        Quaternion restTarget = !dynamicPoseActive && !IsVehicleSeated &&
                                movementAnimator != null && movementAnimator.enabled
            ? head.rotation
            : uprightTargetWorld * headRestRelativeToPelvis;
        Camera view = Camera.main;
        if (view == null) return restTarget;

        Vector3 bodyFacing = GetCurrentBodyFacing();
        Vector3 toCamera = view.transform.position - head.position;
        float cameraDistance = toCamera.magnitude;
        Vector3 cameraForward = view.transform.forward.normalized;
        Vector3 horizontalToCamera = Vector3.ProjectOnPlane(toCamera, Vector3.up).normalized;

        // Blend continuously between following camera aim from behind and looking
        // toward the camera from the front. This removes the old binary snap.
        Vector3 flatAim = Vector3.ProjectOnPlane(cameraForward, Vector3.up).normalized;
        if (flatAim.sqrMagnitude < 0.01f) flatAim = bodyFacing;
        Vector3 flatTowardCamera = horizontalToCamera.sqrMagnitude > 0.01f
            ? horizontalToCamera
            : bodyFacing;
        float frontDot = Vector3.Dot(flatTowardCamera, bodyFacing);
        float frontBlend = (weaponController == null || !weaponController.IsAiming) && cameraDistance > 0.7f
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.35f, frontDot))
            : 0f;

        float aimYaw = Mathf.Clamp(Vector3.SignedAngle(bodyFacing, flatAim, Vector3.up), -80f, 80f);
        float cameraYaw = Mathf.Clamp(Vector3.SignedAngle(bodyFacing, flatTowardCamera, Vector3.up), -80f, 80f);
        float yaw = Mathf.LerpAngle(aimYaw, cameraYaw, frontBlend);
        float aimPitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(cameraForward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        float cameraPitch = cameraDistance > 0.001f
            ? Mathf.Asin(Mathf.Clamp(Vector3.Dot(toCamera.normalized, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg
            : aimPitch;
        float pitch = Mathf.Clamp(Mathf.LerpAngle(aimPitch, cameraPitch, frontBlend), -40f, 50f);

        Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyFacing).normalized;
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, Vector3.up);
        Quaternion pitchRotation = Quaternion.AngleAxis(-pitch, yawRotation * bodyRight);
        return pitchRotation * yawRotation * restTarget;
    }

    private Vector3 GetCurrentBodyFacing()
    {
        return desiredFacing.sqrMagnitude > 0.01f ? desiredFacing : restForward;
    }

    private void PreventFeetFromEnteringSupport()
    {
        bool leftContact = ProtectFootFromGround(leftFoot, out RaycastHit leftHit);
        bool rightContact = ProtectFootFromGround(rightFoot, out RaycastHit rightHit);
        if (jumpSupportCooldown > 0f || (!leftContact && !rightContact)) return;

        // A foot contact is a more reliable landing signal than pelvis speed.
        // Start supporting the body immediately instead of letting the pelvis
        // keep falling and drive the legs through the surface.
        RaycastHit footSupport = leftContact ? leftHit : rightHit;
        if (!hasSupportSurface || !IsGrounded)
        {
            supportHit = footSupport;
            hasSupportSurface = true;
            IsGrounded = true;
        }
    }

    private bool ProtectFootFromGround(Transform visualFoot, out RaycastHit groundHit)
    {
        groundHit = default;
        if (visualFoot == null || !bodyByVisualBone.TryGetValue(visualFoot, out Rigidbody footBody))
            return false;

        Vector3 castOrigin = footBody.position + Vector3.up * 0.36f;
        RaycastHit[] hits = Physics.SphereCastAll(
            castOrigin, 0.025f, Vector3.down, 0.78f, ~0, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || hits[i].normal.y < 0.3f) continue;
            if (physicsRoot != null && candidate.transform.IsChildOf(physicsRoot)) continue;
            if (candidate.transform.IsChildOf(transform)) continue;
            if (hits[i].distance >= closest) continue;
            closest = hits[i].distance;
            groundHit = hits[i];
            found = true;
        }
        if (!found) return false;

        Vector3 normal = groundHit.normal.normalized;
        CapsuleCollider capsule = footBody.GetComponent<CapsuleCollider>();
        float requiredHeight = CalculateFootCenterClearance(capsule, normal, 0.008f);
        float currentHeight = Vector3.Dot(footBody.position - groundHit.point, normal);
        Vector3 surfaceVelocity = groundHit.rigidbody != null
            ? groundHit.rigidbody.GetPointVelocity(footBody.position)
            : Vector3.zero;
        float normalSpeed = Vector3.Dot(footBody.linearVelocity - surfaceVelocity, normal);
        float predictedHeight = currentHeight + normalSpeed * Time.fixedDeltaTime;

        if (currentHeight < requiredHeight)
        {
            footBody.position += normal * (requiredHeight - currentHeight);
            currentHeight = requiredHeight;
        }

        // Limit downward velocity before the next physics step can cross the
        // contact plane. Continuous collision remains enabled as a second guard.
        if (predictedHeight < requiredHeight)
        {
            float minimumNormalSpeed = (requiredHeight - currentHeight) /
                                       Mathf.Max(0.001f, Time.fixedDeltaTime);
            if (normalSpeed < minimumNormalSpeed)
                footBody.linearVelocity += normal * (minimumNormalSpeed - normalSpeed);
        }

        return currentHeight <= requiredHeight + 0.035f || predictedHeight <= requiredHeight + 0.01f;
    }

    private static float CalculateFootCenterClearance(
        CapsuleCollider capsule,
        Vector3 surfaceNormal,
        float skin)
    {
        if (capsule == null) return 0.055f + skin;

        Vector3 scale = capsule.transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 localAxis = capsule.direction == 0 ? Vector3.right :
                            capsule.direction == 1 ? Vector3.up : Vector3.forward;
        float axisScale = capsule.direction == 0 ? scale.x :
                          capsule.direction == 1 ? scale.y : scale.z;
        float radiusScale = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z) :
                            capsule.direction == 1 ? Mathf.Max(scale.x, scale.z) :
                            Mathf.Max(scale.x, scale.y);
        float radius = capsule.radius * radiusScale;
        float halfSegment = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
        Vector3 worldAxis = capsule.transform.TransformDirection(localAxis).normalized;
        Vector3 centerOffset = capsule.transform.TransformVector(capsule.center);
        float lowestOffset = Vector3.Dot(centerOffset, surfaceNormal) -
                             Mathf.Abs(Vector3.Dot(worldAxis, surfaceNormal)) * halfSegment - radius;
        return Mathf.Max(0.01f, -lowestOffset + skin);
    }

    private void DrivePhysicalSteps(Vector3 move)
    {
        if (!hasSupportSurface)
        {
            standingFeetReady = false;
            turningFoot = -1;
            leftHasPlant = false;
            rightHasPlant = false;
            leftStepTargetValid = false;
            rightStepTargetValid = false;
            return;
        }

        if (move.sqrMagnitude < 0.02f)
        {
            DriveStandingFeet();
            return;
        }
        if (standingFeetReady)
        {
            // Restart the walking swing from the current turn-step target,
            // not a cached stride offset from the previous walk.
            leftWasSwinging = false;
            rightWasSwinging = false;
        }
        standingFeetReady = false;
        turningFoot = -1;

        Vector3 stepForward = move.normalized;
        ApplyFootStep(leftFoot, gaitPhase, stepForward, leftFootSideOffset,
            ref leftWasSwinging, ref leftHasPlant, ref leftPlantSurface, ref leftPlantLocal,
            ref leftStepTargetWorld, ref leftStepTargetValid, ref leftSwingStartOffset);
        ApplyFootStep(rightFoot, gaitPhase + Mathf.PI, stepForward, rightFootSideOffset,
            ref rightWasSwinging, ref rightHasPlant, ref rightPlantSurface, ref rightPlantLocal,
            ref rightStepTargetWorld, ref rightStepTargetValid, ref rightSwingStartOffset);
    }

    private void DriveStandingFeet()
    {
        if (leftFoot == null || rightFoot == null ||
            !bodyByVisualBone.TryGetValue(leftFoot, out Rigidbody leftBody) ||
            !bodyByVisualBone.TryGetValue(rightFoot, out Rigidbody rightBody)) return;
        Transform surface = supportHit.collider.transform;
        Vector3 normal = supportHit.normal.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(GetCurrentBodyFacing(), normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward).normalized;
        Vector3 center = pelvisBody.position - normal * Vector3.Dot(pelvisBody.position - supportHit.point, normal);
        float leftClearance = CalculateFootCenterClearance(leftBody.GetComponent<CapsuleCollider>(), normal, .008f);
        float rightClearance = CalculateFootCenterClearance(rightBody.GetComponent<CapsuleCollider>(), normal, .008f);
        Vector3 leftRest = center + right * leftFootSideOffset + normal * leftClearance;
        Vector3 rightRest = center + right * rightFootSideOffset + normal * rightClearance;
        if (!standingFeetReady || leftPlantSurface != surface || rightPlantSurface != surface)
        {
            leftPlantLocal = surface.InverseTransformPoint(leftStepTargetValid ? leftStepTargetWorld : leftBody.position);
            rightPlantLocal = surface.InverseTransformPoint(rightStepTargetValid ? rightStepTargetWorld : rightBody.position);
            leftPlantSurface = rightPlantSurface = surface;
            standingFeetReady = true;
            turningFoot = -1;
        }
        Vector3 leftTarget = surface.TransformPoint(leftPlantLocal);
        Vector3 rightTarget = surface.TransformPoint(rightPlantLocal);
        if (turningFoot < 0)
        {
            float leftError = Vector3.ProjectOnPlane(leftRest - leftTarget, normal).magnitude;
            float rightError = Vector3.ProjectOnPlane(rightRest - rightTarget, normal).magnitude;
            bool leftNeedsStep = leftError > turnStepDistance || Vector3.Dot(leftTarget - leftRest, normal) > .025f;
            bool rightNeedsStep = rightError > turnStepDistance || Vector3.Dot(rightTarget - rightRest, normal) > .025f;
            if (leftNeedsStep || rightNeedsStep)
            {
                turningFoot = leftNeedsStep && rightNeedsStep ? 1 - lastTurningFoot : leftNeedsStep ? 0 : 1;
                turnStepProgress = 0f;
                turnStepStartLocal = turningFoot == 0 ? leftPlantLocal : rightPlantLocal;
                turnStepEndLocal = surface.InverseTransformPoint(turningFoot == 0 ? leftRest : rightRest);
            }
        }
        leftWasSwinging = turningFoot == 0;
        rightWasSwinging = turningFoot == 1;
        if (turningFoot >= 0)
        {
            turnStepProgress = Mathf.Min(1f, turnStepProgress + Time.fixedDeltaTime / Mathf.Max(.1f, turnStepDuration));
            Vector3 target = surface.TransformPoint(Vector3.Lerp(turnStepStartLocal, turnStepEndLocal,
                Mathf.SmoothStep(0f, 1f, turnStepProgress))) + normal * (Mathf.Sin(turnStepProgress * Mathf.PI) * turnStepLift);
            if (turningFoot == 0) leftTarget = target; else rightTarget = target;
            if (turnStepProgress >= 1f)
            {
                if (turningFoot == 0) leftPlantLocal = turnStepEndLocal; else rightPlantLocal = turnStepEndLocal;
                lastTurningFoot = turningFoot;
                turningFoot = -1;
            }
        }
        // Only the lifted foot moves. The other remains anchored in the
        // support's local space, including on moving/rotating platforms.
        TrackStandingFoot(leftBody, leftTarget, normal, leftClearance, leftWasSwinging,
            ref leftStepTargetWorld, ref leftStepTargetValid);
        TrackStandingFoot(rightBody, rightTarget, normal, rightClearance, rightWasSwinging,
            ref rightStepTargetWorld, ref rightStepTargetValid);
        leftHasPlant = !leftWasSwinging;
        rightHasPlant = !rightWasSwinging;
    }

    private void TrackStandingFoot(Rigidbody body, Vector3 target, Vector3 normal, float clearance,
        bool swinging, ref Vector3 previousTarget, ref bool valid)
    {
        float height = Vector3.Dot(target - supportHit.point, normal);
        if (height < clearance) target += normal * (clearance - height);
        Vector3 surfaceVelocity = supportHit.rigidbody != null ? supportHit.rigidbody.GetPointVelocity(target) : Vector3.zero;
        Vector3 velocity = swinging && valid ? (target - previousTarget) / Time.fixedDeltaTime : surfaceVelocity;
        Vector3 force = (target - body.position) * footPositionStrength +
                        (Vector3.ClampMagnitude(velocity, 4f) - body.linearVelocity) * footVelocityDamping;
        body.AddForce(Vector3.ClampMagnitude(force, footMaxAcceleration), ForceMode.Acceleration);
        previousTarget = target;
        valid = true;
    }

    private void ApplyFootStep(
        Transform visualFoot,
        float phase,
        Vector3 movementForward,
        float sideOffset,
        ref bool wasSwinging,
        ref bool hasPlant,
        ref Transform plantSurface,
        ref Vector3 plantLocal,
        ref Vector3 stepTargetWorld,
        ref bool stepTargetValid,
        ref Vector3 swingStartOffset)
    {
        if (visualFoot == null || !bodyByVisualBone.TryGetValue(visualFoot, out Rigidbody footBody))
        {
            stepTargetValid = false;
            return;
        }

        float wrapped = Mathf.Repeat(phase, Mathf.PI * 2f);
        bool swinging = wrapped < Mathf.PI;
        float swingProgress = swinging ? wrapped / Mathf.PI : 0f;
        float liftWave = swinging ? Mathf.Sin(swingProgress * Mathf.PI) : 0f;
        // Zero vertical speed at lift-off/touchdown avoids the sharp kick of
        // sin(phase)^0.72, whose slope becomes infinite at both endpoints.
        float lift = liftWave * liftWave;
        Vector3 normal = supportHit.normal.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(movementForward, normal).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(GetCurrentBodyFacing(), normal).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(restForward, normal).normalized;
        Vector3 right = Vector3.Cross(normal, forward).normalized;
        Transform surface = supportHit.collider.transform;
        CapsuleCollider capsule = footBody.GetComponent<CapsuleCollider>();
        float clearance = CalculateFootCenterClearance(capsule, normal, 0.008f);
        // Use the pelvis projection onto the support plane as a stable gait
        // origin. Sphere-cast hit points can jump between triangle edges.
        Vector3 groundCenter = pelvisBody.position -
                               normal * Vector3.Dot(pelvisBody.position - supportHit.point, normal);
        float stride = CalculateActiveStride();
        Vector3 landingOffset = right * sideOffset + forward * (stride * .65f);
        Vector3 landing = groundCenter + landingOffset + normal * clearance;
        Vector3 previousTarget = stepTargetWorld;
        bool hadTarget = stepTargetValid;

        if (swinging && (!wasSwinging || !stepTargetValid || plantSurface != surface))
        {
            Vector3 start = stepTargetValid ? stepTargetWorld : footBody.position;
            Vector3 offset = Vector3.ProjectOnPlane(start - groundCenter, normal);
            swingStartOffset = right * sideOffset + forward * Mathf.Clamp(Vector3.Dot(offset, forward), -stride * .65f, stride * .5f);
        }

        if (!swinging && (wasSwinging || !hasPlant || plantSurface != surface))
        {
            // Plant the planned landing, not the delayed physical proxy. Using
            // the proxy's position kept restarting steps behind a fast player.
            float stanceProgress = (wrapped - Mathf.PI) / Mathf.PI;
            Vector3 plantedWorld = landing - forward * (stride * stanceProgress);
            plantSurface = surface;
            plantLocal = surface.InverseTransformPoint(plantedWorld);
            hasPlant = true;
        }

        Vector3 target;
        if (swinging)
        {
            float liftHeight = Mathf.Clamp(standingPelvisHeight * stepLiftScale, 0.08f, 0.4f);
            // Interpolate offsets relative to the moving pelvis. World-space
            // low-pass smoothing introduced speed/sharpness metres of lag.
            float progress = Mathf.SmoothStep(0f, 1f, swingProgress);
            target = groundCenter + Vector3.Lerp(swingStartOffset, landingOffset, progress) +
                     normal * (clearance + lift * liftHeight);
        }
        else
        {
            target = plantSurface != null ? plantSurface.TransformPoint(plantLocal) : footBody.position;
        }

        // The swing curve is already smooth, and a planted foot must stay fixed
        // on its surface, not chase a smoothed point moving under the body.
        stepTargetWorld = target;
        stepTargetValid = true;

        // Smoothing must never pull an old airborne target through the current
        // surface on landing. Clamp the physical/IK target to the contact plane.
        float smoothedHeight = Vector3.Dot(stepTargetWorld - supportHit.point, normal);
        if (smoothedHeight < clearance)
            stepTargetWorld += normal * (clearance - smoothedHeight);
        target = stepTargetWorld;

        Vector3 surfaceVelocity = supportHit.rigidbody != null
            ? supportHit.rigidbody.GetPointVelocity(target)
            : Vector3.zero;
        Vector3 targetVelocity = swinging && hadTarget
            ? (target - previousTarget) / Time.fixedDeltaTime : surfaceVelocity;
        float response = Mathf.Clamp(currentGaitFrequency / 2f * Mathf.Sqrt(footTargetSharpness / 12f), 1f, 2f);
        targetVelocity = Vector3.ClampMagnitude(targetVelocity, Mathf.Max(4f, GaitTravelSpeed * 3f));
        Vector3 acceleration = (target - footBody.position) * footPositionStrength * response * response +
                               (targetVelocity - footBody.linearVelocity) * footVelocityDamping * response;
        footBody.AddForce(Vector3.ClampMagnitude(acceleration, footMaxAcceleration * response * response), ForceMode.Acceleration);
        wasSwinging = swinging;
    }

    private float CalculateActiveStride()
    {
        float activeStrideScale = strideScale * (runInput ? runStrideMultiplier : 1f);
        float speedBlend = Mathf.InverseLerp(2f, Mathf.Max(2.1f, fastStrideSpeed), GaitTravelSpeed);
        float reachLimit = Mathf.Min(leftLegIk.thighLength + leftLegIk.shinLength,
            rightLegIk.thighLength + rightLegIk.shinLength) * 1.3f;
        if (reachLimit < .3f) reachLimit = 1.05f;
        return Mathf.Clamp(standingPelvisHeight * activeStrideScale * Mathf.Lerp(1f, fastStrideMultiplier, speedBlend),
            .3f, Mathf.Min(1.4f, reachLimit));
    }

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

    private Vector3 GetSupportVelocityAtPoint(Vector3 worldPoint)
    {
        if (!hasSupportSurface || supportHit.collider == null) return Vector3.zero;
        ClimbableSurface movingSurface = supportHit.collider.GetComponentInParent<ClimbableSurface>();
        if (movingSurface != null) return movingSurface.GetVelocityAtPoint(worldPoint);
        return supportHit.rigidbody != null
            ? supportHit.rigidbody.GetPointVelocity(worldPoint) : Vector3.zero;
    }

    private void ApplyJumpTakeoffVelocity(Rigidbody body, Vector3 jumpDirection,
        Vector3 supportVelocity, Vector3 relativeVelocity)
    {
        if (body == null) return;
        Vector3 normal = jumpDirection.sqrMagnitude > 0.001f ? jumpDirection.normalized : Vector3.up;
        Vector3 inheritedPlanar = Vector3.ProjectOnPlane(supportVelocity, normal);
        Vector3 relativePlanar = Vector3.ProjectOnPlane(relativeVelocity, normal);
        float inheritedNormalSpeed = Vector3.Dot(supportVelocity, normal);
        body.linearVelocity = inheritedPlanar + relativePlanar +
                              normal * (inheritedNormalSpeed + jumpVelocity);
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

    private void UpdateGrounded()
    {
        bool wasGrounded = IsGrounded && hasSupportSurface;
        Vector3 previousSupportVelocity = wasGrounded
            ? GetSupportVelocityAtPoint(pelvisBody.position) : Vector3.zero;
        RaycastHit[] hits = Physics.SphereCastAll(
            pelvisBody.position + Vector3.up * 0.1f, 0.16f, Vector3.down,
            groundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
        IsGrounded = false;
        hasSupportSurface = false;
        if (jumpSupportCooldown > 0f) return;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (physicsRoot != null && hits[i].collider.transform.IsChildOf(physicsRoot)) continue;
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].normal.y < 0.3f) continue;
            if (hits[i].distance >= closestDistance) continue;
            closestDistance = hits[i].distance;
            supportHit = hits[i];
            hasSupportSurface = true;
        }
        if (hasSupportSurface)
        {
            float height = Vector3.Dot(pelvisBody.position - supportHit.point, supportHit.normal);
            float normalSpeed = Vector3.Dot(pelvisBody.linearVelocity, supportHit.normal);
            float groundedHeight = standingPelvisHeight - standingHeightReduction + bodyBobOffset;
            IsGrounded = height <= groundedHeight + 0.16f && normalSpeed <= 1.5f;
        }
        if (!IsGrounded && wasGrounded)
        {
            airborneInheritedVelocity = previousSupportVelocity;
            hasAirborneInheritedVelocity = true;
        }
    }

    public void BuildRagdoll()
    {
        CacheBones();
        if (pelvis == null) return;
        renderedBodyHeight = MeasureRenderedBodyHeight();
        RemoveLegacyBonePhysics();
        CreateOrResetPhysicsRoot();

        muscles.Clear();
        bodyByVisualBone.Clear();
        Quaternion pelvisStartRotation = pelvis.rotation;
        leftHipRestRelativeToPelvis = leftHip != null
            ? Quaternion.Inverse(pelvisStartRotation) * leftHip.rotation
            : Quaternion.identity;
        rightHipRestRelativeToPelvis = rightHip != null
            ? Quaternion.Inverse(pelvisStartRotation) * rightHip.rotation
            : Quaternion.identity;
        leftHipRestPositionInPelvis = leftHip != null
            ? pelvis.InverseTransformPoint(leftHip.position)
            : Vector3.zero;
        rightHipRestPositionInPelvis = rightHip != null
            ? pelvis.InverseTransformPoint(rightHip.position)
            : Vector3.zero;

        // The hip and spine are animation bones, not ragdoll bodies. This one
        // constrained core supplies collision, movement and support without
        // allowing the torso hierarchy itself to bend or twist.
        GameObject coreObject = new GameObject("Physics_Core");
        Transform core = coreObject.transform;
        core.SetParent(physicsRoot, false);
        core.SetPositionAndRotation(pelvis.position, pelvis.rotation);
        pelvisBody = ConfigureBody(core, 12f, true);
        ConfigureCollider(core, pelvis);
        pelvisVisualRotationOffset = Quaternion.Inverse(core.rotation) * pelvis.rotation;
        headRestRelativeToPelvis = head != null
            ? Quaternion.Inverse(pelvisStartRotation) * head.rotation
            : Quaternion.identity;

        for (int i = 0; i < PhysicalBoneNames.Length; i++)
        {
            Transform visual = FindBone(PhysicalBoneNames[i]);
            if (visual == null) continue;
            Transform proxy = GetOrCreateProxy(visual);
            Rigidbody body = ConfigureBody(proxy, MassForBone(visual.name), false);
            ConfigureCollider(proxy, visual);
            bodyByVisualBone[visual] = body;
            muscles.Add(new Muscle
            {
                name = visual.name,
                visualBone = visual,
                physicsBone = proxy,
                body = body,
                restRelativeToPelvis = Quaternion.Inverse(pelvisStartRotation) * visual.rotation,
                visualRotationOffset = Quaternion.Inverse(proxy.rotation) * visual.rotation,
                strength = StrengthForBone(visual.name),
                proceduralEuler = Vector3.zero
            });
        }

        for (int i = 0; i < muscles.Count; i++)
        {
            Muscle muscle = muscles[i];
            Rigidbody connected = FindConnectedBody(muscle.visualBone);
            if (connected != null) ConfigureJoint(muscle.physicsBone, connected, muscle.name);
        }

        IgnoreInternalCollisions();
    }

    private void CreateOrResetPhysicsRoot()
    {
        Transform oldCore = transform.Find("PhysicalCore");
        if (oldCore != null) DestroyObjectSafely(oldCore.gameObject);

        string oldRigName = gameObject.name + "_PhysicsRig";
        GameObject oldExternalRig = GameObject.Find(oldRigName);
        Transform oldNestedRig = transform.Find("Physics");
        if (oldExternalRig != null)
        {
            // Disable immediately because Destroy is deferred during play. A
            // clean replacement prevents obsolete spine bodies from remaining
            // active after the physical-bone layout changes.
            Collider[] staleColliders = oldExternalRig.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < staleColliders.Length; i++) staleColliders[i].enabled = false;
            oldExternalRig.SetActive(false);
            DestroyObjectSafely(oldExternalRig);
        }
        if (oldNestedRig != null)
        {
            Collider[] staleColliders = oldNestedRig.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < staleColliders.Length; i++) staleColliders[i].enabled = false;
            oldNestedRig.gameObject.SetActive(false);
            DestroyObjectSafely(oldNestedRig.gameObject);
        }

        GameObject rigObject = new GameObject("Physics");
        physicsRoot = rigObject.transform;
        physicsRoot.SetParent(transform, false);
        physicsRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        physicsRoot.localScale = Vector3.one;
    }

    private Transform GetOrCreateProxy(Transform visual)
    {
        string proxyName = "Physics_" + visual.name;
        Transform proxy = physicsRoot.Find(proxyName);
        if (proxy == null)
        {
            GameObject proxyObject = new GameObject(proxyName);
            proxy = proxyObject.transform;
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
        // The pelvis is the stable core. Locking only its pitch and roll keeps
        // the character standing while yaw, translation, limbs and impacts all
        // continue to be simulated by Unity physics.
        body.constraints = isPelvis
            ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ
            : RigidbodyConstraints.None;
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
        Vector3 worldEnd;
        if (visual == pelvis && head != null)
            worldEnd = head.position;
        else
            worldEnd = child != null ? child.position : visual.position + visual.up * renderedBodyHeight * 0.09f;
        Vector3 localEnd = Quaternion.Inverse(proxy.rotation) * (worldEnd - visual.position);
        float worldLength = Mathf.Max(renderedBodyHeight * 0.055f, Vector3.Distance(visual.position, worldEnd));
        float worldRadius = visual == pelvis
            ? Mathf.Clamp(renderedBodyHeight * 0.08f, 0.09f, 0.16f)
            : WorldRadiusForBone(visual.name);

        CapsuleCollider capsule = proxy.GetComponent<CapsuleCollider>();
        if (capsule == null) capsule = proxy.gameObject.AddComponent<CapsuleCollider>();
        Vector3 abs = new Vector3(Mathf.Abs(localEnd.x), Mathf.Abs(localEnd.y), Mathf.Abs(localEnd.z));
        capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : (abs.z > abs.y ? 2 : 1);
        capsule.center = localEnd.sqrMagnitude > 0.000001f ? localEnd.normalized * worldLength * 0.46f : Vector3.zero;
        capsule.radius = worldRadius;
        capsule.height = Mathf.Max(worldRadius * 2.05f, worldLength * (visual == pelvis ? 0.92f : 0.82f));
        capsule.isTrigger = false;
        capsule.contactOffset = Mathf.Clamp(worldRadius * 0.15f, 0.005f, 0.015f);
    }

    private static void ConfigureJoint(Transform proxy, Rigidbody connected, string boneName)
    {
        ConfigurableJoint joint = proxy.GetComponent<ConfigurableJoint>();
        if (joint == null) joint = proxy.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = connected;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = connected.transform.InverseTransformPoint(proxy.position);
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        string n = boneName.ToLowerInvariant();
        bool rigidTorsoBone = n.StartsWith("spine") && n != "spine.003";
        joint.angularXMotion = rigidTorsoBone ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
        joint.angularYMotion = rigidTorsoBone
            ? ConfigurableJointMotion.Locked
            : ConfigurableJointMotion.Limited;
        joint.angularZMotion = rigidTorsoBone
            ? ConfigurableJointMotion.Locked
            : ConfigurableJointMotion.Limited;
        float xLimit = 24f;
        float yzLimit = 10f;
        if (n.StartsWith("thigh")) { xLimit = 36f; yzLimit = 12f; }
        else if (n.StartsWith("shin")) { xLimit = n.EndsWith(".001") ? 18f : 48f; yzLimit = n.EndsWith(".001") ? 6f : 5f; }
        else if (n.StartsWith("arm")) { xLimit = 38f; yzLimit = 30f; }
        else if (n.StartsWith("forearm") || n.StartsWith("hand")) { xLimit = 30f; yzLimit = 12f; }
        else if (n == "spine.003") { xLimit = 45f; yzLimit = 8f; }
        else if (n.StartsWith("spine")) { xLimit = 4f; yzLimit = 4f; }
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
        for (int i = 0; i < VisualBoneNames.Length; i++)
        {
            Transform bone = FindBone(VisualBoneNames[i]);
            if (bone == null) continue;
            ConfigurableJoint joint = bone.GetComponent<ConfigurableJoint>();
            Collider collider = bone.GetComponent<Collider>();
            Rigidbody body = bone.GetComponent<Rigidbody>();
            ImpactRelay relay = bone.GetComponent<ImpactRelay>();
            if (collider != null) collider.enabled = false;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            if (joint != null) DestroyObjectSafely(joint);
            if (collider != null) DestroyObjectSafely(collider);
            if (body != null) DestroyObjectSafely(body);
            if (relay != null) DestroyObjectSafely(relay);
        }
    }

    private static void DestroyObjectSafely(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
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
        Transform cursor = visualBone.parent;
        while (cursor != null)
        {
            if (bodyByVisualBone.TryGetValue(cursor, out Rigidbody body)) return body;
            cursor = cursor.parent;
        }
        return pelvisBody;
    }

    private void CacheBones()
    {
        pelvis = FindBone("hip");
        visualRoot = pelvis;
        while (visualRoot != null && visualRoot.parent != null && visualRoot.parent != transform)
            visualRoot = visualRoot.parent;
        if (visualRoot == transform) visualRoot = null;
        chest = FindBone("spine.002") ?? FindBone("spine.001") ?? FindBone("spine");
        head = FindBone("spine.003") ?? chest;
        leftHip = FindBone("hip.L");
        rightHip = FindBone("hip.R");
        leftThigh = FindBone("thigh.L");
        rightThigh = FindBone("thigh.R");
        leftShin = FindBone("shin.L");
        rightShin = FindBone("shin.R");
        leftFoot = FindBone("shin.L.001");
        rightFoot = FindBone("shin.R.001");
        leftArm = FindBone("arm.L");
        leftShoulder = FindBone("sholder.L");
        if (leftShoulder == null) leftShoulder = FindBone("shoulder.L");
        rightArm = FindBone("arm.R");
        leftForearm = FindBone("forearm.L");
        rightForearm = FindBone("forearm.R");
        leftHand = FindBone("hand.L");
        rightHand = FindBone("hand.R");
    }

    private Transform FindBone(string boneName)
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (string.Equals(all[i].name, boneName, StringComparison.OrdinalIgnoreCase)) return all[i];
        return null;
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
        float fraction;
        if (n == "hip") fraction = 0.060f;
        else if (n.StartsWith("spine")) fraction = 0.045f;
        else if (n.StartsWith("thigh")) fraction = 0.040f;
        else if (n.EndsWith(".001") && n.StartsWith("shin")) fraction = 0.032f;
        else if (n.StartsWith("shin")) fraction = 0.032f;
        else if (n.StartsWith("arm")) fraction = 0.027f;
        else if (n.StartsWith("forearm")) fraction = 0.023f;
        else fraction = 0.025f;
        return Mathf.Clamp(renderedBodyHeight * fraction, 0.035f, 0.11f);
    }

    private float MeasureRenderedBodyHeight()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled) continue;
            if (!found) { bounds = renderers[i].bounds; found = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return found ? Mathf.Clamp(bounds.size.y, 0.8f, 3f) : 1.8f;
    }

    private float EstimateLegLength()
    {
        Transform foot = leftFoot != null ? leftFoot : rightFoot;
        return foot != null && pelvis != null ? Vector3.Distance(pelvis.position, foot.position) : 0.9f;
    }

    private void CacheFootOffsets()
    {
        Vector3 right = Vector3.Cross(Vector3.up, restForward).normalized;
        float fallbackSide = Mathf.Clamp(renderedBodyHeight * 0.07f, 0.07f, 0.16f);
        float rawLeft = -fallbackSide;
        float rawRight = fallbackSide;
        if (pelvis != null && leftFoot != null)
        {
            Vector3 offset = leftFoot.position - pelvis.position;
            rawLeft = Vector3.Dot(offset, right);
        }

        if (pelvis != null && rightFoot != null)
        {
            Vector3 offset = rightFoot.position - pelvis.position;
            rawRight = Vector3.Dot(offset, right);
        }

        // Preserve the model's actual left/right ordering while guaranteeing a
        // minimum stance width, so the two physical legs cannot target the same
        // line and cross through one another.
        float center = (rawLeft + rawRight) * 0.5f;
        float halfWidth = Mathf.Max(Mathf.Abs(rawRight - rawLeft) * 0.5f, fallbackSide);
        if (rawLeft <= rawRight)
        {
            leftFootSideOffset = center - halfWidth;
            rightFootSideOffset = center + halfWidth;
        }
        else
        {
            leftFootSideOffset = center + halfWidth;
            rightFootSideOffset = center - halfWidth;
        }
    }

    private void CacheLegIkData()
    {
        leftLegIk = CreateLegIkData(leftThigh, leftShin, leftFoot);
        rightLegIk = CreateLegIkData(rightThigh, rightShin, rightFoot);
    }

    private LegIkData CreateLegIkData(Transform thigh, Transform shin, Transform foot)
    {
        LegIkData data = new LegIkData();
        if (thigh == null || shin == null || foot == null || pelvis == null) return data;

        Vector3 upper = shin.position - thigh.position;
        Vector3 lower = foot.position - shin.position;
        if (upper.sqrMagnitude < 0.000001f || lower.sqrMagnitude < 0.000001f) return data;

        Vector3 fullLeg = foot.position - thigh.position;
        Vector3 line = fullLeg.sqrMagnitude > 0.000001f ? fullLeg.normalized : Vector3.down;
        Vector3 kneeOffset = upper - line * Vector3.Dot(upper, line);
        if (kneeOffset.sqrMagnitude < 0.000001f)
            kneeOffset = Vector3.ProjectOnPlane(restForward, line);
        if (kneeOffset.sqrMagnitude < 0.000001f)
            kneeOffset = Vector3.ProjectOnPlane(Vector3.forward, line);

        data.valid = true;
        data.thighAxisLocal = thigh.InverseTransformDirection(upper.normalized);
        data.shinAxisLocal = shin.InverseTransformDirection(lower.normalized);
        data.thighReferenceLocal = thigh.InverseTransformDirection(kneeOffset.normalized);
        data.shinReferenceLocal = shin.InverseTransformDirection(kneeOffset.normalized);
        data.kneePolePelvisLocal = Quaternion.Inverse(pelvis.rotation) * kneeOffset.normalized;
        data.thighLength = upper.magnitude;
        data.shinLength = lower.magnitude;
        data.footRestRelativeToShin = Quaternion.Inverse(shin.rotation) * foot.rotation;
        return data;
    }

    private void SolveVisualLegIk(Transform thigh, Transform shin, Transform foot, LegIkData data)
    {
        if (!data.valid || thigh == null || shin == null || foot == null) return;
        if (!bodyByVisualBone.TryGetValue(foot, out Rigidbody footBody)) return;

        Vector3 root = thigh.position;
        bool isLeft = foot == leftFoot;
        bool hasStepTarget = isLeft ? leftStepTargetValid : rightStepTargetValid;
        Vector3 desiredFootPosition = hasStepTarget
            ? GetRenderedFootTarget(isLeft)
            : footBody.transform.position;
        Vector3 toTarget = desiredFootPosition - root;
        float rawDistance = toTarget.magnitude;
        if (rawDistance < 0.0001f) return;

        Vector3 direction = toTarget / rawDistance;
        float minimum = Mathf.Abs(data.thighLength - data.shinLength) + 0.001f;
        float maximum = data.thighLength + data.shinLength - 0.001f;
        float distance = Mathf.Clamp(rawDistance, minimum, maximum);
        Vector3 target = root + direction * distance;

        Vector3 pole = pelvis.rotation * data.kneePolePelvisLocal;
        pole = Vector3.ProjectOnPlane(pole, direction).normalized;
        if (pole.sqrMagnitude < 0.001f)
            pole = Vector3.ProjectOnPlane(desiredFacing, direction).normalized;
        if (pole.sqrMagnitude < 0.001f)
            pole = Vector3.Cross(direction, Vector3.right).normalized;

        float along = (data.thighLength * data.thighLength -
                       data.shinLength * data.shinLength + distance * distance) /
                      (2f * distance);
        float kneeHeight = Mathf.Sqrt(Mathf.Max(0f,
            data.thighLength * data.thighLength - along * along));
        Vector3 knee = root + direction * along + pole * kneeHeight;

        AlignBoneFrame(thigh, data.thighAxisLocal, data.thighReferenceLocal, knee - root, pole);
        AlignBoneFrame(shin, data.shinAxisLocal, data.shinReferenceLocal, target - shin.position, pole);

        // Preserve the model's authored ankle angle (normally the intended
        // 90-degree foot-to-shin relationship) instead of copying foot roll
        // from the freely moving ragdoll body.
        foot.rotation = shin.rotation * data.footRestRelativeToShin;
    }

    private Vector3 GetRenderedFootTarget(bool left)
    {
        Vector3 current = left ? leftStepTargetWorld : rightStepTargetWorld;
        bool previousValid = left ? previousLeftStepValid : previousRightStepValid;
        // Script-driven physics has no Unity fixed/render interpolation clock.
        if (!previousValid || Physics.simulationMode != SimulationMode.FixedUpdate) return current;
        float fraction = Mathf.Clamp01((Time.time - Time.fixedTime) / Mathf.Max(.001f, Time.fixedDeltaTime));
        return Vector3.Lerp(left ? previousLeftStepTarget : previousRightStepTarget, current, fraction);
    }

    private static void AlignBoneFrame(
        Transform bone,
        Vector3 localAxis,
        Vector3 localReference,
        Vector3 worldDirection,
        Vector3 worldReference)
    {
        if (bone == null || worldDirection.sqrMagnitude < 0.000001f) return;
        Vector3 targetDirection = worldDirection.normalized;
        Vector3 currentDirection = bone.TransformDirection(localAxis).normalized;
        Quaternion aligned = Quaternion.FromToRotation(currentDirection, targetDirection) * bone.rotation;

        // A direction alone leaves one free roll axis, which accumulated into a
        // twisted lower body. Align a second reference axis to the knee plane.
        Vector3 alignedReference = Vector3.ProjectOnPlane(aligned * localReference, targetDirection).normalized;
        Vector3 targetReference = Vector3.ProjectOnPlane(worldReference, targetDirection).normalized;
        if (alignedReference.sqrMagnitude > 0.001f && targetReference.sqrMagnitude > 0.001f)
            aligned = Quaternion.FromToRotation(alignedReference, targetReference) * aligned;
        bone.rotation = aligned;
    }

    private void AlignHipToPelvis(Transform hipBone, Vector3 restPosition, Quaternion restRotation)
    {
        if (hipBone == null || pelvis == null) return;
        hipBone.SetPositionAndRotation(
            pelvis.TransformPoint(restPosition),
            pelvis.rotation * restRotation);
    }

    private void RotatePhysicalRigAroundPelvis(Quaternion facingDelta)
    {
        // Rotate only the two upper-leg bodies immediately. Rotating every limb
        // and clearing both plants on tiny steering changes made the feet fight
        // their joints and was the main source of movement vibration.
        RotateBoneBody(leftThigh, facingDelta);
        RotateBoneBody(rightThigh, facingDelta);
    }

    private void RotateBoneBody(Transform visualBone, Quaternion facingDelta)
    {
        if (visualBone == null || !bodyByVisualBone.TryGetValue(visualBone, out Rigidbody body)) return;
        body.MoveRotation(facingDelta * body.rotation);
    }

    private bool IsLegBone(Transform bone)
    {
        return bone == leftThigh || bone == leftShin || bone == leftFoot ||
               bone == rightThigh || bone == rightShin || bone == rightFoot;
    }

    private bool IsArmBone(Transform bone)
    {
        return bone == leftArm || bone == leftForearm || bone == leftHand ||
               bone == rightArm || bone == rightForearm || bone == rightHand;
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

    private float StrengthForBone(string boneName)
    {
        string n = boneName.ToLowerInvariant();
        if (n.StartsWith("thigh") || n.StartsWith("shin")) return lowerBodyStrength;
        if (n.StartsWith("spine")) return torsoStrength;
        return armStrength;
    }

    private void SetPose(Transform visualBone, Vector3 euler)
    {
        if (visualBone == null) return;
        for (int i = 0; i < muscles.Count; i++)
        {
            if (muscles[i].visualBone != visualBone) continue;
            muscles[i].proceduralEuler = euler;
            return;
        }
    }

    public void NotifyImpact(float relativeSpeed)
    {
        if (relativeSpeed > 13f) flopTimer = Mathf.Max(flopTimer, impactFlopSeconds);
    }

    public void ApplyTrainWheelKnockdown(Vector3 outwardVelocity, Vector3 trainVelocity, float duration)
    {
        if (pelvisBody == null || IsVehicleSeated || knockdownActive) return;

        animatorWasEnabledBeforeKnockdown = movementAnimator != null && movementAnimator.enabled;
        knockdownActive = true;
        recoveryPoseLockFrames = 0;
        knockdownTimer = Mathf.Max(0.5f, duration > 0f ? duration : knockdownDuration);
        flopTimer = knockdownTimer;
        moveInput = Vector2.zero;
        runInput = false;
        ClearFootPlants();

        if (movementAnimator != null) movementAnimator.enabled = false;
        SynchronizePhysicsLimbsToVisuals();
        ConfigureLimbSimulation(true);
        pelvisBody.isKinematic = false;
        pelvisBody.useGravity = true;
        pelvisBody.detectCollisions = true;
        pelvisBody.constraints = RigidbodyConstraints.None;
        pelvisAngularDampingBeforeKnockdown = pelvisBody.angularDamping;
        pelvisBody.angularDamping = ragdollAngularDamping;
        Vector3 velocity = trainVelocity + outwardVelocity;
        pelvisBody.linearVelocity = velocity;
        pelvisBody.angularVelocity = Vector3.Cross(Vector3.up, outwardVelocity.normalized) * 4f;
        for (int i = 0; i < muscles.Count; i++)
        {
            Rigidbody limb = muscles[i].body;
            if (limb == null) continue;
            limb.angularDamping = ragdollAngularDamping;
            limb.linearVelocity = velocity;
            limb.angularVelocity = UnityEngine.Random.insideUnitSphere * 3f;
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
        knockdownTimer = 0f;
        flopTimer = 0f;

        Vector3 flatForward = Vector3.ProjectOnPlane(desiredFacing, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.ProjectOnPlane(restForward, Vector3.up);
        flatForward.Normalize();
        float yaw = Vector3.SignedAngle(restForward, flatForward, Vector3.up);
        Quaternion upright = Quaternion.AngleAxis(yaw, Vector3.up) * balanceRestWorld;
        pelvisBody.rotation = upright;
        pelvisBody.angularVelocity = Vector3.zero;
        pelvisBody.angularDamping = pelvisAngularDampingBeforeKnockdown > 0f
            ? pelvisAngularDampingBeforeKnockdown : 7f;
        pelvisBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        movementFacingWorld = upright;
        uprightTargetWorld = upright;
        airborneInheritedVelocity = Vector3.ProjectOnPlane(pelvisBody.linearVelocity, Vector3.up);
        hasAirborneInheritedVelocity = !IsGrounded && airborneInheritedVelocity.sqrMagnitude > 0.0001f;

        if (dynamicPoseActive)
        {
            if (movementAnimator != null) movementAnimator.enabled = false;
            ResetProceduralPoseOffsets();
            ArmsReserved = false;
            SupportHandReserved = false;
            // Restore every imported transform, including helper, shoulder and
            // finger bones that are not part of the physical muscle list.
            RestoreVisualRestPose();
            AnchorAnimatedVisualAt(pelvisBody.position, upright);
            ApplyKinematicVisualPose(pelvisBody.position, upright);
            SynchronizePhysicsLimbsToVisuals();
            ConfigureLimbSimulation(true);
            for (int i = 0; i < muscles.Count; i++)
                if (muscles[i].body != null) muscles[i].body.angularDamping = 4.5f;
        }
        else
        {
            RestoreAnimatedPose(animatorWasEnabledBeforeKnockdown);
            for (int i = 0; i < muscles.Count; i++)
                if (muscles[i].body != null) muscles[i].body.angularDamping = 4.5f;
        }
        headSmoothingReady = false;
        jumpSupportCooldown = 0f;
    }
}


public static class GameBootstrap
{
    private const string GameplaySceneName = "game";
    private const string ResourcePath = "Wobbly/robber mangl";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePlayerIfNeeded()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, GameplaySceneName, System.StringComparison.OrdinalIgnoreCase)) return;

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
                float scale = 1.8f / bounds.size.y;
                model.transform.localScale = Vector3.one * scale;
                bounds = CalculateBounds(renderers);
                model.transform.position += Vector3.up * (0.04f - bounds.min.y);
            }

            controller = player.AddComponent<PlayerController>();
            player.transform.position = Vector3.zero;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        PlayerCamera cameraController = camera.GetComponent<PlayerCamera>();
        if (cameraController == null) cameraController = camera.gameObject.AddComponent<PlayerCamera>();
        cameraController.Target = controller;

        WeaponController weaponController = controller.GetComponent<WeaponController>();
        if (weaponController == null) controller.gameObject.AddComponent<WeaponController>();
        VehicleInteraction vehicleInteractor = controller.GetComponent<VehicleInteraction>();
        if (vehicleInteractor == null) controller.gameObject.AddComponent<VehicleInteraction>();
        ClimbController climbController = controller.GetComponent<ClimbController>();
        if (climbController == null) controller.gameObject.AddComponent<ClimbController>();
        CharacterAppearance appearance = controller.GetComponent<CharacterAppearance>();
        if (appearance == null) appearance = controller.gameObject.AddComponent<CharacterAppearance>();
        appearance.LoadSaved();
        MenuManager.Ensure(controller);
        PickupWeapon.PrepareSceneWeapons();
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
