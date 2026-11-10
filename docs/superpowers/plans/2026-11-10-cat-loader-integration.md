# Cat Loader Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task in the current session. Follow AGENTS.md: sequential work in the main thread; no subagents or parallel implementation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 로그인·모델·API 주소를 보존하는 단일 Cat Loader에서 로컬 모델 실행과 ChatGPT 연결을 제공한다.

**Architecture:** 현재 .NET 10 WinForms 창에 지연 생성되는 두 기능을 둔다. 계정 연결 코드는 기존 Core의 `Accounts/` 모듈로 선별 이관하며, 인증·프록시 수명과 로컬 모델 수명을 분리한다. 하나의 셸이 화면·공통 설정·트레이·종료만 조정하고 로컬 API의 요청 처리 경로는 합치지 않는다.

**Tech Stack:** Windows x64, .NET 10 Desktop/ASP.NET Core shared frameworks, WinForms, 기존 콘솔 테스트 러너, Python 검사 스크립트, CLIProxyAPI v8.0.27.

**Spec:** `docs/superpowers/specs/2026-11-10-cat-loader-integration-design.md` — 2026-11-10 사용자 승인.

**Execution:** 현재 checkout에서 본 에이전트가 순차 구현한다. 구현 계획 검토가 끝나면 아래 작업과 최종 검증까지 수행한다. 새 worktree·외부 저장소 변경·실계정 인증 동의는 이 실행 방법에 포함하지 않는다.

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
2. **취소와 늦은 완료:** 로그인·다운로드 취소 직후 도착한 완료가 준비 상태를 되살리지 않아야 한다. Task 3과 4의 `CancelledLoginCannotBecomeReady`로 고정한다.
3. **동시 동작과 종료:** 한쪽 실패 또는 종료 예외로 다른 쪽 서비스 정리가 누락되면 안 된다. Task 5의 `ExitAttemptsBothCleanupsAfterFailure`로 고정한다.
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

테스트는 기존 콘솔 방식을 유지한다. `tests/GemmaLauncher.Tests/AccountTests.cs`와 책임별 partial 파일에서 `RunAsync()`·공통 `Assert(bool, string)`를 제공하고 러너에 `--account-tests` 및 명시적 네트워크 검사 `--account-runtime-smoke`를 추가한다. UI 검사는 기존 `Program` partial과 private `Assert`를 사용한다. xUnit/NuGet 테스트 프레임워크를 새로 도입하지 않는다.

### Task 0: 제품 변경 전 성능 기준 보존

**Files:** Modify `tests/GemmaLauncher.UiTests/Program.cs`, `Benchmark.cs`; create ignored `artifacts/cat-loader/baseline/` reports and copied executable dependencies.

- [ ] **1. 시작 측정 추가:** 테스트 러너에 `--startup-benchmark <reportPath>`를 추가한다. 부모는 단조 시계의 시작 tick을 새 자식 프로세스에 전달하고, 자식은 fake 데이터로 만든 네이티브 창의 첫 Paint 뒤 UI 메시지 처리가 돌아왔을 때 elapsed를 기록하고 종료한다. 새 프로세스 5회의 원시 값·중앙값을 남긴다. OS 파일 캐시를 비우지 않으며 이 결과를 OS 캐시까지 비운 시작 시간으로 표현하지 않는다.
- [ ] **2. 기준 실행물 보존:** 제품 코드는 `da4cd94`인 상태에서 Release 빌드하고, UI 러너와 필요한 실행 의존성을 `artifacts/cat-loader/baseline/native/net10.0-windows/`에 복사한다. 이 깊이는 기존 테스트 Root 계산과 같아야 한다. 복사본의 `--startup-benchmark`와 기존 `--benchmark`를 isolated runner로 실행한다. 후속 제품 변경 뒤에도 이 실행물을 재빌드하거나 덮어쓰지 않는다.
- [ ] **3. 확인·Commit:** JSON의 5회 시작 값과 네 유휴/모의 서버 상태, 런타임·창 크기를 확인한다. 각 실행은 임시 데이터와 비활성 데스크톱만 사용한다. 60초짜리 benchmark는 tool session으로 실행하고 진행 출력을 확인한다. 측정 harness만 `test(perf): capture the pre-integration launcher baseline`로 커밋한다.

### Task 1: 기존 데이터 호환과 읽기 전용 계정 상태

**Files:** Create `Accounts/ConnectionContracts.cs`, `Accounts/AccountStateStore.cs`, `tests/GemmaLauncher.Tests/AccountTests.cs`, `AccountTests.Storage.cs`; modify `GemmaLauncher.Core.csproj`, Core 테스트 `Program.cs`.

