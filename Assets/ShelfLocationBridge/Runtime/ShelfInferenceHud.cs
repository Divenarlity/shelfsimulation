using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation-only view for inference status. The inference client owns networking
/// and state; this component owns the scalable alert card and detection overlays.
/// </summary>
public class ShelfInferenceHud : MonoBehaviour {
    static readonly Color AlertRed = new(.93f, .08f, .10f, 1f);
    static readonly Color ReadyGreen = new(.12f, .75f, .42f, 1f);

    Canvas canvas;
    Text titleText;
    Text bodyText;
    Image accent;
    DetectionOverlayManager detectionOverlay;
    bool initialized;

    public void Initialize(Camera displayCamera) {
        if (initialized) return;
        initialized = true;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject(
            "ShelfInferenceCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        RectTransform overlayRoot = CreateRect("DetectionOverlay", canvas.transform);
        Stretch(overlayRoot);
        detectionOverlay = overlayRoot.gameObject.AddComponent<DetectionOverlayManager>();
        detectionOverlay.Initialize(overlayRoot, displayCamera, font);

        RectTransform panel = CreateRect("AlertCard", canvas.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-24f, -24f);
        panel.sizeDelta = new Vector2(450f, 170f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(.035f, .045f, .065f, .94f);
        panelImage.raycastTarget = false;
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, .18f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform accentRect = CreateRect("Accent", panel);
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, .5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(7f, 0f);
        accent = accentRect.gameObject.AddComponent<Image>();
        accent.color = AlertRed;
        accent.raycastTarget = false;

        titleText = CreateText("Title", panel, font, 30, FontStyle.Bold);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(.5f, 1f);
        titleRect.offsetMin = new Vector2(22f, -80f);
        titleRect.offsetMax = new Vector2(-16f, -13f);
        titleText.alignment = TextAnchor.MiddleLeft;
        titleText.lineSpacing = .85f;

        bodyText = CreateText("Body", panel, font, 24, FontStyle.Normal);
        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(22f, 13f);
        bodyRect.offsetMax = new Vector2(-16f, -82f);
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.lineSpacing = 1.08f;

        ShowStatus("RAF İZLEME", "Shelf inference başlatılıyor...", false);
    }

    public void SetVisible(bool visible) {
        if (canvas != null) canvas.gameObject.SetActive(visible);
    }

    public void ShowStatus(string title, string body, bool isAlert) {
        if (!initialized) return;
        ApplyStatus(title, body, isAlert);
        detectionOverlay.Clear();
    }

    void ApplyStatus(string title, string body, bool isAlert) {
        titleText.text = title;
        bodyText.text = body;
        titleText.color = isAlert ? new Color(1f, .72f, .72f) : Color.white;
        accent.color = isAlert ? AlertRed : ReadyGreen;
    }

    public void ShowResponse(ShelfInferenceShelf shelf, ShelfInferenceResponse response) {
        if (!initialized) return;
        detectionOverlay.Show(response);
        if (shelf == null) {
            ApplyStatus("RAF İZLEME", "Bilinen raf eşleşmesi bekleniyor", false);
            return;
        }
        if (shelf.empty_space_count <= 0) {
            ApplyStatus("RAF DURUMU", $"{shelf.shelf_id}  •  Boşluk yok", false);
            return;
        }

        ShelfInferenceSections sections = shelf.sections ?? new ShelfInferenceSections();
        titleText.text = "RAF BOŞLUĞU\nTESPİT EDİLDİ";
        bodyText.text =
            $"{shelf.shelf_id}  •  Toplam: {shelf.empty_space_count}\n" +
            $"SOL: {sections.SOL}   ORTA: {sections.ORTA}   SAĞ: {sections.SAĞ}";
        titleText.color = new Color(1f, .72f, .72f);
        accent.color = AlertRed;
        detectionOverlay.Show(response);
    }

    void OnDestroy() {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    static RectTransform CreateRect(string name, Transform parent) {
        var gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static void Stretch(RectTransform rect) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static Text CreateText(
        string name, Transform parent, Font font, int size, FontStyle style) {
        RectTransform rect = CreateRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }
}
