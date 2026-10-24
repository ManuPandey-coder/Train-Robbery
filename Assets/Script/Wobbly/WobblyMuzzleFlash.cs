using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyMuzzleFlash : MonoBehaviour
{
    private const float Lifetime = 0.075f;
    private float age;
    private Light flashLight;
    private Material flashMaterial;
    private Color baseColor;

    public static WobblyMuzzleFlash Spawn(Vector3 position, Vector3 direction, float size)
    {
        GameObject root = new GameObject("Muzzle Flash");
        root.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(direction.normalized, Vector3.up));
        WobblyMuzzleFlash flash = root.AddComponent<WobblyMuzzleFlash>();
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