**Interfaces:** `AccountStateStore(string directory)` → `ReadSummary()`, `ReadOrCreateLocalKey()`, `AcquireOwnership(): IDisposable`, `ClearLocalLogin()`. `ReadSummary()`는 쓰기·키 생성·잠금을 수행하지 않는다. 변경 메서드는 반드시 소유권 잠금 안에서 호출한다.

- [ ] **1. 실패 테스트 작성:** `LegacyKeyRoundTripsWithoutRotation`, `ReadSummaryDoesNotCreateFiles`, `LegacyLockAndUnreadableKeyArePreserved`, `CorruptAndMultipleAccountsAreReported`를 만든다. 새 임시 폴더의 가짜 계정만 사용한다.

```csharp
var before = File.ReadAllBytes(keyFile);
Assert(store.ReadOrCreateLocalKey() == legacyKey, "Keep the existing API key");
Assert(before.SequenceEqual(File.ReadAllBytes(keyFile)), "Do not rewrite DPAPI data");
Assert(!Directory.Exists(emptyDirectory), "Reading missing state must not create it");
```

- [ ] **2. Red 확인:** `dotnet build GemmaLauncher.sln -c Release -v quiet` 후 Core 러너 `--account-tests`. 아직 없는 계약/메서드 또는 호환성 assertion에서 실패해야 한다.
- [ ] **3. 구현:** 기존 `CatRoute.OpenAiProxy.LocalKey.v1` entropy, CurrentUser DPAPI, `local-key.dpapi`, `session.lock`, `auth/` 경로와 43자 base64url 키를 보존한다. 기존 키가 읽히지 않으면 덮어쓰거나 새 키를 만들지 않는다. ACL은 현재 사용자·SYSTEM·관리자에 제한한다. 계정 파일은 최대 1MiB로 제한해 읽고 손상·복수 계정을 구분한다. 정리 대상은 소유한 auth 폴더의 기존 codex 로그인 파일로 한정하고 reparse point를 통한 외부 경로는 거부한다.
- [ ] **4. 의존성:** Core에 `Microsoft.WindowsDesktop.App.WindowsForms` FrameworkReference를 추가해 시스템 `ProtectedData`를 사용한다. 현재 .NET 10.0.12 reference pack에 존재함을 확인했다. JWT 라이브러리·CatRoute 원본 Core 프로젝트 참조를 추가하지 않는다.
- [ ] **5. Green 확인:** Core `--account-tests`; 잠금 실패·손상 키 전후 해시가 같고 원본 파일이 남아야 한다. ACL·경로 가드도 임시 폴더에서 검증한다.
- [ ] **6. Commit:** `feat(accounts): preserve CatRoute account state and local keys`.

### Task 2: 선택적 엔진 준비와 소유한 프록시 호스트

**Files:** Create `Accounts/AccountRuntime.cs`, `Accounts/AccountHost.cs`, `Accounts/AccountApi.cs`, `AccountTests.Runtime.cs`, `AccountTests.Host.cs`; modify `InstallationService.cs`, `AccountTests.cs`.

**Interfaces:** `AccountRuntime(AccountStateStore store, IInstallationService installer)` → `EnsureAsync(IProgress<ConnectionProgress>?, CancellationToken): Task<string>`. `AccountHost(AccountStateStore store, string verifiedExecutable, int port)`는 `StartAsync`, `StopAsync`, `IsRunning`, 로컬/관리용 HttpClient를 제공하고 `IAsyncDisposable`을 구현한다. 관리 키는 외부로 노출하지 않는다.

- [ ] **1. 실패 테스트 작성:** `VerifiedLegacyEngineSkipsNetwork`, `CorruptArchiveAndExeAreRejected`, `CancelledDownloadKeepsAccountFiles`, `StandaloneProxyDoesNotRequireLlamaDlls`, `ApiResponsesAreBounded`.
- [ ] **2. Red 확인:** Core `--account-tests`에서 새 런타임·호스트 계약과 standalone 실행 파일 검증에 대한 실패를 확인한다.
- [ ] **3. 다운로드 구현:** `InstallationService`의 다운로드·ZIP 경로 검증·해시 receipt를 재사용한다. standalone 실행 파일 검증의 기존 `uv.exe` 예외에 `cli-proxy-api.exe`를 추가하되 llama DLL 검증은 유지한다. 기존 `runtime/8.0.27/cli-proxy-api.exe`가 맞으면 그대로 재사용하고, 새 설치는 계정 상태 폴더 아래의 관리된 runtime 경로를 쓴다.

