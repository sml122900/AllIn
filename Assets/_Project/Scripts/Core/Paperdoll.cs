using System;
using System.Collections.Generic;

namespace AllIn.Core
{
    // v0.5 E5: the protagonist as a layered paper doll, 32x32, defined entirely in code. Pure C# (no engine types): pixels are
    // packed 0xAARRGGBB uints, transparent = 0. The Game layer turns them into Texture2D with Point filtering.
    // ICharacterLayerSource is the seam: a PNG sprite-sheet source can replace CodeLayerSource later without touching Compose.

    public enum LayerKind { Body = 0, Bottom, Shoes, Top, Hair, Accessory }   // draw order, bottom first

    public enum DollPose { Idle = 0, Walk, Lie, Sit }

    public interface ICharacterLayerSource
    {
        /// <summary>32x32 row-major pixels of one layer (0 = transparent). `variant` is skin / hair / shape index; `tier` only matters for clothes.</summary>
        uint[] GetLayer(LayerKind kind, int variant, int tier, DollPose pose, int frame);
    }

    public static class PaperDoll
    {
        public const int Size = 32;

        public static int FrameCount(DollPose pose)
        {
            switch (pose)
            {
                case DollPose.Idle: return 2;
                case DollPose.Walk: return 4;
                default: return 1;
            }
        }

        /// <summary>Layers in draw order: body, bottom, shoes, top, hair, accessory. Same look + pose + frame -> the same pixels, always.</summary>
        public static uint[] Compose(ICharacterLayerSource source, Appearance a, DollPose pose, int frame)
        {
            frame = ((frame % FrameCount(pose)) + FrameCount(pose)) % FrameCount(pose);
            var px = new uint[Size * Size];
            Blit(px, source.GetLayer(LayerKind.Body, a.skin, 0, pose, frame));
            Blit(px, ClothLayer(source, LayerKind.Bottom, a.bottom, pose, frame));
            Blit(px, ClothLayer(source, LayerKind.Shoes, a.shoes, pose, frame));
            Blit(px, ClothLayer(source, LayerKind.Top, a.top, pose, frame));
            Blit(px, source.GetLayer(LayerKind.Hair, a.hair, 0, pose, frame));
            if (a.accessory >= 0) Blit(px, ClothLayer(source, LayerKind.Accessory, a.accessory, pose, frame));
            return px;
        }

        static uint[] ClothLayer(ICharacterLayerSource s, LayerKind kind, int id, DollPose pose, int frame) =>
            s.GetLayer(kind, Wardrobe.VariantOf(id), (int)Wardrobe.TierOf(id), pose, frame);

        static void Blit(uint[] dst, uint[] src)
        {
            for (int i = 0; i < dst.Length; i++) if ((src[i] >> 24) != 0) dst[i] = src[i];
        }
    }

    /// <summary>The placeholder art: character-grid strings + palettes (E5). See the legend on <see cref="Cells"/>.</summary>
    public sealed class CodeLayerSource : ICharacterLayerSource
    {
        public static readonly CodeLayerSource Instance = new CodeLayerSource();

        // ---------- cell legend ----------
        // Each grid character says what the *body* shows there (skin / shade / eye / mouth / nothing) and which clothing layers may paint over it.
        // A layer paints a cell when its variant "reaches" the cell's level (hair, top, bottom, shoes) — a short-sleeve tee reaches level 0 of Top,
        // a coat level 3 — so one grid serves every variant, and where a variant doesn't reach, the skin (or the layer below) shows through.
        enum Base { None, Skin, Shade, Eye, Mouth }

        struct Over
        {
            public LayerKind Kind; public int Level; public int Slot;   // slot: 0 main colour, 1 unused, 2 shade / trim
            public Over(LayerKind k, int level, int slot) { Kind = k; Level = level; Slot = slot; }
        }

        sealed class Cell
        {
            public Base Base;
            public Over[] Overs;
            public Cell(Base b, params Over[] o) { Base = b; Overs = o; }
        }

