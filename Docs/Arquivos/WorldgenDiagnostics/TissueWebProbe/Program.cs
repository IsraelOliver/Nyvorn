using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using Nyvorn.Source.World.Tissue;

bool previewOnly = args.Contains("--preview");
int[] seeds = previewOnly ? [1337] : [1337, 2468, 9001];
WorldSizePreset[] presets = previewOnly ? [WorldSizePreset.Medium] : [WorldSizePreset.Small, WorldSizePreset.Medium, WorldSizePreset.Large];

ValidateLegacySaveCompatibility();
ValidateTissueQueryApi();

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
        ValidateGeneratedQueries(map, first, preset, seed);
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

static void ValidateTissueQueryApi()
{
    const int width = 5;
    const int height = 4;
    const int tileSize = 8;
    WorldMap map = new(width, height, tileSize);
    byte[] solidTiles = Enumerable.Repeat((byte)TileType.Stone, width * height).ToArray();
    map.ImportTileSnapshot(solidTiles);

    TissueField field = new(width, height);
    map.SetTissueField(field);
    TissueCellState leftEdge = new(0.8f, 0.6f, 0.2f, 0.4f, 1f);
    TissueCellState rightEdge = new(0.4f, 0.2f, 0.6f, 0.8f, 0.5f);
    TissueCellState lower = new(0.6f, 1f, 0f, 0.2f, 0.25f);
    Require(field.SetState(4, 1, leftEdge), "query fixture rejected left-edge tissue");
    Require(field.SetState(0, 1, rightEdge), "query fixture rejected right-edge tissue");
    Require(field.SetState(1, 2, lower), "query fixture rejected lower tissue");
    field.MarkPersisted();

    TissueNode[] nodes =
    [
        new TissueNode(7, new Vector2(39f, 8f), true, 0.9f, 5, 0.7f),
        new TissueNode(9, new Vector2(10f, 8f), false, 0.4f, 2, 0.1f),
        new TissueNode(3, new Vector2(36f, 16f), true, 0.8f, 4, 0.5f),
        new TissueNode(8, new Vector2(4f, 16f), true, 0.8f, 4, 0.5f),
        new TissueNode(6, new Vector2(1f, 8f), false, 0.3f, 0, 0f)
    ];
    TissueBranch[] branches =
    [
        new TissueBranch(100, 7, 9, true, 1f, 0.9f, TissueBranch.TissueBranchKind.Main,
            [new Vector2(39f, 8f), new Vector2(45f, 8f), new Vector2(50f, 8f)]),
        new TissueBranch(101, 9, 8, true, 1f, 0.8f, TissueBranch.TissueBranchKind.Main,
            [new Vector2(10f, 8f), new Vector2(4f, 16f)]),
        new TissueBranch(102, 9, 3, false, 1f, 0.7f, TissueBranch.TissueBranchKind.Main,
            [new Vector2(10f, 8f), new Vector2(-4f, 16f)]),
        new TissueBranch(103, 8, 3, false, 1f, 0.6f, TissueBranch.TissueBranchKind.Main,
            [new Vector2(4f, 16f), new Vector2(-4f, 16f)]),
        new TissueBranch(104, 8, -1, false, 0.5f, 0.4f, TissueBranch.TissueBranchKind.Micro,
            [new Vector2(4f, 16f), new Vector2(4f, 21f)])
    ];
    TissueNetwork network = new(
        1337,
        new Rectangle(0, 0, map.PixelWidth, map.Height * map.TileSize),
        nodes,
        branches);
    ITissueQueryService queries = new TissueQueryService(map, field, network);

    Require(StatesEqual(queries.GetState(-1, 1), leftEdge), "tile query did not wrap negative X");
    Require(StatesEqual(queries.GetState(width, 1), rightEdge), "tile query did not wrap positive X");
    Require(StatesEqual(queries.GetState(0, -1), TissueCellState.Neutral), "tile query did not reject negative Y");
    Require(StatesEqual(queries.GetState(0, height), TissueCellState.Neutral), "tile query did not reject Y past world");
    Require(queries.HasTissue(-1, 1, 0.8f), "minimum presence rejected an equal value");
    Require(!queries.HasTissue(-1, 1, 0.81f), "minimum presence accepted a lower value");
    Require(queries.HasTissue(-1, 1, float.NaN), "invalid presence threshold did not use the default");
    Require(!queries.HasTissue(2, 1, 0f), "neutral tile was reported as tissue");

    TissueAreaSample wrapped = queries.SampleArea(new Rectangle(4, 0, 3, 3));
    Require(wrapped.TotalTileCount == 9, "wrapped area total count mismatch");
    Require(wrapped.ActiveTileCount == 3, "wrapped area active count mismatch");
    Require(Approximately(wrapped.Coverage, 1f / 3f), "wrapped area coverage mismatch");
    Require(Approximately(wrapped.AveragePresence, 0.6f), "wrapped area presence average mismatch");
    Require(Approximately(wrapped.MaximumPresence, 0.8f), "wrapped area maximum presence mismatch");
    Require(Approximately(wrapped.AverageVitality, 0.6f), "wrapped area vitality average mismatch");
    Require(Approximately(wrapped.AverageCorruption, 0.8f / 3f), "wrapped area corruption average mismatch");
    Require(Approximately(wrapped.AverageMemory, 1.4f / 3f), "wrapped area memory average mismatch");
    Require(Approximately(wrapped.AverageFlow, 1.75f / 3f), "wrapped area flow average mismatch");
    Require(wrapped.HasTissue, "wrapped area did not report tissue");

    TissueAreaSample clipped = queries.SampleArea(new Rectangle(4, -1, 3, 3));
    Require(clipped.TotalTileCount == 6 && clipped.ActiveTileCount == 2, "vertical clipping mismatch");
    TissueAreaSample overwide = queries.SampleArea(new Rectangle(0, 0, width * 2, 1));
    Require(overwide.TotalTileCount == width, "overwide area sampled the planet more than once");
    Require(queries.SampleArea(new Rectangle(0, 0, 0, 4)) == default, "empty area did not return an empty sample");
    Require(queries.IsDenseRegion(new Rectangle(4, 0, 3, 3), 0.33f), "dense region rejected sufficient coverage");
    Require(!queries.IsDenseRegion(new Rectangle(4, 0, 3, 3), 0.34f), "dense region accepted insufficient coverage");
    Require(!queries.IsDenseRegion(new Rectangle(0, height, 2, 2), 0f), "empty clipped region was reported as dense");

    Require(queries.TryFindNearestNode(new Vector2(1f, 8f), 3f, out TissueNodeInfo wrappedNode), "seam-aware nearest node was not found");
    Require(wrappedNode.Id == 6 && wrappedNode.Degree == 0, "general nearest node DTO mismatch");
    Require(
        queries.TryFindNearestConnectedNode(new Vector2(1f, 8f), 3f, out TissueNodeInfo connectedNode) &&
        connectedNode.Id == 7 && connectedNode.Degree == 5 && connectedNode.IsPrimary,
        "connected nearest-node query selected an isolated node");
    Require(queries.TryFindNearestNode(new Vector2(0f, 16f), 4f, out TissueNodeInfo tiedNode), "tied nearest node was not found");
    Require(tiedNode.Id == 3, "nearest node tie was not resolved by ID");
    Require(!queries.TryFindNearestNode(new Vector2(20f, 8f), 5f, out _), "nearest node ignored maximum distance");
    Require(!queries.TryFindNearestNode(new Vector2(float.NaN, 8f), 5f, out _), "nearest node accepted invalid position");
    Require(!queries.TryFindNearestNode(new Vector2(1f, 8f), 0f, out _), "nearest node accepted zero distance");
    Require(!queries.TryFindNearestConnectedNode(new Vector2(20f, 8f), 5f, out _), "connected nearest node ignored maximum distance");

    Require(queries.TryBuildPropagation(7, 25f, out TissuePropagationMap propagation), "Dijkstra propagation was not built");
    Require(propagation.Nodes.Count == 3, "Dijkstra included a node beyond maximum distance");
    Require(propagation.Branches.Count == 5, "Dijkstra did not preserve reachable branches and microfilaments");
    Require(propagation.TryGetNode(7, out TissueNodePropagation originArrival) && Approximately(originArrival.ArrivalDistance, 0f),
        "origin node arrival distance mismatch");
    Require(propagation.TryGetNode(9, out TissueNodePropagation secondArrival) && Approximately(secondArrival.ArrivalDistance, 11f),
        "second node arrival distance mismatch");
    Require(propagation.TryGetNode(8, out TissueNodePropagation thirdArrival) && Approximately(thirdArrival.ArrivalDistance, 21f),
        "third node arrival distance mismatch");
    Require(!propagation.TryGetNode(3, out _), "Dijkstra crossed the propagation distance limit");
    Require(propagation.TryGetBranch(100, out TissueBranchPropagation firstBranch) &&
        firstBranch.FromNodeId == 7 && !firstBranch.Reverse && Approximately(firstBranch.StartDistance, 0f),
        "first branch propagation direction mismatch");
    Require(propagation.TryGetBranch(104, out TissueBranchPropagation microBranch) &&
        microBranch.FromNodeId == 8 && Approximately(microBranch.StartDistance, 21f),
        "microfilament did not inherit its node arrival");
    Require(!queries.TryBuildPropagation(999, 25f, out _), "propagation accepted an unknown origin");
    Require(!queries.TryBuildPropagation(7, float.NaN, out _), "propagation accepted an invalid distance");

    int revisionBeforeQueries = field.Revision;
    int overridesBeforeQueries = field.OverrideCount;
    bool dirtyBeforeQueries = field.HasUnsavedChanges;
    _ = queries.GetState(4, 1);
    _ = queries.SampleArea(new Rectangle(0, 0, width, height));
    _ = queries.TryFindNearestNode(new Vector2(1f, 8f), 3f, out _);
    _ = queries.TryFindNearestConnectedNode(new Vector2(1f, 8f), 3f, out _);
    _ = queries.TryBuildPropagation(7, 25f, out _);
    Require(field.Revision == revisionBeforeQueries, "read-only queries changed field revision");
    Require(field.OverrideCount == overridesBeforeQueries, "read-only queries changed field overrides");
    Require(field.HasUnsavedChanges == dirtyBeforeQueries, "read-only queries changed field dirty state");

    Vector2 sensorPosition = new(0f, 8f);
    TissueEnvironmentSensor environmentSensor = new(map, queries);
    environmentSensor.Initialize(sensorPosition);
    TissueEnvironmentState environment = environmentSensor.CurrentState;
    Require(environment.HasTissue, "environment sensor missed local tissue");
    Require(Approximately(environment.Coverage, 3f / (width * height)), "environment coverage mismatch");
    Require(Approximately(environment.Presence, 0.6f), "environment presence mismatch");
    Require(Approximately(environment.Vitality, 0.6f), "environment vitality mismatch");
    Require(Approximately(environment.DistanceToNearestNode, 1f), "environment nearest-node distance mismatch");
    Require(environment.Revision == map.TissueRevision, "environment revision mismatch");

    TissueResonanceController resonance = new(queries, environmentSensor);
    resonance.SetViewport(100f, 100f);
    int revisionBeforeResonance = field.Revision;
    Require(resonance.Trigger(sensorPosition), "healthy local tissue did not trigger resonance");
    TissueResonanceState healthyResonance = resonance.CurrentState;
    Require(healthyResonance.IsActive && healthyResonance.OriginNode.Id == 7, "resonance selected the wrong node");
    Require(healthyResonance.Propagation != null && healthyResonance.Propagation.Nodes.Count == 4,
        "resonance did not retain the propagation map");
    Require(Approximately(healthyResonance.MaximumDistance, TissueConfig.Resonance.MinimumPropagationDistance),
        "resonance did not derive its range from the viewport");
    Require(healthyResonance.ResponseStrength > 0f && healthyResonance.VisualStrength > 0f, "resonance strength was not calculated");
    Require(field.Revision == revisionBeforeResonance, "resonance mutated the tissue field");
    resonance.SetViewport(1000f, 1000f);
    resonance.Update(0.1f);
    Require(
        resonance.CurrentState.IsActive &&
        Approximately(resonance.CurrentState.MaximumDistance, healthyResonance.MaximumDistance) &&
        Approximately(resonance.CurrentState.PulseFront, TissueConfig.Resonance.PulseSpeed * 0.1f) &&
        Approximately(resonance.CurrentState.PulseBack, (TissueConfig.Resonance.PulseSpeed * 0.1f) - TissueConfig.Resonance.TrailLength),
        "resonance front and trail did not advance");
    resonance.Update(
        ((healthyResonance.MaximumDistance + TissueConfig.Resonance.TrailLength) / TissueConfig.Resonance.PulseSpeed) + 0.1f);
    Require(!resonance.CurrentState.IsActive, "resonance did not expire");

    Require(map.ClearTissueAt(4, 1), "query fixture tombstone was rejected");
    Require(!queries.HasTissue(4, 1), "query API did not observe a live tombstone");
    int tombstoneRevision = field.Revision;
    int tombstoneOverrides = field.OverrideCount;
    Require(field.HasUnsavedChanges, "query fixture tombstone was not marked dirty");
    _ = queries.GetState(4, 1);
    Require(field.Revision == tombstoneRevision && field.OverrideCount == tombstoneOverrides && field.HasUnsavedChanges,
        "querying a tombstone changed mutable field state");

    environmentSensor.Update(0f, sensorPosition);
    Require(environmentSensor.CurrentState.Revision == map.TissueRevision, "sensor did not react immediately to tissue revision");
    Require(resonance.Trigger(sensorPosition), "damaged local tissue removed all resonance");
    Require(
        resonance.CurrentState.ResponseStrength < healthyResonance.ResponseStrength,
        "destroying local tissue did not weaken resonance");

    Require(map.ClearTissueAt(0, 1), "second query fixture tombstone was rejected");
    Require(map.ClearTissueAt(1, 2), "third query fixture tombstone was rejected");
    environmentSensor.Update(0f, sensorPosition);
    Require(!environmentSensor.CurrentState.HasTissue, "sensor retained tissue after local destruction");
    Require(!resonance.Trigger(sensorPosition), "destroyed local tissue still triggered resonance");
    Require(!resonance.CurrentState.IsActive, "failed resonance retained stale visual state");
}

