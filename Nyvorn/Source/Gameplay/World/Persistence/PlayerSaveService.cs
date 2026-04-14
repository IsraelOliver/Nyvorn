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

        public PlayerSaveData Load(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                return null;

            string filePath = GetFilePath(worldId);
            if (!File.Exists(filePath))
                return null;

            try
            {
                string json = File.ReadAllText(filePath);
                PlayerSaveData saveData = JsonSerializer.Deserialize<PlayerSaveData>(json, JsonOptions);
                return string.Equals(saveData?.WorldId, worldId, StringComparison.Ordinal)
                    ? saveData
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public void Save(PlayingSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            Save(new PlayerSaveData
            {
                WorldId = session.PlanetMetadata.WorldId,
                SavedAtUtc = DateTime.UtcNow,
                PositionX = session.Player.Position.X,
                PositionY = session.Player.Position.Y,
                SelectedHotbarIndex = session.SelectedHotbarIndex,
                HotbarSlots = CaptureSlots(session.Hotbar),
                InventorySlots = CaptureSlots(session.Inventory)
            });
        }

        public void Save(PlayerSaveData saveData)
        {
            if (saveData == null)
                throw new ArgumentNullException(nameof(saveData));
            if (string.IsNullOrWhiteSpace(saveData.WorldId))
                throw new ArgumentException("WorldId do player save nao pode ser vazio.", nameof(saveData));

            EnsureSaveDirectory();
            string json = JsonSerializer.Serialize(saveData, JsonOptions);
            File.WriteAllText(GetFilePath(saveData.WorldId), json);
        }

        public void Delete(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                return;

            string filePath = GetFilePath(worldId);
            if (File.Exists(filePath))
                File.Delete(filePath);
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

        private string GetFilePath(string worldId)
        {
            return Path.Combine(SaveDirectoryPath, $"{worldId}.jgr");
        }

        private void EnsureSaveDirectory()
        {
            Directory.CreateDirectory(SaveDirectoryPath);
        }
    }
}
