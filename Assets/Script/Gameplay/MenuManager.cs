using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
[AddComponentMenu("Game/Menu Manager")]
public sealed class MenuManager : MonoBehaviour
{
    public static bool BlocksGameplay { get; private set; }

    [Header("Startup")]
    [SerializeField] private bool openMainMenuAtStart = true;
    [SerializeField] private bool pauseWhileMenuIsOpen = true;

    [Header("Menu Panels")]
    [SerializeField] private Canvas menuCanvas;
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject customizationPanel;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button customizeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;

    [Header("Customization Controls")]
    [SerializeField] private TMP_Text categoryLabel;
    [SerializeField] private TMP_Text optionLabel;
    [SerializeField] private Button categoryPreviousButton;
    [SerializeField] private Button categoryNextButton;
    [SerializeField] private Button optionPreviousButton;
    [SerializeField] private Button optionNextButton;
    [SerializeField] private Button backButton;

    [Header("Scene Preview")]
    [SerializeField] private PlayerController player;
    [SerializeField] private VehicleController truck;
    [SerializeField] private Camera sceneCamera;
    [SerializeField, Range(0.5f, 3f)] private float truckViewDistance = 1.15f;
    [SerializeField, Range(0.5f, 3f)] private float characterViewDistance = 1.85f;

    private TMP_Text settingsLabel;
    private TMP_Text playLabel;
    private string playText;
    private string settingsText;
    private CharacterAppearance appearance;
    private PlayerCamera gameplayCamera;
    private CharacterAppearance.Category category;
    private bool gameplayStarted;
    private bool customizing;

    public static void Ensure(PlayerController scenePlayer)
    {
        MenuManager existing = FindFirstObjectByType<MenuManager>();
        if (existing != null) return;
        GameObject root = new GameObject("Scene Menu Controller");
        MenuManager controller = root.AddComponent<MenuManager>();
        controller.player = scenePlayer;
    }

    private void Awake()
    {
        ResolveSceneObjects();
        BindButtons();
        if (openMainMenuAtStart) ShowMainMenu();
        else StartOrResumeGame();
    }

    private void Update()
    {
        if (BlocksGameplay)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (customizing) ShowMainMenu();
        else if (gameplayStarted && BlocksGameplay) StartOrResumeGame();
        else if (gameplayStarted) ShowMainMenu();
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
        BlocksGameplay = false;
    }

    private void ResolveSceneObjects()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (menuCanvas == null)
        {
            for (int i = 0; i < canvases.Length; i++)
                if (canvases[i].name == "Canvas") { menuCanvas = canvases[i]; break; }
            if (menuCanvas == null && canvases.Length > 0) menuCanvas = canvases[0];
        }
        if (menuCanvas != null) menuCanvas.gameObject.SetActive(true);

        if (player == null) player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (truck == null) truck = FindFirstObjectByType<VehicleController>(FindObjectsInactive.Include);
        appearance = player != null ? player.GetComponent<CharacterAppearance>() : null;
        if (sceneCamera == null) sceneCamera = Camera.main;
        gameplayCamera = sceneCamera != null ? sceneCamera.GetComponent<PlayerCamera>() : null;

