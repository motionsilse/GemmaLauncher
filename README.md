# Cat Loader by Drugged Cat

Prepare an OpenAI-compatible API for other apps from one small Windows launcher. Choose **Local models** to run a model on your PC, or **ChatGPT connection** to connect a ChatGPT account through the optional proxy engine. Each has its own address, status and stop button.

Local models use RAM/VRAM. ChatGPT needs internet access and shares the account's usage limits. Opening the app or changing categories starts neither service. Both can run together.

## ChatGPT connection

Open **ChatGPT connection** and choose **Sign in to ChatGPT**. If needed, Cat Loader downloads and verifies the official CLIProxyAPI v8.0.27 engine before opening sign-in in your browser. Complete sign-in yourself, then choose **Start server**. Login prepares the saved account; the separate server button opens the local API. **Stop server** keeps the login, while **Sign out** confirms and removes the saved login.

Copy the API address (`http://127.0.0.1:8319/v1`), local API key and model (`gpt-6-luna`) into your app. **Check usage** shows remaining quota for each period and its reset time. **Connection diagnostics → Test with a real request** sends one inference request and can count toward usage. A saved login and a running API are separate states. The default model is never silently replaced if unavailable.

Closing the window follows the common tray setting for both services; **Full exit** stops both. Existing GemmaLauncher data and CatRoute account/key folders are preserved. Exit the old CatRoute app before connecting. See [upgrade details](docs/migrations/cat-loader-0.4.md) and [third-party sources](THIRD-PARTY-NOTICES.md).

## Getting started

