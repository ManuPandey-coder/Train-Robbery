#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class WobblyVehicleSetup
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string SourcePrefabPath = "Assets/Third Party/Pack_Pickup/Prefabs/Pickup.prefab";
    private const string VehiclePrefabPath = "Assets/Prefabs/Vehicles/DriveablePickup.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/WobblyPlayer.prefab";
    private const string SceneTruckName = "DriveablePickup";
    private static bool running;

    static WobblyVehicleSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryAutomaticSetup;
        };
    }

    [MenuItem("Tools/Wobbly Character/Setup Driveable Pickup")]
    public static void SetupDriveablePickup()
    {
        if (running) return;
        running = true;
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/Vehicles");
            EnsurePlayerInteractor();
            GameObject vehiclePrefab = EnsureVehiclePrefab();
            if (vehiclePrefab == null) return;

            GameObject truck = FindSceneObject(scene, SceneTruckName);
            if (truck == null)
            {
                truck = PrefabUtility.InstantiatePrefab(vehiclePrefab, scene) as GameObject;
                if (truck == null) return;
                Undo.RegisterCreatedObjectUndo(truck, "Place driveable pickup");
                truck.name = SceneTruckName;
                PlaceNearPlayer(truck);
                Debug.Log("Placed the driveable pickup near the player.");
            }

            WobblyActiveRagdoll player = UnityEngine.Object.FindFirstObjectByType<WobblyActiveRagdoll>();
            if (player != null && player.GetComponent<WobblyVehicleInteractor>() == null)
            {
                WobblyVehicleInteractor added = Undo.AddComponent<WobblyVehicleInteractor>(player.gameObject);
                if (PrefabUtility.IsPartOfPrefabInstance(added))
                    PrefabUtility.ApplyAddedComponent(added, PlayerPrefabPath, InteractionMode.AutomatedAction);
            }

            EditorUtility.SetDirty(truck);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Driveable pickup setup complete. F sits directly/gets out, Q switches seats, E starts or stops driving from the driver seat, WASD drives, and Space brakes.");
        }
        finally
        {
            running = false;
        }
    }

    private static void EnsurePlayerInteractor()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null || prefab.GetComponent<WobblyVehicleInteractor>() != null) return;

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (contents.GetComponent<WobblyVehicleInteractor>() == null)
                contents.AddComponent<WobblyVehicleInteractor>();
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static GameObject EnsureVehiclePrefab()
    {
        GameObject configured = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath);
        bool updatingExisting = configured != null && configured.GetComponent<WobblyVehicleController>() != null;
        if (!updatingExisting && AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath) == null)
        {
            Debug.LogError("Could not find the downloaded pickup prefab at " + SourcePrefabPath);
            return null;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(updatingExisting ? VehiclePrefabPath : SourcePrefabPath);
        try
        {
            if (!updatingExisting)
            {
                root.name = SceneTruckName;
                root.transform.localScale = Vector3.one * 0.6f;
                RemoveDownloadedControllers(root);
                ConfigureRigidBody(root);
            }
            ReplaceBlockingCollider(root);

            WobblyVehicleController controller = root.GetComponent<WobblyVehicleController>();
            if (controller == null) controller = root.AddComponent<WobblyVehicleController>();

            RemoveStorageCover(root);
            WobblyVehicleDoor leftDoor = ConfigureSideDoor(root, controller, "DoorL", 72f);
            WobblyVehicleDoor rightDoor = ConfigureSideDoor(root, controller, "DoorR", -72f);
            WobblyVehicleDoor bonnet = ConfigureBonnet(root, controller);
            WobblyVehicleDoor tailgate = ConfigureTailgate(root, controller);
            ConfigureSeats(root, controller, leftDoor, rightDoor);

            WheelCollider fl = FindComponent<WheelCollider>(root.transform, "FL_Col");
            WheelCollider fr = FindComponent<WheelCollider>(root.transform, "FR_Col");
            WheelCollider bl = FindComponent<WheelCollider>(root.transform, "BL_Col");
            WheelCollider br = FindComponent<WheelCollider>(root.transform, "BR_Col");
            ConfigureWheel(fl);
            ConfigureWheel(fr);
            ConfigureWheel(bl);
            ConfigureWheel(br);
            Transform steeringWheel = FindDeepChild(root.transform, "SteeringWheel");
            ConfigureSteeringGrips(steeringWheel, out Transform leftGrip, out Transform rightGrip);
            controller.Configure(
                fl, fr, bl, br,
                FindDeepChild(root.transform, "FL_Mesh"), FindDeepChild(root.transform, "FR_Mesh"),
                FindDeepChild(root.transform, "BL_Mesh"), FindDeepChild(root.transform, "BR_Mesh"),
                steeringWheel, leftGrip, rightGrip,
                new[] { leftDoor, rightDoor, bonnet, tailgate });

            configured = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            Debug.Log((updatingExisting ? "Updated" : "Created") +
                      " configured vehicle prefab: " + VehiclePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return configured;
    }

    private static void RemoveDownloadedControllers(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = behaviours.Length - 1; i >= 0; i--)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;
            string typeName = behaviour.GetType().Name;
            if (typeName == "CarControl" || typeName == "CameraControl")
                UnityEngine.Object.DestroyImmediate(behaviour, true);
        }
    }

    private static void ConfigureRigidBody(GameObject root)
    {
        Rigidbody body = root.GetComponent<Rigidbody>();
        if (body == null) body = root.AddComponent<Rigidbody>();
        body.mass = 1250f;
        body.linearDamping = 0.08f;
        body.angularDamping = 0.9f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.None;
    }

    private static void ReplaceBlockingCollider(GameObject root)
    {
        BoxCollider original = root.GetComponent<BoxCollider>();
        if (original != null) UnityEngine.Object.DestroyImmediate(original, true);

        Transform oldContainer = root.transform.Find("TruckColliders");
        if (oldContainer != null) UnityEngine.Object.DestroyImmediate(oldContainer.gameObject);
        Transform container = new GameObject("TruckColliders").transform;
        container.SetParent(root.transform, false);

        // Thick, overlapping walk surfaces leave no narrow cavity for a ragdoll
        // foot to enter when the moving truck is settling on its suspension.
        AddBox(container, "Underbody", new Vector3(0f, 1.05f, -0.05f), new Vector3(4.25f, 0.7f, 8.1f));
        // Extend beneath both door thresholds and behind the old rear-left
        // corner gap while keeping one unbroken top surface.
        AddBox(container, "CabinWalkSurface", new Vector3(0f, 1.35f, 0.75f), new Vector3(4.9f, 0.56f, 2.8f));
        AddBox(container, "Hood", new Vector3(0f, 2.35f, 2.75f), new Vector3(4.1f, 1.45f, 2.45f));
        AddBox(container, "CabinBack", new Vector3(0f, 2.65f, -0.48f), new Vector3(4.05f, 2.15f, 0.24f));
        AddBox(container, "BedWalkSurface", new Vector3(0f, 1.51f, -2.5f), new Vector3(4.05f, 0.56f, 3.65f));
        AddBox(container, "BedLeft", new Vector3(-1.95f, 2.18f, -2.5f), new Vector3(0.2f, 1.15f, 3.65f));
        AddBox(container, "BedRight", new Vector3(1.95f, 2.18f, -2.5f), new Vector3(0.2f, 1.15f, 3.65f));
    }

    private static void ConfigureSteeringGrips(
        Transform steeringWheel, out Transform leftGrip, out Transform rightGrip)
    {
        leftGrip = null;
        rightGrip = null;
        if (steeringWheel == null) return;

        MeshFilter filter = steeringWheel.GetComponent<MeshFilter>();
        Bounds bounds = filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.bounds
            : new Bounds(Vector3.zero, new Vector3(0.8f, 0.8f, 0.2f));
        float side = bounds.extents.x * 0.84f;
        float height = bounds.center.y + bounds.extents.y * 0.04f;
        float depth = bounds.center.z + bounds.extents.z * 0.2f;

        leftGrip = EnsureChild(steeringWheel, "LeftHandGrip");
        rightGrip = EnsureChild(steeringWheel, "RightHandGrip");
        leftGrip.localPosition = new Vector3(bounds.center.x - side, height, depth);
        rightGrip.localPosition = new Vector3(bounds.center.x + side, height, depth);
        // Grip forward points toward the wheel centre and grip up points at
        // the driver, which gives the IK solver a natural palm orientation.
        leftGrip.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.back);
        rightGrip.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.back);
        leftGrip.localScale = Vector3.one;
        rightGrip.localScale = Vector3.one;
    }

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static WobblyVehicleDoor ConfigureSideDoor(
        GameObject root, WobblyVehicleController controller, string doorName, float openAngle)
    {
        Transform doorTransform = FindRenderableChild(root.transform, doorName);
        if (doorTransform == null) return null;

        BoxCollider collider = doorTransform.GetComponent<BoxCollider>();
        if (collider == null) collider = doorTransform.gameObject.AddComponent<BoxCollider>();
        MeshFilter mesh = doorTransform.GetComponent<MeshFilter>();
        if (mesh != null && mesh.sharedMesh != null)
        {
            collider.center = mesh.sharedMesh.bounds.center;
            collider.size = mesh.sharedMesh.bounds.size;
        }

        Renderer renderer = doorTransform.GetComponent<Renderer>();
        Vector3 hingeWorld = renderer != null
            ? new Vector3(renderer.bounds.center.x, renderer.bounds.center.y, renderer.bounds.max.z)
            : doorTransform.position + root.transform.forward;
        Vector3 hingeInParent = doorTransform.parent.InverseTransformPoint(hingeWorld);
        Vector3 axisInParent = doorTransform.parent.InverseTransformDirection(root.transform.up).normalized;
        WobblyVehicleDoor door = doorTransform.GetComponent<WobblyVehicleDoor>();
        if (door == null) door = doorTransform.gameObject.AddComponent<WobblyVehicleDoor>();
        door.Configure(controller, hingeInParent, axisInParent, openAngle, "Door");
        return door;
    }

    private static WobblyVehicleDoor ConfigureBonnet(GameObject root, WobblyVehicleController controller)
    {
        Transform panel = FindRenderableChild(root.transform, "Hood");
        if (panel == null) return null;
        Renderer renderer = panel.GetComponent<Renderer>();
        Bounds bounds = renderer != null ? renderer.bounds : new Bounds(panel.position, Vector3.one);
        Vector3 hingeWorld = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
        return ConfigurePanel(root, controller, panel, hingeWorld, -58f, "Bonnet");
    }

    private static WobblyVehicleDoor ConfigureTailgate(GameObject root, WobblyVehicleController controller)
    {
        Transform panel = FindRenderableChild(root.transform, "PickupBackDoor");
        if (panel == null) return null;
        Renderer renderer = panel.GetComponent<Renderer>();
        Bounds bounds = renderer != null ? renderer.bounds : new Bounds(panel.position, Vector3.one);
        Vector3 hingeWorld = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        return ConfigurePanel(root, controller, panel, hingeWorld, -88f, "Tailgate");
    }

    private static void RemoveStorageCover(GameObject root)
    {
        Transform cover = FindDeepChild(root.transform, "PickupAwning");
        if (cover != null) UnityEngine.Object.DestroyImmediate(cover.gameObject, true);
    }

    private static WobblyVehicleDoor ConfigurePanel(
        GameObject root, WobblyVehicleController controller, Transform panel,
        Vector3 hingeWorld, float openAngle, string displayName)
    {
        BoxCollider collider = panel.GetComponent<BoxCollider>();
        if (collider == null) collider = panel.gameObject.AddComponent<BoxCollider>();
        MeshFilter mesh = panel.GetComponent<MeshFilter>();
        if (mesh != null && mesh.sharedMesh != null)
        {
            collider.center = mesh.sharedMesh.bounds.center;
            collider.size = mesh.sharedMesh.bounds.size;
        }

        WobblyVehicleDoor movable = panel.GetComponent<WobblyVehicleDoor>();
        if (movable == null) movable = panel.gameObject.AddComponent<WobblyVehicleDoor>();
        Vector3 hingeInParent = panel.parent.InverseTransformPoint(hingeWorld);
        Vector3 axisInParent = panel.parent.InverseTransformDirection(root.transform.right).normalized;
        movable.Configure(controller, hingeInParent, axisInParent, openAngle, displayName);
        return movable;
    }

    private static void ConfigureSeats(
        GameObject root, WobblyVehicleController controller,
        WobblyVehicleDoor leftDoor, WobblyVehicleDoor rightDoor)
    {
        Transform old = root.transform.Find("VehicleInteractions");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        Transform container = new GameObject("VehicleInteractions").transform;
        container.SetParent(root.transform, false);
        // Bring the driver close enough for both arms to reach the real wheel
        // surface without stretching hand or forearm bones.
        CreateSeat(container, controller, leftDoor, "DriverSeat", new Vector3(-1.03f, 2.55f, 0.62f), true);
        CreateSeat(container, controller, rightDoor, "PassengerSeat", new Vector3(1.03f, 2.55f, 0.45f), false);
    }

    private static void CreateSeat(
        Transform parent, WobblyVehicleController controller, WobblyVehicleDoor door,
        string name, Vector3 localPosition, bool driver)
    {
        Transform seat = new GameObject(name).transform;
        seat.SetParent(parent, false);
        seat.localPosition = localPosition;
        seat.localRotation = Quaternion.identity;

        Transform interaction = new GameObject("InteractionPoint").transform;
        interaction.SetParent(seat, false);
        interaction.localPosition = new Vector3(0f, 0.15f, -0.05f);
        Transform stand = new GameObject("StandPoint").transform;
        stand.SetParent(seat, false);
        // Keep the released ragdoll over the continuous cabin floor and well
        // clear of the solid rear wall. The old rearward point overlapped that
        // wall as soon as collisions were restored.
        // Direct get-out placement is outside the open cab door, so no walking
        // through the truck interior is needed after leaving a seat.
        stand.localPosition = new Vector3(driver ? -1.65f : 1.65f, -1.05f, 0f);

        WobblyVehicleSeat vehicleSeat = seat.gameObject.AddComponent<WobblyVehicleSeat>();
        vehicleSeat.Configure(controller, door, seat, stand, interaction, driver);
    }

    private static void ConfigureWheel(WheelCollider wheel)
    {
        if (wheel == null) return;
        wheel.suspensionDistance = 0.36f;
        JointSpring spring = wheel.suspensionSpring;
        spring.spring = 36000f;
        spring.damper = 4800f;
        spring.targetPosition = 0.5f;
        wheel.suspensionSpring = spring;
        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.stiffness = 1.35f;
        wheel.forwardFriction = forward;
        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.stiffness = 1.45f;
        wheel.sidewaysFriction = sideways;
    }

    private static void PlaceNearPlayer(GameObject truck)
    {
        WobblyActiveRagdoll player = UnityEngine.Object.FindFirstObjectByType<WobblyActiveRagdoll>();
        Vector3 origin = player != null && player.Pelvis != null ? player.Pelvis.position : Vector3.zero;
        Vector3 candidate = origin + Vector3.right * 8f + Vector3.forward * 7f;
        float surface = FindSurfaceHeight(candidate, 0f, truck.transform);
        truck.transform.SetPositionAndRotation(new Vector3(candidate.x, surface, candidate.z), Quaternion.identity);
        Physics.SyncTransforms();
        Renderer[] renderers = truck.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = CalculateBounds(renderers);
            truck.transform.position += Vector3.up * (surface + 0.04f - bounds.min.y);
        }
    }

    private static float FindSurfaceHeight(Vector3 position, float fallback, Transform ignoredRoot)
    {
        Physics.SyncTransforms();
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 100f, Vector3.down, 200f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float result = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || candidate.transform.IsChildOf(ignoredRoot)) continue;
            if (candidate.GetComponentInParent<WobblyActiveRagdoll>() != null) continue;
            result = Mathf.Max(result, hits[i].point.y);
        }
        return float.IsNegativeInfinity(result) ? fallback : result;
    }

    private static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        BoxCollider box = part.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    private static T FindComponent<T>(Transform root, string name) where T : Component
    {
        Transform child = FindDeepChild(root, name);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase)) return all[i];
        return null;
    }

    private static Transform FindRenderableChild(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (!string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (all[i].GetComponent<Renderer>() != null || all[i].GetComponent<MeshFilter>() != null) return all[i];
        }
        return null;
    }

    private static GameObject FindSceneObject(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindDeepChild(root.transform, name);
            if (found != null && found.gameObject.scene == scene) return found.gameObject;
        }
        return null;
    }

    private static Bounds CalculateBounds(IReadOnlyList<Renderer> renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    private static void TryAutomaticSetup()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (SceneManager.GetActiveScene().path != ScenePath) return;
        SetupDriveablePickup();
    }
}
#endif
