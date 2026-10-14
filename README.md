# Gemma Model Launcher by Drugged Cat

Install, run and manage local AI with a single click. Choose a model for translation, writing or conversation; the launcher prepares the files and provides both a **browser chat UI and an OpenAI-compatible API** for apps such as CatBridge.

One Windows EXE, an extensible model library, and reusable prompts and glossaries across connected apps.

## Getting started

1. Download the Windows x64 EXE from [Releases](https://github.com/motionsilse/GemmaLauncher/releases).
2. Install the **.NET 10 Desktop Runtime (x64)** and **ASP.NET Core Runtime 10 (x64)**, then run the EXE. The .NET 10 SDK also includes both. .NET 8 alone is insufficient. No ZIP extraction or companion folder is needed.
3. In **Models**, compare the options and choose **Select this model**. In **Run**, review the settings and choose **Download and start**, or **Start server** if the files are ready.
4. Choose **Open Web UI**, or connect another app using the API address and model name shown in **Run**.

The first download needs internet access and sufficient disk space. Keep your graphics card's Vulkan driver current. If Microsoft Visual C++ x64 runtime components are missing, the launcher links to Microsoft's installer; that installer may require administrator approval.

Runtime installers: [Microsoft .NET downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Choose both Windows x64 runtimes; the Desktop installer includes the base .NET runtime. See [Microsoft's installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows#choose-the-correct-runtime).

Selecting or adding a model saves it in **My models** and opens **Run**. Both screens use the same registered models, including those awaiting download. File status shows what is ready or needs attention. Browsing other variants keeps the execution selection unchanged; downloads and running servers retain their cancel or stop control across tabs.

## Choose by purpose

| Model | Best suited to | Model download |
|---|---|---:|
| Translate Gemma 4 · E2B | Lightweight, fast translation of game dialogue and subtitles | 3.26 GB |
| Translate Gemma 4 · E4B | Translation that prioritizes context, nuance and expression | 5.20 GB |
| HY-MT2 · 1.8B Q4_K_M | Responsive multilingual translation with a smaller memory footprint | 1.13 GB |
| HY-MT2 · 7B Q4_K_M | Quality-focused translation of complex sentences and context | 4.62 GB |
| Gemma 4 · 12B Heretic StyleTune | Translation, writing, roleplay and natural conversation | 8.30 GB |

E2B and E4B combine dialogue/subtitle tuning with SSD-backed memory optimization. The 12B model combines uncensored tuning for freer expression with StyleTune for a distinctive voice. These three Gemma configurations include their own MTP acceleration files. HY-MT2 uses its own translation settings and runs without MTP.

Download size is separate from RAM and VRAM usage. GB means one billion bytes; Gemma totals include MTP files. The execution engine downloads separately. The launcher shows model memory guidance and measures the active server's RAM and VRAM. Larger models and longer contexts need more memory.

## Extend your library

- **Local GGUF:** choose **Models → Add model → Connect GGUF**. Files stay in their original location. Registered models are recognized by their contents, even after renaming.
- **Hugging Face:** enter a public repository address, choose a GGUF, and add it. Starting the model downloads and verifies that exact file.
- **My models:** reuse registered models, open their folders, or remove a custom entry without deleting its file.
- **Advanced load settings:** choose GPU offload, KV cache, Flash Attention, reasoning and a custom chat template. Settings are saved per model. Reset restores defaults and disconnects a custom MTP override.
- **Optional MTP:** connect the main model, then attach a compatible Gemma 4 assistant. Vocabulary and target dimensions must match; unrelated draft models cannot be used as MTP.

Custom imports support single-file text-generation GGUF architectures available in the bundled llama.cpp version. Split models, image/audio projectors and arbitrary execution engines are outside this import flow. Missing chat templates can be supplied in advanced settings, up to 14,000 characters. Private or gated Hugging Face files can be downloaded with your account and linked locally.

Advanced model-list JSON import remains available; see [catalog format](docs/model-catalog.md). Adding a model does not require another executable.

## Prompts, glossary and API

Create a named preset under **Prompts & glossary**. Enter instructions and terminology directly, or import a two-column CSV/TSV (`source,target`). Select the preset for a model in **Run**; a preset can be reused across models.

Saved instructions and terminology apply to both Web UI and API requests. Client messages, output-format constraints and tool definitions are preserved. Generation defaults only fill values the client did not specify. Saving or selecting a preset affects the next request; running responses keep their original settings. **Model defaults** disables added instructions and glossary.

- API base URL: `http://127.0.0.1:8080/v1`
- Web UI: `http://127.0.0.1:8080/`
- Model ID: copy the model name shown beside the API address.

The service is available on this PC only. Streaming and cancellation are supported. Text presets require text prompts or user messages; raw token-only requests can use **Model defaults**. If port 8080 is occupied, stop the other server first.

## Window and language

**X hides the window in the tray.** The server keeps running. The tray can open the launcher, start/stop the server, open Web UI or quit completely. Opening the EXE again restores the existing window. One model runs at a time; stop it before switching models or advanced options. Context changes can be applied with restart.

Windows display language is selected automatically, with English fallback. The top-right selector overrides it and remembers your choice. All 17 CatBridge languages are included: English, Korean, Japanese, simplified/traditional Chinese, Spanish, Portuguese, French, German, Filipino, Vietnamese, Russian, Polish, Indonesian, Malay, Turkish and Thai.

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

To run the UI harness without showing test windows on your desktop, use `pwsh -File scripts/run-isolated-ui-tests.ps1 -Executable tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe`. It creates a private Windows desktop and temporary model settings. It does not start the production launcher, acquire its instance mutex or run real inference.

Use `--data-dir "D:\GemmaLauncherData" --model-folder "D:\Models"` for custom data locations, or `--licenses` for embedded notices. Source uses the [MIT license](LICENSE): free use, modification and redistribution, including commercial use, with copyright and license attribution retained. Models and dependencies have their own [terms and notices](THIRD-PARTY-NOTICES.md).

---

# Gemma 모델 런처 by Drugged Cat

AI 모델의 설치부터 실행·관리까지, 클릭 한 번으로 시작하세요. 번역·글쓰기·대화에 맞는 모델을 고르면 필요한 파일을 준비하고 서버를 켭니다. **브라우저 대화 화면과 OpenAI 호환 API**를 함께 제공하므로 CatBridge 같은 앱에도 바로 연결할 수 있습니다.

Windows EXE 하나로 시작하고, 원하는 모델을 추가하고, 저장한 프롬프트와 용어사전을 연결 앱에서도 함께 사용하세요.

## 시작하기

1. [Releases](https://github.com/motionsilse/GemmaLauncher/releases)에서 Windows x64 EXE를 내려받습니다.
2. **.NET 10 Desktop Runtime(x64)**과 **ASP.NET Core Runtime 10(x64)**을 설치한 뒤 EXE를 실행합니다. .NET 10 SDK에도 둘 다 포함됩니다. .NET 8만 설치되어 있으면 실행할 수 없습니다. 압축 해제나 별도 폴더는 필요하지 않습니다.
3. **모델**에서 용도와 크기를 비교하고 **이 모델 선택**을 누릅니다. **실행** 화면에서 설정을 확인한 뒤 **다운로드하고 시작**, 파일이 준비되어 있다면 **서버 켜기**를 누르세요.
4. **Web UI 열기**로 대화하거나, **실행** 화면의 API 주소와 모델 이름을 다른 앱에 입력하세요.

첫 다운로드에는 인터넷과 저장 공간이 필요합니다. 그래픽카드의 Vulkan 드라이버를 최신 상태로 유지하세요. Microsoft Visual C++ x64 실행 구성 요소가 없으면 공식 설치 프로그램을 안내합니다. 해당 설치에는 관리자 승인이 필요할 수 있습니다.

모델을 선택하거나 추가하면 **내 모델**에 등록하고 **실행** 화면으로 이동합니다. 두 화면은 같은 등록 목록을 사용하며, 아직 내려받지 않은 모델도 설정할 수 있습니다. 파일 준비 상태를 함께 표시합니다. 다른 모델을 둘러봐도 실행 대상은 유지되고, 다운로드 중인 모델의 취소 버튼과 실행 중인 서버의 종료 버튼은 다른 탭에서도 사용할 수 있습니다.

## 용도에 맞게 고르세요

| 모델 | 이런 용도에 어울립니다 | 모델 다운로드 |
|---|---|---:|
| Translate Gemma 4 · E2B | 게임 대사와 자막을 가볍고 빠르게 번역할 때 | 3.26 GB |
| Translate Gemma 4 · E4B | 문맥과 표현의 뉘앙스를 살리는 번역이 필요할 때 | 5.20 GB |
| HY-MT2 · 1.8B Q4_K_M | 메모리 부담을 줄이면서 다국어 번역을 빠르게 사용할 때 | 1.13 GB |
| HY-MT2 · 7B Q4_K_M | 복잡한 문장과 문맥의 정확성·표현력을 중시할 때 | 4.62 GB |
| Gemma 4 · 12B Heretic StyleTune | 번역, 글쓰기, 롤플레잉과 사람 같은 자연스러운 대화 | 8.30 GB |

E2B와 E4B에는 대사·자막 번역 튜닝과 SSD를 활용한 메모리 최적화를 적용했습니다. 12B에는 무검열 튜닝으로 자유로운 표현을, 스타일 튜닝으로 말투의 개성을 더했습니다. Gemma 세 구성에는 각각 맞는 MTP 가속용 파일이 포함됩니다. HY-MT2는 별도의 번역 설정을 사용하며 MTP 없이 실행합니다.

다운로드 크기는 RAM·VRAM 사용량과 다릅니다. GB는 10억 바이트 기준이며 Gemma 용량에는 MTP도 포함됩니다. 실행 엔진은 별도로 준비합니다. 런처에서 모델별 메모리 안내와 실행 중인 서버의 실제 RAM·VRAM 사용량을 확인할 수 있습니다. 큰 모델과 긴 글 설정은 메모리를 더 사용합니다.

## 원하는 모델을 추가하세요

- **이미 받은 GGUF:** **모델 → 모델 추가 → GGUF 파일 연결**에서 선택합니다. 원본을 이동하거나 복사하지 않습니다. 등록된 모델은 이름을 바꿔도 파일 내용으로 알아봅니다.
- **Hugging Face:** 공개 저장소 주소를 입력하고 GGUF를 골라 추가합니다. 서버를 켤 때 선택한 파일을 다운로드하고 검증합니다.
- **내 모델:** 등록한 모델을 다시 사용하거나 폴더를 엽니다. 사용자 모델 항목을 삭제해도 원본 파일은 남습니다.
- **고급 실행 설정:** GPU 사용량, KV 캐시, Flash Attention, 추론 모드와 대화 템플릿을 모델별로 저장합니다. 초기화하면 기본값으로 돌아가며 직접 연결한 MTP도 해제됩니다.
- **MTP 연결:** 본체를 연결한 뒤 호환되는 Gemma 4 가속용 파일을 추가할 수 있습니다. 어휘와 대상 크기를 확인하므로 관계없는 모델을 MTP로 연결할 수는 없습니다.

직접 추가하는 모델은 포함된 llama.cpp 버전에서 지원하는 단일 파일 텍스트 생성 GGUF를 대상으로 합니다. 분할 파일, 이미지·음성 보조 파일과 별도 실행 엔진은 이 기능으로 추가하지 않습니다. 대화 템플릿이 없는 모델은 고급 설정에서 최대 14,000자의 템플릿을 입력할 수 있습니다. 비공개·접근 승인 저장소는 본인 계정으로 먼저 내려받은 뒤 로컬 파일을 연결하세요.

고급 기능의 모델 목록 JSON 가져오기도 유지합니다. [모델 카탈로그 작성법](docs/model-catalog.md)을 참고하세요. 모델마다 새 런처를 만들 필요가 없습니다.

## 프롬프트·용어사전과 API

**프롬프트·사전**에서 이름을 정하고 지시문과 용어를 저장하세요. 사전은 직접 입력하거나 원문·번역의 두 열로 된 CSV/TSV(`source,target`)를 가져올 수 있습니다. **실행** 화면에서 사용할 프리셋을 고르며, 하나의 프리셋을 여러 모델에 재사용할 수 있습니다.

저장한 지시문과 사전은 Web UI와 연결 앱에 함께 적용합니다. 앱이 보낸 대화, 출력 형식과 도구 정의는 유지합니다. 생성 설정은 앱이 값을 지정하지 않았을 때 기본값으로 사용합니다. 저장하거나 바꾸면 다음 요청부터 반영하며 진행 중인 응답에는 기존 설정을 유지합니다. **모델 기본값**은 추가 지시문과 사전을 적용하지 않습니다.

- API 주소: `http://127.0.0.1:8080/v1`
- Web UI: `http://127.0.0.1:8080/`
- 모델 이름: 런처의 API 주소 옆에 표시된 값을 복사합니다.

이 PC 안에서만 접속할 수 있으며 스트리밍과 취소를 지원합니다. 텍스트 프리셋은 글 또는 사용자 메시지가 필요합니다. 숫자 토큰만 보내는 요청에는 **모델 기본값**을 사용하세요. 다른 프로그램이 8080 포트를 사용 중이면 해당 서버를 먼저 종료하세요.

## 창과 표시 언어

**X는 트레이로 숨기기입니다.** 서버는 계속 실행됩니다. 트레이에서 런처 열기, 서버 켜기·끄기, Web UI와 완전 종료를 사용할 수 있습니다. EXE를 다시 실행하면 기존 창이 열립니다. 모델은 한 번에 하나만 실행하며 모델이나 고급 설정을 바꾸려면 먼저 서버를 끄세요. 글 길이 변경은 재시작으로 적용합니다.

Windows 표시 언어를 자동으로 사용하고 지원하지 않는 언어는 영어로 표시합니다. 우상단에서 직접 선택한 언어도 기억합니다. CatBridge와 같은 17개 언어를 지원합니다: 영어, 한국어, 일본어, 중국어 간체·번체, 스페인어, 포르투갈어, 프랑스어, 독일어, 필리핀어, 베트남어, 러시아어, 폴란드어, 인도네시아어, 말레이어, 튀르키예어, 태국어.

## 소스와 이용 조건

Windows, .NET 10 SDK와 Python 3에서 위의 빌드 명령을 실행합니다. 배포 스크립트는 모델 목록과 17개 언어를 검사하고 `dist`에 단일 EXE와 SHA-256 파일을 만듭니다. 시스템 .NET 10을 사용하며 런타임은 번들하지 않습니다. 런처 라이선스 고지를 포함하고 다른 파일이 없는 폴더에서 패키지와 HTTP 중계를 검증합니다. 화면은 WPF 의존성이 없는 WinForms를 사용하고, 서버 메모리 조회는 실행 화면이 보일 때만 동작합니다. 모델 등록 정보는 `config/launcher-models.json`에 있습니다.

`--data-dir "D:\GemmaLauncherData" --model-folder "D:\Models"`로 저장 위치를 지정하고 `--licenses`로 내장 고지를 엽니다. 런처 소스는 [MIT 라이선스](LICENSE)로 상업적 이용을 포함해 자유롭게 사용·수정·재배포할 수 있습니다. 원래 저작권 표시와 라이선스 문구는 유지해야 합니다. 모델과 구성 요소에는 각각의 [별도 이용 조건](THIRD-PARTY-NOTICES.md)이 적용됩니다.
