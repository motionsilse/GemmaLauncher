# Cat Loader 0.4 통합 적용·검증

2026-11-10. 수정된 [설계서](../superpowers/specs/2026-11-10-cat-loader-integration-design.md)와 [계획](../superpowers/plans/2026-11-10-cat-loader-integration.md)을 기준으로 현재 checkout에서 순차 구현했다.

## 구현 결과

- 하나의 WinForms 창·설정·트레이에 로컬 모델과 ChatGPT 연결을 통합했다. 처음 선택한 기능만 생성하고 카테고리 전환은 연결을 시작하지 않는다.
- 기존 MainWindow를 로컬 화면으로 재사용하고 LoaderWindow가 통합 셸을 담당한다. 테스트된 로컬 편집기·페이지의 동작을 유지하기 위한 구현 파일 구성 변경이다. 로컬 실행 패널은 셸의 고정 영역으로 옮겼다.
- 기존 GemmaLauncher와 CatRouteCodexProxy 경로, DPAPI entropy, 로컬 키·독점 잠금을 유지한다. 로그인 파일과 키를 다른 폴더로 복사하지 않는다.
- 공식 CLIProxyAPI v8.0.27을 고정 크기·ZIP/EXE 해시로 다운로드한다. 엔진·모델·.NET 런타임은 배포 EXE에 넣지 않는다.
- 저장 계정·엔진 생존·API 준비 상태, 수명 작업·보조 조회 상태를 구분한다. 준비 취소·중지·로그아웃은 해당 자원만 정리한다. OAuth URL·코드·토큰을 로그/복사 UI에 표시하지 않는다.
- 새 조작·오류·브라우저 콜백 문구를 17개 언어에 반영하고 원본 출처·MIT 고지를 내장했다.

## 검증 근거

모든 경로는 이 checkout 기준이며 artifacts는 Git에 포함하지 않는다.

| 검사 | 결과·근거 |
|---|---|
| Release 솔루션 빌드 | 경고 0·오류 0 |
| 전체 Core 콘솔 검사 | 통과: 계정 저장·네트워크/콜백·모델·설치·세션·게이트웨이·지역화 등 |
| 전체 네이티브 UI 검사 | 통과: `artifacts/cat-loader/final-ui.log` |
| 통합 셸 추가 검사 | 통과: `coexistence-final.log`; 계정 복원 시 로컬 미생성, 조회 중 중지, 두 기능 개별 중지, 초안 이동 취소, 종료 예외·무응답 격리 |
| 중지 중 보조 버튼 | 실패를 재현한 뒤 활성 상태 바인딩 수정, 통과: `background-stop.log` 및 최종 UI 검사 |
| 로컬 주 동작 화면 경계 | 다른 기능 실행 시 화면 아래로 밀림을 재현한 뒤 고정 배치, 통과 |
| 공식 엔진 smoke | 빈 임시 폴더에서 실제 다운로드·재사용·API 401·관리 키 분리·종료 통과 |
| 최초 사용 네이티브 버튼 | 실제 ChatGPT 로그인 버튼 → 공식 엔진 다운로드 → 브라우저 handoff → 취소·설정/자식 정리 통과: `first-use-final.log` |
| 화면 캡처 | `artifacts/cat-loader/screens/`: 17개 언어·연결 준비·작은 창·두 기능·수동 150% 확대; 실제 모니터 DPI 검사와 구분 |
| 번역·카탈로그 | 17개 언어, 3개 도메인, 언어당 436개 키; 카탈로그 일치 |
| Python 스크립트 검사 | 5개 통과 |
| 최종 배포 EXE | `artifacts/cat-loader/final-package.json`: 버전 0.4.0, 6개 모델, 17개 언어, accountInactive=true |

저장소 테스트 방식에 맞춰 여러 계획 시나리오를 AccountTests/Network 및 LoaderTests의 통합 assertions로 묶었다. 계획의 모든 테스트 이름을 별도 메서드로 생성했다는 뜻은 아니다. 테스트·공식 smoke는 임시 상태와 가짜 계정만 사용했으며 사용자 계정 파일을 읽거나 삭제하지 않았다.

## 성능과 패키지

기준 실행물은 제품 변경 전 별도 보존했다. 동일 .NET·창 크기와 가짜 세션을 사용했다. 시작 시간은 새 프로세스 실행부터 첫 HWND paint 뒤 메시지 처리까지 5회 중앙값이며 OS 파일 캐시를 강제로 비우지 않았다.

| 지표 | 기존 로컬 창 | 통합 셸 |
|---|---:|---:|
| 새 프로세스 시작 중앙값 | 1011.4ms | 1120.5ms |
| 보이는 유휴 private RAM | 60.0MiB | 64.0MiB |
| 숨긴 유휴 private RAM | 59.5MiB | 63.4MiB |
| 보이는 모의 로컬 서버 | 71.3MiB | 74.3MiB |
| 숨긴 모의 로컬 서버 | 69.5MiB | 72.9MiB |

추가 상태: 계정 화면 표시/숨김 76.2/75.9MiB, 두 기능 모의 실행 표시/숨김 76.1/74.9MiB. 추가 상태는 이미 생성한 로컬 VM과 모의 세션도 포함한다. 10초 표본의 셸 CPU 시간은 31–156ms였고 모든 상태에서 WPF 모듈은 없었다. 수치는 `baseline/startup.json`, `baseline/idle.json`, `startup-final.json`, `idle-final.json`에 보존했다. 통합 셸에 약 3–4MiB와 약 109ms의 추가 비용이 있다. 실제 엔진 RAM과 upstream 추론 성능을 측정한 결과가 아니다.

배포 파일: `dist/CatLoader-0.4.0-win-x64.exe`, 1,345,649바이트(약 1.28MiB).

SHA-256: `aca9a2b690c40105e059cf8c1ed1b191311a963c249d157b1d686f5c12f57898`.

최종 EXE 자체의 utility 검증을 실행했다. 이전 0.2/0.3 배포물은 보존했고 작업 중 첫 0.4 빌드도 `artifacts/cat-loader/pre-final-build/`에 보존했다.

## 확인 범위

실제 사용자 브라우저 인증과 ChatGPT upstream 비스트리밍/SSE 응답은 미검증이다. 이번 첫 사용 smoke는 인증 handoff에서 취소하므로 로그인·추론 성공으로 기록하지 않는다. ANIS의 기존 모델 접근·실번역 검증 제한도 그대로 남는다. GitHub 게시·저장소 이름 변경·CatRoute 보관/삭제는 수행하지 않았다.
