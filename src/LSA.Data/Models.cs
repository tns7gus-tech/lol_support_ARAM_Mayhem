using System.Text.Json.Serialization;

namespace LSA.Data.Models;

/// <summary>
/// 게임 상태 단계 열거형
/// </summary>
public enum GamePhase
{
    None,       // 클라이언트 미연결
    Lobby,      // 로비 대기
    ChampSelect,// 챔피언 선택 중
    InProgress, // 게임 진행 중
    EndOfGame   // 게임 종료
}

/// <summary>
/// 증강 데이터 모델
/// </summary>
public class Augment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "C"; // S, A, B, C

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

/// <summary>
/// 아이템 데이터 모델
/// </summary>
public class Item
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// 챔피언 증강 선호도
/// </summary>
public class AugmentPreference
{
    [JsonPropertyName("augmentId")]
    public string AugmentId { get; set; } = "";

    [JsonPropertyName("baseBonus")]
    public int BaseBonus { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

/// <summary>
/// 상황별 아이템 추천
/// </summary>
public class SituationalItem
{
    [JsonPropertyName("itemId")]
    public int ItemId { get; set; }

    [JsonPropertyName("whenTags")]
    public List<string> WhenTags { get; set; } = new();

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

/// <summary>
/// 챔피언 아이템 빌드
/// </summary>
public class ItemBuild
{
    [JsonPropertyName("core")]
    public List<int> Core { get; set; } = new();

    [JsonPropertyName("situational")]
    public List<SituationalItem> Situational { get; set; } = new();
}

/// <summary>
/// 챔피언 데이터 모델
/// </summary>
public class Champion
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("augmentPreferences")]
    public List<AugmentPreference> AugmentPreferences { get; set; } = new();

    [JsonPropertyName("itemBuild")]
    public ItemBuild ItemBuild { get; set; } = new();
}

/// <summary>
/// 적 태그별 가중치 룰
/// </summary>
public class EnemyTagWeights
{
    [JsonPropertyName("tank")]
    public Dictionary<string, int>? Tank { get; set; }

    [JsonPropertyName("heal")]
    public Dictionary<string, int>? Heal { get; set; }

    [JsonPropertyName("burst")]
    public Dictionary<string, int>? Burst { get; set; }

    [JsonPropertyName("poke")]
    public Dictionary<string, int>? Poke { get; set; }

    [JsonPropertyName("cc")]
    public Dictionary<string, int>? Cc { get; set; }

    /// <summary>
    /// 태그 이름으로 가중치 딕셔너리 조회
    /// </summary>
    public Dictionary<string, int>? GetWeightsForTag(string tag)
    {
        return tag.ToLower() switch
        {
            "tank" => Tank,
            "heal" => Heal,
            "burst" => Burst,
            "poke" => Poke,
            "cc" => Cc,
            _ => null
        };
    }
}

/// <summary>
/// 추천 룰 설정
/// </summary>
public class Rules
{
    [JsonPropertyName("enemyTagWeights")]
    public EnemyTagWeights EnemyTagWeights { get; set; } = new();
}

/// <summary>
/// 메타 정보
/// </summary>
public class KnowledgeBaseMeta
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.1.0";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";
}

/// <summary>
/// 지식베이스 루트 모델 (knowledge_base.json)
/// </summary>
public class KnowledgeBase
{
    [JsonPropertyName("meta")]
    public KnowledgeBaseMeta Meta { get; set; } = new();

    [JsonPropertyName("augments")]
    public Dictionary<string, Augment> Augments { get; set; } = new();

    [JsonPropertyName("items")]
    public Dictionary<string, Item> Items { get; set; } = new();

    [JsonPropertyName("champions")]
    public Dictionary<string, Champion> Champions { get; set; } = new();

    [JsonPropertyName("rules")]
    public Rules Rules { get; set; } = new();
}

/// <summary>
/// 정적 증강 사전 엔트리 (자동 수집 캐시용)
/// </summary>
public class AugmentDictionaryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "communitydragon";
}

/// <summary>
/// 증강 사전 캐시 루트 모델 (data/augments_dictionary.json)
/// </summary>
public class AugmentDictionary
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("entries")]
    public Dictionary<string, AugmentDictionaryEntry> Entries { get; set; } = new();
}

/// <summary>
/// Mock 게임 상태 모델
/// </summary>
public class MockGameState
{
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "None";

    [JsonPropertyName("myChampionId")]
    public int MyChampionId { get; set; }

    [JsonPropertyName("enemyChampionIds")]
    public List<int> EnemyChampionIds { get; set; } = new();

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "ARAM_MAYHEM";
}

/// <summary>
/// 앱 설정 모델 (config.json)
/// </summary>
public class AppConfig
{
    [JsonPropertyName("overlay")]
    public OverlayConfig Overlay { get; set; } = new();

    [JsonPropertyName("hotkeys")]
    public HotkeyConfig Hotkeys { get; set; } = new();

    [JsonPropertyName("lol")]
    public LolConfig Lol { get; set; } = new();

    [JsonPropertyName("app")]
    public AppSettings App { get; set; } = new();
}

public class OverlayConfig
{
    [JsonPropertyName("x")]
    public double X { get; set; } = 1520;

    [JsonPropertyName("y")]
    public double Y { get; set; } = 120;

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.92;

    [JsonPropertyName("isClickThrough")]
    public bool IsClickThrough { get; set; } = false;

    [JsonPropertyName("isCollapsed")]
    public bool IsCollapsed { get; set; } = false;
}

public class HotkeyConfig
{
    [JsonPropertyName("toggleOverlay")]
    public string ToggleOverlay { get; set; } = "Ctrl+Shift+O";

    [JsonPropertyName("toggleClickThrough")]
    public string ToggleClickThrough { get; set; } = "Ctrl+Shift+C";

    [JsonPropertyName("devCyclePhase")]
    public string DevCyclePhase { get; set; } = "Ctrl+Shift+P";
}

public class LolConfig
{
    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = "";
}

public class AppSettings
{
    [JsonPropertyName("useMockWhenLcuMissing")]
    public bool UseMockWhenLcuMissing { get; set; } = true;
}
