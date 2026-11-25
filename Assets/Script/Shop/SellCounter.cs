
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

    [Header("Counter Display")]
    public TMP_Text counterDisplay;

    [Header("Selling Settings")]
    [Range(0.1f, 1f)]
    public float sellPercentage = 0.5f;

    private HashSet<ShopWeapon> itemsOnCounter =
        new HashSet<ShopWeapon>();

    // Tracks colliders belonging to each item
    private Dictionary<ShopWeapon, HashSet<Collider>> itemColliders =
        new Dictionary<ShopWeapon, HashSet<Collider>>();

    private ShopWeapon currentItem;

    private class DisplayRow
    {
        public string itemName;
        public int quantity;
        public int totalValue;
    }

    private void Update()
    {
        RemoveInvalidItems();

        FindNearestItem();
        UpdatePrompt();
        UpdateCounterDisplay();

        if (currentItem != null &&
            Keyboard.current != null &&
            Keyboard.current.fKey.wasPressedThisFrame)
        {
            SellItem();
        }
    }

    // Find nearest item
    private void FindNearestItem()
    {
        currentItem = null;

        if (player == null)
            return;

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

    // Update F key prompt
    private void UpdatePrompt()
    {
        if (promptText == null)
            return;

        if (currentItem == null)
        {
            promptText.text =
                "Place an item on the counter to sell";
            return;
        }

        int sellPrice = Mathf.RoundToInt(
            currentItem.weaponData.price * sellPercentage
        );

        promptText.text =
            "Sell " + currentItem.weaponData.weaponName +
            " for $" + sellPrice + " [F]";
    }

    // Update counter table
private void UpdateCounterDisplay()
{
    if (counterDisplay == null)
        return;

    Dictionary<string, DisplayRow> rows =
        new Dictionary<string, DisplayRow>();

    int grandTotal = 0;

    foreach (ShopWeapon item in itemsOnCounter)
    {
        if (item == null || item.weaponData == null)
            continue;

        string itemName = item.weaponData.weaponName;

        int sellPrice = Mathf.RoundToInt(
            item.weaponData.price * sellPercentage
        );

        string key = itemName + "_" + sellPrice;

        if (!rows.ContainsKey(key))
        {
            rows[key] = new DisplayRow
            {
                itemName = itemName,
                quantity = 0,
                totalValue = 0
            };
        }

        rows[key].quantity++;
        rows[key].totalValue += sellPrice;

        grandTotal += sellPrice;
    }

    // Table header
    string output = "<b>SELL COUNTER</b>\n\n";

    output += "<mspace=23px>";
    output += "S.No  ItemName            QTY          TOTAL\n";
    output += "----------------------------------------\n";

    int serial = 1;

    foreach (DisplayRow row in rows.Values)
    {
        string name = row.itemName;

        if (name.Length > 20)
            name = name.Substring(0, 17) + "...";

        output +=
            serial.ToString().PadRight(6) +
            name.PadRight(16) +
            row.quantity.ToString().PadRight(18) +
            "$" + row.totalValue + "\n";

        serial++;
    }

    output += "</mspace>\n";
    output += "----------------------------------------\n";

    if (rows.Count == 0)
        output += "\nNo items on counter\n";

    output += "\n<b>GRAND TOTAL: $" + grandTotal + "</b>";

    counterDisplay.text = output;
}


    // Remove destroyed or invalid items
    private void RemoveInvalidItems()
    {
        List<ShopWeapon> invalidItems =
            new List<ShopWeapon>();

        foreach (ShopWeapon item in itemsOnCounter)
        {
            if (item == null || item.weaponData == null)
                invalidItems.Add(item);
        }

        foreach (ShopWeapon item in invalidItems)
        {
            itemsOnCounter.Remove(item);
            itemColliders.Remove(item);
        }
    }

    // Sell nearest item
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

        // Give money
        playerWallet.AddMoney(sellPrice);

        // Remove from counter tracking
        itemsOnCounter.Remove(soldItem);
        itemColliders.Remove(soldItem);

        currentItem = null;

        // Destroy physical item
        Destroy(soldItem.gameObject);

        UpdateCounterDisplay();

        if (promptText != null)
        {
            promptText.text =
                "Item sold! +$" + sellPrice;
        }
    }

    // Item enters counter
    private void OnTriggerEnter(Collider other)
    {
        ShopWeapon item =other.GetComponentInParent<ShopWeapon>();
        if (item == null)
        return;
        itemsOnCounter.Add(item);
        UpdateCounterDisplay();
    }

    // Item leaves counter
    private void OnTriggerExit(Collider other)
    {ShopWeapon item =other.GetComponentInParent<ShopWeapon>();
          if (item == null)
          return;
          itemsOnCounter.Remove(item);
          itemColliders.Remove(item);
          UpdateCounterDisplay();
          FindNearestItem();
           UpdatePrompt();
    }
}
