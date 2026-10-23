using UnityEngine;
using UnityEngine.InputSystem;

public class PickUp : MonoBehaviour
{
    public Material highlightmaterial;
    private Material[] originalMaterials;
    private MeshRenderer[] meshRenderers;

    public GameObject weaponPreFab;
    public float lookRange = 3f;

    private bool isLookedAt = false;
    private Camera playercam;
    private PlayerShooting player;

  
    void Start()
    {
        meshRenderers = GetComponentsInChildren<MeshRenderer>();
        originalMaterials = new Material[meshRenderers.Length];
        for(int i = 0; i < meshRenderers.Length; i++)
        {
            originalMaterials[i] = meshRenderers[i].material;
        }

        player = FindObjectOfType<PlayerShooting>();
        playercam = player.GetComponentInChildren<Camera>();
    }

   
    void Update()
    {
        Ray ray = new Ray(playercam.transform.position, playercam.transform.forward);
        if(Physics.Raycast(ray, out RaycastHit hit, lookRange))
        {
            if(hit.collider.GetComponentInParent<PickUp>() == this)
            {
                if(!isLookedAt)
                {
                    SetLookedAt(true);
                    return;

                }
            }
        }

        if(isLookedAt)
        {
            SetLookedAt(false);
        }


        void SetLookedAt(bool lookedAt)
        {
            isLookedAt = lookedAt;
            if(lookedAt)
            {
              foreach(MeshRenderer mr in meshRenderers)
              {
                mr.material =  highlightmaterial ;
              }
            }
           
            else
            {
                for(int i = 0; i < meshRenderers.Length; i++)
                {
                    meshRenderers[i].material = originalMaterials[i];
                }
            }
        }
   }
   public void OnPickup()
    {
        if(!isLookedAt) return;

        if(player.gun != null)
        {
            Destroy(player.gun.gameObject);
        }

        GameObject newWeapon = Instantiate(weaponPreFab, player.gunHolder);
        newWeapon.transform.localPosition = Vector3.zero;
        newWeapon.transform.localRotation = Quaternion.identity;

        player.gun = newWeapon.GetComponent<Gun>();
        Destroy(gameObject);

    }
}
