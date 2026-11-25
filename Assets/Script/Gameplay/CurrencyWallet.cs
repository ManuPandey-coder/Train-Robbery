using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CurrencyWallet : MonoBehaviour
{
    [SerializeField, Min(0)] private int startingBalance = 100;
    [SerializeField] private string currencyName = "Cash";
    [SerializeField] private bool saveBalance = true;
    [SerializeField] private string saveKey = "player.wallet.cash";
    public int Balance { get; private set; }
    public string CurrencyName => currencyName;
    public event Action Changed;

    private void Awake() => Balance = Mathf.Max(0, saveBalance
        ? PlayerPrefs.GetInt(saveKey, startingBalance) : startingBalance);

    public void Add(int amount)
    {
        if (amount <= 0) return;
        Balance = (int)Math.Min(int.MaxValue, (long)Balance + amount);
        Commit();
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > Balance) return false;
        Balance -= amount;
        Commit();
        return true;
    }

    private void Commit()
    {
        if (saveBalance) { PlayerPrefs.SetInt(saveKey, Balance); PlayerPrefs.Save(); }
        Changed?.Invoke();
    }
}
