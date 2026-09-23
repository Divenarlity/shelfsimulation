using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws both cascade stages through one top-left-origin capture-to-viewport mapping.
/// Shelf and empty-gap views are pooled independently but never use separate geometry code.
/// </summary>
public class DetectionOverlayManager : MonoBehaviour {
    const float ShelfBorderThickness = 4f;
    const float EmptyBorderThickness = 5f;
    static readonly Color ShelfColor = new(0f, .88f, .82f, 1f);
    static readonly Color ShelfFill = new(0f, .88f, .82f, .035f);
    static readonly Color EmptyColor = new(1f, .04f, .06f, 1f);
    static readonly Color EmptyFill = new(1f, .05f, .05f, .07f);

    readonly List<OverlayBoxView> shelfBoxes = new();
    readonly List<OverlayBoxView> emptyBoxes = new();
    RectTransform viewport, shelfLayer, emptyLayer;
    Camera displayCamera;
    Font font;

    public void Initialize(RectTransform overlayRoot, Camera camera, Font uiFont) {
        displayCamera = camera;
        font = uiFont;
        if (viewport != null) return;
        var viewportObject = new GameObject("DetectionViewport", typeof(RectTransform));
        viewport = viewportObject.GetComponent<RectTransform>();
        viewport.SetParent(overlayRoot, false);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        shelfLayer = CreateLayer("ShelfOverlays", viewport);
        emptyLayer = CreateLayer("EmptyShelfOverlays", viewport);
        UpdateViewport();
    }

    static RectTransform CreateLayer(string name, RectTransform parent) {
        var layerObject = new GameObject(name, typeof(RectTransform));
        RectTransform layer = layerObject.GetComponent<RectTransform>();
        layer.SetParent(parent, false);
        layer.anchorMin = Vector2.zero;
        layer.anchorMax = Vector2.one;
        layer.offsetMin = Vector2.zero;
        layer.offsetMax = Vector2.zero;
        return layer;
    }

    void LateUpdate() {
        if (viewport != null) UpdateViewport();
    }

    void UpdateViewport() {
        Rect cameraRect = displayCamera != null ? displayCamera.rect : new Rect(0f, 0f, 1f, 1f);
        viewport.anchorMin = cameraRect.min;
        viewport.anchorMax = cameraRect.max;
        viewport.anchoredPosition = Vector2.zero;
        viewport.sizeDelta = Vector2.zero;
    }

    public void Show(ShelfInferenceResponse response) {
        if (viewport == null || response == null || response.image == null ||
            response.image.width <= 0 || response.image.height <= 0) {
            Clear();
            return;
        }

        int shelfIndex = 0;
        int emptyIndex = 0;
        foreach (ShelfInferenceShelf shelf in response.shelves ?? Array.Empty<ShelfInferenceShelf>()) {
            if (shelf == null || !TryMapRect(
                    shelf.global_bbox_xyxy, response.image, out Rect shelfRect)) continue;
            OverlayBoxView shelfBox = GetBox(
                shelfBoxes, shelfLayer, shelfIndex++, "ShelfBox", ShelfColor, ShelfFill,
                ShelfBorderThickness, 190f);
            string shelfName = shelf.shelf_id == "UNKNOWN_SHELF"
                ? $"RAF {shelf.shelf_index + 1}"
                : shelf.shelf_id;
            string shelfLabel = shelf.confidence > 0f
                ? $"{shelfName}  %{Mathf.RoundToInt(Mathf.Clamp01(shelf.confidence) * 100f)}"
                : shelfName;
            shelfBox.Set(shelfRect, shelfLabel);

            foreach (ShelfInferenceDetection detection in
                     shelf.detections ?? Array.Empty<ShelfInferenceDetection>()) {
                if (detection == null || !TryMapRect(
                        detection.global_bbox_xyxy, response.image, out Rect emptyRect)) continue;
                OverlayBoxView emptyBox = GetBox(
                    emptyBoxes, emptyLayer, emptyIndex++, "EmptyShelfBox", EmptyColor, EmptyFill,
                    EmptyBorderThickness, 175f);
                string label = detection.confidence > 0f
                    ? $"BOŞLUK %{Mathf.RoundToInt(Mathf.Clamp01(detection.confidence) * 100f)}"
                    : "BOŞLUK";
                emptyBox.Set(emptyRect, label);
            }
        }

        HideUnused(shelfBoxes, shelfIndex);
        HideUnused(emptyBoxes, emptyIndex);
    }

