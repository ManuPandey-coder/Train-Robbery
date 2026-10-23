using UnityEngine;

public class WeaponFlash : MonoBehaviour
{
    public float time;
    void Start()
    {
        Destroy(gameObject, time);
    }

   
}
