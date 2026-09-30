using System;

namespace AllIn.Core
{
    // v0.5 E8: the four cars as placeholder pixel art (side view, facing right). Grid characters:
    //   B body  b body shade  W window  K tyre  k hub  L headlight  R tail light  D detail (trim / wing)
    public static class CarArt
    {
        static readonly string[][] Grids =
        {
            null,
            // 1: used kei car
            new[]
            {
                "....BBBBBBBB........",
                "...BWWWWWWWWB.......",
                "..BBWWWWWWWWBB......",
                ".BBBBBBBBBBBBBBB....",
                "RBBBBBBBBBBBBBBBBL..",
                "BBBBBBBBBBBBBBBBBBL.",
                "bbbbbbbbbbbbbbbbbbb.",
                ".KKK.........KKK....",
                "KKkKK.......KKkKK...",
                "KKkKK.......KKkKK...",
                ".KKK.........KKK....",
            },
            // 2: mid-size domestic sedan
            new[]
            {
                ".....BBBBBBBBBB.........",
                "....BWWWWWWWWWWB........",
                "...BBWWWWWWWWWWBB.......",
                "..BBBBBBBBBBBBBBBBBB....",
                "RBBBBBBBBBBBBBBBBBBBBL..",
                "BBBBBBBBBBBBBBBBBBBBBBL.",
                "bbbbbbbbbbbbbbbbbbbbbbb.",
                "..KKK...........KKK.....",
                ".KKkKK.........KKkKK....",
                ".KKkKK.........KKkKK....",
                "..KKK...........KKK.....",
            },
            // 3: imported sedan (long, chrome trim)
            new[]
            {
                "......BBBBBBBBBBB.........",
                ".....BWWWWWWWWWWWB........",
                "....BBWWWWWWWWWWWBBB......",
                "..BBBBBBBBBBBBBBBBBBBB....",
                "RBBBBBBBBBBBBBBBBBBBBBBL..",
                "BBBBBBBBBBBBBBBBBBBBBBBBL.",
                "DDDDDDDDDDDDDDDDDDDDDDDD..",
                "bbbbbbbbbbbbbbbbbbbbbbbb..",
                "..KKK.............KKK.....",
                ".KKkKK...........KKkKK....",
                ".KKkKK...........KKkKK....",
                "..KKK.............KKK.....",
            },
            // 4: supercar (low, wedge, wing)
            new[]
            {
                "DD..........................",
                "DD......BBBBBB..............",
                "DD.....BBWWWWWBB............",
                "BB....BBBWWWWWWBBB..........",
                "BBBBBBBBBBBBBBBBBBBBBBB.....",
                "RBBBBBBBBBBBBBBBBBBBBBBBBL..",
                "BBBBBBBBBBBBBBBBBBBBBBBBBBL.",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbb.",
                "..KKK...............KKK.....",
                ".KKkKK.............KKkKK....",
                ".KKkKK.............KKkKK....",
                "..KKK...............KKK.....",
            },
        };

        static readonly uint[] BodyColor = { 0, 0xFFC9CCD1, 0xFF3F6EA8, 0xFF23252B, 0xFFD62828 };
        const uint Tyre = 0xFF141416, Hub = 0xFF9A9AA0, Glass = 0xFF9FD3EE, Light = 0xFFFFE680, Tail = 0xFFD62828, Chrome = 0xFFE8E8EE;

        public static int Width(int tier)
        {
            int w = 0;
            foreach (var r in Grids[Clamp(tier)]) w = Math.Max(w, r.Length);
            return w;
        }

        public static int Height(int tier) => Grids[Clamp(tier)].Length;

        static int Clamp(int tier) => tier < 1 ? 1 : tier > Cars.Count ? Cars.Count : tier;

        static uint Darken(uint c, double k) =>
            0xFF000000u | ((uint)(((c >> 16) & 0xFF) * k) << 16) | ((uint)(((c >> 8) & 0xFF) * k) << 8) | (uint)((c & 0xFF) * k);

        /// <summary>Row-major pixels (row 0 = top), transparent = 0, size Width x Height of the tier.</summary>
        public static uint[] Render(int tier)
        {
            tier = Clamp(tier);
            var rows = Grids[tier];
            int w = Width(tier), h = rows.Length;
            var px = new uint[w * h];
            uint body = BodyColor[tier];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    switch (rows[y][x])
                    {
                        case 'B': px[y * w + x] = body; break;
                        case 'b': px[y * w + x] = Darken(body, 0.7); break;
                        case 'W': px[y * w + x] = Glass; break;
                        case 'K': px[y * w + x] = Tyre; break;
                        case 'k': px[y * w + x] = Hub; break;
                        case 'L': px[y * w + x] = Light; break;
                        case 'R': px[y * w + x] = Tail; break;
                        case 'D': px[y * w + x] = tier == 3 ? Chrome : Darken(body, 0.5); break;
                    }
                }
            return px;
        }
    }
}
