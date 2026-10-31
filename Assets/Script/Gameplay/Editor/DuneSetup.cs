#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class DuneSetup
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string MarkerName = "Subtle Drivable Dunes 0.32m Compact";
    private const string PreviousMarkerName = "Subtle Drivable Dunes 0.32m";
    private const float MaximumDuneHeight = 0.32f;
    private const float TrackFlatHalfWidth = 14f;
    private const float TrackBlendWidth = 16f;
    private static bool isApplying;

    static DuneSetup()
    {
        EditorApplication.update += TryAutoApply;
    }

    [MenuItem("Tools/Game Setup/Apply Subtle Drivable Dunes")]
    public static void ApplyFromMenu()
    {
        ApplyDunes(true);
    }

    public static void ApplyFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyDunes(false);
    }

    private static void TryAutoApply()
    {
        if (isApplying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) return;
        if (GameObject.Find(MarkerName) != null)
        {
            EditorApplication.update -= TryAutoApply;
            return;
        }
        isApplying = true;
        EditorApplication.update -= TryAutoApply;
        ApplyDunes(false);
        isApplying = false;
    }

    private static void ApplyDunes(bool showDialog)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Dune setup", "Open Assets/Scenes/game.unity first.", "OK");
            return;
        }

        if (GameObject.Find(MarkerName) != null)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Dune setup", "The subtle dunes are already applied.", "OK");
            return;
        }

        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (terrains.Length == 0)
        {
            Debug.LogError("Dune setup could not find any terrains.");
            return;
        }

        float trackZ = FindTrackZ();
        float trackY = FindTrackY();
        GameObject previousMarker = GameObject.Find(PreviousMarkerName);
        bool reshapeExistingDunes = previousMarker != null;
        int changedTerrains = 0;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null) continue;
            ApplyDunesToTerrain(terrain, trackZ, trackY, reshapeExistingDunes);
            changedTerrains++;
        }

        if (previousMarker != null) Undo.DestroyObjectImmediate(previousMarker);

        GameObject marker = new GameObject(MarkerName);
        Undo.RegisterCreatedObjectUndo(marker, "Mark subtle drivable dunes");
        marker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Recreate the deterministic prop scatter so every prop rests on the new surface.
        DesertScatterSetup.ScatterFromMenu();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("SUBTLE_DUNES_RESHAPED: " + changedTerrains + " terrains, maximum height " +
                  MaximumDuneHeight.ToString("0.00") +
                  "m, compact rounded drivable dunes, and railway ground flattened to y=" +
                  trackY.ToString("0.##") + ".");
    }

    private static void ApplyDunesToTerrain(
        Terrain terrain, float trackZ, float trackY, bool reshapeExistingDunes)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, resolution, resolution);
        float[,] result = new float[resolution, resolution];
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = data.size;
        float sampleSpacingX = terrainSize.x / Mathf.Max(1, resolution - 1);
        float sampleSpacingZ = terrainSize.z / Mathf.Max(1, resolution - 1);
        float levelTrackHeight = Mathf.Clamp01((trackY - terrainPosition.y) /
                                                Mathf.Max(0.001f, terrainSize.y));

        for (int z = 0; z < resolution; z++)
        {
            int previousZ = Mathf.Max(0, z - 1);
            int nextZ = Mathf.Min(resolution - 1, z + 1);
            float worldZ = terrainPosition.z + z * sampleSpacingZ;
            float trackDistance = Mathf.Abs(worldZ - trackZ);
            float trackBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(TrackFlatHalfWidth,
                    TrackFlatHalfWidth + TrackBlendWidth, trackDistance));

            for (int x = 0; x < resolution; x++)
            {
                int previousX = Mathf.Max(0, x - 1);
                int nextX = Mathf.Min(resolution - 1, x + 1);
                float original = heights[z, x];
                float localHeight = original * terrainSize.y;
                float derivativeX = (heights[z, nextX] - heights[z, previousX]) * terrainSize.y /
                                    Mathf.Max(0.001f, (nextX - previousX) * sampleSpacingX);
                float derivativeZ = (heights[nextZ, x] - heights[previousZ, x]) * terrainSize.y /
                                    Mathf.Max(0.001f, (nextZ - previousZ) * sampleSpacingZ);
                float slope = Mathf.Atan(Mathf.Sqrt(derivativeX * derivativeX +
                                                    derivativeZ * derivativeZ)) * Mathf.Rad2Deg;
                float flatGroundBlend = 1f - Mathf.SmoothStep(6f, 17f, slope);
                float lowGroundBlend = 1f - Mathf.SmoothStep(8f, 25f, localHeight);

                float worldX = terrainPosition.x + x * sampleSpacingX;
                float oldBroad = Mathf.PerlinNoise(worldX * 0.0065f + 41.7f,
                    worldZ * 0.0065f + 17.3f);
                float oldSecondary = Mathf.PerlinNoise(worldX * 0.012f - 23.1f,
                    worldZ * 0.012f + 63.9f);
                float oldRoundedNoise = Mathf.SmoothStep(0.25f, 0.82f,
                    oldBroad * 0.74f + oldSecondary * 0.26f);

                float compactBroad = Mathf.PerlinNoise(worldX * 0.016f + 41.7f,
                    worldZ * 0.016f + 17.3f);
                float compactSecondary = Mathf.PerlinNoise(worldX * 0.032f - 23.1f,
                    worldZ * 0.032f + 63.9f);
                float compactRoundedNoise = Mathf.SmoothStep(0.24f, 0.8f,
                    compactBroad * 0.8f + compactSecondary * 0.2f);
                float noiseChange = reshapeExistingDunes
                    ? compactRoundedNoise - oldRoundedNoise
                    : compactRoundedNoise;
                float duneHeightChange = noiseChange * MaximumDuneHeight * flatGroundBlend *
                                         lowGroundBlend * trackBlend;
                float duneResult = Mathf.Clamp01(original + duneHeightChange / terrainSize.y);
                float corridorFlattening = 1f - trackBlend;
                result[z, x] = Mathf.Lerp(duneResult, levelTrackHeight, corridorFlattening);
            }
        }

        Undo.RegisterCompleteObjectUndo(data, "Apply subtle drivable dunes");
        data.SetHeights(0, 0, result);
        EditorUtility.SetDirty(data);
        terrain.Flush();
        EditorUtility.SetDirty(terrain);
    }

    private static float FindTrackZ()
    {
        GameObject track = GameObject.Find("Straight Train Track");
        Transform marker = track != null ? track.transform.Find("Level Track Plane") : null;
        return marker != null ? marker.position.z : 129f;
    }

    private static float FindTrackY()
    {
        GameObject track = GameObject.Find("Straight Train Track");
        Transform marker = track != null ? track.transform.Find("Level Track Plane") : null;
        return marker != null ? marker.position.y : 0f;
    }
}
#endif
