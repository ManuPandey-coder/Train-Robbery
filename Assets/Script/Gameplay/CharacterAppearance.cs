using UnityEngine;

[DisallowMultipleComponent]
public sealed class CharacterAppearance : MonoBehaviour
{
    public enum Category
    {
        Hat,
        Facial,
        Body
    }

    private const string HatKey = "character.hat";
    private const string FacialKey = "character.facial";
    private const string SkinKey = "character.skin";

    private static readonly Color[] SkinTones =
    {
        new Color(1.00f, 0.78f, 0.62f),
        new Color(0.91f, 0.65f, 0.47f),
        new Color(0.79f, 0.52f, 0.35f),
        new Color(0.65f, 0.40f, 0.25f),
        new Color(0.48f, 0.28f, 0.17f),
        new Color(0.32f, 0.18f, 0.11f)
    };

    private Renderer bodyRenderer;
    private GameObject hat;
    private GameObject duplicateHat;
    private GameObject moustache;
    private GameObject mask;
    private MaterialPropertyBlock bodyProperties;

    public int HatIndex { get; private set; }
    public int FacialIndex { get; private set; }
    public int SkinToneIndex { get; private set; }

    public static int HatOptionCount => 2;
    public static int FacialOptionCount => 3;
    public static int SkinToneCount => SkinTones.Length;

    private void Awake()
    {
        CacheParts();
        LoadSaved();
    }

    private void OnEnable()
    {
        CacheParts();
        Apply();
    }

    public void LoadSaved()
    {
        SetSelection(
            PlayerPrefs.GetInt(HatKey, 0),
            PlayerPrefs.GetInt(FacialKey, 0),
            PlayerPrefs.GetInt(SkinKey, 0),
            false);
    }

    public void Save()
    {
        PlayerPrefs.SetInt(HatKey, HatIndex);
        PlayerPrefs.SetInt(FacialKey, FacialIndex);
        PlayerPrefs.SetInt(SkinKey, SkinToneIndex);
        PlayerPrefs.Save();
    }

    public void SetSelection(int hatIndex, int facialIndex, int skinToneIndex, bool save)
    {
        HatIndex = Mathf.Clamp(hatIndex, 0, HatOptionCount - 1);
        FacialIndex = Mathf.Clamp(facialIndex, 0, FacialOptionCount - 1);
        SkinToneIndex = Mathf.Clamp(skinToneIndex, 0, SkinToneCount - 1);
        Apply();
        if (save) Save();
    }

    public int GetIndex(Category category)
    {
        switch (category)
        {
            case Category.Hat: return HatIndex;
            case Category.Facial: return FacialIndex;
            default: return SkinToneIndex;
        }
    }

    public int GetOptionCount(Category category)
    {
        switch (category)
        {
            case Category.Hat: return HatOptionCount;
            case Category.Facial: return FacialOptionCount;
            default: return SkinToneCount;
        }
    }

    public void SetIndex(Category category, int value, bool save)
    {
        int count = GetOptionCount(category);
        value = (value % count + count) % count;
        switch (category)
        {
            case Category.Hat: HatIndex = value; break;
            case Category.Facial: FacialIndex = value; break;
            default: SkinToneIndex = value; break;
        }
        Apply();
        if (save) Save();
    }

    public static string GetOptionName(Category category, int index)
    {
        if (category == Category.Hat) return index == 0 ? "None" : "Cowboy Hat";
        if (category == Category.Facial)
        {
            if (index == 1) return "Moustache";
            if (index == 2) return "Mask";
            return "None";
        }
        return "Skin Tone " + (Mathf.Clamp(index, 0, SkinToneCount - 1) + 1);
    }

    private void CacheParts()
    {
        if (bodyProperties == null) bodyProperties = new MaterialPropertyBlock();
        Transform[] parts = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i];
            if (part.name == "BODY") bodyRenderer = part.GetComponent<Renderer>();
            else if (part.name == "HAT") hat = part.gameObject;
            else if (part.name == "HAT.001") duplicateHat = part.gameObject;
            else if (part.name == "MUCHH") moustache = part.gameObject;
            else if (part.name == "mask") mask = part.gameObject;
        }
    }

    private void Apply()
    {
        if (hat != null) hat.SetActive(HatIndex == 1);
        // The Blender file contains an identical duplicate. Keeping it disabled
        // avoids flicker while presenting one real hat option, as requested.
        if (duplicateHat != null) duplicateHat.SetActive(false);
        if (moustache != null) moustache.SetActive(FacialIndex == 1);
        if (mask != null) mask.SetActive(FacialIndex == 2);

        if (bodyRenderer == null) return;
        bodyRenderer.GetPropertyBlock(bodyProperties);
        Color tone = SkinTones[Mathf.Clamp(SkinToneIndex, 0, SkinTones.Length - 1)];
        bodyProperties.SetColor("_BaseColor", tone);
        bodyProperties.SetColor("_Color", tone);
        bodyRenderer.SetPropertyBlock(bodyProperties);
    }
}
