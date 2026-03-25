using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Generation.Passes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

string repoRoot = ResolveRepoRoot();
string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvorn", "Worlds", "58207c61d7e141b795e31faba2ce6ec8.plt");
if (!File.Exists(savePath))
{
    throw new FileNotFoundException("Save file not found", savePath);
}

SaveMetadata metadata = LoadMetadata(savePath);
WorldGenConfig config = WorldGenConfig.CreatePreset((WorldSizePreset)metadata.SizePreset, metadata.Seed);
WorldMap map = new WorldMap(config.WorldWidth, config.WorldHeight, config.TileSize);
WorldGenContext context = BuildContext(map, config);
IWorldGenPass[] passes =
[
    new ClearWorldPass(),
    new LayerBoundaryPass(),
    new SurfaceProfilePass(),
    new BaseTerrainFillPass(),
    new SandRegionPass(),
    new CaveFieldPass(),
    new CaveCarvingPass(),
    new ChamberCarvingPass(),
    new SurfaceDecorationPass(),
    new WorldBoundsPass()
];

foreach (IWorldGenPass pass in passes)
{
    if (config.Debug.IsEnabled(pass.Name))
        pass.Apply(context);
}

string outputDir = Path.Combine(repoRoot, "Docs", "WorldgenDiagnostics", $"Seed-{metadata.Seed}");
Directory.CreateDirectory(outputDir);

Dictionary<WorldLayerType, LayerStats> layerStats = ComputeLayerStats(map, context);
WriteLayerPpm(map, context, Path.Combine(outputDir, "layers-air-vs-solid.ppm"));
WriteReport(outputDir, metadata, config, context, layerStats, passes);

Console.WriteLine(outputDir);

static string ResolveRepoRoot()
{
    string dir = AppContext.BaseDirectory;
    DirectoryInfo? current = new DirectoryInfo(dir);
    while (current != null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "Nyvorn")) && Directory.Exists(Path.Combine(current.FullName, "Docs")))
            return current.FullName;
        current = current.Parent;
    }

    throw new InvalidOperationException("Repo root not found.");
}

static SaveMetadata LoadMetadata(string path)
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement meta = doc.RootElement.GetProperty("Metadata");
    return new SaveMetadata(
        meta.GetProperty("WorldId").GetString() ?? string.Empty,
        meta.GetProperty("PlanetName").GetString() ?? string.Empty,
        meta.GetProperty("Seed").GetInt32(),
        meta.GetProperty("SizePreset").GetInt32(),
        meta.GetProperty("WorldWidth").GetInt32(),
        meta.GetProperty("WorldHeight").GetInt32(),
        meta.GetProperty("TileSize").GetInt32());
}

static WorldGenContext BuildContext(WorldMap map, WorldGenConfig config)
{
    return new WorldGenContext
    {
        WorldMap = map,
        Config = config,
        Random = new Random(config.Seed),
        SurfaceNoise = CreateSurfaceNoise(config),
        SurfaceDetailNoise = CreateSurfaceDetailNoise(config),
        SurfaceWarpNoise = CreateSurfaceWarpNoise(config),
        CaveFieldNoise = CreateCaveFieldNoise(config),
        CaveWarpXNoise = CreateCaveWarpXNoise(config),
        CaveWarpYNoise = CreateCaveWarpYNoise(config),
        CaveNoise = CreateCaveNoise(config),
        CaveRoomNoise = CreateCaveRoomNoise(config),
        BiomeNoise = CreateBiomeNoise(config),
        MaterialNoise = CreateMaterialNoise(config)
    };
}

static FastNoiseLite CreateSurfaceNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
    noise.SetFrequency(config.SurfaceFrequency);
    noise.SetFractalType(FastNoiseLite.FractalType.FBm);
    noise.SetFractalOctaves(4);
    noise.SetFractalGain(0.5f);
    return noise;
}

static FastNoiseLite CreateSurfaceDetailNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 57);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.SurfaceDetailFrequency);
    noise.SetFractalType(FastNoiseLite.FractalType.FBm);
    noise.SetFractalOctaves(2);
    noise.SetFractalGain(0.45f);
    return noise;
}

static FastNoiseLite CreateSurfaceWarpNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 101);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.SurfaceWarpFrequency);
    return noise;
}

static FastNoiseLite CreateCaveNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 202);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.CaveFrequency);
    return noise;
}

static FastNoiseLite CreateCaveFieldNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 181);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.CaveFieldFrequency);
    return noise;
}

static FastNoiseLite CreateCaveWarpXNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 191);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
    noise.SetFrequency(config.CaveFieldWarpFrequency);
    return noise;
}

static FastNoiseLite CreateCaveWarpYNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 197);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.CaveFieldWarpFrequency);
    return noise;
}

static FastNoiseLite CreateCaveRoomNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 233);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.CaveRoomFrequency);
    return noise;
}

