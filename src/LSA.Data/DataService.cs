using System.Text.Json;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;

namespace LSA.Data;

/// <summary>
/// JSON 파일 기반 데이터 서비스 — 지식베이스 및 설정 로드/저장
/// </summary>
public class DataService
{
    private readonly ILogger<DataService> _logger;
    private readonly string _basePath;

    // JSON 직렬화 옵션 (한글 지원 + 들여쓰기)
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public KnowledgeBase KnowledgeBase { get; private set; } = new();
    public AppConfig Config { get; private set; } = new();

    public DataService(ILogger<DataService> logger)
    {
        _logger = logger;
        // 실행 파일 기준 경로 (Portable 우선)
        _basePath = AppDomain.CurrentDomain.BaseDirectory;
    }

    /// <summary>
    /// 지식베이스 로드 (data/knowledge_base.json)
    /// </summary>
    public async Task LoadKnowledgeBaseAsync()
    {
        var path = Path.Combine(_basePath, "data", "knowledge_base.json");
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("지식베이스 파일 없음: {Path}", path);
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            KnowledgeBase = JsonSerializer.Deserialize<KnowledgeBase>(json, _jsonOptions) ?? new();
            _logger.LogInformation("지식베이스 로드 완료 — 증강: {AugCount}, 아이템: {ItemCount}, 챔피언: {ChampCount}",
                KnowledgeBase.Augments.Count, KnowledgeBase.Items.Count, KnowledgeBase.Champions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "지식베이스 로드 실패");
        }
    }

    /// <summary>
    /// Mock 게임 상태 로드 (data/mock_game_state.json)
    /// </summary>
    public async Task<MockGameState> LoadMockGameStateAsync()
    {
        var path = Path.Combine(_basePath, "data", "mock_game_state.json");
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Mock 상태 파일 없음: {Path}", path);
                return new MockGameState();
            }

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MockGameState>(json, _jsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mock 상태 로드 실패");
            return new MockGameState();
        }
    }

    /// <summary>
    /// 앱 설정 로드 (config.json)
    /// </summary>
    public async Task LoadConfigAsync()
    {
        var path = Path.Combine(_basePath, "config.json");
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("설정 파일 없음 — 기본값 사용");
                Config = new AppConfig();
                await SaveConfigAsync(); // 기본 설정 파일 생성
                return;
            }

            var json = await File.ReadAllTextAsync(path);
            Config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new();
            _logger.LogInformation("설정 로드 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 로드 실패 — 기본값 사용");
            Config = new AppConfig();
        }
    }

    /// <summary>
    /// 앱 설정 저장
    /// </summary>
    public async Task SaveConfigAsync()
    {
        var path = Path.Combine(_basePath, "config.json");
        try
        {
            var json = JsonSerializer.Serialize(Config, _jsonOptions);
            await File.WriteAllTextAsync(path, json);
            _logger.LogInformation("설정 저장 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 저장 실패");
        }
    }
}
