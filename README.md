# Your.Gengi — ARAM 증강/아이템 오버레이

ARAM(특히 Arena·Mayhem 기간) 게임을 위한 **실시간 증강·아이템 추천 오버레이**.  
League Client와 직접 연동하여 챔피언 선택 시 최적의 증강과 빌드를 즉시 제안합니다.

> ⚠️ **Riot API 정책 준수** — 읽기 전용 API만 사용하며, 자동 입력·매크로·승률 노출 기능은 일절 포함하지 않습니다.

---

## ✨ 주요 기능

| 기능 | 설명 |
|------|------|
| 🎯 **증강 추천** | 티어(S/A/B/C) + 챔피언 시너지 + 적 태그 카운터 기반 스코어링 |
| 🛡️ **아이템 추천** | 코어 빌드 + 상황템 (적 구성에 따라 ⭐ 강조) |
| 🔌 **LCU WebSocket** | 실시간 Phase/ChampSelect 이벤트 수신 (WAMP 프로토콜) |
| 🔄 **자동 재연결** | 지수 백오프 (2s→30s) + LeagueClientUx 프로세스 감시 |
| 🎮 **Mock 모드** | LoL 없이도 개발/테스트 가능 |
| 🖥️ **WPF 오버레이** | 투명/TopMost/드래그/클릭 통과 지원 |

---

## 🏗️ 프로젝트 구조

```
LSA.sln
├── src/
│   ├── LSA.Data/      # 데이터 모델 + JSON 읽기/쓰기
│   ├── LSA.Core/      # 추천 엔진 + IGameStateProvider 인터페이스
│   ├── LSA.Lcu/       # LCU 연결 (lockfile + REST + WebSocket)
│   ├── LSA.Mock/      # Mock Provider (개발/테스트용)
│   ├── LSA.App/       # WPF 오버레이 앱
│   └── LSA.Tests/     # xUnit 테스트
├── data/
│   ├── knowledge_base.json   # 증강/아이템/챔피언/룰 데이터
│   └── mock_game_state.json  # Mock 시나리오 데이터
└── dist/              # 빌드 출력 (LSA.exe)
```

---

## 🚀 빠른 시작

### 요구 사항
- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 빌드 & 실행

```powershell
# 빌드
dotnet build LSA.sln

# 실행 (Mock 모드 — LoL 없이 테스트)
dotnet run --project src/LSA.App

# 테스트
dotnet test src/LSA.Tests
```

### 포터블 .exe 빌드

```powershell
dotnet publish src/LSA.App/LSA.App.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o dist
```

> `dist/LSA.exe` (~69 MB) + `dist/data/` 폴더를 함께 배포하세요.

---

## ⌨️ 핫키

| 키 | 동작 |
|----|------|
| `Ctrl+Shift+O` | 오버레이 표시/숨김 |
| `Ctrl+Shift+C` | 클릭 통과 모드 토글 |
| `Ctrl+Shift+P` | [개발용] Mock Phase 순환 |

---

## 🔌 연결 상태

| 인디케이터 | 의미 |
|------------|------|
| 🟢 **WS** | WebSocket 실시간 연결 |
| 🟡 **REST** | REST API fallback |
| 🔴 **미연결** | LCU 미연결 |
| 🟣 **MOCK** | Mock 모드 |

---

## 📊 추천 알고리즘

```
Score = 티어 기본점수 + 챔피언 시너지 + 적 태그 카운터
          (S:100 A:80       (knowledge_base     (enemyTagWeights
           B:60  C:40)       .augmentPreferences) 룰 적용)
```

**입력**:  
- 내 챔피언 ID → 시너지 보너스  
- 적 챔피언 역할 → 태그 변환 → 카운터 가중치  

**출력**:  
- 증강 Top 8 (점수 내림차순)  
- 코어 아이템 + 상황템 (⭐ 적 매칭)

---

## 🛠️ data/knowledge_base.json 구조

```jsonc
{
  "meta": { "version": "0.1.0" },
  "augments": { "aug_id": { "name": "이름", "tier": "S", "tags": ["tag"] } },
  "items":    { "item_id": { "name": "이름", "tags": ["tag"] } },
  "champions": {
    "champion_id": {
      "name": "이름",
      "roles": ["Marksman"],
      "augmentPreferences": [{ "augmentId": "aug_id", "baseBonus": 20, "reason": "..." }],
      "itemBuild": { "core": [1234], "situational": [{ "itemId": 5678, "whenTags": ["tank"] }] }
    }
  },
  "rules": { "enemyTagWeights": { "tank": { "armorPen": 15 } } }
}
```

데이터를 수정하면 앱 재시작 시 자동 반영됩니다.

---

## 📝 라이선스

개인 사용 목적 프로젝트입니다.  
Riot Games의 [Third Party Developer Policy](https://developer.riotgames.com/docs/lol) 준수.
