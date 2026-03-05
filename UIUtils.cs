using UnityEngine;
using UnityEngine.UI;

public static class UIUtils
{
    public static Camera GetCorrectCamera(this Canvas _rootCanvas) =>
        (_rootCanvas is null || _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : _rootCanvas.worldCamera;

    /// <summary>
    ///  从ScrollRect的某个点开始缩放
    ///  Zoom from a point
    /// </summary>
    /// <param name="scrollRect"></param>
    /// <param name="content"></param>
    /// <param name="screenPos">eventData.position/Input.mousePosition</param>
    /// <param name="camera">GetCorrectCamera(this Canvas _rootCanvas) </param>
    /// <param name="delta">Scroll delta</param>
    /// <param name="_zoomSpeed"></param>
    /// <param name="minZoom"></param>
    /// <param name="maxZoom"></param>
    public static void Zoom(this ScrollRect scrollRect, RectTransform content, Vector2 screenPos, Camera camera,
        float delta, float _zoomSpeed,
        float minZoom, float maxZoom)
    {
        if (delta == 0 ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPos, camera, out var localPoint))
        {
            return;
        }

        scrollRect.velocity = Vector2.zero;

        scrollRect.StopMovement();

        Vector3 oldScale = content.localScale;

        float direction = Mathf.Sign(delta);

        float newScaleVal = Mathf.Clamp(oldScale.x + (direction * _zoomSpeed), minZoom, maxZoom);

        if (Mathf.Abs(newScaleVal - oldScale.x) < 0.0001f) return;

        Vector3 newScale = new Vector3(newScaleVal, newScaleVal, 1f);

        Vector3 pivotOffset = new Vector3(
            localPoint.x * (newScale.x - oldScale.x),
            localPoint.y * (newScale.y - oldScale.y),
            0
        );

        content.localScale = newScale;
        content.anchoredPosition -= (Vector2)pivotOffset;
    }
}