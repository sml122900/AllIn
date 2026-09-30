using System.Collections;
using AllIn.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AllIn.Game.UI
{
    /// <summary>
    /// E1 "무조건 승리": the gold button that appears without warning and must be pressed to go on (the act of pressing is the
    /// ritual). Shared by the watch scene and the hold'em table. Pure presentation: whether it comes up is decided in Core.
    /// </summary>
    public sealed class LuckOffer
    {
        readonly GameObject _root;
        readonly Image _glow, _tint, _burst;
        readonly RectTransform _button;
        readonly TextMeshProUGUI _caption;
        bool _pressed;

        /// <summary>The offer on screen right now (null when none) — the dev capture screenshots it, then presses it.</summary>
        public static LuckOffer Current { get; private set; }

        LuckOffer(Transform parent, float anchorX, float anchorY, float x, float y, float width)
        {
            var rt = Ui.Rect(parent, "LuckOffer");
            _root = rt.gameObject;
            _tint = Ui.Image(rt, "Tint", new Color(1f, 0.82f, 0.25f, 0.0f));
            _burst = Ui.Image(rt, "Burst", new Color(1f, 0.9f, 0.4f, 0f));
            _burst.rectTransform.Box(anchorX, anchorY, width * 1.9f, 60, x, y);
            _glow = Ui.Image(rt, "Glow", new Color(1f, 0.85f, 0.3f, 0.5f));
            _glow.rectTransform.Box(anchorX, anchorY, width + 44, 150, x, y);
            var btn = Ui.Button(rt, Texts.LuckButton, () => _pressed = true, 40);
            btn.Image.color = new Color(1f, 0.80f, 0.20f, 1f);
            btn.Label.color = new Color(0.20f, 0.12f, 0.02f, 1f);
            btn.Rect.Box(anchorX, anchorY, width, 106, x, y);
            _button = btn.Rect;
            _caption = Ui.Text(rt, "", 24, new Color(1f, 0.9f, 0.5f, 1f), TextAlignmentOptions.Center, true);
            _caption.rectTransform.Box(anchorX, anchorY, width + 200, 40, x, y - 92);
            Current = this;
        }

        public static LuckOffer Show(Transform parent, float anchorX = 0.5f, float anchorY = 0.5f, float x = 0, float y = 0, float width = 460)
            => new LuckOffer(parent, anchorX, anchorY, x, y, width);

        public bool Pressed => _pressed;

        public void Press() => _pressed = true;

        /// <summary>Pulses until pressed, calls `onPressed` at that moment, then flashes and goes away. `caption` shows under the button after the press.</summary>
        public IEnumerator Run(string caption, System.Action onPressed = null)
        {
            float t = 0;
            while (!_pressed)
            {
                t += Time.unscaledDeltaTime;
                float s = 0.5f + 0.5f * Mathf.Sin(t * 6f);
                _glow.color = new Color(1f, 0.85f, 0.3f, 0.25f + 0.45f * s);
                _tint.color = new Color(1f, 0.82f, 0.25f, 0.05f + 0.07f * s);
                _button.localScale = Vector3.one * (1f + 0.035f * s);
                yield return null;
            }
            onPressed?.Invoke();
            _caption.text = caption;
            for (float f = 0; f < 0.9f; f += Time.unscaledDeltaTime)
            {
                float k = f / 0.9f;
                _burst.color = new Color(1f, 0.92f, 0.5f, 0.85f * (1 - k));
                _burst.rectTransform.sizeDelta = new Vector2(_burst.rectTransform.sizeDelta.x, 60 + 420 * k);
                _glow.color = new Color(1f, 0.9f, 0.4f, 0.8f * (1 - k));
                _tint.color = new Color(1f, 0.9f, 0.5f, 0.35f * (1 - k));
                _button.localScale = Vector3.one * (1f + 0.25f * k);
                yield return null;
            }
            if (Current == this) Current = null;
            Object.Destroy(_root);
        }
    }
}
