using System;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(320)]
public sealed class WeaponController : MonoBehaviour
{
    [Header("Built-in robber guns")]
    [SerializeField] private GunDefinition[] guns = Array.Empty<GunDefinition>();
    [SerializeField] private int selectedWeapon;
    [SerializeField] private bool startEquipped = true;
    [Header("Aiming")]
    [SerializeField, Range(2f,30f)] private float aimSharpness = 12f;
    [SerializeField] private Vector3 handOffset = new Vector3(.2f,-.03f,.38f);
    private PlayerController player;
    private ArmPoseDriver.Arm rightArm,leftArm;
    private Camera view;
    private bool holstered, virtualAim, virtualFire, reloading;
    private float nextShot,reloadEnd,aimBlend;
    private Quaternion smoothedHand;
    private bool poseReady;
    public int WeaponCount { get { DiscoverGuns(); return guns.Length; } }
    public int SelectedWeapon => selectedWeapon;
    public GunDefinition HeldWeapon => !holstered && selectedWeapon>=0 && selectedWeapon<guns.Length ? guns[selectedWeapon] : null;
    public bool IsAiming => isActiveAndEnabled && HeldWeapon!=null &&
        (FirstPerson || virtualAim || (!Application.isMobilePlatform && Input.GetMouseButton(1)));
    private bool FirstPerson => view!=null && view.GetComponent<PlayerCamera>()!=null && view.GetComponent<PlayerCamera>().IsFirstPerson;
    public bool IsReloading => reloading;
    public Vector3 AimForward => view != null ? view.transform.forward : transform.forward;
    public float ReloadProgress => !reloading || HeldWeapon==null ? 0f : 1f-(reloadEnd-Time.time)/HeldWeapon.ReloadSeconds;

