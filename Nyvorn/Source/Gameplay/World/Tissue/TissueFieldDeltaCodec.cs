using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueFieldDeltaLoadResult(
        bool IsValid,
        bool TopologyMatched,
        int LoadedCount,
        int SkippedCount);

    public static class TissueFieldDeltaCodec
    {
        private const uint Magic = 0x31444654; // TFD1
        private const byte FormatVersion = 1;

        public static byte[] Export(
            TissueField field,
            int seed,
            ulong networkHash,
            int generatorVersion)
        {
            if (field == null)
                return null;

            List<TissueFieldCell> overrides = field
                .EnumerateOverrides()
                .OrderBy(cell => (cell.Y * field.Width) + cell.X)
                .ToList();

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(generatorVersion);
            writer.Write(seed);
            writer.Write(networkHash);
            writer.Write(field.Width);
            writer.Write(field.Height);
            writer.Write(overrides.Count);

            for (int i = 0; i < overrides.Count; i++)
            {
                TissueFieldCell cell = overrides[i];
                writer.Write((cell.Y * field.Width) + cell.X);
                writer.Write(Quantize(cell.State.Presence));
                writer.Write(Quantize(cell.State.Vitality));
                writer.Write(Quantize(cell.State.Corruption));
                writer.Write(Quantize(cell.State.MemoryDensity));
                writer.Write(Quantize(cell.State.Flow));
            }

            return stream.ToArray();
        }

        public static TissueFieldDeltaLoadResult Import(
            byte[] snapshot,
            TissueField field,
            WorldMap worldMap,
            int expectedSeed,
            ulong expectedNetworkHash,
            int expectedGeneratorVersion)
        {
            if (snapshot == null || snapshot.Length == 0 || field == null || worldMap == null)
                return new TissueFieldDeltaLoadResult(false, false, 0, 0);

            try
            {
                using MemoryStream stream = new(snapshot, writable: false);
                using BinaryReader reader = new(stream);
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
                    return new TissueFieldDeltaLoadResult(false, false, 0, 0);

                int generatorVersion = reader.ReadInt32();
                int seed = reader.ReadInt32();
                ulong networkHash = reader.ReadUInt64();
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                int count = reader.ReadInt32();
                if (seed != expectedSeed ||
                    width != field.Width ||
                    height != field.Height ||
                    count < 0 ||
                    count > field.Width * field.Height)
                {
                    return new TissueFieldDeltaLoadResult(false, false, 0, 0);
                }

                bool topologyMatched = generatorVersion == expectedGeneratorVersion && networkHash == expectedNetworkHash;
                int loaded = 0;
                int skipped = 0;
                for (int i = 0; i < count; i++)
                {
                    int key = reader.ReadInt32();
                    TissueCellState state = new(
                        Dequantize(reader.ReadByte()),
                        Dequantize(reader.ReadByte()),
                        Dequantize(reader.ReadByte()),
                        Dequantize(reader.ReadByte()),
                        Dequantize(reader.ReadByte()));

                    if (key < 0 || key >= field.Width * field.Height)
                    {
                        skipped++;
                        continue;
                    }

                    int x = key % field.Width;
                    int y = key / field.Width;
                    bool canApply = state.IsNeutral ||
                                    topologyMatched ||
                                    (field.HasTissue(x, y) && worldMap.IsSolidAt(x, y));
                    if (!canApply || (!state.IsNeutral && !worldMap.IsSolidAt(x, y)))
                    {
                        skipped++;
                        continue;
                    }

                    if (field.SetOverrideFromPersistence(x, y, state))
                        loaded++;
                    else
                        skipped++;
                }

                field.MarkPersisted();
                return new TissueFieldDeltaLoadResult(true, topologyMatched, loaded, skipped);
            }
            catch (EndOfStreamException)
            {
                return new TissueFieldDeltaLoadResult(false, false, 0, 0);
            }
            catch (IOException)
            {
                return new TissueFieldDeltaLoadResult(false, false, 0, 0);
            }
        }

        private static byte Quantize(float value)
        {
            return (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue), 0, byte.MaxValue);
        }

        private static float Dequantize(byte value)
        {
            return value / (float)byte.MaxValue;
        }
    }
}
