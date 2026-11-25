using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class WeaponPriceDisplay : MonoBehaviour
{
    [Header("Weapon Price")]
    public int price = 100;

    [Header("UI")]
    public TextMeshPro priceText;
    public TextMeshPro purchaseText;

    [Header("Player")]
    public PlayerWallet playerWallet;
    public Camera playerCamera;

    [Header("Settings")]
    public float interactionRange = 3f;
    public Vector3 textOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Purchase Event")]
    public UnityEvent onPurchased;

    private bool purchased;

    private void Start()
    {
        if (playerWallet == null)
            playerWallet = FindFirstObjectByType<PlayerWallet>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        UpdatePriceText();
    }

    private void Update()
    {
        if (purchased || Keyboard.current == null)
            return;

        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            TryPurchase();
        }
    }

    private void LateUpdate()
    {
        if (priceText != null)
        {
            priceText.transform.position = transform.position + textOffset;
            FaceCamera(priceText.transform);
        }

        if (purchaseText != null)
        {
            purchaseText.transform.position =
                transform.position + textOffset - new Vector3(0f, 0.3f, 0f);

            FaceCamera(purchaseText.transform);
        }
    }

    private void FaceCamera(Transform target)
    {
        if (playerCamera == null)
            return;

        target.rotation = Quaternion.LookRotation(
            target.position - playerCamera.transform.position
        );
    }

    private void UpdatePriceText()
    {
        if (priceText != null)
            priceText.text = "$" + price;

        if (purchaseText != null)
            purchaseText.text = "Press F to Purchase";
    }

    private void TryPurchase()
    {
        if (playerCamera == null || playerWallet == null)
            return;

        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward
        );

        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {
            WeaponPriceDisplay weapon =
                hit.collider.GetComponentInParent<WeaponPriceDisplay>();

            if (weapon == this)
                Purchase();
        }
    }

    private void Purchase()
    {
        if (!playerWallet.SpendMoney(price))
        {
            Debug.Log("Not enough money!");
            return;
        }

        purchased = true;

        if (priceText != null)
            priceText.gameObject.SetActive(false);

        if (purchaseText != null)
            purchaseText.gameObject.SetActive(false);

        Debug.Log("Weapon purchased!");

        onPurchased?.Invoke();
    }
}