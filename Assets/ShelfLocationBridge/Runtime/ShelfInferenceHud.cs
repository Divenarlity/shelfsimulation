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
    RectTransform detectionOverlayRoot;
    RectTransform modelViewport;
    RawImage modelOutput;
    AspectRatioFitter modelAspect;
    RectTransform alertCard;
    Camera displayCamera;
    Texture2D ownedVisualization;
    string pendingFrameId;
    ShelfVisualizationMode visualizationMode=ShelfVisualizationMode.PythonAnnotatedFrame;
    bool initialized;

    public bool IsPythonVisualizationVisible =>
        modelViewport != null && modelViewport.gameObject.activeSelf;
    public bool IsGeometryOverlayVisible =>
        detectionOverlayRoot != null && detectionOverlayRoot.gameObject.activeSelf;
    public bool IsAlertCardVisible => alertCard != null && alertCard.gameObject.activeSelf;
    public Transform CanvasTransform => canvas != null ? canvas.transform : null;
    public RenderMode CanvasRenderMode =>
        canvas != null ? canvas.renderMode : RenderMode.WorldSpace;
    public Texture2D CurrentVisualizationTexture => ownedVisualization;
    public string VisualizationFrameId { get; private set; }
    public string BodyText => bodyText != null ? bodyText.text : null;
    public Rect VisualizationUvRect => modelOutput != null ? modelOutput.uvRect : default;
    public Vector3 VisualizationLocalScale =>
        modelOutput != null ? modelOutput.rectTransform.localScale : Vector3.zero;
    public Vector3 VisualizationLocalEulerAngles =>
        modelOutput != null ? modelOutput.rectTransform.localEulerAngles : Vector3.zero;
    public AspectRatioFitter.AspectMode VisualizationAspectMode =>
        modelAspect != null ? modelAspect.aspectMode : AspectRatioFitter.AspectMode.None;
    public Vector2 VisualizationViewportAnchorMin =>
        modelViewport != null ? modelViewport.anchorMin : Vector2.zero;
    public Vector2 VisualizationViewportAnchorMax =>
        modelViewport != null ? modelViewport.anchorMax : Vector2.zero;

    public void Initialize(Camera displayCamera) {
        if (initialized) return;
        initialized = true;
        this.displayCamera = displayCamera;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject(
            "ShelfInferenceCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        // This overlay must not inherit RobotRig's world position/rotation. A root
        // ScreenSpaceOverlay canvas is presentation-only and cannot become store geometry.
        canvasObject.transform.SetParent(null, false);
        canvasObject.transform.localPosition = Vector3.zero;
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one;
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        modelViewport = CreateRect("PythonModelOutputViewport", canvas.transform);
        Image viewportBackground = modelViewport.gameObject.AddComponent<Image>();
        viewportBackground.color = Color.black;
        viewportBackground.raycastTarget = false;
        modelViewport.gameObject.AddComponent<RectMask2D>();
        SyncViewport();
        RectTransform outputRect = CreateRect("PythonModelOutput", modelViewport);
        Stretch(outputRect);
        outputRect.localScale = Vector3.one;
        outputRect.localRotation = Quaternion.identity;
        modelOutput = outputRect.gameObject.AddComponent<RawImage>();
        modelOutput.color = Color.white;
        modelOutput.raycastTarget = false;
        modelOutput.uvRect = new Rect(0f, 0f, 1f, 1f);
        modelAspect = outputRect.gameObject.AddComponent<AspectRatioFitter>();
        // Show the complete Python frame. EnvelopeParent filled the viewport by
        // cropping, which could hide roughly half the image in narrow Game views.
        modelAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        modelViewport.gameObject.SetActive(false);

        detectionOverlayRoot = CreateRect("DetectionOverlay", canvas.transform);
        Stretch(detectionOverlayRoot);
        detectionOverlay = detectionOverlayRoot.gameObject.AddComponent<DetectionOverlayManager>();
        detectionOverlay.Initialize(detectionOverlayRoot, displayCamera, font);

        RectTransform panel = CreateRect("AlertCard", canvas.transform);
        alertCard = panel;
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
        SetVisualizationMode(visualizationMode);
    }

    void LateUpdate() {
        if (initialized) SyncViewport();
    }

    void SyncViewport() {
        if (modelViewport == null) return;
        Rect rect = displayCamera != null ? displayCamera.rect : new Rect(0f, 0f, 1f, 1f);
        modelViewport.anchorMin = rect.min;
        modelViewport.anchorMax = rect.max;
        modelViewport.offsetMin = Vector2.zero;
        modelViewport.offsetMax = Vector2.zero;
    }

    public void SetVisualizationMode(ShelfVisualizationMode mode) {
        visualizationMode = mode;
        if (!initialized) return;
        detectionOverlayRoot.gameObject.SetActive(mode == ShelfVisualizationMode.UnityGeometryOverlay);
        modelViewport.gameObject.SetActive(
            mode == ShelfVisualizationMode.PythonAnnotatedFrame && ownedVisualization != null);
        if (mode != ShelfVisualizationMode.UnityGeometryOverlay) detectionOverlay.Clear();
    }

    public void SetVisible(bool visible) {
        if (canvas == null) return;
        if (!visible) ClearVisualization();
        canvas.gameObject.SetActive(visible);
    }

    public void ShowStatus(string title, string body, bool isAlert) {
        if (!initialized) return;
        ApplyStatus(title, body, isAlert);
        detectionOverlay.Clear();
        ClearVisualization();
    }

    void ApplyStatus(string title, string body, bool isAlert) {
        titleText.text = title;
        bodyText.text = body;
        titleText.color = isAlert ? new Color(1f, .72f, .72f) : Color.white;
        accent.color = isAlert ? AlertRed : ReadyGreen;
    }

    public void ShowResponse(ShelfInferenceShelf shelf, ShelfInferenceResponse response) {
        if (!initialized) return;
        if (visualizationMode == ShelfVisualizationMode.UnityGeometryOverlay)
            detectionOverlay.Show(response);
        else detectionOverlay.Clear();
        if (shelf == null) {
            ApplyStatus("RAF İZLEME", "Bilinen raf eşleşmesi bekleniyor", false);
            return;
        }
        string displayId = !string.IsNullOrWhiteSpace(shelf.shelf_level_id)
            ? shelf.shelf_level_id : shelf.shelf_id;
        if (shelf.empty_space_count <= 0) {
            ApplyStatus("RAF DURUMU", $"{displayId}  •  Boşluk yok", false);
            return;
        }

        ShelfInferenceSections sections = shelf.sections ?? new ShelfInferenceSections();
        titleText.text = "RAF BOŞLUĞU\nTESPİT EDİLDİ";
        bodyText.text =
            $"{displayId}  •  Toplam: {shelf.empty_space_count}\n" +
            $"SOL: {sections.SOL}   ORTA: {sections.ORTA}   SAĞ: {sections.SAĞ}";
        titleText.color = new Color(1f, .72f, .72f);
        accent.color = AlertRed;
    }

    public void PrepareVisualizationFrame(string frameId) {
        pendingFrameId = frameId;
        ClearVisualization();
    }

    public bool TryShowVisualization(string frameId, Texture2D texture, int width, int height) {
        if (!initialized || visualizationMode != ShelfVisualizationMode.PythonAnnotatedFrame ||
            texture == null || width <= 0 || height <= 0 || frameId != pendingFrameId)
            return false;
        ClearVisualization();
        pendingFrameId = frameId;
        ownedVisualization = texture;
        VisualizationFrameId = frameId;
        modelOutput.texture = texture;
        modelAspect.aspectRatio = (float)width / height;
        modelViewport.gameObject.SetActive(true);
        return true;
    }

    public void ClearVisualization() {
        if (modelViewport != null) modelViewport.gameObject.SetActive(false);
        if (modelOutput != null) modelOutput.texture = null;
        if (ownedVisualization != null) {
            if (Application.isPlaying) Destroy(ownedVisualization);
            else DestroyImmediate(ownedVisualization);
        }
        ownedVisualization = null;
        VisualizationFrameId = null;
    }

    void OnDestroy() {
        ClearVisualization();
        if (canvas != null) {
            if (Application.isPlaying) Destroy(canvas.gameObject);
            else DestroyImmediate(canvas.gameObject);
        }
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