        static readonly Dictionary<char, Cell> Cells = new Dictionary<char, Cell>
        {
            ['s'] = new Cell(Base.Skin),
            ['S'] = new Cell(Base.Shade),
            ['e'] = new Cell(Base.Eye),
            ['m'] = new Cell(Base.Mouth),
            ['h'] = new Cell(Base.Skin, new Over(LayerKind.Hair, 0, 0)),
            ['i'] = new Cell(Base.Skin, new Over(LayerKind.Hair, 1, 0)),
            ['j'] = new Cell(Base.None, new Over(LayerKind.Hair, 2, 0)),
            ['t'] = new Cell(Base.Skin, new Over(LayerKind.Top, 0, 0)),
            ['T'] = new Cell(Base.Skin, new Over(LayerKind.Top, 0, 2)),
            ['u'] = new Cell(Base.Skin, new Over(LayerKind.Top, 1, 0)),
            ['V'] = new Cell(Base.Skin, new Over(LayerKind.Bottom, 0, 0), new Over(LayerKind.Top, 2, 0)),
            ['W'] = new Cell(Base.Skin, new Over(LayerKind.Bottom, 0, 0), new Over(LayerKind.Top, 3, 0)),
            ['p'] = new Cell(Base.Skin, new Over(LayerKind.Bottom, 0, 0)),
            ['q'] = new Cell(Base.Skin, new Over(LayerKind.Bottom, 1, 0)),
            ['g'] = new Cell(Base.Skin, new Over(LayerKind.Bottom, 1, 0), new Over(LayerKind.Shoes, 1, 0)),
            ['f'] = new Cell(Base.Skin, new Over(LayerKind.Shoes, 0, 0)),
        };

        // how far each shape reaches (level index it covers, inclusive)
        static readonly int[] HairReach = { 0, 1, 2 };          // short / medium / long
        static readonly int[] TopReach = { 1, 0, 1, 2, 3 };     // uniform (long sleeve) / tee / hoodie / jacket / coat
        static readonly int[] BottomReach = { 1, 0, 1 };        // fatigues (full) / shorts / slacks (full)
        static readonly int[] ShoesReach = { 1, 0 };            // boots (shaft) / sneakers

