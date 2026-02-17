# 🚀 LSA Overlay — **최종 종합 계획서 v1.0**
> **목표:** (칼바람/아레나/증강 모드)에서 **증강 추천 + 아이템 트리 추천**을 “오버레이로” 제공하는 **무설치(Portable) PC 프로그램**  
> **핵심 원칙:** **Security First / Read-Only / 정책 리스크 최소화**  
> **최종 산출물:** `LSA.exe` (단일 실행 파일) + `data/knowledge_base.json` + `config.json` (Portable)

- **문서 업데이트:** 2026-02-16 (KST)
- **개발 환경 제약:** 노트북에 LoL 미설치 → 개발은 **Mock LCU 데이터** 기반, PC방에서 **Real LCU 자동 연결**로 검증

---

## 목차
1. [한 장 요약](#한-장-요약)  
2. [목표와 비목표](#목표와-비목표)  
3. [정책/정지 리스크 분석과 설계 원칙](#정책정지-리스크-분석과-설계-원칙)  
4. [사용자 시나리오와 UX 플로우](#사용자-시나리오와-ux-플로우)  
5. [제품 요구사항 PRD](#제품-요구사항-prd)  
6. [기술 설계 TDD](#기술-설계-tdd)  
7. [LCU 연동 설계 Real](#lcu-연동-설계-real)  
8. [Mock 연동 설계 Dev](#mock-연동-설계-dev)  
9. [데이터/지식베이스 설계](#데이터지식베이스-설계)  
10. [추천 엔진 설계](#추천-엔진-설계)  
11. [오버레이 UI 설계](#오버레이-ui-설계)  
12. [무설치 Portable 빌드/배포](#무설치-portable-빌드배포)  
13. [테스트 계획](#테스트-계획)  
14. [개발 체크리스트 WBS](#개발-체크리스트-wbs)  
15. [리스크 레지스터와 대응](#리스크-레지스터와-대응)  
16. [부록: 예시 파일/스키마](#부록-예시-파일스키마)  
17. [참고/출처](#참고출처)  

---

## 한 장 요약
### 우리가 만들 것
- LoL 플레이 중/전/후 상태를 감지해 **오버레이 창(TopMost)** 으로
  - **증강 추천(티어/태그/이유 중심)**
  - **아이템 코어/상황템 추천**
  를 보여주는 **가벼운 Companion 앱**

### 우리가 “안 할 것”(정지 리스크 줄이기)
- ❌ 메모리 읽기/인젝션/스크립팅/자동 입력(게임 조작)  
- ❌ “증강/아레나 아이템” **승률(%) 수치 표시**  
- ❌ 플레이어가 원래 알 수 없는 “게임 세션 전용 정보” 제공  
- ❌ “정답 강요” UI (한 가지 선택을 강제하는 UX)

### MVP(내일 PC방에서 써보기 목표) 핵심 기능
- LCU가 있으면 자동 연결 → **내 챔피언 자동 감지**  
- LCU가 없으면 Mock 모드로 개발/테스트 가능  
- 증강 추천은 **정답이 아니라 ‘후보 + 이유’** 형태로 제시  
- “현재 제시된 3개 증강”은 **사용자가 수동 선택**(자동 인식은 후순위)  
- `dotnet publish`로 **Single-file Portable** 배포

---

## 목표와 비목표
### 목표(Goals)
1. **실시간 상태 감지**
   - ChampSelect / InProgress / EndOfGame 상태에 따라 오버레이 자동 표시/숨김
2. **증강 추천(핵심 가치)**
   - 챔피언별 시너지 + 상황(태그 기반) 추천
   - 사용자가 “지금 뜬 3개”를 선택하면 그 3개를 **우선순위로 정렬**
3. **아이템 트리 추천**
   - 코어 빌드 + 상황템(태그 기반 스왑) 노출
4. **PC방 사용성**
   - 무설치(Portable) / 관리자 권한 없이 실행 / 설정 저장

### 비목표(Non-goals)
- 자동으로 “증강/아이템/스펠/룬”을 **선택하거나 적용**하지 않는다.
- 게임 화면을 캡처해서 OCR로 “3개 증강을 자동 인식”하는 기능은 **MVP에서 제외**(정책/안정성/오탐 리스크 큼).
- 상대 팀 궁극기 쿨다운 “자동 추적” 같은, 게임이 의도적으로 숨긴 정보를 제공하지 않는다.

---

## 정책/정지 리스크 분석과 설계 원칙
> ⚠️ 아래는 “서비스로 배포/공개”가 아니라 **개인용**이라도 동일하게 리스크가 생길 수 있는 항목들이라, 기획 단계에서부터 방어적으로 설계한다.

### 1) Riot 정책상 위험 포인트(핵심 요약)
- Riot Developer Portal(LoL 정책)에는 “**증강(Augments) 또는 Arena Mode 아이템의 승률을 표시하면 안 됨**”, “**플레이어가 원래 알 수 없던 게임 세션 정보 제공 금지**”, “**플레이 결정을 ‘지시’하는 앱 금지**”가 명시되어 있다.  
  → 따라서 **승률 퍼센트(%) 표시는 금지**, 추천은 “선택지/이유 제공” 형태로.  
- Riot Support(Third Party Applications)도 “숨겨진 정보 노출”, “대신 행동(봇/스크립팅)”, “게임 중 결론 내려주기”를 문제로 언급한다.  
  → 추천 UI가 너무 ‘정답 강요’가 되지 않도록 설계 필요.
- Vanguard FAQ에서는 “API/클라이언트/인게임 API를 쓰는 오버레이는 계속 동작할 것으로 예상되지만, 메모리 읽기는 더 이상 동작하지 않는다”고 한다. 또한 “LCU는 지원/업데이트 보장 없음”도 언급한다.  
  → **LCU Read-Only**, 깨져도 앱만 멈추게(게임 영향 0).

### 2) 우리 제품의 “컴플라이언스 가드레일” (절대 규칙)
**A. 데이터/표시 규칙**
- 승률(%) / 평균 등수 같은 **퍼포먼스 통계 수치**는 표시하지 않는다.
- 대신 아래만 표시:
  - 티어(예: S/A/B)  
  - 태그(예: anti-burst, tank-killer, poke)  
  - 추천 이유(텍스트 1~2줄)

**B. 조작 금지**
- LCU의 POST(행동) 계열을 쓰지 않는다. (수락/픽/밴/리롤/구매/설정 적용 등)
- 오버레이는 “정보 표시”만 한다.

**C. ‘결정 강요’ 최소화 UX**
- “BEST 1개”만 강하게 밀지 않는다.
- 기본 화면은 **Top 3 + 이유**로 제공하고, 사용자가 선택할 수 있게 한다.
- “현재 3개 증강 중” 기능도 **랭킹/이유 제공**까지만(선택 강제 X).

**D. 시각적 구분**
- Riot UI와 헷갈리지 않게 **LSA 로고/톤**으로 명확히 구분한다.
- 게임 HUD(미니맵/스킬바/상점 등) 가리는 기본 배치 금지(기본 위치를 안전 영역으로).

---

## 사용자 시나리오와 UX 플로우
### 핵심 사용자(너) 시나리오
1) PC방에서 LoL 실행  
2) `LSA.exe` 실행(USB/클라우드에서)  
3) ChampSelect 진입 → 내 챔피언 자동 감지 → 추천 표시  
4) (증강 모드/아레나) 증강 3개가 뜨면 → 사용자가 오버레이에서 3개를 선택 → “추천 순위/이유” 즉시 갱신  
5) 인게임에서도 코어 아이템/상황템을 빠르게 확인  
6) 게임 끝나면 오버레이 자동 숨김

### 게임 상태별 화면 정책
- **Lobby/None:** 오버레이 숨김(또는 작은 위젯만)
- **ChampSelect:** 추천 패널 표시(챔피언/증강/아이템)
- **InProgress:** 축소된 패널(필요 정보만), 핫키로 확장/축소
- **EndOfGame:** 자동 숨김

---

## 제품 요구사항 PRD
### 1) 기능 요구사항(Functional)
#### 필수(Must)
- [ ] 글로벌 핫키로 오버레이 ON/OFF
- [ ] Real LCU 연결(가능할 때) + 실패 시 안전한 fallback
- [ ] Mock 모드(LoL 없는 노트북 개발용)
- [ ] 챔피언 감지(자동) 또는 수동 선택
- [ ] 증강 추천 리스트(티어/태그/이유)
- [ ] 아이템 빌드(코어/상황템)
- [ ] 설정 저장(Portable 우선)
- [ ] 로그/에러 안내(민감정보 제외)

#### 선택(Should)
- [ ] “현재 3개 증강 중 추천 순위” (수동 입력 기반)
- [ ] 오버레이 위치 드래그/잠금
- [ ] 클릭 통과(Click-through) 토글

#### 후순위(Could)
- [ ] 사용자 커스텀 데이터 편집 UI
- [ ] 자동 업데이트(지식베이스만 교체)
- [ ] VOD/리플레이 분석(후처리)

### 2) 비기능 요구사항(Non-functional)
- **성능:** CPU/RAM 점유 최소(폴링은 1~2초 간격으로 시작)
- **안정성:** LCU 끊겨도 앱만 멈추고 게임 영향 0
- **보안:** lockfile password/토큰 로그 금지, 외부 전송 금지(기본 오프라인)
- **휴대성:** 무설치(Portable), 관리자 권한 없이 실행, 설정은 exe 폴더 우선 저장

---

## 기술 설계 TDD
### 1) 권장 기술 스택(최종 선택)
- **Language:** C# (.NET 8)
- **UI:** WPF (투명/TopMost 오버레이, Win32 interop 가능)
- **통신:** HttpClient (LCU REST), (선택) WebSocket 이벤트
- **데이터:** 로컬 JSON (`data/knowledge_base.json`)
- **빌드:** `dotnet publish` → self-contained single-file

> 참고: Node.js가 이미 설치돼 있어도, “PC방 Portable + 오버레이 + 단일 exe” 목표에는 WPF가 가장 깔끔하다.  
> (Node/Electron은 용량/배포/프로세스 수가 커지기 쉬움)

### 2) 솔루션 구조(권장)
```
LSA/
 ├─ src/
 │   ├─ LSA.App/        (WPF Overlay UI)
 │   ├─ LSA.Core/       (추천 엔진, 태그/스코어링)
 │   ├─ LSA.Lcu/        (Real LCU 연결, API Client)
 │   ├─ LSA.Mock/       (Mock Provider)
 │   └─ LSA.Data/       (지식베이스 로더, 모델)
 ├─ data/
 │   ├─ knowledge_base.json
 │   └─ mock_game_state.json
 ├─ logs/
 └─ README.md
```

### 3) 런타임 아키텍처
```
┌─────────────────────────────┐
│           LSA.App            │  WPF Overlay
│  - OverlayWindow             │
│  - SettingsWindow            │
│  - HotKeyService             │
└──────────────┬──────────────┘
               │ (calls)
┌──────────────▼──────────────┐
│         Application Services  │
│  - GameStateService           │  phase state machine
│  - RecommendationService      │  augments + items
│  - DataService                │  load JSON
└──────────────┬──────────────┘
               │ (interface)
     ┌─────────▼─────────┐
     │ IGameStateProvider │
     └───────┬───────┬───┘
             │       │
   ┌─────────▼─┐   ┌─▼──────────┐
   │ LcuProvider│   │ MockProvider│
   └────────────┘   └────────────┘
```

---

## LCU 연동 설계 Real
> **목표:** PC방에서 LoL 실행 시 **자동 연결** → 현재 상태/챔피언 파악  
> **중요:** LCU는 공식 지원이 아닌 영역이므로 “깨져도 안전”이 최우선

### 1) lockfile 기반 연결 개요
- League Client 실행 시 `lockfile`에 접속 정보가 생성됨
- 형식(일반적으로): `name:pid:port:password:protocol`
- 우리는 이것으로 `https://127.0.0.1:{port}` 로 요청 + Basic Auth 사용

### 2) lockfile 찾기 전략(우선순위)
1. **실행 중 프로세스에서 경로 추론**
   - `LeagueClientUx.exe` 실행 경로 탐색(WMI/Process API)
   - 상위 폴더에서 `lockfile` 존재 확인
2. **저장된 사용자 설정 경로**
   - `config.json`에 저장된 LoL 설치 경로
3. **수동 지정 UI**
   - “LoL 폴더 선택” 다이얼로그

> PC방은 설치 경로가 제각각일 수 있으니 “수동 지정 fallback”은 필수.

### 3) 우리가 사용할 LCU 엔드포인트(읽기 전용만)
- 상태 감지
  - `GET /lol-gameflow/v1/gameflow-phase`
- 챔피언 선택 정보
  - `GET /lol-champ-select/v1/session`
- (선택) 세션/큐 정보
  - `GET /lol-gameflow/v1/session`
- (선택) 내 소환사 정보(표시용)
  - `GET /lol-summoner/v1/current-summoner`

> ❌ POST / PATCH / DELETE 등 “행동” 엔드포인트는 MVP에서 절대 사용하지 않는다.

### 4) 폴링 전략(가벼운 방식으로 시작)
- 1~2초 간격 폴링:
  - phase 변화 감지 → UI 전환
  - ChampSelect에서 내 챔피언 ID 갱신
- 장점: 구현 쉬움, 안정적
- 단점: 반응이 살짝 느림(하지만 MVP에 충분)

### 5) 보안/로그 정책
- lockfile password는 **메모리에만**, 디스크 저장 금지
- 로그에는 절대 남기지 않기:
  - password/token/authorization header
- 로그에는 아래만:
  - phase 변화, endpoint 실패 코드(401/404/500 등), 예외 stack trace(민감정보 제거)

---

## Mock 연동 설계 Dev
> **목표:** LoL이 없는 노트북에서도 UI/추천 로직 100% 완성

### 1) Provider 인터페이스
- `IGameStateProvider`
  - `Task<GamePhase> GetPhaseAsync()`
  - `Task<int?> GetMyChampionIdAsync()`
  - `Task<List<int>> GetEnemyChampionIdsAsync()` (가능할 때만)
  - `event OnChanged` (선택: 데이터 변경 이벤트)

### 2) Mock 데이터 파일 방식
- `data/mock_game_state.json` 기반
- 개발용 핫키로 phase/champion을 바꾸면 앱이 즉시 갱신
- UI 상단에 **“MOCK MODE”** 배지 표시

---

## 데이터/지식베이스 설계
> MVP는 “데이터가 곧 제품 품질”이기 때문에, **확장 가능한 JSON 구조**를 먼저 잡는다.

### 1) 파일 구성
- `data/knowledge_base.json` : 추천 지식베이스(수동/반자동 업데이트)
- `config.json` : 사용자 설정(오버레이 위치/핫키/LoL 경로)
- `logs/app.log` : 실행 로그

### 2) 핵심 설계 철학
- 챔피언/증강/아이템/룰을 **분리**해서 유지보수 용이하게
- “승률 데이터”는 저장/표시하지 않음
- 태그 기반 룰로 “설명 가능한 추천” 제공

### 3) 태그 설계(예시)
- Enemy tags: `tank`, `burst`, `poke`, `heal`, `cc`, `shield`, `dive`
- Augment tags: `tank_killer`, `anti_heal`, `anti_burst`, `range`, `cdr`, `snowball`, `survivability`, `engage`, `kite`
- Item tags: `anti_heal`, `armor`, `mr`, `lifesteal`, `burst`, `dps`

---

## 추천 엔진 설계
### 1) 입력/출력
**입력**
- `myChampionId`
- `mode`(가능하면)
- `enemyTags` (가능하면)
- (선택) `shownAugments[3]` : 사용자가 “현재 뜬 3개”를 수동 선택한 목록

**출력**
- 추천 증강 Top N
  - `augmentId`, `tier`, `score(내부값)`, `reasons[]`
- 아이템 빌드
  - `coreItems[]`, `situationalItems[]`, `reasons[]`

### 2) 점수화(Explainable Scoring) 규칙
**기본 스코어 구성**
```
score = baseTierScore
      + championSynergyScore
      + (tagMatchScore)
      + (counterRuleScore)
      + userPreferenceScore (선택)
```
- `baseTierScore`: S=100, A=80, B=60, C=40 (예시)
- `championSynergyScore`: 챔피언이 선호하는 증강 태그/증강 자체에 가산점
- `counterRuleScore`: enemyTags에 따라 특정 태그 가중치 부여
  - 예) enemy.tankCount >= 2 → tag(tank_killer) +30
  - 예) enemy.healCount >= 1 → tag(anti_heal) +20

### 3) “현재 3개 증강 중 추천” (안전한 MVP 구현)
- LCU로 “화면에 뜬 증강 3개”를 자동으로 가져오지 못하는 경우가 많으므로(또는 정책/기술 리스크),
  - 오버레이에서 사용자가 3개를 직접 클릭/선택
  - 그 3개만 점수화 → **1~3순위 + 이유**를 표시
- 새로고침(리롤)은 게임을 조작하지 않고:
  - “리롤됨” 버튼 → UI 선택 초기화만 수행

### 4) 추천 문구 가이드(정책/UX)
- “정답” 대신:
  - “추천 1순위 / 대안 / 상황별 고려”
  - 이유를 같이 제공
- 예시:
  - ✅ “탱커가 많을 때 유리한 옵션(체력 비례/지속딜 계열)”
  - ✅ “폭딜 조합 상대로 생존력 상승(방어/회피 계열)”

---

## 오버레이 UI 설계
### 1) 오버레이 창 기본 스펙(WPF)
- Borderless, Transparent, TopMost
- 기본 위치: 화면 우측(미니맵/스킬바 안 가리는 위치)
- 드래그 이동 가능 + 위치 저장

### 2) 클릭 통과(Click-through) 모드
- 기본: 클릭 가능(설정/선택 편의)
- 인게임: 클릭 통과 ON을 권장(게임 조작 방해 최소화)
- 핫키로 토글

### 3) 화면 레이아웃(권장)
**상단 바**
- LSA 로고 / 현재 상태(ChampSelect/InProgress) / 모드
- 버튼: `설정`, `접기`, `Mock 토글(개발용)`

**본문 1: 증강 추천**
- Top 3 리스트(티어 뱃지 + 태그 + 짧은 이유)
- “현재 3개 입력” 모드(선택 UI)

**본문 2: 아이템 추천**
- 코어 아이템 3~6개
- 상황템 3~6개 (태그 기반)
- 이유 툴팁

### 4) 오버레이 UX 안전 수칙
- 화면을 가리지 않게 작은 사이즈/접기 제공
- 게임 UI와 구분되는 디자인(색/폰트/레이아웃)
- 광고/결제 CTA 같은 요소는 인게임 오버레이에 넣지 않음(추후에도)

---

## 무설치 Portable 빌드/배포
### 1) 빌드 목표
- `LSA.exe` 단일 파일 실행
- `data/` 폴더와 같은 위치에서 실행 가능
- 관리자 권한 없이 실행

### 2) publish 명령(예시)
> 프로젝트 경로/이름은 실제 csproj에 맞게 조정

```bash
dotnet publish src/LSA.App/LSA.App.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false
```

### 3) 배포 폴더 구조(권장)
```
LSA_Portable/
 ├─ LSA.exe
 ├─ data/
 │   ├─ knowledge_base.json
 │   └─ mock_game_state.json
 ├─ config.json   (첫 실행 시 생성)
 └─ logs/         (첫 실행 시 생성)
```

### 4) PC방 실행 팁
- LoL이 **전체화면(독점)**이면 오버레이가 안 보일 수 있음 → **테두리 없는 창모드/창모드** 권장
- 백신 오탐을 피하려면:
  - 실행 파일 이름을 너무 수상하게 하지 않기
  - 필요시 zip으로 묶어서 이동

---

## 테스트 계획
### 1) 노트북(LoL 없음) — Mock 테스트
- [ ] 앱 실행 → MOCK MODE 배지 확인
- [ ] phase 변경(핫키) → 화면 전환 확인
- [ ] 챔피언 ID 변경 → 추천 즉시 갱신
- [ ] “현재 3개 증강 선택” → 1~3순위 정렬 확인
- [ ] config 저장/재실행 후 유지

### 2) PC방(LoL 있음) — Real LCU 테스트
- [ ] LoL 실행 후 LSA 실행 → 자동 연결 성공/실패 메시지 확인
- [ ] ChampSelect 진입 → 내 챔피언 자동 감지
- [ ] InProgress 전환 → 오버레이 표시/접기
- [ ] 핫키 토글/클릭통과 확인
- [ ] 게임 종료 → 오버레이 자동 숨김
- [ ] LCU 끊김/재접속 상황에서 앱이 크래시 없이 복구

---

## 개발 체크리스트 WBS
> “다음 단계, 다음 단계”가 아니라 **끝까지 한 번에** 보이도록 **Phase 기반 전체 체크리스트**로 정리

### Phase 0 — 프로젝트 뼈대(필수)
- [ ] 레포/솔루션 생성, 폴더 구조 세팅
- [ ] WPF 오버레이 창(투명/TopMost/드래그) 구현
- [ ] 글로벌 핫키(Overlay ON/OFF, Click-through 토글) 구현
- [ ] config.json 저장/로드(Portable 우선) 구현
- [ ] logging 기본 구축(민감정보 제외)

### Phase 1 — Mock 기반 기능 완성(LoL 없이 개발 가능)
- [ ] `IGameStateProvider` 인터페이스 정의
- [ ] `MockProvider` 구현 + `mock_game_state.json` 로드
- [ ] Phase state machine 구현(ChampSelect/InProgress/EndOfGame)
- [ ] knowledge_base.json 로더 + 모델 구현
- [ ] 추천 엔진(룰 기반) 구현
- [ ] UI에 “증강 Top 3 + 이유”, “아이템 코어/상황템” 표시
- [ ] “현재 3개 증강 선택(수동)” UI + 정렬/하이라이트

### Phase 2 — Real LCU 연결(PC방 검증 핵심)
- [ ] LeagueClientUx 프로세스 탐색 + lockfile 경로 추론
- [ ] lockfile 파싱 → port/password 획득
- [ ] HttpClient 구성(로컬 self-signed 인증서 허용 처리)
- [ ] `LcuProvider` 구현: phase/champ-select/session 읽기
- [ ] 연결 실패 시 Mock/Manual fallback
- [ ] 예외 처리 강화(401/404/연결 끊김)

### Phase 3 — Portable 패키징 + 품질
- [ ] publish 스크립트/배치파일 추가
- [ ] 배포 폴더 구성 자동화(필요 파일 복사)
- [ ] 기본 지식베이스 데이터 최소 세트 작성(주력 챔피언 5~10)
- [ ] UI polish(접기/툴팁/배지/안전 배치)
- [ ] PC방 실전 테스트 후 버그 수정

### Phase 4 — 확장(원할 때만)
- [ ] 적 챔피언 파악 가능 시 enemyTags 강화
- [ ] 사용자 커스텀 데이터 편집 UI
- [ ] 지식베이스 업데이트(버전 체크) 기능
- [ ] Overwolf 버전(배포/컴플라이언스 강화) 별도 트랙 검토

---

## 리스크 레지스터와 대응
| 리스크 | 발생 가능성 | 영향 | 대응 |
|---|---:|---:|---|
| LCU 엔드포인트/응답 구조 변경 | 중 | 중~상 | Provider 레이어 분리 + 예외 처리 + Mock로 회귀 |
| PC방에서 exe 실행 차단/백신 오탐 | 중 | 상 | 단일 exe + 로고/서명(추후) + zip 배포 |
| 전체화면에서 오버레이 미표시 | 중 | 중 | 창모드/테두리 없는 창모드 안내 |
| “정답 강요 앱”으로 해석될 UX | 중 | 상 | Top3 + 이유 중심, 강제 문구 제거 |
| 승률/통계 노출 정책 위반 | 낮~중 | 상 | 승률 수치 저장/표시 금지, 티어/태그 중심 |
| 개인정보/토큰 로그 유출 | 낮 | 상 | 민감정보 마스킹/로그 금지 룰 적용 |
| 데이터 품질이 낮아 추천이 별로임 | 상 | 상 | 주력 챔피언부터 수동 고퀄 데이터로 시작, 점진 확장 |

---

## 부록: 예시 파일/스키마
### 1) config.json 예시(Portable)
```json
{
  "overlay": {
    "x": 1520,
    "y": 120,
    "opacity": 0.92,
    "isClickThrough": false,
    "isCollapsed": false
  },
  "hotkeys": {
    "toggleOverlay": "Ctrl+Shift+O",
    "toggleClickThrough": "Ctrl+Shift+C",
    "devCyclePhase": "Ctrl+Shift+P"
  },
  "lol": {
    "installPath": ""
  },
  "app": {
    "useMockWhenLcuMissing": true
  }
}
```

### 2) mock_game_state.json 예시
```json
{
  "phase": "ChampSelect",
  "myChampionId": 145,
  "enemyChampionIds": [64, 238, 412, 99, 31],
  "mode": "ARAM_MAYHEM"
}
```

### 3) knowledge_base.json 스키마(축약 예시)
```json
{
  "meta": {
    "version": "0.1.0",
    "updatedAt": "2026-02-16"
  },
  "augments": {
    "GiantSlayer": {
      "name": "Giant Slayer",
      "tier": "S",
      "tags": ["tank_killer", "dps"],
      "notes": "탱커/체력 많은 조합 상대로 강함"
    }
  },
  "items": {
    "6672": { "name": "Kraken Slayer", "tags": ["dps"] },
    "3124": { "name": "Guinsoo's Rageblade", "tags": ["dps", "onhit"] }
  },
  "champions": {
    "145": {
      "name": "Kai'Sa",
      "roles": ["Marksman", "Assassin"],
      "augmentPreferences": [
        { "augmentId": "GiantSlayer", "baseBonus": 25, "reason": "온힛/지속딜 시너지" }
      ],
      "itemBuild": {
        "core": [6672, 3006, 3124],
        "situational": [
          { "itemId": 3153, "whenTags": ["burst"], "reason": "폭딜 상대 생존" },
          { "itemId": 3036, "whenTags": ["tank"], "reason": "탱커 상대로 관통" }
        ]
      }
    }
  },
  "rules": {
    "enemyTagWeights": {
      "tank": { "tank_killer": 30 },
      "heal": { "anti_heal": 20 },
      "burst": { "anti_burst": 25 }
    }
  }
}
```

---

## 참고/출처
> (정책은 바뀔 수 있으니, “배포/공개”로 갈수록 주기적으로 재확인 권장)

- Riot Developer Portal — LoL 정책(게임 무결성 / Unapproved use cases: 증강·아레나 아이템 win rate 금지, 세션 정보/결정 지시 앱 금지 등)
- Riot Support — Third Party Applications(숨겨진 정보 노출/대신 행동/게임 중 결론 등 경고)
- Riot DevRel — Vanguard FAQ(메모리 리딩 불가, API/LCU 기반 오버레이는 동작 예상, LCU는 지원 보장 없음)
- OP.GG Desktop Patch Notes — 2026-02-04 “LoL 증강체 티어 오버레이” 추가 (시장 검증 관점)
