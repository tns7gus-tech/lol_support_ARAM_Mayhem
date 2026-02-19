## 사용 방법

1. 아래 `LSA_v0.3.7.zip` 다운로드
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

## v0.3.7 Added
- 인게임(`InProgress`) 진입 시 추천표를 자동으로 고정(Freeze)하여 참고표를 유지
- Connection Log에 추천 고정 ON/OFF 상태 로그 추가

## v0.3.7 Fixed
- 인게임 동안 fallback polling/champion 이벤트가 추천표를 지우는 문제 수정
- LCU 런타임 진단 로그 강화(WS 연결/해제, 재연결 시도/성공/실패)

## v0.3.6 Included
- Connection Log 텍스트 선택/복사 UX 개선
- 상단 상태바 클릭 통과(`CT ON/OFF`) 표시 추가
- LCU lockfile/재연결 안정성 보강
