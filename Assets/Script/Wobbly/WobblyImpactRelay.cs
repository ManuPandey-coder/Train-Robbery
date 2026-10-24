using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyImpactRelay : MonoBehaviour
{
    public WobblyActiveRagdoll owner;

    private void OnCollisionEnter(Collision collision)
    {
        if (owner != null) owner.NotifyImpact(collision.relativeVelocity.magnitude);
    }
}
