using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.NightVisionGoggles
{
    /// <summary>
    /// Battery status indicator and status message overlay.
    /// Styled to match Raft's UI design: dark navy panel background (#161B23),
    /// warm cream text (#F2E2C5), colored durability bar (green→yellow→red).
    /// Font is auto-detected from the game scene at runtime.
    /// </summary>
    public class NightVisionUI : MonoBehaviour
    {
        // -- UI elements ----------------------------------------------------------
        private Canvas overlayCanvas;
        private GameObject overlayRoot;
        private GameObject batteryPanel;
        private Text batteryPercentText;
        private Image batteryFillImage;
        private GameObject statusPanel;
        private Text statusText;

        // -- Font reference -------------------------------------------------------
        private Font raftFont;

        // -- State ----------------------------------------------------------------
        private float lowBatteryWarningTimer = 0f;
        private bool isLowBattery = false;
        private Coroutine messageCoroutine;
        private NightVisionController controller;

        // -- Raft color palette (from UIStyleTokens) ------------------------------
        private static readonly Color ColPanel     = new Color(0.09f, 0.11f, 0.14f, 0.95f);
        private static readonly Color ColPanelDark  = new Color(0.06f, 0.08f, 0.11f, 0.97f);
        private static readonly Color ColText       = new Color(0.95f, 0.89f, 0.77f, 1f);
        private static readonly Color ColFill      = new Color(0.745f, 0.627f, 0.424f, 1f);
        private static readonly Color ColWarn       = new Color(0.94f, 0.73f, 0.33f, 1f);
        private static readonly Color ColBad        = new Color(0.95f, 0.47f, 0.43f, 1f);
        private static readonly Color ColTextOutline = new Color(0f, 0f, 0f, 0.7f);
        private static readonly Color ColAccent     = new Color(0.36f, 0.79f, 0.90f, 1f);

        private const float LOW_BATTERY_THRESHOLD = 0.2f;
        private const float WARNING_BEEP_INTERVAL = 3f;

        /// <summary>
        /// Detects the font Raft uses by searching active Text components.
        /// The game ships a custom font; we find it at runtime.
        /// </summary>
        private Font DetectRaftFont()
        {
            Text[] allTexts = FindObjectsOfType<Text>();
            foreach (Text t in allTexts)
            {
                if (t.font != null && t.font.name != "Arial" && t.font.name.Length > 0)
                {

                    return t.font;
                }
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>
        /// Creates a Raft-style panel sprite procedurally (dark rounded rect).
        /// Raft uses 9-sliced sprites; we generate a simple filled one as fallback.
        /// </summary>
        private Sprite CreatePanelSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] p = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - size / 2f) / (size / 2f);
                    float dy = Mathf.Abs(y - size / 2f) / (size / 2f);
                    float edge = Mathf.Max(dx, dy);
                    // Rounded corners: fade to transparent at the very edges
                    float alpha = edge > 0.92f ? Mathf.Lerp(1f, 0f, (edge - 0.92f) / 0.08f) : 1f;
                    p[y * size + x] = new Color(ColPanelDark.r, ColPanelDark.g, ColPanelDark.b, alpha * ColPanelDark.a);
                }
            }
            tex.SetPixels(p); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        public void Initialize(NightVisionController controller)
        {
            this.controller = controller;
            raftFont = DetectRaftFont();
            CreateUI();

        }

        private void CreateUI()
        {
            GameObject canvasGO = GameObject.Find("Canvases/_CanvasGame_New");
            if (canvasGO == null)
            {
                Canvas[] all = FindObjectsOfType<Canvas>();
                foreach (Canvas c in all)
                    if (c.enabled && c.gameObject.activeInHierarchy && c.renderMode == RenderMode.ScreenSpaceOverlay)
                        { canvasGO = c.gameObject; break; }
            }
            if (canvasGO == null) { Debug.LogError("[NightVisionUI] No canvas found"); return; }

            overlayRoot = new GameObject("NightVisionUI_Overlay");
            overlayRoot.transform.SetParent(canvasGO.transform, false);
            RectTransform rootRect = overlayRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;

            overlayCanvas = overlayRoot.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = 2; // Above NV overlay, below game UI

            overlayRoot.AddComponent<GraphicRaycaster>();

            CreateBatteryPanel(overlayRoot.transform);
            CreateStatusPanel(overlayRoot.transform);

        }

        // ========================================================================
        // BATTERY PANEL (top-right corner)
        // ========================================================================

        private void CreateBatteryPanel(Transform parent)
        {
            float panelWidth = 200f;
            float panelHeight = 24f;

            batteryPanel = new GameObject("BatteryPanel");
            batteryPanel.transform.SetParent(parent, false);
            RectTransform panelRect = batteryPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            // Battery panel: Y=105 (center-bottom, above hotbar, below status text)
            panelRect.anchoredPosition = new Vector2(0f, 107f);
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            // Panel background
            Image panelBg = batteryPanel.AddComponent<Image>();
            panelBg.sprite = CreatePanelSprite(16);
            panelBg.color = ColPanel;
            panelBg.raycastTarget = false;

            // Battery fill bar background (dark track)
            GameObject barBgGO = new GameObject("BarBg");
            barBgGO.transform.SetParent(batteryPanel.transform, false);
            RectTransform barBgRect = barBgGO.AddComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0f, 0.1f); barBgRect.anchorMax = new Vector2(1f, 0.9f);
            barBgRect.offsetMin = new Vector2(6f, 0f); barBgRect.offsetMax = new Vector2(-6f, 0f);

            Image barBg = barBgGO.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.6f);
            barBg.raycastTarget = false;

            // Battery fill bar (foreground, left-anchored, width by sizeDelta)
            GameObject barGO = new GameObject("FillBar");
            barGO.transform.SetParent(barBgGO.transform, false);
            RectTransform barRect = barGO.AddComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f); barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.offsetMin = new Vector2(0f, 2f); barRect.offsetMax = new Vector2(0f, -2f);
            barRect.sizeDelta = new Vector2(0f, 0f);

            batteryFillImage = barGO.AddComponent<Image>();
            batteryFillImage.color = ColFill;
            batteryFillImage.raycastTarget = false;

            // Percentage text centered OVER the bar
            GameObject pctGO = new GameObject("PercentText");
            pctGO.transform.SetParent(batteryPanel.transform, false);
            RectTransform pctRect = pctGO.AddComponent<RectTransform>();
            pctRect.anchorMin = Vector2.zero; pctRect.anchorMax = Vector2.one;
            pctRect.offsetMin = new Vector2(4f, 1f); pctRect.offsetMax = new Vector2(-4f, -1f);

            batteryPercentText = pctGO.AddComponent<Text>();
            batteryPercentText.text = "100%";
            batteryPercentText.font = raftFont;
            batteryPercentText.fontSize = 10;
            batteryPercentText.alignment = TextAnchor.MiddleCenter;
            batteryPercentText.color = ColText;
            batteryPercentText.raycastTarget = false;

            Outline pctOutline = batteryPercentText.gameObject.AddComponent<Outline>();
            pctOutline.effectColor = ColTextOutline;
            pctOutline.effectDistance = new Vector2(1f, -1f);

            // Hidden until NV is activated
            batteryPanel.SetActive(false);
        }

        // ========================================================================
        // STATUS PANEL (center-bottom)
        // ========================================================================

        /// <summary>
        /// Shows or hides the battery panel. Called by the controller when NV is toggled.
        /// </summary>
        public void SetBatteryVisible(bool visible)
        {
            if (batteryPanel != null)
                batteryPanel.SetActive(visible);
        }

        private void CreateStatusPanel(Transform parent)
        {
            statusPanel = new GameObject("StatusPanel");
            statusPanel.transform.SetParent(parent, false);
            RectTransform panelRect = statusPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f); panelRect.anchorMax = new Vector2(0.5f, 0f);
            // Status text above battery bar
            panelRect.anchoredPosition = new Vector2(0f, 145f);
            panelRect.sizeDelta = new Vector2(280f, 36f);

            // Panel background
            Image panelBg = statusPanel.AddComponent<Image>();
            panelBg.sprite = CreatePanelSprite(32);
            panelBg.color = ColPanel;
            panelBg.raycastTarget = false;

            // Status text
            GameObject textGO = new GameObject("StatusText");
            textGO.transform.SetParent(statusPanel.transform, false);
            RectTransform textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 2f); textRect.offsetMax = new Vector2(-8f, -2f);

            statusText = textGO.AddComponent<Text>();
            statusText.text = "";
            statusText.font = raftFont;
            statusText.fontSize = 14;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = ColText;
            statusText.raycastTarget = false;

            Outline sOutline = statusText.gameObject.AddComponent<Outline>();
            sOutline.effectColor = ColTextOutline;
            sOutline.effectDistance = new Vector2(1f, -1f);

            statusPanel.SetActive(false);
        }

        // ========================================================================
        // UPDATE
        // ========================================================================

        private void Update()
        {
            if (this == null || !isActiveAndEnabled) return;

            try
            {
                if (controller == null || controller.Equipment == null) return;

                float durabilityPercent = controller.Equipment.GetDurabilityPercent();
                UpdateBatteryDisplay(durabilityPercent);

                if (durabilityPercent <= LOW_BATTERY_THRESHOLD && durabilityPercent > 0f)
                {
                    if (!isLowBattery)
                    {
                        isLowBattery = true;
                        lowBatteryWarningTimer = 0f;
                    }
                    lowBatteryWarningTimer += Time.deltaTime;
                    if (lowBatteryWarningTimer >= WARNING_BEEP_INTERVAL)
                    {
                        lowBatteryWarningTimer = 0f;
                        PlayLowBatteryWarning();
                    }
                }
                else
                {
                    isLowBattery = false;
                    lowBatteryWarningTimer = 0f;
                }
            }
            catch (Exception ex) { Debug.LogError("[NightVisionUI] Update failed: " + ex); }
        }

        public void UpdateBatteryDisplay(float percent)
        {
            if (batteryPercentText == null || batteryFillImage == null) return;

            int pct = Mathf.RoundToInt(percent * 100f);
            batteryPercentText.text = pct + "%";

            // Set fill bar width proportional to percentage
            // FillBar is left-anchored, so sizeDelta.x controls the visible width
            RectTransform fillRect = batteryFillImage.rectTransform;
            RectTransform barBg = fillRect.parent as RectTransform;
            if (barBg != null)
            {
                float maxWidth = barBg.rect.width - 4f; // 2px padding each side
                fillRect.sizeDelta = new Vector2(maxWidth * Mathf.Clamp01(percent), fillRect.sizeDelta.y);
            }

            batteryFillImage.color = ColFill;
        }

        // ========================================================================
        // STATUS MESSAGE
        // ========================================================================

        public void ShowStatusMessage(string message, float duration)
        {
            if (this == null || !isActiveAndEnabled) return;
            if (statusPanel == null || statusText == null) return;

            if (messageCoroutine != null)
            {
                StopCoroutine(messageCoroutine);
                messageCoroutine = null;
            }
            messageCoroutine = StartCoroutine(ShowMessageCoroutine(message, duration));
        }

        private IEnumerator ShowMessageCoroutine(string message, float duration)
        {
            statusPanel.SetActive(true);
            statusText.text = message;

            // Show at full opacity
            Color tc = statusText.color; tc.a = 1f; statusText.color = tc;
            Image bg = statusPanel.GetComponent<Image>();
            if (bg != null) { Color bc = bg.color; bc.a = ColPanel.a; bg.color = bc; }

            yield return new WaitForSeconds(duration);

            // Fade out over 0.5s
            float fadeTime = 0.5f;
            float elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - (elapsed / fadeTime);
                tc.a = alpha; statusText.color = tc;
                if (bg != null) { Color bc = bg.color; bc.a = ColPanel.a * alpha; bg.color = bc; }
                yield return null;
            }

            statusPanel.SetActive(false);
            messageCoroutine = null;
        }

        private void PlayLowBatteryWarning()
        {
            if (controller == null || controller.Equipment == null) return;

            if (controller.Equipment.warningBeepSound != null)
                AudioSource.PlayClipAtPoint(controller.Equipment.warningBeepSound,
                    Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.5f);

            if (batteryPercentText != null)
                StartCoroutine(PulseBatteryIndicator());
        }

        private IEnumerator PulseBatteryIndicator()
        {
            if (batteryPercentText == null) yield break;

            float duration = 0.3f;
            float elapsed = 0f;
            Color original = batteryPercentText.color;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.PingPong(elapsed / duration * 2f, 1f);
                batteryPercentText.color = Color.Lerp(original, ColBad, t);
                yield return null;
            }
            batteryPercentText.color = original;
        }

        private void OnDestroy()
        {
            try
            {
                if (messageCoroutine != null) { StopCoroutine(messageCoroutine); messageCoroutine = null; }
                if (overlayCanvas != null) { Destroy(overlayCanvas.gameObject); overlayCanvas = null; }

            }
            catch (Exception ex) { Debug.LogError("[NightVisionUI] OnDestroy failed: " + ex); }
        }
    }
}
