# 모델 카탈로그 작성법

런처는 JSON 카탈로그의 모델 설명, 파일 정보와 실행 프로필을 읽습니다. 코드 변경 없이 호환되는 모델을 추가할 수 있으며, 각 모델은 자신의 본체·MTP 파일·글 길이·응답 설정을 따로 갖습니다.

## 불러오기

1. 소스의 `src/GemmaLauncher.App/Assets/catalog.json`을 참고해 추가 모델만 들어 있는 JSON을 만듭니다. 배포 EXE에는 카탈로그가 내장되어 있습니다.
2. 런처에서 서버를 끕니다.
3. **모델 → 모델 추가 → 고급 → 모델 목록 불러오기 (JSON)**에서 선택합니다.
4. 목록에 추가된 모델을 선택하고 **다운로드하고 시작**을 누릅니다.

추가 목록은 실행 데이터 폴더의 `catalog.user.json`에 저장되어 다음 실행에도 유지됩니다. 기본 목록에 있는 모델이나 이미 추가한 모델과 같은 ID는 다시 등록할 수 없습니다.

## 최상위 구조

```json
{
  "schemaVersion": 1,
  "runtime": {
    "version": "b10795",
    "url": "https://github.com/ggml-org/llama.cpp/releases/download/b10795/llama-b10795-bin-win-vulkan-x64.zip",
    "bytes": 35208196,
    "sha256": "d6f81f2cbbaf457aa4392080a6b8d7675e0cb4ef0163ee26076439dfc956aab4",
    "executableRelativePath": "llama-server.exe"
  },
  "models": []
}
```

`models`에는 아래 모델 객체를 하나 이상 넣습니다. 카탈로그는 최대 2 MB, 모델 수는 1~100개입니다. 추가 카탈로그의 `runtime.version`과 `runtime.sha256`은 배포된 기본 카탈로그와 같아야 합니다. `runtime` 전체를 기본 카탈로그에서 복사하세요. 추가 카탈로그로 실행 엔진을 교체하지 않습니다.

## 모델 객체

아래 값은 형식 설명을 위한 예입니다. URL·파일 크기·SHA-256을 실제 배포 파일의 값으로 바꿔야 합니다.

```json
{
  "id": "my-gemma-text-q4",
  "displayName": "나의 Gemma 텍스트 모델",
  "apiModelId": "My-Gemma-Text",
  "category": "번역·창작",
  "headline": "내 작업에 맞춘 AI",
  "description": "이 모델을 어떤 작업에 쓰면 좋은지 설명합니다.",
  "hardwareGuide": "PC 사양과 용도에 맞는 선택 기준을 적습니다.",
  "memoryGuide": "모델 규모와 메모리 사용 특성을 설명합니다.",
  "benefits": ["모델에 적용한 튜닝의 장점"],
  "sourceUrl": "https://huggingface.co/owner/model-repo",
  "licenseUrl": "https://huggingface.co/owner/model-repo/blob/main/LICENSE",
  "profile": {
    "defaultContext": 8192,
    "contextSizes": [4096, 8192, 16384],
    "lazy": "auto",
    "mtpDraftMax": 0,
    "temperature": 0.7,
    "topP": 0.95,
    "topK": 64,
    "minP": 0.05
  },
  "artifacts": [
    {
      "role": "model",
      "filename": "my-model-Q4.gguf",
      "url": "https://huggingface.co/owner/model-repo/resolve/COMMIT/my-model-Q4.gguf",
      "bytes": 123456789,
      "sha256": "REPLACE_WITH_THE_ACTUAL_64_CHARACTER_SHA256"
    }
  ]
}
```

| 필드 | 규칙 |
|---|---|
| `id` | 영문·숫자로 시작하는 1~100자. 영문·숫자·점·밑줄·하이픈 사용. 다른 모델과 겹치지 않는 ID를 지정합니다. |
| `displayName` | 모델 선택 목록과 비교 화면에 표시할 이름 |
| `credit` | 선택 사항. 모델 이름 아래에 표시할 제작자·튜닝 제공자 문구. 언어를 바꿔도 원문을 유지합니다. |
| `apiModelId` | API에서 사용하는 모델 이름. 비어 있거나 제어문자·쉼표를 포함하면 안 됩니다. |
| `category`, `headline`, `description` | 용도와 선택 이유를 설명하는 제품 문구 |
| `hardwareGuide`, `memoryGuide` | PC 규모·RAM·VRAM 선택 안내. 특정 PC의 실행 기록 대신 일반 사용자가 판단할 수 있는 내용을 적습니다. |
| `benefits` | 모델 튜닝이나 구성의 장점을 담은 문자열 배열 |
| `sourceUrl`, `licenseUrl` | 모델 출처와 이용 조건의 HTTPS 주소 |

