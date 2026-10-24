using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyVehicleDoor : MonoBehaviour
{
    [SerializeField] private WobblyVehicleController vehicle;
    [SerializeField] private Vector3 hingePointInParent;
    [SerializeField] private Vector3 rotationAxisInParent = Vector3.up;
    [SerializeField] private float openAngle = 72f;
    [SerializeField, Min(30f)] private float angularSpeed = 150f;
    [SerializeField] private bool startsOpen;
    [SerializeField] private string interactionName = "Door";

    private Vector3 closedLocalPosition;
    private Quaternion closedLocalRotation;
    private float openAmount;
    private bool initialized;

    public WobblyVehicleController Vehicle => vehicle;
    public bool IsOpen => openAmount > 0.55f;
    public float OpenAmount => openAmount;
    public Vector3 InteractionPosition => transform.position;
    public string InteractionName => string.IsNullOrWhiteSpace(interactionName) ? "Panel" : interactionName;

    private void Awake()
    {
        InitializeIfNeeded();
        openAmount = startsOpen ? 1f : 0f;
        ApplyPose();
    }

    private void Update()
    {
        InitializeIfNeeded();
        float target = startsOpen ? 1f : 0f;
        openAmount = Mathf.MoveTowards(openAmount, target,
            angularSpeed / Mathf.Max(1f, Mathf.Abs(openAngle)) * Time.deltaTime);
        ApplyPose();
    }

    public void Configure(
        WobblyVehicleController owner, Vector3 hingeInParent, Vector3 rotationAxis,
        float signedOpenAngle, string displayName)
    {
        vehicle = owner;
        hingePointInParent = hingeInParent;
        rotationAxisInParent = rotationAxis.sqrMagnitude > 0.001f ? rotationAxis.normalized : Vector3.up;
        openAngle = signedOpenAngle;
        interactionName = displayName;
        closedLocalPosition = transform.localPosition;
        closedLocalRotation = transform.localRotation;
        initialized = true;
        openAmount = 0f;
        startsOpen = false;
        ApplyPose();
    }

    public void Toggle()
    {
        SetOpen(!startsOpen);
    }

    public void SetOpen(bool value)
    {
        InitializeIfNeeded();
        startsOpen = value;
    }

    private void InitializeIfNeeded()
    {
        if (initialized) return;
        if (vehicle == null) vehicle = GetComponentInParent<WobblyVehicleController>();
        closedLocalPosition = transform.localPosition;
        closedLocalRotation = transform.localRotation;
        initialized = true;
    }

    private void ApplyPose()
    {
        Quaternion turn = Quaternion.AngleAxis(openAngle * openAmount, rotationAxisInParent);
        transform.localPosition = hingePointInParent + turn * (closedLocalPosition - hingePointInParent);
        transform.localRotation = turn * closedLocalRotation;
    }
}
