# ChatGPT UX/UI 4개 지적 수정·테스트·재리뷰

2026-11-11 · Cat Loader 0.4.4 · 본 에이전트가 순차 자체 검토했다. 독립 리뷰어를 실행하거나 사용자 UX 승인을 대신한 결과가 아니다.

## 수정 결과

| 지적 | 변경 | 검증 |
|---|---|---|
| 인증 오류의 원인·복구 안내가 사라짐 | HTTP 401은 인증 재확인, 403은 계정 이용 권한 확인으로 구분한다. 계정 영역에 로그아웃 후 다시 로그인하는 경로를 표시한다. 저장 로그인을 자동 삭제하지 않는다. 로컬 모델 목록 조회 성공만으로 실제 계정 인증 문제가 해결됐다고 표시하지 않는다. | 실제 `HttpClient` 응답 → `AccountApi` → 예외 → VM 경로의 401/403, 시작 단계 실패, 모델 조회 후 안내 유지, upstream 조회 성공 후 안내 해제 통과 |
| 사용량과 진단 결과 혼재 | 사용량 오류와 진단 결과를 별도로 보관한다. 모델 확인·실제 요청 테스트 각각의 진행/성공/취소/시간 초과를 진단 영역에 표시한다. 서버가 꺼져 있으면 진단을 사용할 조건을 안내한다. | 사용량 보존, 진단 성공, 두 영역의 오류 독립성, 진행·취소·시간 초과, 오프라인 안내 통과 |
| 예기치 않은 서버 종료가 정상 중지처럼 보임 | backend에서 비정상 종료 원인을 보존해 화면·공통 하단에 전달한다. 로그인은 유지하고 서버 다시 시작을 안내한다. 정상 중지 과정은 먼저 준비 상태를 해제해 감지 타이머와 경합해도 비정상 종료로 표시되지 않도록 했다. | Core 실제 타이머, VM 종료 알림·재시작·정상 중지, 공식 엔진 프로세스 종료 감지 통과 |
| 동적 버튼의 접근성 이름 불일치 | 로그인/취소, 키 표시/숨기기는 문구와 접근성 이름을 함께 갱신한다. 정적 번역과 동적 문구가 중복 바인딩되지 않게 했다. | 네이티브 `AccessibilityObject.Name`, 상태 변화, 무관한 갱신 중 기본 문구로 되돌아가지 않음, 복사 대상의 접근성 이름 보존 통과 |

새 테스트 4개가 기존 코드에서 각각 실패하는 것을 먼저 확인했다. Core의 종료 원인 보존 검사도 수정 전에 실패했다. 수정 후 모두 통과했다.

## 재리뷰 중 발견해 함께 수정한 문제

1. 최소 창의 스페인어 화면에서 하단의 긴 문구가 줄바꿈되며 서버 끄기 버튼이 잘렸다. 공통 하단 높이를 실제 가용 너비로 계산하도록 고쳤다. 인증 복구 상태에서 17개 언어의 로그아웃·하단 중지 버튼이 클리핑 영역 안에 있는지 재검사했다.
2. 첫 접근성 수정은 최종 이름은 맞았지만, 무관한 화면 갱신 때 정적 로그인 문구를 잠깐 거쳤다. 이 현상을 잡는 실패 테스트를 추가하고 중복 바인딩을 제거했다. 최종 회귀·로그인 사용자 흐름 검사를 다시 통과했다.

## 검사 결과

