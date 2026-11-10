# Cat Loader 0.4.2 — 기본 글씨와 DPI 가독성

사용자 첨부 화면에서 모델 목록의 이름과 용량이 다른 내용보다 작게 보였다. 목록은 직접 그리는 글꼴을 10pt/9pt로 고정하고 행 높이만 DPI에 맞춰 변경했다. 나머지 화면이 확대되어도 이 두 글꼴은 부모의 변경을 따르지 않았다.

## 수정

- 본문·버튼·모델 목록 이름: 11.5pt. 모델 이름은 Bold.
- 보조 정보·용량·출처·하단 안내: 10.5pt.
- 공통 설정·확인 대화상자에도 같은 기본 글꼴 적용.
- 모델 목록은 부모 Font 변경, 핸들 생성, DPI 변경 후 글꼴과 행 높이를 다시 계산한다. 두 줄 이름과 보조 정보가 겹치지 않도록 실제 글꼴 높이를 사용한다.
- 부모가 이미 확대한 Font에 DPI를 다시 곱하지 않는다. WinForms의 [자동 배율 문서](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale)와 [Control 구현](https://source.dot.net/System.Windows.Forms/System/Windows/Forms/Control.cs.html)을 확인했다.
- 작은 창에서는 글씨를 줄이지 않고 영역을 스크롤하며, 주 동작은 하단에 유지한다. 기본 창에서도 내용 높이에 따라 스크롤이 필요할 수 있다.

## 검증

`TypographyTests.cs`는 실제 네이티브 ListBox를 렌더링하고 어두운 글자 픽셀의 높이를 측정한다. 부모 글꼴을 100% → 150% → 200% → 100%로 변경했다.

| 상태 | 100% 이름/보조 | 150% | 200% | 100% 복원 |
|---|---|---|---|---|
| 수정 전 | 10/11 px | 10/11 px | 10/11 px | 10/11 px |
| 수정 후 | 11/12 px | 16/19 px | 22/26 px | 11/12 px |

수정 전에 회귀 검사가 실패하고 수정 후 통과했다. 증거: `artifacts/cat-loader/typography-red.log`, `typography-green.log`.

- Release 빌드: 경고 0, 오류 0.
- 전체 네이티브 UI 테스트 통과: `artifacts/cat-loader/typography-full-ui.log`. 기존 5개 리뷰 수정 검사와 지역화·레이아웃 검사 포함.
- 실제 96 DPI 창에서 로컬 모델, ANIS, 최소 창 720×560, 프롬프트, ChatGPT 연결, 설정을 캡처하고 확인했다: `artifacts/cat-loader/typography/`.
- 17개 언어의 451개 키, 카탈로그 검사, 단일 EXE 검증 통과.
- 최종 배포 경로의 EXE 재검증 통과: `artifacts/cat-loader/typography-package.json`.

150%/200% 검사는 부모 글꼴 전환을 재현한 회귀 검사이며 실제 서로 다른 배율의 모니터 사이를 이동한 검사는 아니다. 실제 화면 캡처는 100% 배율이다. 사용자 계정과 실행 중인 앱 대신 별도 Windows 데스크톱의 임시 테스트 상태를 사용했다.

## 재검토 및 실행 파일

글꼴 소유권·해제, 부모 변경 전파, 중복 DPI 확대 방지, 행 높이와 두 줄 이름, 최소 창의 스크롤 및 고정 주 동작을 순차 재검토했다. 이번 수정 범위에서 추가로 확인된 결함은 없다.

`dist/CatLoader-0.4.2-win-x64.exe` — 1,370,225바이트. 모델 6개, 언어 17개. 기존 0.4.1 보존.

SHA-256: `c4facddc41f556f4d398abe8a2b2e736f7e098cfdfdebd0928013e43f881f355`.

로컬 실행 파일만 생성했다. 공개 배포나 커밋은 수행하지 않았다.
