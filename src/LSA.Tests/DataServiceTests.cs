using LSA.Data;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

public class DataServiceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "knowledge_base.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public async Task LoadKnowledgeBaseAsync_WithExplicitBasePath_LoadsData()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Warning));

        var dataService = new DataService(
            loggerFactory.CreateLogger<DataService>(),
            FindRepoRoot());

        await dataService.LoadKnowledgeBaseAsync();

        Assert.NotEmpty(dataService.KnowledgeBase.Augments);
        Assert.NotEmpty(dataService.KnowledgeBase.Items);
        Assert.NotEmpty(dataService.KnowledgeBase.Champions);
    }
}
