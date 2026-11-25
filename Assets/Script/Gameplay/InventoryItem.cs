using UnityEngine;

[DisallowMultipleComponent]
public sealed class InventoryItem : MonoBehaviour
{
    [SerializeField] private string displayName;
    [Tooltip("Unowned items may be carried and thrown, but cannot be stored.")]
    [SerializeField] private bool ownedByPlayer;
    [SerializeField, Min(0)] private int purchasePrice = 25;
    [SerializeField] private Vector3 handPosition = new Vector3(0f, 0f, 0.08f);
    [SerializeField] private Vector3 handRotation;
    public bool Owned => ownedByPlayer;
    public int Price => purchasePrice;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName :
        Weapon != null ? Weapon.DisplayName : name.Replace("(Clone)", "").Trim();
    public PickupWeapon Weapon => GetComponent<PickupWeapon>();
    private Rigidbody body;
    private Collider[] colliders;
    private bool[] colliderStates;
    private Transform worldParent;
    private bool carried;
    private Collider[] carrierColliders;

    public void SetOwned(bool owned) => ownedByPlayer = owned;

    public void BeginCarry(Transform hand)
    {
        if (carried || hand == null || Weapon != null) return;
        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
        colliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        { colliderStates[i] = colliders[i].enabled; colliders[i].enabled = false; }
        worldParent = transform.parent;
        PlayerController owner = hand.GetComponentInParent<PlayerController>();
        carrierColliders = owner != null ? owner.GetComponentsInChildren<Collider>(true) : null;
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.detectCollisions = false;
        transform.SetParent(hand, false);
        transform.SetLocalPositionAndRotation(handPosition, Quaternion.Euler(handRotation));
        carried = true;
    }

    public void Release(Vector3 position, Vector3 velocity)
    {
        if (!carried) return;
        carried = false;
        transform.SetParent(worldParent, true);
        body.position = position;
        body.isKinematic = false;
        body.useGravity = true;
        body.detectCollisions = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderStates[i];
        // A released prop starts near the hands. Ignore the carrier until it
        // has separated, so throwing cannot kick the player or trap the prop.
        if (carrierColliders != null) StartCoroutine(RestoreCarrierContacts());
    }

    private System.Collections.IEnumerator RestoreCarrierContacts()
    {
        Collider[] owners = carrierColliders;
        foreach (Collider item in colliders)
            foreach (Collider owner in owners)
                if (item != null && owner != null) Physics.IgnoreCollision(item, owner, true);
        yield return new WaitForSeconds(.25f);
        bool overlapping;
        do
        {
            overlapping = false;
            foreach (Collider item in colliders)
                foreach (Collider owner in owners)
                {
                    if (item == null || owner == null || !owner.enabled || !item.enabled) continue;
                    if (item.bounds.Intersects(owner.bounds)) overlapping = true;
                }
            if (overlapping) yield return new WaitForFixedUpdate();
        } while (overlapping && !carried);
        foreach (Collider item in colliders)
            foreach (Collider owner in owners)
                if (item != null && owner != null) Physics.IgnoreCollision(item, owner, false);
    }
}
