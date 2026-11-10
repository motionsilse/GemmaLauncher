# Cat Loader Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task in the current session. Follow AGENTS.md: sequential work in the main thread; no subagents or parallel implementation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 로그인·모델·API 주소를 보존하는 단일 Cat Loader에서 로컬 모델 실행과 ChatGPT 연결을 제공한다.

**Architecture:** 현재 .NET 10 WinForms 창에 지연 생성되는 두 기능을 둔다. 계정 연결 코드는 기존 Core의 `Accounts/` 모듈로 선별 이관하며, 인증·프록시 수명과 로컬 모델 수명을 분리한다. 하나의 셸이 화면·공통 설정·트레이·종료만 조정하고 로컬 API의 요청 처리 경로는 합치지 않는다.

**Tech Stack:** Windows x64, .NET 10 Desktop/ASP.NET Core shared frameworks, WinForms, 기존 콘솔 테스트 러너, Python 검사 스크립트, CLIProxyAPI v8.0.27.

**Spec:** `docs/superpowers/specs/2026-11-10-cat-loader-integration-design.md` — 2026-11-10 사용자 승인.

**Final review:** 2026-11-10 최종 리뷰 수정 반영. 근거와 수정 목록: `docs/reviews/2026-11-10-cat-loader-final-design-review.md`.

**Execution result:** 2026-11-10 사용자 구현 지시에 따라 로컬 통합 앱·검증·패키지를 완성했다. 이 문서의 원래 작업 체크리스트는 계획으로 보존하며, 실제 수행·시나리오 통합·파일 구성 변경과 증거는 `docs/work/2026-11-10-cat-loader-integration.md`를 따른다.

**Execution:** 후속 구현은 현재 checkout에서 본 에이전트가 순차 진행해 아래 작업과 최종 검증까지 수행한다. 이번 최종 리뷰의 범위는 문서 수정이다. 새 worktree·외부 저장소 변경·실계정 인증 동의는 이 실행 방법에 포함하지 않는다.

## Global Constraints

- 실제 앱 이름은 `Cat Loader`, 배포 파일은 `CatLoader-0.4.0-win-x64.exe`.
- 기존 연결 주소는 `http://127.0.0.1:8080/v1` 및 `http://127.0.0.1:8319/v1`.
- 창·트레이·표시 언어·공통 설정은 하나다. 기능별 서비스와 취소 상태는 독립적이다.
- 기능 전환 자체는 다운로드·로그인·연결·종료를 수행하지 않는다.
- 기존 기본 데이터 폴더와 저장 형식을 보존한다. 이름 변경만으로 모델을 다시 받지 않는다.
- 기존 `CatRouteCodexProxy` 저장 폴더를 사용한다. 실제 사용자 토큰을 복사하거나 변환하는 마이그레이션은 하지 않는다.
- 현재 사용자 범위와 기존 entropy 식별자를 보존한다. 제품명 변경을 암호화 식별자 변경으로 연결하지 않는다.
- 엔진·모델·.NET 런타임 비내장, 시스템 .NET 10 사용, 초기 EXE 5MiB 이하 목표.
- 새 핵심 문구와 조작은 기존 17개 언어에 반영한다.
- `research/`, `.local/`, CatBridge 소스·연구 DLL, 이전 직접 인증 실험 전체와 압축 엔진 payload 프로젝트는 가져오지 않는다.
- 자동 소스 전환·통합 단일 API·자동 fallback·새 채팅/번역 GUI를 추가하지 않는다. 공개 배포·저장소 이름 변경·CatRoute 보관은 별도 이행 단계다.

## Review Focus

1. **레거시 잠금·키:** 이전 CatRoute가 실행 중이거나 DPAPI 복호화가 실패하면 원본을 보존하고 연결을 거부해야 한다. Task 1의 `LegacyLockAndUnreadableKeyArePreserved`로 고정한다.
2. **취소와 늦은 완료:** 로그인·다운로드·조회 취소 직후 도착한 완료가 준비 상태를 되살리지 않아야 한다. Tasks 3–4의 `CancelledLoginCannotBecomeReady`, `StopDuringUsageKeepsStopAvailable`로 고정한다.
3. **동시 동작과 종료:** 한쪽 예외·무응답으로 다른 쪽 서비스 정리가 누락되면 안 된다. Task 5의 `ExitAttemptsBothCleanupsAfterFailure`, `HungCleanupDoesNotBlockOtherFeature`로 고정한다.
4. **초안과 화면 이동:** ‘계속 편집’을 고르면 기능 이동도 취소되며 다른 영역의 연결은 유지돼야 한다. Task 5의 `CategoryChangeRespectsPresetDraft`로 고정한다.
5. **첫 시작과 유휴 비용:** 최근 기능 복원·빈 계정 화면·패키지 검사에서 미사용 기능을 생성하거나 엔진·네트워크를 시작하면 안 된다. Task 5와 7의 `ColdStartAndNavigationDoNotConnect`로 고정한다.

## 파일 구조와 공통 계약

기존 프로젝트·네임스페이스와 리소스 logical name은 유지한다. 새로운 Core 코드는 `GemmaLauncher.Core.Accounts`에 둔다. Core에 제품용 파일만 선별 이관하고 원본 CatRoute checkout은 수정하지 않는다.

