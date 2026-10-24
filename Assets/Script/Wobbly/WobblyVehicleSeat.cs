using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyVehicleSeat : MonoBehaviour
{
    [SerializeField] private WobblyVehicleController vehicle;
    [SerializeField] private WobblyVehicleDoor accessDoor;
    [SerializeField] private Transform seatAnchor;
    [SerializeField] private Transform standPoint;
    [SerializeField] private Transform interactionPoint;
    [SerializeField] private bool driverSeat;
    [SerializeField, Min(0.5f)] private float interactionDistance = 1.45f;

    public WobblyVehicleController Vehicle => vehicle;
    public WobblyVehicleDoor AccessDoor => accessDoor;
    public Transform InteractionPoint => interactionPoint != null ? interactionPoint : transform;
    public bool IsDriverSeat => driverSeat;
    public WobblyActiveRagdoll Occupant { get; private set; }
    public string SeatName => driverSeat ? "Driver Seat" : "Passenger Seat";

    public void Configure(
        WobblyVehicleController owner, WobblyVehicleDoor door, Transform anchor,
        Transform standingPoint, Transform promptPoint, bool isDriver)
    {
        vehicle = owner;
        accessDoor = door;
        seatAnchor = anchor;
        standPoint = standingPoint;
        interactionPoint = promptPoint;
        driverSeat = isDriver;
    }

    public bool CanSit(WobblyActiveRagdoll player)
    {
        if (player == null || Occupant != null || player.IsVehicleSeated) return false;
        if (accessDoor != null && !accessDoor.IsOpen) return false;
        return Vector3.Distance(player.Pelvis.position, InteractionPoint.position) <= interactionDistance;
    }

    public bool Sit(WobblyActiveRagdoll player)
    {
        if (!CanSit(player) || seatAnchor == null) return false;
        if (!player.EnterVehicleSeat(seatAnchor, vehicle != null ? vehicle.GetComponent<Rigidbody>() : null)) return false;
        Occupant = player;
        return true;
    }

    public bool BeginDriving()
    {
        return driverSeat && Occupant != null && vehicle != null && vehicle.BeginDriving(this);
    }

    public void StopDriving()
    {
        if (vehicle != null) vehicle.StopDriving(this);
    }

    public void StandUp()
    {
        if (Occupant == null) return;
        StopDriving();
        Transform exit = standPoint != null ? standPoint : transform;
        WobblyActiveRagdoll player = Occupant;
        Occupant = null;
        player.ExitVehicleSeat(exit.position, exit.rotation);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = driverSeat ? new Color(0.2f, 0.8f, 1f, 0.85f) : new Color(0.3f, 1f, 0.45f, 0.85f);
        if (seatAnchor != null) Gizmos.DrawWireSphere(seatAnchor.position, 0.12f);
        if (interactionPoint != null) Gizmos.DrawWireSphere(interactionPoint.position, interactionDistance);
    }
}
