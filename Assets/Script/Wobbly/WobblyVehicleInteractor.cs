using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(WobblyActiveRagdoll))]
public sealed class WobblyVehicleInteractor : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField, Min(0.5f)] private float doorReach = 2.1f;
    [SerializeField, Min(0.5f)] private float seatSearchRadius = 1.75f;
    [SerializeField] private KeyCode doorKey = KeyCode.E;
    [SerializeField] private KeyCode sitKey = KeyCode.F;

    private WobblyActiveRagdoll player;
    private WobblyVehicleSeat currentSeat;
    private WobblyVehicleSeat nearbySeat;
    private WobblyVehicleDoor nearbyDoor;
    private float nextSearchTime;
    private GUIStyle titleStyle;
    private GUIStyle hintStyle;

    public bool IsSeated => currentSeat != null;
    public bool IsDriving => currentSeat != null && currentSeat.Vehicle != null &&
                             currentSeat.Vehicle.DriverSeat == currentSeat;
    public bool WantsCursor => IsSeated && !IsDriving;

    private void Awake()
    {
        player = GetComponent<WobblyActiveRagdoll>();
    }

    private void Update()
    {
        if (player == null || player.Pelvis == null) return;

        if (currentSeat != null)
        {
            if (IsDriving)
            {
                if (Input.GetKeyDown(sitKey)) currentSeat.StopDriving();
            }
            else
            {
                if (currentSeat.IsDriverSeat && Input.GetKeyDown(doorKey)) currentSeat.BeginDriving();
                if (Input.GetKeyDown(sitKey)) StandUp();
            }
            return;
        }

        if (Time.unscaledTime >= nextSearchTime)
        {
            nextSearchTime = Time.unscaledTime + 0.12f;
            FindNearbyInteractions();
        }

        if (nearbyDoor != null && Input.GetKeyDown(doorKey)) nearbyDoor.Toggle();
        if (nearbySeat != null && Input.GetKeyDown(sitKey)) SitDown(nearbySeat);
    }

    private void FindNearbyInteractions()
    {
        Vector3 playerPosition = player.Pelvis.position;
        nearbySeat = null;
        nearbyDoor = null;
        float bestSeat = seatSearchRadius * seatSearchRadius;
        float bestDoor = doorReach * doorReach;

        WobblyVehicleSeat[] seats = Object.FindObjectsByType<WobblyVehicleSeat>(FindObjectsSortMode.None);
        for (int i = 0; i < seats.Length; i++)
        {
            if (!seats[i].CanSit(player)) continue;
            float distance = (seats[i].InteractionPoint.position - playerPosition).sqrMagnitude;
            if (distance >= bestSeat) continue;
            bestSeat = distance;
            nearbySeat = seats[i];
        }

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

    private void OnGUI()
    {
        EnsureStyles();
        float width = Mathf.Min(440f, Screen.width - 32f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height - 145f;

        if (IsDriving)
        {
            GUI.Box(new Rect(x, y, width, 105f), GUIContent.none);
            GUI.Label(new Rect(x + 12f, y + 9f, width - 24f, 26f),
                $"DRIVING  {currentSeat.Vehicle.SpeedKilometresPerHour:0} km/h", titleStyle);
            GUI.Label(new Rect(x + 12f, y + 42f, width - 24f, 48f),
                "WASD steer and accelerate   •   Space brake   •   F stop driving", hintStyle);
            return;
        }

        if (currentSeat != null)
        {
            GUI.Box(new Rect(x, y, width, 112f), GUIContent.none);
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 26f),
                "SITTING — " + currentSeat.SeatName, titleStyle);
            float buttonWidth = currentSeat.IsDriverSeat ? (width - 36f) * 0.5f : width - 24f;
            if (currentSeat.IsDriverSeat && GUI.Button(new Rect(x + 12f, y + 43f, buttonWidth, 48f), "Drive Truck  [E]"))
                currentSeat.BeginDriving();
            float standX = currentSeat.IsDriverSeat ? x + 24f + buttonWidth : x + 12f;
            if (GUI.Button(new Rect(standX, y + 43f, buttonWidth, 48f), "Stand Up  [F]"))
                StandUp();
            return;
        }

        if (nearbyDoor == null && nearbySeat == null) return;
        GUI.Box(new Rect(x, y, width, 104f), GUIContent.none);
        float cursorX = x + 12f;
        if (nearbyDoor != null)
        {
            string doorText = (nearbyDoor.IsOpen ? "Close " : "Open ") + nearbyDoor.InteractionName + "  [E]";
            if (GUI.Button(new Rect(cursorX, y + 38f, (width - 36f) * 0.5f, 46f), doorText)) nearbyDoor.Toggle();
            cursorX += (width - 36f) * 0.5f + 12f;
        }
        if (nearbySeat != null)
        {
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 25f),
                "Inside the cabin — choose a seat", titleStyle);
            float seatWidth = nearbyDoor != null ? (width - 36f) * 0.5f : width - 24f;
            if (GUI.Button(new Rect(cursorX, y + 38f, seatWidth, 46f),
                    "Sit in " + nearbySeat.SeatName + "  [F]"))
                SitDown(nearbySeat);
        }
        else
        {
            GUI.Label(new Rect(x + 12f, y + 8f, width - 24f, 25f),
                nearbyDoor.InteractionName == "Door"
                    ? "Open the door, then climb into the cabin"
                    : "Interact with the truck", titleStyle);
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
