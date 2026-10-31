using UnityEngine;

[DefaultExecutionOrder(75)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public sealed class ClimbController : MonoBehaviour
{
    [Header("Automatic moving-surface climb")]
    [SerializeField, Range(0.1f, 0.4f)] private float grabRadius = 0.2f;
    [SerializeField, Range(0.45f, 1.5f)] private float grabReach = 0.95f;
    [SerializeField, Range(1.5f, 7f)] private float maximumTopSearchHeight = 5.5f;
    [SerializeField, Range(0.25f, 1f)] private float minimumClimbRise = 0.45f;
    [SerializeField, Range(0.7f, 2.5f)] private float baseClimbDuration = 1.05f;
    [SerializeField, Range(0.2f, 0.9f)] private float standInset = 0.55f;
    [Header("Climb-Up Edge Matching")]
    [Tooltip("Play the authored Climb Up animation only after the pelvis reaches the edge of a detected flat top surface.")]
    [SerializeField] private bool playClimbUpAnimationAtEdge = true;
    [Tooltip("Minimum upward-facing normal required for a ledge top to count as flat. One requires a perfectly horizontal top.")]
    [SerializeField, Range(0.5f, 1f)] private float flatSurfaceThreshold = 0.68f;
    [Tooltip("The climb-up clip starts when the pelvis reaches the ledge at this fraction of standing height above the flat surface.")]
    [SerializeField, Range(0f, 0.3f)] private float edgeHeightFraction = 0.1f;
    [Tooltip("How far outside the wall the pelvis remains when the Climb Up animation begins.")]
    [SerializeField, Range(0.1f, 0.65f)] private float edgePoseOutset = 0.3f;
    [Tooltip("Full duration of the authored climb-up animation. The player reaches the standing point over exactly this time.")]
    [SerializeField, Range(0.3f, 2f)] private float climbUpDuration = 0.75f;
    [Tooltip("Time after pressing Jump during which a moving surface can be grabbed.")]
    [SerializeField, Range(0.4f, 2f)] private float jumpGrabWindow = 1.15f;

    private PlayerController player;
    private Transform surface;
    private Rigidbody surfaceBody;
    private Vector3 startPelvisLocal;
    private Vector3 wallPointLocal;
    private Vector3 topPointLocal;
    private Vector3 wallNormalLocal;
    private float climbProgress;
    private float climbDuration;
    private float climbUpElapsed;
    private float nextGrabTime;
    private float jumpArmedUntil;
    private bool climbing;
    private bool climbUpStarted;
    private Vector3 renderedPelvisLocal;
    private Vector3 renderedFacingLocal;
    private bool renderPoseValid;

    public bool IsClimbing => climbing;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (MenuManager.BlocksGameplay) return;
        if (Input.GetButtonDown("Jump") && !climbing)
            jumpArmedUntil = Time.time + jumpGrabWindow;

        if (climbing)
        {
            if (Input.GetAxisRaw("Vertical") < -0.35f) EndClimb(false);
            return;
        }

        if (player == null || player.PhysicsBody == null || player.IsVehicleSeated ||
            player.IsExternallyControlled || player.IsGrounded || Time.time < nextGrabTime ||
            Time.time > jumpArmedUntil)
            return;

        TryBeginClimb();
    }

    private void FixedUpdate()
    {
        if (!climbing) return;
        if (surface == null || player == null || player.PhysicsBody == null)
        {
            EndClimb(false);
            return;
        }

        float climbInput = Mathf.Max(0f, Input.GetAxisRaw("Vertical"));
        if (Input.GetButton("Jump")) climbInput = Mathf.Max(climbInput, 1f);
        if (!climbUpStarted && climbInput < 0.05f)
        {
            player.SetExternalClimbAnimation(false, false);
            return;
        }
        if (!climbUpStarted)
        {
            climbProgress = Mathf.Clamp01(climbProgress +
                                          Time.fixedDeltaTime * climbInput / Mathf.Max(0.1f, climbDuration));
            if (climbProgress >= 1f)
            {
                climbProgress = 1f;
                climbUpStarted = true;
                climbUpElapsed = 0f;
                player.SetExternalClimbAnimation(false, playClimbUpAnimationAtEdge);
            }
            else player.SetExternalClimbAnimation(true, false);
        }
        else
        {
            // Once the body reaches the edge, top-out is one uninterrupted action.
            // It must not pause halfway when the player releases the movement key.
            climbUpElapsed = Mathf.Min(climbUpDuration, climbUpElapsed + Time.fixedDeltaTime);
            player.SetExternalClimbAnimation(false, playClimbUpAnimationAtEdge);
        }
        Vector3 wallPoint = surface.TransformPoint(wallPointLocal);
        Vector3 topPoint = surface.TransformPoint(topPointLocal);
        Vector3 wallNormal = surface.TransformDirection(wallNormalLocal).normalized;
        Vector3 inward = -Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
        if (inward.sqrMagnitude < 0.001f) inward = player.FacingDirection;

        Vector3 start = surface.TransformPoint(startPelvisLocal);
        Vector3 hang = wallPoint + wallNormal * 0.46f + Vector3.up * 0.12f;
        Vector3 edgePose = topPoint + wallNormal * edgePoseOutset +
                           Vector3.up * (player.StandingPelvisHeight * edgeHeightFraction);
        Vector3 stand = topPoint + inward * standInset +
                        Vector3.up * (player.StandingPelvisHeight + 0.035f);

        Vector3 pelvisPosition;
        if (!climbUpStarted && climbProgress < 0.38f)
        {
            float t = Smooth01(climbProgress / 0.38f);
            pelvisPosition = Vector3.Lerp(start, hang, t);
        }
        else if (!climbUpStarted)
        {
            float t = Smooth01((climbProgress - 0.38f) / 0.62f);
            pelvisPosition = Vector3.Lerp(hang, edgePose, t);
        }
        else
        {
            float t = Smooth01(climbUpElapsed / Mathf.Max(0.01f, climbUpDuration));
            pelvisPosition = Vector3.Lerp(edgePose, stand, t);
        }

        renderedPelvisLocal = surface.InverseTransformPoint(pelvisPosition);
        renderedFacingLocal = surface.InverseTransformDirection(inward);
        renderPoseValid = true;
        player.SetExternalClimbPose(pelvisPosition, inward);
        if (climbUpStarted && climbUpElapsed >= climbUpDuration) EndClimb(true);
    }

    private void LateUpdate()
    {
        if (!climbing || !renderPoseValid || surface == null || player == null) return;
        // Rigidbody interpolation changes the displayed train pose between
        // physics ticks. Rebuild the character's displayed pose from the same
        // interpolated transform so it cannot shake or trail behind the train.
        player.SetExternalClimbRenderPose(
            surface.TransformPoint(renderedPelvisLocal),
            surface.TransformDirection(renderedFacingLocal));
    }

    private void TryBeginClimb()
    {
        Vector3 origin = player.PhysicsBody.position + Vector3.up * 0.35f;
        Vector3 velocityDirection = Vector3.ProjectOnPlane(player.PhysicsBody.linearVelocity, Vector3.up);
        Vector3 facing = Vector3.ProjectOnPlane(player.FacingDirection, Vector3.up);
        Camera camera = Camera.main;
        Vector3 cameraDirection = camera != null
            ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up) : facing;

        Vector3[] directions =
        {
            facing.sqrMagnitude > 0.001f ? facing.normalized : transform.forward,
            velocityDirection.sqrMagnitude > 0.04f ? velocityDirection.normalized : facing.normalized,
            cameraDirection.sqrMagnitude > 0.001f ? cameraDirection.normalized : facing.normalized
        };

        RaycastHit bestWall = default;
        RaycastHit bestTop = default;
        Rigidbody bestBody = null;
        float bestDistance = float.PositiveInfinity;
        for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
        {
            Vector3 direction = directions[directionIndex];
            if (direction.sqrMagnitude < 0.001f) continue;
            RaycastHit[] walls = Physics.SphereCastAll(origin, grabRadius, direction, grabReach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < walls.Length; i++)
            {
                RaycastHit wall = walls[i];
                Rigidbody targetBody = wall.rigidbody;
                if (!IsAllowedSurface(targetBody, wall.collider) || Mathf.Abs(wall.normal.y) > 0.58f)
                    continue;
                if (!TryFindFlatTop(wall, targetBody, out RaycastHit top)) continue;
                float rise = top.point.y - player.PhysicsBody.position.y;
                if (rise < minimumClimbRise || rise > maximumTopSearchHeight) continue;
                if (wall.distance >= bestDistance) continue;
                bestDistance = wall.distance;
                bestWall = wall;
                bestTop = top;
                bestBody = targetBody;
            }
        }

        if (bestBody == null) return;
        BeginClimb(bestWall, bestTop, bestBody);
    }

    private bool TryFindFlatTop(RaycastHit wall, Rigidbody targetBody, out RaycastHit bestTop)
    {
        bestTop = default;
        Vector3 horizontalNormal = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
        Vector3 origin = wall.point - horizontalNormal * 0.38f +
                         Vector3.up * maximumTopSearchHeight;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down,
            maximumTopSearchHeight + 1.5f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float highest = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.rigidbody != targetBody || hit.normal.y < flatSurfaceThreshold || hit.point.y <= highest) continue;
            highest = hit.point.y;
            bestTop = hit;
        }
        return !float.IsNegativeInfinity(highest);
    }

    private bool IsAllowedSurface(Rigidbody targetBody, Collider candidate)
    {
        if (targetBody == null || candidate == null || targetBody == player.PhysicsBody) return false;
        ImpactRelay relay = candidate.GetComponentInParent<ImpactRelay>();
        if (relay != null && relay.owner == player) return false;
        return targetBody.GetComponent<ClimbableSurface>() != null ||
               targetBody.GetComponent<VehicleController>() != null;
    }

    private void BeginClimb(RaycastHit wall, RaycastHit top, Rigidbody targetBody)
    {
        if (!player.BeginExternalClimbControl()) return;
        surfaceBody = targetBody;
        surface = targetBody.transform;
        Vector3 wallNormal = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;

        startPelvisLocal = surface.InverseTransformPoint(player.PhysicsBody.position);
        wallPointLocal = surface.InverseTransformPoint(wall.point);
        topPointLocal = surface.InverseTransformPoint(top.point);
        wallNormalLocal = surface.InverseTransformDirection(wallNormal);
        float rise = Mathf.Max(0f, top.point.y - player.PhysicsBody.position.y);
        // This duration covers only reaching the edge. The full climb-up clip
        // has its own exact timer and drives the final movement onto the top.
        climbDuration = Mathf.Clamp(baseClimbDuration + rise * 0.12f, 0.7f, 2.5f);
        climbProgress = 0f;
        climbUpElapsed = 0f;
        climbUpStarted = false;
        renderedPelvisLocal = startPelvisLocal;
        renderedFacingLocal = surface.InverseTransformDirection(-wallNormal);
        renderPoseValid = true;
        jumpArmedUntil = 0f;
        climbing = true;
        player.SetExternalClimbAnimation(false, false);
    }

    private void EndClimb(bool reachedTop)
    {
        if (!climbing) return;
        ClimbableSurface movingSurface = surfaceBody != null
            ? surfaceBody.GetComponent<ClimbableSurface>() : null;
        Vector3 inheritedVelocity = movingSurface != null
            ? movingSurface.GetVelocityAtPoint(player.PhysicsBody.position)
            : surfaceBody != null ? surfaceBody.GetPointVelocity(player.PhysicsBody.position) : Vector3.zero;
        Vector3 wallNormal = surface != null
            ? surface.TransformDirection(wallNormalLocal).normalized : -player.FacingDirection;
        Vector3 inward = -Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
        if (inward.sqrMagnitude < 0.001f) inward = player.FacingDirection;

        Vector3 finishPosition = player.PhysicsBody.position;
        if (reachedTop && surface != null)
        {
            Vector3 top = surface.TransformPoint(topPointLocal);
            finishPosition = top + inward * standInset +
                             Vector3.up * (player.StandingPelvisHeight + 0.035f);
        }

        climbing = false;
        renderPoseValid = false;
        player.FinishExternalClimbControl(finishPosition, inward, inheritedVelocity);
        surface = null;
        surfaceBody = null;
        nextGrabTime = Time.time + 0.45f;
    }

    public void KnockOffByTrainWheel(Vector3 outwardVelocity, Vector3 trainVelocity, float duration)
    {
        if (climbing) EndClimb(false);
        if (player != null)
            player.ApplyTrainWheelKnockdown(outwardVelocity, trainVelocity, duration);
    }

    private void OnDisable()
    {
        if (climbing) EndClimb(false);
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}


[DisallowMultipleComponent]
public sealed class ClimbableSurface : MonoBehaviour
{
    private Rigidbody surfaceBody;
    private bool hasScriptedVelocity;
    private Vector3 scriptedVelocity;

    public void SetScriptedVelocity(Vector3 velocity)
    {
        hasScriptedVelocity = true;
        scriptedVelocity = velocity;
    }

    public Vector3 GetVelocityAtPoint(Vector3 worldPoint)
    {
        if (hasScriptedVelocity) return scriptedVelocity;
        if (surfaceBody == null) surfaceBody = GetComponent<Rigidbody>();
        return surfaceBody != null ? surfaceBody.GetPointVelocity(worldPoint) : Vector3.zero;
    }
}
