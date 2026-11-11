# ChatGPT 연결: GitHub 원본 비교와 실계정 검증

검증일: 2026-11-11. 범위: CatRoute에서 가져온 로그인·로그아웃, CLIProxyAPI 실행 및 ChatGPT 요청. 컴퓨터 유즈와 브라우저 자동화는 사용하지 않았다. 실제 인증은 일반 시스템 브라우저에서 사용자가 완료했다.

## 원본 조사

- [CatRoute c5c74ad](https://github.com/motionsilse/CatRoute/commit/c5c74add05e538f2d59f29beb236863c8f84c31a): 현재 CatRoute main과 앱에 표기한 이식 기준이 일치한다.
- [CLIProxyAPI v8.0.66](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.66): 조사 시점 최신 정식 릴리즈. 기존 고정 버전은 8.0.27이었다.
- [8.0.27 → 8.0.66 비교](https://github.com/router-for-me/CLIProxyAPI/compare/v8.0.27...v8.0.66): 토큰 갱신 불능 시 재로그인 복구, 불확실한 갱신 결과의 중복 요청 방지, SIWC 갱신 정보 보존 등이 변경됐다. [갱신 실패 복구 이슈 #5783](https://github.com/router-for-me/CLIProxyAPI/issues/5783), [불확실한 갱신 결과 수정](https://github.com/router-for-me/CLIProxyAPI/commit/68cd75724e).
- [로그인 핸들러](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.66/internal/api/handlers/management/auth_files_provider_oauth.go), [콜백 처리](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.66/internal/api/handlers/management/oauth_callback.go): 앱이 사용하는 codex 인증 URL, localhost:1455 콜백, management 콜백 전달·상태 확인·취소 계약을 대조했다. 실계정 실행으로 기존 v0 management 경로의 호환성도 확인했다.

## 수정한 문제

1. **오래된 인증 엔진:** CLIProxyAPI를 8.0.66으로 고정했다. GitHub 릴리즈 digest와 checksums.txt가 일치하는 ZIP을 확인했고, 별도로 EXE의 크기·SHA-256도 고정했다. 자동으로 임의의 최신 바이너리를 실행하는 방식은 사용하지 않는다.
2. **로그아웃 후 갱신 인증정보 잔류:** 새 엔진의 [갱신 이력](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.66/internal/auth/codex/refresh_journal.go)은 실제 인증정보를 포함하고, [별도 파일 저장소](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.66/internal/auth/codex/refresh_journal_file.go)에 저장된다. 계정 JSON만 삭제하면 이력이 남는다. 엔진 종료 후 `.codex-refresh`와 해당 계정의 `.json.lock`도 정리한다. 경로와 하위 항목의 reparse point를 검사하고, 정리가 실패하면 계정 JSON을 유지해 로그아웃을 재시도할 수 있게 했다. 다른 provider의 JSON과 로컬 API 키는 보존한다.
3. **로그인 포트 충돌 안내 부재:** 1455 포트를 다른 로그인 프로세스가 사용하면 일반 연결 실패로 처리됐다. 해당 오류를 구분하고 다른 앱의 로그인을 완료·취소한 뒤 재시도하도록 17개 언어로 안내한다. 브라우저를 열기 전에 실패하며, upstream의 대기 인증 세션도 취소한다.
4. **완료된 설치가 다시 다운로드로 보임:** verifying/ready까지 다운로드로 매핑하던 코드를 고쳤다. `다운로드 → 검증 → 압축 해제 → 검증 → 서버 준비` 순서로 전달하며, 중복 비동기 디스패치를 제거했다.

## 검증

실계정 테스트는 새 임시 저장소와 임시 API 포트를 사용했다. 기존 Cat Loader/CatRoute/Codex 계정 저장소는 변경하지 않았다. 테스트 로그와 보고서에는 토큰·이메일·로컬 API 키를 기록하지 않는다.

- 실제 GitHub 런타임 신규 다운로드·해시 검증·재사용.
- 로그인 대기 중 취소 → 보조 프로세스 종료·계정 미저장 → 재시도.
- 실제 브라우저 OAuth → 계정 저장. 로그인 직후 사용자 API는 꺼진 상태 유지.
- 서버 시작 → 모델 목록에 gpt-6-luna 확인 → 인증 없는 API 요청 401.
- 실제 잔여 사용량 조회와 `OK` 응답 생성.
- 서버 종료 → 계정 유지 → 로그인 없이 서버 재시작 → 다시 실제 응답 생성.
- 실행 중 로그아웃 → 엔진 종료 → 계정 파일·계정 잠금 파일 삭제 → 로그인 없는 서버 시작 거절.
- 위 실계정 시나리오는 2회 통과했다. 두 번째는 최종 로그아웃 정리 코드를 포함한다. [최종 결과 JSON](assets/0.1.0/chatgpt-live-report.json).
- 가짜 OAuth 서버와 실제 localhost 콜백으로 성공, 거부, 코드 교환 실패, 저장 실패, 취소, 포트 충돌·세션 정리를 검증했다.
- 갱신 receipt fixture의 잔류를 수정 전에 재현했다. 수정 후 삭제, 잠긴 receipt로 인한 실패 시 계정 보존·재시도, 타 provider 보존을 검증했다.
- Release 빌드, Core 전체 검사, 네이티브 UI 검사(별도 테스트 데스크톱), 17개 언어 검사를 실행했다. 사용자 데스크톱 입력이나 창 조작은 하지 않았다.

## 재리뷰와 한계

인증 계약, 상태 전이, 저장 데이터 정리, 취소, 오류 전달, 패키지 무결성을 순차 재리뷰했다. 수정 범위에서 남은 배포 차단 문제는 발견하지 못했다.

실제 계정의 토큰 만료·폐기까지 기다리거나 강제로 갱신시키지는 않았다. 따라서 장시간 뒤의 모든 upstream 갱신 실패를 실계정에서 재현했다고 주장하지 않는다. 해당 수정은 upstream 소스·회귀 테스트를 조사했고, 앱의 401/403 복구 안내는 자동 테스트로 검증했다. 새 갱신 receipt 삭제는 synthetic fixture 검증이며, 실계정 실행에서 갱신을 강제로 유발하지 않았다. 브라우저 인증과 서버 동작의 실검증은 생산 코드 경로를 호출하는 테스트 하네스로 수행했고, 사용자의 실제 창을 클릭하는 E2E는 수행하지 않았다.
