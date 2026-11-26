# AGENTS.md — LiveCG-EFF 에이전트 작업 지침

Antigravity 및 Codex가 공통으로 참조하는 최소 컨텍스트 규칙 문서입니다.

## 1. 프로젝트 개요 및 기술 스택
- **개요:** 이미지 지정 영역에 모션 효과를 적용해 **투명 VP9 WebM** 및 **로컬 OBS 브라우저 패키지(HTML/Canvas)**로 출력하는 라이브커머스 자막 편집기 (OCR·오디오 미포함)
- **언어/런타임:** C# (`LangVersion: latest`, `Nullable`/`AllowUnsafeBlocks` 활성), `.NET 10.0` (`10.0.403`, `win-x64`), JavaScript (OBS 로컬 런타임), PowerShell, Lua (OBS 스크립트)
- **프레임워크/라이브러리:** WPF (`net10.0-windows10.0.19041.0`), `SkiaSharp` & `SkiaSharp.Views.WPF` (`3.119.2`)
- **실행/외부 환경:** Windows x64 전용, FFmpeg/FFprobe `9.0.2` (`.tools/ffmpeg` 및 배포본 내장), 선택 검증용 Node.js 24 + Chrome / OBS 32·33

## 2. 핵심 폴더 및 파일 구조
- `LiveCG-EFF.slnx` / `Directory.Build.props` / `global.json`: 솔루션 구성, 앱 버전(`1.6.1`)·메타데이터, 고정 .NET SDK 버전 정의
- `src/LiveCG.Core/`: UI 독립 도메인·렌더링·입출력 코어 (`net10.0`)
  - `Models.cs`, `MotionModels.cs`, `DocumentStore.cs`, `EffectAssets.cs`: 도메인 모델(`EffectKind` 등), JSON 직렬화·검증, 스키마 마이그레이션(`SchemaVersion` 1→6), 프리셋/내 효과 저장소
  - `EffectScene.cs`, `EffectScene.Browser.cs`, `EffectRenderer.cs`, `EffectMotionRenderer.cs`: SkiaSharp 프레임 렌더링 및 브라우저용 에셋 추출
  - `RegionSeparation.cs`, `TextSeparation*.cs`, `DigitRollLayout.cs`, `RegionMask.cs`, `BlurFocus.cs`, `TravelRoutes.cs`: 배경/글자 획 보존 분리, 숫자 롤링 칸 분할, 마스크, 순차 블러 강조, 여행 경로
  - `WebmExporter.cs`, `BrowserExporter.cs`, `LinkedOutput.cs`, `BroadcastUpdates.cs`, `ObsScenePaths.cs`: 투명 WebM 인코딩, OBS 패키지 및 `.livecg` 연결형 무중단 갱신, 타 PC OBS 장면 경로 보정
  - `Browser/` (`index.html`, `player.js`, `preview.html`): 서버 없이 동작하는 OBS 로컬 Canvas 재생 런타임 (`EmbeddedResource`)
- `src/LiveCG.App/`: WPF 데스크톱 편집기 (`win-x64`)
  - `MainWindow.xaml(.cs)` 및 `MainWindow.*.cs`: 기능별 partial 클래스 분할 (`Editor`, `Effects`, `Favorites`, `Layers`, `LayerPanel`, `Groups`, `Playback`, `Output`, `LinkedOutput`, `Broadcast`, `Softness`, `Sources`, `Travel`, `BlurFocus`, `Mouse`, `Update`, `Workspace`)
  - 전용 편집·도구 창: `EffectStudioWindow.cs`(키프레임 모션), `ObjectSelectionWindow*.cs`(정밀 분리·보정), `TravelRouteWindow.cs`(여행 경로), `BlurFocusWindow.cs`(순차 블러), `DigitCellWindow.cs`(숫자 칸), `BroadcastManagerWindow.cs`
  - `PreviewControl*.cs`, `TimelineControl.cs`, `MotionTimelineControl.cs`, `WorkspaceState.cs`: Skia 뷰포트/영역 조작, 타임라인 편집기, 창 배치 상태 저장
- `effects/*.lcgeffect.json`: 기본 번들 프리셋 효과 에셋 (빌드/배포 시 `effects/`로 복사)
- `tests/`: 자체 실행형 회귀 테스트 (`xUnit/NUnit` 아님, 콘솔 종료 코드 `0`/`1`로 판정)
  - `LiveCG.Verify/`: 코어 로직·직렬화·렌더링·WebM 인코딩 검증 (`Program.cs` 및 기능별 `*Checks.cs`)
  - `LiveCG.UiVerify/`: 오프스크린 WPF 창·타임라인·프리뷰 상호작용 검증 (`Program.cs` 및 기능별 `*UiChecks.cs`)
- `tools/`: `bootstrap.ps1`(SDK·FFmpeg 준비), `publish.ps1`·`Launcher.cs`·`InstallUpdate.ps1`(배포 ZIP·런처·자동 업데이트), `LiveCG-OBS-Hotkeys.lua`(OBS 단축키·sRGB 경계 보정), `verify-*.mjs`/`ps1`(브라우저·OBS·패키지 검증)
- `docs/` & `licenses/`: 기능 설계·검증 이력(`01-plan`~`04-report`, `verification/`), 사용설명서, 외부 오픈소스 라이선스 고지

