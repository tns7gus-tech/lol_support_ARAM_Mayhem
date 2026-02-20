using System.Text.Json;
using LSA.Data;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

public class DataServiceTests
{
    [Fact]
    public async Task LoadKnowledgeBaseAsync_RefreshesExistingAugmentNames_WithoutAddingUnknownAugments()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"lsa-tests-{Guid.NewGuid():N}");
        var dataDir = Path.Combine(tempRoot, "data");
        Directory.CreateDirectory(dataDir);

        try
        {
            var kb = new KnowledgeBase
            {
                Augments = new Dictionary<string, Augment>
                {
                    ["existing_aug"] = new() { Name = "Old Name", Tier = "S", Tags = new List<string>() }
                }
            };

            var dictionary = new AugmentDictionary
            {
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Entries = new Dictionary<string, AugmentDictionaryEntry>
                {
                    ["existing_aug"] = new() { Id = "existing_aug", Name = "Official Name" },
                    ["new_aug"] = new() { Id = "new_aug", Name = "New Name" }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(
                Path.Combine(dataDir, "knowledge_base.json"),
                JsonSerializer.Serialize(kb, options));
            await File.WriteAllTextAsync(
                Path.Combine(dataDir, "augments_dictionary.json"),
                JsonSerializer.Serialize(dictionary, options));

            using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var service = new DataService(loggerFactory.CreateLogger<DataService>(), tempRoot);

            await service.LoadKnowledgeBaseAsync();

            Assert.True(service.KnowledgeBase.Augments.ContainsKey("existing_aug"));
            Assert.False(service.KnowledgeBase.Augments.ContainsKey("new_aug"));
            Assert.Equal("Official Name", service.KnowledgeBase.Augments["existing_aug"].Name);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }
}
