using UnityEngine;

[DisallowMultipleComponent]
public sealed class WobblyAmmoHud : MonoBehaviour
{
    [SerializeField] private WobblyWeaponController controller;
    private GUIStyle ammoStyle;
    private GUIStyle nameStyle;
    private GUIStyle hintStyle;
    private GUIStyle reloadStyle;
    private Texture2D panelTexture;

    public WobblyWeaponController Controller
    {
        get => controller;
        set => controller = value;
    }

    private void Awake()
    {
        if (controller == null) controller = GetComponent<WobblyWeaponController>();
    }

    private void OnGUI()
    {
        if (controller == null || controller.HeldWeapon == null) return;
        EnsureStyles();
        WobblyPickupWeapon weapon = controller.HeldWeapon;
        float width = 260f;
        float height = 104f;
        Rect panel = new Rect(Screen.width - width - 24f, Screen.height - height - 24f, width, height);
        GUI.DrawTexture(panel, panelTexture, ScaleMode.StretchToFill);
        GUI.Label(new Rect(panel.x + 16f, panel.y + 9f, width - 32f, 24f), weapon.DisplayName, nameStyle);
        GUI.Label(new Rect(panel.x + 14f, panel.y + 29f, width - 28f, 42f),
            weapon.RoundsInMagazine + "  /  " + weapon.ReserveAmmo, ammoStyle);

        if (controller.IsReloading)
        {
            GUI.Label(new Rect(panel.x + 16f, panel.y + 72f, width - 32f, 20f), "RELOADING", reloadStyle);
            Rect bar = new Rect(panel.x + 118f, panel.y + 80f, width - 134f, 5f);
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.73f, 0.25f, 0.95f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * controller.ReloadProgress, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        else
        {
            string hint = weapon.RoundsInMagazine == 0 && weapon.ReserveAmmo > 0
                ? "R  RELOAD" : "LMB  FIRE     R  RELOAD";
            GUI.Label(new Rect(panel.x + 16f, panel.y + 76f, width - 32f, 18f), hint, hintStyle);
        }

        if (controller.IsAiming)
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 7f, cy - 1f, 14f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 7f, 2f, 14f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    private void EnsureStyles()
    {
        if (ammoStyle != null) return;
        panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Ammo HUD panel" };
        panelTexture.SetPixel(0, 0, new Color(0.025f, 0.03f, 0.035f, 0.82f));
        panelTexture.Apply();
        ammoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 31,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = Color.white }
        };
        nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 0.74f, 0.27f) }
        };
        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.78f, 0.81f, 0.84f) }
        };
        reloadStyle = new GUIStyle(hintStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.74f, 0.27f) }
        };
    }

    private void OnDestroy()
    {
        if (panelTexture != null) Destroy(panelTexture);
    }
}
