using LSA.Core;
using LSA.Data;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

public class RecommendationServiceTests
{
    private readonly RecommendationService _service;
    private readonly DataService _dataService;

    public RecommendationServiceTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Warning));

        _dataService = new DataService(loggerFactory.CreateLogger<DataService>());
        _dataService.LoadKnowledgeBaseAsync().Wait();

        _service = new RecommendationService(
            _dataService, loggerFactory.CreateLogger<RecommendationService>());
    }

    [Fact]
    public void GetRecommendations_KnownChampion_ReturnsAugmentsAndItems()
    {
        var result = _service.GetRecommendations(222); // Jinx

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.ChampionName));
        Assert.NotEmpty(result.Augments);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public void GetRecommendations_UnknownChampion_ReturnsGenericAugmentsAndNoItems()
    {
        var result = _service.GetRecommendations(99999);

        Assert.NotNull(result);
        Assert.Contains("Unknown", result.ChampionName);
        Assert.NotEmpty(result.Augments);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void GetRecommendations_Augments_AreSortedByScoreDescending()
    {
        var result = _service.GetRecommendations(222);

        for (var i = 0; i < result.Augments.Count - 1; i++)
        {
            Assert.True(result.Augments[i].Score >= result.Augments[i + 1].Score);
        }
    }

    [Fact]
    public void GetRecommendations_ChampionSynergy_AddsReasonForPreferredAugments()
    {
        var kb = _dataService.KnowledgeBase;
        var champion = kb.Champions["222"];
        var firstPreference = champion.AugmentPreferences.FirstOrDefault();
        if (firstPreference == null) return;

        var result = _service.GetRecommendations(222);
        var boosted = result.Augments.FirstOrDefault(a => a.AugmentId == firstPreference.AugmentId);

        Assert.NotNull(boosted);
        Assert.Contains(boosted.Reasons, r => r.Contains("챔피언 시너지"));
    }

    [Fact]
    public void GetRecommendations_WithEnemyTags_DoesNotCrash_AndKeepsCardinality()
    {
        var noTags = _service.GetRecommendations(222);
        var withTags = _service.GetRecommendations(222, new List<string> { "tank", "heal" });

        Assert.NotNull(withTags);
        Assert.NotEmpty(withTags.Augments);
        Assert.Equal(noTags.Augments.Count, withTags.Augments.Count);
    }

    [Fact]
    public void FilterShownAugments_ReturnsOnlySelectedAugments_AndAddsRankReason()
    {
        var full = _service.GetRecommendations(222);
        var selected = full.Augments.Take(3).Select(a => a.AugmentId).ToList();

        var filtered = _service.FilterShownAugments(full, selected);

        Assert.Equal(3, filtered.Count);
        Assert.All(filtered, a => Assert.Contains(a.AugmentId, selected));
        Assert.Contains("추천 1순위", filtered[0].Reasons[0]);
        Assert.Contains("추천 2순위", filtered[1].Reasons[0]);
        Assert.Contains("추천 3순위", filtered[2].Reasons[0]);
    }

    [Fact]
    public void GetRecommendations_CoreItemsMarkedCorrectly()
    {
        var result = _service.GetRecommendations(222);
        var coreItems = result.Items.Where(i => i.IsCore).ToList();

        Assert.NotEmpty(coreItems);
        Assert.All(coreItems, i => Assert.Equal("코어 빌드", i.Reason));
    }

    [Fact]
    public void GetRecommendations_SituationalItems_HaveReasons()
    {
        var result = _service.GetRecommendations(222);
        var situationalItems = result.Items.Where(i => !i.IsCore).ToList();

        if (situationalItems.Any())
        {
            Assert.All(situationalItems, i => Assert.False(string.IsNullOrWhiteSpace(i.Reason)));
        }
    }

    [Fact]
    public void GetRecommendations_NullAndEmptyEnemyTags_DoNotCrash()
    {
        var empty = _service.GetRecommendations(222, new List<string>());
        var nullTags = _service.GetRecommendations(222, null);

        Assert.NotNull(empty);
        Assert.NotNull(nullTags);
        Assert.NotEmpty(empty.Augments);
        Assert.NotEmpty(nullTags.Augments);
    }

    [Fact]
    public void GetRecommendations_MultipleCalls_ReturnConsistentShape()
    {
        var result1 = _service.GetRecommendations(222);
        var result2 = _service.GetRecommendations(222);

        Assert.Equal(result1.ChampionName, result2.ChampionName);
        Assert.Equal(result1.Augments.Count, result2.Augments.Count);
        Assert.Equal(result1.Items.Count, result2.Items.Count);
    }
}