## 3. 빌드·실행 및 테스트 명령어 (PowerShell, 저장소 루트 기준)
- **도구 부트스트랩 (최초 1회 및 새 셸 세션마다 실행):**
  - `. .\tools\bootstrap.ps1` (`.tools/` 하위에 고정 .NET SDK `10.0.403` 및 FFmpeg 자동 준비, `$taskSdk` 변수 설정)
- **전체 빌드:**
  - `& $taskSdk build .\LiveCG-EFF.slnx --disable-build-servers -m:1 -v minimal`
- **앱 로컬 실행:**
  - `& $taskSdk run --project .\src\LiveCG.App\LiveCG.App.csproj --no-build`
- **회귀 테스트 실행 (`dotnet test` 대신 빌드된 실행 어셈블리 직접 호출):**
  - 코어 전체 검증: `& $taskSdk .\tests\LiveCG.Verify\bin\Debug\net10.0\LiveCG.Verify.dll $PWD.Path` (확장 WebM 포함 시 `--extended`)
  - 코어 부분 검증: 뒤에 `--playback-only`, `--linked-output`, `--broadcast-updates`, `--new-effects`, `--project-sources` 등 플래그 지정 가능
  - WPF UI 전체 검증: `& $taskSdk .\tests\LiveCG.UiVerify\bin\Debug\net10.0-windows10.0.19041.0\win-x64\LiveCG.UiVerify.dll $PWD.Path`
  - WPF UI 부분 검증: 뒤에 `--linked-ui`, `--broadcast-ui`, `--project-sources`, `--blur-focus-preview` 등 플래그 지정 가능
- **선택적 통합/배포 검증 (Node 24·Chrome·OBS 환경 필요 시):**
  - 브라우저 비교: `node .\tools\verify-browser.mjs .` 후 `& $taskSdk .\tests\LiveCG.Verify\bin\Debug\net10.0\LiveCG.Verify.dll $PWD.Path --browser-compare`
  - 배포 패키지 생성 및 무결성 검증: `.\tools\publish.ps1` 실행 후 `.\tools\verify-package.ps1`

## 4. 코드 수정 규칙
- **범위 엄수:** 요청받은 기능·버그와 직접 관련된 파일만 수정한다. 범위 밖 파일의 리팩터링, 포맷팅, 네이밍 변경, 임의 문서 수정을 금지한다.
- **격리 작업:** `main` 브랜치에서 직접 작업하거나 커밋하지 말고, 반드시 격리된 Git Worktree 또는 별도 피처/픽스 브랜치에서 작업한다.
- **산출물 오염 금지:** `.tools/`, `bin/`, `obj/`, `artifacts/`, `dist/`, `*.livecg.json`, 테스트 생성 미디어 등 재생성·개인 작업 파일을 커밋에 포함하지 않는다.
- **완료 보고 형식:** 작업 종료 시 반드시 ① **변경된 파일 목록**, ② **주요 변경점 요약**, ③ **실행한 검증 결과 및 추가 테스트 필요 항목**을 정리해 보고한다.

## 5. 주의사항 (핵심 로직 및 호환성 보호)
- **C# Skia ↔ JS Canvas 렌더링 동기화:** 효과·모션·마스크·숫자 롤링·순차 블러·여행 경로 로직 변경 시 `EffectScene*.cs`(네이티브/WebM)와 `src/LiveCG.Core/Browser/player.js`(OBS 브라우저) 양쪽 결과 및 알파 경계 처리가 일치해야 한다.
- **저장 포맷 및 프리셋 하위 호환성:** `Models.cs`의 직렬화 Enum 멤버명(재생 제외된 `Sparkle`, `Highlight`, `Underline` 포함)을 삭제·개명하지 말고, `DocumentStore.cs`의 `SchemaVersion`(현재 6) 마이그레이션과 `effects/*.lcgeffect.json` 로드 호환성을 유지한다.
- **OBS 연동 및 방송 무중단 갱신 계약:** `BrowserExporter.cs`, `LinkedOutput.cs`(`.livecg` 폴더 구조, `pointer.js`, `binding.json`, HTML 첫 줄 마커 주석), `BroadcastUpdates.cs`, `tools/LiveCG-OBS-Hotkeys.lua`의 헤더 판별식, `window.LiveCG` API 및 `LiveCGControl` 이벤트를 임의로 변경하지 않는다.
- **배포·자동 업데이트·버전 무결성:** `MainWindow.Update.cs`, `tools/InstallUpdate.ps1`, `tools/publish.ps1`, `tools/Launcher.cs`가 전제하는 배포 레이아웃(`LiveCG-EFF.exe` + `app/`), `Directory.Build.props`의 버전/제작자 표기(`SunjooAn`), `THIRD_PARTY_NOTICES.md` 및 `licenses/` 고지를 훼손하지 않는다.
- **사용자 로컬 설정 보호:** 테스트·검증 실행 시 실제 `%LOCALAPPDATA%\LiveCG-EFF`(`defaults.json`, `workspace.json`, `assets/`)를 오염시키지 않도록 `LIVECG_DATA_DIR` 격리 및 `LIVECG_DISABLE_WORKSPACE_RESTORE=1` 설정을 유지한다.
