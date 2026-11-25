#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class GameMenuSetup
{
    private const string GameScenePath = "Assets/Scenes/game.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/WobblyPlayer.prefab";
    private const string ModelPath = "Assets/meshes/robber mangl.fbx";

    static GameMenuSetup()
    {
        EditorApplication.delayCall += TryAutomaticSetup;
    }

    [MenuItem("Tools/Game Setup/Connect Existing Game Menu")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath) return;

        EnsureMaskInPlayerPrefab();
        Canvas canvas = FindNamed<Canvas>("Canvas");
        if (canvas == null)
        {
            Debug.LogError("The existing Canvas was not found. No replacement Canvas was created.");
            return;
        }

        canvas.gameObject.SetActive(true);
        Transform mainMenu = FindDescendant(canvas.transform, "main menu");
        Transform customization = FindDescendant(canvas.transform, "customize menu");
        if (mainMenu == null || customization == null)
        {
            Debug.LogError("The existing 'main menu' or 'customize menu' panel was not found.");
            return;
        }

        Button template = FindDescendant(mainMenu, "play button")?.GetComponent<Button>();
        TMP_Text textTemplate = FindDirectText(mainMenu);
        EnsureCustomizationUi(customization, template, textTemplate);
        mainMenu.gameObject.SetActive(true);
        customization.gameObject.SetActive(false);

        MenuManager menuManager = canvas.GetComponent<MenuManager>();
        if (menuManager == null) menuManager = canvas.gameObject.AddComponent<MenuManager>();
        EnsureEventSystem(scene);

        PlayerController player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null && player.GetComponent<CharacterAppearance>() == null)
            player.gameObject.AddComponent<CharacterAppearance>();
        AssignMenuManagerReferences(menuManager, canvas, mainMenu, customization, player);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(GameScenePath, true)
        };
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath);
        EditorUtility.SetDirty(canvas.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Existing game-scene menu connected. The real truck/player and the user's Canvas buttons are used.");
    }

    private static void AssignMenuManagerReferences(MenuManager manager, Canvas canvas,
        Transform mainMenu, Transform customization, PlayerController player)
    {
        if (manager == null) return;
        SerializedObject serialized = new SerializedObject(manager);
        SetReference(serialized, "menuCanvas", canvas);
        SetReference(serialized, "mainMenuPanel", mainMenu != null ? mainMenu.gameObject : null);
        SetReference(serialized, "customizationPanel", customization != null ? customization.gameObject : null);
        SetReference(serialized, "playButton", FindDescendant(mainMenu, "play button")?.GetComponent<Button>());
        SetReference(serialized, "customizeButton", FindDescendant(mainMenu, "character customize button")?.GetComponent<Button>());
        SetReference(serialized, "settingsButton", FindDescendant(mainMenu, "setting button")?.GetComponent<Button>());
        SetReference(serialized, "exitButton", FindDescendant(mainMenu, "exit button")?.GetComponent<Button>());
        SetReference(serialized, "categoryLabel", FindDescendant(customization, "category label")?.GetComponent<TMP_Text>());
        SetReference(serialized, "optionLabel", FindDescendant(customization, "option label")?.GetComponent<TMP_Text>());
        SetReference(serialized, "categoryPreviousButton", FindDescendant(customization, "category previous button")?.GetComponent<Button>());
        SetReference(serialized, "categoryNextButton", FindDescendant(customization, "category next button")?.GetComponent<Button>());
        SetReference(serialized, "optionPreviousButton", FindDescendant(customization, "option previous button")?.GetComponent<Button>());
        SetReference(serialized, "optionNextButton", FindDescendant(customization, "option next button")?.GetComponent<Button>());
        SetReference(serialized, "backButton", FindDescendant(customization, "back button")?.GetComponent<Button>());
        SetReference(serialized, "player", player);
        SetReference(serialized, "truck", Object.FindFirstObjectByType<VehicleController>(FindObjectsInactive.Include));
        SetReference(serialized, "sceneCamera", Camera.main != null
            ? Camera.main : Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    private static void SetReference(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null && value != null) property.objectReferenceValue = value;
    }

    private static void TryAutomaticSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (SceneManager.GetActiveScene().path != GameScenePath) return;
        Canvas canvas = FindNamed<Canvas>("Canvas");
        // Connecting an existing menu is a one-time operation. Code reloads
        // must preserve the designer's layout and current panel selection.
        if (canvas != null && canvas.GetComponent<MenuManager>() == null) Setup();
    }

    private static void EnsureCustomizationUi(Transform panel, Button buttonTemplate, TMP_Text textTemplate)
    {
        EnsureText(panel, "Customization Title", "CHARACTER CUSTOMIZATION", textTemplate,
            new Vector2(0.05f, 0.82f), new Vector2(0.43f, 0.93f), 42);
        EnsureText(panel, "Category Label", "HAT", textTemplate,
            new Vector2(0.13f, 0.64f), new Vector2(0.35f, 0.72f), 30);
        EnsureText(panel, "Option Label", "NONE", textTemplate,
            new Vector2(0.13f, 0.49f), new Vector2(0.35f, 0.59f), 28);

        EnsureButton(panel, "Category Previous Button", "<", buttonTemplate,
            new Vector2(0.05f, 0.64f), new Vector2(0.11f, 0.72f));
        EnsureButton(panel, "Category Next Button", ">", buttonTemplate,
            new Vector2(0.37f, 0.64f), new Vector2(0.43f, 0.72f));
        EnsureButton(panel, "Option Previous Button", "<", buttonTemplate,
            new Vector2(0.05f, 0.49f), new Vector2(0.11f, 0.59f));
        EnsureButton(panel, "Option Next Button", ">", buttonTemplate,
            new Vector2(0.37f, 0.49f), new Vector2(0.43f, 0.59f));
        EnsureButton(panel, "Back Button", "BACK", buttonTemplate,
            new Vector2(0.05f, 0.12f), new Vector2(0.43f, 0.21f));
    }

    [MenuItem("Tools/Game Setup/Repair Customization Layout")]
    public static void RepairCustomizationLayout()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Canvas canvas = FindNamed<Canvas>("Canvas");
        Transform panel = canvas != null ? FindDescendant(canvas.transform, "customize menu") : null;
        if (panel == null) { Debug.LogError("The existing customization panel was not found."); return; }
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Repair customization layout");
        // Repair the existing controls in place. A bounded left card leaves the
        // right side clear for the real scene character, at every aspect ratio.
        SetRect(panel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        RectTransform card = FindDescendant(panel, "Customization Controls") as RectTransform;
        if (card == null)
        {
            var go = new GameObject("Customization Controls", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            Undo.RegisterCreatedObjectUndo(go, "Create customization card");
            go.transform.SetParent(panel, false);
            card = go.GetComponent<RectTransform>();
        }
        SetRect(card, new Vector2(.035f, .1f), new Vector2(.39f, .9f));
        var background = card.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(.065f, .08f, .105f, .96f);
        background.raycastTarget = false;
        MoveControl(panel, card, "Customization Title", new Vector2(.08f,.8f), new Vector2(.92f,.95f), 34);
        MoveControl(panel, card, "Category Label", new Vector2(.24f,.61f), new Vector2(.76f,.72f), 30);
        MoveControl(panel, card, "Category Previous Button", new Vector2(.08f,.61f), new Vector2(.21f,.72f), 30);
        MoveControl(panel, card, "Category Next Button", new Vector2(.79f,.61f), new Vector2(.92f,.72f), 30);
        MoveControl(panel, card, "Option Label", new Vector2(.24f,.39f), new Vector2(.76f,.53f), 26);
        MoveControl(panel, card, "Option Previous Button", new Vector2(.08f,.4f), new Vector2(.21f,.52f), 30);
        MoveControl(panel, card, "Option Next Button", new Vector2(.79f,.4f), new Vector2(.92f,.52f), 30);
        MoveControl(panel, card, "Back Button", new Vector2(.08f,.07f), new Vector2(.92f,.19f), 25);
        // Only transparent panel background is changed; the user's other menu
        // objects and button callbacks remain intact.
        var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
        if (panelImage != null) { panelImage.color = Color.clear; panelImage.raycastTarget = false; }
        Canvas.ForceUpdateCanvases();
        EditorUtility.SetDirty(panel.gameObject);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveScene(panel.gameObject.scene);
        Debug.Log("Customization layout repaired in place; future code reloads preserve it.");
    }

    private static void MoveControl(Transform searchRoot, RectTransform card, string name,
        Vector2 min, Vector2 max, int fontSize)
    {
        Transform node = FindDescendant(searchRoot, name);
        if (node == null) return;
        var rect = node as RectTransform;
        if (rect == null) return;
        rect.SetParent(card, false);
        var layout = rect.GetComponent<UnityEngine.UI.LayoutElement>();
        if (layout != null) layout.ignoreLayout = true;
        SetRect(rect, min, max);
        rect.localRotation = Quaternion.identity;
        var button = rect.GetComponent<UnityEngine.UI.Button>();
        if (button != null && button.targetGraphic != null)
        { button.targetGraphic.color = new Color(.32f,.24f,.15f,1f); button.targetGraphic.raycastTarget = true; }
        foreach (TMP_Text label in rect.GetComponentsInChildren<TMP_Text>(true))
        {
            label.raycastTarget = false;
            label.color = new Color(.96f,.94f,.88f);
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = fontSize;
            if (label.transform != rect) SetRect(label.rectTransform, new Vector2(.06f,.08f), new Vector2(.94f,.92f));
        }
    }

    private static TMP_Text EnsureText(Transform parent, string name, string value, TMP_Text template,
        Vector2 anchorMin, Vector2 anchorMax, int fontSize)
    {
        Transform existing = FindDescendant(parent, name);
        TMP_Text text;
        if (existing != null)
        {
            text = existing.GetComponent<TMP_Text>();
        }
        else if (template != null)
        {
            GameObject clone = Object.Instantiate(template.gameObject, parent);
            clone.name = name;
            text = clone.GetComponent<TMP_Text>();
        }
        else
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            item.transform.SetParent(parent, false);
            text = item.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
        }

        if (text == null) return null;
        if (existing != null) return text;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        SetRect(text.rectTransform, anchorMin, anchorMax);
        return text;
    }

    private static Button EnsureButton(Transform parent, string name, string label, Button template,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform existing = FindDescendant(parent, name);
        Button button;
        if (existing != null)
        {
            button = existing.GetComponent<Button>();
        }
        else if (template != null)
        {
            GameObject clone = Object.Instantiate(template.gameObject, parent);
            clone.name = name;
            button = clone.GetComponent<Button>();
        }
        else
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            item.transform.SetParent(parent, false);
            button = item.GetComponent<Button>();
            button.targetGraphic = item.GetComponent<Image>();
        }

        if (button == null) return null;
        if (existing != null) return button;
        button.onClick.RemoveAllListeners();
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;
        SetRect(button.GetComponent<RectTransform>(), anchorMin, anchorMax);
        return button;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void EnsureEventSystem(Scene scene)
    {
        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (eventSystem == null)
        {
            GameObject item = new GameObject("EventSystem", typeof(EventSystem));
            SceneManager.MoveGameObjectToScene(item, scene);
            eventSystem = item.GetComponent<EventSystem>();
        }
        BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
        for (int i = 0; i < modules.Length; i++)
            if (!(modules[i] is InputSystemUIInputModule)) Object.DestroyImmediate(modules[i]);
        InputSystemUIInputModule input = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (input == null)
        {
            input = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            input.AssignDefaultActions();
        }
    }

    private static void EnsureMaskInPlayerPrefab()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        GameObject sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        try
        {
            Transform visual = FindDescendant(prefabRoot.transform, "RobberVisualRig");
            Transform sourceMask = sourceModel != null ? FindDescendant(sourceModel.transform, "mask") : null;
            if (visual == null || sourceMask == null || FindDescendant(visual, "mask") != null) return;

            SkinnedMeshRenderer sourceRenderer = sourceMask.GetComponent<SkinnedMeshRenderer>();
            if (sourceRenderer == null) return;
            GameObject maskObject = new GameObject("mask");
            maskObject.layer = visual.gameObject.layer;
            maskObject.transform.SetParent(visual, false);
            maskObject.transform.localPosition = sourceMask.localPosition;
            maskObject.transform.localRotation = sourceMask.localRotation;
            maskObject.transform.localScale = sourceMask.localScale;
            SkinnedMeshRenderer target = maskObject.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = sourceRenderer.sharedMesh;
            target.sharedMaterials = sourceRenderer.sharedMaterials;
            target.localBounds = sourceRenderer.localBounds;
            target.updateWhenOffscreen = sourceRenderer.updateWhenOffscreen;
            target.quality = sourceRenderer.quality;

            Dictionary<string, Transform> targetBones = new Dictionary<string, Transform>();
            Transform[] transforms = visual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (!targetBones.ContainsKey(transforms[i].name)) targetBones.Add(transforms[i].name, transforms[i]);
            Transform[] bones = new Transform[sourceRenderer.bones.Length];
            for (int i = 0; i < bones.Length; i++)
                if (sourceRenderer.bones[i] != null)
                    targetBones.TryGetValue(sourceRenderer.bones[i].name, out bones[i]);
            target.bones = bones;
            Transform rootBone = null;
            if (sourceRenderer.rootBone != null)
                targetBones.TryGetValue(sourceRenderer.rootBone.name, out rootBone);
            target.rootBone = rootBone;
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
            Debug.Log("Added the imported mask mesh to the real player prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static T FindNamed<T>(string name) where T : Component
    {
        T[] items = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].name != name || items[i].gameObject.scene != SceneManager.GetActiveScene()) continue;
            if (typeof(T) == typeof(Canvas) && FindDescendant(items[i].transform, "customize menu") == null) continue;
            return items[i];
        }
        return null;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root == null) return null;
        Transform[] items = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < items.Length; i++)
            if (string.Equals(items[i].name, name, System.StringComparison.OrdinalIgnoreCase)) return items[i];
        return null;
    }

    private static TMP_Text FindDirectText(Transform parent)
    {
        TMP_Text[] texts = parent.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++) if (texts[i].transform.parent == parent) return texts[i];
        return texts.Length > 0 ? texts[0] : null;
    }
}
#endif
