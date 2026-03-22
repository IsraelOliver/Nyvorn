using Nyvorn.Source.Game.States;
using System;
using System.Collections.Generic;
using System.IO;
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

            File.Delete(filePath);
        }

        public void Save(PlayingSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            Save(new PlanetSaveData
            {
                Metadata = session.PlanetMetadata,
                SavedAtUtc = DateTime.UtcNow,
                TileChanges = session.WorldMap.TrackedTileChanges.ToList()
            });
        }

        public void Save(PlanetSaveData saveData)
        {
            if (saveData == null)
                throw new ArgumentNullException(nameof(saveData));

            EnsureSaveDirectory();
            string filePath = GetFilePath(saveData.Metadata.WorldId);
            string json = JsonSerializer.Serialize(saveData, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        private PlanetSaveData TryLoadFromPath(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<PlanetSaveData>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private string GetFilePath(string worldId)
        {
            return Path.Combine(SaveDirectoryPath, $"{worldId}.plt");
        }

        private void EnsureSaveDirectory()
        {
            Directory.CreateDirectory(SaveDirectoryPath);
        }
    }
}