| 책임 | 파일 |
|---|---|
| 계정 계약·실패·진행·읽기 전용 요약 | `src/GemmaLauncher.Core/Accounts/ConnectionContracts.cs` |
| 레거시 폴더·DPAPI·ACL·파일 잠금·계정 상태 | `src/GemmaLauncher.Core/Accounts/AccountStateStore.cs` |
| 고정 엔진 준비 | `src/GemmaLauncher.Core/Accounts/AccountRuntime.cs` |
| 소유한 프록시 프로세스와 관리 API | `src/GemmaLauncher.Core/Accounts/AccountHost.cs` |
| 모델 조회·실제 요청 테스트 | `src/GemmaLauncher.Core/Accounts/AccountApi.cs` |
| 로그인 URL·callback·취소 처리 | `src/GemmaLauncher.Core/Accounts/AccountLogin.cs` |
| 사용량 응답 | `src/GemmaLauncher.Core/Accounts/AccountUsageClient.cs` |
| 위 구성의 수명 조정 | `src/GemmaLauncher.Core/Accounts/AccountConnection.cs` |
| 계정 화면 상태·명령 | `src/GemmaLauncher.App/AccountConnectionViewModel.cs` |
| 계정 네이티브 화면 | `src/GemmaLauncher.App/AccountConnectionView.cs` |
| 기능 선택·생성·공통 설정·종료 | `src/GemmaLauncher.App/LoaderShellViewModel.cs` |
| 다른 영역·대화상자의 활성 작업 제어 | `src/GemmaLauncher.App/BackgroundOperationsView.cs` |

Core 계약은 다음 이름과 의미를 사용한다. 결과에는 키·토큰을 담지 않는다. `ConnectionFailure` 예외는 `ConnectionFailure(string code, int? httpStatus = null)`로 만들고 안전한 code와 HTTP status만 노출한다. 서비스 응답 본문이나 인증 URL을 예외 메시지에 붙이지 않는다.

```csharp
public enum SavedAccountState { Missing, Available, Unreadable, Multiple }
public sealed record AccountSummary(SavedAccountState State, string? Email, string? Plan);
public enum AccountPreparationStage
{
    CheckingRuntime, DownloadingRuntime, ExtractingRuntime, StartingProxy,
    AwaitingLogin, CheckingAccount, ReadingModels, Ready, Stopping
}
public sealed record ConnectionProgress(AccountPreparationStage Stage,
    long CompletedBytes = 0, long? TotalBytes = null);
public sealed record UsageWindow(string Bucket, double? UsedPercent,
    long? DurationSeconds, DateTimeOffset? ResetsAt);
public sealed record AccountUsage(DateTimeOffset CheckedAt, IReadOnlyList<UsageWindow> Windows);
public sealed record ConnectionProbeResult(bool TextMatched, string ReportedModel, double ElapsedMs);
public interface IAccountConnection : IAsyncDisposable
{
    string ApiAddress { get; }
    string? LocalApiKey { get; }
    bool IsRunning { get; }
    bool IsReady { get; }
    event EventHandler? StatusChanged;
    AccountSummary ReadSavedAccount();
    Task ConnectAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct);
    Task LoginAsync(Action<Uri> openBrowser, string uiLanguage,
        IProgress<ConnectionProgress>? progress, CancellationToken ct);
    Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct);
    Task<AccountUsage> UsageAsync(CancellationToken ct);
    Task<ConnectionProbeResult> TestAsync(string model, CancellationToken ct);
    Task SignOutAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
```

`AccountConnection(string stateDirectory, int port = 8319)` 구현은 생성만으로 디스크 쓰기·키 생성·엔진 준비를 하지 않는다. `LocalApiKey`는 소유권을 얻어 연결을 준비한 뒤에만 제공한다. `ModelsAsync`·`UsageAsync`·`TestAsync`는 연결된 호스트에만 요청한다. 기본 모델은 기존 `gpt-6-luna`이며 모델 조회에 없으면 실패로 표시하고 대체하지 않는다.

`IsRunning`은 소유한 엔진 프로세스 생존만 의미한다. `IsReady`는 계정 1개·관리 API·기본 모델 확인이 모두 성공한 뒤에만 true이며 중지·준비 실패·프로세스 종료 시 false다. UI의 `IsConnected`는 `IsReady`를 사용한다. `StatusChanged`는 이 값의 변화와 예기치 않은 프로세스 종료를 전달하며 UI가 UI 스레드로 마샬링한다. 부가 조회 실패는 준비 상태를 해제하지 않는다. `ModelsAsync`는 초기 준비에도 사용하므로 IsReady를 선행 조건으로 요구하지 않는다.

연결/로그인/로그아웃은 서비스 내부 수명 gate로 직렬화한다. 중지/폐기는 먼저 활성 작업 CTS를 취소한 뒤 gate와 정리를 기다린다. 같은 gate를 잡은 로그인 완료만 기다리면서 취소를 전달하지 못하는 구조를 금지한다. 공개 `StopAsync`의 취소된 입력 토큰이 소유 자원 정리를 생략하게 해서는 안 된다. 정리는 독립 예산을 쓴다. 모델·사용량·테스트 요청은 별도 CTS와 작업 세대 번호를 사용해 늦은 결과를 거부한다.

테스트는 기존 콘솔 방식을 유지한다. `tests/GemmaLauncher.Tests/AccountTests.cs`와 책임별 partial 파일에서 `RunAsync()`·공통 `Assert(bool, string)`를 제공하고 러너에 `--account-tests` 및 명시적 네트워크 검사 `--account-runtime-smoke`를 추가한다. UI 검사는 기존 `Program` partial과 private `Assert`를 사용한다. xUnit/NuGet 테스트 프레임워크를 새로 도입하지 않는다.

### Task 0: 제품 변경 전 성능 기준 보존

**Files:** Modify `tests/GemmaLauncher.UiTests/Program.cs`, `Benchmark.cs`; create ignored `artifacts/cat-loader/baseline/` reports and copied executable dependencies.