## 본체와 MTP 파일

`artifacts`에는 `role: "model"`인 본체를 정확히 하나 넣습니다. `role: "mtp"`인 가속용 파일은 최대 하나이며, 다른 역할은 지원하지 않습니다.

MTP를 사용할 때는 해당 본체와 호환되는 MTP 파일을 추가하고 `profile.mtpDraftMax`를 1~16으로 지정하세요. MTP를 사용하지 않는 모델은 MTP 파일을 넣지 않고 `mtpDraftMax`를 0으로 지정합니다. E2B용 MTP를 E4B나 12B에 연결하면 안 됩니다.

모든 파일에 필요한 정보는 다음과 같습니다.

- `filename`: 경로를 제외한 파일 이름. 모델별 파일 이름이 중복되면 안 됩니다.
- `url`: 사용자 정보가 포함되지 않은 HTTPS 직접 다운로드 주소
- `bytes`: 정확한 파일 크기. 0보다 크고 1 TB 이하여야 합니다.
- `sha256`: 파일 내용의 SHA-256을 나타내는 64자리 16진수

Hugging Face 주소는 `resolve/main/` 대신 특정 커밋의 `resolve/<commit>/` 주소를 사용하면 파일 버전이 고정됩니다. 파일 크기와 해시는 다음 명령으로 확인할 수 있습니다.

```powershell
(Get-Item -LiteralPath "D:\Models\my-model-Q4.gguf").Length
(Get-FileHash -LiteralPath "D:\Models\my-model-Q4.gguf" -Algorithm SHA256).Hash.ToLowerInvariant()
```

여러 조각으로 나뉜 GGUF와 이미지·음성용 추가 파일은 현재 카탈로그 형식에서 지원하지 않습니다. 파일 URL에 토큰을 넣지 마세요. 로그인이나 이용 승인 절차가 필요한 파일을 다운로드하는 기능은 제공하지 않습니다.

## 실행 프로필

| 필드 | 허용 값 |
|---|---|
| `defaultContext` | `contextSizes`에 포함된 기본 글 길이 |
| `contextSizes` | 512~262144 범위의 정수, 1~16개 |
| `lazy` | `on`, `off`, `auto` |
| `mtpDraftMax` | MTP 없음: `0`, 호환되는 MTP 파일 있음: `1`~`16` |
| `temperature` | `0`~`2` |
| `topP` | `0`보다 크고 `1` 이하 |
| `topK` | `1`~`1000` |
| `minP` | `0`~`1` |
| `repeatPenalty` | `0.5`~`2`, 생략하면 `1` |

공통 실행 방식은 Vulkan, mmap과 단일 병렬 슬롯입니다. 카탈로그 모델은 기본적으로 GPU 자동 배치, Q8 KV 캐시와 Flash Attention을 사용하며 reasoning은 끕니다. 사용자가 고급 설정에서 모델별 GPU·캐시·추론·MTP·대화 템플릿을 변경할 수 있습니다. 임의의 명령줄 인수나 별도 프로그램 실행은 카탈로그에 넣을 수 없습니다. 모델 구조와 템플릿은 함께 제공되는 llama.cpp와 맞아야 합니다.

기본 추천 목록은 `config/launcher-models.json`에서 관리합니다. `familyId`, `familyName`, `familyOrder`로 묶음을 정하고 `variant`로 E2B/E4B처럼 선택지를 표시합니다. `localizationPrefix`는 모델 번역 문구의 접두사이며, `order`는 목록의 순서입니다. 변경한 뒤 `python scripts/create-product-catalog.py`로 내장 카탈로그를 갱신합니다.

로컬 GGUF·Hugging Face 가져오기는 이 JSON을 작성할 필요가 없습니다. 모델 등록, 고급 실행 설정과 재사용할 프롬프트·용어사전은 `settings.json`에 따로 저장합니다.

## 이미 받은 모델 사용

**기존 모델 찾기**에서 지정한 폴더의 바로 아래와 `<폴더>/<모델 ID>/`를 검색합니다. 파일 이름·크기·SHA-256이 맞으면 기존 파일을 직접 사용하며, 파일을 이동하거나 수정하지 않습니다. WinGet의 llama.cpp 모델 폴더도 자동으로 검색합니다.

런처가 직접 받는 파일은 다음 위치에 저장합니다.

```text
%LOCALAPPDATA%\GemmaLauncher\
  models\<모델 ID>\<SHA-256 앞 12자리>\<파일 이름>
  runtime\
  logs\
  settings.json
  catalog.user.json
```

`--data-dir`로 지정한 실행 데이터 폴더를 사용하는 경우 이 구조는 그 폴더 아래에 생성됩니다. 연결한 기존 폴더는 검색 대상으로 사용되며 새 다운로드 위치를 바꾸지는 않습니다.
