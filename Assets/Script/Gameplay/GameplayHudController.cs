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
    private readonly Button[] slotButtons = new Button[5];
    private readonly TMP_Text[] slotLabels = new TMP_Text[5];
    private readonly Image[] slotImages = new Image[5];
    private float nextRefresh;
    private static Sprite joystickCircle;

    private static readonly Color Panel = new Color(0.035f, 0.045f, 0.055f, 0.78f);
    private static readonly Color Slot = new Color(0.07f, 0.085f, 0.095f, 0.9f);
    private static readonly Color Accent = new Color(1f, 0.58f, 0.12f, 0.98f);
    private static readonly Color Text = new Color(0.96f, 0.96f, 0.92f, 1f);

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        weapons = player != null ? player.GetComponent<WeaponController>() : null;
        BuildHud();
        SetGameplayVisible(false);
    }

    private void Update()
    {
        if (hudRoot == null || !hudRoot.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;
        RefreshSlots();
    }

    public void SetGameplayVisible(bool visible)
    {
        if (hudRoot != null) hudRoot.SetActive(visible);
        if (!visible)
        {
            if (player != null) player.SetVirtualMovement(Vector2.zero);
            if (weapons != null) weapons.SetVirtualAim(false);
        }
        RefreshSlots();
    }

    public void OpenMenu()
    {
        MenuManager manager = GetComponent<MenuManager>();
        if (manager != null) manager.OpenMenuFromHud();
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

        // Compact five-slot inventory strip along the bottom center.
        Image inventoryPanel = CreateImage("InventoryPanel", hudRoot.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(310f, 58f), new Vector2(0f, 16f), Panel);
        for (int i = 0; i < slotButtons.Length; i++)
        {
            int slotIndex = i;
            float x = (i - 2) * 60f;
            Image slotImage = CreateImage("Slot" + (i + 1), inventoryPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(54f, 46f), new Vector2(x, 0f), Slot);
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
            () => { if (weapons != null) weapons.TryPickupFromHud(); }, 11);
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

    private void SelectSlot(int index)
    {
        if (weapons == null) return;
        weapons.SelectInventorySlot(index);
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
            string item = weapons.GetInventorySlotName(i);
            slotLabels[i].text = (i + 1) + "\n" + (string.IsNullOrEmpty(item) ? "EMPTY" : item.ToUpperInvariant());
            if (slotImages[i] != null)
                slotImages[i].color = i == weapons.SelectedInventorySlot ? Accent : Slot;
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

public sealed class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private RectTransform baseRect;
    private RectTransform knob;
    private PlayerController player;
    private float radius = 38f;

    public void Initialize(RectTransform knobRect, PlayerController target)
    {
        baseRect = transform as RectTransform;
        knob = knobRect;
        player = target;
    }

    public void OnPointerDown(PointerEventData eventData) { OnDrag(eventData); }

    public void OnDrag(PointerEventData eventData)
    {
        if (baseRect == null || knob == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                baseRect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
        Vector2 value = Vector2.ClampMagnitude(local / radius, 1f);
        knob.anchoredPosition = value * radius;
        if (player != null) player.SetVirtualMovement(value);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (knob != null) knob.anchoredPosition = Vector2.zero;
        if (player != null) player.SetVirtualMovement(Vector2.zero);
    }
}
