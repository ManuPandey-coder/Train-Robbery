using System;
using UnityEngine;

[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CurrencyWallet))]
public sealed class PlayerInventory : MonoBehaviour
{
    [Header("Inventory")]
    [SerializeField, Range(1, 9)] private int slotCount = 8;
    [SerializeField, Min(0.5f)] private float grabDistance = 3f;
    [SerializeField, Min(1f)] private float maximumCarryMass = 30f;
    [Header("Throw")]
    [SerializeField, Min(0f)] private float throwSpeed = 10f;
    [SerializeField, Min(0f)] private float throwLift = 1.5f;
    [Header("Controls")]
    [SerializeField] private KeyCode grabKey = KeyCode.E;
    [SerializeField] private KeyCode storeKey = KeyCode.V;
    [SerializeField] private KeyCode throwKey = KeyCode.G;
    [SerializeField] private KeyCode buyKey = KeyCode.B;
    [SerializeField] private KeyCode inventoryKey = KeyCode.I;
    public static bool IsUIOpen { get; private set; }
    public event Action Changed;
    public InventoryItem HeldItem { get; private set; }
    public InventoryItem[] Slots { get; private set; }
    public int SelectedSlot { get; private set; } = -1;
    public string Message { get; private set; } = "E carry  |  V store  |  G throw  |  I inventory";
    public CurrencyWallet Wallet { get; private set; }
    private PlayerController player;
    private WeaponController weapons;

    private void Awake()
    {
        Slots = new InventoryItem[slotCount];
        Wallet = GetComponent<CurrencyWallet>();
        IsUIOpen = false;
    }

    private void Start()
    {
        player = GetComponent<PlayerController>();
        weapons = GetComponent<WeaponController>();
        if (GetComponent<InventoryHud>() == null) gameObject.AddComponent<InventoryHud>();
    }

    private void Update()
    {
        if (MenuManager.IsMenuOpen)
        { if (IsUIOpen) SetUIOpen(false); return; }
        if (player == null) return;
        if (player.IsVehicleSeated || player.IsExternallyControlled || player.IsKnockedDown)
        {
            if (HeldItem != null && HeldItem.Weapon == null) ReleaseHeld(false);
            if (IsUIOpen) SetUIOpen(false);
            return;
        }
        // Existing vehicle/climb logic may have released a weapon.
        if (HeldItem != null && HeldItem.Weapon != null && weapons.HeldWeapon != HeldItem.Weapon)
        { RemoveSlot(HeldItem); HeldItem = null; SelectedSlot = -1; Changed?.Invoke(); }
        if (Input.GetKeyDown(inventoryKey)) SetUIOpen(!IsUIOpen);
        if (IsUIOpen) return;
        for (int i = 0; i < Slots.Length; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) EquipSlot(i);
        if (Input.GetKeyDown(grabKey)) GrabLookedItem();
        if (Input.GetKeyDown(storeKey)) StoreHeld();
        if (Input.GetKeyDown(throwKey)) ReleaseHeld(true);
        if (Input.GetKeyDown(buyKey)) BuyHeld();
    }

    public void SetUIOpen(bool open)
    {
        IsUIOpen = open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        Changed?.Invoke();
    }

    public void GrabLookedItem()
    {
        Camera view = Camera.main;
        if (view == null) return;
        RaycastHit[] hits = Physics.RaycastAll(view.ViewportPointToRay(new Vector3(.5f, .5f)),
            grabDistance + (view.transform.position - player.CameraAnchorPosition).magnitude,
            ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (Vector3.Distance(hit.point, player.CameraAnchorPosition) > grabDistance) return;
            InventoryItem item = hit.collider.GetComponentInParent<InventoryItem>();
            PickupWeapon gun = hit.collider.GetComponentInParent<PickupWeapon>();
            if (item == null && gun != null)
                item = gun.gameObject.AddComponent<InventoryItem>();
            if (item == null && hit.rigidbody != null && !hit.rigidbody.isKinematic &&
                hit.rigidbody.mass <= maximumCarryMass &&
                hit.rigidbody.GetComponentInParent<PlayerController>() == null &&
                hit.rigidbody.GetComponentInParent<VehicleController>() == null &&
                hit.rigidbody.GetComponentInParent<ClimbableSurface>() == null)
                item = hit.rigidbody.gameObject.AddComponent<InventoryItem>();
            if (item != null) Hold(item);
            return; // solid scenery blocks interaction with items behind it
        }
    }

