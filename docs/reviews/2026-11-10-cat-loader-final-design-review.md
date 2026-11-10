# Cat Loader 최종 설계 리뷰

리뷰 기준: GemmaLauncher `90a39a6`의 설계서·구현 계획, 제품 코드 `da4cd94`, CatRoute 제품 코드 `c5c74ad`. 2026-11-10.

사용자의 “설계를 최종 리뷰후 픽스할거 있으면 해” 지시에 따라 설계와 구현 계획을 함께 수정했다. `ce-doc-review`의 일관성·실현 가능성·제품·디자인·보안·범위·실패 시나리오 관점으로 순차 검토했다. AGENTS.md에 따라 본 에이전트가 수행했으며 독립 리뷰어를 실행한 결과는 아니다. 문서 유형은 설계서=requirements, 구현 계획=plan이고 승인된 설계서를 계획의 근거로 사용했다.

## 수정한 문제

### F1 · P1 · 계정 잠금이 로그아웃 정리보다 먼저 해제됨

- 근거: 기존 계획 Task 2는 “Stop/Dispose는 자신의 프로세스·설정만 정리하고 잠금을 해제한다”고 했으나 Task 3은 중지 후 로그인 파일을 삭제한다. 원본 `CliProxyHost.cs:121`의 ClearLocalLoginAsync는 StopAsync 후 삭제하고, 소유권은 DisposeAsync(`:136`)까지 유지한다.
- 영향: 계획대로 Stop에서 lease를 해제하면 파일 삭제 전에 기존 CatRoute가 잠금을 획득할 수 있다. 준비 도중 잠금을 누가 소유하는지도 불명확했다.
- 수정: AccountConnection이 변경 전부터 다운로드·실행·중지·로그아웃 정리까지 lease 하나를 보유한다. host는 lease를 관리하지 않는다. 잠금 이후 계정 재확인, 정지 확인 전 삭제 금지, 확인된 codex JSON만 삭제를 고정했다.
- 검증 계획: `OwnershipCoversSignOutDeletion`, `AccountChangedBeforeOwnershipIsRechecked`, 종료 확인 실패와 설정 삭제 실패를 구분하는 테스트. Tasks 1–3.
- 확신: 100. 분류: 오류, 구현 방식 보완. **반영 완료.**

### F2 · P1 · 엔진 실행과 연결 준비, 보조 작업이 같은 상태로 읽힘

- 근거: 계약에 IsRunning만 있었고 원본 `CliProxyHost.cs:23`은 프로세스 생존만 반환한다. Task 4는 IsBusy 하나와 PrimaryCommand를 정의하면서 조회 중 중지 버튼의 동작을 정하지 않았다. 설계의 “준비 중/연결 중” 표현도 혼동 가능했다.
- 영향: 로그인 대기에서 연결 가능으로 표시하거나, 사용량 조회 중 주 버튼이 작업 취소로 바뀌어 서버 중지 위치가 사라질 수 있다. 로그인 중 탭을 다시 열면 저장 상태 갱신이 진행 상태를 덮을 수도 있다.
- 수정: IsReady와 StatusChanged, IsQueryBusy와 CancelQueryCommand를 구분했다. 상태별 주/보조 버튼 표, 준비 작업 중 읽기 활성화의 제한, 작업 세대 번호, 취소를 gate 대기보다 먼저 전달하는 규칙을 추가했다.
- 검증 계획: `UnexpectedExitClearsReadiness`, `StopDuringUsageKeepsStopAvailable`, `ReturningDuringLoginPreservesProgress`, `StopCancelsBeforeWaitingForLifecycleGate`. Tasks 3–4.
- 확신: 75. 분류: 누락, 상태 계약 보완. **반영 완료.**

### F3 · P1 · 종료 예외는 다뤘으나 무응답과 초기화 실패는 빠짐

- 근거: 기존 계획은 “각각의 cleanup을 모두 시도”와 예외 테스트만 지정했다. CatRoute `OwnedProcess.cs:62`는 종료 및 pipe drain을 제한 없이 기다린다. 현재 Gemma App.cs:63의 생성 실패 경로는 App.Dispose를 부르지만 VM의 비동기 정리와 동일하지 않다.
- 영향: 원본 정리를 그대로 이관하거나 순차 await하면 한쪽이 멈춰 두 번째 서비스와 창 종료가 지연될 수 있다. 초기 창 생성 실패 시 앞서 만든 서비스·구독도 남을 수 있다.
- 수정: 두 기능 취소를 먼저 전달하고 각 정리를 모두 시작한다. host의 job/kill/drain 제한과 셸의 30초 대기 한도를 정했다. 동기 예외도 격리하고 초기화 실패를 포함한다. 제한 시간 초과를 성공으로 보고하지 않는다. 계정 예외가 기존 App 로그에 전달되는 경계도 안전한 오류로 제한했다.
- 검증 계획: `HostCleanupIsBounded`, `HungCleanupDoesNotBlockOtherFeature`, `StartupFailureDisposesCreatedFeature`, `AccountFailureDoesNotLeakThroughAppLog`. Tasks 2·5.
- 확신: 75. 분류: 누락, 수명·실패 처리 보완. **반영 완료.**

### F4 · P2 · 손상 상태와 로그인 후 준비 실패의 복구 동작이 불명확함

