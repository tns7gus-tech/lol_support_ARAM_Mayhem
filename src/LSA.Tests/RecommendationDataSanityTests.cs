using LSA.Core;
using LSA.Data;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

public class RecommendationDataSanityTests
{
    private readonly RecommendationService _service;
    private readonly DataService _dataService;

    public RecommendationDataSanityTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Warning));

        _dataService = new DataService(loggerFactory.CreateLogger<DataService>());
        _dataService.LoadKnowledgeBaseAsync().Wait();

        _service = new RecommendationService(
            _dataService, loggerFactory.CreateLogger<RecommendationService>());
    }

    [Theory]
    [InlineData(7)]   // Leblanc
    [InlineData(28)]  // Evelynn
    [InlineData(55)]  // Katarina
    [InlineData(60)]  // Elise
    [InlineData(76)]  // Nidalee
    [InlineData(84)]  // Akali
    [InlineData(105)] // Fizz
    [InlineData(131)] // Diana
    [InlineData(245)] // Ekko
    public void GetRecommendations_ApAssassinChampions_UseApCoreItems(int championId)
    {
        var result = _service.GetRecommendations(championId);

        var coreItemIds = result.Items
            .Where(i => i.IsCore)
            .Select(i => i.ItemId)
            .ToList();

        //  source data can rotate AP cores by patch/champion.
        // Validate broad AP core presence and block AD lethality core.
        var apCoreCandidates = new HashSet<int>
        {
            6655, // Ludens (legacy expectation)
            6653, // Liandry
            3146, // Hextech Rocketbelt
            4633, // Riftmaker
            4645, // Shadowflame
            3118, // Malignance
            3089, // Rabadon's Deathcap
            3157, // Zhonya's Hourglass
            2503  // Blackfire Torch
        };

        Assert.True(
            coreItemIds.Any(id => apCoreCandidates.Contains(id)),
            $"Expected AP core candidates but found: {string.Join(", ", coreItemIds)}");
        Assert.DoesNotContain(6692, coreItemIds); // AD lethality core
    }

    [Fact]
    public void KnowledgeBase_AllReferencedItemIds_AreDefined()
    {
        var kb = _dataService.KnowledgeBase;
        var undefined = new List<string>();

        foreach (var (championKey, champion) in kb.Champions)
        {
            foreach (var itemId in champion.ItemBuild.Core)
            {
                if (!kb.Items.ContainsKey(itemId.ToString()))
                {
                    undefined.Add($"{championKey}:{champion.Name}:core:{itemId}");
                }
            }

            foreach (var situational in champion.ItemBuild.Situational)
            {
                if (!kb.Items.ContainsKey(situational.ItemId.ToString()))
                {
                    undefined.Add($"{championKey}:{champion.Name}:situational:{situational.ItemId}");
                }
            }
        }

        Assert.True(
            undefined.Count == 0,
            $"Undefined item IDs found in knowledge base: {string.Join(", ", undefined)}");
    }
}