        static int Reach(LayerKind k, int variant)
        {
            switch (k)
            {
                case LayerKind.Hair: return HairReach[Clamp(variant, HairReach.Length)];
                case LayerKind.Top: return TopReach[Clamp(variant, TopReach.Length)];
                case LayerKind.Bottom: return BottomReach[Clamp(variant, BottomReach.Length)];
                default: return ShoesReach[Clamp(variant, ShoesReach.Length)];
            }
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        // ---------- the art ----------
        // Rows are "x:cells" (x = first column inside the 16-wide sprite; '.' = empty). Front view for Idle, side view (facing right) for the rest.

        static readonly string[] FrontRows =
        {
            "4:hhhhhhhh",
            "3:hhhhhhhhhh",
            "3:hhhhhhhhhh",
            "2:jhhissssihhj",
            "2:jssessssessj",
            "2:jssssssssssj",
            "2:jssssmmssssj",
            "3:jssssssssj",
            "3:j..SssS..j",
            "2:tttttttttttt",
            "2:tttttttttttt",
            "2:uuTttttttTuu",
            "2:uuTttttttTuu",
            "2:uuTttttttTuu",
            "2:uuTttttttTuu",
            "2:uuTttttttTuu",
            "2:ssTttttttTss",
            "2:ssTVVVVVVTss",
            "4:WWWWWWWW",
            "4:WWWWWWWW",
            "4:pppppppp",
            "4:ppp..ppp",
            "4:ppp..ppp",
            "4:qqq..qqq",
            "4:qqq..qqq",
            "4:qqq..qqq",
            "4:qqq..qqq",
            "4:ggg..ggg",
            "3:ffff..ffff",
            "3:ffff..ffff",
        };

        // side view, rows 0..19 (head, torso, hips); the legs come from LegRows / SitLegs
        static readonly string[] SideUpper =
        {
            "5:hhhhhhh",
            "4:hhhhhhhhh",
            "4:hhhhhhhhh",
            "3:jhhisssssh",
            "3:jssssssses",
            "3:jssssssssS",
            "3:jsssssssmm",
            "4:jsssssss",
            "5:jSssS",
            "5:ttttttt",
            "5:ttttttt",
            "5:tttuutt",
            "5:tttuutt",
            "5:tttuutt",
            "5:tttuutt",
            "5:tttuutt",
            "5:tttssTt",
            "5:VVVVVVV",
            "5:WWWWWWW",
            "5:WWWWWWW",
        };

        static readonly string[] SitLegs =
        {
            "5:ppppppppppp",      // 20
            "5:ppppppppppp",      // 21
            "11:qqq",             // 22
            "11:qqq",             // 23
            "11:ggg",             // 24
            "10:ffffff",          // 25
            "10:ffffff",          // 26
        };

        // accessory overlays (sparse: row -> "x:cells"; 'w' watch, 'B' strap, 'k' bag body)
        static readonly Dictionary<int, string> FrontAcc = new Dictionary<int, string>
        {
            [10] = "4:B", [11] = "5:B", [12] = "6:B", [13] = "7:B", [14] = "8:B", [15] = "9:B", [16] = "2:w.......B",
            [17] = "11:kkk", [18] = "11:kkk", [19] = "11:kkk",
        };

        static readonly Dictionary<int, string> SideAcc = new Dictionary<int, string>
        {
            [10] = "6:B", [11] = "7:B", [12] = "8:B", [13] = "9:B", [14] = "10:B", [16] = "8:w",
            [15] = "3:kkk", [17] = "3:kkk", [18] = "3:kkk", [19] = "3:kkk",
        };

        // (xBack, xFront, liftBack, liftFront) per walk frame: wide, back foot passing, wide, front foot passing
        static readonly int[][] WalkLegs =
        {
            new[] { 4, 9, 0, 0 },
            new[] { 6, 9, 1, 0 },
            new[] { 4, 9, 0, 0 },
            new[] { 4, 7, 0, 1 },
        };

        // ---------- grid building ----------
        const int OriginX = 8, OriginY = 2;

        static readonly Dictionary<string, char[]> GridCache = new Dictionary<string, char[]>();
        static readonly Dictionary<string, char[]> AccCache = new Dictionary<string, char[]>();

        static void PutRow(char[] canvas, int y, string spec, int ox, int oy)
        {
            int colon = spec.IndexOf(':');
            int x0 = int.Parse(spec.Substring(0, colon));
            for (int i = 0; i < spec.Length - colon - 1; i++)
            {
                char c = spec[colon + 1 + i];
                if (c == '.') continue;
                int cx = ox + x0 + i, cy = oy + y;
                if (cx < 0 || cx >= PaperDoll.Size || cy < 0 || cy >= PaperDoll.Size) continue;
                canvas[cy * PaperDoll.Size + cx] = c;
            }
        }

        static char[] Empty() { var c = new char[PaperDoll.Size * PaperDoll.Size]; for (int i = 0; i < c.Length; i++) c[i] = '.'; return c; }

        static void SetLeg(char[] canvas, int x, int lift, bool front)
        {
            // pants rows 21..26, shaft 27, foot 28..29; a lifted foot moves up one row (and its shaft)
            for (int r = 21; r <= 22; r++) for (int i = 0; i < 3; i++) Plot(canvas, x + i, r, 'p');
            for (int r = 23; r <= 26 - lift; r++) for (int i = 0; i < 3; i++) Plot(canvas, x + i, r, 'q');
            for (int i = 0; i < 3; i++) Plot(canvas, x + i, 27 - lift, 'g');
            for (int r = 28 - lift; r <= 29 - lift; r++) for (int i = 0; i < 4; i++) Plot(canvas, x + i, r, 'f');
        }

        static void Plot(char[] canvas, int x, int y, char c)
        {
            int cx = OriginX + x, cy = OriginY + y;
            if (cx >= 0 && cx < PaperDoll.Size && cy >= 0 && cy < PaperDoll.Size) canvas[cy * PaperDoll.Size + cx] = c;
        }

        static char[] BuildGrid(DollPose pose, int frame)
        {
            var g = Empty();
            switch (pose)
            {
                case DollPose.Idle:
                    {
                        var rows = FrontRows;
                        int bob = frame == 1 ? 1 : 0;                 // breathing: everything above the hips sinks one pixel
                        for (int y = 0; y < rows.Length; y++)
                        {
                            if (bob == 1 && y == 19) continue;        // the hem row that the bob eats
                            int dy = bob == 1 && y < 19 ? 1 : 0;
                            PutRow(g, y + dy, rows[y], OriginX, OriginY);
                        }
                        break;
                    }
                case DollPose.Walk:
                    {
                        for (int y = 0; y < SideUpper.Length; y++) PutRow(g, y, SideUpper[y], OriginX, OriginY);
                        PutRow(g, 20, "5:ppppppp", OriginX, OriginY);
                        var l = WalkLegs[frame % WalkLegs.Length];
                        SetLeg(g, l[0], l[2], false);
                        SetLeg(g, l[1], l[3], true);
                        break;
                    }
                case DollPose.Sit:
                    {
                        for (int y = 0; y < SideUpper.Length; y++) PutRow(g, y, SideUpper[y], OriginX, OriginY);
                        for (int y = 0; y < SitLegs.Length; y++) PutRow(g, 20 + y, SitLegs[y], OriginX, OriginY);
                        break;
                    }
                default: // Lie: the standing side view turned onto its back
                    return Rotate(BuildGrid(DollPose.Walk, 2));
            }
            return g;
        }

        static char[] BuildAcc(DollPose pose, int frame)
        {
            var g = Empty();
            switch (pose)
            {
                case DollPose.Idle:
                    {
                        int bob = frame == 1 ? 1 : 0;
                        foreach (var kv in FrontAcc)
                        {
                            if (bob == 1 && kv.Key == 19) continue;
                            PutRow(g, kv.Key + (bob == 1 && kv.Key < 19 ? 1 : 0), kv.Value, OriginX, OriginY);
                        }
                        break;
                    }
                case DollPose.Walk:
                case DollPose.Sit:
                    foreach (var kv in SideAcc) PutRow(g, kv.Key, kv.Value, OriginX, OriginY);
                    break;
                default:
                    {
                        var side = Empty();
                        foreach (var kv in SideAcc) PutRow(side, kv.Key, kv.Value, OriginX, OriginY);
                        return RotateWith(side, BuildGrid(DollPose.Walk, 2));
                    }
            }
            return g;
        }

        static char[] Rotate(char[] src) => RotateWith(src, src);

        /// <summary>Lays the figure on its back (head to the left, face up) with its feet-side bounding box resting on the canvas floor.
        /// `reference` decides the offsets, so the accessory overlay lines up with the body.</summary>
        static char[] RotateWith(char[] src, char[] reference)
        {
            int n = PaperDoll.Size;
            int minX = n, maxX = -1, minY = n, maxY = -1;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    if (reference[y * n + x] != '.') { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            var dst = Empty();
            // (x, y) -> (x' = y - minY + 1, y' = (n - 1) - (x - minX))
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    char c = src[y * n + x];
                    if (c == '.') continue;
                    int nx = y - minY + 1, ny = (n - 1) - (x - minX);
                    if (nx >= 0 && nx < n && ny >= 0 && ny < n) dst[ny * n + nx] = c;
                }
            return dst;
        }

        static char[] Grid(DollPose pose, int frame)
        {
            string key = pose + ":" + frame;
            lock (GridCache)
            {
                if (!GridCache.TryGetValue(key, out var g)) GridCache[key] = g = BuildGrid(pose, frame);
                return g;
            }
        }

        static char[] AccGrid(DollPose pose, int frame)
        {
            string key = pose + ":" + frame;
            lock (AccCache)
            {
                if (!AccCache.TryGetValue(key, out var g)) AccCache[key] = g = BuildAcc(pose, frame);
                return g;
            }
        }

        // ---------- palettes (0xAARRGGBB) ----------
        static readonly uint[][] SkinPal = { new uint[] { 0xFFF0C8A0, 0xFFD6A87E }, new uint[] { 0xFFD9A066, 0xFFB9804A }, new uint[] { 0xFF8D5A3C, 0xFF6E4229 } };
        static readonly uint[] MouthPal = { 0xFFB0524A, 0xFFA04A3C, 0xFF7A3A30 };
        const uint EyeColor = 0xFF2A1E1A;
        static readonly uint[] HairPal = { 0xFF2B2B2E, 0xFF6B4A2B, 0xFF3B2F5C };
        const uint Gold = 0xFFD4AF37;

        static readonly uint[][] TopPal =
        {
            new uint[] { 0xFF5B6B3A, 0xFF9AA0A6, 0xFF2F3E5C, 0xFF8A7A54, 0xFF3A3A40 },      // basic
            new uint[] { 0xFFD94F3D, 0xFF3D7BD9, 0xFFE3B341, 0xFF3DBFA0, 0xFFB04FD9 },      // brand
            new uint[] { 0xFF1B1B22, 0xFF5A1830, 0xFF0E2A3F, 0xFF2A2A2E, 0xFF3A1E5A },      // luxury (gold trim)
        };
        static readonly uint[][] BottomPal =
        {
            new uint[] { 0xFF4A5530, 0xFFB09A6A, 0xFF55575C },
            new uint[] { 0xFF2E4A78, 0xFFC98A3E, 0xFF3A3A48 },
            new uint[] { 0xFF15151A, 0xFFE8DCC0, 0xFF101B33 },
        };
        static readonly uint[][] ShoesPal =
        {
            new uint[] { 0xFF3B2A1C, 0xFFCFCFCF },
            new uint[] { 0xFF5A3A24, 0xFFFFFFFF },
            new uint[] { 0xFF0E0E10, 0xFFF2F0EA },
        };
        static readonly uint[][] AccPal =
        {
            new uint[] { 0xFFB0B0B8, 0xFF6B5A3A },
            new uint[] { 0xFF6FA8DC, 0xFF3D3D46 },
            new uint[] { Gold, Gold },
        };

        static uint Darken(uint c, double k)
        {
            uint r = (uint)(((c >> 16) & 0xFF) * k), g = (uint)(((c >> 8) & 0xFF) * k), b = (uint)((c & 0xFF) * k);
            return 0xFF000000u | (r << 16) | (g << 8) | b;
        }

        static uint ClothColor(LayerKind kind, int variant, int tier, int slot)
        {
            tier = Clamp(tier, 3);
            uint main;
            switch (kind)
            {
                case LayerKind.Top: main = TopPal[tier][Clamp(variant, TopPal[tier].Length)]; break;
                case LayerKind.Bottom: main = BottomPal[tier][Clamp(variant, BottomPal[tier].Length)]; break;
                case LayerKind.Shoes: main = ShoesPal[tier][Clamp(variant, ShoesPal[tier].Length)]; break;
                default: main = AccPal[tier][Clamp(variant, AccPal[tier].Length)]; break;
            }
            if (slot == 2) return tier == 2 ? Gold : Darken(main, 0.72);
            return main;
        }

        // ---------- ICharacterLayerSource ----------
        public uint[] GetLayer(LayerKind kind, int variant, int tier, DollPose pose, int frame)
        {
            var px = new uint[PaperDoll.Size * PaperDoll.Size];
            var grid = Grid(pose, frame);
            if (kind == LayerKind.Accessory)
            {
                var acc = AccGrid(pose, frame);
                for (int i = 0; i < acc.Length; i++)
                {
                    char c = acc[i];
                    if (c == '.') continue;
                    bool watch = c == 'w', strap = c == 'B', body = c == 'k';
                    if (variant == 0 && watch) px[i] = ClothColor(LayerKind.Accessory, 0, tier, 0);
                    else if (variant == 1 && strap) px[i] = ClothColor(LayerKind.Accessory, 1, tier, 2) == Gold ? Gold : Darken(ClothColor(LayerKind.Accessory, 1, tier, 0), 0.7);
                    else if (variant == 1 && body) px[i] = ClothColor(LayerKind.Accessory, 1, tier, 0);
                }
                return px;
            }

            int skin = kind == LayerKind.Body ? Clamp(variant, SkinPal.Length) : 0;
            int reach = kind == LayerKind.Body ? 0 : Reach(kind, variant);
            for (int i = 0; i < grid.Length; i++)
            {
                char c = grid[i];
                if (c == '.' || !Cells.TryGetValue(c, out var cell)) continue;
                if (kind == LayerKind.Body)
                {
                    switch (cell.Base)
                    {
                        case Base.Skin: px[i] = SkinPal[skin][0]; break;
                        case Base.Shade: px[i] = SkinPal[skin][1]; break;
                        case Base.Eye: px[i] = EyeColor; break;
                        case Base.Mouth: px[i] = MouthPal[skin]; break;
                    }
                    continue;
                }
                foreach (var o in cell.Overs)
                {
                    if (o.Kind != kind || o.Level > reach) continue;
                    if (kind == LayerKind.Hair) px[i] = HairPal[Clamp(variant, HairPal.Length)];
                    else px[i] = ClothColor(kind, variant, tier, o.Slot);
                }
            }
            return px;
        }
    }
}
