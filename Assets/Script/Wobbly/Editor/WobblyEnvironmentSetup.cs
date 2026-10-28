#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class WobblyEnvironmentSetup
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string EnvironmentFolder = "Assets/Environment";
    private const string TerrainFolder = EnvironmentFolder + "/Terrain";
    private const string TextureFolder = TerrainFolder + "/Textures";
    private const string NormalFolder = TerrainFolder + "/Normals";
    private const string LayerFolder = TerrainFolder + "/Layers";
    private const string SandTexturePath = TextureFolder + "/Desert Sand.asset";
    private const string EarthTexturePath = TextureFolder + "/Dry Earth.asset";
    private const string RockTexturePath = TextureFolder + "/Weathered Rock.asset";
    private const string SandNormalPath = NormalFolder + "/Desert Sand Normal.asset";
    private const string EarthNormalPath = NormalFolder + "/Dry Earth Normal.asset";
    private const string RockNormalPath = NormalFolder + "/Weathered Rock Normal.asset";
    private const string SandLayerPath = LayerFolder + "/Desert Sand.terrainlayer";
    private const string EarthLayerPath = LayerFolder + "/Dry Earth.terrainlayer";
    private const string RockLayerPath = LayerFolder + "/Weathered Rock.terrainlayer";
    private const string TerrainMaterialPath = TerrainFolder + "/Western Desert Terrain.mat";
    private const string SkyboxPath =
        "Assets/Third Party/EmaceArt - raft on the desert/Skybox/Cartoon Airbrush Day NoSun/Cartoon_Airbrush_Day_NoSun.mat";
    private static readonly string[] ImportedEnvironmentFolders =
    {
        "Assets/Third Party/LowPoly Environment Pack",
        "Assets/Third Party/EmaceArt - raft on the desert"
    };

    static WobblyEnvironmentSetup()
    {
        EditorApplication.delayCall += TryAutoApply;
    }

    [MenuItem("Tools/Wobbly/Apply Western Desert Environment")]
    public static void ApplyFromMenu()
    {
        ApplyToActiveScene(true);
    }

    public static void ApplyFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyToActiveScene(false);
    }

    [MenuItem("Tools/Wobbly/Convert Imported Environment Materials to URP")]
    public static void ConvertMaterialsFromMenu()
    {
        int converted = ConvertImportedMaterialsToUrp();
        AssetDatabase.SaveAssets();
        Debug.Log("URP_ENVIRONMENT_MATERIALS_CONVERTED: " + converted);
    }

    private static void TryAutoApply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) return;
        if (IsConfigured()) return;
        ApplyToActiveScene(false);
    }

    private static bool IsConfigured()
    {
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        Material terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (terrains.Length == 0 || terrainMaterial == null) return false;

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            TerrainLayer[] layers = terrain != null && terrain.terrainData != null
                ? terrain.terrainData.terrainLayers
                : null;
            if (terrain == null || terrain.materialTemplate != terrainMaterial ||
                layers == null || layers.Length != 1 ||
                AssetDatabase.GetAssetPath(layers[0]) != SandLayerPath ||
                layers[0].diffuseTexture == null || layers[0].diffuseTexture.width < 256 ||
                layers[0].diffuseTexture.GetPixel(
                    layers[0].diffuseTexture.width / 2,
                    layers[0].diffuseTexture.height / 2).grayscale < 0.64f ||
                layers[0].diffuseTexture.GetPixel(0, 0).a > 0.001f ||
                layers[0].normalMapTexture == null)
                return false;
        }

        return !HasLegacyEnvironmentMaterials() && sky != null && RenderSettings.skybox == sky;
    }

    private static void ApplyToActiveScene(bool showDialog)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Environment setup", "Open Assets/Scenes/game.unity first.", "OK");
            return;
        }

        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (terrains.Length == 0)
        {
            Debug.LogError("Environment setup could not find any Terrain objects in game.unity.");
            return;
        }

        int convertedMaterials = ConvertImportedMaterialsToUrp();
        EnsureFolders();
        Texture2D sandTexture = EnsureTexture(SandTexturePath, "Desert Sand",
            new Color(0.72f, 0.65f, 0.52f), new Color(0.92f, 0.86f, 0.73f), 11.7f, 0.055f);
        Texture2D earthTexture = EnsureTexture(EarthTexturePath, "Dry Earth",
            new Color(0.38f, 0.20f, 0.10f), new Color(0.59f, 0.35f, 0.17f), 37.1f, 0.075f);
        Texture2D rockTexture = EnsureTexture(RockTexturePath, "Weathered Rock",
            new Color(0.25f, 0.23f, 0.21f), new Color(0.48f, 0.42f, 0.34f), 73.9f, 0.09f);
        Texture2D sandNormal = EnsureNormalTexture(SandNormalPath, "Desert Sand Normal", sandTexture, 3.2f);
        Texture2D earthNormal = EnsureNormalTexture(EarthNormalPath, "Dry Earth Normal", earthTexture, 4.4f);
        Texture2D rockNormal = EnsureNormalTexture(RockNormalPath, "Weathered Rock Normal", rockTexture, 6.5f);

        TerrainLayer sand = EnsureLayer(SandLayerPath, "Desert Sand", sandTexture, sandNormal,
            new Vector2(10f, 10f), 0f, 0.75f);
        TerrainLayer earth = EnsureLayer(EarthLayerPath, "Dry Earth", earthTexture, earthNormal,
            new Vector2(8f, 8f), 0f, 0.95f);
        TerrainLayer rock = EnsureLayer(RockLayerPath, "Weathered Rock", rockTexture, rockNormal,
            new Vector2(6f, 6f), 0f, 1.2f);
        Material terrainMaterial = EnsureTerrainMaterial();
        if (terrainMaterial == null) return;

        float terrainSpan = 0f;
        HashSet<TerrainData> configuredData = new HashSet<TerrainData>();
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            TerrainData data = terrain != null ? terrain.terrainData : null;
            if (terrain == null || data == null) continue;

            Undo.RecordObject(terrain, "Apply western desert terrain material");
            terrain.materialTemplate = terrainMaterial;
            terrain.drawInstanced = true;
            terrain.basemapDistance = Mathf.Max(500f, Mathf.Max(data.size.x, data.size.z) * 0.72f);
            terrainSpan = Mathf.Max(terrainSpan, Mathf.Max(data.size.x, data.size.z));
            EditorUtility.SetDirty(terrain);

            if (!configuredData.Add(data)) continue;
            Undo.RecordObject(data, "Apply western desert terrain layers");
            data.terrainLayers = new[] { sand };
            PaintTerrain(data);
            EditorUtility.SetDirty(data);
        }

        Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skybox == null)
        {
            Debug.LogError("Environment setup could not load the imported desert skybox: " + SkyboxPath);
            return;
        }

        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 0.92f;
        RenderSettings.reflectionIntensity = 0.82f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.67f, 0.57f, 1f);
        RenderSettings.fogStartDistance = Mathf.Max(90f, terrainSpan * 0.22f);
        RenderSettings.fogEndDistance = Mathf.Max(360f, terrainSpan * 0.78f);

        Light sun = FindSun();
        if (sun != null)
        {
            Undo.RecordObject(sun, "Tune western desert sunlight");
            sun.color = new Color(1f, 0.91f, 0.72f, 1f);
            sun.intensity = 1.12f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            EditorUtility.SetDirty(sun);
        }

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("WESTERN_DESERT_ENVIRONMENT_APPLIED: pale desert sand terrain, cartoon desert skybox, warm sun, " +
                  "distance fog, and " + convertedMaterials + " imported materials converted to URP.");
    }

    private static bool HasLegacyEnvironmentMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", ImportedEnvironmentFolders);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.Contains("/Skybox/")) continue;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null) continue;
            string shaderName = material.shader.name;
            if (!shaderName.StartsWith("Universal Render Pipeline/") &&
                !shaderName.StartsWith("Shader Graphs/")) return true;
        }
        return false;
    }

    private static int ConvertImportedMaterialsToUrp()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (lit == null || unlit == null)
        {
            Debug.LogError("URP material conversion failed because URP shaders are unavailable.");
            return 0;
        }

        int converted = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material", ImportedEnvironmentFolders);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.Contains("/Skybox/")) continue;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null) continue;
            string oldShaderName = material.shader.name;
            if (oldShaderName.StartsWith("Universal Render Pipeline/") ||
                oldShaderName.StartsWith("Shader Graphs/")) continue;

            Texture baseMap = FirstTexture(material, "_BaseMap", "_MainTex");
            Texture normalMap = FirstTexture(material, "_BumpMap", "_NormalMap");
            Texture emissionMap = FirstTexture(material, "_EmissionMap");
            Color baseColor = FirstColor(material, Color.white, "_BaseColor", "_Color");
            Color emissionColor = FirstColor(material, Color.black, "_EmissionColor");
            float metallic = FirstFloat(material, 0f, "_Metallic");
            float smoothness = FirstFloat(material, 0.18f, "_Smoothness", "_Glossiness");
            float cutoff = FirstFloat(material, 0.5f, "_Cutoff", "_AlphaClipThreshold");
            bool alphaClip = material.IsKeywordEnabled("_ALPHATEST_ON") || oldShaderName.Contains("Cutout");
            bool transparent = !alphaClip && (material.renderQueue >= 3000 || baseColor.a < 0.995f ||
                               oldShaderName.Contains("Transparent") || oldShaderName.Contains("Fade"));
            bool useUnlit = oldShaderName.Contains("Unlit");

            Undo.RecordObject(material, "Convert imported material to URP");
            material.shader = useUnlit ? unlit : lit;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baseMap);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (!useUnlit)
            {
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                if (normalMap != null && material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normalMap);
                    material.EnableKeyword("_NORMALMAP");
                }
                if ((emissionMap != null || emissionColor.maxColorComponent > 0.001f) &&
                    material.HasProperty("_EmissionColor"))
                {
                    material.SetTexture("_EmissionMap", emissionMap);
                    material.SetColor("_EmissionColor", emissionColor);
                    material.EnableKeyword("_EMISSION");
                }
            }
            ConfigureSurface(material, transparent, alphaClip, cutoff);
            EditorUtility.SetDirty(material);
            converted++;
        }
        return converted;
    }

    private static void ConfigureSurface(Material material, bool transparent, bool alphaClip, float cutoff)
    {
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", cutoff);
        if (alphaClip)
        {
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            material.renderQueue = (int)RenderQueue.AlphaTest;
            return;
        }

        material.DisableKeyword("_ALPHATEST_ON");
        if (transparent)
        {
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            material.renderQueue = -1;
        }
    }

    private static Texture FirstTexture(Material material, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
            if (material.HasProperty(names[i]))
            {
                Texture value = material.GetTexture(names[i]);
                if (value != null) return value;
            }
        return null;
    }

    private static Color FirstColor(Material material, Color fallback, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
            if (material.HasProperty(names[i])) return material.GetColor(names[i]);
        return fallback;
    }

    private static float FirstFloat(Material material, float fallback, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
            if (material.HasProperty(names[i])) return material.GetFloat(names[i]);
        return fallback;
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "Environment");
        EnsureFolder(EnvironmentFolder, "Terrain");
        EnsureFolder(TerrainFolder, "Textures");
        EnsureFolder(TerrainFolder, "Normals");
        EnsureFolder(TerrainFolder, "Layers");
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }

    private static Texture2D EnsureTexture(
        string path, string name, Color dark, Color light, float seed, float frequency)
    {
        const int size = 256;
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        bool created = texture == null;
        if (created)
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
        else if (texture.width != size || texture.height != size)
            texture.Reinitialize(size, size, TextureFormat.RGBA32, true);

        texture.name = name;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 8;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float broad = Mathf.PerlinNoise((x + seed * 17f) * frequency,
                    (y + seed * 29f) * frequency);
                float fine = Mathf.PerlinNoise((x - seed * 11f) * frequency * 2.9f,
                    (y + seed * 7f) * frequency * 2.9f);
                float grit = Mathf.PerlinNoise((x + seed * 5f) * frequency * 7.1f,
                    (y - seed * 3f) * frequency * 7.1f);
                float grain = Hash01(x, y, seed);
                float value = Mathf.Clamp01(broad * 0.43f + fine * 0.27f + grit * 0.2f + grain * 0.1f);
                value = Mathf.Pow(value, 0.9f);
                Color pixel = Color.Lerp(dark, light, value);
                // URP Terrain/Lit reads diffuse alpha as per-pixel smoothness.
                // A value of zero keeps dry sand, earth, and rock fully matte.
                pixel.a = 0f;
                pixels[y * size + x] = pixel;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(true, false);
        if (created) AssetDatabase.CreateAsset(texture, path);
        EditorUtility.SetDirty(texture);
        return texture;
    }

    private static Texture2D EnsureNormalTexture(
        string path, string name, Texture2D source, float strength)
    {
        int width = source.width;
        int height = source.height;
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        bool created = normal == null;
        if (created)
            normal = new Texture2D(width, height, TextureFormat.RGBA32, true, true);
        else if (normal.width != width || normal.height != height)
            normal.Reinitialize(width, height, TextureFormat.RGBA32, true);

        normal.name = name;
        normal.wrapMode = TextureWrapMode.Repeat;
        normal.filterMode = FilterMode.Trilinear;
        normal.anisoLevel = 8;
        Color[] sourcePixels = source.GetPixels();
        Color[] normalPixels = new Color[sourcePixels.Length];
        for (int y = 0; y < height; y++)
        {
            int down = ((y - 1 + height) % height) * width;
            int up = ((y + 1) % height) * width;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int left = (x - 1 + width) % width;
                int right = (x + 1) % width;
                float dx = sourcePixels[row + right].grayscale - sourcePixels[row + left].grayscale;
                float dy = sourcePixels[up + x].grayscale - sourcePixels[down + x].grayscale;
                Vector3 value = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                normalPixels[row + x] = new Color(value.x * 0.5f + 0.5f,
                    value.y * 0.5f + 0.5f, value.z * 0.5f + 0.5f, 1f);
            }
        }
        normal.SetPixels(normalPixels);
        normal.Apply(true, false);
        if (created) AssetDatabase.CreateAsset(normal, path);
        EditorUtility.SetDirty(normal);
        return normal;
    }

    private static float Hash01(int x, int y, float seed)
    {
        float value = Mathf.Sin(x * 12.9898f + y * 78.233f + seed * 19.19f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    private static TerrainLayer EnsureLayer(
        string path, string name, Texture2D texture, Texture2D normalTexture,
        Vector2 tileSize, float smoothness, float normalScale)
    {
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer { name = name };
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = texture;
        layer.normalMapTexture = normalTexture;
        layer.normalScale = normalScale;
        layer.tileSize = tileSize;
        layer.tileOffset = Vector2.zero;
        layer.metallic = 0f;
        layer.smoothness = smoothness;
        layer.specular = Color.black;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    private static Material EnsureTerrainMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (shader == null)
        {
            Debug.LogError("Could not find the URP Terrain Lit shader for the desert terrain material.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Western Desert Terrain" };
            AssetDatabase.CreateAsset(material, TerrainMaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.enableInstancing = true;
        for (int i = 0; i < 4; i++)
        {
            material.SetFloat("_Metallic" + i, 0f);
            material.SetFloat("_Smoothness" + i, 0f);
        }
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void PaintTerrain(TerrainData data)
    {
        int width = data.alphamapWidth;
        int height = data.alphamapHeight;
        float[,,] weights = new float[height, width, 1];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                weights[z, x, 0] = 1f;
            }
        }
        data.SetAlphamaps(0, 0, weights);
    }

    private static Light FindSun()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
            if (lights[i] != null && lights[i].type == LightType.Directional) return lights[i];
        return null;
    }
}
#endif
