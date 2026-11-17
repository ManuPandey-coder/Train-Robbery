
using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Weapon Shop/Weapon Data")]
public class WeaponData : ScriptableObject
{
    public string weaponName;
    public int price = 100;
    public Sprite weaponIcon;
    public GameObject weaponPrefab;
}
