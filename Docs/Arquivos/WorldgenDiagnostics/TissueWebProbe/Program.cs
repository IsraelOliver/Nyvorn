using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using Nyvorn.Source.World.Tissue;

bool previewOnly = args.Contains("--preview");
int[] seeds = previewOnly ? [1337] : [1337, 2468, 9001];
WorldSizePreset[] presets = previewOnly ? [WorldSizePreset.Medium] : [WorldSizePreset.Small, WorldSizePreset.Medium, WorldSizePreset.Large];

ValidateLegacySaveCompatibility();

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
        Require(first.RasterizedField.OverrideCount == 0, $"generated field contains overrides for {preset}/{seed}");
        Require(first.RasterizedField.BaseCellCount == first.Stats.RasterizedTileCount, $"base cell count differs from stats for {preset}/{seed}");
        Require(first.RasterizedField.CountActiveTiles() == first.Stats.RasterizedTileCount, $"effective cell count differs from stats for {preset}/{seed}");
        Require(HasSameField(first.RasterizedField, second.RasterizedField), $"non-deterministic field for {preset}/{seed}");
        map.SetTissueField(first.RasterizedField);
        Require(ReferenceEquals(map.TissueField, first.RasterizedField), $"world map does not reference generated field for {preset}/{seed}");
        ValidateMutableField(map, first, preset, seed);
        ValidateDeltaRoundTrip(map, second, preset, seed);

        Console.WriteLine(
            $"{preset,-6} seed={seed} points={first.Stats.PointCount} edges={first.Stats.EdgeCount} " +
            $"micro={first.Stats.MicroFilamentCount} component={first.Stats.DominantComponentRatio:P1} " +
            $"field={first.Stats.RasterizedTileCount} " +
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
    foreach (TissueFieldCell cell in field.EnumerateActiveCells())
    {
        if (!map.IsSolidAt(cell.X, cell.Y))
            return false;
    }
    return true;
}

static bool HasSameField(TissueField first, TissueField second)
{
    if (first.Width != second.Width ||
        first.Height != second.Height ||
        first.CountActiveTiles() != second.CountActiveTiles())
    {
        return false;
    }

    foreach (TissueFieldCell cell in first.EnumerateActiveCells())
    {
        TissueCellState other = second.GetState(cell.X, cell.Y);
        if (cell.State.Presence != other.Presence ||
            cell.State.Vitality != other.Vitality ||
            cell.State.Corruption != other.Corruption ||
            cell.State.MemoryDensity != other.MemoryDensity ||
            cell.State.Flow != other.Flow)
        {
            return false;
        }
    }

    return true;
}

static void ValidateMutableField(WorldMap map, TissueGenerationResult generation, WorldSizePreset preset, int seed)
{
    TissueField field = generation.RasterizedField;
    TissueFieldCell[] samples = field.EnumerateActiveCells().Take(2).ToArray();
    Require(samples.Length == 2, $"not enough field cells for mutation test for {preset}/{seed}");

    TissueFieldCell target = samples[0];
    TissueFieldCell unaffected = samples[1];
    TissueCellState unaffectedBefore = field.GetState(unaffected.X, unaffected.Y);
    int initialRevision = field.Revision;
    int initialWorldRevision = map.TissueRevision;

    TissueCellState changed = target.State.With(corruption: 0.5f, memoryDensity: 0.25f);
    Require(map.TrySetTissueState(target.X, target.Y, changed), $"solid field mutation rejected for {preset}/{seed}");
    Require(field.Revision == initialRevision + 1, $"field revision did not advance for {preset}/{seed}");
    Require(map.TissueRevision == initialWorldRevision + 1, $"world tissue revision did not advance for {preset}/{seed}");
    Require(field.HasUnsavedChanges, $"field mutation was not marked dirty for {preset}/{seed}");
    Require(field.OverrideCount == 1, $"field mutation changed more than one override for {preset}/{seed}");
    Require(field.GetState(target.X, target.Y).Corruption == 0.5f, $"field mutation was not retained for {preset}/{seed}");

    Require(map.TrySetTissueState(target.X, target.Y, target.State), $"field baseline restore rejected for {preset}/{seed}");
    Require(field.OverrideCount == 0, $"baseline restore did not remove override for {preset}/{seed}");
    field.MarkPersisted();
    Require(!field.HasUnsavedChanges, $"field persisted marker did not reset dirty state for {preset}/{seed}");

    int revisionBeforeMining = field.Revision;
    Require(map.TryBreakTile(target.X, target.Y, out _), $"tissue tile could not be mined for {preset}/{seed}");
    Require(!field.HasTissue(target.X, target.Y), $"mined tissue remained active for {preset}/{seed}");
    Require(field.OverrideCount == 1, $"mining did not create one tombstone for {preset}/{seed}");
    Require(field.Revision == revisionBeforeMining + 1, $"mining changed field revision incorrectly for {preset}/{seed}");
    Require(field.HasUnsavedChanges, $"mining did not mark field dirty for {preset}/{seed}");

    Require(map.TryPlaceTile(target.X, target.Y, TileType.Stone), $"replacement tile could not be placed for {preset}/{seed}");
    Require(!field.HasTissue(target.X, target.Y), $"placing a tile restored tissue for {preset}/{seed}");
    Require(field.OverrideCount == 1, $"placing a tile changed the tissue tombstone for {preset}/{seed}");
    Require(StatesEqual(unaffectedBefore, field.GetState(unaffected.X, unaffected.Y)), $"mutation affected a neighboring field cell for {preset}/{seed}");

    TissueCellState airState = new(0.5f, 0.5f, 0f, 0f, 0.5f);
    int revisionBeforeAirMutation = field.Revision;
    Require(!map.TrySetTissueState(0, 0, airState), $"world accepted tissue in air for {preset}/{seed}");
    Require(!field.SetState(0, 0, airState), $"field accepted tissue in air for {preset}/{seed}");
    Require(field.Revision == revisionBeforeAirMutation, $"rejected air mutation changed revision for {preset}/{seed}");
}

