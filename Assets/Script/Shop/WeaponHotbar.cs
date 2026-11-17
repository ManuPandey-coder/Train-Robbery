
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class WeaponHotbar : MonoBehaviour
{
    [Header("Hotbar")]
    public WeaponData[] slots = new WeaponData[10];

    [Header("UI")]
    public Image[] slotIcons;
    public Image[] slotHighlights;

    [Header("Weapon Holding")]
    public Transform weaponHolder;

    private int selectedSlot = -1;
    private GameObject equippedWeapon;

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectSlot(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectSlot(1);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) SelectSlot(2);
        if (Keyboard.current.digit4Key.wasPressedThisFrame) SelectSlot(3);
        if (Keyboard.current.digit5Key.wasPressedThisFrame) SelectSlot(4);
        if (Keyboard.current.digit6Key.wasPressedThisFrame) SelectSlot(5);
        if (Keyboard.current.digit7Key.wasPressedThisFrame) SelectSlot(6);
        if (Keyboard.current.digit8Key.wasPressedThisFrame) SelectSlot(7);
        if (Keyboard.current.digit9Key.wasPressedThisFrame) SelectSlot(8);
        if (Keyboard.current.digit0Key.wasPressedThisFrame) SelectSlot(9);
    }

    public bool AddWeapon(WeaponData weapon)
    {
        if (weapon == null)
            return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = weapon;
                RefreshUI();
                return true;
            }
        }

        Debug.Log("Hotbar is full!");
        return false;
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= slots.Length)
            return;

        if (slots[index] == null)
            return;

        selectedSlot = index;

        EquipWeapon(slots[index]);
        RefreshUI();
    }

    private void EquipWeapon(WeaponData weapon)
    {
        if (weaponHolder == null || weapon.weaponPrefab == null)
            return;

        if (equippedWeapon != null)
            Destroy(equippedWeapon);

        equippedWeapon = Instantiate(
            weapon.weaponPrefab,
            weaponHolder
        );

        equippedWeapon.transform.localPosition = Vector3.zero;
        equippedWeapon.transform.localRotation = Quaternion.identity;
        equippedWeapon.transform.localScale = Vector3.one;
    }

    private void RefreshUI()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slotIcons != null && i < slotIcons.Length &&
                slotIcons[i] != null)
            {
                slotIcons[i].sprite =
                    slots[i] != null ? slots[i].weaponIcon : null;

                slotIcons[i].enabled =
                    slots[i] != null && slots[i].weaponIcon != null;
            }

            if (slotHighlights != null &&
                i < slotHighlights.Length &&
                slotHighlights[i] != null)
            {
                slotHighlights[i].enabled = (i == selectedSlot);
            }
        }
    }
}
