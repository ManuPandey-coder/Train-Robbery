
using UnityEngine;
using TMPro;

public class PlayerWallet : MonoBehaviour
{
    public int startingMoney = 1000;
    public int money;

    public TMP_Text moneyText;

    private void Awake()
    {
        money = startingMoney;
        UpdateMoneyUI();
    }

    public bool CanAfford(int amount)
    {
        return money >= amount;
    }

    public bool SpendMoney(int amount)
    {
        if (amount < 0 || !CanAfford(amount))
            return false;

        money -= amount;
        UpdateMoneyUI();

        return true;
    }

    public void UpdateMoneyUI()
    {
        if (moneyText != null)
            moneyText.text = "$" + money;
    }

    public void AddMoney(int amount)
     {        
       money += amount;
       UpdateMoneyUI();
     }
}