static bool StatesEqual(TissueCellState a, TissueCellState b)
{
    return a.Presence == b.Presence &&
           a.Vitality == b.Vitality &&
           a.Corruption == b.Corruption &&
           a.MemoryDensity == b.MemoryDensity &&
           a.Flow == b.Flow;
}

static void ValidateDeltaRoundTrip(WorldMap map, TissueGenerationResult generation, WorldSizePreset preset, int seed)
{
    TissueField source = generation.RasterizedField;
    map.SetTissueField(source);
    TissueFieldCell[] samples = source.EnumerateActiveCells().Take(2).ToArray();
    Require(samples.Length == 2, $"not enough field cells for delta test for {preset}/{seed}");

    TissueCellState changed = new(0.37f, 0.62f, 0.41f, 0.23f, 0.84f);
    Require(map.TrySetTissueState(samples[0].X, samples[0].Y, changed), $"delta state mutation rejected for {preset}/{seed}");
    Require(map.TryBreakTile(samples[1].X, samples[1].Y, out _), $"delta tombstone mining rejected for {preset}/{seed}");

    byte[] snapshot = TissueFieldDeltaCodec.Export(
        source,
        seed,
        generation.Stats.DeterministicHash,
        TissueGenerator.AlgorithmVersion);
    Require(snapshot != null && snapshot.Length > 0, $"empty tissue delta snapshot for {preset}/{seed}");

    TissueGenerationResult regenerated = new TissueGenerator(seed).Generate(map);
    map.SetTissueField(regenerated.RasterizedField);
    TissueFieldDeltaLoadResult load = TissueFieldDeltaCodec.Import(
        snapshot,
        regenerated.RasterizedField,
        map,
        seed,
        regenerated.Stats.DeterministicHash,
        TissueGenerator.AlgorithmVersion);

    Require(load.IsValid, $"tissue delta rejected for {preset}/{seed}");
    Require(load.TopologyMatched, $"matching tissue topology was rejected for {preset}/{seed}");
    Require(load.LoadedCount == 2 && load.SkippedCount == 0, $"tissue delta count mismatch for {preset}/{seed}");
    Require(regenerated.RasterizedField.OverrideCount == 2, $"restored override count mismatch for {preset}/{seed}");
    Require(!regenerated.RasterizedField.HasUnsavedChanges, $"loaded tissue delta was marked dirty for {preset}/{seed}");
    Require(!regenerated.RasterizedField.HasTissue(samples[1].X, samples[1].Y), $"restored tombstone became active for {preset}/{seed}");
    Require(map.TryPlaceTile(samples[1].X, samples[1].Y, TileType.Stone), $"post-load replacement tile failed for {preset}/{seed}");
    Require(!regenerated.RasterizedField.HasTissue(samples[1].X, samples[1].Y), $"post-load replacement restored tissue for {preset}/{seed}");
    Require(regenerated.RasterizedField.OverrideCount == 2, $"post-load replacement removed tombstone for {preset}/{seed}");

    TissueCellState restored = regenerated.RasterizedField.GetState(samples[0].X, samples[0].Y);
    const float tolerance = (1f / byte.MaxValue) + 0.00001f;
    Require(System.MathF.Abs(restored.Presence - changed.Presence) <= tolerance, $"presence quantization exceeded tolerance for {preset}/{seed}");
    Require(System.MathF.Abs(restored.Vitality - changed.Vitality) <= tolerance, $"vitality quantization exceeded tolerance for {preset}/{seed}");
    Require(System.MathF.Abs(restored.Corruption - changed.Corruption) <= tolerance, $"corruption quantization exceeded tolerance for {preset}/{seed}");
    Require(System.MathF.Abs(restored.MemoryDensity - changed.MemoryDensity) <= tolerance, $"memory quantization exceeded tolerance for {preset}/{seed}");
    Require(System.MathF.Abs(restored.Flow - changed.Flow) <= tolerance, $"flow quantization exceeded tolerance for {preset}/{seed}");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void ValidateLegacySaveCompatibility()
{
    WorldGenConfig config = WorldGenConfig.CreatePreset(WorldSizePreset.Small, 1337);
    foreach (int legacyVersion in new[] { 10, 11 })
    {
        string path = Path.Combine(Path.GetTempPath(), $"nyvorn-v{legacyVersion}-{Guid.NewGuid():N}.plt");
        try
        {
            var legacySave = new
            {
                Version = legacyVersion,
                Metadata = PlanetWorldMetadata.Create("Legacy Probe", config, $"legacy-probe-{legacyVersion}")
            };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(legacySave));
            PlanetSaveData loaded = new PlanetSaveService().Load(path);
            Require(loaded.Version == legacyVersion, $"legacy v{legacyVersion} save did not load");
            Require(loaded.ConsoleCommandHistory.Count == 0, $"legacy v{legacyVersion} save unexpectedly contains console history");
            Require(loaded.TissueFieldDeltaSnapshot == null, $"legacy v{legacyVersion} save unexpectedly contains tissue deltas");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    Require(
        new PlanetSaveData { Metadata = PlanetWorldMetadata.Create("Current Probe", config, "current-probe") }.Version == 12,
        "new save version is not 12");
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
