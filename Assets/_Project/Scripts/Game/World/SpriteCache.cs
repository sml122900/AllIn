using System.Collections.Generic;
using AllIn.Core;
using UnityEngine;

namespace AllIn.Game.World
{
    /// <summary>Turns Core's packed-pixel paper dolls and cars into Point-filtered sprites (32x32 / car-sized, 1 pixel = 1 unit, pivot at the feet).</summary>
    public static class SpriteCache
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static Sprite _pixel;

        /// <summary>The paper-doll source; swap for a PNG-sheet source later (ICharacterLayerSource).</summary>
        public static ICharacterLayerSource Source = CodeLayerSource.Instance;

        public static Sprite Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    _pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
                }
                return _pixel;
            }
        }

        public static Sprite Person(Appearance a, DollPose pose, int frame)
        {
            frame = ((frame % PaperDoll.FrameCount(pose)) + PaperDoll.FrameCount(pose)) % PaperDoll.FrameCount(pose);
            string key = "p" + a.skin + "," + a.hair + "," + a.top + "," + a.bottom + "," + a.shoes + "," + a.accessory + "," + (int)pose + "," + frame;
            if (!Cache.TryGetValue(key, out var s))
            {
                var px = PaperDoll.Compose(Source, a, pose, frame);
                Cache[key] = s = FromPixels(px, PaperDoll.Size, PaperDoll.Size, new Vector2(0.5f, 0f), "person " + key);
            }
            return s;
        }

        public static Sprite Car(int tier)
        {
            string key = "c" + tier;
            if (!Cache.TryGetValue(key, out var s))
                Cache[key] = s = FromPixels(CarArt.Render(tier), CarArt.Width(tier), CarArt.Height(tier), new Vector2(0.5f, 0f), "car " + tier);
            return s;
        }

        static Sprite FromPixels(uint[] px, int w, int h, Vector2 pivot, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            var colors = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint c = px[y * w + x];
                    colors[(h - 1 - y) * w + x] = new Color32((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF), (byte)(c >> 24));
                }
            tex.SetPixels32(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, 1);
        }
    }
}
