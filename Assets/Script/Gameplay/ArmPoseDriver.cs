using UnityEngine;

public static class ArmPoseDriver
{
    public sealed class Arm
    {
        public Transform upper, lower, hand;
        public float upperLength, lowerLength;
        public Vector3 upperAxis, lowerAxis;
        public Quaternion restHandFromPelvis;
        public bool Valid => upper != null && lower != null && hand != null;
        public Arm(Transform a,Transform b,Transform c,Transform pelvis)
        {
            upper=a; lower=b; hand=c;
            if(!Valid) return;
            upperLength=Vector3.Distance(a.position,b.position);
            lowerLength=Vector3.Distance(b.position,c.position);
            upperAxis=a.InverseTransformDirection((b.position-a.position).normalized);
            lowerAxis=b.InverseTransformDirection((c.position-b.position).normalized);
            restHandFromPelvis=Quaternion.Inverse(pelvis.rotation)*c.rotation;
        }
    }
    public static void Solve(Arm arm,Vector3 target,Quaternion handRotation,Vector3 pole)
    {
        if(arm==null || !arm.Valid) return;
        Vector3 origin=arm.upper.position;
        Vector3 delta=target-origin;
        if(delta.sqrMagnitude<.000001f) return;
        Vector3 dir=delta.normalized;
        float reach=Mathf.Clamp(delta.magnitude,Mathf.Abs(arm.upperLength-arm.lowerLength)+.001f,
            (arm.upperLength+arm.lowerLength)*.96f);
        Vector3 bend=Vector3.ProjectOnPlane(pole,dir).normalized;
        if(bend.sqrMagnitude<.001f) bend=Vector3.ProjectOnPlane(Vector3.down,dir).normalized;
        float along=(arm.upperLength*arm.upperLength-arm.lowerLength*arm.lowerLength+reach*reach)/(2f*reach);
        Vector3 elbow=origin+dir*along+bend*Mathf.Sqrt(Mathf.Max(0f,arm.upperLength*arm.upperLength-along*along));
        arm.upper.rotation=Quaternion.FromToRotation(arm.upper.TransformDirection(arm.upperAxis),elbow-origin)*arm.upper.rotation;
        Vector3 end=origin+dir*reach;
        arm.lower.rotation=Quaternion.FromToRotation(arm.lower.TransformDirection(arm.lowerAxis),end-arm.lower.position)*arm.lower.rotation;
        arm.hand.rotation=handRotation;
    }
}
