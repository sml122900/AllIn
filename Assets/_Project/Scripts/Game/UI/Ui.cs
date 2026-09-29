using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AllIn.Game.UI
{
    /// <summary>Korean pixel font (Galmuri, OFL) turned into dynamic TMP font assets at boot — no editor font work.</summary>
    public static class Fonts
    {
        public static TMP_FontAsset Regular { get; private set; }
        public static TMP_FontAsset Bold { get; private set; }

        public static void Ensure()
        {
            if (Regular != null) return;
            Regular = Create("Galmuri11");
            Bold = Create("Galmuri11-Bold") ?? Regular;
            if (Bold != Regular && Bold != null)
            {
                // a freshly created asset has no fallback list yet
                if (Bold.fallbackFontAssetTable == null) Bold.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                Bold.fallbackFontAssetTable.Add(Regular);
            }
        }

        static TMP_FontAsset Create(string resource)
        {
            var font = Resources.Load<Font>(resource);
            if (font == null)
            {
                Debug.LogError("[AllIn] font missing: Resources/" + resource);
                return null;
            }
            var asset = TMP_FontAsset.CreateFontAsset(font, 44, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic);
            asset.name = resource;
            return asset;
        }
    }

    public static class Palette
    {
        public static readonly Color Bg = new Color(0.07f, 0.07f, 0.09f, 1f);
        public static readonly Color Panel = new Color(0.11f, 0.11f, 0.14f, 0.97f);
        public static readonly Color PanelLight = new Color(0.17f, 0.17f, 0.21f, 1f);
        public static readonly Color Text = new Color(0.92f, 0.92f, 0.90f, 1f);
        public static readonly Color Dim = new Color(0.55f, 0.55f, 0.58f, 1f);
        public static readonly Color Up = new Color(0.30f, 0.85f, 0.45f, 1f);
        public static readonly Color Down = new Color(0.95f, 0.30f, 0.30f, 1f);
        public static readonly Color Accent = new Color(1f, 0.80f, 0.25f, 1f);
        public static readonly Color Button = new Color(0.24f, 0.25f, 0.31f, 1f);
        public static readonly Color ButtonOn = new Color(0.85f, 0.65f, 0.20f, 1f);
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color Body = new Color(0.35f, 0.70f, 0.95f, 1f);
        public static readonly Color Mental = new Color(0.80f, 0.50f, 0.95f, 1f);
    }

    /// <summary>Tiny code-only UGUI toolkit: everything is built at runtime, nothing lives in prefabs.</summary>
    public static class Ui
    {
        public static readonly Vector2 Reference = new Vector2(1280, 720);

        public static Canvas Canvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            return rt;
        }

        /// <summary>Anchors in 0..1 of the parent, then pixel offsets (reference-resolution pixels).</summary>
        public static RectTransform Place(this RectTransform rt, float minX, float minY, float maxX, float maxY,
                                          float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Fixed-size box centered on an anchor point.</summary>
        public static RectTransform Box(this RectTransform rt, float anchorX, float anchorY, float width, float height, float x = 0, float y = 0)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, anchorY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        public static void Stretch(RectTransform rt) => rt.Place(0, 0, 1, 1);

        public static Image Image(Transform parent, string name, Color color, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size = 22, Color? color = null,
                                           TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold ? Fonts.Bold : Fonts.Regular;
            t.fontSize = size;
            t.color = color ?? Palette.Text;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public sealed class Btn
        {
            public Button Button;
            public Image Image;
            public TextMeshProUGUI Label;
            public RectTransform Rect => (RectTransform)Button.transform;

            public bool Interactable
            {
                get => Button.interactable;
                set => Button.interactable = value;
            }

            public void SetOn(bool on) => Image.color = on ? Palette.ButtonOn : Palette.Button;
        }

        public static Btn Button(Transform parent, string label, Action onClick, float size = 24)
        {
            var img = Image(parent, "Button:" + label, Palette.Button, raycast: true);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            b.colors = colors;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Text(img.transform, label, size, align: TextAlignmentOptions.Center, bold: true);
            t.rectTransform.Place(0, 0, 1, 1, 6, 2, 6, 2);
            return new Btn { Button = b, Image = img, Label = t };
        }

        public static Slider Slider(Transform parent, Action<float> onChange)
        {
            var root = Rect(parent, "Slider");
            var bg = Image(root, "Background", Palette.PanelLight);
            bg.rectTransform.Place(0, 0.35f, 1, 0.65f);
            var fillArea = Rect(root, "Fill Area").Place(0, 0.35f, 1, 0.65f, 0, 0, 0, 0);
            var fill = Image(fillArea, "Fill", Palette.Accent);
            var handleArea = Rect(root, "Handle Area").Place(0, 0, 1, 1, 10, 0, 10, 0);
            var handle = Image(handleArea, "Handle", Palette.Text, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(20, 0);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = 0;
            s.maxValue = 1;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        /// <summary>A horizontal bar gauge 0..100 with a label.</summary>
        public sealed class Gauge
        {
            public RectTransform Root;
            Image _fill, _cap;
            TextMeshProUGUI _label;
            string _name;

            public static Gauge Create(Transform parent, string name, Color color)
            {
                var g = new Gauge { _name = name };
                g.Root = Rect(parent, "Gauge:" + name);
                var bg = Image(g.Root, "bg", Palette.PanelLight);
                g._fill = Image(bg.transform, "fill", color);
                g._cap = Image(bg.transform, "cap", new Color(0.03f, 0.03f, 0.04f, 0.9f)); // the part of the bar a shrunken maximum takes away
                g._cap.rectTransform.Place(1, 0, 1, 1);
                g._label = Text(g.Root, name, 20, align: TextAlignmentOptions.Center, bold: true);
                return g;
            }

            public void Set(int value, int max = 100)
            {
                float v = Mathf.Clamp01(value / 100f);
                _fill.rectTransform.Place(0, 0, v, 1);
                _cap.rectTransform.Place(Mathf.Clamp01(max / 100f), 0, 1, 1);
                _label.text = _name + " " + value + (max < 100 ? "/" + max : "");
            }
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
