using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class MobileLookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public System.Action<Vector2> Dragged;
    private int pointerId = int.MinValue;
    private Vector2 lastPosition;
    public void OnPointerDown(PointerEventData data)
    {
        if (pointerId != int.MinValue || MenuManager.BlocksGameplay) return;
        pointerId = data.pointerId;
        lastPosition = data.position;
    }
    public void OnDrag(PointerEventData data)
    {
        if (pointerId != data.pointerId || MenuManager.BlocksGameplay) return;
        Vector2 delta = data.position - lastPosition;
        lastPosition = data.position;
        Dragged?.Invoke(delta);
    }
    public void OnPointerUp(PointerEventData data) { if(data.pointerId == pointerId) Release(); }
    public void Release() => pointerId = int.MinValue;
    private void OnDisable() => Release();
}
