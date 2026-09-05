# Gemma Model Launcher

**Choose a model and start local AI with one click.** Gemma Model Launcher handles the downloads, model-specific settings, and startup for you. From first installation to everyday use, manage your models and start or stop your AI in one simple Windows app.

Built for people who want to use AI without having to learn how to set it up. Translate, write, roleplay, or chat through the browser-based Web UI—or connect your favorite translation and chat apps to the **OpenAI-compatible API server running on your PC**.

## Download

**[Download the latest launcher](https://github.com/motionsilse/GemmaLauncher/releases/latest)**

1. Download `GemmaLauncher-0.1.4-win-x64.exe` from Releases.
2. Run the EXE directly. No ZIP extraction or companion folder is needed.
3. Select a model and start the server.
4. When ready, choose **Open Web UI** to chat in your browser, or **connect another app through the API** using the settings below.

Windows x64 and a Vulkan-capable graphics driver are required. The launcher includes .NET. Model inference also needs the Microsoft Visual C++ x64 runtime; the app offers Microsoft's official installer link if it is missing. Internet access is needed to prepare models initially. Model files and the inference engine are downloaded separately.

## Use with other apps through the API

The launcher can supply the AI model for translation tools, chat clients, and other apps that support a custom OpenAI-compatible API. In the other app's connection settings, enter:

| Setting | Value |
|---|---|
| API base URL | `http://127.0.0.1:8080/v1` |
| Model name / ID | Enter the **model name** shown in the launcher's app-connection section. |

Keep the launcher server running while using the connected app. You do not need to open the Web UI to use the API. The API is accessible from this PC only.

## Models

| Model | Purpose | Model download |
|---|---|---:|
| **Translate Gemma 4 Sub · E2B** | Lightweight translation for game dialogue and subtitles | 3.26 GB |
| **Translate Gemma 4 Sub · E4B** | Translation focused on context and nuance | 5.20 GB |
| **Gemma 4 · 12B Heretic StyleTune** | Translation, creative writing, roleplay, and natural conversation | 8.30 GB |

E2B and E4B use SSD-backed memory optimization. The 12B model combines unrestricted-expression tuning with a distinctive conversational style. All three include MTP acceleration. Larger models and longer contexts need more memory; the app shows memory guidance and live RAM/VRAM usage. Download sizes include the model and MTP files, in decimal GB.

## Already have a model file?

Choose **Model files and management → Connect GGUF file**, then select the main `.gguf` file you already downloaded for one of the listed models. The launcher checks its contents, selects the matching model, and remembers the original location without copying or downloading that file again. Renamed files are supported.

If you also have that model's MTP file, keep it in the same folder with its original filename. Otherwise, the launcher downloads the missing MTP file when you start the server. **Advanced → Import model list (JSON)** is for adding compatible catalog entries; it is separate from connecting a GGUF file.

## Features

- Verified, resumable downloads and reuse of matching local model files.
- Direct connection of existing GGUF files, with each model's own context and acceleration settings.
- Advanced import of compatible model catalog JSON files to add model entries.
- One launcher per Windows user session. Closing the window hides it to the tray; use **Exit** in the tray menu or **Ctrl+Q** to stop the server and quit.
- Local-only API: `http://127.0.0.1:8080/v1`; Web UI: `http://127.0.0.1:8080/`.
- 17 languages, automatic Windows language detection, English fallback, and a saved manual language selector.

Supported languages: English, Korean, Japanese, Simplified Chinese, Traditional Chinese, Spanish, Portuguese, French, German, Filipino, Vietnamese, Russian, Polish, Indonesian, Malay, Turkish, and Thai.

App data is stored in `%LOCALAPPDATA%\GemmaLauncher` by default.

## Build

Use Windows, the .NET 10 SDK, and Python 3. The model catalog is maintained directly in `src/GemmaLauncher.App/Assets/catalog.json`.

```powershell
dotnet build src/GemmaLauncher.App/GemmaLauncher.App.csproj -c Release
powershell -ExecutionPolicy Bypass -File scripts/publish-launcher.ps1
```

The publish script checks all translations and produces `dist/GemmaLauncher-0.1.4-win-x64.exe`, plus a local SHA-256 file. The model catalog, translations, .NET runtime, and license notices are included in the EXE. The script verifies the EXE in a directory without companion files before publishing it. Existing releases are never overwritten; use `-Version` to select a new version.

## License

The launcher source is available under the [MIT License](LICENSE). Downloaded models, the inference engine, and bundled runtimes retain their own licenses; see [Third-party notices](THIRD-PARTY-NOTICES.md).

You may use, modify, and redistribute the launcher freely, including commercially. Keep the original copyright and license notices when distributing it.

To read the notices included in the EXE, run `./GemmaLauncher-0.1.4-win-x64.exe --licenses`.

Gemma is used to identify the supported models. This is an independent project, not an official Google product.


<img width="1150" height="954" alt="image" src="https://github.com/user-attachments/assets/2f58dc1d-546a-4b4f-ba13-b7ffc9911f77" />
<img width="1094" height="852" alt="image" src="https://github.com/user-attachments/assets/35b6c523-9376-4042-ae30-78370f05a45e" />

---

# 한국어

## Gemma 모델 런처

**모델을 고르고 한 번만 누르면, 다운로드부터 설정과 실행까지 자동으로 진행됩니다.** Gemma 모델 런처는 처음 설치할 때부터 매일 사용할 때까지, 모델 관리와 AI 켜기·끄기를 한 화면에서 간편하게 처리하는 Windows 앱입니다.

로컬 AI를 처음 접하는 사람도 쉽게 시작할 수 있도록 만들었습니다. Web UI에서 번역·글쓰기·롤플레잉·대화를 바로 즐기거나, **내 PC에서 제공하는 OpenAI 호환 API**를 평소 쓰는 번역 프로그램이나 채팅 앱에 연결해 사용하세요.

## 다운로드

**[최신 런처 다운로드](https://github.com/motionsilse/GemmaLauncher/releases/latest)**

1. Releases에서 `GemmaLauncher-0.1.4-win-x64.exe`를 내려받습니다.
2. EXE를 바로 실행합니다. 압축 해제나 함께 보관할 별도 폴더가 필요하지 않습니다.
3. 사용할 모델을 선택하고 서버를 켭니다.
4. 준비가 끝나면 **Web UI 열기**로 브라우저에서 직접 대화하거나, 아래 설정으로 **다른 앱에 API를 연결**해 사용합니다.

Windows x64와 Vulkan을 지원하는 그래픽 드라이버가 필요합니다. 런처에는 .NET이 포함되어 있습니다. 모델 실행에 필요한 Microsoft Visual C++ x64 구성요소가 없으면 앱에서 Microsoft 공식 설치 링크를 안내합니다. 첫 준비에는 인터넷 연결이 필요하며, 모델과 실행 엔진은 별도로 다운로드합니다.

## API로 다른 앱에 연결하기

번역 도구나 채팅 앱에서 이 런처의 AI 모델을 사용할 수 있습니다. 연결하려는 앱이 직접 API 주소를 입력하는 OpenAI 호환 연결을 지원한다면, 해당 앱의 연결 설정에 다음 값을 넣으세요.

| 설정 항목 | 입력할 값 |
|---|---|
| API 주소 / Base URL | `http://127.0.0.1:8080/v1` |
| 모델 이름 / Model ID | 런처의 **앱 연결 → 모델 이름**에 표시된 값을 그대로 입력합니다. |

연결한 앱을 사용하는 동안 런처의 서버를 켜 두세요. API를 사용할 때는 Web UI를 열 필요가 없습니다. 이 API는 같은 PC 안에서만 접속할 수 있습니다.

## 모델

| 모델 | 용도 | 모델 다운로드 |
|---|---|---:|
| **Translate Gemma 4 Sub · E2B** | 게임 대사와 영상 자막을 위한 가볍고 빠른 번역 | 3.26 GB |
| **Translate Gemma 4 Sub · E4B** | 문맥과 뉘앙스를 살리는 번역 | 5.20 GB |
| **Gemma 4 · 12B Heretic StyleTune** | 번역·창작·롤플레잉·자연스러운 대화 | 8.30 GB |

E2B·E4B에는 SSD를 활용한 메모리 최적화를 적용했습니다. 12B는 무검열 튜닝의 자유로운 표현과 스타일 튜닝의 개성 있는 말투를 결합했습니다. 세 모델 모두 MTP 가속엔진을 사용합니다. 모델과 한 번에 읽을 글의 양이 커질수록 더 많은 메모리가 필요하며, 앱에서 예상 사용량과 실시간 RAM·VRAM 사용량을 확인할 수 있습니다. 다운로드 용량은 본체와 MTP 파일의 합계이며, GB는 10억 바이트 기준입니다.

## 이미 받은 모델 파일이 있다면

**모델 파일과 관리 → GGUF 파일 연결**을 누르고, 목록에 있는 모델의 본체 `.gguf` 파일을 선택하세요. 파일 내용을 확인해 해당 모델을 자동으로 선택하고 원래 위치를 기억합니다. 파일을 복사하거나 다시 다운로드하지 않으며, 파일 이름을 바꿨더라도 연결할 수 있습니다.

전용 MTP 파일도 이미 받았다면 원래 파일 이름을 유지한 채 같은 폴더에 두세요. 없다면 서버를 켤 때 빠진 MTP 파일을 다운로드합니다. **고급 → 모델 목록 불러오기 (JSON)**은 호환되는 모델 항목을 목록에 추가하는 별도 기능입니다.

## 기능

- 파일 무결성 검사, 다운로드 이어받기, 이미 받은 모델 파일 재사용.
- 이미 받은 GGUF 파일 직접 연결과 모델별 글 길이·가속 설정.
- 고급 기능에서 호환되는 모델 카탈로그 JSON을 불러와 모델 항목 추가.
- 같은 Windows 사용자 세션에서 런처 하나만 실행. 창의 **X는 트레이로 숨기기**이며, 완전히 종료하려면 트레이 메뉴의 **완전 종료** 또는 **Ctrl+Q**를 사용합니다.
- 이 PC 안에서만 접속하는 API: `http://127.0.0.1:8080/v1`, Web UI: `http://127.0.0.1:8080/`.
- Windows 표시 언어 자동 감지, 영어 폴백, 선택을 기억하는 수동 언어 선택기.

지원 언어: 한국어, 영어, 일본어, 중국어 간체·번체, 스페인어, 포르투갈어, 프랑스어, 독일어, 필리핀어, 베트남어, 러시아어, 폴란드어, 인도네시아어, 말레이어, 튀르키예어, 태국어.

실행 데이터는 기본적으로 `%LOCALAPPDATA%\GemmaLauncher`에 저장됩니다.

## 빌드

Windows, .NET 10 SDK, Python 3을 사용합니다. 모델 목록은 `src/GemmaLauncher.App/Assets/catalog.json`에서 직접 관리합니다.

```powershell
dotnet build src/GemmaLauncher.App/GemmaLauncher.App.csproj -c Release
powershell -ExecutionPolicy Bypass -File scripts/publish-launcher.ps1
```

배포 스크립트는 모든 언어 리소스를 검사하고 `dist/GemmaLauncher-0.1.4-win-x64.exe`와 로컬 SHA-256 파일을 만듭니다. 모델 목록, 번역 리소스, .NET 런타임과 라이선스 고지는 EXE 안에 포함됩니다. 다른 파일이 없는 폴더에서 EXE를 검증한 뒤 배포 파일로 내보냅니다. 기존 배포 파일은 덮어쓰지 않으며, 새 버전은 `-Version`으로 지정합니다.

## 라이선스

런처 소스에는 [MIT 라이선스](LICENSE)가 적용됩니다. 다운로드하는 모델·실행 엔진과 포함된 런타임은 각각의 라이선스를 따릅니다. [타사 구성요소 안내](THIRD-PARTY-NOTICES.md)를 참고하세요.

런처는 상업적 이용을 포함해 자유롭게 사용·수정·재배포할 수 있습니다. 배포할 때 원래 저작권 표시와 라이선스 문구를 유지해 주세요.

EXE에 포함된 고지를 읽으려면 `./GemmaLauncher-0.1.4-win-x64.exe --licenses`로 실행하세요.

Gemma는 지원 모델을 식별하기 위해 사용합니다. 이 런처는 Google의 공식 제품이 아닌 독립 프로젝트입니다.

<img width="1153" height="953" alt="image" src="https://github.com/user-attachments/assets/03c9eef6-117e-4b68-b73e-739554259648" />
<img width="1113" height="853" alt="image" src="https://github.com/user-attachments/assets/0ac99358-5762-4859-8787-f793d18228d7" />