static FastNoiseLite CreateBiomeNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 303);
    noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
    noise.SetFrequency(config.BiomeFrequency);
    return noise;
}

static FastNoiseLite CreateMaterialNoise(WorldGenConfig config)
{
    FastNoiseLite noise = new(config.Seed + 404);
    noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
    noise.SetFrequency(config.MaterialFrequency);
    return noise;
}

static Dictionary<WorldLayerType, LayerStats> ComputeLayerStats(WorldMap map, WorldGenContext context)
{
    Dictionary<WorldLayerType, LayerStats> stats = new();
    foreach (WorldLayerDefinition layer in context.LayerDefinitions)
    {
        int total = layer.Height * map.Width;
        int air = 0;
        bool[,] visited = new bool[map.Width, layer.Height];
        int componentCount = 0;
        long componentTileSum = 0;

        for (int y = layer.StartY; y <= layer.EndY; y++)
        {
            int localY = y - layer.StartY;
            for (int x = 0; x < map.Width; x++)
            {
                if (map.GetTile(x, y) == TileType.Empty)
                    air++;

                if (map.GetTile(x, y) != TileType.Empty || visited[x, localY])
                    continue;

                int size = FloodFill(map, layer, x, y, visited);
                if (size > 0)
                {
                    componentCount++;
                    componentTileSum += size;
                }
            }
        }

        stats[layer.LayerType] = new LayerStats(
            layer.StartY,
            layer.EndY,
            total,
            air,
            total == 0 ? 0 : (air * 100.0) / total,
            componentCount,
            componentCount == 0 ? 0 : componentTileSum / (double)componentCount);
    }

    return stats;
}

static int FloodFill(WorldMap map, WorldLayerDefinition layer, int startX, int startY, bool[,] visited)
{
    Queue<(int X, int Y)> queue = new();
    queue.Enqueue((startX, startY));
    visited[startX, startY - layer.StartY] = true;
    int count = 0;

    while (queue.Count > 0)
    {
        (int x, int y) = queue.Dequeue();
        count++;
        TryVisit(x + 1, y);
        TryVisit(x - 1, y);
        TryVisit(x, y + 1);
        TryVisit(x, y - 1);
    }

    return count;

    void TryVisit(int x, int y)
    {
        if (x < 0 || x >= map.Width || y < layer.StartY || y > layer.EndY)
            return;
        if (map.GetTile(x, y) != TileType.Empty)
            return;
        int localY = y - layer.StartY;
        if (visited[x, localY])
            return;
        visited[x, localY] = true;
        queue.Enqueue((x, y));
    }
}

static void WriteLayerPpm(WorldMap map, WorldGenContext context, string path)
{
    using StreamWriter writer = new(path, false, Encoding.ASCII);
    writer.WriteLine("P3");
    writer.WriteLine($"{map.Width} {map.Height}");
    writer.WriteLine("255");

    for (int y = 0; y < map.Height; y++)
    {
        WorldLayerType layer = context.GetLayerAtY(y);
        for (int x = 0; x < map.Width; x++)
        {
            bool isAir = map.GetTile(x, y) == TileType.Empty;
            (int r, int g, int b) = layer switch
            {
                WorldLayerType.Space => isAir ? (8, 12, 18) : (24, 28, 36),
                WorldLayerType.Surface => isAir ? (120, 190, 255) : (36, 72, 44),
                WorldLayerType.ShallowUnderground => isAir ? (255, 214, 132) : (72, 54, 34),
                WorldLayerType.Cavern => isAir ? (120, 220, 255) : (46, 56, 74),
                WorldLayerType.DeepCavern => isAir ? (255, 120, 180) : (60, 34, 52),
                _ => isAir ? (255, 255, 255) : (0, 0, 0)
            };
            writer.Write($"{r} {g} {b} ");
        }
        writer.WriteLine();
    }
}

static void WriteReport(string outputDir, SaveMetadata metadata, WorldGenConfig config, WorldGenContext context, Dictionary<WorldLayerType, LayerStats> layerStats, IWorldGenPass[] passes)
{
    StringBuilder sb = new();
    sb.AppendLine($"Seed: {metadata.Seed}");
    sb.AppendLine($"WorldId: {metadata.WorldId}");
    sb.AppendLine($"Planet: {metadata.PlanetName}");
    sb.AppendLine($"SizePreset: {(WorldSizePreset)metadata.SizePreset}");
    sb.AppendLine($"World: {config.WorldWidth}x{config.WorldHeight} tiles");
    sb.AppendLine();
    sb.AppendLine("PassOrder:");
    foreach (IWorldGenPass pass in passes)
        sb.AppendLine($"- {pass.Name}");
    sb.AppendLine();
    sb.AppendLine("Layers:");
    foreach ((WorldLayerType layer, LayerStats stats) in layerStats)
        sb.AppendLine($"- {layer}: Y {stats.StartY}-{stats.EndY}, Air {stats.AirTiles}/{stats.TotalTiles} ({stats.AirPercent:F2}%), Components {stats.VoidComponentCount}, AvgComponentSize {stats.AverageVoidComponentSize:F2}");
    sb.AppendLine();
    sb.AppendLine("DebugStats:");
    foreach (var kv in context.DebugStats.OrderBy(kv => kv.Key))
        sb.AppendLine($"- {kv.Key}={kv.Value}");
    sb.AppendLine();
    sb.AppendLine("Config:");
    foreach (var line in GetConfigLines(config))
        sb.AppendLine(line);

    File.WriteAllText(Path.Combine(outputDir, "report.txt"), sb.ToString());
}