    private void Awake()
    {
        player=GetComponent<PlayerController>(); view=Camera.main;
        DiscoverGuns(); holstered=!startEquipped;
        HideAll();
    }
    private void Start() { CacheArms(); RefreshVisibility(); }
    public void DiscoverGuns()
    {
        if(guns==null || guns.Length==0 || guns.Any(g=>g==null))
            guns=GetComponentsInChildren<GunDefinition>(true).OrderBy(g=>g.name,StringComparer.Ordinal).ToArray();
    }
    public string GetWeaponName(int index) => index>=0 && index<WeaponCount ? guns[index].DisplayName : "";
    public void SelectWeapon(int index)
    {
        if(index<0 || index>=WeaponCount || MenuManager.BlocksGameplay) return;
        selectedWeapon=index; holstered=false; reloading=false; poseReady=false;
        RefreshVisibility();
    }
    public void ToggleHolster()
    {
        if(MenuManager.BlocksGameplay) return;
        holstered=!holstered; reloading=false; poseReady=false;
        RefreshVisibility();
        if(holstered && player!=null) player.ResetUpperBodyAfterGrip();
    }
    public void SetVirtualAim(bool held) => virtualAim=held;
    public void QueueVirtualFire() => virtualFire=true;
    public void ClearTouchInput() { virtualAim=false;virtualFire=false; }
    public void ReloadFromHud() => BeginReload();
    public bool BeginReload()
    {
        if(!CanUse() || reloading || !HeldWeapon.CanReload) return false;
        reloading=true; reloadEnd=Time.time+HeldWeapon.ReloadSeconds; return true;
    }
    public void PrepareForVehicleSeat()
    {
        reloading=false; ClearTouchInput(); HideAll(); poseReady=false;
        if(player!=null){player.ArmsReserved=false;player.SupportHandReserved=false;player.ResetUpperBodyAfterGrip();}
    }
    private bool CanUse() => !MenuManager.BlocksGameplay && player!=null && !player.IsVehicleSeated &&
        !player.IsExternallyControlled && !player.IsPoseRecoveryLocked && HeldWeapon!=null;
    private void Update()
    {
        if(MenuManager.BlocksGameplay || player==null || player.IsVehicleSeated ||
            player.IsExternallyControlled || player.IsPoseRecoveryLocked) {virtualFire=false;return;}
        for(int i=0;i<guns.Length;i++)
            if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i))) SelectWeapon(i);
        if(Input.GetKeyDown(KeyCode.H)) ToggleHolster();
        if(!CanUse()) return;
        bool overUi=UnityEngine.EventSystems.EventSystem.current!=null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        if(!Application.isMobilePlatform && Input.GetKeyDown(KeyCode.R)) BeginReload();
        if(reloading && Time.time>=reloadEnd){HeldWeapon.Reload();reloading=false;}
        bool fire=virtualFire || (!Application.isMobilePlatform && !overUi && Input.GetMouseButtonDown(0));
        virtualFire=false;
        if(fire) TryFireHeldWeapon();
    }
    public bool TryFireHeldWeapon()
    {
        if(!CanUse() || reloading || Time.time<nextShot || !HeldWeapon.Consume()) return false;
        GunDefinition gun=HeldWeapon;
        nextShot=Time.time+gun.ShotInterval;
        Vector3 origin=gun.Muzzle.position;
        Vector3 direction=gun.transform.TransformDirection(gun.BarrelAxis).normalized;
        // Bullets and lasers leave the visible barrel. The wrist solver aligns
        // that barrel with the camera aim; close scenery still blocks the shot.
        for(int i=0;i<gun.Pellets;i++)
        {
            Vector3 shot=Quaternion.AngleAxis(UnityEngine.Random.Range(-gun.Spread,gun.Spread),Vector3.up)*
                Quaternion.AngleAxis(UnityEngine.Random.Range(-gun.Spread,gun.Spread),Vector3.right)*direction;
            WeaponShot.Fire(gun,origin,shot,transform);
        }
        WeaponShot.Flash(origin,gun.Type==GunDefinition.FireType.Laser ? gun.LaserColor : new Color(4,2,.3f));
        if(view!=null) view.GetComponent<PlayerCamera>()?.AddRecoil(gun.Recoil,UnityEngine.Random.Range(-.25f,.25f));
        return true;
    }
    private void CacheArms()
    {
        if(player==null) player=GetComponent<PlayerController>();
        if(player==null || player.Pelvis==null) return;
        rightArm=new ArmPoseDriver.Arm(player.RightUpperArm,player.RightForearm,player.RightHand,player.Pelvis);
        leftArm=new ArmPoseDriver.Arm(player.LeftUpperArm,player.LeftForearm,player.LeftHand,player.Pelvis);
    }
    private void LateUpdate()
    {
        RefreshVisibility();
        if(!CanUse()){poseReady=false;return;}
        if(rightArm==null) CacheArms();
        if(rightArm==null || !rightArm.Valid) return;
        if(view==null) view=Camera.main;
        Vector3 forward=player.FacingDirection.normalized;
        Vector3 right=Vector3.Cross(Vector3.up,forward).normalized;
        bool aiming=IsAiming;
        float blend=1f-Mathf.Exp(-aimSharpness*Time.deltaTime);
        aimBlend=Mathf.Lerp(aimBlend,aiming ? 1f : 0f,blend);
        Vector3 aim=view!=null ? view.transform.forward : forward;
        float yaw=Vector3.SignedAngle(forward,Vector3.ProjectOnPlane(aim,Vector3.up),Vector3.up);
        float pitch=Mathf.Asin(Mathf.Clamp(aim.y,-1f,1f))*Mathf.Rad2Deg;
        Vector3 safeFlat=Quaternion.AngleAxis(Mathf.Clamp(yaw,-55f,55f),Vector3.up)*forward;
        Vector3 safeAim=(safeFlat*Mathf.Cos(pitch*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(pitch*Mathf.Deg2Rad)).normalized;
        Vector3 fireDirection=Vector3.Slerp(forward,safeAim,aimBlend);
        Quaternion neutral=player.Pelvis.rotation*rightArm.restHandFromPelvis;
        Vector3 gunAxisInHand=HeldWeapon.transform.localRotation*HeldWeapon.BarrelAxis;
        Quaternion desired=Quaternion.FromToRotation(neutral*gunAxisInHand,fireDirection)*neutral;
        if(!poseReady){smoothedHand=desired;poseReady=true;} else smoothedHand=Quaternion.Slerp(smoothedHand,desired,blend);
        Transform chest=player.Chest!=null ? player.Chest : player.Pelvis;
        Vector3 target=chest.position+right*handOffset.x+Vector3.up*handOffset.y+fireDirection*handOffset.z;
        if(FirstPerson)
            target=view.transform.position+view.transform.right*.18f-view.transform.up*.18f+fireDirection*.42f;
        ArmPoseDriver.Solve(rightArm,target,smoothedHand,right+Vector3.down*.35f);
        player.ArmsReserved=true;player.SupportHandReserved=false;
    }
    private void HideAll(){foreach(GunDefinition gun in guns) if(gun!=null) gun.gameObject.SetActive(false);}
    private void RefreshVisibility()
    {
        DiscoverGuns();
        bool show=enabled && CanUse();
        for(int i=0;i<guns.Length;i++) if(guns[i]!=null) guns[i].gameObject.SetActive(show && i==selectedWeapon);
        if(player!=null && !show && !player.IsVehicleSeated){player.ArmsReserved=false;player.SupportHandReserved=false;}
    }
    public void ApplySteeringWheelPose(Transform left,Transform right,Transform wheel,bool active)
    {
        if(player==null) player=GetComponent<PlayerController>();
        if(player==null) return;
        if(!active){player.ArmsReserved=false;player.SupportHandReserved=false;return;}
        if(rightArm==null) CacheArms();
        if(left==null || right==null) return;
        Vector3 side=Vector3.Cross(Vector3.up,player.FacingDirection).normalized;
        ArmPoseDriver.Solve(leftArm,left.position,left.rotation,-side+Vector3.down*.3f);
        ArmPoseDriver.Solve(rightArm,right.position,right.rotation,side+Vector3.down*.3f);
        player.ArmsReserved=player.SupportHandReserved=true;
    }
    private void OnDisable(){HideAll();reloading=false;ClearTouchInput();poseReady=false;}
}
