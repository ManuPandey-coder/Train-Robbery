using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WobblyActiveRagdoll))]
public sealed class WobblyVehicleInteractor : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField, Min(0.5f)] private float doorReach = 2.1f;
    [SerializeField, Min(0.5f)] private float seatSearchRadius = 2.6f;
    [SerializeField] private KeyCode doorKey = KeyCode.E;
    [SerializeField] private KeyCode sitKey = KeyCode.F;
    [SerializeField] private KeyCode switchSeatKey = KeyCode.Q;

    private WobblyActiveRagdoll player;
    private WobblyVehicleSeat currentSeat;
    private WobblyVehicleSeat nearbySeat;
    private WobblyVehicleDoor nearbyDoor;
    private float nextSearchTime;
    private GUIStyle titleStyle;
    private GUIStyle hintStyle;
    private WobblyCameraController cameraController;

    public bool IsSeated => currentSeat != null;
    public bool IsDriving => currentSeat != null && currentSeat.Vehicle != null &&
                             currentSeat.Vehicle.DriverSeat == currentSeat;
    public bool WantsCursor => IsSeated && !IsDriving;

    private void Awake()
    {
        player = GetComponent<WobblyActiveRagdoll>();
        seatSearchRadius = Mathf.Max(2.6f, seatSearchRadius);
        Camera camera = Camera.main;
        cameraController = camera != null ? camera.GetComponent<WobblyCameraController>() : null;
    }

    private void Update()
    {
        if (player == null || player.Pelvis == null) return;

        if (currentSeat != null)
        {
            if (Input.GetKeyDown(switchSeatKey)) SwitchSeat();
            if (Input.GetKeyDown(doorKey) && currentSeat.IsDriverSeat)
            {
                if (IsDriving && currentSeat.AccessDoor != null) currentSeat.AccessDoor.Toggle();
                else if (!IsDriving) currentSeat.BeginDriving();
            }
            if (Input.GetKeyDown(sitKey)) StandUp();
            return;
        }

        if (Time.unscaledTime >= nextSearchTime)
        {
            nextSearchTime = Time.unscaledTime + 0.12f;
            FindNearbyInteractions();
        }

        if (nearbySeat != null && Input.GetKeyDown(sitKey)) SitDown(nearbySeat);
        if (nearbySeat == null && nearbyDoor != null && Input.GetKeyDown(doorKey)) nearbyDoor.Toggle();
    }

    private void FindNearbyInteractions()
    {
        Vector3 playerPosition = player.Pelvis.position;
        nearbySeat = null;
        nearbyDoor = null;
        float bestSeat = seatSearchRadius * seatSearchRadius;
        float bestDriverSeat = seatSearchRadius * seatSearchRadius;
        WobblyVehicleSeat closestDriverSeat = null;
        float bestDoor = doorReach * doorReach;

        WobblyVehicleSeat[] seats = Object.FindObjectsByType<WobblyVehicleSeat>(FindObjectsSortMode.None);
        for (int i = 0; i < seats.Length; i++)
        {
            if (!seats[i].CanSit(player)) continue;
            float distance = (seats[i].InteractionPoint.position - playerPosition).sqrMagnitude;
            if (seats[i].IsDriverSeat && distance < bestDriverSeat)
            {
                bestDriverSeat = distance;
                closestDriverSeat = seats[i];
            }
            if (distance >= bestSeat) continue;
            bestSeat = distance;
            nearbySeat = seats[i];
        }
        if (closestDriverSeat != null) nearbySeat = closestDriverSeat;

        WobblyVehicleDoor[] doors = Object.FindObjectsByType<WobblyVehicleDoor>(FindObjectsSortMode.None);
        for (int i = 0; i < doors.Length; i++)
        {
            float distance = (doors[i].InteractionPosition - playerPosition).sqrMagnitude;
            if (distance >= bestDoor) continue;
            bestDoor = distance;
            nearbyDoor = doors[i];
        }
    }

    private void SitDown(WobblyVehicleSeat seat)
    {
        if (seat != null && seat.Sit(player))
        {
            currentSeat = seat;
            nearbySeat = null;
            nearbyDoor = null;
            if (seat.IsDriverSeat) seat.BeginDriving();
            if (cameraController == null && Camera.main != null)
                cameraController = Camera.main.GetComponent<WobblyCameraController>();
            if (cameraController != null) cameraController.ForceThirdPerson();
        }
    }

    private void StandUp()
    {
        if (currentSeat == null) return;
        WobblyVehicleSeat seat = currentSeat;
        currentSeat = null;
        seat.StandUp();
        nextSearchTime = Time.unscaledTime + 0.35f;
    }

    private void SwitchSeat()
    {
        if (currentSeat == null || currentSeat.Vehicle == null) return;
        WobblyVehicleSeat[] seats = currentSeat.Vehicle.GetComponentsInChildren<WobblyVehicleSeat>(true);
        WobblyVehicleSeat targetSeat = null;
        for (int i = 0; i < seats.Length; i++)
        {
            if (seats[i] == currentSeat || seats[i].Occupant != null) continue;
            targetSeat = seats[i];
            break;
        }
        if (targetSeat == null || !currentSeat.SwitchOccupantTo(targetSeat)) return;
        currentSeat = targetSeat;
    }

    private void OnGUI()
    {
        EnsureStyles();
        float width = Mathf.Min(540f, Screen.width - 32f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height - 155f;

        if (IsDriving)
        {
            GUI.Box(new Rect(x, y, width, 118f), GUIContent.none);
            GUI.Label(new Rect(x + 12f, y + 9f, width - 24f, 26f),
                $"DRIVING  {currentSeat.Vehicle.SpeedKilometresPerHour:0} km/h", titleStyle);
            float drivingButtonWidth = (width - 48f) / 3f;
            if (GUI.Button(new Rect(x + 12f, y + 45f, drivingButtonWidth, 52f), "Switch Seat  [Q]"))
                SwitchSeat();
            string doorLabel = currentSeat.AccessDoor != null && currentSeat.AccessDoor.IsOpen
                ? "Close Door  [E]" : "Open Door  [E]";
            if (GUI.Button(new Rect(x + 24f + drivingButtonWidth, y + 45f, drivingButtonWidth, 52f), doorLabel) &&
                currentSeat.AccessDoor != null)
                currentSeat.AccessDoor.Toggle();
            if (GUI.Button(new Rect(x + 36f + drivingButtonWidth * 2f, y + 45f, drivingButtonWidth, 52f), "Get Out  [F]"))
                StandUp();
            return;
        }

        if (currentSeat != null)
        {
            GUI.Box(new Rect(x, y, width, 118f), GUIContent.none);
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 26f),
                "SITTING — " + currentSeat.SeatName, titleStyle);
            float buttonWidth = (width - 48f) / 3f;
            if (GUI.Button(new Rect(x + 12f, y + 43f, buttonWidth, 52f), "Switch Seat  [Q]"))
                SwitchSeat();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = currentSeat != null && currentSeat.IsDriverSeat;
            if (GUI.Button(new Rect(x + 24f + buttonWidth, y + 43f, buttonWidth, 52f),
                    currentSeat != null && currentSeat.IsDriverSeat ? "Drive Truck  [E]" : "Driver Seat Only"))
                currentSeat.BeginDriving();
            GUI.enabled = previousEnabled;
            if (GUI.Button(new Rect(x + 36f + buttonWidth * 2f, y + 43f, buttonWidth, 52f), "Get Out  [F]"))
                StandUp();
            return;
        }

        if (nearbyDoor == null && nearbySeat == null) return;
        GUI.Box(new Rect(x, y, width, 104f), GUIContent.none);
        if (nearbySeat != null)
        {
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 25f),
                "Truck nearby — drive directly", titleStyle);
            if (GUI.Button(new Rect(x + 12f, y + 38f, width - 24f, 46f),
                    "Drive Truck  [F]"))
                SitDown(nearbySeat);
        }
        else
        {
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 25f),
                "Interact with the truck", titleStyle);
            string doorText = (nearbyDoor.IsOpen ? "Close " : "Open ") + nearbyDoor.InteractionName + "  [E]";
            if (GUI.Button(new Rect(x + 12f, y + 38f, width - 24f, 46f), doorText))
                nearbyDoor.Toggle();
        }
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 16,
            normal = { textColor = Color.white }
        };
        hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 14,
            normal = { textColor = new Color(0.86f, 0.93f, 1f) }
        };
    }
}
