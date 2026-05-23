using Nyvorn.Source.Game.States;
using Nyvorn.Source.Gameplay.Items;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetSaveService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
        private readonly PlayerSaveService playerSaveService = new();

        public string SaveDirectoryPath { get; }

        public PlanetSaveService()
        {
            SaveDirectoryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nyvorn",
                "Worlds");
        }

        public IReadOnlyList<PlanetSaveSummary> ListWorlds()
        {
            EnsureSaveDirectory();

            List<PlanetSaveSummary> summaries = new();
            foreach (string filePath in Directory.EnumerateFiles(SaveDirectoryPath, "*.plt"))
            {
                PlanetSaveData saveData = TryLoadFromPath(filePath);
                if (saveData == null)
                    continue;

                summaries.Add(new PlanetSaveSummary
                {
                    FilePath = filePath,
                    Metadata = saveData.Metadata,
                    SavedAtUtc = saveData.SavedAtUtc
                });
            }

            return summaries
                .OrderByDescending(summary => summary.SavedAtUtc)
                .ToArray();
        }

        public PlanetSaveData Load(string filePath)
        {
            PlanetSaveData saveData = TryLoadFromPath(filePath);
            return saveData ?? throw new InvalidOperationException($"Nao foi possivel carregar o mundo em '{filePath}'.");
        }

        public void Delete(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            string worldId = TryLoadFromPath(filePath)?.Metadata?.WorldId;
            File.Delete(filePath);
            playerSaveService.Delete(worldId);
        }

        public void Rename(string filePath, string newPlanetName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Arquivo do mundo nao encontrado.", filePath);

            string finalPlanetName = string.IsNullOrWhiteSpace(newPlanetName) ? "Mundo" : newPlanetName.Trim();
            PlanetSaveData saveData = Load(filePath);
            saveData.Metadata = new PlanetWorldMetadata
            {
                WorldId = saveData.Metadata.WorldId,
                PlanetName = finalPlanetName,
                Seed = saveData.Metadata.Seed,
                SizePreset = saveData.Metadata.SizePreset,
                WorldWidth = saveData.Metadata.WorldWidth,
                WorldHeight = saveData.Metadata.WorldHeight,
                TileSize = saveData.Metadata.TileSize
            };

            Save(saveData);
        }

        public void Save(PlayingSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            Save(new PlanetSaveData
            {
                Metadata = session.PlanetMetadata,
                SavedAtUtc = DateTime.UtcNow,
                TileChanges = session.WorldMap.TrackedTileChanges.ToList(),
                Trees = session.WorldMap.Trees
                    .Select(TreeSaveData.FromTree)
                    .ToList(),
                WorldItems = session.WorldItems
                    .Select(item => new WorldItemSaveData
                    {
                        ItemId = item.ItemId,
                        PositionX = item.Position.X,
                        PositionY = item.Position.Y,
                        VelocityX = item.VelocityX,
                        VelocityY = item.VelocityY,
                        PickupDelayRemaining = item.PickupDelayRemaining
                    })
                    .ToList(),
                Workbenches = session.WorkbenchRuntimeSystem.Workbenches
                    .Select(workbench => new WorkbenchSaveData
                    {
                        PositionX = workbench.Position.X,
                        PositionY = workbench.Position.Y
                    })
                    .ToList(),
                Doors = session.DoorRuntimeSystem.Doors
                    .Select(door => new DoorSaveData
                    {
                        TileX = door.Tile.X,
                        TileY = door.Tile.Y,
                        IsOpen = door.IsOpen,
                        OpensRight = door.OpensRight
                    })
                    .ToList(),
                WorldTileSnapshot = session.WorldMap.ExportTileSnapshot(),
                BackgroundTileSnapshot = session.WorldMap.ExportBackgroundTileSnapshot(),
                SandSnapshot = session.SandSystem?.ExportSnapshot(),
                TissueFieldSnapshot = session.WorldMap.ExportTissueSnapshot(),
                TissueAnalysisSnapshot = session.WorldMap.ExportTissueAnalysisSnapshot()
            });

            playerSaveService.Save(session);
            session.WorldMap.MarkPersisted();
            session.WorkbenchRuntimeSystem.MarkPersisted();
            session.DoorRuntimeSystem.MarkPersisted();
        }

        public void SavePlayerOnly(PlayingSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            playerSaveService.Save(session);
        }

        public void Save(PlanetSaveData saveData)
        {
            if (saveData == null)
                throw new ArgumentNullException(nameof(saveData));

            EnsureSaveDirectory();
            string existingFilePath = FindExistingFilePathByWorldId(saveData.Metadata.WorldId);
            string filePath = ResolveFilePath(saveData, existingFilePath);
            PlanetSaveData persisted = CreatePersistedCopy(saveData);
            string json = JsonSerializer.Serialize(persisted, JsonOptions);
            File.WriteAllText(filePath, json);

            if (!string.IsNullOrWhiteSpace(existingFilePath) &&
                !string.Equals(existingFilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(existingFilePath))
            {
                File.Delete(existingFilePath);
            }
        }

        private PlanetSaveData TryLoadFromPath(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                PlanetSaveData persisted = JsonSerializer.Deserialize<PlanetSaveData>(json, JsonOptions);
                return persisted == null ? null : CreateRuntimeCopy(persisted);
            }
            catch
            {
                return null;
            }
        }

        private static PlanetSaveData CreatePersistedCopy(PlanetSaveData saveData)
        {
            return new PlanetSaveData
            {
                Version = saveData.Version,
                Metadata = saveData.Metadata,
                SavedAtUtc = saveData.SavedAtUtc,
                TileChanges = saveData.TileChanges ?? new List<WorldTileChange>(),
                WorldItems = saveData.WorldItems ?? new List<WorldItemSaveData>(),
                Workbenches = saveData.Workbenches ?? new List<WorkbenchSaveData>(),
                Doors = saveData.Doors ?? new List<DoorSaveData>(),
                Trees = saveData.Trees ?? new List<TreeSaveData>(),
                WorldTileSnapshot = CompressBytes(saveData.WorldTileSnapshot),
                BackgroundTileSnapshot = CompressBytes(saveData.BackgroundTileSnapshot),
                SandSnapshot = CompressBytes(saveData.SandSnapshot),
                TissueFieldSnapshot = CompressBytes(saveData.TissueFieldSnapshot),
                TissueAnalysisSnapshot = CompressBytes(saveData.TissueAnalysisSnapshot)
            };
        }

        private static PlanetSaveData CreateRuntimeCopy(PlanetSaveData saveData)
        {
            return new PlanetSaveData
            {
                Version = saveData.Version,
                Metadata = saveData.Metadata,
                SavedAtUtc = saveData.SavedAtUtc,
                TileChanges = saveData.TileChanges ?? new List<WorldTileChange>(),
                WorldItems = saveData.WorldItems ?? new List<WorldItemSaveData>(),
                Workbenches = saveData.Workbenches ?? new List<WorkbenchSaveData>(),
                Doors = saveData.Doors ?? new List<DoorSaveData>(),
                Trees = saveData.Trees ?? new List<TreeSaveData>(),
                WorldTileSnapshot = DecompressBytes(saveData.WorldTileSnapshot),
                BackgroundTileSnapshot = DecompressBytes(saveData.BackgroundTileSnapshot),
                SandSnapshot = DecompressBytes(saveData.SandSnapshot),
                TissueFieldSnapshot = DecompressBytes(saveData.TissueFieldSnapshot),
                TissueAnalysisSnapshot = DecompressBytes(saveData.TissueAnalysisSnapshot)
            };
        }

        private static byte[] CompressBytes(byte[] source)
        {
            if (source == null || source.Length == 0)
                return source;

            using MemoryStream output = new();
            using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
                gzip.Write(source, 0, source.Length);

            return output.ToArray();
        }

        private static byte[] DecompressBytes(byte[] source)
        {
            if (source == null || source.Length == 0)
                return source;

            try
            {
                using MemoryStream input = new(source);
                using GZipStream gzip = new(input, CompressionMode.Decompress);
                using MemoryStream output = new();
                gzip.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return source;
            }
        }

        private string ResolveFilePath(PlanetSaveData saveData, string existingFilePath)
        {
            string baseFileName = GetSafeFileName(saveData.Metadata.PlanetName);
            string desiredFilePath = Path.Combine(SaveDirectoryPath, $"{baseFileName}.plt");

            if (IsPathAvailableForWorld(desiredFilePath, saveData.Metadata.WorldId))
                return desiredFilePath;

            for (int suffix = 2; suffix < 10_000; suffix++)
            {
                string candidatePath = Path.Combine(SaveDirectoryPath, $"{baseFileName} ({suffix}).plt");
                if (IsPathAvailableForWorld(candidatePath, saveData.Metadata.WorldId))
                    return candidatePath;
            }

            if (!string.IsNullOrWhiteSpace(existingFilePath))
                return existingFilePath;

            return Path.Combine(SaveDirectoryPath, $"{saveData.Metadata.WorldId}.plt");
        }

        private string FindExistingFilePathByWorldId(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId) || !Directory.Exists(SaveDirectoryPath))
                return null;

            foreach (string filePath in Directory.EnumerateFiles(SaveDirectoryPath, "*.plt"))
            {
                PlanetSaveData saveData = TryLoadFromPath(filePath);
                if (saveData?.Metadata == null)
                    continue;

                if (string.Equals(saveData.Metadata.WorldId, worldId, StringComparison.Ordinal))
                    return filePath;
            }

            return null;
        }

        private bool IsPathAvailableForWorld(string filePath, string worldId)
        {
            if (!File.Exists(filePath))
                return true;

            PlanetSaveData saveData = TryLoadFromPath(filePath);
            return string.Equals(saveData?.Metadata?.WorldId, worldId, StringComparison.Ordinal);
        }

        private static string GetSafeFileName(string worldName)
        {
            string candidate = string.IsNullOrWhiteSpace(worldName) ? "Mundo" : worldName.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
                candidate = candidate.Replace(invalidChar, '_');

            candidate = candidate.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(candidate) ? "Mundo" : candidate;
        }

        private void EnsureSaveDirectory()
        {
            Directory.CreateDirectory(SaveDirectoryPath);
        }
    }
}
