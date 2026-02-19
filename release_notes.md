## 사용 방법

1. 아래 `LSA_v0.3.6.zip` 다운로드
2. 압축 해제
3. LoL 클라이언트 실행
4. `LSA.exe` 더블클릭
5. 완료

## 단축키
- `Ctrl+Shift+O`: 오버레이 표시/숨김
- `Ctrl+Shift+C`: 클릭 통과 모드 토글
- `Ctrl+Shift+P`: [DEV] Mock Phase 전환

## 포함 파일
- `LSA.exe`: 메인 프로그램 (self-contained)
- `data/`: 증강/아이템/챔피언 데이터

## v0.3.6 Added
- Connection Log를 텍스트 선택/복사 가능한 형태로 개선
- 상단 상태바에 클릭 통과 상태(`CT ON/OFF`) 표시 추가

## v0.3.6 Fixed
- LCU lockfile 점유/갱신 타이밍 이슈 대응(재시도 + 후보 경로 계속 탐색)
- LCU 연결 판정 강화(REST probe 실패 시 연결 성공으로 처리하지 않음)
- 재연결 시 HttpClient 재생성 전 dispose 처리

## v0.3.5 Included
- 문서 및 오버레이 UI에서 깨진 한글 텍스트(인코딩 오염) 복구
- 릴리즈 노트 한글 가독성 복원

## v0.3.4 Included
- PC방/다중 드라이브 환경에서 LCU lockfile 탐지 개선
- 앱 내부 연결 진단(상태 표시/로그) 강화
- 트레이 UX 보완
