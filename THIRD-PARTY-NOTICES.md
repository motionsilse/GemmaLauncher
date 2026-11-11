# 타사 소프트웨어 및 모델 안내

Gemma 모델 런처는 아래 프로젝트를 사용합니다. 각 구성 요소의 권리와 이용 조건은 해당 저작권자와 원본 라이선스에 따릅니다.

## 시스템 .NET 구성 요소

Windows용 단일 EXE는 PC에 설치된 .NET 10 Desktop Runtime과 ASP.NET Core Runtime(x64)을 사용합니다. .NET 런타임은 EXE에 번들하지 않습니다. 런처의 MIT 라이선스와 이 안내를 EXE에 보관하며 `--licenses` 인수로 열 수 있습니다. 시스템 런타임의 라이선스와 타사 고지는 Microsoft 설치 배포본에서 제공합니다.

- [.NET Runtime](https://github.com/dotnet/runtime) — [라이선스](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [타사 고지](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [Windows Forms](https://github.com/dotnet/winforms) — [라이선스](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT)
- [ASP.NET Core](https://github.com/dotnet/aspnetcore) — [라이선스](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt)

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
| HY-MT2 1.8B Q4_K_M | [unsloth/Hy-MT2-1.8B-GGUF](https://huggingface.co/unsloth/Hy-MT2-1.8B-GGUF) |
| HY-MT2 7B Q4_K_M | [unsloth/Hy-MT2-7B-GGUF](https://huggingface.co/unsloth/Hy-MT2-7B-GGUF) |
| ANIS Trans (일본어 전용) | [ANIS · jintigers/anis-trans](https://huggingface.co/jintigers/anis-trans) |

기본 모델 카탈로그의 `sourceUrl`과 `licenseUrl`에도 각 출처가 기록되어 있습니다. 추가 모델은 해당 모델의 카탈로그와 원본 저장소의 조건을 확인하세요.

## ANIS Trans 선택 시 준비하는 프로그램

ANIS Trans의 모델과 함께 배포되는 실행 프로그램은 ANIS의 저작물이며 런처의 MIT 라이선스와 별개입니다. 런처는 [공식 프로그램](https://github.com/jintigers/anis-trans/tree/1165f0cda25a9afdd0c8e0491038e949d9446e6e)을 수정하지 않고 다운로드해 실행하며 원본 고지를 유지합니다. [ANIS Trans 라이선스 한국어 정본](https://github.com/jintigers/anis-trans/blob/1165f0cda25a9afdd0c8e0491038e949d9446e6e/LICENSE-ko.md)을 확인하세요. 비영리 이용과 영리 이용의 조건이 다르며 영리 이용은 ANIS와 별도 계약이 필요합니다. 가중치와 공식 프로그램은 이 런처 EXE에 포함하거나 미러링하지 않습니다. 최초 다운로드에 필요한 공식 접근 신청과 이용 조건 확인은 사용자가 진행합니다.

전용 실행 환경에 필요한 아래 구성 요소도 공식 배포 경로에서 받으며 EXE에 포함하지 않습니다. 시스템 Python과 분리된 폴더에 설치하며 원본 라이선스 파일을 유지합니다.

- [uv](https://github.com/astral-sh/uv) — [라이선스](https://github.com/astral-sh/uv#license)
- [Python](https://www.python.org/) — [라이선스](https://docs.python.org/3/license.html)
- [CTranslate2](https://github.com/OpenNMT/CTranslate2) — [라이선스](https://github.com/OpenNMT/CTranslate2/blob/master/LICENSE)
- [SentencePiece](https://github.com/google/sentencepiece) — [라이선스](https://github.com/google/sentencepiece/blob/master/LICENSE)
- [NumPy](https://numpy.org/) — [라이선스](https://numpy.org/doc/stable/license.html)
- [PyYAML](https://pyyaml.org/) — [라이선스](https://github.com/yaml/pyyaml/blob/main/LICENSE)

Gemma는 모델 이름을 식별하기 위해 사용합니다. 이 런처는 Google의 공식 제품이 아니며, 원본 모델·튜닝·실행 엔진의 제작자와 독립된 프로젝트입니다.

## ChatGPT 연결의 출처

계정 연결의 제품용 로그인·프록시 호스트·사용량·API 처리는 [motionsilse/CatRoute](https://github.com/motionsilse/CatRoute)의 `c5c74ad`에서 선별 이관했습니다. 연구 폴더·비공개 바이너리·압축 엔진 payload는 포함하지 않았습니다. 저장 경로와 API 키 형식을 보존하고 지연 실행·취소·다운로드 및 네이티브 화면을 통합했습니다.

프록시는 [router-for-me/CLIProxyAPI v8.0.66](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.66)의 수정하지 않은 공식 Windows amd64 프로그램입니다. 첫 연결 준비 때 다운로드하며 Cat Loader EXE에는 포함하지 않습니다. 원본 [MIT 라이선스](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.66/LICENSE)와 다운로드 ZIP의 고지를 보존합니다.

```text
MIT License

Copyright (c) 2025-2005.9 Luis Pater
Copyright (c) 2025.9-present Router-For.ME
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
