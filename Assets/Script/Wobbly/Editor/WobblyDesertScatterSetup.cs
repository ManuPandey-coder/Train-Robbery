#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class WobblyDesertScatterSetup
{
    private enum PropKind { Rock, Cactus, Grass, Deadwood }

    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string RootName = "Desert Props";
    private const float TrackClearance = 15f;
    private const float EdgeMargin = 8f;
    private const float MinimumSpacing = 7f;
    private const int Seed = 731947;

    private static readonly string[] RockPrefabPaths =
    {
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Rocks/EA04_Env_Rocks_Sand_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Rocks/EA04_Env_Rocks_Sand_01b_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Rocks/EA04_Env_Rocks_Sand_01c_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Rocks/EA04_Env_Rocks_Sand_01d_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Stone/EA04_Env_Stone_Sand_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Stone/EA04_Env_Stone_Sand_01b_PRE.prefab"
    };

    private static readonly string[] CactusPrefabPaths =
    {
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Cactus_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Cactus_01b_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Cactus_01c_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Cactus_Dry_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Cactus_Dry_01b_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Cactus_Dry_01c_PRE.prefab"
    };

    private static readonly string[] GrassPrefabPaths =
    {
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Grass_Dry_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Grass_Dry_01b_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Grass_Dry_01b.001_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Plants/EA04_Env_Plants_Grass_Dry_01c_PRE.prefab"
    };

    private static readonly string[] DeadwoodPrefabPaths =
    {
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Trunk/EA04_Env_Tree_Trunk_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Trunk/EA04_Env_Tree_Trunk_01c_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Trunk/EA04_Env_Tree_Trunk_01e_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Roots/EA04_Env_Tree_Root_01a_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Roots/EA04_Env_Tree_Root_01c_PRE.prefab",
        "Assets/Third Party/EmaceArt - raft on the desert/Prefabs/Env/Roots/EA04_Env_Tree_Root_02a_PRE.prefab"
    };

    static WobblyDesertScatterSetup()
    {
        EditorApplication.delayCall += TryAutoScatter;
    }

    [MenuItem("Tools/Wobbly/Scatter Desert Rocks And Cacti")]
    public static void ScatterFromMenu()
    {
        Scatter(true);
    }

    public static void ScatterFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Scatter(false);
    }

    private static void TryAutoScatter()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) return;
        GameObject existing = GameObject.Find(RootName);
        if (existing != null && existing.transform.Find("Rocks") != null &&
            existing.transform.Find("Cacti") != null &&
            existing.transform.Find("Dry Grass") != null &&
            existing.transform.Find("Deadwood") != null) return;
        Scatter(false);
    }

    private static void Scatter(bool showDialog)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Desert scatter", "Open Assets/Scenes/game.unity first.", "OK");
            return;
        }

        GameObject[] rockPrefabs = LoadPrefabs(RockPrefabPaths);
        GameObject[] cactusPrefabs = LoadPrefabs(CactusPrefabPaths);
        GameObject[] grassPrefabs = LoadPrefabs(GrassPrefabPaths);
        GameObject[] deadwoodPrefabs = LoadPrefabs(DeadwoodPrefabPaths);
        if (rockPrefabs.Length == 0 || cactusPrefabs.Length == 0 ||
            grassPrefabs.Length == 0 || deadwoodPrefabs.Length == 0)
        {
            Debug.LogError("Desert scatter could not load all desert prop prefabs.");
            return;
        }

        Terrain[] terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        Array.Sort(terrains, (left, right) =>
            left.transform.position.x.CompareTo(right.transform.position.x));
        if (terrains.Length == 0)
        {
            Debug.LogError("Desert scatter could not find any terrains.");
            return;
        }

        GameObject oldRoot = GameObject.Find(RootName);
        if (oldRoot != null) Undo.DestroyObjectImmediate(oldRoot);
        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Scatter desert props");
        GameObject rocksRoot = new GameObject("Rocks");
        rocksRoot.transform.SetParent(root.transform, false);
        GameObject cactiRoot = new GameObject("Cacti");
        cactiRoot.transform.SetParent(root.transform, false);
        GameObject grassRoot = new GameObject("Dry Grass");
        grassRoot.transform.SetParent(root.transform, false);
        GameObject deadwoodRoot = new GameObject("Deadwood");
        deadwoodRoot.transform.SetParent(root.transform, false);

        float[] rockMinimumY = CalculatePrefabMinimumY(rockPrefabs, scene);
        float[] cactusMinimumY = CalculatePrefabMinimumY(cactusPrefabs, scene);
        float[] grassMinimumY = CalculatePrefabMinimumY(grassPrefabs, scene);
        float[] deadwoodMinimumY = CalculatePrefabMinimumY(deadwoodPrefabs, scene);
        float trackZ = FindTrackZ();
        System.Random random = new System.Random(Seed);
        List<Vector2> largePropPositions = new List<Vector2>();
        List<Vector2> grassPositions = new List<Vector2>();
        int rockCount = 0;
        int cactusCount = 0;
        int grassCount = 0;
        int deadwoodCount = 0;

        for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
        {
            Terrain terrain = terrains[terrainIndex];
            if (terrain == null || terrain.terrainData == null) continue;
            Vector3 size = terrain.terrainData.size;
            float area = size.x * size.z;
            int desiredRocks = Mathf.Clamp(Mathf.RoundToInt(area / 5000f), 45, 115);
            int desiredCacti = Mathf.Clamp(Mathf.RoundToInt(area / 11000f), 22, 55);
            int desiredGrass = Mathf.Clamp(Mathf.RoundToInt(area / 2500f), 90, 210);
            int desiredDeadwood = Mathf.Clamp(Mathf.RoundToInt(area / 30000f), 8, 22);
            rockCount += ScatterType(terrain, rocksRoot.transform, rockPrefabs, rockMinimumY,
                desiredRocks, 55f, 0.65f, 2.35f, trackZ, largePropPositions, 7f,
                random, PropKind.Rock, rockCount, 0.008f, 0.38f);
            cactusCount += ScatterType(terrain, cactiRoot.transform, cactusPrefabs, cactusMinimumY,
                desiredCacti, 22f, 0.7f, 1.55f, trackZ, largePropPositions, 8f,
                random, PropKind.Cactus, cactusCount, 0.006f, 0.48f);
            grassCount += ScatterType(terrain, grassRoot.transform, grassPrefabs, grassMinimumY,
                desiredGrass, 20f, 0.55f, 1.45f, trackZ, grassPositions, 3.2f,
                random, PropKind.Grass, grassCount, 0.012f, 0.42f);
            deadwoodCount += ScatterType(terrain, deadwoodRoot.transform, deadwoodPrefabs,
                deadwoodMinimumY, desiredDeadwood, 34f, 0.7f, 1.45f, trackZ,
                largePropPositions, 10f, random, PropKind.Deadwood, deadwoodCount, 0.005f, 0.35f);
        }

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("DESERT_PROPS_SCATTERED: " + rockCount + " rocks, " + cactusCount +
                  " cacti, " + grassCount + " dry grass tufts, and " + deadwoodCount +
                  " deadwood props across " + terrains.Length +
                  " terrains; clustered variation added and railway corridor kept clear.");
    }

    private static int ScatterType(
        Terrain terrain, Transform parent, GameObject[] prefabs, float[] minimumY,
        int desiredCount, float maximumSlope, float minimumScale, float maximumScale,
        float trackZ, List<Vector2> occupied, float minimumSpacing, System.Random random,
        PropKind kind, int startingIndex, float noiseScale, float noiseThreshold)
    {
        Vector3 position = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float minimumSpacingSquared = minimumSpacing * minimumSpacing;
        int placed = 0;
        int attempts = desiredCount * 30;
        for (int attempt = 0; attempt < attempts && placed < desiredCount; attempt++)
        {
            float worldX = Mathf.Lerp(position.x + EdgeMargin, position.x + size.x - EdgeMargin,
                (float)random.NextDouble());
            float worldZ = Mathf.Lerp(position.z + EdgeMargin, position.z + size.z - EdgeMargin,
                (float)random.NextDouble());
            if (Mathf.Abs(worldZ - trackZ) < TrackClearance) continue;
            float distributionNoise = Mathf.PerlinNoise(
                worldX * noiseScale + (int)kind * 17.31f,
                worldZ * noiseScale + (int)kind * 29.17f);
            if (distributionNoise < noiseThreshold && random.NextDouble() > 0.12) continue;

            float normalizedX = Mathf.InverseLerp(position.x, position.x + size.x, worldX);
            float normalizedZ = Mathf.InverseLerp(position.z, position.z + size.z, worldZ);
            float slope = terrain.terrainData.GetSteepness(normalizedX, normalizedZ);
            if (slope > maximumSlope) continue;

            Vector2 candidate = new Vector2(worldX, worldZ);
            bool overlaps = false;
            for (int i = 0; i < occupied.Count; i++)
            {
                if ((occupied[i] - candidate).sqrMagnitude < minimumSpacingSquared)
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps) continue;

            int prefabIndex = random.Next(prefabs.Length);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefabs[prefabIndex], parent) as GameObject;
            if (instance == null) continue;
            float scale = Mathf.Lerp(minimumScale, maximumScale, (float)random.NextDouble());
            float groundY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + position.y;
            float yaw = (float)random.NextDouble() * 360f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            if (kind == PropKind.Rock || kind == PropKind.Deadwood)
            {
                Vector3 normal = terrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
                rotation = Quaternion.FromToRotation(Vector3.up,
                    Vector3.Slerp(Vector3.up, normal, 0.7f)) * rotation;
            }
            else if (kind == PropKind.Grass)
            {
                rotation = Quaternion.Euler(
                    Mathf.Lerp(-4f, 4f, (float)random.NextDouble()), yaw,
                    Mathf.Lerp(-4f, 4f, (float)random.NextDouble()));
            }
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.SetPositionAndRotation(new Vector3(worldX,
                groundY - minimumY[prefabIndex] * scale + 0.015f, worldZ), rotation);
            string label = kind == PropKind.Cactus ? "Cactus " :
                           kind == PropKind.Grass ? "Dry Grass " :
                           kind == PropKind.Deadwood ? "Deadwood " : "Rock ";
            instance.name = label + (startingIndex + placed + 1).ToString("000");
            MarkStatic(instance);
            if (kind == PropKind.Cactus) AddCactusCollider(instance);
            else if (kind == PropKind.Rock || kind == PropKind.Deadwood)
                AddRockColliders(instance);
            occupied.Add(candidate);
            placed++;
        }
        return placed;
    }

    private static GameObject[] LoadPrefabs(string[] paths)
    {
        List<GameObject> loaded = new List<GameObject>();
        for (int i = 0; i < paths.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (prefab != null) loaded.Add(prefab);
        }
        return loaded.ToArray();
    }

    private static float[] CalculatePrefabMinimumY(GameObject[] prefabs, Scene scene)
    {
        float[] values = new float[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject probe = PrefabUtility.InstantiatePrefab(prefabs[i], scene) as GameObject;
            if (probe == null) continue;
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            Renderer[] renderers = probe.GetComponentsInChildren<Renderer>(true);
            float minimum = 0f;
            if (renderers.Length > 0)
            {
                minimum = renderers[0].bounds.min.y;
                for (int r = 1; r < renderers.Length; r++)
                    minimum = Mathf.Min(minimum, renderers[r].bounds.min.y);
            }
            values[i] = minimum;
            UnityEngine.Object.DestroyImmediate(probe);
        }
        return values;
    }

    private static float FindTrackZ()
    {
        GameObject track = GameObject.Find("Straight Train Track");
        Transform marker = track != null ? track.transform.Find("Level Track Plane") : null;
        return marker != null ? marker.position.z : 129f;
    }

    private static void AddRockColliders(GameObject root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh == null || filters[i].GetComponent<Collider>() != null) continue;
            MeshCollider collider = filters[i].gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filters[i].sharedMesh;
        }
    }

    private static void AddCactusCollider(GameObject root)
    {
        if (root.GetComponentInChildren<Collider>() != null) return;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.direction = 1;
        collider.center = root.transform.InverseTransformPoint(bounds.center);
        float inverseScale = 1f / Mathf.Max(0.001f, root.transform.lossyScale.y);
        collider.height = bounds.size.y * inverseScale;
        collider.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.28f * inverseScale;
    }

    private static void MarkStatic(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            GameObjectUtility.SetStaticEditorFlags(transforms[i].gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
    }
}
#endif
