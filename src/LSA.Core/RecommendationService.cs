using LSA.Data;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;

namespace LSA.Core;

/// <summary>
/// 추천 엔진 — 룰 기반 증강/아이템 스코어링
/// score = baseTierScore + championSynergyScore + tagMatchScore + counterRuleScore
/// </summary>
public class RecommendationService
{
    private readonly DataService _dataService;
    private readonly ILogger<RecommendationService> _logger;

    // 티어별 기본 점수
    private static readonly Dictionary<string, int> TierScores = new()
    {
        ["S"] = 100,
        ["A"] = 80,
        ["B"] = 60,
        ["C"] = 40
    };

    public RecommendationService(DataService dataService, ILogger<RecommendationService> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    /// <summary>
    /// 챔피언 기반 전체 추천 생성
    /// </summary>
    public RecommendationResult GetRecommendations(int championId, List<string>? enemyTags = null)
    {
        var kb = _dataService.KnowledgeBase;
        var champKey = championId.ToString();
        var result = new RecommendationResult();

        // 챔피언 정보 조회
        if (!kb.Champions.TryGetValue(champKey, out var champion))
        {
            _logger.LogWarning("챔피언 ID {ChampId} 데이터 없음", championId);
            result.ChampionName = $"Unknown ({championId})";
            // 챔피언 데이터 없어도 범용 증강 추천은 제공
            result.Augments = GetGenericAugmentRecommendations(kb, enemyTags);
            return result;
        }

        result.ChampionName = champion.Name;
        result.Augments = ScoreAugments(kb, champion, enemyTags);
        result.Items = GetItemRecommendations(kb, champion, enemyTags);

        _logger.LogInformation("{ChampName} 추천 생성 완료 — 증강: {AugCount}, 아이템: {ItemCount}",
            champion.Name, result.Augments.Count, result.Items.Count);

        return result;
    }

    /// <summary>
    /// "현재 3개 증강 중 추천" — 사용자가 선택한 3개만 필터링 + 정렬
    /// </summary>
    public List<AugmentRecommendation> FilterShownAugments(
        RecommendationResult fullResult, List<string> shownAugmentIds)
    {
        var filtered = fullResult.Augments
            .Where(a => shownAugmentIds.Contains(a.AugmentId))
            .OrderByDescending(a => a.Score)
            .ToList();

        // 순위 태그 추가
        for (int i = 0; i < filtered.Count; i++)
        {
            var rank = i + 1;
            filtered[i].Reasons.Insert(0, $"추천 {rank}순위");
        }

        return filtered;
    }

    /// <summary>
    /// 증강 스코어링 — 모든 증강에 점수 부여 후 내림차순 정렬
    /// </summary>
    private List<AugmentRecommendation> ScoreAugments(
        KnowledgeBase kb, Champion champion, List<string>? enemyTags)
    {
        var recommendations = new List<AugmentRecommendation>();

        foreach (var (augId, augment) in kb.Augments)
        {
            var score = 0;
            var reasons = new List<string>();

            // 1) 기본 티어 점수
            score += TierScores.GetValueOrDefault(augment.Tier, 40);
            reasons.Add($"티어 {augment.Tier}");

            // 2) 챔피언 시너지 점수
            var synergy = champion.AugmentPreferences
                .FirstOrDefault(p => p.AugmentId == augId);
            if (synergy != null)
            {
                score += synergy.BaseBonus;
                reasons.Add($"챔피언 시너지: {synergy.Reason}");
            }

            // 3) 적 태그 카운터 점수
            if (enemyTags != null)
            {
                foreach (var enemyTag in enemyTags)
                {
                    var weights = kb.Rules.EnemyTagWeights.GetWeightsForTag(enemyTag);
                    if (weights == null) continue;

                    foreach (var augTag in augment.Tags)
                    {
                        if (weights.TryGetValue(augTag, out var bonus))
                        {
                            score += bonus;
                            reasons.Add($"카운터: {enemyTag} 상대 → {augTag} 유리");
                        }
                    }
                }
            }

            recommendations.Add(new AugmentRecommendation
            {
                AugmentId = augId,
                Name = augment.Name,
                Tier = augment.Tier,
                Score = score,
                Tags = augment.Tags,
                Reasons = reasons
            });
        }

        return recommendations
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    /// <summary>
    /// 범용 증강 추천 (챔피언 데이터 없을 때)
    /// </summary>
    private List<AugmentRecommendation> GetGenericAugmentRecommendations(
        KnowledgeBase kb, List<string>? enemyTags)
    {
        return kb.Augments
            .Select(kv => new AugmentRecommendation
            {
                AugmentId = kv.Key,
                Name = kv.Value.Name,
                Tier = kv.Value.Tier,
                Score = TierScores.GetValueOrDefault(kv.Value.Tier, 40),
                Tags = kv.Value.Tags,
                Reasons = new List<string> { $"티어 {kv.Value.Tier} (범용 추천)" }
            })
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    /// <summary>
    /// 아이템 추천 — 코어 + 상황템
    /// </summary>
    private List<ItemRecommendation> GetItemRecommendations(
        KnowledgeBase kb, Champion champion, List<string>? enemyTags)
    {
        var recommendations = new List<ItemRecommendation>();

        // 코어 아이템
        foreach (var itemId in champion.ItemBuild.Core)
        {
            var itemKey = itemId.ToString();
            var itemName = kb.Items.TryGetValue(itemKey, out var item) ? item.Name : $"Item {itemId}";
            var itemTags = item?.Tags ?? new();

            recommendations.Add(new ItemRecommendation
            {
                ItemId = itemId,
                Name = itemName,
                IsCore = true,
                Reason = "코어 빌드",
                Tags = itemTags
            });
        }

        // 상황템 — 적 태그와 매칭
        foreach (var sit in champion.ItemBuild.Situational)
        {
            var itemKey = sit.ItemId.ToString();
            var itemName = kb.Items.TryGetValue(itemKey, out var item) ? item.Name : $"Item {sit.ItemId}";
            var itemTags = item?.Tags ?? new();

            // 적 태그가 있으면 매칭되는 상황템만 강조
            var isRelevant = enemyTags == null ||
                             sit.WhenTags.Any(t => enemyTags.Contains(t));

            recommendations.Add(new ItemRecommendation
            {
                ItemId = sit.ItemId,
                Name = itemName,
                IsCore = false,
                Reason = sit.Reason + (isRelevant ? " ⭐" : ""),
                Tags = itemTags
            });
        }

        return recommendations;
    }
}
