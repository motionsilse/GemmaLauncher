# GemmaLauncher 경량화 결과 — 2026-10-14

로컬 `860170b`의 기능을 유지하며 WPF를 WinForms로 교체했다. 검증본은 `0.2.0-preview.3`이다. [원시 측정값](2026-10-14-lightweight-launcher-data.json)에 각 실행의 수치와 패키지 해시를 보관했다. 공개 저장소의 `f6f0c5409fc21e200881b8d2906233aac12e3e0c`는 같은 기준 제품 소스와 테스트를 보관하여 비교를 재현할 수 있게 한다.

## 기준과 변경 범위

공개 [0.1.4 README](https://github.com/motionsilse/GemmaLauncher/blob/v0.1.4/README.md)는 기존 세 모델 중심의 실행·파일 연결 기능을 설명한다. 로컬 최신판에는 HY-MT2, 사용자 모델 라이브러리, Hugging Face 등록, 공유 프리셋과 용어사전이 추가되어 있어 로컬 최신판을 기준으로 선택했다. 공개 이력과 로컬 연구 이력이 별도이므로 공개 저장소의 기존 이력 위에 런처 제품 소스와 테스트를 반영했다. 기존 미추적 연구 문서 다섯 개와 CatRoute 프로젝트는 수정하지 않았다.

- `UseWPF`, XAML, WPF Application/Dispatcher/Clipboard/대화상자 의존성을 제거했다. `ICommand`와 ObservableCollection은 계속 사용한다. 최종 앱의 어셈블리 참조에는 PresentationFramework, PresentationCore, WindowsBase가 없다.
- WinForms ApplicationContext, Form, NotifyIcon과 기본 컨트롤을 사용한다. 실행·모델·프리셋 화면과 모델 하위 화면은 처음 열 때 생성한다. 서버 중지·다운로드 취소는 어느 탭에서도 접근할 수 있다.
- 사용자별 mutex 이름, 이전 데이터 폴더 기반 활성화 메시지, X로 숨기기, 복원, Ctrl+Q/트레이 완전 종료를 유지했다. 종료는 진행 중인 작업과 소유 서버 정리를 기다린다.
- 모델/엔진 다운로드, 검증, 설정 형식, GGUF/Hugging Face 추가, MTP, 실행 옵션, 컨텍스트 재시작, API/Web UI 중계 및 프리셋 의미를 유지했다. Core 소스는 변경하지 않았다.
- 실행 화면이 보이고 최소화되지 않았을 때만 서버 메모리를 조회한다. 같은 PID의 ProcessMetrics를 재사용하며 숨기기·중지·종료 시 진행 중인 읽기를 기다리고 해제한다. 메모리 숫자 갱신은 해당 라벨만 수정한다.
- 배포는 시스템 .NET을 사용하는 단일 EXE다. **.NET 10 Desktop Runtime(x64)과 ASP.NET Core Runtime 10(x64)**이 필요하다. .NET 8만으로는 실행되지 않는다. Windows .NET 10 SDK에도 필요한 런타임이 포함된다. [Microsoft 설치 안내](https://learn.microsoft.com/en-us/dotnet/core/install/windows#choose-the-correct-runtime).

## 측정 방법과 한계

Windows의 별도 비활성 데스크톱에서 실제 창과 ViewModel을 생성하는 실행형 테스트 하네스를 사용했다. 기존 사용자 창·브라우저·실행 중인 서버·정상 런처 인스턴스는 조작하지 않았다. 하네스는 제품의 사용자 mutex를 획득하지 않는다.

두 버전 모두 SDK 10.0.401, 런타임 10.0.12, Release, 같은 카탈로그·선택 모델·한국어·1080×860 창과 새 임시 설정을 사용했다. 각 상태에서 5초 안정화 후 1초 간격으로 10회 관측했다. 버전별 새 프로세스 세 개의 평균을 아래에 표시했다. 강제 GC, Working Set 축소, 캐시 강제 비우기는 하지 않았다.

**서버 상태는 가짜 IServerSession과 별도의 대기 자식 프로세스로 재현했다. 실제 GGUF 추론이나 ASP.NET 게이트웨이를 이 자원 측정 중 실행한 것은 아니다.** 따라서 서버 상태 행은 런처의 상태 표시와 PID 메모리 조회 비용을 비교한다. 게이트웨이 기능은 별도 Core 테스트와 정확한 배포 EXE 검사로 검증했다. 자식 프로세스의 수치는 원시 자료에 따로 저장했고 아래 런처 메모리에 합치지 않았다.

숨김 상태는 창을 숨겨 재현했다. 테스트 데스크톱에는 일반 Explorer 트레이가 없으므로 실제 트레이 아이콘과 DWM 전체 비용을 비교한 것은 아니다. 일반 데스크톱, 다른 DPI/GPU 드라이버, 여러 페이지를 방문한 후, 실제 모델 추론 중에는 절대 수치가 달라질 수 있다.

## 런처 메모리

MiB는 1,048,576바이트다. Working Set은 상주 메모리, Private Bytes는 프로세스 전용 커밋 메모리다. UI가 모델 서버에 표시하는 private working set과 Private Bytes는 다른 지표다.

| 상태 | WPF Working Set | WinForms Working Set | WPF Private Bytes | WinForms Private Bytes |
|---|---:|---:|---:|---:|
| 창 표시, 대기 | 157.71 MiB | 94.40 MiB | 109.17 MiB | 35.74 MiB |
| 창 숨김, 대기 | 157.44 MiB | 94.54 MiB | 107.44 MiB | 35.53 MiB |
| 창 표시, 서버 상태 재현 | 168.05 MiB | 103.33 MiB | 115.82 MiB | 41.91 MiB |
| 창 숨김, 서버 상태 재현 | 167.65 MiB | 102.89 MiB | 115.22 MiB | 41.17 MiB |

표시 중 대기 상태에서 Working Set은 약 **40%**, Private Bytes는 약 **67%** 감소했다. 이 값은 테스트 하네스에서 관측한 런처 측 비교이며 모델 RAM/VRAM 절감 수치가 아니다.

## CPU, I/O, 그래픽

약 10초 관측 구간의 프로세스 CPU 시간이다. 평균 뒤 괄호는 세 실행의 최소–최대다. 프로세스 조회를 하는 하네스 자체의 비용도 포함된다.

| 상태 | WPF CPU 시간 | WinForms CPU 시간 |
|---|---:|---:|
| 창 표시, 대기 | 62.50 ms (31.25–125) | 72.92 ms (62.50–78.13) |
| 창 숨김, 대기 | 78.13 ms (62.50–93.75) | 67.71 ms (46.88–93.75) |
| 창 표시, 서버 상태 재현 | 109.38 ms (62.50–171.88) | 67.71 ms (31.25–125) |
| 창 숨김, 서버 상태 재현 | 52.08 ms (31.25–62.50) | 41.67 ms (15.63–62.50) |

서버 상태에서는 평균 CPU 시간이 낮아졌지만 구간이 겹치고 절대량도 작다. **CPU의 일관된 대폭 절감을 입증했다고 보기는 어렵다.** 숨김·최소화·다른 탭에서 조회 타이머가 정지하는 것은 별도 동작 테스트로 확인했다. 최초 구현에서 보인 전체 UI 갱신 비용은 메모리 라벨만 갱신하도록 수정했다. 모든 최종 관측 구간에서 읽기/쓰기 I/O 바이트와 작업 수는 0이었다. 시작·다운로드·설정 저장 I/O는 이 관측 구간에 포함하지 않는다.

그래픽 카운터는 RAM/CPU 측정 프로세스와 분리한 두 번의 실행에서 관측했다.

| 지표 | WPF | WinForms |
|---|---:|---:|
| 프로세스 전용 GPU 메모리 | 20,373,504 / 20,377,600 B (약 19.43 MiB) | 0 / 0 B |
| 프로세스 공유 GPU 메모리 | 3,624,960 / 3,624,960 B | 0 / 0 B |
| WPF 렌더링 모듈 | PresentationFramework/Core, PresentationNative, wpfgfx 로드 | 로드 없음 |

WinForms에서는 기본 HWND 컨트롤을 사용하므로 GDI/USER 객체 수는 증가했다. 원시 자료의 최종 실행에서 WPF는 GDI 64개/USER 약 68개, WinForms는 GDI 약 88–90개/USER 약 106–110개였다. 위 GPU 0은 해당 테스트에서 런처 PID에 귀속된 카운터 값이며 시스템 전체 그래픽 비용이 0이라는 뜻은 아니다.

## 배포 크기와 .NET 번들 효과

같은 SDK로 기준 소스를 다시 빌드해 UI 전환과 런타임 제외 효과를 분리했다. 모든 단일 EXE는 카탈로그·언어·내장 고지·게이트웨이 시작 검사를 통과했다.

| 패키지 | 바이트 | 십진 MB |
|---|---:|---:|
| 기준 WPF, 런타임 포함 | 87,657,076 | 87.66 |
| 기준 WPF, 시스템 런타임 사용 | 1,136,285 | 1.14 |
| 최종 WinForms, 시스템 런타임 사용 | 988,829 | 0.99 |

총 감소는 약 **98.9%**이며 대부분은 .NET 런타임을 EXE에서 제외한 효과다. 시스템 런타임의 설치 용량이 사라진 것은 아니다. WPF와 그에 필요한 앱 리소스/고지 구성 변경의 추가 차이는 위 두 framework-dependent 빌드 사이에 나타난다. 기존 디스크의 preview.2 EXE는 87,735,658바이트였으며, 표에는 동일 SDK로 재생성한 기준 파일을 사용했다.

최종 파일: `dist/GemmaLauncher-0.2.0-preview.3-win-x64.exe`

SHA-256: `621875aa5589b021d86fdc9f4b8bce1036a38c7a563b3e630aa3fe7075639483`

## 검증과 코드 검토

- 솔루션 Release 빌드: 경고 0, 오류 0.
- 기존 WPF 하네스: 변경 전 격리 데스크톱에서 통과.
- Core 실행형 테스트: Hugging Face, gateway, library, installation, session, catalog/settings, metrics, prerequisites, localization 통과.
- 새 WinForms 실행형 테스트 13개 시나리오: 시작 스캔, 실행/재시작/취소, 종료 대기, 파일 연결/취소/롤백, 선택 실패 복원, 빈 라이브러리, 모델 등록/제거, 실제 컨트롤 선택과 진행률, 편집 중 용어사전 저장/삭제, 모든 언어, 작은 작업 영역과 150% 배율 모사, 표시 상태별 telemetry 수명 검증 통과.
- 두 활성화 메시지를 숨긴 테스트 창의 HWND에 전달하여 각각 한 번 수신하는지 확인했다. 일반 사용자 세션의 mutex/창은 건드리지 않았다.
- ProcessMetrics 인스턴스 재사용, 숨김 후 실제 Dispose, 빠른 숨김/복원과 종료 시 읽기 정리, 새 메모리 샘플의 라벨 반영을 확인했다.
- Python 테스트 5개, 17개 언어/3개 도메인/306개 키 검사, 카탈로그 생성물 일치 검사 통과.
- 최종 단일 EXE의 빈 폴더 검증 통과. `--licenses`의 내장 텍스트는 확인했으며 사용자 텍스트 뷰어를 열지는 않았다.

코드 검토에서는 저장 실패 후 선택 복원, 컨트롤 이벤트 구독 해제, 지연 생성 시 초기 탭 선택, telemetry의 읽기/해제 순서, 완전 종료 대기, 기존 인스턴스 프로토콜, 배포 런타임/고지 일치를 순차 점검했다. 초기 탭 생성 문제, telemetry에 따른 전체 UI 갱신, 시작 실패 로그 누락을 수정하고 관련 검증을 다시 실행했다. 실제 모델 추론, 일반 Explorer 트레이의 상호작용, 모니터 간 실제 DPI 전환은 이번 자동 검증 범위에 포함하지 않았다.

## 재현

```powershell
python scripts/prepare-launcher-baseline.py --ref f6f0c5409fc21e200881b8d2906233aac12e3e0c --output artifacts/lightweight/baseline-reference
dotnet build artifacts/lightweight/baseline-reference/tests/GemmaLauncher.UiTests -c Release
dotnet build GemmaLauncher.sln -c Release

& ./scripts/run-isolated-ui-tests.ps1 `
  -Executable tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe

& ./scripts/run-isolated-ui-tests.ps1 `
  -Executable artifacts/lightweight/baseline-reference/tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe `
  -Arguments '--benchmark D:/work/gemma4/artifacts/lightweight/baseline-repeat.json'

& ./scripts/run-isolated-ui-tests.ps1 `
  -Executable tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe `
  -Arguments '--benchmark D:/work/gemma4/artifacts/lightweight/native-repeat.json'
```

저장소가 다른 위치라면 결과 경로를 바꾼다. 기준 준비 스크립트는 새 `artifacts` 하위 폴더만 허용하며 기존 폴더는 덮어쓰지 않는다. 역사적 제품 코드는 바꾸지 않고 테스트 하네스에 공통 측정 루틴만 추가한다. 그래픽 관측은 같은 명령에서 `--benchmark` 대신 `--graphics-probe`를 사용한다. 실제 사용자 런처 EXE를 성능 테스트용으로 실행하지 않는다.
