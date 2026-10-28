#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class WobblyTrackSetup
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string TrackRootName = "Straight Train Track";
    private const string TrackPrefabPath =
        "Assets/Third Party/TrueTrackSystem/Prefabs/TrackPrefabs/trackStraight_v2.prefab";
    private const string SleeperPrefabPath =
        "Assets/Third Party/TrueTrackSystem/Prefabs/TrackPrefabs/sleeper_v2.prefab";
    private const string TrackAssetFolder = "Assets/Third Party/TrueTrackSystem";
    private const float SegmentSpacing = 4f;
    private const float SleeperSpacing = 1f;
    private const float TrackOverhang = 12f;
    private const float GroundClearance = 0.035f;
    private static readonly Quaternion SleeperRotation = Quaternion.Euler(0f, 90f, 0f);

    static WobblyTrackSetup()
    {
        EditorApplication.delayCall += TryAutoBuild;
    }

    [MenuItem("Tools/Wobbly/Build Straight Train Track")]
    public static void BuildFromMenu()
    {
        BuildTrack(true);
    }

    public static void BuildFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BuildTrack(false);
    }

    private static void TryAutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) return;
        GameObject existing = GameObject.Find(TrackRootName);
        Transform existingSleepers = existing != null ? existing.transform.Find("Wooden Sleepers") : null;
        Transform existingRails = existing != null ? existing.transform.Find("Metal Rails") : null;
        Terrain mainTerrain = FindMainTerrain();
        Terrain[] allTerrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        float terrainMinimumX;
        float terrainMaximumX;
        bool hasTerrainSpan = TryGetTerrainSpan(allTerrains, out terrainMinimumX, out terrainMaximumX);
        int expectedRailCount = mainTerrain != null && hasTerrainSpan
            ? Mathf.FloorToInt((terrainMaximumX - terrainMinimumX + TrackOverhang * 2f) / SegmentSpacing)
            : 0;
        bool sleepersAreSideways = existingSleepers != null && existingSleepers.childCount > 0 &&
                                   Quaternion.Angle(existingSleepers.GetChild(0).rotation,
                                       SleeperRotation) < 0.1f;
        bool trackHasFullOverhang = existingRails != null && existingRails.childCount == expectedRailCount;
        Transform levelMarker = existing != null ? existing.transform.Find("Level Track Plane") : null;
        float desiredCenterZ = mainTerrain != null && mainTerrain.terrainData != null
            ? mainTerrain.transform.position.z + mainTerrain.terrainData.size.z * 0.5f
            : 0f;
        float desiredLevelY = mainTerrain != null && mainTerrain.terrainData != null
            ? mainTerrain.SampleHeight(new Vector3(
                mainTerrain.transform.position.x + mainTerrain.terrainData.size.x * 0.5f,
                0f, desiredCenterZ)) + mainTerrain.transform.position.y
            : 0f;
        bool trackIsLevel = levelMarker != null &&
                            Mathf.Abs(levelMarker.position.z - desiredCenterZ) < 0.01f &&
                            Mathf.Abs(levelMarker.position.y - desiredLevelY) < 0.01f;
        if (existing != null && sleepersAreSideways && trackHasFullOverhang && trackIsLevel &&
            existing.transform.Find("Center Track Spline") != null) return;
        BuildTrack(false);
    }

    private static void BuildTrack(bool showDialog)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Train track setup", "Open Assets/Scenes/game.unity first.", "OK");
            return;
        }

        ConfigureTrackAssets();

        GameObject trackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrackPrefabPath);
        GameObject sleeperPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SleeperPrefabPath);
        if (trackPrefab == null || sleeperPrefab == null)
        {
            Debug.LogError("Train track setup could not load the straight rail and wooden sleeper prefabs.");
            return;
        }

        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("Train track setup could not find the main terrain.");
            return;
        }
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        float terrainMinimumX;
        float terrainMaximumX;
        if (!TryGetTerrainSpan(terrains, out terrainMinimumX, out terrainMaximumX))
        {
            Debug.LogError("Train track setup could not calculate the combined terrain span.");
            return;
        }

        GameObject oldRoot = GameObject.Find(TrackRootName);
        if (oldRoot != null) Undo.DestroyObjectImmediate(oldRoot);

        GameObject root = new GameObject(TrackRootName);
        Undo.RegisterCreatedObjectUndo(root, "Build straight train track");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        GameObject railsRoot = new GameObject("Metal Rails");
        railsRoot.transform.SetParent(root.transform, false);
        GameObject sleepersRoot = new GameObject("Wooden Sleepers");
        sleepersRoot.transform.SetParent(root.transform, false);

        GameObject probe = PrefabUtility.InstantiatePrefab(trackPrefab, scene) as GameObject;
        if (probe == null)
        {
            Undo.DestroyObjectImmediate(root);
            Debug.LogError("Train track setup could not instantiate the straight track prefab.");
            return;
        }
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Bounds probeBounds = CalculateRendererBounds(probe);
        Object.DestroyImmediate(probe);

        GameObject sleeperProbe = PrefabUtility.InstantiatePrefab(sleeperPrefab, scene) as GameObject;
        if (sleeperProbe == null)
        {
            Undo.DestroyObjectImmediate(root);
            Debug.LogError("Train track setup could not instantiate the wooden sleeper prefab.");
            return;
        }
        sleeperProbe.transform.SetPositionAndRotation(Vector3.zero, SleeperRotation);
        Bounds sleeperBounds = CalculateRendererBounds(sleeperProbe);
        Object.DestroyImmediate(sleeperProbe);

        float minimumX = terrainMinimumX - TrackOverhang;
        float maximumX = terrainMaximumX + TrackOverhang;
        float centerZ = terrain.transform.position.z + terrain.terrainData.size.z * 0.5f;
        float centerX = terrain.transform.position.x + terrain.terrainData.size.x * 0.5f;
        float levelGroundY = terrain.SampleHeight(new Vector3(centerX, 0f, centerZ)) +
                             terrain.transform.position.y;
        GameObject levelMarker = new GameObject("Level Track Plane");
        levelMarker.transform.SetParent(root.transform, false);
        levelMarker.transform.position = new Vector3(0f, levelGroundY, centerZ);
        float availableLength = Mathf.Max(SegmentSpacing, maximumX - minimumX);
        int segmentCount = Mathf.Max(1, Mathf.FloorToInt(availableLength / SegmentSpacing));
        float builtLength = segmentCount * SegmentSpacing;
        float firstCenterX = (minimumX + maximumX - builtLength) * 0.5f + SegmentSpacing * 0.5f;

        List<GameObject> railSegments = new List<GameObject>(segmentCount);
        for (int i = 0; i < segmentCount; i++)
        {
            GameObject segment = PrefabUtility.InstantiatePrefab(trackPrefab, railsRoot.transform) as GameObject;
            if (segment == null) continue;

            segment.name = "Straight Track " + (i + 1).ToString("000");
            float desiredCenterX = firstCenterX + i * SegmentSpacing;
            Vector3 position = new Vector3(
                desiredCenterX - probeBounds.center.x,
                levelGroundY - probeBounds.min.y + GroundClearance,
                centerZ - probeBounds.center.z);
            segment.transform.SetPositionAndRotation(position, Quaternion.identity);
            AddStaticColliders(segment);
            railSegments.Add(segment);
        }

        float trackStartX = firstCenterX - SegmentSpacing * 0.5f;
        int sleeperCount = Mathf.FloorToInt(builtLength / SleeperSpacing) + 1;
        for (int i = 0; i < sleeperCount; i++)
        {
            float desiredCenterX = trackStartX + i * SleeperSpacing;
            GameObject sleeper = PrefabUtility.InstantiatePrefab(sleeperPrefab, sleepersRoot.transform) as GameObject;
            if (sleeper == null) continue;
            sleeper.name = "Wooden Sleeper " + (i + 1).ToString("0000");
            sleeper.transform.SetPositionAndRotation(new Vector3(
                desiredCenterX - sleeperBounds.center.x,
                levelGroundY - sleeperBounds.min.y + GroundClearance * 0.45f,
                centerZ - sleeperBounds.center.z), SleeperRotation);
            MarkStatic(sleeper);
        }

        bool splineCreated = CreateCombinedSpline(root, railSegments);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("STRAIGHT_TRAIN_TRACK_BUILT: " + segmentCount +
                  " rail segments, " + sleeperCount + " wooden sleepers, and TrueTrackSystem spline=" +
                  (splineCreated ? "yes" : "no") + " across all " + terrains.Length +
                  " terrains on one level at z=" + centerZ.ToString("0.##") +
                  ", y=" + levelGroundY.ToString("0.##") + ".");
    }

    private static bool TryGetTerrainSpan(Terrain[] terrains, out float minimumX, out float maximumX)
    {
        minimumX = float.PositiveInfinity;
        maximumX = float.NegativeInfinity;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null) continue;
            minimumX = Mathf.Min(minimumX, terrain.transform.position.x);
            maximumX = Mathf.Max(maximumX,
                terrain.transform.position.x + terrain.terrainData.size.x);
        }
        return !float.IsInfinity(minimumX) && !float.IsInfinity(maximumX) && maximumX > minimumX;
    }

    private static float SampleTerrainHeight(Terrain[] terrains, float worldX, float worldZ)
    {
        Terrain nearest = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 nearestPoint = Vector3.zero;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null) continue;
            Vector3 position = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float clampedX = Mathf.Clamp(worldX, position.x, position.x + size.x);
            float clampedZ = Mathf.Clamp(worldZ, position.z, position.z + size.z);
            float distance = (worldX - clampedX) * (worldX - clampedX) +
                             (worldZ - clampedZ) * (worldZ - clampedZ);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = terrain;
            nearestPoint = new Vector3(clampedX, 0f, clampedZ);
            if (distance < 0.0001f) break;
        }

        return nearest != null
            ? nearest.SampleHeight(nearestPoint) + nearest.transform.position.y
            : 0f;
    }

    private static Terrain FindMainTerrain()
    {
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        Terrain nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null) continue;
            if (terrain.gameObject.name == "Terrain") return terrain;
            float distance = terrain.transform.position.sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = terrain;
            }
        }
        return nearest;
    }

    private static Bounds CalculateRendererBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(Vector3.zero, new Vector3(SegmentSpacing, 0.2f, 2f));
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void AddStaticColliders(GameObject root)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
            {
                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
            GameObjectUtility.SetStaticEditorFlags(filter.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
        }
    }

    private static void MarkStatic(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            GameObjectUtility.SetStaticEditorFlags(transforms[i].gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
    }

    private static bool CreateCombinedSpline(GameObject root, List<GameObject> railSegments)
    {
        GameObject looseSpline = GameObject.Find("CombinedSpline");
        if (looseSpline != null && !looseSpline.transform.IsChildOf(root.transform))
            Undo.DestroyObjectImmediate(looseSpline);

        System.Type managerType = System.Type.GetType("TrueTrackSystem.SplineManager, Assembly-CSharp");
        if (managerType == null)
        {
            Debug.LogError("TrueTrackSystem SplineManager was not found after installing Unity Splines.");
            return false;
        }

        Component manager = root.AddComponent(managerType);
        managerType.GetField("splinePrefabs")?.SetValue(manager, railSegments.ToArray());
        managerType.GetField("tangentValue")?.SetValue(manager, SegmentSpacing * 0.33f);
        managerType.GetField("closeThreshold")?.SetValue(manager, 0.05f);
        managerType.GetMethod("CreateConnectedSpline")?.Invoke(manager, null);

        GameObject combined = GameObject.Find("CombinedSpline");
        if (combined == null) return false;
        combined.name = "Center Track Spline";
        combined.transform.SetParent(root.transform, true);
        return true;
    }

    private static void ConfigureTrackAssets()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TrackAssetFolder + "/Textures" });
        for (int i = 0; i < textureGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            bool changed = false;
            string lower = path.ToLowerInvariant();
            if (lower.Contains("_normal") && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }
            if ((lower.Contains("metallic") || lower.Contains("smoothness")) && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { TrackAssetFolder + "/Materials" });
        for (int i = 0; i < materialGuids.Length; i++)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                AssetDatabase.GUIDToAssetPath(materialGuids[i]));
            if (material == null) continue;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }
    }

}
#endif