- 근거: SavedAccountState에 Unreadable/Multiple이 있으나 기존 주 동작은 계정 유무 네 경우만 다뤘다. 원본 CodexLogin.cs:134에서 계정 저장 완료와 이후 모델 준비는 다른 단계다.
- 영향: 읽기 오류를 계정 없음으로 취급해 반복 로그인하거나, 계정 저장에는 성공했는데 모델 확인에 실패한 뒤 이미 저장된 계정을 다시 로그인하라는 안내가 생길 수 있다.
- 수정: 손상·복수 계정은 연결을 막고 저장 폴더 열기/쓰기 없는 상태 재확인을 제공한다. Missing은 정상적인 읽기 결과에만 사용한다. 모델 확인 실패·취소는 저장 계정을 보존하고 서버만 정리한다. 로그아웃 파일 삭제 실패도 성공으로 숨기지 않는다.
- 검증 계획: `UnreadableStateDoesNotOfferLogin`, `SavedLoginSurvivesModelCheckFailure`, 손상·복수·접근 거부 테스트. Tasks 1·3·4.
- 확신: 75. 분류: 누락, 복구 동작 보완. **반영 완료.**

### F5 · P2 · 계정 엔진 installer의 저장 경로와 검색 범위 미지정

- 근거: 기존 계획은 IInstallationService 재사용만 지시했다. 현재 InstallationService.cs:25–31은 생성 기본값으로 WinGet 모델 디렉터리를 탐색하며 인스턴스별 gate(:18)와 LauncherPaths를 갖는다.
- 영향: 로컬 installer를 넘기면 계정 작업이 로컬 설치 gate와 저장 경로를 공유한다. 기본 생성자를 사용해도 사용하지 않는 로컬 모델 경로를 읽어 지연 생성 원칙을 어길 수 있다.
- 수정: 연결 명령 이후 계정 전용 LauncherPaths와 `discoverExistingModels: false`로 별도 생성한다. 기존·새 엔진 모두 고정 EXE 해시를 다시 검사하며 원본 설치 서비스의 ZIP 검증을 재사용한다.
- 검증 계획: `AccountRuntimeDoesNotScanLocalModels`, `AccountInstallDoesNotBlockLocalInstall`, 기존 엔진 재사용·손상 검사. Task 2.
- 확신: 75. 분류: 누락, 코드에 근거한 구성 보완. **반영 완료.**

### F6 · P2 · 조작 목록은 있으나 화면 위계와 키보드 동작이 부족함

- 근거: 기존 화면 설계는 여섯 그룹의 기능 목록이고, 계획은 이를 그대로 구현하라고 했다. 조회·진단·로그아웃을 모두 주 화면에 펼칠지, 작은 창에서 주 동작을 유지할지 결정하지 않았다.
- 영향: 버튼이 한꺼번에 노출돼 사용자가 지적한 복잡한 화면으로 되돌아갈 수 있다. 카테고리가 실행 버튼처럼 보이는 문제도 재발할 수 있다.
- 수정: 기존 DESIGN.md의 시각 규칙을 재사용하고 선택 탭/주 동작을 구분한다. 계정·연결 정보·사용량 순서를 고정하고 관리·진단은 기본 접기, 본문 스크롤과 주 동작 고정, 키보드·포커스·접근성 이름을 명시했다. 화면 이동/숨기기 시 키를 다시 마스킹한다.
- 검증 계획: 17개 언어, 기본/최소 크기, 확대, 키보드 이동, `LeavingViewMasksApiKey`. Tasks 4·6.
- 확신: 75. 분류: 누락, 기존 단순한 UI 요구의 구체화. **반영 완료.**

### F7 · P2 · 첫 사용 검사와 fake 테스트 구성이 실제 구현 경로와 어긋남

- 근거: 기존 Task 6은 “엔진 다운로드 버튼”을 눌러 검사한다고 했으나 제품 주 동작은 ChatGPT 로그인이다. Task 3의 fake HTTP 검사에는 구체적인 주입 경계가 없고 AccountLogin이 host와 직접 결합하면 실제 EXE가 필요해질 수 있었다.
- 영향: 제품에 없는 우회 경로로 첫 사용 검사를 통과시키거나, offline 테스트를 위해 엔진 해시 검증을 우회하게 될 위험이 있다.
- 수정: 실제 ChatGPT 로그인 버튼에서 다운로드·대기·취소·재시도를 검사하도록 바꿨다. 내부 HTTP 구성요소에 handler를 주입하고 host 프로세스 검사를 분리했다. 제품 검증 우회 플래그는 금지하고, 실계정 비스트리밍/SSE 종단 검증은 별도 증거가 필요하다고 명시했다.
- 검증 계획: Tasks 2–3의 offline 테스트, Task 6의 공식 엔진 첫 사용 검사. **테스트를 실행했다는 뜻은 아니다.**
- 확신: 75. 분류: 오류·누락, 검증 경로 보완. **반영 완료.**

## 범위와 잔여 검증

- 두 카테고리, 기존 두 API 주소, 여섯 기본 모델, 기존 계정·키·모델 보존, 엔진 비내장 원칙은 승인된 범위를 유지한다. 새 라우터·자동 대체·채팅 UI·저장소 삭제는 추가하지 않았다.
- 수정된 설계서와 계획의 상태·수명·복구·검증 이름을 대조했고 문서 diff의 공백 오류를 검사했다. 이번 변경은 설계서, 계획, 이 리뷰 기록뿐이다.
- 구현 시 실제 엔진 준비, 17개 언어의 화면, 패키지 크기와 성능을 검증해야 한다. 실계정 OAuth 및 upstream 요청 성공은 구현 후 별도 확인 대상이다.

결론: 발견한 설계 문제 7건을 반영했다. 이번 최종 리뷰에서 미해결로 남긴 설계 차단 항목은 없다. 제품 코드·실행 테스트 완료에 대한 판정은 하지 않았다.
