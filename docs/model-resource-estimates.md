# 로컬 모델 리소스 추정

2026-11-11 · Cat Loader 0.4.5

모델 선택 화면은 다운로드 전부터 저장공간과 기본 설정의 RAM·VRAM 참고치를 표시한다. 이 수치는 모델 프로세스의 대략적인 예산이며 Windows·다른 앱의 메모리를 포함한 최소 시스템 사양이 아니다. 전체 모델을 같은 장비에서 측정한 벤치마크도 아니다. 실행 중 조회하는 실제 서버 RAM·VRAM은 별도 항목으로 유지한다.

| 모델 | 모델 파일 합계 | RAM 참고 | VRAM 참고 | 근거 |
|---|---:|---:|---:|---|
| Translate Gemma 4 E2B | 3.26 GB | 약 2.1 GB | 약 1.9 GB | 기존 앱의 lazy/GPU 배치 참고값 유지 |
| Translate Gemma 4 E4B | 5.20 GB | 약 3.7 GB | 약 3.6 GB | 기존 앱의 lazy/GPU 배치 참고값 유지 |
| HY-MT2 1.8B | 1.13 GB | 약 2–4 GB | 약 2–4 GB | 파일 크기에 컨텍스트·버퍼 여유를 더한 앱 추정 범위, 미실측 |
| HY-MT2 7B | 4.62 GB | 약 6–9 GB | 약 6–9 GB | 파일 크기에 컨텍스트·버퍼 여유를 더한 앱 추정 범위, 미실측 |
| Gemma 4 12B Heretic StyleTune | 8.30 GB | 약 8.1 GB | 약 8.3 GB | 기존 앱의 GPU 배치 참고값 유지 |
| ANIS Trans | 0.30 GB | 약 1 GB | CUDA에서 약 1–2 GB | RAM은 제작자 CPU 참고값, VRAM은 앱의 미실측 추정 범위 |

GB는 10억 바이트다. Gemma 저장공간은 본체와 번들 MTP 합계다. 파일 크기는 고정된 카탈로그 파일의 실제 바이트 합계이므로 메모리 추정과 구분한다. 첫 설치 권장 여유공간은 내려받는 모델 파일 합계에 실행환경·압축 해제·캐시용 2 GB를 더하고 0.1 GB 단위로 올림한다. 이는 정확한 설치 크기나 디스크 검사 결과가 아니다. 연결한 로컬 파일은 복사하지 않으며 추가 다운로드 예산에서 제외한다. 크기가 알려지지 않은 파일에는 전체 설치 여유공간을 임의로 계산하지 않는다.

GGUF 참고값의 기본 대화 길이는 E2B/E4B/HY-MT2 8,192, 12B 32,768이다. GPU 할당·KV 캐시·대화 길이·lazy 설정과 드라이버에 따라 실제 RAM/VRAM 분포가 달라진다. CPU 모드는 모델 VRAM을 쓰지 않지만 RAM 사용이 늘어날 수 있다. 화면의 RAM·VRAM을 더하거나 각각 설치 최소 사양으로 해석하면 안 된다. [llama.cpp 공식 서버 문서](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md)의 GPU offload·context·KV cache 옵션을 참고했다.

[ANIS 공식 README](https://github.com/jintigers/anis-trans/blob/1165f0cda25a9afdd0c8e0491038e949d9446e6e/README.md)는 CPU RAM 약 1 GB, 설치 디스크 약 0.6 GB와 GPU cuBLAS 추가분을 안내한다. 화면에서도 CPU RAM 제작자 참고값과 CUDA VRAM 앱 추정치를 구분한다. CPU에서는 VRAM을 사용하지 않는다. 실제 접근 승인된 모델로 CUDA 사용량을 측정한 수치는 아니다.

추정치는 `config/launcher-models.json`의 선택 사항인 `resources`에 둔다. 잘못된 숫자나 추정 정보가 없는 사용자 모델에는 정보 없음으로 표시한다. 실행 설정이 바뀌어도 이 안내를 현재 설정의 실측값으로 바꾸지 않고 기본 설정 참고치라는 설명을 유지한다.
