using UnityEngine;

[DisallowMultipleComponent]
public sealed class GunDefinition : MonoBehaviour
{
    public enum FireType { Bullet, Laser }
    [SerializeField] private string displayName;
    [SerializeField] private FireType fireType;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Vector3 barrelAxis = Vector3.up;
    [SerializeField, Min(1)] private int magazineSize = 12;
    [SerializeField, Min(0)] private int startingReserve = 120;
    [SerializeField, Min(.05f)] private float shotInterval = .2f;
    [SerializeField, Min(.1f)] private float reloadSeconds = 1.5f;
    [SerializeField, Min(1f)] private float projectileSpeed = 90f;
    [SerializeField, Min(1f)] private float range = 180f;
    [SerializeField, Min(0f)] private float impulse = 6f;
    [SerializeField, Range(1,12)] private int pellets = 1;
    [SerializeField, Range(0f,12f)] private float spreadDegrees;
    [SerializeField, Range(0f,10f)] private float recoil = 1.5f;
    [SerializeField, ColorUsage(true,true)] private Color laserColor = new Color(0f,3f,5f);
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public FireType Type => fireType;
    public Transform Muzzle => muzzle != null ? muzzle : transform;
    public Vector3 BarrelAxis => barrelAxis.normalized;
    public int MagazineSize => magazineSize;
    public int Rounds { get; private set; }
    public int Reserve { get; private set; }
    public float ShotInterval => shotInterval;
    public float ReloadSeconds => reloadSeconds;
    public float Speed => projectileSpeed;
    public float Range => range;
    public float Impulse => impulse;
    public int Pellets => pellets;
    public float Spread => spreadDegrees;
    public float Recoil => recoil;
    public Color LaserColor => laserColor;
    public bool CanReload => Rounds < magazineSize && Reserve > 0;

    private void Awake() { Rounds = magazineSize; Reserve = startingReserve; }
    public bool Consume() { if(Rounds<=0) return false; Rounds--; return true; }
    public void Reload()
    {
        int amount = Mathf.Min(magazineSize-Rounds,Reserve);
        Rounds += amount; Reserve -= amount;
    }
    public void Configure(Transform muzzleTransform)
    {
        muzzle = muzzleTransform;
        string n = name.ToLowerInvariant();
        fireType = n.Contains("laser") || n.Contains("plasma") || n.Contains("pulse") ? FireType.Laser : FireType.Bullet;
        displayName = n.Contains("shortshotgun") ? "Shotgun" : n.Contains("revolver") ? "Revolver" :
            n.Contains("cowboy") ? "Cowboy" : n.Contains("plasma") ? "Plasma" :
            n.Contains("pulse") ? "Pulse" : n.Contains("laser") ? "Laser" : "Pistol";
        magazineSize = n.Contains("shotgun") ? 2 : n.Contains("revolver") || n.Contains("cowboy") ? 6 : fireType==FireType.Laser ? 24 : 12;
        pellets = n.Contains("shotgun") ? 6 : 1;
        spreadDegrees = pellets>1 ? 4f : .25f;
        shotInterval = pellets>1 ? .7f : n.Contains("pulse") ? .12f : .25f;
        recoil = pellets>1 ? 3f : fireType==FireType.Laser ? .65f : 1.5f;
        laserColor = n.Contains("plasma") ? new Color(3f,.2f,5f) : n.Contains("pulse") ? new Color(.1f,5f,1f) : new Color(0f,3f,5f);
    }
}
