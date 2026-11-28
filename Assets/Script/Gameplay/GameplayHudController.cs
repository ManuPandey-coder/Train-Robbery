using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public sealed class GameplayHudController : MonoBehaviour
{
    private Canvas canvas;
    private PlayerController player;
    private WeaponController weapons;
    private GameObject hudRoot;
    private Button[] slotButtons;
    private TMP_Text[] slotLabels;
    private Image[] slotImages;
    private TMP_Text ammoLabel;
    private float nextRefresh;
    private static Sprite joystickCircle;
    private static GameplayHudController visibleHud;
    public static bool IsTouchHudVisible => visibleHud != null && visibleHud.hudRoot != null &&
        visibleHud.hudRoot.activeInHierarchy;
    private VehicleInteraction vehicle;
    private ClimbController climb;
    private PlayerCamera view;
    private VehicleController drivenVehicle;
    private Vector2 stickInput;
    private bool running, jumpHeld, fireHeld, brakeHeld;
    private Button runButton, aimButton, reloadButton, jumpButton, fireButton, takeButton;
    private Button enterButton, doorButton, seatButton, fallButton;
    private Rect lastSafeArea;
    private Vector2 lastScreenSize;

    private static readonly Color Panel = new Color(0.035f, 0.045f, 0.055f, 0.78f);
    private static readonly Color Slot = new Color(0.07f, 0.085f, 0.095f, 0.9f);
    private static readonly Color Accent = new Color(1f, 0.58f, 0.12f, 0.98f);
    private static readonly Color Text = new Color(0.96f, 0.96f, 0.92f, 1f);

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        weapons = player != null ? player.GetComponent<WeaponController>() : null;
        int count = weapons != null ? Mathf.Max(1,weapons.WeaponCount) : 7;
        slotButtons = new Button[count]; slotLabels = new TMP_Text[count]; slotImages = new Image[count];
        BuildHud();
        BuildAdditionalControls();
        SetGameplayVisible(false);
    }

    private void Update()
    {
        if (hudRoot == null || !hudRoot.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;
        ApplySafeArea();
        RefreshSlots();
        RefreshActions();
    }

    private void LateUpdate()
    {
        if (hudRoot == null || !hudRoot.activeInHierarchy || MenuManager.BlocksGameplay) return;
        ResolveTargets();
        ApplyMovement();
        if (fireHeld && weapons != null && (vehicle == null || !vehicle.IsSeated)) weapons.QueueVirtualFire();
    }

    public void SetGameplayVisible(bool visible)
    {
        if (!visible) ResetTouchInput();
        if (hudRoot != null) hudRoot.SetActive(visible);
        if (visible) visibleHud = this;
        if (visible) ApplySafeArea();
        if (!visible)
        {
            if (player != null) player.SetVirtualMovement(Vector2.zero);
            if (weapons != null) weapons.SetVirtualAim(false);
        }
        RefreshSlots();
        RefreshActions();
    }

    public void OpenMenu()
    {
        MenuManager manager = GetComponent<MenuManager>();
        if (manager != null) manager.OpenMenuFromHud();
    }

    private void ApplySafeArea()
    {
        if (hudRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        Rect area = Screen.safeArea;
        Vector2 size = new Vector2(Screen.width,Screen.height);
        if (area == lastSafeArea && size == lastScreenSize) return;
        lastSafeArea = area; lastScreenSize = size;
        RectTransform rect = hudRoot.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(area.xMin / size.x,area.yMin / size.y);
        rect.anchorMax = new Vector2(area.xMax / size.x,area.yMax / size.y);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void BuildHud()
    {
        if (canvas == null) return;
        Transform existing = transform.Find("GameplayHUD");
        if (existing != null)
        {
            hudRoot = existing.gameObject;
            return;
        }

        hudRoot = new GameObject("GameplayHUD", typeof(RectTransform));
        hudRoot.transform.SetParent(transform, false);
        RectTransform root = hudRoot.GetComponent<RectTransform>();
        Stretch(root);

        // Left movement stick.
        Image stickBase = CreateImage("MovementJoystick", hudRoot.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(132f, 132f), new Vector2(28f, 24f), Panel);
        stickBase.sprite = GetJoystickCircle();
        stickBase.type = Image.Type.Simple;
        stickBase.raycastTarget = true;
        RectTransform knobRect = CreateRect("StickKnob", stickBase.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(54f, 54f), Vector2.zero);
        Image knobImage = knobRect.gameObject.AddComponent<Image>();
        knobImage.sprite = GetJoystickCircle();
        knobImage.color = new Color(1f, 0.68f, 0.25f, 0.88f);
        knobImage.raycastTarget = false;
        MobileJoystick joystick = stickBase.gameObject.AddComponent<MobileJoystick>();
        joystick.Initialize(knobRect, player);

        // All authored guns are switched by visibility; no pickup inventory.
        Image inventoryPanel = CreateImage("InventoryPanel", hudRoot.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(310f, 58f), new Vector2(0f, 16f), Panel);
        for (int i = 0; i < slotButtons.Length; i++)
        {
            int slotIndex = i;
            float stride = Mathf.Min(60f,300f / slotButtons.Length);
            float x = (i - (slotButtons.Length-1)*.5f) * stride;
            Image slotImage = CreateImage("Slot" + (i + 1), inventoryPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(stride-5f, 46f), new Vector2(x, 0f), Slot);
            Button button = slotImage.gameObject.AddComponent<Button>();
            button.targetGraphic = slotImage;
            button.onClick.AddListener(() => SelectSlot(slotIndex));
            slotImages[i] = slotImage;
            slotButtons[i] = button;
            TMP_Text label = CreateText("SlotLabel", slotImage.transform,
                (i + 1) + "\nEMPTY", 10, Text, TextAlignmentOptions.Center);
            RectTransform labelRect = label.rectTransform;
            Stretch(labelRect);
            labelRect.offsetMin = new Vector2(3f, 3f);
            labelRect.offsetMax = new Vector2(-3f, -3f);
            slotLabels[i] = label;
        }

        // Three compact touch actions keep the right side clear.
        Button useButton = CreateHudButton("UseButton", "USE", new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(58f, 58f), new Vector2(-42f, 48f),
            () => weapons?.ToggleHolster(), 11);
        MakeCircular(useButton);
        Button jumpButton = CreateHudButton("JumpButton", "JUMP", new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(58f, 58f), new Vector2(-112f, 48f),
            () => { if (player != null) player.QueueVirtualJump(); }, 11);
        MakeCircular(jumpButton);
        Button fireButton = CreateHudButton("FireButton", "FIRE", new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(68f, 68f), new Vector2(-48f, 126f),
            () => { if (weapons != null) weapons.QueueVirtualFire(); }, 11);
        MakeCircular(fireButton);
        CreateHudButton("MenuButton", "MENU", new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(88f, 40f), new Vector2(-22f, -18f), OpenMenu, 14);
    }

    private void ResolveTargets()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            if (weapons == null) weapons = player.GetComponent<WeaponController>();
            if (vehicle == null) vehicle = player.GetComponent<VehicleInteraction>();
            if (climb == null) climb = player.GetComponent<ClimbController>();
        }
        if (view == null && Camera.main != null) view = Camera.main.GetComponent<PlayerCamera>();
    }

    private void BuildAdditionalControls()
    {
        ResolveTargets();
        if (hudRoot == null) return;
        Transform look = hudRoot.transform.Find("CameraLookArea");
        if (look == null)
        {
            var image = CreateImage("CameraLookArea", hudRoot.transform, new Vector2(.48f,.15f),
                new Vector2(1f,1f), new Vector2(.5f,.5f), Vector2.zero, Vector2.zero, Color.clear);
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.raycastTarget = true;
            look = image.transform;
        }
        look.SetAsFirstSibling();
        var lookPad = look.GetComponent<MobileLookPad>();
        if (lookPad == null) lookPad = look.gameObject.AddComponent<MobileLookPad>();
        lookPad.Dragged = delta => { ResolveTargets(); if (view != null) view.AddTouchLook(delta); };

        var joystick = hudRoot.GetComponentInChildren<MobileJoystick>(true);
        if (joystick != null) joystick.MovementChanged = SetTouchMovement;
        // Reuse the existing inventory slots and actions if the HUD was saved
        // in the scene. Keep their references and layout intact.
        for (int i = 0; i < slotButtons.Length; i++)
        {
            int index = i;
            Transform slot = hudRoot.transform.Find("InventoryPanel/Slot" + (i + 1));
            if (slot == null) continue;
            slotButtons[i] = slot.GetComponent<Button>();
            slotImages[i] = slot.GetComponent<Image>();
            slotLabels[i] = slot.GetComponentInChildren<TMP_Text>(true);
            slotButtons[i].onClick.RemoveAllListeners();
            slotButtons[i].onClick.AddListener(() => SelectSlot(index));
        }

        jumpButton = ActionButton("JumpButton", "JUMP", new Vector2(-24,24), () => {});
        Hold(jumpButton, held =>
        {
            jumpHeld = held;
            if (held && player != null && (climb == null || !climb.IsClimbing)) player.QueueVirtualJump();
            if (climb != null) climb.SetTouchClimbInput(stickInput.y, held);
        });
        fireButton = ActionButton("FireButton", "FIRE", new Vector2(-24,100), () => {});
        Hold(fireButton, held => { fireHeld = held; brakeHeld = held; ApplyMovement(); });
        takeButton = ActionButton("UseButton", "HOLSTER", new Vector2(-96,24), () => weapons?.ToggleHolster());
        aimButton = ActionButton("AimButton", "AIM", new Vector2(-96,100), () => {});
        Hold(aimButton, held => { if (weapons != null) weapons.SetVirtualAim(held); });
        reloadButton = ActionButton("ReloadButton", "RELOAD", new Vector2(-168,170), () => weapons?.ReloadFromHud());
        var letGo = ActionButton("LetGoButton", "LET GO", new Vector2(-168,24), () => climb?.ReleaseFromHud());
        letGo.gameObject.SetActive(false);
        foreach (string obsolete in new[]{"DropButton","ThrowButton"})
        { var old=hudRoot.transform.Find(obsolete); if(old!=null) old.gameObject.SetActive(false); }
        ammoLabel = CreateText("WeaponAmmo",hudRoot.transform,"",16,Text,TextAlignmentOptions.Center);
        RectTransform ammoRect=ammoLabel.rectTransform;
        ammoRect.anchorMin=ammoRect.anchorMax=new Vector2(.5f,1f);
        ammoRect.pivot=new Vector2(.5f,1f); ammoRect.sizeDelta=new Vector2(260,44); ammoRect.anchoredPosition=new Vector2(0,-18);
        runButton = ActionButton("RunButton", "RUN", new Vector2(26,174), () => { running = !running; ApplyMovement(); RefreshActions(); });
        Place(runButton, new Vector2(0,0), new Vector2(0,0), new Vector2(68,42), new Vector2(26,174));
        fallButton = ActionButton("FallButton", "FALL", new Vector2(26,228), () =>
        { if (player == null) return; if (player.IsKnockedDown) player.RecoverFromRagdoll(); else player.TriggerRagdoll(); });
        Place(fallButton, Vector2.zero, Vector2.zero, new Vector2(68,42), new Vector2(26,228));
        var viewButton = ActionButton("ViewButton", "VIEW", new Vector2(-24,-70), () => view?.ToggleViewFromHud());
        Place(viewButton, Vector2.one, Vector2.one, new Vector2(68,40), new Vector2(-24,-70));
        enterButton = ActionButton("VehicleButton", "GET IN", Vector2.zero, () => vehicle?.EnterOrExitFromHud());
        doorButton = ActionButton("DoorButton", "OPEN", Vector2.zero, () => vehicle?.DoorFromHud());
        seatButton = ActionButton("SeatButton", "SEAT", Vector2.zero, () => vehicle?.SwitchSeatFromHud());
        Place(enterButton, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(76,42), new Vector2(-85,88));
        Place(doorButton, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(76,42), new Vector2(0,88));
        Place(seatButton, new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(76,42), new Vector2(85,88));
    }

    private Button ActionButton(string name, string label, Vector2 position, UnityEngine.Events.UnityAction clicked)
    {
        Transform existing = hudRoot.transform.Find(name);
        Button button = existing != null ? existing.GetComponent<Button>() : null;
        if (button == null) button = CreateHudButton(name, label, Vector2.right, Vector2.right,
            Vector2.right, new Vector2(58,58), position, clicked, 11);
        else { button.onClick.RemoveAllListeners(); button.onClick.AddListener(clicked); }
        Place(button, Vector2.right, Vector2.right, new Vector2(58,58), position);
        SetLabel(button, label);
        MakeCircular(button);
        return button;
    }

    private static void Place(Button button, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position;
    }

    private static void Hold(Button button, System.Action<bool> pressed)
    {
        button.onClick.RemoveAllListeners();
        var hold = button.GetComponent<MobileHoldButton>();
        if (hold == null) hold = button.gameObject.AddComponent<MobileHoldButton>();
        hold.Pressed = pressed;
    }

    public void SetTouchMovement(Vector2 value) { stickInput = Vector2.ClampMagnitude(value,1f); ApplyMovement(); }

    private void ApplyMovement()
    {
        VehicleController nextVehicle = vehicle != null && vehicle.IsDriving ? vehicle.CurrentVehicle : null;
        if (drivenVehicle != null && drivenVehicle != nextVehicle) drivenVehicle.SetTouchDriveInput(Vector2.zero,false);
        drivenVehicle = nextVehicle;
        if (drivenVehicle != null) drivenVehicle.SetTouchDriveInput(stickInput,brakeHeld);
        if (player != null) player.SetVirtualMovement(vehicle != null && vehicle.IsSeated ? Vector2.zero : stickInput,running);
        if (climb != null) climb.SetTouchClimbInput(stickInput.y,jumpHeld);
    }

    private void ResetTouchInput()
    {
        stickInput = Vector2.zero;
        running = jumpHeld = fireHeld = brakeHeld = false;
        if (hudRoot != null)
        {
            foreach(var hold in hudRoot.GetComponentsInChildren<MobileHoldButton>(true)) hold.Release();
            hudRoot.GetComponentInChildren<MobileJoystick>(true)?.Release();
            hudRoot.GetComponentInChildren<MobileLookPad>(true)?.Release();
        }
        if (weapons != null) weapons.ClearTouchInput();
        if (drivenVehicle != null) drivenVehicle.SetTouchDriveInput(Vector2.zero,false);
        if (climb != null) climb.SetTouchClimbInput(0f,false);
        if (player != null) player.ClearTouchInput();
    }

    private void RefreshActions()
    {
        if (runButton == null) return;
        ResolveTargets();
        bool seated = vehicle != null && vehicle.IsSeated;
        bool climbing = climb != null && climb.IsClimbing;
        bool hasWeapon = weapons != null && weapons.HeldWeapon != null;
        if(ammoLabel!=null) ammoLabel.text = !hasWeapon ? "" : weapons.HeldWeapon.DisplayName.ToUpperInvariant()+"  "+
            weapons.HeldWeapon.Rounds+" / "+weapons.HeldWeapon.Reserve+(weapons.IsReloading ? "   RELOADING" : "");
        bool canUseWeapon = player != null && !player.IsKnockedDown && !player.IsExternallyControlled;
        bool driving = vehicle != null && vehicle.IsDriving;
        takeButton.gameObject.SetActive(!seated && !climbing);
        takeButton.interactable = canUseWeapon;
        runButton.gameObject.SetActive(!seated && !climbing);
        runButton.GetComponent<Image>().color = running ? Accent : Panel;
        SetLabel(jumpButton,climbing ? "CLIMB" : "JUMP");
        jumpButton.gameObject.SetActive(!seated);
        SetLabel(fireButton,driving ? "BRAKE" : "FIRE");
        fireButton.gameObject.SetActive(!seated || driving);
        fireButton.interactable = driving || (hasWeapon && canUseWeapon);
        aimButton.gameObject.SetActive(!seated);
        aimButton.interactable = hasWeapon && canUseWeapon;
        reloadButton.gameObject.SetActive(!seated);
        reloadButton.interactable = hasWeapon && canUseWeapon;
        Transform letGo=hudRoot.transform.Find("LetGoButton");
        if(letGo!=null) letGo.gameObject.SetActive(climbing);
        enterButton.gameObject.SetActive(vehicle != null && (seated || vehicle.CanEnterFromHud));
        SetLabel(enterButton,seated ? "GET OUT" : "GET IN");
        doorButton.gameObject.SetActive(vehicle != null && vehicle.CanUseDoorFromHud);
        if (vehicle != null) SetLabel(doorButton,vehicle.DoorActionLabel);
        seatButton.gameObject.SetActive(seated);
        fallButton.gameObject.SetActive(!seated && !climbing);
        SetLabel(fallButton,player != null && player.IsKnockedDown ? "GET UP" : "FALL");
    }

    private static void SetLabel(Button button,string value)
    {
        TMP_Text text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (text != null) { text.text=value; text.enableAutoSizing=true; text.fontSizeMin=8; text.fontSizeMax=12; }
    }

    private void OnApplicationFocus(bool focused) { if(!focused) ResetTouchInput(); }
    private void OnDisable() { ResetTouchInput(); if (visibleHud == this) visibleHud = null; }

    private void SelectSlot(int index)
    {
        if (weapons == null) return;
        weapons.SelectWeapon(index);
        RefreshSlots();
    }

    private static void MakeCircular(Button button)
    {
        Image image = button != null ? button.GetComponent<Image>() : null;
        if (image == null) return;
        image.sprite = GetJoystickCircle();
        image.type = Image.Type.Simple;
    }

    private void RefreshSlots()
    {
        if (weapons == null) return;
        for (int i = 0; i < slotLabels.Length; i++)
        {
            if (slotLabels[i] == null) continue;
            string item = weapons.GetWeaponName(i);
            slotLabels[i].text = string.IsNullOrEmpty(item) ? "EMPTY" : item.ToUpperInvariant();
            slotLabels[i].enableAutoSizing = true;
            slotLabels[i].fontSizeMin = 5f;
            slotLabels[i].fontSizeMax = 10f;
            slotLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
            slotLabels[i].overflowMode = TextOverflowModes.Ellipsis;
            if (slotImages[i] != null)
                slotImages[i].color = i == weapons.SelectedWeapon && weapons.HeldWeapon!=null ? Accent : Slot;
        }
    }

    private Button CreateHudButton(string name, string label, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction onClick, int fontSize = 17)
    {
        Image image = CreateImage(name, hudRoot.transform, anchorMin, anchorMax, pivot, size, position, Accent);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        TMP_Text text = CreateText("Label", image.transform, label, fontSize, Text, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.raycastTarget = false;
        return button;
    }

    private static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 size, Vector2 position, Color color)
    {
        RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, pivot, size, position);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, int size,
        Color color, TextAlignmentOptions alignment)
    {
        GameObject item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        TMP_Text text = item.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position)
    {
        GameObject item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Sprite GetJoystickCircle()
    {
        if (joystickCircle != null) return joystickCircle;
        const int size = 96;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "HUD Joystick Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
            float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
            float edge = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * size * 0.5f);
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
        }
        texture.Apply(false, true);
        joystickCircle = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return joystickCircle;
    }
}
