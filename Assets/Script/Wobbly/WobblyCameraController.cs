using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class WobblyCameraController : MonoBehaviour
{
    [SerializeField] private WobblyActiveRagdoll target;
    [SerializeField] private float mouseSensitivity = 2.2f;
    [SerializeField] private float thirdPersonDistance = 5.2f;
    [SerializeField] private float thirdPersonHeight = 1.15f;
    [SerializeField] private float followSharpness = 14f;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float firstPersonForwardOffset = 0.18f;
    [SerializeField] private bool startInThirdPerson = true;
    [SerializeField, Range(2f, 30f)] private float recoilRecoverySharpness = 11f;

    private Camera viewCamera;
    private bool thirdPerson;
    private float yaw;
    private float pitch = 12f;
    private float firstPersonEyeHeight = -1f;
    private Vector3 smoothedFirstPersonAnchor;
    private bool firstPersonAnchorReady;
    private bool followPositionReady;
    private Vector3 previousTargetPosition;
    private WobblyActiveRagdoll previousTarget;
    private readonly List<RendererState> characterRenderers = new List<RendererState>();
    private SkinnedMeshRenderer firstPersonHands;
    private Mesh firstPersonHandsMesh;
    private float recoilPitchOffset;
    private float recoilYawOffset;

    public bool IsFirstPerson => !thirdPerson;

    public void AddRecoil(float pitchKick, float yawKick)
    {
        recoilPitchOffset = Mathf.Clamp(recoilPitchOffset - Mathf.Max(0f, pitchKick), -22f, 5f);
        recoilYawOffset = Mathf.Clamp(recoilYawOffset + yawKick, -10f, 10f);
    }

    private sealed class RendererState
    {
        public Renderer renderer;
        public bool forceRenderingOff;
    }

    public WobblyActiveRagdoll Target
    {
        get => target;
        set => target = value;
    }

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
        thirdPerson = startInThirdPerson;
        yaw = transform.eulerAngles.y;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (target == null) target = FindFirstObjectByType<WobblyActiveRagdoll>();
        BuildFirstPersonHands();
        ApplyViewVisibility();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            thirdPerson = !thirdPerson;
            firstPersonAnchorReady = false;
            ApplyViewVisibility();
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -70f, 78f);
        }
        float recoilBlend = 1f - Mathf.Exp(-recoilRecoverySharpness * Time.deltaTime);
        recoilPitchOffset = Mathf.Lerp(recoilPitchOffset, 0f, recoilBlend);
        recoilYawOffset = Mathf.Lerp(recoilYawOffset, 0f, recoilBlend);
    }

    private void LateUpdate()
    {
        if (target == null || target.Pelvis == null) return;
        Vector3 targetPosition = target.Pelvis.position;
        if (followPositionReady && previousTarget == target)
        {
            // Follow translation on the same rendered frame as the body.
            // Damping world position made the camera chase each gait pulse;
            // damp only the orbit/view offset instead.
            Vector3 displacement = targetPosition - previousTargetPosition;
            transform.position += displacement;
            if (firstPersonAnchorReady) smoothedFirstPersonAnchor += displacement;
        }
        previousTargetPosition = targetPosition;
        previousTarget = target;
        followPositionReady = true;
        Quaternion lookRotation = Quaternion.Euler(
            Mathf.Clamp(pitch + recoilPitchOffset, -75f, 82f), yaw + recoilYawOffset, 0f);
        Vector3 focus = target.Pelvis.position + Vector3.up * thirdPersonHeight;
        Vector3 desired;

        if (thirdPerson)
        {
            Vector3 direction = -(lookRotation * Vector3.forward);
            float distance = thirdPersonDistance;
            RaycastHit[] hits = Physics.SphereCastAll(focus, collisionRadius, direction, thirdPersonDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (BelongsToTarget(hits[i].collider)) continue;
                distance = Mathf.Min(distance, Mathf.Max(0.3f, hits[i].distance - collisionRadius));
            }
            desired = focus + direction * distance;
            viewCamera.nearClipPlane = 0.08f;
        }
        else
        {
            Transform head = target.Head != null ? target.Head : target.Pelvis;
            if (firstPersonEyeHeight < 0f)
            {
                float measuredHeight = head.position.y - target.Pelvis.position.y;
                firstPersonEyeHeight = Mathf.Clamp(measuredHeight, 0.48f, Mathf.Max(0.55f, thirdPersonHeight));
            }

            // Use the interpolated pelvis as the first-person anchor instead of
            // the procedural head bone. This removes head/neck wobble from the
            // camera while the visible hands can remain physically animated.
            Vector3 rawAnchor = target.Pelvis.position + Vector3.up * firstPersonEyeHeight;
            if (!firstPersonAnchorReady)
            {
                smoothedFirstPersonAnchor = rawAnchor;
                firstPersonAnchorReady = true;
            }
            float anchorBlend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            smoothedFirstPersonAnchor = Vector3.Lerp(smoothedFirstPersonAnchor, rawAnchor, anchorBlend);
            desired = smoothedFirstPersonAnchor + lookRotation * Vector3.forward * firstPersonForwardOffset;
            viewCamera.nearClipPlane = 0.025f;
        }

        float blend = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, blend);
    }

    private void BuildFirstPersonHands()
    {
        if (target == null) return;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        SkinnedMeshRenderer bodyRenderer = null;
        int largestVertexCount = -1;
        bool namedBodyFound = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            characterRenderers.Add(new RendererState
            {
                renderer = renderer,
                forceRenderingOff = renderer.forceRenderingOff
            });

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned == null || skinned.sharedMesh == null) continue;
            int vertexCount = skinned.sharedMesh.vertexCount;
            bool isNamedBody = skinned.name.Equals("BODY", System.StringComparison.OrdinalIgnoreCase);
            if (isNamedBody || (!namedBodyFound && vertexCount > largestVertexCount))
            {
                bodyRenderer = skinned;
                largestVertexCount = vertexCount;
                namedBodyFound |= isNamedBody;
            }
        }

        if (bodyRenderer == null || bodyRenderer.sharedMesh == null) return;
        Mesh sourceMesh = bodyRenderer.sharedMesh;
        if (!sourceMesh.isReadable)
        {
            Debug.LogWarning("First-person hands need Read/Write enabled on the character mesh.", bodyRenderer);
            return;
        }

        HashSet<int> armBoneIndices = new HashSet<int>();
        Transform[] bones = bodyRenderer.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            string boneName = bones[i].name.ToLowerInvariant();
            if (boneName.StartsWith("arm.") || boneName.StartsWith("forearm.") || boneName.StartsWith("hand.") ||
                (target.LeftHand != null && bones[i].IsChildOf(target.LeftHand)) ||
                (target.RightHand != null && bones[i].IsChildOf(target.RightHand)))
                armBoneIndices.Add(i);
        }
        if (armBoneIndices.Count == 0) return;

        BoneWeight[] weights = sourceMesh.boneWeights;
        firstPersonHandsMesh = Instantiate(sourceMesh);
        firstPersonHandsMesh.name = sourceMesh.name + "_FirstPersonHands";
        int keptTriangleCount = 0;
        for (int subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            int[] sourceTriangles = sourceMesh.GetTriangles(subMesh);
            List<int> handTriangles = new List<int>(sourceTriangles.Length / 4);
            for (int t = 0; t + 2 < sourceTriangles.Length; t += 3)
            {
                int a = sourceTriangles[t];
                int b = sourceTriangles[t + 1];
                int c = sourceTriangles[t + 2];
                if (ArmWeight(weights[a], armBoneIndices) < 0.42f ||
                    ArmWeight(weights[b], armBoneIndices) < 0.42f ||
                    ArmWeight(weights[c], armBoneIndices) < 0.42f)
                    continue;
                handTriangles.Add(a);
                handTriangles.Add(b);
                handTriangles.Add(c);
            }
            keptTriangleCount += handTriangles.Count / 3;
            firstPersonHandsMesh.SetTriangles(handTriangles, subMesh, false);
        }

        if (keptTriangleCount == 0)
        {
            Destroy(firstPersonHandsMesh);
            firstPersonHandsMesh = null;
            Debug.LogWarning("No arm-weighted triangles were found for the first-person hands.", bodyRenderer);
            return;
        }

        GameObject handsObject = new GameObject("FirstPersonHandsOnly");
        handsObject.layer = bodyRenderer.gameObject.layer;
        handsObject.transform.SetParent(bodyRenderer.transform, false);
        firstPersonHands = handsObject.AddComponent<SkinnedMeshRenderer>();
        firstPersonHands.sharedMesh = firstPersonHandsMesh;
        firstPersonHands.sharedMaterials = bodyRenderer.sharedMaterials;
        firstPersonHands.bones = bones;
        firstPersonHands.rootBone = bodyRenderer.rootBone;
        firstPersonHands.localBounds = bodyRenderer.localBounds;
        firstPersonHands.quality = bodyRenderer.quality;
        firstPersonHands.updateWhenOffscreen = true;
        firstPersonHands.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        firstPersonHands.receiveShadows = false;
    }

    private static float ArmWeight(BoneWeight weight, HashSet<int> armBones)
    {
        float total = 0f;
        if (armBones.Contains(weight.boneIndex0)) total += weight.weight0;
        if (armBones.Contains(weight.boneIndex1)) total += weight.weight1;
        if (armBones.Contains(weight.boneIndex2)) total += weight.weight2;
        if (armBones.Contains(weight.boneIndex3)) total += weight.weight3;
        return total;
    }

    private void ApplyViewVisibility()
    {
        bool handsReady = firstPersonHands != null;
        bool hideFullCharacter = !thirdPerson && handsReady;
        for (int i = 0; i < characterRenderers.Count; i++)
        {
            RendererState state = characterRenderers[i];
            if (state.renderer != null)
                state.renderer.forceRenderingOff = state.forceRenderingOff || hideFullCharacter;
        }
        if (firstPersonHands != null)
            firstPersonHands.forceRenderingOff = thirdPerson;
    }

    private void OnDisable()
    {
        followPositionReady = false;
        firstPersonAnchorReady = false;
        for (int i = 0; i < characterRenderers.Count; i++)
        {
            RendererState state = characterRenderers[i];
            if (state.renderer != null)
                state.renderer.forceRenderingOff = state.forceRenderingOff;
        }
        if (firstPersonHands != null) firstPersonHands.forceRenderingOff = true;
    }

    private void OnDestroy()
    {
        if (firstPersonHandsMesh != null) Destroy(firstPersonHandsMesh);
    }

    private bool BelongsToTarget(Collider candidate)
    {
        if (candidate == null || target == null) return false;
        if (candidate.transform.IsChildOf(target.transform)) return true;

        // The physical ragdoll is deliberately a separate scene-root object,
        // so Transform.IsChildOf cannot identify it as part of the player.
        WobblyImpactRelay relay = candidate.GetComponentInParent<WobblyImpactRelay>();
        return relay != null && relay.owner == target;
    }
}
