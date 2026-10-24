#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class WobblyCharacterSetup
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string ModelPath = "Assets/meshes/robber mangl.fbx";
    private const string PickupRootName = "PickupItems";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/WobblyPlayer.prefab";
    private const string ThirdPartyRoot = "Assets/Third Party/";
    private const string RevolverBulletPath = ThirdPartyRoot + "RyanNein/MidPolyGunWeapons/Revolver/RevolverBullet.fbx";
    private const string RifleBulletPath = ThirdPartyRoot + "RyanNein/MidPolyGunWeapons/Rifle/RifleBullet.fbx";

    private static readonly WeaponPlacement[] FloorWeapons =
    {
        new WeaponPlacement("FloorPickup_DeadWest_Revovler", ThirdPartyRoot + "Dead West - Animated Western Weapons/Prefabs/Revovler.prefab", -1.35f, 1.25f, -12f, false),
        new WeaponPlacement("FloorPickup_DeadWest_Rifle1", ThirdPartyRoot + "Dead West - Animated Western Weapons/Prefabs/Rifle 1.prefab", -0.45f, 1.25f, -4f, true),
        new WeaponPlacement("FloorPickup_DeadWest_Rifle", ThirdPartyRoot + "Dead West - Animated Western Weapons/Prefabs/Rifle.prefab", 0.45f, 1.25f, 4f, true),
        new WeaponPlacement("FloorPickup_DeadWest_ShotGun", ThirdPartyRoot + "Dead West - Animated Western Weapons/Prefabs/ShotGun.prefab", 1.35f, 1.25f, 12f, true),
        new WeaponPlacement("FloorPickup_DeadWest_Sniper", ThirdPartyRoot + "Dead West - Animated Western Weapons/Prefabs/Sniper.prefab", -1.35f, 2.35f, -12f, true),
        new WeaponPlacement("FloorPickup_RyanNein_Revolver", ThirdPartyRoot + "RyanNein/MidPolyGunWeapons/Revolver/Revolver.prefab", -0.45f, 2.35f, -4f, false),
        new WeaponPlacement("FloorPickup_RyanNein_Rifle", ThirdPartyRoot + "RyanNein/MidPolyGunWeapons/Rifle/Rifle.prefab", 0.45f, 2.35f, 4f, true),
        new WeaponPlacement("FloorPickup_RyanNein_Shotgun", ThirdPartyRoot + "RyanNein/MidPolyGunWeapons/Shotgun/Shotgun.prefab", 1.35f, 2.35f, 12f, false)
    };

    static WobblyCharacterSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += TryAutomaticSetup;
    }

    [MenuItem("Tools/Wobbly Character/Setup Gameplay Scene")]
    public static void SetupGameplayScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        WobblyActiveRagdoll existing = Object.FindFirstObjectByType<WobblyActiveRagdoll>();
        if (existing == null)
        {
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelPrefab == null)
            {
                Debug.LogError("Wobbly setup could not load " + ModelPath);
                return;
            }

            GameObject player = new GameObject("WobblyPlayer");
            Undo.RegisterCreatedObjectUndo(player, "Create Wobbly Player");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, scene);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "RobberVisualRig";
            model.transform.SetParent(player.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = CalculateBounds(renderers);
            if (bounds.size.y > 0.001f)
            {
                float scale = 1.8f / bounds.size.y;
                model.transform.localScale = Vector3.one * scale;
                bounds = CalculateBounds(renderers);
                model.transform.position += Vector3.up * (0.04f - bounds.min.y);
            }

            existing = player.AddComponent<WobblyActiveRagdoll>();
            player.transform.position = Vector3.zero;
        }

        // Re-run this after every script reload so old oversized colliders are
        // resized using the character's current world scale and saved to scene.
        existing.BuildRagdoll();

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        WobblyCameraController cameraController = mainCamera.GetComponent<WobblyCameraController>();
        if (cameraController == null) cameraController = mainCamera.gameObject.AddComponent<WobblyCameraController>();
        cameraController.Target = existing;

        WobblyWeaponController weaponController = existing.GetComponent<WobblyWeaponController>();
        if (weaponController == null) weaponController = Undo.AddComponent<WobblyWeaponController>(existing.gameObject);
        EnsurePlayerPrefab(existing);
        EnsureFloorWeapons(scene, existing.transform.position);
        WobblyPickupWeapon.PrepareSceneWeapons();
        EditorUtility.SetDirty(cameraController);
        EditorUtility.SetDirty(weaponController);
        EditorUtility.SetDirty(existing);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = existing.gameObject;
        Debug.Log("Wobbly setup complete. E picks up, left mouse shoots, right mouse aims, R reloads, G drops, Shift runs, Space jumps, and C switches camera mode.");
    }

    private static void EnsurePlayerPrefab(WobblyActiveRagdoll player)
    {
        if (player == null || player.gameObject.scene != SceneManager.GetActiveScene()) return;
        EnsureAssetFolder("Assets/Prefabs");
        EnsureAssetFolder("Assets/Prefabs/Player");

        GameObject root = player.gameObject;
        string currentPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
        if (currentPath == PlayerPrefabPath) return;
        if (!string.IsNullOrEmpty(currentPath))
        {
            Debug.LogWarning("WobblyPlayer is already connected to a different prefab: " + currentPath);
            return;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            root, PlayerPrefabPath, InteractionMode.AutomatedAction, out bool success);
        if (!success || prefab == null)
            Debug.LogError("Could not create the player prefab at " + PlayerPrefabPath);
        else
            Debug.Log("Created and connected player prefab: " + PlayerPrefabPath);
    }

    private static void EnsureAssetFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        string folderName = path.Substring(separator + 1);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void EnsureFloorWeapons(Scene scene, Vector3 playerPosition)
    {
        GameObject revolverBullet = AssetDatabase.LoadAssetAtPath<GameObject>(RevolverBulletPath);
        GameObject rifleBullet = AssetDatabase.LoadAssetAtPath<GameObject>(RifleBulletPath);
        GameObject pickupRoot = FindSceneObject(scene, PickupRootName);
        if (pickupRoot == null)
        {
            pickupRoot = new GameObject(PickupRootName);
            SceneManager.MoveGameObjectToScene(pickupRoot, scene);
            Undo.RegisterCreatedObjectUndo(pickupRoot, "Create pickup container");
        }

        int createdCount = 0;
        for (int i = 0; i < FloorWeapons.Length; i++)
        {
            WeaponPlacement placement = FloorWeapons[i];
            GameObject instance = FindSceneObject(scene, placement.Name);
            if (instance == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(placement.PrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("Could not place missing weapon prefab: " + placement.PrefabPath);
                    continue;
                }

                instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null) continue;

                Undo.RegisterCreatedObjectUndo(instance, "Place pickup weapon");
                instance.name = placement.Name;
                instance.transform.SetParent(pickupRoot.transform, true);
                instance.transform.position = playerPosition + new Vector3(placement.X, 1f, placement.Z);
                instance.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);
                NormalizeWeaponSize(instance, placement.TwoHanded ? 1.05f : 0.46f);
                LayDownIfVertical(instance, placement.Yaw);
                Physics.SyncTransforms();
                RestOnSurface(instance, FindSurfaceHeight(instance.transform.position, playerPosition.y));
                createdCount++;
            }

            else
            {
                RaiseAboveSurfaceIfEmbedded(instance,
                    FindSurfaceHeight(instance.transform.position, playerPosition.y));
            }

            WobblyPickupWeapon pickup = instance.GetComponent<WobblyPickupWeapon>();
            if (pickup == null) pickup = Undo.AddComponent<WobblyPickupWeapon>(instance);
            pickup.SetGripMode(placement.TwoHanded
                ? WobblyPickupWeapon.GripMode.TwoHanded
                : WobblyPickupWeapon.GripMode.OneHanded);
            WobblyPickupWeapon.FirearmClass firearmClass = FirearmClassFor(placement.Name);
            pickup.ConfigureFirearm(firearmClass == WobblyPickupWeapon.FirearmClass.Revolver
                ? revolverBullet : rifleBullet, firearmClass);
            pickup.ConfigureForFloor();
            EditorUtility.SetDirty(instance);
            EditorUtility.SetDirty(pickup);
        }

        EditorUtility.SetDirty(pickupRoot);
        if (createdCount > 0)
            Debug.Log("Placed " + createdCount + " pickup guns near the player.");
    }

    private static WobblyPickupWeapon.FirearmClass FirearmClassFor(string weaponName)
    {
        string lower = weaponName.ToLowerInvariant();
        if (lower.Contains("shot")) return WobblyPickupWeapon.FirearmClass.Shotgun;
        if (lower.Contains("sniper")) return WobblyPickupWeapon.FirearmClass.Sniper;
        if (lower.Contains("rifle")) return WobblyPickupWeapon.FirearmClass.Rifle;
        return WobblyPickupWeapon.FirearmClass.Revolver;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i].name == objectName) return transforms[i].gameObject;
        }
        return null;
    }

    private static void NormalizeWeaponSize(GameObject weapon, float targetLength)
    {
        Renderer[] renderers = weapon.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = CalculateBounds(renderers);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest <= 0.0001f) return;

        float scaleFactor = targetLength / longest;
        weapon.transform.localScale *= scaleFactor;
    }

    private static void LayDownIfVertical(GameObject weapon, float yaw)
    {
        Renderer[] renderers = weapon.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = CalculateBounds(renderers);
        if (bounds.size.y > Mathf.Max(bounds.size.x, bounds.size.z) * 0.9f)
            weapon.transform.rotation = Quaternion.Euler(0f, yaw, 90f);
    }

    private static float FindSurfaceHeight(Vector3 position, float fallbackHeight)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 25f, Vector3.down, 50f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bestHeight = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider.GetComponentInParent<WobblyActiveRagdoll>() != null ||
                hitCollider.GetComponentInParent<WobblyPickupWeapon>() != null)
                continue;
            if (hits[i].point.y > bestHeight) bestHeight = hits[i].point.y;
        }
        return float.IsNegativeInfinity(bestHeight) ? fallbackHeight : bestHeight;
    }

    private static void RestOnSurface(GameObject weapon, float surfaceHeight)
    {
        Renderer[] renderers = weapon.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            weapon.transform.position = new Vector3(weapon.transform.position.x, surfaceHeight + 0.1f,
                weapon.transform.position.z);
            return;
        }

        Bounds bounds = CalculateBounds(renderers);
        weapon.transform.position += Vector3.up * (surfaceHeight + 0.035f - bounds.min.y);
    }

    private static void RaiseAboveSurfaceIfEmbedded(GameObject weapon, float surfaceHeight)
    {
        Renderer[] renderers = weapon.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = CalculateBounds(renderers);
        if (bounds.min.y < surfaceHeight + 0.02f)
            weapon.transform.position += Vector3.up * (surfaceHeight + 0.035f - bounds.min.y);
    }

    private static void TryAutomaticSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) return;
        SetupGameplayScene();
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private readonly struct WeaponPlacement
    {
        public readonly string Name;
        public readonly string PrefabPath;
        public readonly float X;
        public readonly float Z;
        public readonly float Yaw;
        public readonly bool TwoHanded;

        public WeaponPlacement(string name, string prefabPath, float x, float z, float yaw, bool twoHanded)
        {
            Name = name;
            PrefabPath = prefabPath;
            X = x;
            Z = z;
            Yaw = yaw;
            TwoHanded = twoHanded;
        }
    }
}
#endif
