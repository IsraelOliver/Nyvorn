using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Tissue;

bool previewOnly = args.Contains("--preview");
int[] seeds = previewOnly ? [1337] : [1337, 2468, 9001];
WorldSizePreset[] presets = previewOnly ? [WorldSizePreset.Medium] : [WorldSizePreset.Small, WorldSizePreset.Medium, WorldSizePreset.Large];

foreach (WorldSizePreset preset in presets)
{
    foreach (int seed in seeds)
    {
        WorldGenConfig config = WorldGenConfig.CreatePreset(preset, seed);
        WorldMap map = CreateSolidUndergroundMap(config);
        TissueGenerator generator = new(seed);
        TissueGenerationResult first = generator.Generate(map);
        TissueGenerationResult second = generator.Generate(map);

        Require(first.Stats.DeterministicHash == second.Stats.DeterministicHash, $"non-deterministic hash for {preset}/{seed}");
        Require(first.Stats.PointCount == second.Stats.PointCount, $"non-deterministic point count for {preset}/{seed}");
        Require(first.Stats.EdgeCount == second.Stats.EdgeCount, $"non-deterministic edge count for {preset}/{seed}");
        Require(first.Stats.DominantComponentRatio >= 0.95f, $"dominant component below 95% for {preset}/{seed}");
        Require(first.Network.Nodes.All(node => node.Position.Y >= map.Height * map.TileSize * 0.22f), $"node above underground boundary for {preset}/{seed}");
        Require(HasUniqueMainEdges(first.Network), $"duplicate main edge for {preset}/{seed}");
        Require(RasterContainsOnlySolidTiles(first.RasterizedField, map), $"raster contains air tile for {preset}/{seed}");

        Console.WriteLine(
            $"{preset,-6} seed={seed} points={first.Stats.PointCount} edges={first.Stats.EdgeCount} " +
            $"micro={first.Stats.MicroFilamentCount} component={first.Stats.DominantComponentRatio:P1} " +
            $"degree={first.Stats.MinDegree}/{first.Stats.AverageDegree:0.00}/{first.Stats.MaxDegree} " +
            $"vertical={first.Stats.UpperPointCount}/{first.Stats.MiddlePointCount}/{first.Stats.DeepPointCount} " +
            $"hash={first.Stats.DeterministicHash:X16}");

        if (previewOnly)
        {
            string previewPath = Path.Combine(Path.GetTempPath(), "nyvorn-tissue-web-preview.ppm");
            WritePreview(first.Network, map, previewPath);
            Console.WriteLine(previewPath);
        }
    }
}

static WorldMap CreateSolidUndergroundMap(WorldGenConfig config)
{
    WorldMap map = new(config.WorldWidth, config.WorldHeight, config.TileSize);
    byte[] snapshot = new byte[config.WorldWidth * config.WorldHeight];
    int solidStart = (int)MathF.Floor(config.WorldHeight * 0.22f);
    for (int y = solidStart; y < config.WorldHeight; y++)
    {
        int rowStart = y * config.WorldWidth;
        for (int x = 0; x < config.WorldWidth; x++)
            snapshot[rowStart + x] = (byte)TileType.Stone;
    }
    map.ImportTileSnapshot(snapshot);
    return map;
}

static bool HasUniqueMainEdges(TissueNetwork network)
{
    HashSet<long> keys = new();
    foreach (TissueBranch branch in network.Branches)
    {
        if (branch.Kind != TissueBranch.TissueBranchKind.Main)
            continue;
        int min = Math.Min(branch.StartNodeId, branch.EndNodeId);
        int max = Math.Max(branch.StartNodeId, branch.EndNodeId);
        if (!keys.Add(((long)min << 32) | (uint)max))
            return false;
    }
    return true;
}

static bool RasterContainsOnlySolidTiles(TissueField field, WorldMap map)
{
    for (int y = 0; y < field.Height; y++)
    {
        for (int x = 0; x < field.Width; x++)
        {
            if (field.HasTissue(x, y) && !map.IsSolidAt(x, y))
                return false;
        }
    }
    return true;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void WritePreview(TissueNetwork network, WorldMap map, string path)
{
    const int width = 1200;
    int height = Math.Max(1, (int)MathF.Round(width * (map.Height / (float)map.Width)));
    byte[] pixels = new byte[width * height * 3];

    foreach (TissueBranch branch in network.Branches)
    {
        byte red = branch.Kind == TissueBranch.TissueBranchKind.Micro ? (byte)112 : (byte)218;
        byte green = branch.Kind == TissueBranch.TissueBranchKind.Micro ? (byte)25 : (byte)55;
        byte blue = branch.Kind == TissueBranch.TissueBranchKind.Micro ? (byte)155 : (byte)205;
        for (int i = 0; i < branch.Points.Count - 1; i++)
        {
            Point a = Map(branch.Points[i]);
            Point b = Map(branch.Points[i + 1]);
            DrawLine(a, b, red, green, blue);
        }
    }

    foreach (TissueNode node in network.Nodes)
    {
        if (!node.IsPrimary)
            continue;
        Point center = Map(node.Position);
        int radius = Math.Clamp(1 + node.Degree / 3, 2, 4);
        for (int y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (int x = center.X - radius; x <= center.X + radius; x++)
            {
                int dx = x - center.X;
                int dy = y - center.Y;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                    SetPixel(x, y, 255, 205, 82);
            }
        }
    }

    using FileStream stream = File.Create(path);
    using BinaryWriter writer = new(stream);
    writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
    writer.Write(pixels);

    Point Map(Microsoft.Xna.Framework.Vector2 point)
    {
        int x = (int)MathF.Round((point.X / map.PixelWidth) * (width - 1));
        x %= width;
        if (x < 0) x += width;
        int y = Math.Clamp((int)MathF.Round((point.Y / (map.Height * map.TileSize)) * (height - 1)), 0, height - 1);
        return new Point(x, y);
    }

    void DrawLine(Point start, Point end, byte red, byte green, byte blue)
    {
        int dx = end.X - start.X;
        if (dx > width / 2) end.X -= width;
        else if (dx < -width / 2) end.X += width;
        dx = Math.Abs(end.X - start.X);
        int sx = start.X < end.X ? 1 : -1;
        int dy = -Math.Abs(end.Y - start.Y);
        int sy = start.Y < end.Y ? 1 : -1;
        int error = dx + dy;
        int x = start.X;
        int y = start.Y;
        while (true)
        {
            SetPixel(x, y, red, green, blue);
            if (x == end.X && y == end.Y) break;
            int e2 = error * 2;
            if (e2 >= dy) { error += dy; x += sx; }
            if (e2 <= dx) { error += dx; y += sy; }
        }
    }

    void SetPixel(int x, int y, byte red, byte green, byte blue)
    {
        if (y < 0 || y >= height) return;
        x %= width;
        if (x < 0) x += width;
        int index = ((y * width) + x) * 3;
        pixels[index] = Math.Max(pixels[index], red);
        pixels[index + 1] = Math.Max(pixels[index + 1], green);
        pixels[index + 2] = Math.Max(pixels[index + 2], blue);
    }
}
