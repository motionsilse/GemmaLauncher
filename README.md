# Gemma Model Launcher by Drugged Cat

A portable Windows launcher for local translation, writing, roleplay, and conversation. Choose a model, let the launcher prepare its files, and open the built-in Web UI or connect an OpenAI-compatible app.

## Download

**[Download the latest launcher](https://github.com/motionsilse/GemmaLauncher/releases/latest)**

1. Download `GemmaLauncher-*-win-x64.zip` from Releases and extract it.
2. Run `GemmaLauncher.exe`, keeping the `Assets` folder beside it.
3. Select a model, start it, and choose **Open Web UI** when ready.

Windows x64 and a Vulkan-capable graphics driver are required. The launcher includes .NET. Model inference also needs the Microsoft Visual C++ x64 runtime; the app offers Microsoft's official installer link if it is missing. Internet access is needed to prepare models initially. Model files and the inference engine are downloaded separately.

## Models

| Model | Purpose | Model download |
|---|---|---:|
| **Translate Gemma 4 Sub · E2B** | Lightweight translation for game dialogue and subtitles | 3.26 GB |
| **Translate Gemma 4 Sub · E4B** | Translation focused on context and nuance | 5.20 GB |
| **Gemma 4 · 12B Heretic StyleTune** | Translation, creative writing, roleplay, and natural conversation | 8.30 GB |

E2B and E4B use SSD-backed memory optimization. The 12B model combines unrestricted-expression tuning with a distinctive conversational style. All three include MTP acceleration. Larger models and longer contexts need more memory; the app shows memory guidance and live RAM/VRAM usage. Download sizes include the model and MTP files, in decimal GB.

## Features

- Verified, resumable downloads and reuse of matching local model files.
- Per-model context and acceleration settings; import compatible model catalog JSON files to add models.
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

The publish script checks all translations and creates a self-contained ZIP and SHA-256 checksum in `dist/`. Existing release files are never overwritten. Use `-Version` to select a new version.

## License

The launcher source is available under the [MIT License](LICENSE). Downloaded models, the inference engine, and bundled runtimes retain their own licenses; see [Third-party notices](THIRD-PARTY-NOTICES.md).

Gemma is used to identify the supported models. This is an independent project, not an official Google product.

---

# 한국어

## Gemma 모델 런처 by Drugged Cat

번역과 창작용 AI를 내 PC에서 실행하는 Windows 런처입니다. 모델 선택부터 다운로드, 실행, Web UI 연결까지 한곳에서 관리합니다. Web UI로 바로 대화하거나 OpenAI 호환 API를 지원하는 다른 앱에 연결할 수 있습니다.

## 다운로드

**[최신 런처 다운로드](https://github.com/motionsilse/GemmaLauncher/releases/latest)**

1. Releases에서 `GemmaLauncher-*-win-x64.zip`을 내려받아 압축을 풉니다.
2. `GemmaLauncher.exe`를 실행합니다. `Assets` 폴더도 함께 보관하세요.
3. 모델을 선택해 준비를 마치면 **Web UI 열기**로 바로 사용할 수 있습니다.

Windows x64와 Vulkan을 지원하는 그래픽 드라이버가 필요합니다. 런처에는 .NET이 포함되어 있습니다. 모델 실행에 필요한 Microsoft Visual C++ x64 구성요소가 없으면 앱에서 Microsoft 공식 설치 링크를 안내합니다. 첫 준비에는 인터넷 연결이 필요하며, 모델과 실행 엔진은 별도로 다운로드합니다.

## 모델

| 모델 | 용도 | 모델 다운로드 |
|---|---|---:|
| **Translate Gemma 4 Sub · E2B** | 게임 대사와 영상 자막을 위한 가볍고 빠른 번역 | 3.26 GB |
| **Translate Gemma 4 Sub · E4B** | 문맥과 뉘앙스를 살리는 번역 | 5.20 GB |
| **Gemma 4 · 12B Heretic StyleTune** | 번역·창작·롤플레잉·자연스러운 대화 | 8.30 GB |

E2B·E4B에는 SSD를 활용한 메모리 최적화를 적용했습니다. 12B는 무검열 튜닝의 자유로운 표현과 스타일 튜닝의 개성 있는 말투를 결합했습니다. 세 모델 모두 MTP 가속엔진을 사용합니다. 모델과 한 번에 읽을 글의 양이 커질수록 더 많은 메모리가 필요하며, 앱에서 예상 사용량과 실시간 RAM·VRAM 사용량을 확인할 수 있습니다. 다운로드 용량은 본체와 MTP 파일의 합계이며, GB는 10억 바이트 기준입니다.

## 기능

- 파일 무결성 검사, 다운로드 이어받기, 이미 받은 모델 파일 재사용.
- 모델별 글 길이와 가속 설정. 호환되는 모델 카탈로그 JSON을 불러와 모델 추가.
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

배포 스크립트는 모든 언어 리소스를 검사하고 `dist/`에 독립 실행 ZIP과 SHA-256 체크섬을 만듭니다. 기존 배포 파일은 덮어쓰지 않으며, 새 버전은 `-Version`으로 지정합니다.

## 라이선스

런처 소스에는 [MIT 라이선스](LICENSE)가 적용됩니다. 다운로드하는 모델·실행 엔진과 포함된 런타임은 각각의 라이선스를 따릅니다. [타사 구성요소 안내](THIRD-PARTY-NOTICES.md)를 참고하세요.

Gemma는 지원 모델을 식별하기 위해 사용합니다. 이 런처는 Google의 공식 제품이 아닌 독립 프로젝트입니다.
