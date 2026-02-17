# 개선 제안 리포트 (현재 소스/구성 기준)

## 1) 테스트 안정성 개선 (경로 의존 제거)
- `DataService`가 실행 파일 기준(`AppDomain.CurrentDomain.BaseDirectory`)으로 `data/`를 읽기 때문에, 테스트/런타임 위치가 바뀌면 실패 가능성이 있습니다.
- 제안:
  - `DataService` 생성자에 `basePath`를 주입 가능하게 변경 (옵션 매개변수)
  - 테스트에서는 명시 경로를 넣어 결정적 실행 보장

## 2) 추천 성능 개선 (선형 탐색 제거)
- `RecommendationService.ScoreAugments`에서 각 증강마다 `champion.AugmentPreferences.FirstOrDefault(...)`를 수행해 O(N*M) 구조입니다.
- 제안:
  - `augmentId -> preference` 딕셔너리 사전 구성 후 O(1) 조회

## 3) 추천 결과 일관성 개선 (동점 정렬 기준)
- 현재 점수만으로 내림차순 정렬하므로 동점일 때 노출 순서가 데이터 입력 순서에 좌우될 수 있습니다.
- 제안:
  - `ThenByDescending(Tier)` 또는 `ThenBy(Name)` 같은 tie-breaker 추가

## 4) 보안 강화 (TLS 인증서 무조건 허용 축소)
- `LcuProvider`에서 REST/WS 모두 self-signed 인증서를 전부 허용하고 있습니다.
- LCU 특성상 로컬 루프백 통신이지만, 완전 허용은 최소화가 바람직합니다.
- 제안:
  - 루프백 + 포트/프로세스 검증 조합으로 범위 제한
  - 연결 실패 로깅에 진단 정보(엔드포인트/상태) 구조화

## 5) 리소스 관리/메모리 누수 방지
- `OverlayWindow.SubscribeProviderEvents`는 이벤트 구독 해제를 하지 않습니다.
- 현재 라이프사이클상 치명적이지 않을 수 있으나, provider 교체/재연결 시 중복 구독 위험이 있습니다.
- 제안:
  - `UnsubscribeProviderEvents` 추가 또는 weak event 패턴 사용

## 6) 핫키 설정 실제 반영
- `config.json` 모델에 핫키 문자열이 있으나, `HotKeyService`는 하드코딩 키만 사용합니다.
- 제안:
  - 문자열(`Ctrl+Shift+O`) 파서 도입 후 `RegisterHotKey`에 동적 적용
  - 잘못된 입력에 대한 유효성 검사/기본값 fallback 제공

## 7) 도메인 규칙 정합성 보완
- `DeriveEnemyTags`에서 `marksman -> dps` 태그를 생성하지만, `EnemyTagWeights`에는 `dps` 매핑 속성이 없습니다.
- 제안:
  - `EnemyTagWeights`에 `dps` 추가 또는 역할-태그 매핑을 규칙 파일 기반으로 이동

## 8) 예외 처리 가시성 개선
- 일부 구간(`FallbackPollAsync`, `UpdateRecommendationsAsync`)이 광범위 `catch { }`로 침묵 실패합니다.
- 제안:
  - Debug/Trace 레벨 로깅 추가로 현장 진단 가능성 확보

## 9) 프로젝트 정리
- 여러 프로젝트에 미사용 `Class1.cs`가 남아 있어 유지보수 신뢰도를 낮춥니다.
- 제안:
  - 미사용 파일 제거

## 10) CI/CD 품질 게이트 추가
- 현재 저장소에 CI 파이프라인 정의가 보이지 않습니다.
- 제안:
  - 최소 게이트: `dotnet restore`, `dotnet build`, `dotnet test`
  - PR마다 자동 수행하여 회귀 방지