- [ ] **1. 시작 측정 추가:** 테스트 러너에 `--startup-benchmark <reportPath>`를 추가한다. 부모는 단조 시계의 시작 tick을 새 자식 프로세스에 전달하고, 자식은 fake 데이터로 만든 네이티브 창의 첫 Paint 뒤 UI 메시지 처리가 돌아왔을 때 elapsed를 기록하고 종료한다. 새 프로세스 5회의 원시 값·중앙값을 남긴다. OS 파일 캐시를 비우지 않으며 이 결과를 OS 캐시까지 비운 시작 시간으로 표현하지 않는다.
- [ ] **2. 기준 실행물 보존:** 제품 코드는 `da4cd94`인 상태에서 Release 빌드하고, UI 러너와 필요한 실행 의존성을 `artifacts/cat-loader/baseline/native/net10.0-windows/`에 복사한다. 이 깊이는 기존 테스트 Root 계산과 같아야 한다. 복사본의 `--startup-benchmark`와 기존 `--benchmark`를 isolated runner로 실행한다. 후속 제품 변경 뒤에도 이 실행물을 재빌드하거나 덮어쓰지 않는다.
- [ ] **3. 확인·Commit:** JSON의 5회 시작 값과 네 유휴/모의 서버 상태, 런타임·창 크기를 확인한다. 각 실행은 임시 데이터와 비활성 데스크톱만 사용한다. 60초짜리 benchmark는 tool session으로 실행하고 진행 출력을 확인한다. 측정 harness만 `test(perf): capture the pre-integration launcher baseline`로 커밋한다.

### Task 1: 기존 데이터 호환과 읽기 전용 계정 상태

**Files:** Create `Accounts/ConnectionContracts.cs`, `Accounts/AccountStateStore.cs`, `tests/GemmaLauncher.Tests/AccountTests.cs`, `AccountTests.Storage.cs`; modify `GemmaLauncher.Core.csproj`, Core 테스트 `Program.cs`.

**Interfaces:** `AccountStateStore(string directory)` → `ReadSummary(): AccountSummary`, `ReadOrCreateLocalKey(): string`, `AcquireOwnership(): IDisposable`, `ClearLocalLogin(): void`. `ReadSummary()`는 쓰기·키 생성·잠금을 수행하지 않는다. 변경 메서드는 반드시 소유권 잠금 안에서 호출한다. 잠금 lease는 `AccountConnection`이 한 번 획득하고 runtime과 host가 공유하며 중복 획득하지 않는다. 다운로드 전 획득해 마지막 프로세스·파일 정리 뒤 해제한다. 중지된 상태의 로그아웃도 먼저 잠금을 획득한다.

- [ ] **1. 실패 테스트 작성:** `LegacyKeyRoundTripsWithoutRotation`, `ReadSummaryDoesNotCreateFiles`, `LegacyLockAndUnreadableKeyArePreserved`, `CorruptAndMultipleAccountsAreReported`, `OwnershipCoversSignOutDeletion`, `AccountChangedBeforeOwnershipIsRechecked`를 만든다. 새 임시 폴더의 가짜 계정만 사용한다. 로그아웃 삭제 직전에 별도 파일 핸들로 두 번째 잠금을 시도해 실패해야 하며, 종료 완료 후에만 성공해야 한다.

```csharp
var before = File.ReadAllBytes(keyFile);
using var ownership = store.AcquireOwnership();
Assert(store.ReadOrCreateLocalKey() == legacyKey, "Keep the existing API key");
Assert(before.SequenceEqual(File.ReadAllBytes(keyFile)), "Do not rewrite DPAPI data");
Assert(!Directory.Exists(emptyDirectory), "Reading missing state must not create it");
```

- [ ] **2. Red 확인:** `dotnet build GemmaLauncher.sln -c Release -v quiet` 후 Core 러너 `--account-tests`. 아직 없는 계약/메서드 또는 호환성 assertion에서 실패해야 한다.
- [ ] **3. 구현:** 기존 `CatRoute.OpenAiProxy.LocalKey.v1` entropy, CurrentUser DPAPI, `local-key.dpapi`, `session.lock`, `auth/` 경로와 43자 base64url 키를 보존한다. 기존 키가 읽히지 않으면 덮어쓰거나 새 키를 만들지 않는다. ACL은 현재 사용자·SYSTEM·관리자에 제한한다. 계정 파일은 최대 1MiB로 제한해 읽고 손상·복수 계정을 구분한다. 정리 대상은 소유한 auth 폴더의 기존 codex 로그인 파일로 한정하고 reparse point를 통한 외부 경로는 거부한다.
- [ ] **3a. 읽기·삭제 경계:** Missing은 auth 폴더가 없거나 정상 읽기가 끝나 codex 계정이 없는 경우만 사용한다. 접근 거부·JSON 오류·크기 초과는 Unreadable이며 새 로그인으로 덮지 않는다. 요약에는 email/plan만 보존한다. 로그아웃은 정지 확인 후 JSON의 `type=codex`로 확인한 파일만 삭제하고 나머지 파일·키·폴더는 보존한다. OAuth 임시 세션 정리는 해당 state의 관리 API에 한정한다. 잠금 획득 뒤 재검사해 표시 이후 계정이 추가됐다면 새 로그인을 시작하지 않는다.
- [ ] **4. 의존성:** Core에 `Microsoft.WindowsDesktop.App.WindowsForms` FrameworkReference를 추가해 시스템 `ProtectedData`를 사용한다. 현재 .NET 10.0.12 reference pack에 존재함을 확인했다. JWT 라이브러리·CatRoute 원본 Core 프로젝트 참조를 추가하지 않는다.
- [ ] **5. Green 확인:** Core `--account-tests`; 잠금 실패·손상 키 전후 해시가 같고 원본 파일이 남아야 한다. ACL·경로 가드도 임시 폴더에서 검증한다.
- [ ] **6. Commit:** `feat(accounts): preserve CatRoute account state and local keys`.

