using UnityEngine;

[DisallowMultipleComponent]
public sealed class Projectile : MonoBehaviour
{
    private Vector3 velocity;
    private float gravity;
    private float impactImpulse;
    private float collisionRadius;
    private float remainingLife;
    private Transform ownerRoot;
    private Transform weaponRoot;
    private TrailRenderer trail;
    private Renderer[] visualRenderers;
    private bool impacted;

    public bool HasImpacted => impacted;
    public Vector3 Velocity => velocity;

    public static Projectile Spawn(
        GameObject visualPrefab,
        Vector3 position,
        Vector3 direction,
        float speed,
        float gravityStrength,
        float impulse,
        float visualLength,
        Transform owner,
        Transform weapon)
    {
        GameObject projectile = new GameObject("Wobbly Bullet");
        projectile.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(direction.normalized, Vector3.up));
        Projectile bullet = projectile.AddComponent<Projectile>();
        bullet.Initialize(visualPrefab, direction.normalized * speed, gravityStrength,
            impulse, visualLength, owner, weapon);
        return bullet;
    }

    public void Initialize(
        GameObject visualPrefab,
        Vector3 initialVelocity,
        float gravityStrength,
        float impulse,
        float visualLength,
        Transform owner,
        Transform weapon)
    {
        velocity = initialVelocity;
        gravity = Mathf.Max(0f, gravityStrength);
        impactImpulse = Mathf.Max(0f, impulse);
        collisionRadius = Mathf.Clamp(visualLength * 0.11f, 0.006f, 0.025f);
        remainingLife = 6f;
        ownerRoot = owner;
        weaponRoot = weapon;
        CreateTrail(visualLength);
        CreateVisual(visualPrefab, visualLength);
    }

    private void Update()
    {
        SimulateStep(Time.deltaTime);
    }

    public void SimulateStep(float deltaTime)
    {
        if (impacted || deltaTime <= 0f) return;
        remainingLife -= deltaTime;
        if (remainingLife <= 0f)
        {
            FinishWithoutImpact();
            return;
        }

        Vector3 start = transform.position;
        Vector3 nextVelocity = velocity + Vector3.down * gravity * deltaTime;
        Vector3 displacement = (velocity + nextVelocity) * (0.5f * deltaTime);
        float distance = displacement.magnitude;
        if (distance > 0.00001f)
        {
            Vector3 direction = displacement / distance;
            RaycastHit[] hits = Physics.SphereCastAll(start, collisionRadius, direction, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            int closestIndex = -1;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider candidate = hits[i].collider;
                if (candidate == null || IsIgnored(candidate.transform)) continue;
                if (hits[i].distance >= closestDistance) continue;
                closestDistance = hits[i].distance;
                closestIndex = i;
            }

            if (closestIndex >= 0)
            {
                Impact(hits[closestIndex], direction);
                return;
            }
            transform.position = start + displacement;
        }
        velocity = nextVelocity;
        if (velocity.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
    }

    private bool IsIgnored(Transform candidate)
    {
        return candidate == transform || candidate.IsChildOf(transform) ||
               (ownerRoot != null && (candidate == ownerRoot || candidate.IsChildOf(ownerRoot))) ||
               (weaponRoot != null && (candidate == weaponRoot || candidate.IsChildOf(weaponRoot)));
    }

    private void Impact(RaycastHit hit, Vector3 direction)
    {
        impacted = true;
        transform.position = hit.point + hit.normal * 0.002f;
        Rigidbody body = hit.rigidbody;
        if (body != null && !body.isKinematic)
            body.AddForceAtPosition(direction * impactImpulse, hit.point, ForceMode.Impulse);

        ImpactRelay relay = hit.collider.GetComponentInParent<ImpactRelay>();
        if (relay != null && relay.owner != null)
            relay.owner.NotifyImpact(Mathf.Max(4f, velocity.magnitude * 0.1f));

        HideVisualAndFade();
    }

    private void FinishWithoutImpact()
    {
        impacted = true;
        HideVisualAndFade();
    }

    private void HideVisualAndFade()
    {
        if (visualRenderers != null)
            for (int i = 0; i < visualRenderers.Length; i++)
                if (visualRenderers[i] != null) visualRenderers[i].enabled = false;
        if (trail != null) trail.emitting = false;
        float delay = trail != null ? trail.time + 0.06f : 0.02f;
        if (Application.isPlaying) Destroy(gameObject, delay);
    }

    private void CreateTrail(float visualLength)
    {
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.2f;
        trail.minVertexDistance = 0.015f;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.startWidth = Mathf.Clamp(visualLength * 0.11f, 0.008f, 0.018f);
        trail.endWidth = 0f;
        trail.startColor = new Color(1f, 0.82f, 0.28f, 0.9f);
        trail.endColor = new Color(1f, 0.25f, 0.04f, 0f);
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            Material material = new Material(shader) { name = "Bullet Trail (runtime)" };
            material.color = Color.white;
            trail.material = material;
        }
    }

    private void CreateVisual(GameObject visualPrefab, float targetLength)
    {
        GameObject visual;
        if (visualPrefab != null)
        {
            visual = Instantiate(visualPrefab, transform);
            visual.name = "Bullet Model";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Fallback Bullet Model";
            visual.transform.SetParent(transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Collider primitiveCollider = visual.GetComponent<Collider>();
            if (primitiveCollider != null) Destroy(primitiveCollider);
            Renderer fallbackRenderer = visual.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (fallbackRenderer != null && shader != null)
            {
                Material material = new Material(shader) { name = "Fallback Bullet (runtime)" };
                material.color = new Color(0.95f, 0.68f, 0.18f);
                fallbackRenderer.material = material;
            }
        }

        DisableVisualPhysics(visual);
        NormalizeVisual(visual.transform, Mathf.Max(0.025f, targetLength));
        visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
    }

    private static void DisableVisualPhysics(GameObject visual)
    {
        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = false;
        Rigidbody[] bodies = visual.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null) continue;
            bodies[i].isKinematic = true;
            bodies[i].useGravity = false;
            bodies[i].detectCollisions = false;
        }
        Animator[] animators = visual.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            if (animators[i] != null) animators[i].enabled = false;
        Animation[] animations = visual.GetComponentsInChildren<Animation>(true);
        for (int i = 0; i < animations.Length; i++)
            if (animations[i] != null) animations[i].enabled = false;
        MonoBehaviour[] behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            if (behaviours[i] != null) behaviours[i].enabled = false;
    }

    private static void NormalizeVisual(Transform visual, float targetLength)
    {
        Bounds bounds = CalculateLocalBounds(visual);
        Vector3 size = bounds.size;
        int longestAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 axis = longestAxis == 0 ? Vector3.right : longestAxis == 1 ? Vector3.up : Vector3.forward;
        visual.localRotation = Quaternion.FromToRotation(axis, Vector3.forward) * visual.localRotation;
        bounds = CalculateLocalBounds(visual);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest > 0.0001f) visual.localScale *= targetLength / longest;
        bounds = CalculateLocalBounds(visual);
        visual.localPosition -= bounds.center;
    }

    private static Bounds CalculateLocalBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds result = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshFilter filter = renderers[i].GetComponent<MeshFilter>();
            Bounds source = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds : renderers[i].localBounds;
            Matrix4x4 matrix = root.worldToLocalMatrix * renderers[i].transform.localToWorldMatrix;
            Vector3 min = source.min;
            Vector3 max = source.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
        }
        return initialized ? result : new Bounds(Vector3.zero, Vector3.one);
    }
}