| 고정 값 | 내용 |
|---|---|
| ZIP URL | `https://github.com/router-for-me/CLIProxyAPI/releases/download/v8.0.27/CLIProxyAPI_8.0.27_windows_amd64.zip` |
| ZIP bytes | `23629480` |
| ZIP SHA-256 | `ac6867818696aefe299c8bac1730f831cbb127f0854acca1b1aebb0110a5dc53` |
| EXE bytes | `72386560` |
| EXE SHA-256 | `a3620a7e8d54d0f4257b493ea5fc01b3c59d581fb8c3596df5880976596da162` |

ZIP size/digest는 2026-11-10 [공식 릴리즈 metadata](https://api.github.com/repos/router-for-me/CLIProxyAPI/releases/tags/v8.0.27)와 CatRoute 고지의 해시가 일치함을 확인했다.

- [ ] **4. 호스트·API 이관:** CatRoute `c5c74ad`의 제품용 `CliProxyHost`·`ProxyApi` 동작을 이관한다. 소유권 잠금 후에만 키/설정 파일을 만들고 127.0.0.1에 바인딩한다. 기존 `WindowsJob`을 사용하고 출력은 비밀 없는 bounded drain으로 처리한다. `StartAsync`의 15초 준비 예산, `/v0/management/auth-files` 확인, 로컬 API/관리 키 분리를 유지한다. Stop/Dispose는 자신의 프로세스·설정만 정리하고 잠금을 해제한다. 요청·본문 읽기는 같은 예산과 1MiB 한도를 적용한다.
- [ ] **5. Green 확인:** Core `--account-tests`. 별도 `--account-runtime-smoke`는 빈 임시 계정 폴더로 공식 엔진을 준비하고 키 없는 API 401, 관리 API 분리, 정상 종료 후 listener 해제를 확인한다. 로그인·upstream 추론은 수행하지 않는다.
- [ ] **6. Commit:** `feat(accounts): prepare and own the optional ChatGPT proxy`.

### Task 3: 로그인·사용량·연결 수명 서비스

**Files:** Create `Accounts/AccountLogin.cs`, `Accounts/AccountUsageClient.cs`, `Accounts/AccountConnection.cs`, `AccountTests.Login.cs`, `AccountTests.Usage.cs`; modify 계약과 Core 테스트 러너.

**Interfaces:** Task 1의 `IAccountConnection`을 `AccountConnection`이 구현한다. `AccountLogin`은 기존 브라우저 URL 검증·callback·관리 API 전달을 담당한다. `AccountUsageClient(HttpClient management)`의 `ReadAsync(CancellationToken ct): Task<AccountUsage>`는 기존 `UsageApi`의 결과 구조와 누락 값 보존을 담당한다.

- [ ] **1. 실패 테스트 작성:** `AuthorizationRejectsUnexpectedHostRedirectAndState`, `CancelledLoginCannotBecomeReady`, `UsageFailureDoesNotChangeConnection`, `OnlyExplicitTestSendsCompletion`, `DisposeReleasesOwnershipAfterFailure`.

```csharp
Assert(requests.All(r => r.Path != "/v1/chat/completions"), "No implicit inference");
Assert(!connection.IsRunning, "Cancellation must leave no owned proxy");
Assert(!failureText.Contains(secretMarker), "Failures must not reveal credentials");
```

- [ ] **2. Red 확인:** Core `--account-tests`에서 callback·실패 수명 assertion이 아직 충족되지 않음을 확인한다.
- [ ] **3. 로그인 구현:** `auth.openai.com:443/oauth/authorize`, 단일 state, loopback `:1455/auth/callback`, 5분 로그인 예산을 보존한다. 잘못된 state/중복 파라미터/거절/기한 만료를 거부하고, 취소 때 관리 API의 해당 OAuth 세션과 자신의 listener를 정리한다. callback 수신만으로 준비됐다고 표시하지 않고 저장 계정 1개와 기본 모델을 확인한다. 브라우저 완료 페이지 이름은 Cat Loader로 바꾼다.
- [ ] **4. 사용량·서비스 구현:** 기존 `UsageApi`의 workspace 선택과 서비스 응답 해석을 유지한다. 누락한 비율·기간·시각은 null로 유지한다. `TestAsync`만 `gpt-6-luna`·`low`·`Reply with exactly OK.`의 실제 요청을 10초 예산으로 전송한다. 기본 모델 미제공은 명시적 실패다. 로그아웃은 확인된 로컬 codex 로그인만 제거하며 원격 revoke나 키 재발급을 수행하지 않는다.
- [ ] **5. Green 확인:** Core `--account-tests`에서 fake HttpMessageHandler/loopback 서버만으로 검증한다. 취소 후 늦은 callback, 중복 callback, 부적절한 Host, usage 401·403·기간 누락을 포함한다. 테스트 기본 실행은 실사용자 계정이나 외부 추론을 사용하지 않는다.
- [ ] **6. Commit:** `feat(accounts): preserve login usage and connection contracts`.

### Task 4: ChatGPT 화면과 독립 상태 제어

**Files:** Create `AccountConnectionViewModel.cs`, `AccountConnectionView.cs`, `tests/GemmaLauncher.UiTests/AccountConnectionTests.cs`; modify `UiBindings.cs`, UI 테스트 `Program.cs`, `Locales/*.ui.json`.

**Interfaces:** `AccountConnectionViewModel(IAccountConnection connection)`은 `ObservableObject`, `IAsyncDisposable`을 구현한다. `ActivateAsync(CancellationToken)`는 저장 상태만 읽는다. `IsBusy`, `IsConnected`, `Account`, `ApiAddress`, `ApiModelId`, 마스킹한 `ApiKeyText`, `StatusTitle`, `StatusDescription`, `UsageText`를 제공한다. `PrimaryCommand`, `UsageCommand`, `RefreshCommand`, `TestCommand`, `SignOutCommand`, 복사/키 표시/로그인 페이지 다시 열기 명령을 제공한다. `OpenBrowser: Action<Uri>`와 `ConfirmSignOut: Func<bool>`은 UI가 설정한다. UI 주 스레드에서 상태·명령을 갱신하고 진행 중 작업을 추적·취소·await한다.

- [ ] **1. 실패 테스트 작성:** `ActivationReadsOnlySavedState`, `PrimaryMatchesAccountAndConnectionState`, `CancelledLoginCannotBecomeReady`, `QuotaFailureKeepsCopyAndStopAvailable`, `SignOutRequiresExplicitConfirmation`.
- [ ] **2. Red 확인:** 솔루션 빌드 후 isolated UI 러너에서 새 테스트가 상태/명령 assertion으로 실패하는지 확인한다.
- [ ] **3. 바인딩 정리:** `UiBindings`의 알림 원본을 `INotifyPropertyChanged`로 일반화한다. 로컬 전용 `Models`에는 `LauncherViewModel`을 명시적으로 전달하고 `MemoryText`에는 getter를 필수로 받는다. 기존 메모리 갱신의 단일 소비자 경로를 유지한다.
- [ ] **4. 화면·문구 구현:** 설계서의 계정 화면 순서와 주 동작 네 가지를 그대로 사용한다. 준비되지 않은 연결 정보 복사는 비활성화하고 키는 기본 마스킹한다. 사용량 조회·모델 새로고침·실제 요청 테스트를 분리한다. 새 정적 문구·오류 매핑을 17개 언어에 넣으며 API 키나 OAuth URL 전체를 알림·로그에 남기지 않는다.
- [ ] **5. Green 확인:** 전체 isolated UI 러너와 `python scripts/check-locales.py`. 마스킹, 로그인 필요/저장됨/준비 중/연결 가능/사용량 오류, 버튼 대상과 취소 후 오래된 완료 거부를 확인한다.
- [ ] **6. Commit:** `feat(ui): add the ChatGPT connection workspace`.

### Task 5: 지연 생성 셸·두 기능 수명·호환 설정

**Files:** Create `LoaderShellViewModel.cs`, `BackgroundOperationsView.cs`, `tests/GemmaLauncher.UiTests/LoaderShellTests.cs`; modify `App.cs`, `MainWindow.cs`, `AppSettingsView.cs`, `LauncherDialogs.cs`, `LauncherWorkspace.cs`, `RunOperationPanel.cs`, `SettingsStore.cs`, `UiBindings.cs`, UI 테스트 러너.

**Interfaces:** `LoaderFeature { LocalModels, ChatGpt }`. `LoaderShellViewModel(LauncherSettings settings, SettingsStore store, Func<LauncherViewModel> localFactory, Func<AccountConnectionViewModel> accountFactory)`는 `ObservableObject`, `IAsyncDisposable`을 구현한다. `SelectedFeature: LoaderFeature`, nullable `Local: LauncherViewModel?`/`Account: AccountConnectionViewModel?`, 공통 `LanguageSelection: string`·`CloseToTray: bool`을 제공한다. `SelectFeatureAsync(LoaderFeature feature, CancellationToken ct): Task<bool>`, `ResolvePendingEdits(): bool`을 제공한다. factory 호출 전에는 그 기능이 존재하지 않으며 조회용 속성은 factory를 호출하지 않는다. `MainWindow(LoaderShellViewModel shell, uint activateMessage = 0, uint legacyActivateMessage = 0)`를 추가하고 기존 `MainWindow(LauncherViewModel, uint, uint)` 호출은 테스트용 기존 로컬 인스턴스 어댑터로 유지한다. 어댑터는 창 종료 때 외부 소유 로컬 VM을 폐기하지 않는다.

- [ ] **1. 실패 테스트 작성:** `ColdStartAndNavigationDoNotConnect`, `RestoreChatDoesNotCreateLocalModel`, `CategoryChangeRespectsPresetDraft`, `StoppingOneKeepsOtherRunning`, `ExitAttemptsBothCleanupsAfterFailure`, `LegacyPreferenceAndKeyAreNotOverwritten`, `SharedSettingsPreserveModelChangesAfterNavigation`.

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
- [ ] **5. 작업 제어·종료:** `BackgroundOperationsView`는 생성된 기능만 관찰해 활성 작업의 이름·상태·중지/취소를 표시한다. 주 화면에서는 현재 기능의 주 동작을 중복하지 않고 다른 기능만 표시한다. 공통 대화상자는 두 기능의 활성 작업을 모두 보여준다. 트레이 항목을 기능별로 만들고 창 닫기 설정이 두 기능에 적용됨을 안내한다. 완전 종료는 두 취소를 요청하고 각각의 cleanup을 모두 시도한다. 미접속 계정에는 폴링 타이머가 없어야 하며 연결 중 감시는 기존 2초 프로세스 상태 확인 수준으로 제한한다.
- [ ] **6. Green 확인:** 전체 Core·isolated UI 검사. 동시 실행, 양방향 화면 이동, 숨김/복원, 한쪽 fault, 로그인 대기 중 종료, 기존 잠금 충돌을 가짜 서비스로 검증한다. 실제 HWND 캡처에서 공통 설정·초안/로그아웃 확인 중에도 각 중지/취소를 확인한다.
- [ ] **7. Commit:** `feat(shell): unify local models and account connections`.

### Task 6: 네이티브 QA·성능 기준 비교

**Files:** Modify `tests/GemmaLauncher.UiTests/Program.cs`, `Benchmark.cs`; create `LoaderRenderTests.cs`, `AccountFirstUseSmoke.cs`, `docs/work/2026-11-10-cat-loader-integration.md`.

**Interfaces:** `--render-loader`는 fake 서비스와 임시 저장 폴더만 사용한다. `--account-first-use-smoke`는 빈 계정 폴더와 실제 공식 엔진을 사용하되 OpenBrowser를 캡처 콜백으로 교체해 로그인 대기까지 확인한 뒤 취소한다. 인증 URL 전체·state·실계정은 출력하지 않는다. `--benchmark <reportPath>`는 기존 공통 네 상태 이름과 1080×860, warmup 5초, 상태당 10초 측정을 유지하고 계정 영역 상태를 추가한다.

- [ ] **1. 비교 조건 확인:** Task 0에서 보존한 실행물·기준 JSON의 해시와 측정 조건을 확인한다. 새 셸의 계정 화면에도 같은 시작 측정 경계를 적용한다. baseline 실행물은 원래 로컬 창을, 새 실행물은 기본 로컬 창과 복원된 계정 창을 각각 측정한다.
- [ ] **2. 화면 검사:** `--render-loader`로 두 기본 화면·빈/저장 계정·로그인 대기·연결 가능·권한/포트/사용량 오류·동시 동작·미저장 초안·작은 창을 캡처한다. 17개 언어와 수동 150% 확대에서 잘림·버튼 대상·키 마스킹을 확인한다. 실제 모니터 DPI 검사와 수동 확대를 구분한다.
- [ ] **3. 첫 사용 검사:** 실제 공식 엔진 다운로드 버튼부터 로그인 대기·취소·재시도까지 확인한다. 종료 후 해당 임시 폴더가 소유한 listener·자식 프로세스가 없어야 한다. 계정 없이 수행한 이 결과를 실계정 로그인·추론 성공으로 기록하지 않는다.
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

다음 단계는 이 구현 계획 검토다. 실행 방법은 이미 지정된 현재 세션·본 에이전트 순차 실행을 유지하며, 검토 승인 뒤 `superpowers:executing-plans`로 전체 작업을 수행한다.
