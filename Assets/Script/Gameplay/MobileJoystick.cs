using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public System.Action<Vector2> MovementChanged;
    private RectTransform baseRect, knob;
    private PlayerController player;
    private int pointerId = int.MinValue;
    private const float Radius = 38f;

    public void Initialize(RectTransform knobRect, PlayerController target)
    { baseRect = transform as RectTransform; knob = knobRect; player = target; }

    public void OnPointerDown(PointerEventData data)
    {
        if (pointerId != int.MinValue) return;
        pointerId = data.pointerId;
        OnDrag(data);
    }

    public void OnDrag(PointerEventData data)
    {
        if (pointerId != data.pointerId) return;
        if (baseRect == null) baseRect = transform as RectTransform;
        if (knob == null) knob = transform.Find("StickKnob") as RectTransform;
        if (baseRect == null || knob == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(baseRect,data.position,data.pressEventCamera,out Vector2 local)) return;
        local -= baseRect.rect.center;
        Vector2 value = Vector2.ClampMagnitude(local / Radius,1f);
        knob.anchoredPosition = value * Radius;
        if (MovementChanged != null) MovementChanged(value);
        else if (player != null) player.SetVirtualMovement(value);
    }

    public void OnPointerUp(PointerEventData data) { if (pointerId == data.pointerId) Release(); }
    public void Release()
    {
        pointerId = int.MinValue;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
        if (MovementChanged != null) MovementChanged(Vector2.zero);
        else if (player != null) player.SetVirtualMovement(Vector2.zero);
    }
    private void OnDisable() => Release();
}
