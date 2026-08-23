#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassLibrary4
{
    /// <summary>
    /// STEP29A.3 - World-space tiling planner for large MEP drawings.
    ///
    /// Responsibilities:
    /// - Partition world-space geometry into overlapping, populated tiles.
    /// - Never rasterize and never classify by itself.
    /// - Keep the type AutoCAD-independent so the same tiling plan can be reused
    ///   by OpenCV now and YOLO detector in STEP29B.
    ///
    /// Safety/performance:
    /// - Creates only populated tiles (no full empty grid allocation).
    /// - De-duplicates tiles containing the same item set.
    /// - Marks overly dense tiles instead of forcing expensive CV work.
    /// - Caps the number of tiles returned for one scan.
    /// </summary>
    internal sealed class MepOpenCvWorldTilingEngine
    {
        internal sealed class WorldItem
        {
            public int Index { get; set; }
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }

            public double CenterX => (MinX + MaxX) * 0.5;
            public double CenterY => (MinY + MaxY) * 0.5;
            public double Span => Math.Max(Math.Abs(MaxX - MinX), Math.Abs(MaxY - MinY));
        }

        internal sealed class TileWindow
        {
            public long GridX { get; set; }
            public long GridY { get; set; }
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public bool IsDense { get; set; }
            public List<int> ItemIndexes { get; set; } = new List<int>();

            public int ItemCount => ItemIndexes?.Count ?? 0;
        }

        internal sealed class Layout
        {
            public List<TileWindow> Tiles { get; set; } = new List<TileWindow>();
            public int InputItemCount { get; set; }
            public int PopulatedTileCount { get; set; }
            public int DenseTileCount { get; set; }
            public int DuplicateTileCount { get; set; }
            public int TruncatedTileCount { get; set; }
            public double TileSize { get; set; }
            public double Step { get; set; }
            public string Message { get; set; } = "";
        }

        private sealed class TileBuilder
        {
            public long GridX { get; set; }
            public long GridY { get; set; }
            public HashSet<int> ItemIndexes { get; } = new HashSet<int>();
        }

        public Layout BuildTiles(
            IEnumerable<WorldItem> source,
            double requestedTileSize = 0.0,
            double overlapRatio = 0.20,
            int minItemsPerTile = 1,
            int maxItemsPerTile = 96,
            int maxTiles = 160)
        {
            Layout result = new Layout();

            List<WorldItem> items = (source ?? Enumerable.Empty<WorldItem>())
                .Where(IsValidItem)
                .ToList();

            result.InputItemCount = items.Count;

            if (items.Count == 0)
            {
                result.Message = "Không có geometry hợp lệ để chia world tile.";
                return result;
            }

            overlapRatio = Clamp(overlapRatio, 0.05, 0.35);
            minItemsPerTile = Math.Max(1, minItemsPerTile);
            maxItemsPerTile = Math.Max(minItemsPerTile, maxItemsPerTile);
            maxTiles = Math.Max(1, Math.Min(512, maxTiles));

            double tileSize = requestedTileSize > 1e-6
                ? requestedTileSize
                : RecommendTileSize(items);

            tileSize = Clamp(tileSize, 900.0, 4200.0);

            double step = tileSize * (1.0 - overlapRatio);
            if (step <= 1e-6)
                step = tileSize * 0.8;

            result.TileSize = tileSize;
            result.Step = step;

            Dictionary<(long X, long Y), TileBuilder> builders =
                new Dictionary<(long X, long Y), TileBuilder>();

            foreach (WorldItem item in items)
            {
                // A window with start S intersects [Min,Max] when:
                // S <= Max && S + tileSize >= Min.
                long ixMin = SafeFloorToLong((item.MinX - tileSize) / step) + 1;
                long ixMax = SafeFloorToLong(item.MaxX / step);
                long iyMin = SafeFloorToLong((item.MinY - tileSize) / step) + 1;
                long iyMax = SafeFloorToLong(item.MaxY / step);

                // Primitive đầu vào đã được BOCTACHUI lọc span dài. Clamp thêm
                // để dữ liệu lỗi/extents cực lớn không nổ số lượng tile.
                if (ixMax - ixMin > 6)
                {
                    long center = SafeFloorToLong(item.CenterX / step);
                    ixMin = center - 3;
                    ixMax = center + 3;
                }

                if (iyMax - iyMin > 6)
                {
                    long center = SafeFloorToLong(item.CenterY / step);
                    iyMin = center - 3;
                    iyMax = center + 3;
                }

                for (long ix = ixMin; ix <= ixMax; ix++)
                {
                    for (long iy = iyMin; iy <= iyMax; iy++)
                    {
                        double minX = ix * step;
                        double minY = iy * step;
                        double maxX = minX + tileSize;
                        double maxY = minY + tileSize;

                        if (!Intersects(item, minX, minY, maxX, maxY))
                            continue;

                        var key = (ix, iy);
                        if (!builders.TryGetValue(key, out TileBuilder builder))
                        {
                            builder = new TileBuilder
                            {
                                GridX = ix,
                                GridY = iy
                            };
                            builders[key] = builder;
                        }

                        builder.ItemIndexes.Add(item.Index);
                    }
                }
            }

            result.PopulatedTileCount = builders.Count;

            List<TileWindow> windows = new List<TileWindow>();
            HashSet<string> seenItemSets = new HashSet<string>(StringComparer.Ordinal);

            foreach (TileBuilder builder in builders.Values)
            {
                List<int> indexes = builder.ItemIndexes.OrderBy(x => x).ToList();
                if (indexes.Count < minItemsPerTile)
                    continue;

                string signature = string.Join(",", indexes);
                if (!seenItemSets.Add(signature))
                {
                    result.DuplicateTileCount++;
                    continue;
                }

                double minX = builder.GridX * step;
                double minY = builder.GridY * step;

                bool dense = indexes.Count > maxItemsPerTile;
                if (dense)
                    result.DenseTileCount++;

                windows.Add(new TileWindow
                {
                    GridX = builder.GridX,
                    GridY = builder.GridY,
                    MinX = minX,
                    MinY = minY,
                    MaxX = minX + tileSize,
                    MaxY = minY + tileSize,
                    IsDense = dense,
                    ItemIndexes = indexes
                });
            }

            // Ưu tiên tile vừa phải: symbol thường nằm trong tile ít geometry hơn.
            // Dense tile xếp cuối và BOCTACHUI sẽ bỏ qua CV pass.
            List<TileWindow> ordered = windows
                .OrderBy(x => x.IsDense ? 1 : 0)
                .ThenBy(x => Math.Abs(x.ItemCount - 18))
                .ThenBy(x => x.ItemCount)
                .ThenBy(x => x.GridX)
                .ThenBy(x => x.GridY)
                .ToList();

            if (ordered.Count > maxTiles)
            {
                result.TruncatedTileCount = ordered.Count - maxTiles;
                ordered = ordered.Take(maxTiles).ToList();
            }

            result.Tiles = ordered;
            result.Message =
                "STEP29A.3 tile=" + ordered.Count +
                ", populated=" + result.PopulatedTileCount +
                ", dense=" + result.DenseTileCount +
                ", duplicate=" + result.DuplicateTileCount +
                ", truncated=" + result.TruncatedTileCount +
                ", size=" + tileSize.ToString("0") + ".";

            return result;
        }

        private static double RecommendTileSize(List<WorldItem> items)
        {
            List<double> spans = items
                .Select(x => x.Span)
                .Where(x => x > 1e-6 && !double.IsNaN(x) && !double.IsInfinity(x))
                .OrderBy(x => x)
                .ToList();

            if (spans.Count == 0)
                return 2000.0;

            int index = (int)Math.Round((spans.Count - 1) * 0.75);
            index = Math.Max(0, Math.Min(spans.Count - 1, index));

            double p75 = spans[index];

            // CAD MEP trong project hiện dùng mm. Clamp vẫn giữ an toàn nếu
            // geometry nhỏ/lớn hơn bình thường.
            return Clamp(p75 * 10.0, 1600.0, 2800.0);
        }

        private static bool IsValidItem(WorldItem item)
        {
            if (item == null)
                return false;

            if (double.IsNaN(item.MinX) || double.IsNaN(item.MinY) ||
                double.IsNaN(item.MaxX) || double.IsNaN(item.MaxY) ||
                double.IsInfinity(item.MinX) || double.IsInfinity(item.MinY) ||
                double.IsInfinity(item.MaxX) || double.IsInfinity(item.MaxY))
            {
                return false;
            }

            return item.MaxX >= item.MinX && item.MaxY >= item.MinY;
        }

        private static bool Intersects(
            WorldItem item,
            double minX,
            double minY,
            double maxX,
            double maxY)
        {
            return item.MinX <= maxX &&
                   item.MaxX >= minX &&
                   item.MinY <= maxY &&
                   item.MaxY >= minY;
        }

        private static long SafeFloorToLong(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0L;

            if (value >= long.MaxValue)
                return long.MaxValue;

            if (value <= long.MinValue)
                return long.MinValue;

            return (long)Math.Floor(value);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
