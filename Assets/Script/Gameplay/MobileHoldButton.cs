using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class MobileHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public System.Action<bool> Pressed;
    public bool IsHeld => pointerId != int.MinValue;
    private int pointerId = int.MinValue;

    public void OnPointerDown(PointerEventData data)
    {
        if (IsHeld || MenuManager.BlocksGameplay) return;
        var button = GetComponent<UnityEngine.UI.Button>();
        if (button != null && !button.IsInteractable()) return;
        pointerId = data.pointerId;
        Pressed?.Invoke(true);
    }

    public void OnPointerUp(PointerEventData data) { if (data.pointerId == pointerId) Release(); }
    public void Release()
    {
        if (!IsHeld) return;
        pointerId = int.MinValue;
        Pressed?.Invoke(false);
    }
    private void OnDisable() => Release();
}
