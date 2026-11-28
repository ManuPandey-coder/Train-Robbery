using UnityEngine;

public sealed class WeaponShot : MonoBehaviour
{
    private Vector3 velocity;
    private Transform owner;
    private float lifetime, impulse;
    private bool laser;
    private LineRenderer beam;
    private static Material bulletMaterial, laserMaterial;
    public static int BulletsFired { get; private set; }
    public static int LasersFired { get; private set; }

    private static Material Material(bool energy)
    {
        ref Material cached = ref (energy ? ref laserMaterial : ref bulletMaterial);
        if(cached!=null) return cached;
        Shader shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if(shader==null) shader=Shader.Find("Sprites/Default");
        cached=new Material(shader){name=energy ? "Laser effect" : "Bullet effect"};
        cached.color=Color.white;
        if(cached.HasProperty("_Surface")) cached.SetFloat("_Surface",1);
        if(cached.HasProperty("_SrcBlend")) cached.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if(cached.HasProperty("_DstBlend")) cached.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.One);
        cached.renderQueue=3000;
        return cached;
    }

    public static WeaponShot Fire(GunDefinition gun,Vector3 origin,Vector3 direction,Transform owner)
    {
        GameObject go=new GameObject(gun.Type==GunDefinition.FireType.Laser ? "Laser Shot" : "Bullet");
        go.transform.SetPositionAndRotation(origin,Quaternion.LookRotation(direction));
        var shot=go.AddComponent<WeaponShot>();
        shot.owner=owner; shot.impulse=gun.Impulse;
        shot.laser=gun.Type==GunDefinition.FireType.Laser;
        if(shot.laser)
        {
            LasersFired++;
            Vector3 end=origin+direction*gun.Range;
            if(shot.FindHit(origin,direction,gun.Range,out RaycastHit hit))
            {end=hit.point; shot.Hit(hit,direction);}
            shot.beam=go.AddComponent<LineRenderer>();
            shot.beam.sharedMaterial=Material(true);
            shot.beam.positionCount=2;
            shot.beam.SetPosition(0,origin); shot.beam.SetPosition(1,end);
            shot.beam.startColor=shot.beam.endColor=gun.LaserColor;
            shot.beam.startWidth=.025f; shot.beam.endWidth=.012f;
            shot.beam.numCapVertices=4;
            shot.lifetime=.16f;
        }
        else
        {
            BulletsFired++;
            shot.velocity=direction*gun.Speed;
            shot.lifetime=Mathf.Min(5f,gun.Range/gun.Speed);
            var trail=go.AddComponent<TrailRenderer>();
            trail.sharedMaterial=Material(false);
            trail.time=.18f; trail.minVertexDistance=.015f;
            trail.startWidth=.014f; trail.endWidth=0f;
            trail.startColor=new Color(1f,.75f,.2f,.95f);
            trail.endColor=new Color(1f,.3f,.02f,0f);
            GameObject visual=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name="Bullet Visual"; visual.transform.SetParent(go.transform,false);
            visual.transform.localRotation=Quaternion.Euler(90,0,0);
            visual.transform.localScale=new Vector3(.015f,.045f,.015f);
            Destroy(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().sharedMaterial=Material(false);
        }
        return shot;
    }

    private bool FindHit(Vector3 start,Vector3 direction,float distance,out RaycastHit nearest)
    {
        nearest=default; float closest=float.PositiveInfinity;
        foreach(RaycastHit hit in Physics.SphereCastAll(start,.007f,direction,distance,~0,QueryTriggerInteraction.Ignore))
        {
            Transform t=hit.collider.transform;
            if(t.IsChildOf(transform) || (owner!=null && (t==owner || t.IsChildOf(owner)))) continue;
            if(hit.distance>=closest) continue;
            closest=hit.distance; nearest=hit;
        }
        return closest<float.PositiveInfinity;
    }
    private void Hit(RaycastHit hit,Vector3 direction)
    {
        if(hit.rigidbody!=null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(direction*impulse,hit.point,ForceMode.Impulse);
    }
    private void Update()
    {
        float dt=Time.deltaTime;
        lifetime-=dt;
        if(lifetime<=0f){Destroy(gameObject);return;}
        if(laser)
        {
            beam.widthMultiplier=Mathf.Clamp01(lifetime/.16f);
            return;
        }
        Vector3 step=velocity*dt+Vector3.down*4.9f*dt*dt;
        if(step.sqrMagnitude>.000001f && FindHit(transform.position,step.normalized,step.magnitude,out RaycastHit hit))
        {
            Hit(hit,step.normalized); transform.position=hit.point;
            velocity=Vector3.zero; lifetime=.18f;
            foreach(Renderer r in GetComponentsInChildren<Renderer>()) if(!(r is TrailRenderer)) r.enabled=false;
            var trail=GetComponent<TrailRenderer>(); trail.emitting=false;
            enabled=false; Destroy(gameObject,.2f);return;
        }
        transform.position+=step; velocity+=Vector3.down*9.8f*dt;
    }
    public static void Flash(Vector3 origin,Color color)
    {
        GameObject flash=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name="Muzzle Flash";
        flash.transform.position=origin; flash.transform.localScale=Vector3.one*.07f;
        Destroy(flash.GetComponent<Collider>());
        var renderer=flash.GetComponent<Renderer>(); renderer.sharedMaterial=Material(true);
        var block=new MaterialPropertyBlock(); block.SetColor("_BaseColor",color); renderer.SetPropertyBlock(block);
        Destroy(flash,.045f);
    }
}
