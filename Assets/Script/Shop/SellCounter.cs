
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class SellCounter : MonoBehaviour
{
    [Header("References")]
    public PlayerWallet playerWallet;
    public Transform player;
    public TMP_Text promptText;

    [Header("Selling Settings")]
    [Range(0.1f, 1f)]
    public float sellPercentage = 0.5f;

    private HashSet<ShopWeapon> itemsOnCounter =
        new HashSet<ShopWeapon>();

    private ShopWeapon currentItem;

    private void Update()
    {
        FindNearestItem();
        UpdatePrompt();

        if (currentItem != null &&
            Keyboard.current != null &&
            Keyboard.current.fKey.wasPressedThisFrame)
        {
            SellItem();
        }
    }

    private void FindNearestItem()
    {
        currentItem = null;

        float closestDistance = float.MaxValue;

        foreach (ShopWeapon item in itemsOnCounter)
        {
            if (item == null || item.weaponData == null)
                continue;

            float distance = Vector3.Distance(
                player.position,
                item.transform.position
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                currentItem = item;
            }
        }
    }

    private void UpdatePrompt()
    {
        if (promptText == null)
            return;

        if (currentItem == null)
        {
            promptText.text = "Place an item on the counter to sell";
            return;
        }

        int sellPrice = Mathf.RoundToInt(
            currentItem.weaponData.price * sellPercentage
        );

        promptText.text =
            "Sell " + currentItem.weaponData.weaponName +
            " for $" + sellPrice + " [F]";
    }

    private void SellItem()
    {
        if (currentItem == null ||
            currentItem.weaponData == null ||
            playerWallet == null)
            return;

        int sellPrice = Mathf.RoundToInt(
            currentItem.weaponData.price * sellPercentage
        );

        ShopWeapon soldItem = currentItem;

        // Give money to player
        playerWallet.AddMoney(sellPrice);

        // Remove item from counter
        itemsOnCounter.Remove(soldItem);
        currentItem = null;

        // Destroy the physical item
        Destroy(soldItem.gameObject);

        if (promptText != null)
            promptText.text = "Item sold! +" + sellPrice;
    }

    private void OnTriggerEnter(Collider other)
    {
        ShopWeapon item = other.GetComponentInParent<ShopWeapon>();

        if (item != null)
            itemsOnCounter.Add(item);
    }

    private void OnTriggerExit(Collider other)
    {
        ShopWeapon item = other.GetComponentInParent<ShopWeapon>();

        if (item != null)
            itemsOnCounter.Remove(item);
    }
}