    private void Hold(InventoryItem item)
    {
        if (item == null || item == HeldItem) return;
        if (item.Weapon != null && item.Weapon.IsHeld) return;
        if (HeldItem != null && !StoreHeld()) ReleaseHeld(false);
        item.gameObject.SetActive(true);
        if (item.Weapon != null)
        { if (weapons == null || !weapons.EquipWeapon(item.Weapon)) return; }
        else item.BeginCarry(player.RightHand != null ? player.RightHand : player.Pelvis);
        HeldItem = item;
        SelectedSlot = Array.IndexOf(Slots, item);
        Notify(item.Owned ? "V store  |  G throw" : $"Unowned: B buy ({item.Price})  |  G throw");
    }

    public bool StoreHeld()
    {
        if (HeldItem == null) return true;
        InventoryItem item = HeldItem;
        if (!item.Owned) { Notify("Only owned items can be stored. B buys this item."); return false; }
        int index = Array.IndexOf(Slots, item);
        if (index < 0) index = Array.IndexOf(Slots, null);
        if (index < 0) { Notify("Inventory is full."); return false; }
        DetachHeld(player.CameraAnchorPosition, Vector3.zero);
        item.transform.SetParent(transform, true);
        item.gameObject.SetActive(false);
        Slots[index] = item;
        HeldItem = null;
        SelectedSlot = -1;
        Notify("Stored " + item.DisplayName);
        return true;
    }

    public void EquipSlot(int index)
    {
        if (index < 0 || index >= Slots.Length || Slots[index] == null) return;
        if (HeldItem == Slots[index]) { StoreHeld(); return; }
        Hold(Slots[index]);
    }

    public void ReleaseHeld(bool thrown)
    {
        if (HeldItem == null) return;
        Camera view = Camera.main;
        Vector3 forward = view != null ? view.transform.forward : player.FacingDirection;
        Vector3 position = player.CameraAnchorPosition + Vector3.up * .4f + forward * .65f;
        // Stop a release position from passing through a nearby wall.
        if (Physics.Raycast(player.CameraAnchorPosition + Vector3.up * .4f, forward,
            out RaycastHit hit, .8f, ~0, QueryTriggerInteraction.Ignore) &&
            !hit.collider.transform.IsChildOf(transform)) position = hit.point - forward * .15f;
        Vector3 velocity = player.MovementVelocity + (thrown ? forward * throwSpeed + Vector3.up * throwLift : Vector3.zero);
        InventoryItem item = HeldItem;
        DetachHeld(position, velocity);
        RemoveSlot(item);
        HeldItem = null;
        SelectedSlot = -1;
        Notify(thrown ? "Thrown " + item.DisplayName : "Dropped " + item.DisplayName);
    }

    private void DetachHeld(Vector3 position, Vector3 velocity)
    {
        if (HeldItem.Weapon != null) weapons.ReleaseEquippedWeapon(position, velocity);
        else HeldItem.Release(position, velocity);
        HeldItem.transform.SetParent(null, true);
    }

    public bool BuyHeld()
    {
        if (HeldItem == null || HeldItem.Owned) return false;
        if (!Wallet.TrySpend(HeldItem.Price)) { Notify("Not enough " + Wallet.CurrencyName); return false; }
        HeldItem.SetOwned(true);
        Notify("Owned: " + HeldItem.DisplayName + "  |  V store");
        return true;
    }

    private void RemoveSlot(InventoryItem item)
    {
        int index = Array.IndexOf(Slots, item);
        if (index >= 0) Slots[index] = null;
    }

    private void Notify(string message) { Message = message; Changed?.Invoke(); }
    private void OnDisable() { if (IsUIOpen) SetUIOpen(false); }
}
