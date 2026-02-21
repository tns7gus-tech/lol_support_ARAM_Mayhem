# 🎮 칼바람나락 아수라장(ARAM) 증강/아이템 추천 오버레이

> 칼바람나락 아수라장(ARAM) 게임에서 **어떤 증강을 골라야 할지** 바로 알려주는 오버레이 프로그램입니다.

---

## ⚡ 3분만에 시작하기

### 1. 다운로드

👉 [**최신 버전 다운로드 (v0.4.5)**](https://github.com/tns7gus-tech/lol_support_ARAM_Mayhem/releases/latest)

> ZIP 파일을 다운로드하고 원하는 곳에 압축 해제하세요.

### 2. 실행

```
📁 압축 해제한 폴더
├── LSA.exe       ← 이것만 더블클릭!
└── data/         ← 같은 폴더에 있어야 함 (건드리지 마세요)
```

1. **LoL 클라이언트를 먼저 실행**
2. **`LSA.exe` 더블클릭**
3. **게임 화면은 창모드 또는 테두리 없음(전체화면 창) 모드 권장**
4. 끝! 오버레이가 화면에 표시됩니다

### 3. 핫키

| 키 | 동작 |
|----|------|
| `Ctrl+Shift+O` | 오버레이 표시 / 숨김 |
| `Ctrl+Shift+C` | 클릭 통과 모드 (마우스가 오버레이를 무시) |

---

## 🎯 사용 방법

### 칼바람나락 아수라장(ARAM) 게임 진행 흐름

```
1. LoL 실행 → LSA.exe 실행
2. 칼바람나락 아수라장(ARAM) 큐 돌리기
3. 챔피언 선택 화면 → 오버레이에 추천 증강 + 아이템 자동 표시!
4. 게임 중 증강 3개 선택 화면 → 오버레이에서 최적 픽 확인
```

### 오버레이 화면 설명

- **증강 추천 (S티어 전체)** — 티어(S/A/B/C)별 색상으로 표시
  - 🟡 **S티어** = 무조건 픽
  - 🟣 **A티어** = 강추
  - 🔵 **B티어** = 괜찮음
  - ⚪ **C티어** = 차선책
- **코어 아이템** — 반드시 사야 할 아이템
- **상황템** — 적 구성에 따라 추천되는 아이템

### 증강 3개 선택 기능

게임 내에서 증강 3개가 뜨면:
1. 오버레이에서 해당 3개 증강을 **클릭**
2. 그 3개 중 **최적 선택**을 순위별로 보여줌 (1순위/2순위/3순위)

### 연결 상태 표시

| 색상 | 의미 |
|------|------|
| 🟢 **WS** | 실시간 연결 (최상) |
| 🟡 **REST** | 일반 연결 |
| 🔴 **미연결** | LoL 클라이언트를 먼저 실행하세요 |
| 🟣 **MOCK** | 테스트 모드 (LoL 없이 작동) |

---

## ❓ FAQ

### Q: PC방에서도 되나요?
**네!** LoL이 어디에 설치되어 있든 (`C:`, `D:`, `E:` 등) **자동으로 찾습니다.**
USB나 클라우드에 `LSA.exe` + `data/` 폴더만 넣어가세요.

### Q: LoL 없이도 실행할 수 있나요?
**네!** LoL이 없으면 자동으로 **MOCK 모드**로 전환됩니다.
`Ctrl+Shift+P`로 가상 게임 진행을 시뮬레이션할 수 있습니다.

### Q: 밴 당하지 않나요?
**안전합니다.** 이 프로그램은:
- ✅ 읽기 전용 API만 사용 (lockfile + REST GET)
- ❌ 자동 입력/매크로 **없음**
- ❌ 게임 메모리 접근 **없음**
- ❌ 이미지 해킹/승률 노출 **없음**

[Riot Third Party Developer Policy](https://developer.riotgames.com/docs/lol) 준수

### Q: .NET 런타임 설치해야 하나요?
**아니요!** `LSA.exe`는 self-contained 빌드라 **.NET 없이 바로 실행** 가능합니다.

---

## 🛠️ 개발자 정보

<details>
<summary>📐 프로젝트 구조 (클릭해서 펼치기)</summary>

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
</details>

<details>
<summary>🔧 빌드 & 테스트 (클릭해서 펼치기)</summary>

**요구 사항**: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# 빌드
dotnet build LSA.sln

# 실행 (Mock 모드)
dotnet run --project src/LSA.App

# 테스트
dotnet test src/LSA.Tests

# 포터블 .exe 빌드
dotnet publish src/LSA.App/LSA.App.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist
```
</details>

<details>
<summary>📊 추천 알고리즘 (클릭해서 펼치기)</summary>

```
Score = 티어 기본점수 + 챔피언 시너지 + 적 태그 카운터
          (S:100 A:80       (knowledge_base     (enemyTagWeights
           B:60  C:40)       .augmentPreferences) 룰 적용)
```

- **챔피언 시너지**: knowledge_base에서 해당 챔피언에 정의된 증강 보너스 적용
- **적 태그 카운터**: 적 챔피언 역할(Tank, Mage 등)에 따른 카운터 가중치 적용
</details>

<details>
<summary>📄 data/knowledge_base.json 구조 (클릭해서 펼치기)</summary>

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
</details>

---

## 📝 라이선스

개인 사용 목적 프로젝트입니다.  
Riot Games의 [Third Party Developer Policy](https://developer.riotgames.com/docs/lol) 준수.