[DisallowMultipleComponent]
public sealed class MuzzleFlash : MonoBehaviour
{
    private const float Lifetime = 0.075f;
    private float age;
    private Light flashLight;
    private Material flashMaterial;
    private Color baseColor;

    public static MuzzleFlash Spawn(Vector3 position, Vector3 direction, float size)
    {
        GameObject root = new GameObject("Muzzle Flash");
        root.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(direction.normalized, Vector3.up));
        MuzzleFlash flash = root.AddComponent<MuzzleFlash>();
        flash.Build(Mathf.Max(0.05f, size));
        return flash;
    }

    private void Build(float size)
    {
        baseColor = new Color(1f, 0.56f, 0.08f, 1f);
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            flashMaterial = new Material(shader) { name = "Muzzle flash (runtime)" };
            flashMaterial.color = baseColor;
        }

        GameObject star = new GameObject("Flash Star", typeof(MeshFilter), typeof(MeshRenderer));
        star.transform.SetParent(transform, false);
        star.transform.localPosition = Vector3.zero;
        star.transform.localRotation = Quaternion.identity;
        star.transform.localScale = Vector3.one * size;
        star.GetComponent<MeshFilter>().sharedMesh = CreateStarMesh();
        star.GetComponent<MeshRenderer>().sharedMaterial = flashMaterial;

        GameObject flame = new GameObject("Flash Flame", typeof(MeshFilter), typeof(MeshRenderer));
        flame.transform.SetParent(transform, false);
        flame.transform.localPosition = new Vector3(0f, 0f, size * 0.45f);
        flame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        flame.transform.localScale = new Vector3(size * 0.42f, size * 1.25f, size * 0.42f);
        flame.GetComponent<MeshFilter>().sharedMesh = CreateDiamondMesh();
        flame.GetComponent<MeshRenderer>().sharedMaterial = flashMaterial;

        flashLight = gameObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.48f, 0.12f);
        flashLight.range = Mathf.Lerp(1.8f, 3.4f, Mathf.InverseLerp(0.1f, 0.24f, size));
        flashLight.intensity = Mathf.Lerp(2.5f, 5.5f, Mathf.InverseLerp(0.1f, 0.24f, size));
        flashLight.shadows = LightShadows.None;
    }

    private void Update()
    {
        age += Time.deltaTime;
        float fade = 1f - Mathf.Clamp01(age / Lifetime);
        if (flashLight != null) flashLight.intensity *= fade;
        if (flashMaterial != null)
        {
            Color color = baseColor;
            color.a = fade;
            flashMaterial.color = color;
        }
        transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.25f, fade);
        if (age >= Lifetime && Application.isPlaying) Destroy(gameObject);
    }

    private static Mesh CreateStarMesh()
    {
        const int points = 12;
        Vector3[] vertices = new Vector3[points + 1];
        Vector2[] uv = new Vector2[points + 1];
        int[] triangles = new int[points * 3];
        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < points; i++)
        {
            float angle = i * Mathf.PI * 2f / points;
            float radius = i % 2 == 0 ? 1f : 0.34f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            uv[i + 1] = new Vector2(vertices[i + 1].x * 0.5f + 0.5f, vertices[i + 1].y * 0.5f + 0.5f);
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % points + 1;
        }
        Mesh mesh = new Mesh { name = "Procedural muzzle star" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateDiamondMesh()
    {
        Mesh mesh = new Mesh { name = "Procedural muzzle flame" };
        mesh.vertices = new[]
        {
            new Vector3(0f, 1f, 0f), new Vector3(0.32f, 0f, 0f),
            new Vector3(0f, -1f, 0f), new Vector3(-0.32f, 0f, 0f)
        };
        mesh.uv = new[] { new Vector2(.5f, 1f), new Vector2(1f, .5f), new Vector2(.5f, 0f), new Vector2(0f, .5f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private void OnDestroy()
    {
        if (flashMaterial != null) Destroy(flashMaterial);
    }
}
