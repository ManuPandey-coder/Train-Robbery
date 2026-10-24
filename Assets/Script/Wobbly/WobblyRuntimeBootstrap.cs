using UnityEngine;
using UnityEngine.SceneManagement;

public static class WobblyRuntimeBootstrap
{
    private const string GameplaySceneName = "game";
    private const string ResourcePath = "Wobbly/robber mangl";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePlayerIfNeeded()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, GameplaySceneName, System.StringComparison.OrdinalIgnoreCase)) return;

        WobblyActiveRagdoll controller = Object.FindFirstObjectByType<WobblyActiveRagdoll>();
        if (controller == null)
        {
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError("Could not load the robber model from Resources/" + ResourcePath);
                return;
            }

            GameObject player = new GameObject("WobblyPlayer");
            GameObject model = Object.Instantiate(prefab, player.transform);
            model.name = "RobberVisualRig";
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

            controller = player.AddComponent<WobblyActiveRagdoll>();
            player.transform.position = Vector3.zero;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        WobblyCameraController cameraController = camera.GetComponent<WobblyCameraController>();
        if (cameraController == null) cameraController = camera.gameObject.AddComponent<WobblyCameraController>();
        cameraController.Target = controller;

        WobblyWeaponController weaponController = controller.GetComponent<WobblyWeaponController>();
        if (weaponController == null) controller.gameObject.AddComponent<WobblyWeaponController>();
        WobblyPickupWeapon.PrepareSceneWeapons();
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
