## 사용 방법

1. 아래 `LSA_v0.4.5.zip` 다운로드
2. 압축 해제
3. LoL 클라이언트 실행
4. `LSA.exe` 더블클릭
5. 게임 화면은 **창모드 또는 테두리 없음 모드** 권장
6. 완료

## 단축키
- `Ctrl+Shift+O`: 오버레이 표시/숨김
- `Ctrl+Shift+C`: 클릭 통과 모드 토글
- `Ctrl+Shift+P`: [DEV] Mock Phase 전환

## 포함 파일
- `LSA.exe`: 메인 프로그램 (self-contained)
- `data/`: 증강/아이템/챔피언 데이터

## v0.3.9 Added
- AP 암살자 챔피언(르블랑/이블린/카타리나/엘리스/니달리/아칼리/피즈/다이애나/에코) 아이템/증강 추천을 AP 빌드로 보정
- knowledge_base 누락 아이템 ID(3143/3190/4401/6693) 정의 추가
- 데이터 생성 스크립트에 챔피언별 AP 오버라이드 템플릿 추가
- 데이터 무결성 및 AP 코어 회귀 테스트 추가

## v0.3.8 Added
- 모니터/해상도 변경 시 오버레이 위치 자동 복구(화면 밖 방지)
- 오버레이 재표시 시 자동 가시 영역 보정
- 앱 내에 인게임 표시 안정성 안내(테두리 없음/창 모드 권장) 추가

## v0.3.8 Fixed
- 저장된 좌표가 잘못되어 오버레이가 안 보이는 문제 보정
- 표시 토글/클릭 통과/디스플레이 변경 시 topmost 재적용 강화

## v0.3.7 Included
- 인게임 추천 고정(정적 참고표 유지)
- LCU 런타임 진단 로그 강화

## v0.4.2 Added
- 챔피언별 OP.GG ARAM Mayhem 증강/아이템 데이터 전체 수집 파이프라인 추가
- 증강 추천 UI Top 8 제한 제거 (챔피언별 S티어 전체 표시)
- 스몰더 포함 챔피언별 S티어/템트리 데이터 정합성 보강

## v0.4.2 Fixed
- 챔피언 무관 공용 S티어 노출 문제 수정 (챔피언별 증강 풀만 표시)
- 일부 증강명이 `???`로 보이던 매핑 누락 보정

## v0.4.3 Fixed
- 아이템/챔피언 이름 한글 인코딩 깨짐(모지바케) 복구
- 아이템 섹션 `Situational` 라벨을 `상황별`로 변경
- OP.GG 동기화 스크립트의 Data Dragon 로딩 경로를 UTF-8 안전 방식으로 보강

## v0.4.4 Changed
- 오버레이를 스크롤 없는 미니멀 레이아웃으로 재구성(정보 한 화면 표시)
- UI 전체 크기/폰트/여백을 50%+ 축소
- 티어 문자(`S/A/B/C`) 제거, 색상 점만 유지
- 증강/아이템 `reason` 텍스트 및 선택 힌트 제거

## v0.4.4 Removed
- 연결 로그/핫키 안내 등 부가 문구 표시 제거
- `knowledge_base.json`의 불필요 OP.GG 문구 제거
  - `notes: source: op.gg aram-mayhem + communitydragon ko_kr`
  - `reason: OP.GG S-tier (ARAM Mayhem)`
  - `reason: OP.GG alternative core build`

## v0.4.5 Changed
- 오버레이 전체 폰트를 `20px`로 확대해 가독성 개선
- 접기 상태 높이를 조정해 큰 폰트에서도 잘림 방지
- README 실행 가이드에 `창모드/테두리 없음 모드 권장` 문구 추가

## v0.4.5 Restored
- 오버레이 내 `Connection Log` 창 복원
- 연결 상태(CT/WS/REST/MOCK) 표시 복원
