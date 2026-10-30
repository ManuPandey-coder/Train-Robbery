using UnityEngine;

[DefaultExecutionOrder(240)]
[DisallowMultipleComponent]
[RequireComponent(typeof(WobblyActiveRagdoll))]
public sealed class WobblyClimbController : MonoBehaviour
{
    [Header("Automatic moving-surface climb")]
    [SerializeField, Range(0.1f, 0.4f)] private float grabRadius = 0.2f;
    [SerializeField, Range(0.45f, 1.5f)] private float grabReach = 0.95f;
    [SerializeField, Range(1.5f, 7f)] private float maximumTopSearchHeight = 5.5f;
    [SerializeField, Range(0.25f, 1f)] private float minimumClimbRise = 0.45f;
    [SerializeField, Range(0.7f, 2.5f)] private float baseClimbDuration = 1.05f;
    [SerializeField, Range(0.15f, 0.6f)] private float handSpacing = 0.42f;
    [SerializeField, Range(0.2f, 0.9f)] private float standInset = 0.55f;
    [Tooltip("Time after pressing Jump during which a moving surface can be grabbed.")]
    [SerializeField, Range(0.4f, 2f)] private float jumpGrabWindow = 1.15f;

    private WobblyActiveRagdoll player;
    private WobblyWeaponController handPose;
    private Transform surface;
    private Rigidbody surfaceBody;
    private Vector3 startPelvisLocal;
    private Vector3 wallPointLocal;
    private Vector3 topPointLocal;
    private Vector3 wallNormalLocal;
    private Vector3 leftGrabStartLocal;
    private Vector3 rightGrabStartLocal;
    private float climbProgress;
    private float climbDuration;
    private float nextGrabTime;
    private float jumpArmedUntil;
    private bool climbing;

    public bool IsClimbing => climbing;

    private void Awake()
    {
        player = GetComponent<WobblyActiveRagdoll>();
        handPose = GetComponent<WobblyWeaponController>();
    }

    private void Update()
    {
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
        if (climbInput < 0.05f) return;
        climbProgress = Mathf.Clamp01(climbProgress +
                                      Time.fixedDeltaTime * climbInput / Mathf.Max(0.1f, climbDuration));
        Vector3 wallPoint = surface.TransformPoint(wallPointLocal);
        Vector3 topPoint = surface.TransformPoint(topPointLocal);
        Vector3 wallNormal = surface.TransformDirection(wallNormalLocal).normalized;
        Vector3 inward = -Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
        if (inward.sqrMagnitude < 0.001f) inward = player.FacingDirection;

        Vector3 start = surface.TransformPoint(startPelvisLocal);
        Vector3 hang = wallPoint + wallNormal * 0.46f + Vector3.up * 0.12f;
        Vector3 overEdge = topPoint + wallNormal * 0.3f +
                           Vector3.up * (player.StandingPelvisHeight * 0.48f);
        Vector3 stand = topPoint + inward * standInset +
                        Vector3.up * (player.StandingPelvisHeight + 0.035f);

        Vector3 pelvisPosition;
        if (climbProgress < 0.24f)
        {
            float t = Smooth01(climbProgress / 0.24f);
            pelvisPosition = Vector3.Lerp(start, hang, t);
        }
        else if (climbProgress < 0.76f)
        {
            float t = Smooth01((climbProgress - 0.24f) / 0.52f);
            pelvisPosition = Vector3.Lerp(hang, overEdge, t);
        }
        else
        {
            float t = Smooth01((climbProgress - 0.76f) / 0.24f);
            pelvisPosition = Vector3.Lerp(overEdge, stand, t);
        }

        player.SetExternalClimbPose(pelvisPosition, inward);
        if (climbProgress >= 1f) EndClimb(true);
    }

    private void LateUpdate()
    {
        if (!climbing || surface == null || handPose == null) return;
        Vector3 wallNormal = surface.TransformDirection(wallNormalLocal).normalized;
        Vector3 topPoint = surface.TransformPoint(topPointLocal);
        Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal).normalized;
        if (tangent.sqrMagnitude < 0.001f) tangent = transform.right;

        float handTravel = Smooth01(Mathf.InverseLerp(0.08f, 0.72f, climbProgress));
        Vector3 edgeCenter = topPoint + wallNormal * 0.045f + Vector3.up * 0.025f;
        Vector3 leftStart = surface.TransformPoint(leftGrabStartLocal);
        Vector3 rightStart = surface.TransformPoint(rightGrabStartLocal);
        Vector3 leftTarget = Vector3.Lerp(leftStart, edgeCenter - tangent * handSpacing * 0.5f, handTravel);
        Vector3 rightTarget = Vector3.Lerp(rightStart, edgeCenter + tangent * handSpacing * 0.5f, handTravel);
        handPose.ApplyClimbHandPose(leftTarget, rightTarget, wallNormal, true);
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
            if (hit.rigidbody != targetBody || hit.normal.y < 0.68f || hit.point.y <= highest) continue;
            highest = hit.point.y;
            bestTop = hit;
        }
        return !float.IsNegativeInfinity(highest);
    }

    private bool IsAllowedSurface(Rigidbody targetBody, Collider candidate)
    {
        if (targetBody == null || candidate == null || targetBody == player.PhysicsBody) return false;
        WobblyImpactRelay relay = candidate.GetComponentInParent<WobblyImpactRelay>();
        if (relay != null && relay.owner == player) return false;
        return targetBody.GetComponent<WobblyClimbableSurface>() != null ||
               targetBody.GetComponent<WobblyVehicleController>() != null;
    }

    private void BeginClimb(RaycastHit wall, RaycastHit top, Rigidbody targetBody)
    {
        if (!player.BeginExternalClimbControl()) return;
        if (handPose == null) handPose = GetComponent<WobblyWeaponController>();
        surfaceBody = targetBody;
        surface = targetBody.transform;
        Vector3 wallNormal = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
        Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal).normalized;
        Vector3 grabCenter = wall.point + wallNormal * 0.04f + Vector3.up * 0.5f;

        startPelvisLocal = surface.InverseTransformPoint(player.PhysicsBody.position);
        wallPointLocal = surface.InverseTransformPoint(wall.point);
        topPointLocal = surface.InverseTransformPoint(top.point);
        wallNormalLocal = surface.InverseTransformDirection(wallNormal);
        leftGrabStartLocal = surface.InverseTransformPoint(grabCenter - tangent * handSpacing * 0.5f);
        rightGrabStartLocal = surface.InverseTransformPoint(grabCenter + tangent * handSpacing * 0.5f);
        float rise = Mathf.Max(0f, top.point.y - player.PhysicsBody.position.y);
        climbDuration = Mathf.Clamp(baseClimbDuration + rise * 0.12f, 0.75f, 2.2f);
        climbProgress = 0f;
        jumpArmedUntil = 0f;
        climbing = true;
    }

    private void EndClimb(bool reachedTop)
    {
        if (!climbing) return;
        Vector3 inheritedVelocity = surfaceBody != null
            ? surfaceBody.GetPointVelocity(player.PhysicsBody.position) : Vector3.zero;
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
        if (handPose != null) handPose.ApplyClimbHandPose(Vector3.zero, Vector3.zero, wallNormal, false);
        player.FinishExternalClimbControl(finishPosition, inward, inheritedVelocity);
        surface = null;
        surfaceBody = null;
        nextGrabTime = Time.time + 0.45f;
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
