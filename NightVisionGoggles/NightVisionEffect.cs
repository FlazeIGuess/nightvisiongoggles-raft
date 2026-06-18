using System;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.NightVisionGoggles
{
    /// <summary>
    /// Night vision lens effects (scanlines + vignette) via lightweight UI overlay.
    /// The actual color/brightness manipulation is done by PostProcessing v1 (GPU shader)
    /// in NightVisionController — this class only handles the optical lens artifacts.
    /// </summary>
    public class NightVisionEffect : MonoBehaviour
    {
        // -- UI overlay -----------------------------------------------------------
        private Canvas overlayCanvas;
        private GameObject overlayRoot;
        private Image tintImage;
        private Image vignetteImage;

        // -- State ----------------------------------------------------------------
        private float fadeProgress = 0f;
        private bool isFadingIn = false;
        private bool isFadingOut = false;

        private const float FADE_IN_DURATION = 0.3f;
        private const float FADE_OUT_DURATION = 0.3f;

        // -- Mode-specific settings -----------------------------------------------
        private NightVisionMode currentMode = NightVisionMode.Standard;
        private float currentVignetteIntensity = 0.65f;

        private NightVisionController controller;

        private void Awake()
        {
            try
            {
                CreateOverlayUI();

            }
            catch (Exception ex)
            {
                Debug.LogError("[NightVisionEffect] Awake failed: " + ex);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (overlayRoot != null) { Destroy(overlayRoot); overlayRoot = null; }

            }
            catch (Exception ex) { Debug.LogError("[NightVisionEffect] OnDestroy failed: " + ex); }
        }

        public void Initialize(NightVisionController controller)
        {
            this.controller = controller;
            if (controller != null) SetMode(controller.CurrentMode);
            FadeIn();

        }

        // ========================================================================
        // UI OVERLAY (tint + scanlines + vignette)
        // ========================================================================

        private void CreateOverlayUI()
        {
            GameObject canvasGO = GameObject.Find("Canvases/_CanvasGame_New");
            if (canvasGO == null)
            {
                Canvas[] all = FindObjectsOfType<Canvas>();
                foreach (Canvas c in all)
                    if (c.enabled && c.gameObject.activeInHierarchy && c.renderMode == RenderMode.ScreenSpaceOverlay)
                        { canvasGO = c.gameObject; break; }
            }

            if (canvasGO == null)
            {
                overlayRoot = new GameObject("NightVisionOverlay");
                overlayCanvas = overlayRoot.AddComponent<Canvas>();
                overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                overlayCanvas.sortingOrder = 30000;
                CanvasScaler scaler = overlayRoot.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                overlayRoot.AddComponent<GraphicRaycaster>();
            }
            else
            {
                overlayRoot = new GameObject("NightVisionOverlay");
                overlayRoot.transform.SetParent(canvasGO.transform, false);

                // SetAsFirstSibling: NV overlay BEHIND game UI (hotbar, health, etc.)
                overlayRoot.transform.SetAsFirstSibling();

                RectTransform overlayRect = overlayRoot.AddComponent<RectTransform>();
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
            }

            // Bottom: scanlines, Top: vignette
            CreateTintLayer(overlayRoot.transform);
            CreateVignetteLayer(overlayRoot.transform);

            overlayRoot.SetActive(false);
        }

        private void CreateTintLayer(Transform parent)
        {
            GameObject go = new GameObject("TintLayer");
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;

            tintImage = go.AddComponent<Image>();
            tintImage.color = new Color(0f, 0.8f, 0.1f, 0f);
            tintImage.raycastTarget = false;
        }

        private void CreateVignetteLayer(Transform parent)
        {
            GameObject go = new GameObject("VignetteLayer");
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;

            vignetteImage = go.AddComponent<Image>();
            vignetteImage.color = Color.white;
            vignetteImage.raycastTarget = false;
            vignetteImage.sprite = CreateVignetteSprite();
        }

        private Sprite CreateVignetteSprite()
        {
            int s = 256;
            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color[] p = new Color[s * s];
            float c = s / 2f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    p[y * s + x] = new Color(0f, 0f, 0f, Mathf.Clamp01(d * d));
                }
            tex.SetPixels(p); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        }

        // ========================================================================
        // FADE & MODE
        // ========================================================================

        private void Update()
        {
            try
            {
                if (overlayRoot == null) return;

                if (isFadingIn)
                {
                    fadeProgress += Time.deltaTime / FADE_IN_DURATION;
                    if (fadeProgress >= 1f) { fadeProgress = 1f; isFadingIn = false; }
                }
                if (isFadingOut)
                {
                    fadeProgress -= Time.deltaTime / FADE_OUT_DURATION;
                    if (fadeProgress <= 0f) { fadeProgress = 0f; isFadingOut = false; overlayRoot.SetActive(false); }
                }

                if (tintImage != null)
                    tintImage.color = new Color(0f, 0.8f, 0.1f, 0.18f * fadeProgress);
                if (vignetteImage != null)
                    vignetteImage.color = new Color(1f, 1f, 1f, currentVignetteIntensity * fadeProgress);
            }
            catch (Exception ex) { Debug.LogError("[NightVisionEffect] Update failed: " + ex); }
        }

        public void SetMode(NightVisionMode mode)
        {
            try
            {
                currentMode = mode;
                switch (mode)
                {
                    case NightVisionMode.Standard:
                        currentVignetteIntensity = 0.65f;
                        break;
                    case NightVisionMode.Bright:
                        currentVignetteIntensity = 0.60f;
                        break;
                    case NightVisionMode.Thermal:
                        currentVignetteIntensity = 0.70f;
                        break;
                    default:
                        currentVignetteIntensity = 0.65f;
                        break;
                }

            }
            catch (Exception ex) { Debug.LogError("[NightVisionEffect] SetMode failed: " + ex); }
        }

        public void FadeIn()
        {
            if (overlayRoot == null) return;
            overlayRoot.SetActive(true);
            isFadingIn = true; isFadingOut = false;
            fadeProgress = 0f;

        }

        public void FadeOut()
        {
            isFadingOut = true; isFadingIn = false;
            fadeProgress = 1f;

        }
    }
}