### Task 2: 선택적 엔진 준비와 소유한 프록시 호스트

**Files:** Create `Accounts/AccountRuntime.cs`, `Accounts/AccountHost.cs`, `Accounts/AccountApi.cs`, `AccountTests.Runtime.cs`, `AccountTests.Host.cs`; modify `InstallationService.cs`, `AccountTests.cs`.

**Interfaces:** `AccountRuntime(AccountStateStore store, IInstallationService installer)` → `EnsureAsync(IProgress<ConnectionProgress>?, CancellationToken): Task<string>`. `AccountHost(AccountStateStore store, string verifiedExecutable, int port)`는 `StartAsync(CancellationToken): Task`, `StopAsync(CancellationToken): Task`, `IsRunning: bool`, 내부 로컬/관리용 HttpClient를 제공하고 `IAsyncDisposable`을 구현한다. host는 서비스가 보유한 lease 안에서만 생성·호출하며 lease를 획득하거나 해제하지 않는다. 관리 키는 외부로 노출하지 않는다.

- [ ] **1. 실패 테스트 작성:** `VerifiedLegacyEngineSkipsNetwork`, `CorruptArchiveAndExeAreRejected`, `CancelledDownloadKeepsAccountFiles`, `StandaloneProxyDoesNotRequireLlamaDlls`, `ApiResponsesAreBounded`.
- [ ] **2. Red 확인:** Core `--account-tests`에서 새 런타임·호스트 계약과 standalone 실행 파일 검증에 대한 실패를 확인한다.
- [ ] **3. 다운로드 구현:** `InstallationService`의 다운로드·ZIP 경로 검증·해시 receipt를 재사용한다. standalone 실행 파일 검증의 기존 `uv.exe` 예외에 `cli-proxy-api.exe`를 추가하되 llama DLL 검증은 유지한다. 기존 `runtime/8.0.27/cli-proxy-api.exe`가 맞으면 그대로 재사용하고, 새 설치는 계정 상태 폴더 아래의 관리된 runtime 경로를 쓴다.
- [ ] **3a. 설치 인스턴스 경계:** 계정용 installer는 연결 명령 후 `new InstallationService(new LauncherPaths(stateDirectory), discoverExistingModels: false)`로 별도 생성한다. 로컬 installer·gate·모델 검색 목록을 공유하지 않는다. 생성 자체가 WinGet 모델 경로를 탐색하는 현재 기본값을 사용하지 않는다. 기존 엔진과 새 설치 모두 최종 EXE의 고정 해시를 실제로 다시 확인하고, 매 실행 직전에도 검증한다. 테스트 `AccountRuntimeDoesNotScanLocalModels`와 `AccountInstallDoesNotBlockLocalInstall`을 추가한다.

| 고정 값 | 내용 |
|---|---|
| ZIP URL | `https://github.com/router-for-me/CLIProxyAPI/releases/download/v8.0.27/CLIProxyAPI_8.0.27_windows_amd64.zip` |
| ZIP bytes | `23629480` |
| ZIP SHA-256 | `ac6867818696aefe299c8bac1730f831cbb127f0854acca1b1aebb0110a5dc53` |
| EXE bytes | `72386560` |
| EXE SHA-256 | `a3620a7e8d54d0f4257b493ea5fc01b3c59d581fb8c3596df5880976596da162` |