static void ValidateGeneratedQueries(WorldMap map, TissueGenerationResult generation, WorldSizePreset preset, int seed)
{
    ITissueQueryService queries = new TissueQueryService(map, generation.RasterizedField, generation.Network);
    TissueFieldCell activeCell = generation.RasterizedField.EnumerateActiveCells().First();
    Require(
        StatesEqual(queries.GetState(activeCell.X, activeCell.Y), activeCell.State),
        $"query API state differs from generated field for {preset}/{seed}");
    Require(queries.HasTissue(activeCell.X, activeCell.Y), $"query API missed generated tissue for {preset}/{seed}");

    TissueNode firstNode = generation.Network.Nodes[0];
    Require(
        queries.TryFindNearestNode(firstNode.Position, 1f, out TissueNodeInfo nearest) && nearest.Id == firstNode.Id,
        $"query API missed an exact generated node for {preset}/{seed}");
    Require(
        queries.TryBuildPropagation(firstNode.Id, 500f, out TissuePropagationMap propagation),
        $"query API failed to build generated propagation for {preset}/{seed}");
    Require(
        propagation.TryGetNode(firstNode.Id, out TissueNodePropagation origin) && Approximately(origin.ArrivalDistance, 0f),
        $"generated propagation lost its origin for {preset}/{seed}");
    Require(
        propagation.Nodes.Select(node => node.Node.Id).Distinct().Count() == propagation.Nodes.Count,
        $"generated propagation contains duplicate nodes for {preset}/{seed}");
    Require(
        propagation.Branches.Select(branch => branch.BranchId).Distinct().Count() == propagation.Branches.Count,
        $"generated propagation contains duplicate branches for {preset}/{seed}");
    Require(
        propagation.Nodes.All(node => node.ArrivalDistance <= propagation.MaximumDistance),
        $"generated propagation exceeded its distance limit for {preset}/{seed}");
    Require(
        queries.TryBuildPropagation(firstNode.Id, 500f, out TissuePropagationMap repeatedPropagation) &&
        propagation.Nodes.SequenceEqual(repeatedPropagation.Nodes) &&
        propagation.Branches.SequenceEqual(repeatedPropagation.Branches),
        $"generated propagation is not deterministic for {preset}/{seed}");

    int revision = generation.RasterizedField.Revision;
    int overrides = generation.RasterizedField.OverrideCount;
    bool dirty = generation.RasterizedField.HasUnsavedChanges;
    _ = queries.SampleArea(new Rectangle(activeCell.X - 2, activeCell.Y - 2, 5, 5));
    Require(generation.RasterizedField.Revision == revision, $"generated query changed revision for {preset}/{seed}");
    Require(generation.RasterizedField.OverrideCount == overrides, $"generated query changed overrides for {preset}/{seed}");
    Require(generation.RasterizedField.HasUnsavedChanges == dirty, $"generated query changed dirty state for {preset}/{seed}");
}

static bool Approximately(float actual, float expected)
{
    return MathF.Abs(actual - expected) <= 0.00001f;
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