    bool TryMapRect(float[] bbox, ImageMetadata image, out Rect mapped) {
        mapped = default;
        if (!TryGetNormalizedRect(bbox, image.width, image.height, out Rect normalized))
            return false;
        mapped = MapCaptureToDisplayAspect(
            normalized,
            (float)image.width / image.height,
            displayCamera != null ? displayCamera.aspect :
            (float)Screen.width / Mathf.Max(1, Screen.height));
        if (mapped.xMax <= 0f || mapped.xMin >= 1f) return false;
        mapped.xMin = Mathf.Clamp01(mapped.xMin);
        mapped.xMax = Mathf.Clamp01(mapped.xMax);
        return mapped.width > 0f && mapped.height > 0f;
    }

    public void Clear() {
        HideUnused(shelfBoxes, 0);
        HideUnused(emptyBoxes, 0);
    }

    OverlayBoxView GetBox(
        List<OverlayBoxView> pool, RectTransform parent, int index, string prefix,
        Color border, Color fill, float thickness, float labelWidth) {
        if (index < pool.Count) {
            pool[index].SetActive(true);
            return pool[index];
        }
        var box = new OverlayBoxView(
            parent, font, $"{prefix}_{pool.Count + 1}", border, fill, thickness, labelWidth);
        pool.Add(box);
        return box;
    }

    static void HideUnused(List<OverlayBoxView> pool, int firstUnused) {
        for (int i = firstUnused; i < pool.Count; i++) pool[i].SetActive(false);
    }

    public static bool TryGetNormalizedRect(
        float[] bbox, int imageWidth, int imageHeight, out Rect normalized) {
        normalized = default;
        if (bbox == null || bbox.Length != 4 || imageWidth <= 0 || imageHeight <= 0)
            return false;
        foreach (float value in bbox)
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;

        float x1 = Mathf.Clamp(bbox[0], 0f, imageWidth);
        float y1 = Mathf.Clamp(bbox[1], 0f, imageHeight);
        float x2 = Mathf.Clamp(bbox[2], 0f, imageWidth);
        float y2 = Mathf.Clamp(bbox[3], 0f, imageHeight);
        if (x2 <= x1 || y2 <= y1) return false;
        normalized = Rect.MinMaxRect(
            x1 / imageWidth, 1f - y2 / imageHeight,
            x2 / imageWidth, 1f - y1 / imageHeight);
        return true;
    }

    public static Rect MapCaptureToDisplayAspect(
        Rect normalized, float captureAspect, float displayAspect) {
        if (captureAspect <= 0f || displayAspect <= 0f) return normalized;
        float ratio = captureAspect / displayAspect;
        normalized.xMin = .5f + (normalized.xMin - .5f) * ratio;
        normalized.xMax = .5f + (normalized.xMax - .5f) * ratio;
        return normalized;
    }

    sealed class OverlayBoxView {
        readonly GameObject rootObject;
        readonly RectTransform root;
        readonly Text label;

        public OverlayBoxView(
            RectTransform parent, Font font, string name, Color borderColor, Color fillColor,
            float borderThickness, float labelWidth) {
            rootObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            root = rootObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            Image fill = rootObject.GetComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;

            CreateEdge("Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -borderThickness * .5f), new Vector2(0f, borderThickness), borderColor);
            CreateEdge("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, borderThickness * .5f), new Vector2(0f, borderThickness), borderColor);
            CreateEdge("Left", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(borderThickness * .5f, 0f), new Vector2(borderThickness, 0f), borderColor);
            CreateEdge("Right", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-borderThickness * .5f, 0f), new Vector2(borderThickness, 0f), borderColor);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Image));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(root, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 5f);
            labelRect.sizeDelta = new Vector2(labelWidth, 31f);
            Image labelBackground = labelObject.GetComponent<Image>();
            labelBackground.color = new Color(borderColor.r, borderColor.g, borderColor.b, .94f);
            labelBackground.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(labelRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5f, 0f);
            textRect.offsetMax = new Vector2(-5f, 0f);
            label = textObject.GetComponent<Text>();
            label.font = font;
            label.fontSize = 20;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        void CreateEdge(
            string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position,
            Vector2 size, Color color) {
            var edgeObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform edge = edgeObject.GetComponent<RectTransform>();
            edge.SetParent(root, false);
            edge.anchorMin = anchorMin;
            edge.anchorMax = anchorMax;
            edge.anchoredPosition = position;
            edge.sizeDelta = size;
            Image image = edgeObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        public void Set(Rect normalized, string text) {
            root.anchorMin = normalized.min;
            root.anchorMax = normalized.max;
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = Vector2.zero;
            label.text = text;
            SetActive(true);
        }

        public void SetActive(bool active) => rootObject.SetActive(active);
    }
}
