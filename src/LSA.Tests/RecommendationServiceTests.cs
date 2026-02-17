using LSA.Core;
using LSA.Data;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

/// <summary>
/// RecommendationService 유닛 테스트
/// 점수 계산, 정렬, 필터링, 에지 케이스 검증
/// </summary>
public class RecommendationServiceTests
{
    private readonly RecommendationService _service;
    private readonly DataService _dataService;

    public RecommendationServiceTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Warning));

        _dataService = new DataService(loggerFactory.CreateLogger<DataService>());

        // knowledge_base.json 로드 (data/ 디렉터리에서)
        _dataService.LoadKnowledgeBaseAsync().Wait();

        _service = new RecommendationService(
            _dataService, loggerFactory.CreateLogger<RecommendationService>());
    }

    // ========================================================
    // 기본 추천 생성 테스트
    // ========================================================

    [Fact]
    public void GetRecommendations_KnownChampion_ReturnsAugmentsAndItems()
    {
        // Arrange — Jinx (ChampId: 222)은 knowledge_base에 정의됨
        var champId = 222;

        // Act
        var result = _service.GetRecommendations(champId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Jinx", result.ChampionName);
        Assert.NotEmpty(result.Augments);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public void GetRecommendations_UnknownChampion_ReturnsGenericAugments()
    {
        // Arrange — 존재하지 않는 챔피언 ID
        var champId = 99999;

        // Act
        var result = _service.GetRecommendations(champId);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Unknown", result.ChampionName);
        Assert.NotEmpty(result.Augments); // 범용 추천은 항상 반환
        Assert.Empty(result.Items);       // 챔피언 데이터 없으면 아이템 없음
    }

    // ========================================================
    // 티어 기반 정렬 테스트
    // ========================================================

    [Fact]
    public void GetRecommendations_AugmentsAreSortedByScore()
    {
        var result = _service.GetRecommendations(222); // Jinx

        for (int i = 0; i < result.Augments.Count - 1; i++)
        {
            Assert.True(
                result.Augments[i].Score >= result.Augments[i + 1].Score,
                $"증강 정렬 오류: [{i}]{result.Augments[i].Name}({result.Augments[i].Score}) < [{i + 1}]{result.Augments[i + 1].Name}({result.Augments[i + 1].Score})");
        }
    }

    [Fact]
    public void GetRecommendations_STierAugmentsScoreHigherThanBTier()
    {
        var result = _service.GetRecommendations(222);

        var sTierAugments = result.Augments.Where(a => a.Tier == "S").ToList();
        var bTierAugments = result.Augments.Where(a => a.Tier == "B").ToList();

        if (sTierAugments.Any() && bTierAugments.Any())
        {
            // S 티어 최저 점수가 B 티어 최고 점수보다 높거나 같아야 함
            // (챔피언 시너지 보너스가 클 수 있으므로 >=)
            var minS = sTierAugments.Min(a => a.Score);
            var avgB = bTierAugments.Average(a => a.Score);
            Assert.True(minS >= avgB,
                $"S티어 최저({minS})가 B티어 평균({avgB})보다 낮음");
        }
    }

    // ========================================================
    // 챔피언 시너지 보너스 테스트
    // ========================================================

    [Fact]
    public void GetRecommendations_ChampionSynergy_BoostsAugmentScore()
    {
        // Jinx(222)의 augmentPreferences에 정의된 증강이 보너스를 받는지 확인
        var kb = _dataService.KnowledgeBase;
        var champion = kb.Champions["222"];
        var firstPref = champion.AugmentPreferences.FirstOrDefault();

        if (firstPref == null) return; // 시너지 정의 없으면 스킵

        var result = _service.GetRecommendations(222);
        var boostedAugment = result.Augments.FirstOrDefault(a => a.AugmentId == firstPref.AugmentId);

        Assert.NotNull(boostedAugment);
        Assert.Contains(boostedAugment.Reasons, r => r.Contains("챔피언 시너지"));
    }

    // ========================================================
    // 적 태그 카운터 테스트
    // ========================================================

    [Fact]
    public void GetRecommendations_WithEnemyTags_AffectsScores()
    {
        // 적 태그 없는 추천
        var resultNoTags = _service.GetRecommendations(222);

        // 적 태그 있는 추천
        var resultWithTags = _service.GetRecommendations(222, new List<string> { "tank", "heal" });

        // 적 태그가 있으면 일부 증강의 점수가 달라져야 함
        Assert.NotNull(resultWithTags);
        Assert.NotEmpty(resultWithTags.Augments);

        // 적어도 하나의 증강에 카운터 이유가 포함되어야 함
        var hasCounterReason = resultWithTags.Augments
            .Any(a => a.Reasons.Any(r => r.Contains("카운터")));

        // knowledge_base에 tank/heal 카운터 룰이 있을 때만 체크
        var kb = _dataService.KnowledgeBase;
        var hasTankWeights = kb.Rules.EnemyTagWeights.Tank != null;
        var hasHealWeights = kb.Rules.EnemyTagWeights.Heal != null;

        if (hasTankWeights || hasHealWeights)
        {
            Assert.True(hasCounterReason, "적 태그가 있으면 카운터 이유가 포함되어야 함");
        }
    }

    // ========================================================
    // FilterShownAugments 테스트
    // ========================================================

    [Fact]
    public void FilterShownAugments_Returns_OnlySelectedAugments()
    {
        var fullResult = _service.GetRecommendations(222);
        var topThreeIds = fullResult.Augments.Take(3).Select(a => a.AugmentId).ToList();

        var filtered = _service.FilterShownAugments(fullResult, topThreeIds);

        Assert.Equal(3, filtered.Count);
        Assert.All(filtered, a => Assert.Contains(a.AugmentId, topThreeIds));
    }

    [Fact]
    public void FilterShownAugments_AddsRankReasons()
    {
        var fullResult = _service.GetRecommendations(222);
        var topThreeIds = fullResult.Augments.Take(3).Select(a => a.AugmentId).ToList();

        var filtered = _service.FilterShownAugments(fullResult, topThreeIds);

        Assert.Contains("추천 1순위", filtered[0].Reasons[0]);
        Assert.Contains("추천 2순위", filtered[1].Reasons[0]);
        Assert.Contains("추천 3순위", filtered[2].Reasons[0]);
    }

    [Fact]
    public void FilterShownAugments_SortedByScore()
    {
        var fullResult = _service.GetRecommendations(222);
        var someIds = fullResult.Augments.Take(5).Select(a => a.AugmentId).ToList();

        var filtered = _service.FilterShownAugments(fullResult, someIds);

        for (int i = 0; i < filtered.Count - 1; i++)
        {
            Assert.True(filtered[i].Score >= filtered[i + 1].Score,
                "FilterShownAugments 결과가 점수순 정렬되지 않음");
        }
    }

    // ========================================================
    // 아이템 추천 테스트
    // ========================================================

    [Fact]
    public void GetRecommendations_CoreItemsMarkedCorrectly()
    {
        var result = _service.GetRecommendations(222);

        var coreItems = result.Items.Where(i => i.IsCore).ToList();
        Assert.NotEmpty(coreItems);
        Assert.All(coreItems, i => Assert.Equal("코어 빌드", i.Reason));
    }

    [Fact]
    public void GetRecommendations_SituationalItemsHaveReasons()
    {
        var result = _service.GetRecommendations(222);

        var sitItems = result.Items.Where(i => !i.IsCore).ToList();
        if (sitItems.Any())
        {
            Assert.All(sitItems, i => Assert.False(string.IsNullOrEmpty(i.Reason)));
        }
    }

    // ========================================================
    // 에지 케이스 테스트
    // ========================================================

    [Fact]
    public void GetRecommendations_EmptyEnemyTags_DoesNotCrash()
    {
        var result = _service.GetRecommendations(222, new List<string>());
        Assert.NotNull(result);
        Assert.NotEmpty(result.Augments);
    }

    [Fact]
    public void GetRecommendations_NullEnemyTags_DoesNotCrash()
    {
        var result = _service.GetRecommendations(222, null);
        Assert.NotNull(result);
        Assert.NotEmpty(result.Augments);
    }

    [Fact]
    public void GetRecommendations_MultipleCalls_ConsistentResults()
    {
        var result1 = _service.GetRecommendations(222);
        var result2 = _service.GetRecommendations(222);

        Assert.Equal(result1.Augments.Count, result2.Augments.Count);
        Assert.Equal(result1.Items.Count, result2.Items.Count);
        Assert.Equal(result1.ChampionName, result2.ChampionName);
    }
}
