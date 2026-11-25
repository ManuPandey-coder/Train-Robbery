using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class InventoryHud : MonoBehaviour
{
    private PlayerInventory inventory;
    private GameObject canvasRoot;
    private GameObject details;
    private TMP_Text money, hint, itemName, ownership;
    private UnityEngine.UI.RawImage preview;
    private UnityEngine.UI.Image[] slotImages;
    private UnityEngine.UI.RawImage[] slotPreviews;
    private TMP_Text[] slotLabels;
    private readonly Dictionary<InventoryItem, RenderTexture> thumbnails = new Dictionary<InventoryItem, RenderTexture>();
    private readonly List<Mesh> bakedMeshes = new List<Mesh>();
    private readonly Color panelColor = new Color(.07f, .09f, .12f, .95f);

    private void Start()
    {
        inventory = GetComponent<PlayerInventory>();
        if (inventory == null) { enabled = false; return; }
        canvasRoot = new GameObject("Inventory Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        Transform root = canvasRoot.transform;
        money = Text(root, "Currency", "", new Vector2(.79f, .91f), new Vector2(.97f, .98f), 30);
        money.alignment = TextAlignmentOptions.Right;
        hint = Text(root, "Controls", "", new Vector2(.12f, .15f), new Vector2(.88f, .21f), 22);
        slotImages = new UnityEngine.UI.Image[inventory.Slots.Length];
        slotPreviews = new UnityEngine.UI.RawImage[slotImages.Length];
        slotLabels = new TMP_Text[slotImages.Length];
        float width = .062f, gap = .007f;
        float start = .5f - (slotImages.Length * (width + gap) - gap) * .5f;
        for (int i = 0; i < slotImages.Length; i++)
        {
            int index = i;
            var slot = Panel(root, "Slot " + (i + 1),
                new Vector2(start + i * (width + gap), .025f),
                new Vector2(start + i * (width + gap) + width, .14f));
            slotImages[i] = slot;
            slot.raycastTarget = true;
            var button = slot.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = slot;
            button.onClick.AddListener(() => { if (PlayerInventory.IsUIOpen) inventory.EquipSlot(index); });
            slotPreviews[i] = Raw(slot.transform, "Item Preview", new Vector2(.08f, .24f), new Vector2(.92f, .9f));
            slotLabels[i] = Text(slot.transform, "Name", "Empty", new Vector2(.04f, .02f), new Vector2(.96f, .25f), 16);
            Text(slot.transform, "Key", (i + 1).ToString(), new Vector2(.02f, .73f), new Vector2(.25f, .98f), 18);
        }
        var card = Panel(root, "Item Details", new Vector2(.33f, .27f), new Vector2(.67f, .84f));
        details = card.gameObject;
        itemName = Text(card.transform, "Title", "INVENTORY", new Vector2(.05f, .86f), new Vector2(.95f, .97f), 32);
        preview = Raw(card.transform, "Selected Preview", new Vector2(.08f, .28f), new Vector2(.92f, .85f));
        ownership = Text(card.transform, "Ownership", "", new Vector2(.05f, .16f), new Vector2(.95f, .28f), 22);
        MakeButton(card.transform, "Store", new Vector2(.05f, .04f), new Vector2(.34f, .15f), () => inventory.StoreHeld());
        MakeButton(card.transform, "Buy", new Vector2(.36f, .04f), new Vector2(.64f, .15f), () => inventory.BuyHeld());
        MakeButton(card.transform, "Throw", new Vector2(.66f, .04f), new Vector2(.95f, .15f), () => inventory.ReleaseHeld(true));
        inventory.Changed += Refresh;
        inventory.Wallet.Changed += Refresh;
        Refresh();
    }

    private void Update()
    {
        if (canvasRoot == null) return;
        canvasRoot.SetActive(!MenuManager.IsMenuOpen);
        details.SetActive(PlayerInventory.IsUIOpen);
    }

    private void Refresh()
    {
        if (money == null) return;
        money.text = inventory.Wallet.CurrencyName + "  " + inventory.Wallet.Balance.ToString("N0");
        hint.text = inventory.Message;
        InventoryItem selected = inventory.HeldItem;
        itemName.text = selected != null ? selected.DisplayName : "INVENTORY";
        ownership.text = selected == null ? "Select a slot below  |  I close" :
            selected.Owned ? "OWNED  |  V store  |  G throw" : "UNOWNED  |  B buy for " + selected.Price;
        preview.texture = GetPreview(selected);
        preview.enabled = selected != null;
        for (int i = 0; i < slotImages.Length; i++)
        {
            InventoryItem item = inventory.Slots[i];
            slotImages[i].color = i == inventory.SelectedSlot ? new Color(.65f, .4f, .12f, .98f) : panelColor;
            slotLabels[i].text = item != null ? item.DisplayName : "Empty";
            slotPreviews[i].texture = GetPreview(item);
            slotPreviews[i].enabled = item != null;
        }
    }

    private RenderTexture GetPreview(InventoryItem item)
    {
        if (item == null) return null;
        if (thumbnails.TryGetValue(item, out RenderTexture cached)) return cached;
        // Copy renderers only. Never instantiate the item's gameplay scripts,
        // rigidbodies, pickup logic or ammunition just to draw a thumbnail.
        var stage = new GameObject("Inventory Preview Stage");
        stage.transform.position = new Vector3(0, 10000, 0);
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (Renderer source in item.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skinned)
            { mesh = new Mesh(); skinned.BakeMesh(mesh); bakedMeshes.Add(mesh); }
            else
            { var filter = source.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
            if (mesh == null) continue;
            var visual = new GameObject("Preview Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            visual.layer = 31;
            visual.transform.SetParent(stage.transform, false);
            visual.transform.localPosition = item.transform.InverseTransformPoint(source.transform.position);
            visual.transform.localRotation = Quaternion.Inverse(item.transform.rotation) * source.transform.rotation;
            Vector3 scale = source.transform.lossyScale;
            visual.transform.localScale = scale;
            visual.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
        }
        if (!hasBounds) { Destroy(stage); return null; }
        var cameraObject = new GameObject("Preview Camera", typeof(Camera));
        cameraObject.transform.SetParent(stage.transform, false);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.1f, .13f, .17f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(.05f, bounds.size.magnitude * .58f);
        float distance = Mathf.Max(1f, bounds.size.magnitude * 3f);
        camera.transform.position = bounds.center + new Vector3(1, .6f, -1).normalized * distance;
        camera.transform.LookAt(bounds.center);
        camera.nearClipPlane = .01f;
        camera.farClipPlane = distance * 3f;
        var lightObject = new GameObject("Preview Light", typeof(Light));
        lightObject.transform.SetParent(stage.transform, false);
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.cullingMask = 1 << 31;
        light.intensity = 1.6f;
        light.transform.rotation = Quaternion.Euler(35, -30, 0);
        var texture = new RenderTexture(256, 256, 24);
        texture.Create();
        camera.targetTexture = texture;
        camera.Render();
        camera.targetTexture = null;
        stage.SetActive(false);
        Destroy(stage);
        thumbnails[item] = texture;
        return texture;
    }

    private UnityEngine.UI.Image Panel(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        Rect(go.GetComponent<RectTransform>(), min, max);
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.color = panelColor; image.raycastTarget = false;
        return image;
    }

    private static UnityEngine.UI.RawImage Raw(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        go.transform.SetParent(parent, false);
        Rect(go.GetComponent<RectTransform>(), min, max);
        var image = go.GetComponent<UnityEngine.UI.RawImage>(); image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Text(Transform parent, string name, string value, Vector2 min, Vector2 max, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Rect(go.GetComponent<RectTransform>(), min, max);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = value; text.fontSize = size; text.color = new Color(.94f, .93f, .88f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true; text.fontSizeMin = 10; text.fontSizeMax = size;
        text.raycastTarget = false;
        return text;
    }

    private void MakeButton(Transform parent, string name, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
    {
        var panel = Panel(parent, name, min, max);
        panel.color = new Color(.35f, .25f, .14f); panel.raycastTarget = true;
        var button = panel.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = panel; button.onClick.AddListener(action);
        Text(panel.transform, "Label", name.ToUpperInvariant(), Vector2.zero, Vector2.one, 21);
    }

    private static void Rect(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }

    private void OnDestroy()
    {
        if (inventory != null) { inventory.Changed -= Refresh; if (inventory.Wallet != null) inventory.Wallet.Changed -= Refresh; }
        if (canvasRoot != null) Destroy(canvasRoot);
        foreach (RenderTexture texture in thumbnails.Values) { if (texture != null) { texture.Release(); Destroy(texture); } }
        foreach (Mesh mesh in bakedMeshes) if (mesh != null) Destroy(mesh);
    }
}