1. Download the Windows x64 EXE from the [latest stable release](https://github.com/motionsilse/GemmaLauncher/releases/latest).
2. Install the **.NET 10 Desktop Runtime (x64)** and **ASP.NET Core Runtime 10 (x64)**, then run the EXE. The .NET 10 SDK also includes both. .NET 8 alone is insufficient. No ZIP extraction or companion folder is needed.
3. Choose one of the **six built-in models** on the main screen. Choose **Download and start**, or **Start server** if its files are ready. No library registration is needed.
4. Copy the API address and model name into your connected app. GGUF models also offer a Web UI under advanced run settings.

The first download needs internet access and sufficient disk space. Keep your graphics card's Vulkan driver current. If Microsoft Visual C++ x64 runtime components are missing, the launcher links to Microsoft's installer; that installer may require administrator approval.

Runtime installers: [Microsoft .NET downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Choose both Windows x64 runtimes; the Desktop installer includes the base .NET runtime. See [Microsoft's installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows#choose-the-correct-runtime).

All six built-in choices remain visible before downloading. The first launch selects E2B without starting a download. A check marks prepared files separately from each model's purpose. **Add model** connects your own GGUF files or public Hugging Face repositories. Stop and cancel remain available while editing presets or opening settings dialogs.

## Choose by purpose

| Model | Best suited to | Model download |
|---|---|---:|
| Translate Gemma 4 · E2B | Lightweight, fast translation of game dialogue and subtitles | 3.26 GB |
| Translate Gemma 4 · E4B | Translation that prioritizes context, nuance and expression | 5.20 GB |
| HY-MT2 · 1.8B Q4_K_M | Responsive multilingual translation with a smaller memory footprint | 1.13 GB |
| HY-MT2 · 7B Q4_K_M | Quality-focused translation of complex sentences and context | 4.62 GB |
| Gemma 4 · 12B Heretic StyleTune | Translation, writing, roleplay and natural conversation | 8.30 GB |
| ANIS Trans (Japanese only) | Specialized Japanese ↔ Korean translation | 0.30 GB |

E2B and E4B combine dialogue/subtitle tuning with SSD-backed memory optimization. The 12B model combines uncensored tuning for freer expression with StyleTune for a distinctive voice. These three Gemma configurations include their own MTP acceleration files. HY-MT2 uses its own translation settings and runs without MTP.

Selecting a model shows its model storage, recommended free space for first installation, and approximate RAM/VRAM at default settings. GB means one billion bytes; Gemma totals include MTP files. The execution engine downloads separately. Installation space includes a 2 GB allowance for runtime, extraction and caches; connected local files are not copied. Memory references are estimates, not minimum PC specifications or measured guarantees. GPU offload, context, cache and CPU mode change memory use. Live server measurements remain separate. See [estimate basis](docs/model-resource-estimates.md).

## Extend your library

- **Local GGUF:** choose **Add model → Connect GGUF**. Files stay in their original location. Registered models are recognized by their contents, even after renaming.
- **Hugging Face:** enter a public repository address, choose a GGUF, and add it. Starting the model downloads and verifies that exact file.
- **My models:** reuse registered models, open their folders, or remove a custom entry without deleting its file.
- **Advanced load settings:** choose GPU offload, KV cache, Flash Attention, reasoning and a custom chat template. Settings are saved per model. Reset restores defaults and disconnects a custom MTP override.
- **Optional MTP:** connect the main model, then attach a compatible Gemma 4 assistant. Vocabulary and target dimensions must match; unrelated draft models cannot be used as MTP.

Custom imports support single-file text-generation GGUF architectures available in the bundled llama.cpp version. Split models, image/audio projectors and arbitrary execution engines are outside this import flow. Missing chat templates can be supplied in advanced settings, up to 14,000 characters. Private or gated Hugging Face files can be downloaded with your account and linked locally.

Advanced model-list JSON import remains available; see [catalog format](docs/model-catalog.md). Adding a model does not require another executable.

## Prompts, glossary and API

Choose **Edit** beside the main screen's task preset. Instructions and glossary have separate full-width tabs; generation options stay collapsed. Enter terminology directly or import a two-column CSV/TSV (`source,target`). **Save and apply** saves the draft and assigns it to the selected model. Leaving with changes asks whether to save, discard, or continue editing. Shared presets identify affected models and offer a separate copy.

Saved instructions and terminology apply to both Web UI and API requests. Client messages, output-format constraints and tool definitions are preserved. Generation defaults only fill values the client did not specify. Saving or selecting a preset affects the next request; running responses keep their original settings. **Model defaults** disables added instructions and glossary.

- API base URL: `http://127.0.0.1:8080/v1`
- Web UI: `http://127.0.0.1:8080/`
- Model ID: copy the model name shown beside the API address.

The service is available on this PC only. Streaming and cancellation are supported. Text presets require text prompts or user messages; raw token-only requests can use **Model defaults**. If port 8080 is occupied, stop the other server first.

## ANIS Trans

[ANIS · jintigers](https://huggingface.co/jintigers/anis-trans) provides the sixth model and its [official API program](https://github.com/jintigers/anis-trans). Select it and choose **Download and start**. If access is required, the launcher opens a first-use dialog with the official model/terms link and a masked download-token field. Obtain approval with your own Hugging Face account and supply a read token from that account. The token is used for this download only and is never saved. Connecting an existing official model folder is also supported.

The launcher verifies downloads and prepares a private Python/CTranslate2 environment automatically. Global Python settings are unchanged. It runs the upstream API without a separate translation GUI, using the same local API address. Select automatic direction, Japanese → Korean, or Korean → Japanese; the displayed model ID changes accordingly. ANIS does not use LLM prompt presets or the GGUF Web UI. Its [separate license](https://github.com/jintigers/anis-trans/blob/1165f0cda25a9afdd0c8e0491038e949d9446e6e/LICENSE-ko.md) applies to the model and official program; commercial use requires a separate agreement with ANIS. Their files are downloaded from official channels, not bundled in this EXE.

## Window and language

**X hides the window in the tray by default.** Change this in **Settings** to exit instead. The tray can restore the launcher or stop/quit its server. Opening the EXE again restores the existing window. One model runs at a time; stop it before switching models. GGUF context and load-option changes take effect with **Apply and restart**.

Windows display language is selected automatically, with English fallback. The language selector in **Settings** overrides it and remembers your choice. All 17 CatBridge languages are included: English, Korean, Japanese, simplified/traditional Chinese, Spanish, Portuguese, French, German, Filipino, Vietnamese, Russian, Polish, Indonesian, Malay, Turkish and Thai.

## Build and license

Requires Windows, .NET 10 SDK and Python 3.

```powershell
dotnet build GemmaLauncher.sln -c Release
.\tests\GemmaLauncher.Tests\bin\Release\net10.0-windows\GemmaLauncher.Tests.exe
.\tests\GemmaLauncher.UiTests\bin\Release\net10.0-windows\GemmaLauncher.UiTests.exe
python -m unittest discover -s tests/scripts
powershell -ExecutionPolicy Bypass -File scripts/publish-launcher.ps1
```

The publish script checks the catalog and 17 locales, builds a framework-dependent single EXE under `dist`, bundles launcher notices, and verifies the exact package and HTTP gateway in an otherwise empty directory using the installed .NET 10 runtimes. The UI uses WinForms; it does not reference WPF. Server memory telemetry runs only while Run is visible (not minimized or hidden in the tray). Registrations live in `config/launcher-models.json`; regenerate after editing registrations or English model copy with `python scripts/create-product-catalog.py`.

To run the UI harness without showing test windows on your desktop, use `pwsh -File scripts/run-isolated-ui-tests.ps1 -Executable tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe`. It creates a private Windows desktop and temporary model settings. The default suite uses simulated sessions. Optional `-Arguments '--first-download-smoke' -TimeoutSeconds 1200` exercises the actual main download button, runtime/model downloads and CPU inference in a fresh private data directory; it needs internet access and roughly 2 GB of disk space. It also checks the ANIS access dialog without accepting terms or supplying credentials. `--render` captures native layouts under `artifacts/ux-redesign`.

Use `--data-dir "D:\GemmaLauncherData" --model-folder "D:\Models"` for custom data locations, or `--licenses` for embedded notices. Source uses the [MIT license](LICENSE): free use, modification and redistribution, including commercial use, with copyright and license attribution retained. Models and dependencies have their own [terms and notices](THIRD-PARTY-NOTICES.md).

---

# Gemma 모델 런처 by Drugged Cat

AI 모델의 설치부터 실행·관리까지, 클릭 한 번으로 시작하세요. 번역·글쓰기·대화에 맞는 모델을 고르면 필요한 파일을 준비하고 서버를 켭니다. **브라우저 대화 화면과 OpenAI 호환 API**를 함께 제공하므로 CatBridge 같은 앱에도 바로 연결할 수 있습니다.

Windows EXE 하나로 시작하고, 원하는 모델을 추가하고, 저장한 프롬프트와 용어사전을 연결 앱에서도 함께 사용하세요.

## ChatGPT 연결

**ChatGPT 연결 → ChatGPT 로그인**을 눌러 브라우저에서 인증한 다음 **서버 켜기**를 누르세요. 처음에는 공식 CLIProxyAPI 엔진을 내려받아 검증합니다. 로그인과 API 서버 실행은 별도 동작입니다. **서버 끄기**는 로그인을 유지하고, 계정 영역의 **로그아웃**은 확인 후 저장된 로그인을 지웁니다.

실행 후 API 주소 `http://127.0.0.1:8319/v1`, 로컬 API 키와 모델 `gpt-6-luna`를 연결 앱에 복사하세요. **사용량 조회**는 기간별 남은 사용량과 초기화 시각을 보여줍니다. **연결 진단**에서 모델 확인과 실제 요청 테스트를 할 수 있으며 실제 요청은 계정 한도를 사용합니다. 인터넷과 본인 계정이 필요하며 로컬 모델 서버와 함께 실행할 수 있습니다. 창 닫기·트레이 설정은 두 기능에 공통으로 적용됩니다. [기존 데이터 유지와 업데이트](docs/migrations/cat-loader-0.4.md)도 참고하세요.

## 시작하기

1. [최신 정식 릴리즈](https://github.com/motionsilse/GemmaLauncher/releases/latest)에서 Windows x64 EXE를 내려받습니다.
2. **.NET 10 Desktop Runtime(x64)**과 **ASP.NET Core Runtime 10(x64)**을 설치한 뒤 EXE를 실행합니다. .NET 10 SDK에도 둘 다 포함됩니다. .NET 8만 설치되어 있으면 실행할 수 없습니다. 압축 해제나 별도 폴더는 필요하지 않습니다.
3. 메인 화면에 펼쳐진 **기본 모델 6개** 중 하나를 고르고 **다운로드하고 시작**을 누르세요. 파일이 있으면 **서버 켜기**로 바로 실행합니다. 별도 등록은 필요하지 않습니다.
4. 같은 화면의 API 주소와 모델 이름을 복사해 다른 앱에 입력하세요. GGUF 모델은 **고급 실행 설정**에서 Web UI도 열 수 있습니다.

첫 다운로드에는 인터넷과 저장 공간이 필요합니다. 그래픽카드의 Vulkan 드라이버를 최신 상태로 유지하세요. Microsoft Visual C++ x64 실행 구성 요소가 없으면 공식 설치 프로그램을 안내합니다. 해당 설치에는 관리자 승인이 필요할 수 있습니다.

기본 모델 6개는 다운로드 여부와 관계없이 표시됩니다. 처음에는 E2B를 선택해 안내하며 자동으로 받거나 실행하지 않습니다. 모델 용도는 설명으로, 파일 준비 상태는 별도 체크로 구분합니다. **모델 추가**에서 사용자 GGUF를 연결할 수 있습니다. 프롬프트 편집 화면과 설정 대화상자에서도 다운로드 취소·서버 종료를 사용할 수 있습니다.

## 용도에 맞게 고르세요

| 모델 | 이런 용도에 어울립니다 | 모델 다운로드 |
|---|---|---:|
| Translate Gemma 4 · E2B | 게임 대사와 자막을 가볍고 빠르게 번역할 때 | 3.26 GB |
| Translate Gemma 4 · E4B | 문맥과 표현의 뉘앙스를 살리는 번역이 필요할 때 | 5.20 GB |
| HY-MT2 · 1.8B Q4_K_M | 메모리 부담을 줄이면서 다국어 번역을 빠르게 사용할 때 | 1.13 GB |
| HY-MT2 · 7B Q4_K_M | 복잡한 문장과 문맥의 정확성·표현력을 중시할 때 | 4.62 GB |
| Gemma 4 · 12B Heretic StyleTune | 번역, 글쓰기, 롤플레잉과 사람 같은 자연스러운 대화 | 8.30 GB |
| ANIS Trans (일본어 전용) | 일본어 ↔ 한국어에 특화된 번역 | 0.30 GB |

E2B와 E4B에는 대사·자막 번역 튜닝과 SSD를 활용한 메모리 최적화를 적용했습니다. 12B에는 무검열 튜닝으로 자유로운 표현을, 스타일 튜닝으로 말투의 개성을 더했습니다. Gemma 세 구성에는 각각 맞는 MTP 가속용 파일이 포함됩니다. HY-MT2는 별도의 번역 설정을 사용하며 MTP 없이 실행합니다.

모델을 선택하면 모델 저장공간, 첫 설치 권장 여유공간과 기본 설정의 RAM·VRAM 추정치를 보여줍니다. GB는 10억 바이트 기준이며 Gemma 용량에는 MTP도 포함됩니다. 설치 여유공간에는 실행환경·압축 해제·캐시용 2 GB를 더하며, 연결한 로컬 파일은 복사하지 않습니다. 메모리 수치는 최소 PC 사양이나 보장된 실측값이 아닙니다. GPU 할당·대화 길이·캐시·CPU 모드에 따라 달라지며 실행 중 실측값은 별도로 표시합니다. [추정 근거](docs/model-resource-estimates.md)를 참고하세요.

## 원하는 모델을 추가하세요

- **이미 받은 GGUF:** **모델 추가 → GGUF 파일 연결**에서 선택합니다. 원본을 이동하거나 복사하지 않습니다. 등록된 모델은 이름을 바꿔도 파일 내용으로 알아봅니다.
- **Hugging Face:** 공개 저장소 주소를 입력하고 GGUF를 골라 추가합니다. 서버를 켤 때 선택한 파일을 다운로드하고 검증합니다.
- **내 모델:** 등록한 모델을 다시 사용하거나 폴더를 엽니다. 사용자 모델 항목을 삭제해도 원본 파일은 남습니다.
- **고급 실행 설정:** GPU 사용량, KV 캐시, Flash Attention, 추론 모드와 대화 템플릿을 모델별로 저장합니다. 초기화하면 기본값으로 돌아가며 직접 연결한 MTP도 해제됩니다.
- **MTP 연결:** 본체를 연결한 뒤 호환되는 Gemma 4 가속용 파일을 추가할 수 있습니다. 어휘와 대상 크기를 확인하므로 관계없는 모델을 MTP로 연결할 수는 없습니다.

직접 추가하는 모델은 포함된 llama.cpp 버전에서 지원하는 단일 파일 텍스트 생성 GGUF를 대상으로 합니다. 분할 파일, 이미지·음성 보조 파일과 별도 실행 엔진은 이 기능으로 추가하지 않습니다. 대화 템플릿이 없는 모델은 고급 설정에서 최대 14,000자의 템플릿을 입력할 수 있습니다. 비공개·접근 승인 저장소는 본인 계정으로 먼저 내려받은 뒤 로컬 파일을 연결하세요.

고급 기능의 모델 목록 JSON 가져오기도 유지합니다. [모델 카탈로그 작성법](docs/model-catalog.md)을 참고하세요. 모델마다 새 런처를 만들 필요가 없습니다.

## 프롬프트·용어사전과 API

메인 화면의 작업 설정 옆 **편집**을 누르세요. 지시문과 용어사전은 넓은 탭에서 편집하고 생성 옵션은 필요할 때 펼칩니다. 사전은 직접 입력하거나 원문·번역 두 열의 CSV/TSV(`source,target`)를 가져올 수 있습니다. **저장하고 적용**을 눌러 선택한 모델에 적용하세요. 수정 중 이동하면 저장·버리기·계속 편집을 선택합니다. 여러 모델이 쓰는 설정은 영향받는 모델을 표시하고 복제할 수 있습니다.

저장한 지시문과 사전은 Web UI와 연결 앱에 함께 적용합니다. 앱이 보낸 대화, 출력 형식과 도구 정의는 유지합니다. 생성 설정은 앱이 값을 지정하지 않았을 때 기본값으로 사용합니다. 저장하거나 바꾸면 다음 요청부터 반영하며 진행 중인 응답에는 기존 설정을 유지합니다. **모델 기본값**은 추가 지시문과 사전을 적용하지 않습니다.

- API 주소: `http://127.0.0.1:8080/v1`
- Web UI: `http://127.0.0.1:8080/`
- 모델 이름: 런처의 API 주소 옆에 표시된 값을 복사합니다.

이 PC 안에서만 접속할 수 있으며 스트리밍과 취소를 지원합니다. 텍스트 프리셋은 글 또는 사용자 메시지가 필요합니다. 숫자 토큰만 보내는 요청에는 **모델 기본값**을 사용하세요. 다른 프로그램이 8080 포트를 사용 중이면 해당 서버를 먼저 종료하세요.

## ANIS Trans

여섯 번째 모델은 [ANIS · jintigers](https://huggingface.co/jintigers/anis-trans)의 일본어 ↔ 한국어 전용 모델입니다. 선택 후 **다운로드하고 시작**을 누르면 접근 승인이 필요한 경우 공식 모델·이용 조건 링크와 다운로드 토큰 입력 창을 보여줍니다. 본인 Hugging Face 계정으로 접근 승인을 받고 해당 계정의 읽기 토큰을 입력하세요. 토큰은 이번 다운로드에만 쓰며 저장하지 않습니다. 이미 받은 공식 모델 폴더도 연결할 수 있습니다.

파일 검증과 전용 Python/CTranslate2 실행 환경 준비는 런처가 처리합니다. 시스템 Python 설정을 바꾸지 않으며 별도 번역 GUI 없이 [공식 API 프로그램](https://github.com/jintigers/anis-trans)을 실행합니다. API 주소는 다른 모델과 같고 자동 방향·일본어 → 한국어·한국어 → 일본어에 따라 모델 ID가 바뀝니다. ANIS에는 LLM 프롬프트 프리셋과 GGUF Web UI를 적용하지 않습니다. 모델과 공식 프로그램에는 [별도 라이선스](https://github.com/jintigers/anis-trans/blob/1165f0cda25a9afdd0c8e0491038e949d9446e6e/LICENSE-ko.md)가 적용되며 영리 이용은 ANIS와 별도 계약이 필요합니다. 해당 파일은 공식 경로에서 내려받으며 EXE에 포함하지 않습니다.

## 창과 표시 언어

기본 동작은 **X를 누르면 트레이로 숨기기**입니다. **설정**에서 창을 닫을 때 완전 종료하도록 바꿀 수 있습니다. 트레이에서 런처 복원과 서버 종료를 할 수 있고, EXE를 다시 실행하면 기존 창이 열립니다. 모델은 한 번에 하나만 실행하며 다른 모델로 바꾸려면 먼저 서버를 끄세요. GGUF의 글 길이와 고급 실행 설정은 **적용하고 재시작**으로 반영합니다.

Windows 표시 언어를 자동으로 사용하고 지원하지 않는 언어는 영어로 표시합니다. **설정**에서 직접 고른 언어도 기억합니다. CatBridge와 같은 17개 언어를 지원합니다: 영어, 한국어, 일본어, 중국어 간체·번체, 스페인어, 포르투갈어, 프랑스어, 독일어, 필리핀어, 베트남어, 러시아어, 폴란드어, 인도네시아어, 말레이어, 튀르키예어, 태국어.

## 소스와 이용 조건

Windows, .NET 10 SDK와 Python 3에서 위의 빌드 명령을 실행합니다. 배포 스크립트는 모델 목록과 17개 언어를 검사하고 `dist`에 단일 EXE와 SHA-256 파일을 만듭니다. 시스템 .NET 10을 사용하며 런타임은 번들하지 않습니다. 런처 라이선스 고지를 포함하고 다른 파일이 없는 폴더에서 패키지와 HTTP 중계를 검증합니다. 화면은 WPF 의존성이 없는 WinForms를 사용하고, 서버 메모리 조회는 실행 화면이 보일 때만 동작합니다. 모델 등록 정보는 `config/launcher-models.json`에 있습니다.

`--data-dir "D:\GemmaLauncherData" --model-folder "D:\Models"`로 저장 위치를 지정하고 `--licenses`로 내장 고지를 엽니다. 런처 소스는 [MIT 라이선스](LICENSE)로 상업적 이용을 포함해 자유롭게 사용·수정·재배포할 수 있습니다. 원래 저작권 표시와 라이선스 문구는 유지해야 합니다. 모델과 구성 요소에는 각각의 [별도 이용 조건](THIRD-PARTY-NOTICES.md)이 적용됩니다.