        if (menuCanvas != null)
        {
            Transform[] nodes = menuCanvas.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                string nodeName = nodes[i].name.ToLowerInvariant();
                if (nodeName == "main menu" && mainMenuPanel == null) mainMenuPanel = nodes[i].gameObject;
                else if (nodeName == "customize menu" && customizationPanel == null) customizationPanel = nodes[i].gameObject;
                else if (nodeName == "category label" && categoryLabel == null) categoryLabel = nodes[i].GetComponent<TMP_Text>();
                else if (nodeName == "option label" && optionLabel == null) optionLabel = nodes[i].GetComponent<TMP_Text>();
            }
            Button[] buttons = menuCanvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                string name = buttons[i].name.ToLowerInvariant();
                if (name == "play button" && playButton == null) playButton = buttons[i];
                else if (name == "character customize button" && customizeButton == null) customizeButton = buttons[i];
                else if (name == "setting button" && settingsButton == null) settingsButton = buttons[i];
                else if (name == "exit button" && exitButton == null) exitButton = buttons[i];
                else if (name == "category previous button" && categoryPreviousButton == null) categoryPreviousButton = buttons[i];
                else if (name == "category next button" && categoryNextButton == null) categoryNextButton = buttons[i];
                else if (name == "option previous button" && optionPreviousButton == null) optionPreviousButton = buttons[i];
                else if (name == "option next button" && optionNextButton == null) optionNextButton = buttons[i];
                else if (name == "back button" && backButton == null) backButton = buttons[i];
            }

            // The menu labels are separate siblings placed over their buttons.
            // They must not consume pointer clicks intended for the button below.
            TMP_Text[] labels = menuCanvas.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
                labels[i].raycastTarget = false;
        }

        playLabel = Label(playButton);
        settingsLabel = Label(settingsButton);
        playText = TextOf(playLabel, "PLAY");
        settingsText = TextOf(settingsLabel, "SETTINGS");
        EnsureEventSystem();
    }

    private void BindButtons()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayButton);
            playButton.onClick.AddListener(OnPlayButton);
        }
        if (customizeButton != null)
        {
            customizeButton.onClick.RemoveListener(OnCustomizeButton);
            customizeButton.onClick.AddListener(OnCustomizeButton);
        }
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OnSettingsButton);
            settingsButton.onClick.AddListener(OnSettingsButton);
        }
        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(OnExitButton);
            exitButton.onClick.AddListener(OnExitButton);
        }
        if (categoryPreviousButton != null) categoryPreviousButton.onClick.AddListener(() => ChangeCategory(-1));
        if (categoryNextButton != null) categoryNextButton.onClick.AddListener(() => ChangeCategory(1));
        if (optionPreviousButton != null) optionPreviousButton.onClick.AddListener(() => ChangeOption(-1));
        if (optionNextButton != null) optionNextButton.onClick.AddListener(() => ChangeOption(1));
        if (backButton != null) backButton.onClick.AddListener(ShowMainMenu);
    }

    private void OnPlayButton()
    {
        StartOrResumeGame();
    }

    private void OnCustomizeButton()
    {
        ShowCustomization();
    }

    private void OnSettingsButton()
    {
        string[] names = QualitySettings.names;
        if (names.Length == 0) return;
        int next = (QualitySettings.GetQualityLevel() + 1) % names.Length;
        QualitySettings.SetQualityLevel(next, true);
        if (settingsLabel != null) settingsLabel.text = "QUALITY: " + names[next].ToUpperInvariant();
    }

    private void OnExitButton()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void StartOrResumeGame()
    {
        gameplayStarted = true;
        customizing = false;
        BlocksGameplay = false;
        Time.timeScale = 1f;
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (customizationPanel != null) customizationPanel.SetActive(false);
        if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);
        if (gameplayCamera != null)
        {
            gameplayCamera.enabled = true;
            if (player != null) gameplayCamera.Target = player;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ShowMainMenu()
    {
        customizing = false;
        BlocksGameplay = true;
        Time.timeScale = pauseWhileMenuIsOpen ? 0f : 1f;
        if (menuCanvas != null) menuCanvas.gameObject.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (customizationPanel != null) customizationPanel.SetActive(false);
        if (gameplayCamera != null) gameplayCamera.enabled = false;
        if (playLabel != null) playLabel.text = gameplayStarted ? "RESUME" : playText;
        if (settingsLabel != null) settingsLabel.text = settingsText;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Focus(truck != null ? truck.gameObject : null, truckViewDistance);
        if (EventSystem.current != null && playButton != null)
            EventSystem.current.SetSelectedGameObject(playButton.gameObject);
    }

    private void ShowCustomization()
    {
        customizing = true;
        BlocksGameplay = true;
        Time.timeScale = pauseWhileMenuIsOpen ? 0f : 1f;
        if (menuCanvas != null) menuCanvas.gameObject.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (customizationPanel != null) customizationPanel.SetActive(true);
        category = CharacterAppearance.Category.Hat;
        if (appearance != null) appearance.LoadSaved();
        if (gameplayCamera != null) gameplayCamera.enabled = false;
        Focus(player != null ? player.gameObject : null, characterViewDistance);
        RefreshCustomizationLabels();
        if (EventSystem.current != null && optionNextButton != null)
            EventSystem.current.SetSelectedGameObject(optionNextButton.gameObject);
    }

    private void ChangeCategory(int direction)
    {
        int count = 3;
        category = (CharacterAppearance.Category)(((int)category + direction + count) % count);
        RefreshCustomizationLabels();
    }

    private void ChangeOption(int direction)
    {
        if (appearance == null) return;
        appearance.SetIndex(category, appearance.GetIndex(category) + direction, true);
        RefreshCustomizationLabels();
    }

    private void RefreshCustomizationLabels()
    {
        int selected = appearance != null ? appearance.GetIndex(category) : 0;
        string option = CharacterAppearance.GetOptionName(category, selected).ToUpperInvariant();
        if (categoryLabel != null) categoryLabel.text = category.ToString().ToUpperInvariant();
        if (optionLabel != null) optionLabel.text = option;
    }

    private void Focus(GameObject target, float distanceScale)
    {
        if (sceneCamera == null) sceneCamera = Camera.main;
        if (sceneCamera == null || target == null) return;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float distance = Mathf.Max(3.4f, bounds.size.magnitude * distanceScale);
        sceneCamera.transform.position = bounds.center + new Vector3(0f, bounds.extents.y * 0.28f, -distance);
        sceneCamera.transform.rotation = Quaternion.LookRotation(
            bounds.center + Vector3.up * (bounds.extents.y * 0.12f) - sceneCamera.transform.position);
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (eventSystem == null)
        {
            GameObject item = new GameObject("EventSystem", typeof(EventSystem));
            InputSystemUIInputModule module = item.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
        else if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
        {
            BaseInputModule[] oldModules = eventSystem.GetComponents<BaseInputModule>();
            for (int i = 0; i < oldModules.Length; i++) Destroy(oldModules[i]);
            InputSystemUIInputModule module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    private static TMP_Text Label(Button button)
    {
        return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static string TextOf(TMP_Text label, string fallback)
    {
        return label != null ? label.text : fallback;
    }
}
