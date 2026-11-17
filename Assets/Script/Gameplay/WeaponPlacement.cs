
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponPlacement : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform holdPoint;

    [Header("Settings")]
    public float pickupRange = 3f;

    private ShopWeapon heldWeapon;
    private Rigidbody heldBody;
    private Collider heldCollider;

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (heldWeapon == null)
                PickUpWeapon();
            else
                DropWeapon();
        }
    }

    private void PickUpWeapon()
    {
        if (playerCamera == null || holdPoint == null)
            return;

        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward
        );

        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
        {
            ShopWeapon weapon =
                hit.collider.GetComponentInParent<ShopWeapon>();

            if (weapon == null)
                return;

            heldWeapon = weapon;
            heldBody = weapon.GetComponent<Rigidbody>();
            heldCollider = weapon.GetComponent<Collider>();

            if (heldBody != null)
            {
                heldBody.isKinematic = true;
                heldBody.linearVelocity = Vector3.zero;
                heldBody.angularVelocity = Vector3.zero;
            }

            if (heldCollider != null)
                heldCollider.enabled = false;

            heldWeapon.transform.SetParent(holdPoint);
            heldWeapon.transform.localPosition = Vector3.zero;
            heldWeapon.transform.localRotation = Quaternion.identity;
        }
    }

    private void DropWeapon()
    {
        if (heldWeapon == null)
            return;

        heldWeapon.transform.SetParent(null);

        if (heldCollider != null)
            heldCollider.enabled = true;

        if (heldBody != null)
        {
            heldBody.isKinematic = false;
            heldBody.linearVelocity = Vector3.zero;
            heldBody.angularVelocity = Vector3.zero;
        }

        heldWeapon = null;
        heldBody = null;
        heldCollider = null;
    }
}
