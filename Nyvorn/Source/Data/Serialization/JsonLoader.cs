using System;
using System.IO;
using System.Text.Json;

namespace Nyvorn.Source.Data.Serialization
{
    public static class JsonLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static T LoadContentData<T>(string fileName)
        {
            string filePath = ResolveContentDataPath(fileName);
            string json = File.ReadAllText(filePath);
            T result = JsonSerializer.Deserialize<T>(json, Options);
            if (result == null)
                throw new InvalidOperationException($"Content data file '{fileName}' is empty or invalid.");

            return result;
        }

        private static string ResolveContentDataPath(string fileName)
        {
            string normalized = fileName.Replace('/', Path.DirectorySeparatorChar);
            string relativePath = Path.Combine("Content", "data", normalized);

            foreach (string root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                string direct = Path.Combine(root, relativePath);
                if (File.Exists(direct))
                    return direct;

                string projectNested = Path.Combine(root, "Nyvorn", relativePath);
                if (File.Exists(projectNested))
                    return projectNested;

                string ancestor = FindInAncestors(root, relativePath);
                if (ancestor != null)
                    return ancestor;
            }

            throw new FileNotFoundException($"Content data file '{fileName}' was not found under Content/data.");
        }

        private static string FindInAncestors(string start, string relativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                    return candidate;

                string nestedCandidate = Path.Combine(directory.FullName, "Nyvorn", relativePath);
                if (File.Exists(nestedCandidate))
                    return nestedCandidate;

                directory = directory.Parent;
            }

            return null;
        }
    }
}