ZIP size/digest는 2026-11-10 [공식 릴리즈 metadata](https://api.github.com/repos/router-for-me/CLIProxyAPI/releases/tags/v8.0.27)와 CatRoute 고지의 해시가 일치함을 확인했다.

- [ ] **4. 호스트·API 이관:** CatRoute `c5c74ad`의 제품용 `CliProxyHost`·`ProxyApi` 동작을 이관한다. 서비스가 잠금을 보유한 동안에만 키/설정 파일을 만들고 127.0.0.1에 바인딩한다. 기존 `WindowsJob`을 사용하고 출력은 로그로 저장하지 않고 bounded drain으로 버린다. `StartAsync`의 15초 준비 예산, `/v0/management/auth-files` 확인, 로컬 API/관리 키 분리를 유지한다. host Stop/Dispose는 자신의 프로세스·설정만 정리하고 lease는 서비스가 마지막에 해제한다. 요청·본문 읽기는 같은 예산과 1MiB 한도를 적용한다. 원본의 UseProxy=false, AllowAutoRedirect=false, 비공개 관리 API, 자동 프로젝트/모델 전환 금지, 로그·통계·관리 UI 비활성 설정과 외부 저장소 설정 환경변수 제거도 유지한다.
- [ ] **4a. 정리 시간과 소유권:** 계정 host는 job 닫기 후 최대 2초, 소유 프로세스 kill 이후 최대 5초, pipe drain 최대 2초로 제한한다. 정지 확인 전 auth 삭제를 금지하며 종료 확인 실패는 `stop_failed`로 남기고 재연결을 막는다. 설정 삭제 실패여도 job/HttpClient/pipe 정리를 모두 시도한다. 정상 정지 확인 후에만 서비스가 lease를 해제한다. fake child의 무응답·pipe 종료 지연으로 `HostCleanupIsBounded`를 검증한다.
- [ ] **5. Green 확인:** Core `--account-tests`. 별도 `--account-runtime-smoke`는 빈 임시 계정 폴더로 공식 엔진을 준비하고 키 없는 API 401, 관리 API 분리, 정상 종료 후 listener 해제를 확인한다. 로그인·upstream 추론은 수행하지 않는다.
- [ ] **6. Commit:** `feat(accounts): prepare and own the optional ChatGPT proxy`.

### Task 3: 로그인·사용량·연결 수명 서비스

**Files:** Create `Accounts/AccountLogin.cs`, `Accounts/AccountUsageClient.cs`, `Accounts/AccountConnection.cs`, `AccountTests.Login.cs`, `AccountTests.Usage.cs`; modify 계약과 Core 테스트 러너.

**Interfaces:** Task 1의 `IAccountConnection`을 `AccountConnection`이 구현한다. 내부 `AccountLogin(HttpClient management, Func<AccountSummary> readAccount)`의 `LoginAsync(Action<Uri> openBrowser, string uiLanguage, CancellationToken ct): Task`는 URL 검증·callback·관리 API 전달을 담당한다. 프로세스 시작·중지는 호출자인 서비스가 소유한다. `AccountUsageClient(HttpClient management)`의 `ReadAsync(CancellationToken ct): Task<AccountUsage>`는 기존 `UsageApi`의 결과 구조와 누락 값 보존을 담당한다. 내부 HTTP 구성요소는 테스트에서 fake handler를 주입하고, 프로세스 검사는 작은 fixture child로 분리한다. 제품 EXE 해시 검사를 끄는 우회 플래그는 만들지 않는다.

- [ ] **1. 실패 테스트 작성:** `AuthorizationRejectsUnexpectedHostRedirectAndState`, `CancelledLoginCannotBecomeReady`, `UsageFailureDoesNotChangeConnection`, `OnlyExplicitTestSendsCompletion`, `ConfirmedExitReleasesOwnershipDespiteConfigDeletionFailure`, `SavedLoginSurvivesModelCheckFailure`, `UnexpectedExitClearsReadiness`, `StopCancelsBeforeWaitingForLifecycleGate`.

```csharp
Assert(requests.All(r => r.Path != "/v1/chat/completions"), "No implicit inference");
Assert(!connection.IsRunning, "Cancellation must leave no owned proxy");
Assert(!failureText.Contains(secretMarker), "Failures must not reveal credentials");
```

- [ ] **2. Red 확인:** Core `--account-tests`에서 callback·실패 수명 assertion이 아직 충족되지 않음을 확인한다.
- [ ] **3. 로그인 구현:** `auth.openai.com:443/oauth/authorize`, 단일 state, loopback `:1455/auth/callback`, 5분 로그인 예산을 보존한다. 잘못된 state/중복 파라미터/거절/기한 만료를 거부하고, 취소 때 관리 API의 해당 OAuth 세션과 자신의 listener를 정리한다. callback 수신만으로 준비됐다고 표시하지 않고 저장 계정 1개와 기본 모델을 확인한다. 브라우저 완료 페이지 이름은 Cat Loader로 바꾼다.
- [ ] **4. 사용량·서비스 구현:** 기존 `UsageApi`의 workspace 선택과 서비스 응답 해석을 유지한다. 누락한 비율·기간·시각은 null로 유지한다. `TestAsync`만 `gpt-6-luna`·`low`·`Reply with exactly OK.`의 실제 요청을 10초 예산으로 전송한다. 기본 모델 미제공은 명시적 실패다. 로그아웃은 확인된 로컬 codex 로그인만 제거하며 원격 revoke나 키 재발급을 수행하지 않는다.
- [ ] **4a. 실패 후 상태:** 로그인 저장 뒤 모델 확인 실패·취소는 저장 계정을 지우지 않고 host만 정리한다. 로그아웃은 보조 요청 취소 → host 정지 확인 → codex 파일 삭제 → summary 재검사 → lease 해제 순서다. 파일 삭제 실패는 로그아웃 성공으로 표시하지 않는다. 수명/조회 예외는 code·HTTP status로 변환하고 App의 기존 exception 로그 경로로 인증 URL·본문·inner exception이 전달되지 않게 한다.
- [ ] **5. Green 확인:** Core `--account-tests`에서 fake HttpMessageHandler/loopback 서버만으로 검증한다. 취소 후 늦은 callback, 중복 callback, 부적절한 Host, usage 401·403·기간 누락을 포함한다. 테스트 기본 실행은 실사용자 계정이나 외부 추론을 사용하지 않는다.
- [ ] **6. Commit:** `feat(accounts): preserve login usage and connection contracts`.

### Task 4: ChatGPT 화면과 독립 상태 제어

**Files:** Create `AccountConnectionViewModel.cs`, `AccountConnectionView.cs`, `tests/GemmaLauncher.UiTests/AccountConnectionTests.cs`; modify `UiBindings.cs`, UI 테스트 `Program.cs`, `Locales/*.ui.json`.

**Interfaces:** `AccountConnectionViewModel(IAccountConnection connection)`은 `ObservableObject`, `IAsyncDisposable`을 구현한다. `ActivateAsync(CancellationToken)`는 저장 상태만 읽으며 활성 수명 작업 중에는 저장 요약으로 진행 상태를 덮지 않는다. `IsBusy`는 준비·정리만, `IsQueryBusy`는 사용량·새로고침·테스트를 나타낸다. `IsConnected`, `Account`, `ApiAddress`, `ApiModelId`, 마스킹한 `ApiKeyText`, `StatusTitle`, `StatusDescription`, `UsageText`를 제공한다. `PrimaryCommand`, `UsageCommand`, `RefreshCommand`, `TestCommand`, `SignOutCommand`, `CancelQueryCommand`, 복사/키 표시/로그인 페이지 다시 열기 명령을 제공한다. `OpenBrowser: Action<Uri>`와 `ConfirmSignOut: Func<bool>`은 UI가 설정한다. `CancelPending(): void`는 기다리지 않고 두 CTS를 취소한다. UI 주 스레드에서 상태·명령을 갱신하고 진행 중 작업을 추적·취소·await한다. 조회와 수명 작업의 세대 번호를 나누며, dispose 뒤에는 알림을 적용하지 않는다.

- [ ] **1. 실패 테스트 작성:** `ActivationReadsOnlySavedState`, `PrimaryMatchesAccountAndConnectionState`, `CancelledLoginCannotBecomeReady`, `QuotaFailureKeepsCopyAndStopAvailable`, `SignOutRequiresExplicitConfirmation`, `StopDuringUsageKeepsStopAvailable`, `ReturningDuringLoginPreservesProgress`, `UnreadableStateDoesNotOfferLogin`, `LeavingViewMasksApiKey`.
- [ ] **2. Red 확인:** 솔루션 빌드 후 isolated UI 러너에서 새 테스트가 상태/명령 assertion으로 실패하는지 확인한다.
- [ ] **3. 바인딩 정리:** `UiBindings`의 알림 원본을 `INotifyPropertyChanged`로 일반화한다. 로컬 전용 `Models`에는 `LauncherViewModel`을 명시적으로 전달하고 `MemoryText`에는 getter를 필수로 받는다. 기존 메모리 갱신의 단일 소비자 경로를 유지한다.
- [ ] **4. 화면·문구 구현:** 설계서의 상태별 동작 표와 화면 배치를 사용한다. 준비되지 않은 연결 정보 복사는 비활성화하고 키는 기본 마스킹한다. 사용량 조회·모델 새로고침·실제 요청 테스트를 분리하고 진행 중에도 연결 중지를 유지한다. Unreadable/Multiple/키 복호화 실패는 원인, `저장 폴더 열기`, 쓰기 없는 `상태 다시 확인`을 제공한다. 첫 사용 주 버튼은 `ChatGPT 로그인`이며 필요할 때 엔진도 내려받는다고 설명한다. 별도 모델 추가/엔진 설치 페이지를 만들지 않는다. 새 문구·오류 매핑·callback 완료 안내를 17개 언어에 넣으며 API 키나 OAuth URL 전체를 알림·로그에 남기지 않는다. 계정 연결 오류가 로컬 영역의 오류 표시를 덮지 않는다.
- [ ] **5. Green 확인:** 전체 isolated UI 러너와 `python scripts/check-locales.py`. 마스킹, 로그인 필요/저장됨/준비 중/연결 가능/사용량 오류, 버튼 대상과 취소 후 오래된 완료 거부를 확인한다.
- [ ] **6. Commit:** `feat(ui): add the ChatGPT connection workspace`.

### Task 5: 지연 생성 셸·두 기능 수명·호환 설정

**Files:** Create `LoaderShellViewModel.cs`, `BackgroundOperationsView.cs`, `tests/GemmaLauncher.UiTests/LoaderShellTests.cs`; modify `App.cs`, `MainWindow.cs`, `AppSettingsView.cs`, `LauncherDialogs.cs`, `LauncherWorkspace.cs`, `LauncherViewModel.cs`, `RunOperationPanel.cs`, `SettingsStore.cs`, `UiBindings.cs`, UI 테스트 러너.

**Interfaces:** `LoaderFeature { LocalModels, ChatGpt }`. `LoaderShellViewModel(LauncherSettings settings, SettingsStore store, Func<LauncherViewModel> localFactory, Func<AccountConnectionViewModel> accountFactory)`는 `ObservableObject`, `IAsyncDisposable`을 구현한다. `SelectedFeature: LoaderFeature`, nullable `Local: LauncherViewModel?`/`Account: AccountConnectionViewModel?`, 공통 `LanguageSelection: string`·`CloseToTray: bool`을 제공한다. `SelectFeatureAsync(LoaderFeature feature, CancellationToken ct): Task<bool>`, `ResolvePendingEdits(): bool`을 제공한다. factory 호출 전에는 그 기능이 존재하지 않으며 조회용 속성은 factory를 호출하지 않는다. `MainWindow(LoaderShellViewModel shell, uint activateMessage = 0, uint legacyActivateMessage = 0)`를 추가하고 기존 `MainWindow(LauncherViewModel, uint, uint)` 호출은 테스트용 기존 로컬 인스턴스 어댑터로 유지한다. 어댑터는 창 종료 때 외부 소유 로컬 VM을 폐기하지 않는다.

- [ ] **1. 실패 테스트 작성:** `ColdStartAndNavigationDoNotConnect`, `RestoreChatDoesNotCreateLocalModel`, `CategoryChangeRespectsPresetDraft`, `StoppingOneKeepsOtherRunning`, `ExitAttemptsBothCleanupsAfterFailure`, `HungCleanupDoesNotBlockOtherFeature`, `StartupFailureDisposesCreatedFeature`, `LegacyPreferenceAndKeyAreNotOverwritten`, `SharedSettingsPreserveModelChangesAfterNavigation`.

```csharp
Assert(localFactoryCalls == 0, "Restoring ChatGPT must not scan model files");
Assert(fakeAccount.StartCalls == 0 && browserOpens == 0, "Navigation is not login");
Assert(shell.SelectedFeature == LoaderFeature.LocalModels && local.HasPresetChanges,
    "Keep editing cancels category change");
Assert(localDisposed && accountDisposed, "Attempt both cleanups even after one failure");
```

- [ ] **2. Red 확인:** isolated UI 러너에서 미사용 기능 생성, 초안 손실 또는 두 서비스 정리 누락에 대한 실패를 확인한다.
- [ ] **3. 셸 연결:** App은 설정과 factory만 준비하고 처음 선택된 기능만 생성한다. `MainWindow`의 세 로컬 보조 페이지도 기존처럼 지연 생성한다. ChatGPT 선택은 `ActivateAsync`만 호출한다. 카테고리 전환 전 활성 로컬 초안의 save/discard/keep-editing을 처리하고 성공한 선택만 저장한다. 모델 ID나 주소를 바꾸지 않는다. 로컬 계측은 해당 메인 화면이 실제 보일 때만 수행한다.
- [ ] **4. 공통 설정·이관:** App이 읽은 하나의 `LauncherSettings` 인스턴스를 셸과 로컬 factory가 공유한다. 별도 복사본으로 전체 설정을 저장해 모델 변경을 덮어쓰지 않는다. `LauncherSettings.ActiveFeature`는 `local`/`chatgpt`만 저장하고 잘못된 값은 `local`로 읽는다. 기본 GemmaLauncher 경로·사용자별 mutex/활성화 메시지를 보존한다. 런처 설정이 없을 때만 CatRoute `ui-language.json`을 최대 1024바이트로 읽어 auto/ko/en을 가져온다. `--account-data-dir`를 추가해 계정 저장 경로를 독립적으로 지정하게 하고, 테스트는 모델·계정 경로를 모두 임시 경로로 지정한다.
- [ ] **5. 작업 제어·종료:** `BackgroundOperationsView`는 생성된 기능만 관찰해 활성 작업의 이름·상태·중지/취소를 표시한다. 주 화면에서는 현재 기능의 주 동작을 중복하지 않고 다른 기능만 표시한다. 공통 대화상자는 두 기능의 활성 작업을 모두 보여준다. 트레이 항목을 기능별로 만들고 창 닫기 설정이 두 기능에 적용됨을 안내한다. 두 VM에 동기 `CancelPending(): void`를 두고 완전 종료에서 모두 먼저 호출한다. 이어 두 DisposeAsync를 모두 시작하고, 각 실패/30초 무응답을 기능별로 수집한다. 순차 await 때문에 두 번째 cleanup이 시작되지 않는 구조를 금지한다. 이 런타임 동시 정리는 에이전트 순차 작업 규칙과 별개다. App 초기화 실패도 생성된 VM과 구독을 정리한다. 미접속 계정에는 폴링 타이머가 없어야 하며 실행 중 감시는 기존 2초 프로세스 상태 확인 수준으로 제한한다.
- [ ] **5a. 시간 초과 처리:** 서비스 자체의 소유 프로세스 정리는 Task 2의 제한을 따르고 로컬의 기존 job/kill/pipe 제한을 유지한다. 셸의 30초 제한은 마지막 UI 대기 한도이며 timeout만으로 정리가 성공했다고 간주하지 않는다. 취소·dispose를 시작한 task의 예외를 관찰하고 실패 기능을 보고한다. 정리 실패나 무응답 뒤 같은 셸에서 새 연결을 시작하지 않는다. 테스트는 짧은 내부 예산을 주입하며 실제 PID/listener 종료 증거와 단순 UI 대기 종료를 구분한다.
- [ ] **5b. 실패 주입 검증:** DisposeAsync 호출 자체의 동기 예외도 수집해 다른 기능의 정리를 시작한다. 초기 창 생성 실패, 계정 오류의 내부 예외에 가짜 secret marker가 있는 경우를 주입해 자원 정리와 비밀 없는 로그를 확인한다. `StartupFailureDisposesCreatedFeature`, `AccountFailureDoesNotLeakThroughAppLog`를 실행한다.
- [ ] **6. Green 확인:** 전체 Core·isolated UI 검사. 동시 실행, 양방향 화면 이동, 숨김/복원, 한쪽 fault, 로그인 대기 중 종료, 기존 잠금 충돌을 가짜 서비스로 검증한다. 실제 HWND 캡처에서 공통 설정·초안/로그아웃 확인 중에도 각 중지/취소를 확인한다.
- [ ] **7. Commit:** `feat(shell): unify local models and account connections`.

### Task 6: 네이티브 QA·성능 기준 비교

**Files:** Modify `tests/GemmaLauncher.UiTests/Program.cs`, `Benchmark.cs`; create `LoaderRenderTests.cs`, `AccountFirstUseSmoke.cs`, `docs/work/2026-11-10-cat-loader-integration.md`.

**Interfaces:** `--render-loader`는 fake 서비스와 임시 저장 폴더만 사용한다. `--account-first-use-smoke`는 빈 계정 폴더와 실제 공식 엔진을 사용하되 OpenBrowser를 캡처 콜백으로 교체해 로그인 대기까지 확인한 뒤 취소한다. 인증 URL 전체·state·실계정은 출력하지 않는다. `--benchmark <reportPath>`는 기존 공통 네 상태 이름과 1080×860, warmup 5초, 상태당 10초 측정을 유지하고 계정 영역 상태를 추가한다.

- [ ] **1. 비교 조건 확인:** Task 0에서 보존한 실행물·기준 JSON의 해시와 측정 조건을 확인한다. 새 셸의 계정 화면에도 같은 시작 측정 경계를 적용한다. baseline 실행물은 원래 로컬 창을, 새 실행물은 기본 로컬 창과 복원된 계정 창을 각각 측정한다.
- [ ] **2. 화면 검사:** `--render-loader`로 두 기본 화면·빈/저장 계정·로그인 대기·연결 가능·권한/포트/사용량 오류·동시 동작·미저장 초안·작은 창을 캡처한다. 17개 언어와 수동 150% 확대에서 잘림·버튼 대상·키 마스킹을 확인한다. 기본/최소 크기에서 고정 주 동작·본문 스크롤, Tab 순서·선택 탭·키보드 조작을 검증한다. 실제 모니터 DPI 검사와 수동 확대를 구분한다.
- [ ] **3. 첫 사용 검사:** 빈 폴더에서 실제 `ChatGPT 로그인` 버튼부터 공식 엔진 다운로드·로그인 대기·취소·재시도까지 확인한다. 종료 후 해당 임시 폴더가 소유한 listener·자식 프로세스가 없어야 한다. 계정 없이 수행한 이 결과를 실계정 로그인·추론 성공으로 기록하지 않는다. 기존 비스트리밍/SSE 요청 중계는 고정 원본 엔진이 처리하며, 실계정 종단 검증 전에는 이 호환성까지 통과했다고 보고하지 않는다.
- [ ] **4. 성능 비교:** 같은 시스템 런타임·창 크기·fake 데이터·공통 상태로 baseline과 새 JSON을 비교해 새 프로세스 시작 시간, private RAM, working set, CPU, I/O, GDI/USER 개수, WPF 로딩 여부를 기록한다. 로컬/계정 엔진과 외부 추론 시간을 셸 수치에 섞지 않는다. 설명되지 않은 유휴 작업이나 미사용 영역 생성은 수정한다.
- [ ] **5. Commit:** `test(loader): verify coexistence first use and idle behavior`.

### Task 7: Cat Loader 브랜딩·고지·최종 단일 EXE

**Files:** Modify `GemmaLauncher.App.csproj`, `scripts/publish-launcher.ps1`, `BundledResources.cs`, `README.md`, `THIRD-PARTY-NOTICES.md`, `Locales/*.ui.json`, `DESIGN.md`; create `docs/migrations/cat-loader-0.4.md`, `docs/reviews/2026-11-10-cat-loader-implementation-review.md`.

- [ ] **1. 브랜드와 고지:** 제품 이름·title·tray·사용자 문구를 Cat Loader로 바꾼다. assembly와 배포 이름은 `CatLoader`/`0.4.0`으로 맞추고 기존 namespace·리소스 logical name·기본 데이터 폴더는 보존한다. CLIProxyAPI 원본 MIT 고지를 내장 notices에 포함하고 CatRoute 제품 코드의 출처 commit·이관 목록을 기록한다. 연구 자료나 payload를 넣지 않는다.
- [ ] **2. 배포 스크립트:** 출력 `CatLoader-0.4.0-win-x64.exe`·checksum과 내부 `CatLoader.exe`를 검증한다. 이전 0.2/0.3 산출물을 덮어쓰지 않는다. 새 staging에서 정확한 EXE의 `--verify-package`를 실행해 6개 모델·17개 언어·고지·HTTP runtime과 계정 모듈의 비활성 초기 상태를 확인한다. utility mode는 계정 저장 폴더·키·엔진을 만들지 않는다.
- [ ] **3. 최종 검사:** 아래 명령을 실행하고 exit code와 보고서를 확인한다. 실패 시 해당 책임의 regression을 보완한 뒤 재검증한다.

```powershell
dotnet build GemmaLauncher.sln -c Release -v quiet
& ./tests/GemmaLauncher.Tests/bin/Release/net10.0-windows/GemmaLauncher.Tests.exe
& ./scripts/run-isolated-ui-tests.ps1 -Executable tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe -LogPath artifacts/cat-loader/final-ui.log
python scripts/check-locales.py
python scripts/create-product-catalog.py --check
python -m unittest discover -s tests/scripts
& ./scripts/publish-launcher.ps1 -Version 0.4.0
```

- [ ] **4. 순차 최종 리뷰:** 작성자가 정확성·API 계약·인증/키 노출·수명·복구 문구·범위/의존성을 각각 검토한다. 승인된 AGENTS.md에 따라 리뷰 subagent도 만들지 않는다. 5MiB 목표, 테스트·실측·실계정 검증의 한계, 미해결 항목을 최종 보고에 적는다. 필요한 사용자 브라우저 인증을 완료하지 못했다면 실제 로그인·upstream 추론은 미검증으로 분리한다.
- [ ] **5. 문서·Commit:** 기존 모델 재다운로드가 필요 없고 CatRoute 계정 폴더를 그대로 사용함을 이전 안내에 적는다. 기존 CatRoute가 켜져 있으면 종료 후 연결하도록 안내한다. 로컬 통합 EXE·SHA-256·검증 근거를 남기고 `feat(loader): deliver the Cat Loader integration candidate`로 커밋한다. 공개 배포·GitHub 이름 변경·CatRoute 보관은 수행하지 않는다.

## 실행 전 자체 검토

- 설계서의 두 카테고리·독립 수명·명시적 연결은 Tasks 4–5, 저장·인증·기존 키 호환은 Tasks 1–3, 작은 패키지·성능·이행 안내는 Tasks 6–7에서 검사한다.
- 모든 소비자는 `IAccountConnection`과 명시된 셸 계약을 사용한다. UI가 인증 파일을 직접 삭제하거나 엔진을 직접 실행하지 않는다.
- Review Focus의 다섯 조건은 각각 담당 Task의 이름 있는 테스트로 연결했다. baseline은 Task 0으로 분리해 제품 구현 전에 보존한다. 사용량 결과와 조회 클래스 이름 충돌, 셸/로컬의 설정 복사본 간 덮어쓰기 가능성도 계약에서 제거했다.
- 새 타입과 테스트는 만들 예정인 계약이다. 이 문서 작성으로 제품 코드나 실제 사용자 계정을 변경하지 않았다.

수정된 설계의 로컬 앱 적용·검증과 CatLoader-0.4.0-win-x64.exe 생성을 완료했다. 실행 결과와 실계정 OAuth/추론 미검증 범위는 위 작업 기록을 따른다.