- Release 빌드: 경고 0, 오류 0.
- Core 전체 검사 통과. 계정 상태·키·소유권·네트워크·타이머 알림과 기존 모델 기능 포함.
- UI 전체 회귀 검사 통과. 마지막 동적 바인딩 정리 후에는 4개 회귀와 로그인·서버 시작·중지·로그아웃 사용자 흐름을 다시 실행해 통과했다.
- 17개 언어, 언어당 486개 키와 자리표시자 검사 통과. 카탈로그 검사와 Python 테스트 5개 통과.
- 네이티브 창의 인증 오류·진단 성공·비정상 종료·최소 창 캡처 및 중요 버튼 가시 영역 검사 통과.
- 빈 임시 저장소의 버튼 → 공식 엔진 다운로드 → 인증 URL 인계 → 취소 정리 통과. 사용자 API 포트가 열리지 않고, 정상 취소를 비정상 종료로 표시하지 않는 것도 확인했다.
- 공식 엔진 smoke에서 다운로드/재사용·API 키·관리 권한 분리·엔진 종료 알림·정리를 확인했다. 종료 감지 검사는 OAuth 없이 준비 상태만 테스트에서 배치한 뒤 실제 프로세스 수명과 production 감지 코드를 사용했다. 실계정 준비 완료를 입증하는 검사가 아니다.
- 배포된 0.4.4 EXE를 별도 저장소로 직접 실행해 ChatGPT 화면과 오프라인 진단창을 확인했다. 기존의 사용량 안내 대신 연결 진단의 사용 조건이 나왔다. 검토용 앱은 정상 종료했다.
- 단일 EXE 리소스 검사 통과: 모델 6개, 언어 17개, 출처·라이선스 포함. `git diff --check` 통과.

재현:

```powershell
dotnet build tests/GemmaLauncher.UiTests -c Release
dotnet run --project tests/GemmaLauncher.Tests -c Release
dotnet run --project tests/GemmaLauncher.Tests -c Release -- --account-runtime-smoke
./scripts/run-isolated-ui-tests.ps1 -Executable tests/GemmaLauncher.UiTests/bin/Release/net10.0-windows/GemmaLauncher.UiTests.exe
# 같은 실행기로 --account-regression-tests, --account-journey-tests,
# --render-account-regressions, --account-first-use-smoke 실행
python scripts/check-locales.py
python scripts/create-product-catalog.py --check
python -m unittest discover -s tests/scripts -v
./scripts/publish-launcher.ps1 -Version 0.4.4
```

## 화면 증거

예시 계정·오류 상태는 테스트 서비스로 재현한 실제 WinForms 컨트롤이다. 마지막 항목은 배포 EXE를 직접 열어 캡처했다.

- [작은 창의 인증 복구 안내](../design/2026-11-11-chatgpt-redesign/screens/fixes-0.4.4/auth-small.png)
- [모델 목록 확인 성공](../design/2026-11-11-chatgpt-redesign/screens/fixes-0.4.4/diagnostics.png)
- [비정상 종료와 재시작 안내](../design/2026-11-11-chatgpt-redesign/screens/fixes-0.4.4/unexpected-exit.png)
- [배포 EXE의 오프라인 진단](../design/2026-11-11-chatgpt-redesign/screens/fixes-0.4.4/live-diagnostics.jpg)

## 재리뷰 결론과 한계

수정한 네 항목과 추가로 발견한 하단 클리핑·중복 바인딩을 다시 검토했다. 상태 보존, 인증 실패 후 재로그인 안내, 정상/비정상 중지 구분, 취소·오래된 조회 결과, 사용자 흐름, 복사 대상 이름을 확인했으며 이 수정 범위에서 추가 P1/P2 문제를 발견하지 못했다.

실제 사용자 계정의 OAuth 완료·해당 계정 로그아웃/재로그인·실제 upstream 추론은 수행하지 않았다. 로그인·로그아웃 UI 흐름은 테스트 서비스와 실제 네이티브 컨트롤로 검사했고, 저장소 삭제 범위는 Core 검사로 확인했다. 물리 모니터의 DPI 전환도 이번 검증에 포함하지 않았다. 실제 계정 검증이나 사용자의 UX 판단이 완료됐다고 해석하지 않는다.

## 실행 파일

- `dist/CatLoader-0.4.4-win-x64.exe`: 1,432,177 bytes.
- SHA-256: `8e68eafa4b52c6fea86a00551244bdbd3aa6f8bcb7218de424d099d3f27daef0`.
- 이전 배포물은 보존했다. 커밋·푸시·공개 릴리즈는 수행하지 않았다.
