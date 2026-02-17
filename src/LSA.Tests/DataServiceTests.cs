using System.Text.Json;
using LSA.Data;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

public class DataServiceTests
{
    [Fact]
    public async Task LoadKnowledgeBaseAsync_MergesAugmentDictionaryEntries()
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
                    ["existing_aug"] = new() { Name = "기존 증강", Tier = "A", Tags = new List<string> { "dps" } }
                }
            };

            var dictionary = new AugmentDictionary
            {
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Entries = new Dictionary<string, AugmentDictionaryEntry>
                {
                    ["existing_aug"] = new() { Id = "existing_aug", Name = "덮어쓰면 안 됨" },
                    ["new_aug"] = new() { Id = "new_aug", Name = "신규 증강" }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(Path.Combine(dataDir, "knowledge_base.json"), JsonSerializer.Serialize(kb, options));
            await File.WriteAllTextAsync(Path.Combine(dataDir, "augments_dictionary.json"), JsonSerializer.Serialize(dictionary, options));

            using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var service = new DataService(loggerFactory.CreateLogger<DataService>(), tempRoot);

            await service.LoadKnowledgeBaseAsync();

            Assert.True(service.KnowledgeBase.Augments.ContainsKey("existing_aug"));
            Assert.True(service.KnowledgeBase.Augments.ContainsKey("new_aug"));
            Assert.Equal("기존 증강", service.KnowledgeBase.Augments["existing_aug"].Name);
            Assert.Equal("신규 증강", service.KnowledgeBase.Augments["new_aug"].Name);
            Assert.Equal("C", service.KnowledgeBase.Augments["new_aug"].Tier);
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
