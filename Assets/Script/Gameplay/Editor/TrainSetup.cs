using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TrainSetup
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string TrainModelPath = "Assets/meshes/TRAIN.fbx";
    private const string TrainPrefabFolder = "Assets/Prefabs/Train";
    private const string ManagerName = "Train Manager";
    private const string SetupMarkerName = "Train And Shop Setup Ready 4";

    static TrainSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
    }

    [MenuItem("Tools/Game Setup/Setup Passing Train And Gun Shop")]
    public static void SetupPassingTrainAndGunShop()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogWarning("Open Assets/Scenes/game.unity before setting up the passing train.");
            return;
        }

        EnsureTrainModelReadable();
        GameObject trainAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TrainModelPath);
        if (trainAsset == null)
        {
            Debug.LogError("Could not load the imported train model at " + TrainModelPath);
            return;
        }

        GameObject enginePrefab = EnsureCarPrefab(trainAsset, "engine", "Train Engine.prefab");
        GameObject passengerPrefab = EnsureCarPrefab(trainAsset, "passenger", "Passenger Car.prefab");
        GameObject storagePrefab = EnsureCarPrefab(trainAsset, "STORAGE", "Storage Car.prefab");
        GameObject lastPrefab = EnsureCarPrefab(trainAsset, "last", "Last Car.prefab");

        GameObject tunnel = FindSceneObject(scene, "TUNNRL");
        GameObject bridge = FindSceneObject(scene, "BRIDGE");
        GameObject shop = FindSceneObject(scene, "SHOP");
        if (tunnel == null || bridge == null)
        {
            Debug.LogError("The train setup needs the TUNNRL and BRIDGE objects already placed in the scene.");
            return;
        }

        GameObject managerObject = FindSceneObject(scene, ManagerName);
        if (managerObject == null)
        {
            managerObject = new GameObject(ManagerName);
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            Undo.RegisterCreatedObjectUndo(managerObject, "Create Train Manager");
        }

        TrainManager manager = managerObject.GetComponent<TrainManager>();
        if (manager == null) manager = Undo.AddComponent<TrainManager>(managerObject);

        Transform routeStart = EnsureMarker(managerObject.transform, "Route Start - Behind Tunnel");
        Transform routeEnd = EnsureMarker(managerObject.transform, "Route End - After Bridge");
        ConfigureRoute(tunnel, bridge, routeStart, routeEnd);
        ConfigureManager(manager, trainAsset, enginePrefab,
            new[] { passengerPrefab, storagePrefab }, lastPrefab, routeStart, routeEnd);

        if (shop != null) RemoveAccidentalShopWeaponPhysics(shop);
        int placedWeapons = shop != null ? PlaceGunsOnShopTables(shop) : 0;
        RemoveObsoleteSetupMarkers(managerObject.transform);
        EnsureMarker(managerObject.transform, SetupMarkerName);

        EditorUtility.SetDirty(managerObject);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = managerObject;
        Debug.Log($"PASSING_TRAIN_AND_SHOP_READY: one engine plus five random wagons, 300-second service, tunnel-to-bridge route, and {placedWeapons} guns placed on shop tables.");
    }

    private static void TryAutomaticSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) return;

        GameObject manager = FindSceneObject(scene, ManagerName);
        if (manager != null && FindDeepChild(manager.transform, SetupMarkerName) != null) return;
        SetupPassingTrainAndGunShop();
    }

    private static void ConfigureRoute(GameObject tunnel, GameObject bridge, Transform routeStart, Transform routeEnd)
    {
        Bounds tunnelBounds = CalculateWorldBounds(tunnel);
        Bounds bridgeBounds = CalculateWorldBounds(bridge);
        Vector3 direction = bridgeBounds.center - tunnelBounds.center;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.right;
        direction.Normalize();

        float tunnelHalfLength = ProjectedHalfExtent(tunnelBounds.extents, direction);
        float bridgeHalfLength = ProjectedHalfExtent(bridgeBounds.extents, direction);
        Vector3 start = tunnelBounds.center - direction * (tunnelHalfLength + 12f);
        Vector3 end = bridgeBounds.center + direction * (bridgeHalfLength + 15f);

        start.y = 0f;
        end.y = 0f;
        routeStart.position = start;
        routeEnd.position = end;
        routeStart.rotation = Quaternion.LookRotation(direction, Vector3.up);
        routeEnd.rotation = routeStart.rotation;
    }

    private static float ProjectedHalfExtent(Vector3 extents, Vector3 direction)
    {
        Vector3 absolute = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
        return Vector3.Dot(extents, absolute);
    }

    private static void ConfigureManager(TrainManager manager, GameObject trainAsset,
        GameObject enginePrefab, GameObject[] wagonPrefabs, GameObject lastWagonPrefab,
        Transform routeStart, Transform routeEnd)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SetObject(serialized, "enginePrefab", enginePrefab);
        SetObjectArray(serialized, "wagonPrefabs", wagonPrefabs);
        SetObject(serialized, "lastWagonPrefab", lastWagonPrefab);
        SetObject(serialized, "trainModel", trainAsset);
        SetObject(serialized, "routeStart", routeStart);
        SetObject(serialized, "routeEnd", routeEnd);
        SetBool(serialized, "trainsEnabled", true);
        SetFloat(serialized, "firstTrainDelay", 8f);
        SetFloat(serialized, "repeatInterval", 300f);
        SetBool(serialized, "allowOverlappingTrains", true);
        SetFloat(serialized, "travelSpeed", 12f);
        SetFloat(serialized, "acceleration", 1.8f);
        SetString(serialized, "enginePartName", "engine");
        SetInt(serialized, "wagonCount", 5);
        SetFloat(serialized, "trainScale", 2.37f);
        SetFloat(serialized, "couplingGap", 0.65f);
        SetFloat(serialized, "railClearance", 0.03f);
        SetFloat(serialized, "despawnPadding", 2f);

        SerializedProperty wagonNames = serialized.FindProperty("wagonPartNames");
        if (wagonNames != null)
        {
            wagonNames.arraySize = 2;
            wagonNames.GetArrayElementAtIndex(0).stringValue = "passenger";
            wagonNames.GetArrayElementAtIndex(1).stringValue = "STORAGE";
        }
        SetString(serialized, "lastWagonPartName", "last");
        SetFloat(serialized, "engineYawCorrection", 180f);
        SetFloat(serialized, "wagonYawCorrection", 180f);
        SetFloat(serialized, "lastWagonYawCorrection", 0f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject EnsureCarPrefab(GameObject trainAsset, string partName, string fileName)
    {
        EnsureAssetFolder(TrainPrefabFolder);
        string path = TrainPrefabFolder + "/" + fileName;
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        Transform source = FindImportedPart(trainAsset.transform, partName);
        if (source == null)
        {
            Debug.LogWarning($"Could not create {fileName}: train part '{partName}' was not found.");
            return null;
        }

        GameObject root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(fileName));
        GameObject visual = UnityEngine.Object.Instantiate(source.gameObject, root.transform, false);
        visual.name = partName + " Visual";
        visual.transform.localPosition = source.localPosition;
        visual.transform.localRotation = source.localRotation;
        visual.transform.localScale = source.localScale;
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    private static Transform FindImportedPart(Transform root, string partName)
    {
        Transform[] parts = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
            if (string.Equals(parts[i].name, partName, StringComparison.OrdinalIgnoreCase))
                return parts[i];
        return null;
    }

    private static void EnsureAssetFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void EnsureTrainModelReadable()
    {
        ModelImporter importer = AssetImporter.GetAtPath(TrainModelPath) as ModelImporter;
        if (importer == null || importer.isReadable) return;
        importer.isReadable = true;
        importer.SaveAndReimport();
    }

    private static void RemoveAccidentalShopWeaponPhysics(GameObject shop)
    {
        PickupWeapon[] mistakenWeapons = shop.GetComponentsInChildren<PickupWeapon>(true);
        for (int i = 0; i < mistakenWeapons.Length; i++)
        {
            PickupWeapon mistaken = mistakenWeapons[i];
            if (mistaken == null || mistaken.name.StartsWith("FloorPickup_", StringComparison.OrdinalIgnoreCase))
                continue;
            GameObject target = mistaken.gameObject;
            Rigidbody body = target.GetComponent<Rigidbody>();
            BoxCollider box = target.GetComponent<BoxCollider>();
            UnityEngine.Object.DestroyImmediate(mistaken);
            if (body != null) UnityEngine.Object.DestroyImmediate(body);
            if (box != null) UnityEngine.Object.DestroyImmediate(box);
            EditorUtility.SetDirty(target);
        }
    }

    private static int PlaceGunsOnShopTables(GameObject shop)
    {
        Renderer[] shopRenderers = shop.GetComponentsInChildren<Renderer>(true);
        List<Renderer> tables = new List<Renderer>();
        for (int i = 0; i < shopRenderers.Length; i++)
            if (shopRenderers[i].name.IndexOf("TABLE", StringComparison.OrdinalIgnoreCase) >= 0)
                tables.Add(shopRenderers[i]);

        if (tables.Count == 0)
        {
            Debug.LogWarning("No table meshes were found under SHOP. Guns were left where they were.");
            return 0;
        }
        tables.Sort((a, b) => a.bounds.center.x.CompareTo(b.bounds.center.x));
        EnsureTableColliders(shop.transform, tables);

        PickupWeapon.PrepareSceneWeapons();
        PickupWeapon[] allWeapons = UnityEngine.Object.FindObjectsByType<PickupWeapon>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<PickupWeapon> weapons = new List<PickupWeapon>();
        for (int i = 0; i < allWeapons.Length; i++)
        {
            PickupWeapon weapon = allWeapons[i];
            if (weapon == null || weapon.IsHeld) continue;
            if (!weapon.name.StartsWith("FloorPickup_", StringComparison.OrdinalIgnoreCase)) continue;
            weapons.Add(weapon);
        }
        weapons.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

        int placed = 0;
        for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            int first = Mathf.FloorToInt((float)tableIndex * weapons.Count / tables.Count);
            int lastExclusive = Mathf.FloorToInt((float)(tableIndex + 1) * weapons.Count / tables.Count);
            int countOnTable = Mathf.Max(0, lastExclusive - first);
            Bounds tableBounds = tables[tableIndex].bounds;
            bool tableRunsAlongX = tableBounds.size.x > tableBounds.size.z;
            float longMin = tableRunsAlongX ? tableBounds.min.x : tableBounds.min.z;
            float longMax = tableRunsAlongX ? tableBounds.max.x : tableBounds.max.z;
            float margin = Mathf.Min(0.4f, (longMax - longMin) * 0.12f);

            for (int localIndex = 0; localIndex < countOnTable; localIndex++)
            {
                PickupWeapon weapon = weapons[first + localIndex];
                RemoveMissingScripts(weapon.gameObject);
                weapon.ConfigureForFloor();
                Bounds weaponBounds = CalculateWorldBounds(weapon.gameObject);
                bool weaponRunsAlongX = weaponBounds.size.x > weaponBounds.size.z;
                if (weaponRunsAlongX != tableRunsAlongX)
                    weapon.transform.rotation = Quaternion.AngleAxis(90f, Vector3.up) * weapon.transform.rotation;

                float t = countOnTable == 1 ? 0.5f : (localIndex + 0.5f) / countOnTable;
                float along = Mathf.Lerp(longMin + margin, longMax - margin, t);
                Vector3 desired = tableBounds.center;
                if (tableRunsAlongX) desired.x = along;
                else desired.z = along;
                desired.y = tableBounds.max.y + 0.5f;
                weapon.transform.position = desired;
                Physics.SyncTransforms();

                weaponBounds = CalculateWorldBounds(weapon.gameObject);
                weapon.transform.position += Vector3.up * (tableBounds.max.y + 0.035f - weaponBounds.min.y);
                Rigidbody body = weapon.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = weapon.transform.position;
                    body.rotation = weapon.transform.rotation;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = false;
                    body.useGravity = true;
                }
                EditorUtility.SetDirty(weapon);
                EditorUtility.SetDirty(weapon.gameObject);
                placed++;
            }
        }
        Physics.SyncTransforms();
        return placed;
    }

    private static void RemoveMissingScripts(GameObject root)
    {
        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(root);
        if (prefabRoot == root)
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[i].gameObject);
    }

    private static void EnsureTableColliders(Transform shop, List<Renderer> tables)
    {
        Transform colliderRoot = FindDeepChild(shop, "Shop Table Colliders");
        if (colliderRoot == null)
        {
            GameObject root = new GameObject("Shop Table Colliders");
            root.transform.SetParent(shop, true);
            colliderRoot = root.transform;
        }

        for (int i = 0; i < tables.Count; i++)
        {
            string colliderName = $"Table Surface {i + 1:00}";
            Transform existing = FindDeepChild(colliderRoot, colliderName);
            GameObject colliderObject = existing != null ? existing.gameObject : new GameObject(colliderName);
            colliderObject.transform.SetParent(colliderRoot, true);
            Bounds bounds = tables[i].bounds;
            colliderObject.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
            colliderObject.transform.localScale = Vector3.one;
            BoxCollider collider = colliderObject.GetComponent<BoxCollider>();
            if (collider == null) collider = colliderObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = bounds.size;
            EditorUtility.SetDirty(colliderObject);
        }
    }

    private static Transform EnsureMarker(Transform parent, string markerName)
    {
        Transform marker = FindDeepChild(parent, markerName);
        if (marker != null) return marker;
        GameObject markerObject = new GameObject(markerName);
        markerObject.transform.SetParent(parent, true);
        return markerObject.transform;
    }

    private static void RemoveObsoleteSetupMarkers(Transform parent)
    {
        Transform[] transforms = parent.GetComponentsInChildren<Transform>(true);
        for (int i = transforms.Length - 1; i >= 0; i--)
        {
            Transform candidate = transforms[i];
            if (candidate == parent || candidate.name == SetupMarkerName) continue;
            if (candidate.name.StartsWith("Train And Shop Setup", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(candidate.gameObject);
        }
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null) return null;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            if (transforms[i].name == childName) return transforms[i];
        return null;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int r = 0; r < roots.Length; r++)
        {
            Transform[] transforms = roots[r].GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i].name == objectName) return transforms[i].gameObject;
        }
        return null;
    }

    private static Bounds CalculateWorldBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void SetObject(SerializedObject serialized, string name, UnityEngine.Object value)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property != null) property.objectReferenceValue = value;
    }

    private static void SetObjectArray(SerializedObject serialized, string name, GameObject[] values)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null) return;
        property.arraySize = values != null ? values.Length : 0;
        for (int i = 0; i < property.arraySize; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetFloat(SerializedObject serialized, string name, float value)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property != null) property.floatValue = value;
    }

    private static void SetInt(SerializedObject serialized, string name, int value)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property != null) property.intValue = value;
    }

    private static void SetBool(SerializedObject serialized, string name, bool value)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property != null) property.boolValue = value;
    }

    private static void SetString(SerializedObject serialized, string name, string value)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property != null) property.stringValue = value;
    }
}

[CustomEditor(typeof(TrainManager))]
public sealed class TrainManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        TrainManager manager = (TrainManager)target;
        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Spawn Train Now")) manager.SpawnTrainNow();
        }
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                $"Next train: {manager.SecondsUntilNextTrain:0.0}s   Active trains: {manager.ActiveTrainCount}",
                MessageType.Info);
            Repaint();
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play Mode to test the train. All timing, movement, consist, and route settings are above.", MessageType.None);
        }
    }
}
