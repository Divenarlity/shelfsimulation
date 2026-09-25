using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>UI-native closed polygon outline rendered in normalized viewport space.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ShelfPolygonGraphic : MaskableGraphic {
    readonly List<Vector2> points = new();
    float thickness = 4f;

    public void SetPoints(IReadOnlyList<Vector2> normalizedPoints, float lineThickness) {
        points.Clear();
        if (normalizedPoints != null) {
            for (int i = 0; i < normalizedPoints.Count; i++)
                points.Add(normalizedPoints[i]);
        }
        thickness = Mathf.Max(1f, lineThickness);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper) {
        vertexHelper.Clear();
        if (points.Count < 3 || rectTransform.rect.width <= 0f ||
            rectTransform.rect.height <= 0f) return;

        Rect rect = rectTransform.rect;
        for (int i = 0; i < points.Count; i++) {
            Vector2 start = ToLocal(points[i], rect);
            Vector2 end = ToLocal(points[(i + 1) % points.Count], rect);
            AddSegment(vertexHelper, start, end);
        }
    }

    static Vector2 ToLocal(Vector2 normalized, Rect rect) => new(
        Mathf.Lerp(rect.xMin, rect.xMax, normalized.x),
        Mathf.Lerp(rect.yMin, rect.yMax, normalized.y));

    void AddSegment(VertexHelper vertexHelper, Vector2 start, Vector2 end) {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude <= .0001f) return;
        Vector2 direction = delta.normalized;
        Vector2 normal = new(-direction.y, direction.x);
        Vector2 halfNormal = normal * (thickness * .5f);
        Vector2 cap = direction * (thickness * .5f);
        start -= cap;
        end += cap;

        int first = vertexHelper.currentVertCount;
        vertexHelper.AddVert(start - halfNormal, color, Vector2.zero);
        vertexHelper.AddVert(start + halfNormal, color, Vector2.zero);
        vertexHelper.AddVert(end + halfNormal, color, Vector2.zero);
        vertexHelper.AddVert(end - halfNormal, color, Vector2.zero);
        vertexHelper.AddTriangle(first, first + 1, first + 2);
        vertexHelper.AddTriangle(first, first + 2, first + 3);
    }
}
