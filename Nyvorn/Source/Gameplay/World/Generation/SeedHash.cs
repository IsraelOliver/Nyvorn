using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Nyvorn.Source.World.Generation
{
    public static class SeedHash
    {
        public const string Domain = "Nyvorn:v1";

        public static ulong Hash64(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] hash = SHA256.HashData(bytes);
            return BinaryPrimitives.ReadUInt64LittleEndian(hash);
        }

        public static ulong Derive(ulong masterSeed, string label)
        {
            return Hash64($"{Domain}:{masterSeed:X16}:{label}");
        }

        public static int ToIntSeed(ulong seed)
        {
            int value = unchecked((int)(seed & 0x7FFFFFFFUL));
            return value == 0 ? 1 : value;
        }

        public static string NormalizeSeedText(string seedText)
        {
            string normalized = (seedText ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? WorldGenConfig.DefaultSeed.ToString() : normalized;
        }

        public static string CreateRandomSeedText()
        {
            return RandomNumberGenerator.GetInt32(WorldGenConfig.MinimumRandomSeed, int.MaxValue).ToString();
        }
    }
}
