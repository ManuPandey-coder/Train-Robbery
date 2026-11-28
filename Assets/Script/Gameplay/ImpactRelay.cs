using UnityEngine;

[DisallowMultipleComponent]
public sealed class ImpactRelay : MonoBehaviour
{
    public PlayerController owner;

    private void OnCollisionEnter(Collision collision)
    {
        if (owner != null) owner.NotifyImpact(collision.relativeVelocity.magnitude);
    }
}
