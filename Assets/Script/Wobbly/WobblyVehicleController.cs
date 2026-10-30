using UnityEngine;

[DefaultExecutionOrder(150)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class WobblyVehicleController : MonoBehaviour
{
    [Header("Truck driving")]
    [SerializeField, Min(100f)] private float engineTorque = 2300f;
    [SerializeField, Min(1f)] private float maximumForwardSpeed = 24f;
    [SerializeField, Min(1f)] private float maximumReverseSpeed = 9f;
    [SerializeField, Range(5f, 45f)] private float maximumSteerAngle = 29f;
    [SerializeField, Min(1f)] private float steeringSharpness = 7f;
    [SerializeField, Min(0f)] private float rollingBrakeTorque = 180f;
    [SerializeField, Min(100f)] private float footBrakeTorque = 4200f;
    [SerializeField, Min(0f)] private float downforce = 65f;

    [Header("Wheel setup")]
    [SerializeField] private WheelCollider frontLeft;
    [SerializeField] private WheelCollider frontRight;
    [SerializeField] private WheelCollider rearLeft;
    [SerializeField] private WheelCollider rearRight;
    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform rearLeftMesh;
    [SerializeField] private Transform rearRightMesh;
    [SerializeField] private Transform steeringWheel;
    [SerializeField] private Transform leftSteeringGrip;
    [SerializeField] private Transform rightSteeringGrip;
    [SerializeField, Range(-50f, 10f)] private float steeringWheelPitch = -24f;
    [SerializeField, Range(1f, 6f)] private float steeringWheelTurns = 3f;
    [SerializeField] private WobblyVehicleDoor[] doors;

    private Rigidbody body;
    private WobblyVehicleSeat driverSeat;
    private WobblyWeaponController steeringPoseController;
    private Quaternion steeringWheelRestLocalRotation = Quaternion.identity;
    private float steerAngle;

    public bool IsBeingDriven => driverSeat != null && driverSeat.Occupant != null;
    public WobblyVehicleSeat DriverSeat => driverSeat;
    public Transform SteeringWheel => steeringWheel;
    public Transform LeftSteeringGrip => leftSteeringGrip;
    public Transform RightSteeringGrip => rightSteeringGrip;
    public float SpeedKilometresPerHour => body == null ? 0f : body.linearVelocity.magnitude * 3.6f;

    private void Awake()
    {
        if (GetComponent<WobblyClimbableSurface>() == null)
            gameObject.AddComponent<WobblyClimbableSurface>();
        body = GetComponent<Rigidbody>();
        body.centerOfMass = new Vector3(0f, 0.85f, -0.15f);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearDamping = 0.08f;
        body.angularDamping = 0.9f;
        if (steeringWheel != null)
        {
            steeringWheelRestLocalRotation = steeringWheel.localRotation;
            if (leftSteeringGrip == null) leftSteeringGrip = steeringWheel.Find("LeftHandGrip");
            if (rightSteeringGrip == null) rightSteeringGrip = steeringWheel.Find("RightHandGrip");
        }
    }

    private void FixedUpdate()
    {
        float throttle = IsBeingDriven ? Input.GetAxisRaw("Vertical") : 0f;
        float steering = IsBeingDriven ? Input.GetAxisRaw("Horizontal") : 0f;
        bool braking = IsBeingDriven && Input.GetKey(KeyCode.Space);

        float localForwardSpeed = body == null ? 0f : Vector3.Dot(body.linearVelocity, transform.forward);
        bool beyondLimit = throttle > 0f
            ? localForwardSpeed >= maximumForwardSpeed
            : throttle < 0f && localForwardSpeed <= -maximumReverseSpeed;
        float torque = beyondLimit ? 0f : throttle * engineTorque;
        float brake = braking ? footBrakeTorque : (Mathf.Abs(throttle) < 0.05f ? rollingBrakeTorque : 0f);

        float speed01 = Mathf.Clamp01(Mathf.Abs(localForwardSpeed) / Mathf.Max(1f, maximumForwardSpeed));
        float targetSteer = steering * maximumSteerAngle * Mathf.Lerp(1f, 0.48f, speed01);
        steerAngle = Mathf.Lerp(steerAngle, targetSteer,
            1f - Mathf.Exp(-steeringSharpness * Time.fixedDeltaTime));

        ApplyWheel(frontLeft, steerAngle, torque * 0.32f, brake);
        ApplyWheel(frontRight, steerAngle, torque * 0.32f, brake);
        ApplyWheel(rearLeft, 0f, torque * 0.68f, brake);
        ApplyWheel(rearRight, 0f, torque * 0.68f, brake);

        if (body != null && body.linearVelocity.sqrMagnitude > 0.1f)
            body.AddForce(-transform.up * downforce * body.linearVelocity.magnitude, ForceMode.Force);
    }

    private void LateUpdate()
    {
        MatchWheel(frontLeft, frontLeftMesh);
        MatchWheel(frontRight, frontRightMesh);
        MatchWheel(rearLeft, rearLeftMesh);
        MatchWheel(rearRight, rearRightMesh);
        if (steeringWheel != null)
        {
            Quaternion correctedRest = steeringWheelRestLocalRotation *
                                       Quaternion.AngleAxis(steeringWheelPitch, Vector3.right);
            steeringWheel.localRotation = correctedRest *
                                          Quaternion.AngleAxis(-steerAngle * steeringWheelTurns, Vector3.forward);
        }
        UpdateDriverHands();
    }

    public void Configure(
        WheelCollider fl, WheelCollider fr, WheelCollider rl, WheelCollider rr,
        Transform flMesh, Transform frMesh, Transform rlMesh, Transform rrMesh,
        Transform wheel, Transform leftGrip, Transform rightGrip,
        WobblyVehicleDoor[] configuredDoors)
    {
        frontLeft = fl;
        frontRight = fr;
        rearLeft = rl;
        rearRight = rr;
        frontLeftMesh = flMesh;
        frontRightMesh = frMesh;
        rearLeftMesh = rlMesh;
        rearRightMesh = rrMesh;
        steeringWheel = wheel;
        leftSteeringGrip = leftGrip;
        rightSteeringGrip = rightGrip;
        doors = configuredDoors;
    }

    public bool BeginDriving(WobblyVehicleSeat seat)
    {
        if (seat == null || !seat.IsDriverSeat || seat.Occupant == null || driverSeat != null) return false;
        driverSeat = seat;
        steeringPoseController = seat.Occupant.GetComponent<WobblyWeaponController>();
        CloseAllDoors();
        seat.Occupant.SetVehicleDrivingPose(true);
        return true;
    }

    public void StopDriving(WobblyVehicleSeat seat)
    {
        if (driverSeat != seat) return;
        if (driverSeat != null && driverSeat.Occupant != null)
            driverSeat.Occupant.SetVehicleDrivingPose(false);
        ReleaseDriverHands();
        driverSeat = null;
        ClearWheelInput();
    }

    public void CloseAllDoors()
    {
        if (doors == null) return;
        for (int i = 0; i < doors.Length; i++)
            if (doors[i] != null) doors[i].SetOpen(false);
    }

    private void OnDisable()
    {
        ReleaseDriverHands();
        driverSeat = null;
        ClearWheelInput();
    }

    private void ClearWheelInput()
    {
        ApplyWheel(frontLeft, 0f, 0f, rollingBrakeTorque);
        ApplyWheel(frontRight, 0f, 0f, rollingBrakeTorque);
        ApplyWheel(rearLeft, 0f, 0f, rollingBrakeTorque);
        ApplyWheel(rearRight, 0f, 0f, rollingBrakeTorque);
    }

    private void UpdateDriverHands()
    {
        if (!IsBeingDriven || leftSteeringGrip == null || rightSteeringGrip == null) return;
        if (steeringPoseController == null)
            steeringPoseController = driverSeat.Occupant.GetComponent<WobblyWeaponController>();
        if (steeringPoseController != null)
            steeringPoseController.ApplySteeringWheelPose(
                leftSteeringGrip, rightSteeringGrip, steeringWheel, true);
    }

    private void ReleaseDriverHands()
    {
        if (steeringPoseController != null)
            steeringPoseController.ApplySteeringWheelPose(
                leftSteeringGrip, rightSteeringGrip, steeringWheel, false);
        steeringPoseController = null;
    }

    private static void ApplyWheel(WheelCollider wheel, float steering, float motor, float brake)
    {
        if (wheel == null) return;
        wheel.steerAngle = steering;
        wheel.motorTorque = motor;
        wheel.brakeTorque = brake;
    }

    private static void MatchWheel(WheelCollider wheel, Transform wheelMesh)
    {
        if (wheel == null || wheelMesh == null) return;
        wheel.GetWorldPose(out Vector3 position, out Quaternion rotation);
        wheelMesh.SetPositionAndRotation(position, rotation);
    }
}
