
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class ShopCounter : MonoBehaviour
{
    [Header("Player")]
    public Transform player;
    public float interactDistance = 3f;

    [Header("References")]
    public PlayerWallet wallet;
    public WeaponHotbar hotbar;

    [Header("UI")]
    public TMP_Text promptText;

    private HashSet<ShopWeapon> weaponsOnCounter =
        new HashSet<ShopWeapon>();

    private void Start()
    {
        if (promptText != null)
            promptText.text = "";
    }

    private void Update()
    {
        ShopWeapon weapon = GetAvailableWeapon();

        if (weapon == null || player == null)
        {
            ShowMessage("");
            return;
        }

        if (Vector3.Distance(player.position, transform.position)
            > interactDistance)
        {
            ShowMessage("");
            return;
        }

        if (weapon.weaponData == null)
        {
            ShowMessage("Invalid weapon!");
            return;
        }

        ShowMessage(
            "Buy " + weapon.weaponData.weaponName +
            " - $" + weapon.weaponData.price +
            "\nPress [F] to Buy"
        );

        if (Keyboard.current != null &&
            Keyboard.current.fKey.wasPressedThisFrame)
        {
            BuyWeapon(weapon);
        }
    }

    private ShopWeapon GetAvailableWeapon()
    {
        weaponsOnCounter.RemoveWhere(w => w == null);

        if (weaponsOnCounter.Count != 1)
            return null;

        foreach (ShopWeapon weapon in weaponsOnCounter)
            return weapon;

        return null;
    }

    private void BuyWeapon(ShopWeapon shopWeapon)
    {
        if (wallet == null || hotbar == null)
        {
            Debug.LogError("Shop references are missing!");
            return;
        }

        WeaponData data = shopWeapon.weaponData;

        if (data == null)
            return;

        if (data.price < 0)
        {
            ShowMessage("Invalid price!");
            return;
        }

        if (data.weaponPrefab == null)
        {
            ShowMessage("Weapon prefab missing!");
            return;
        }

        if (!wallet.CanAfford(data.price))
        {
            ShowMessage("Not Enough Money!");
            return;
        }

        if (hotbar.AddWeapon(data))
        {
            wallet.SpendMoney(data.price);

            weaponsOnCounter.Remove(shopWeapon);

            Destroy(shopWeapon.gameObject);

            ShowMessage("Purchase Successful!");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        ShopWeapon weapon =
            other.GetComponentInParent<ShopWeapon>();

        if (weapon != null)
            weaponsOnCounter.Add(weapon);
    }

    private void OnTriggerExit(Collider other)
    {
        ShopWeapon weapon =
            other.GetComponentInParent<ShopWeapon>();

        if (weapon != null)
            weaponsOnCounter.Remove(weapon);
    }

    private void ShowMessage(string message)
    {
        if (promptText != null)
            promptText.text = message;
    }
}
