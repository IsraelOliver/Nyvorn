using Nyvorn.Source.Game.States;
using Nyvorn.Source.Gameplay.Items;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlayerSaveService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public string SaveDirectoryPath { get; }

        public PlayerSaveService()
        {
            SaveDirectoryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nyvorn",
                "Players");
        }

        public IReadOnlyList<PlayerSaveSummary> ListPlayers()
        {
            EnsureSaveDirectory();

            List<PlayerSaveSummary> summaries = new();
            foreach (string filePath in Directory.EnumerateFiles(SaveDirectoryPath, "*.ply"))
            {
                PlayerSaveData saveData = TryLoadFromPath(filePath);
                if (saveData == null)
                    continue;

                summaries.Add(new PlayerSaveSummary
                {
                    FilePath = filePath,
                    PlayerId = saveData.PlayerId,
                    Name = saveData.Name,
                    SavedAtUtc = saveData.SavedAtUtc
                });
            }

            return summaries
                .OrderByDescending(summary => summary.SavedAtUtc)
                .ToArray();
        }

        public PlayerSaveData Load(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return null;

            string filePath = GetFilePath(playerId);
            if (!File.Exists(filePath))
                return null;

            return TryLoadFromPath(filePath);
        }

        public PlayerSaveData CreatePlayer(string name)
        {
            PlayerSaveData saveData = new()
            {
                PlayerId = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "Jogador" : name.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                SavedAtUtc = DateTime.UtcNow
            };

            Save(saveData);
            return saveData;
        }

        public void Rename(string playerId, string newName)
        {
            PlayerSaveData saveData = Load(playerId);
            if (saveData == null)
                return;

            Save(new PlayerSaveData
            {
                Version = saveData.Version,
                PlayerId = saveData.PlayerId,
                Name = string.IsNullOrWhiteSpace(newName) ? saveData.Name : newName.Trim(),
                CreatedAtUtc = saveData.CreatedAtUtc,
                SavedAtUtc = saveData.SavedAtUtc,
                SelectedHotbarIndex = saveData.SelectedHotbarIndex,
                CurrentHealth = saveData.CurrentHealth,
                HotbarSlots = saveData.HotbarSlots,
                InventorySlots = saveData.InventorySlots
            });
        }

        public void Save(PlayingSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(session.PlayerId))
                return;

            PlayerSaveData existing = Load(session.PlayerId);

            Save(new PlayerSaveData
            {
                PlayerId = session.PlayerId,
                Name = existing?.Name ?? "Jogador",
                CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow,
                SavedAtUtc = DateTime.UtcNow,
                SelectedHotbarIndex = session.SelectedHotbarIndex,
                CurrentHealth = session.Player.Health,
                HotbarSlots = CaptureSlots(session.Hotbar),
                InventorySlots = CaptureSlots(session.Inventory)
            });
        }

        public void Save(PlayerSaveData saveData)
        {
            if (saveData == null)
                throw new ArgumentNullException(nameof(saveData));
            if (string.IsNullOrWhiteSpace(saveData.PlayerId))
                throw new ArgumentException("PlayerId do player save nao pode ser vazio.", nameof(saveData));

            EnsureSaveDirectory();
            string json = JsonSerializer.Serialize(saveData, JsonOptions);
            File.WriteAllText(GetFilePath(saveData.PlayerId), json);
        }

        public void Delete(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            string filePath = GetFilePath(playerId);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        // Small persisted pointer to whichever player was last active, so WorldSelectState (which
        // is freshly reconstructed every time you navigate back to it) can restore the selection
        // without threading a playerId through every state that happens to return to it.
        public string GetLastSelectedPlayerId()
        {
            try
            {
                string filePath = GetLastSelectedPointerPath();
                return File.Exists(filePath) ? File.ReadAllText(filePath).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        public void SetLastSelectedPlayerId(string playerId)
        {
            EnsureSaveDirectory();
            File.WriteAllText(GetLastSelectedPointerPath(), playerId ?? string.Empty);
        }

        private static List<PlayerInventorySlotSaveData> CaptureSlots(Inventory inventory)
        {
            List<PlayerInventorySlotSaveData> slots = new();

            for (int i = 0; i < inventory.Capacity; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot.IsEmpty)
                    continue;

                slots.Add(new PlayerInventorySlotSaveData
                {
                    SlotIndex = i,
                    ItemId = slot.ItemId,
                    Quantity = slot.Quantity
                });
            }

            return slots;
        }

        private PlayerSaveData TryLoadFromPath(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<PlayerSaveData>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private string GetFilePath(string playerId)
        {
            return Path.Combine(SaveDirectoryPath, $"{playerId}.ply");
        }

        private string GetLastSelectedPointerPath()
        {
            return Path.Combine(SaveDirectoryPath, "last_selected.txt");
        }

        private void EnsureSaveDirectory()
        {
            Directory.CreateDirectory(SaveDirectoryPath);
        }
    }
}
