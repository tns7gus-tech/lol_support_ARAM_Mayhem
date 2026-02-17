namespace LSA.Core;

/// <summary>
/// 추천 결과 모델 — 증강/아이템 추천 출력
/// </summary>
public class AugmentRecommendation
{
    /// <summary>증강 ID (knowledge_base 키)</summary>
    public string AugmentId { get; set; } = "";

    /// <summary>증강 이름</summary>
    public string Name { get; set; } = "";

    /// <summary>티어 (S/A/B/C)</summary>
    public string Tier { get; set; } = "C";

    /// <summary>내부 점수 (표시하지 않음 — 정렬용)</summary>
    public int Score { get; set; }

    /// <summary>추천 이유 목록</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>태그 목록</summary>
    public List<string> Tags { get; set; } = new();
}

public class ItemRecommendation
{
    /// <summary>아이템 ID</summary>
    public int ItemId { get; set; }

    /// <summary>아이템 이름</summary>
    public string Name { get; set; } = "";

    /// <summary>코어 아이템 여부</summary>
    public bool IsCore { get; set; }

    /// <summary>추천 이유</summary>
    public string Reason { get; set; } = "";

    /// <summary>태그 목록</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// 전체 추천 결과
/// </summary>
public class RecommendationResult
{
    /// <summary>내 챔피언 이름</summary>
    public string ChampionName { get; set; } = "";

    /// <summary>증강 추천 목록 (점수 내림차순)</summary>
    public List<AugmentRecommendation> Augments { get; set; } = new();

    /// <summary>아이템 추천 목록</summary>
    public List<ItemRecommendation> Items { get; set; } = new();

    /// <summary>"현재 3개 증강 중 추천" — 필터링 결과</summary>
    public List<AugmentRecommendation>? FilteredAugments { get; set; }
}