static IEnumerable<string> GetConfigLines(WorldGenConfig config)
{
    yield return $"SizePreset={(WorldSizePreset)config.SizePreset}";
    yield return $"Seed={config.Seed}";
    yield return $"WorldWidth={config.WorldWidth}";
    yield return $"WorldHeight={config.WorldHeight}";
    yield return $"SurfaceBaseHeight={config.SurfaceBaseHeight}";
    yield return $"SurfaceAmplitude={config.SurfaceAmplitude}";
    yield return $"DirtDepthBelowSurface={config.DirtDepthBelowSurface}";
    yield return $"StoneStartDepth={config.StoneStartDepth}";
    yield return $"StoneFullDepth={config.StoneFullDepth}";
    yield return $"CaveFieldFrequency={config.CaveFieldFrequency}";
    yield return $"CaveFieldWarpFrequency={config.CaveFieldWarpFrequency}";
    yield return $"CaveFieldWarpStrengthX={config.CaveFieldWarpStrengthX}";
    yield return $"CaveFieldWarpStrengthY={config.CaveFieldWarpStrengthY}";
    yield return $"CaveFieldThresholdShallowMin={config.CaveFieldThresholdShallowMin}";
    yield return $"CaveFieldThresholdShallowMax={config.CaveFieldThresholdShallowMax}";
    yield return $"CaveFieldThresholdCavernMin={config.CaveFieldThresholdCavernMin}";
    yield return $"CaveFieldThresholdCavernMax={config.CaveFieldThresholdCavernMax}";
    yield return $"CaveFieldThresholdDeepMin={config.CaveFieldThresholdDeepMin}";
    yield return $"CaveFieldThresholdDeepMax={config.CaveFieldThresholdDeepMax}";
    yield return $"CaveWormCountShallow={config.CaveWormCountShallow}";
    yield return $"CaveWormCountMid={config.CaveWormCountMid}";
    yield return $"CaveWormCountDeep={config.CaveWormCountDeep}";
    yield return $"CaveWormLengthMin={config.CaveWormLengthMin}";
    yield return $"CaveWormLengthMax={config.CaveWormLengthMax}";
    yield return $"CaveWormRadiusMin={config.CaveWormRadiusMin}";
    yield return $"CaveWormRadiusMax={config.CaveWormRadiusMax}";
    yield return $"CaveChamberCountShallow={config.CaveChamberCountShallow}";
    yield return $"CaveChamberCountCavern={config.CaveChamberCountCavern}";
    yield return $"CaveChamberCountDeep={config.CaveChamberCountDeep}";
    yield return $"CaveGuaranteedLargeChambersCavern={config.CaveGuaranteedLargeChambersCavern}";
    yield return $"CaveGuaranteedLargeChambersDeep={config.CaveGuaranteedLargeChambersDeep}";
    yield return $"CaveChamberSmallRadiusMin={config.CaveChamberSmallRadiusMin}";
    yield return $"CaveChamberSmallRadiusMax={config.CaveChamberSmallRadiusMax}";
    yield return $"CaveChamberMediumRadiusMin={config.CaveChamberMediumRadiusMin}";
    yield return $"CaveChamberMediumRadiusMax={config.CaveChamberMediumRadiusMax}";
    yield return $"CaveChamberLargeRadiusMin={config.CaveChamberLargeRadiusMin}";
    yield return $"CaveChamberLargeRadiusMax={config.CaveChamberLargeRadiusMax}";
    yield return $"SpaceLayerEndPercent={config.SpaceLayerEndPercent}";
    yield return $"SurfaceLayerEndPercent={config.SurfaceLayerEndPercent}";
    yield return $"ShallowLayerEndPercent={config.ShallowLayerEndPercent}";
    yield return $"CavernLayerEndPercent={config.CavernLayerEndPercent}";
}

record SaveMetadata(string WorldId, string PlanetName, int Seed, int SizePreset, int WorldWidth, int WorldHeight, int TileSize);
record LayerStats(int StartY, int EndY, int TotalTiles, int AirTiles, double AirPercent, int VoidComponentCount, double AverageVoidComponentSize);
