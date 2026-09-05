# 타사 소프트웨어 및 모델 안내

Gemma 모델 런처는 아래 프로젝트를 사용합니다. 각 구성 요소의 권리와 이용 조건은 해당 저작권자와 원본 라이선스에 따릅니다.

## 단일 EXE에 포함된 구성 요소

Windows용 단일 EXE에는 .NET 런타임과 Windows Desktop 구성 요소가 포함됩니다. 사용한 런타임 패키지의 라이선스와 타사 고지 원문을 EXE 안에 보관합니다. `GemmaLauncher-0.1.3-win-x64.exe --licenses`로 실행하면 내장된 고지를 텍스트 뷰어로 열 수 있습니다.

- [.NET Runtime](https://github.com/dotnet/runtime) — [라이선스](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [타사 고지](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [Windows Presentation Foundation](https://github.com/dotnet/wpf) — [라이선스](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)
- [Windows Forms](https://github.com/dotnet/winforms) — [라이선스](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT)

## 첫 준비 시 다운로드하는 실행 엔진

실행 엔진에 필요한 [Microsoft Visual C++ 재배포 가능 패키지](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)는 Microsoft가 제공하는 별도 구성 요소입니다. 런처는 필요한 경우 공식 설치 프로그램의 다운로드를 안내하며, 해당 설치 프로그램의 이용 조건이 적용됩니다.

[llama.cpp](https://github.com/ggml-org/llama.cpp)는 모델 실행과 Web UI·호환 API를 제공합니다. 런처는 기본 카탈로그에 지정된 버전의 Vulkan Windows x64 배포 파일을 받습니다.

- [llama.cpp 라이선스](https://github.com/ggml-org/llama.cpp/blob/master/LICENSE)
- [실행 엔진 배포 목록](https://github.com/ggml-org/llama.cpp/releases)

실행 엔진은 런처 EXE에 포함하지 않습니다. 다운로드한 실행 엔진과 함께 제공되는 고지 파일은 원래 배포 파일에 포함된 상태로 유지됩니다.

## 사용자가 선택해 다운로드하는 모델

모델 본체와 MTP 파일은 런처 EXE에 포함하지 않습니다. 모델별 원본 정보와 이용 조건은 다음 저장소에서 확인할 수 있습니다.

| 구성 | 출처 |
|---|---|
| E2B 번역 모델 · MTP | [17slever17/translate-gemma-4-sub-e2b-GGUF](https://huggingface.co/17slever17/translate-gemma-4-sub-e2b-GGUF) |
| E4B 번역 모델 · MTP | [17slever17/translate-gemma-4-sub-e4b-GGUF](https://huggingface.co/17slever17/translate-gemma-4-sub-e4b-GGUF) |
| 12B Heretic StyleTune 모델 | [motionsilse/Gemma-4-12B-QAT-Heretic-StyleTune-GGUF](https://huggingface.co/motionsilse/Gemma-4-12B-QAT-Heretic-StyleTune-GGUF) |
| 12B MTP | [unsloth/gemma-4-12B-it-qat-GGUF](https://huggingface.co/unsloth/gemma-4-12B-it-qat-GGUF) |

기본 모델 카탈로그의 `sourceUrl`과 `licenseUrl`에도 각 출처가 기록되어 있습니다. 추가 모델은 해당 모델의 카탈로그와 원본 저장소의 조건을 확인하세요.

Gemma는 모델 이름을 식별하기 위해 사용합니다. 이 런처는 Google의 공식 제품이 아니며, 원본 모델·튜닝·실행 엔진의 제작자와 독립된 프로젝트입니다.
