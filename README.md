# PensionCompass

> 삼성생명 IRP(개인형 퇴직연금) 계좌를 AI에게 분석시켜 리밸런싱 제안을 받는 Windows 11 데스크톱 앱

삼성생명 마이페이지에서 저장한 상품 목록 HTML과 사용자가 입력한 보유 상품 정보를 종합해, Claude / Gemini / GPT 중 하나의 AI에게 구체적인 매도·매수 제안을 받아 PDF로 출력합니다.

---

## 주요 기능

- **HTML / CSV 임포트** — 삼성생명 "퇴직연금 전체 상품" 페이지를 저장한 HTML을 파싱해 원리금보장형 / 펀드 두 개의 CSV로 변환. HTML 한 부에는 수익률 한 기간만 들어 있어 정렬을 바꿔가며 여러 번 저장해 머지하면 1개월/3개월/6개월/1년/3년 컬럼을 모두 채울 수 있음. 한번 저장된 CSV는 다음 실행 시 다시 임포트하지 않아도 자동 복원
- **계좌·보유 상품 입력** — 총 적립금·입금액·운용수익을 입력하고, 임포트된 카탈로그를 검색해 보유 상품을 추가. 적립금/수익률/연환산수익률/운용일수/좌수를 행마다 인라인 편집
- **매도 가능 여부 + 실행 시점** — 보유 상품마다 "매도 가능" 체크박스만으로 사용자 의사 표시 (전량/부분 매도 비중은 AI가 판단). 즉시 리밸런싱 또는 만기 예약용 리밸런싱 선택 + 만기 예약일 경우 실행 날짜 입력
- **AI 리밸런싱 제안** — Anthropic Claude, Google Gemini, OpenAI GPT 중 선택. 공급자별로 별도의 API Key·모델 ID·기본 사고 수준(끔/낮음/보통/최상) 저장. 모델 목록 조회 기능으로 정확한 모델 ID(예: `gemini-3.1-pro-preview`) 발견 가능
- **마크다운 렌더링 + PDF 출력** — AI 응답은 WebView2로 마크다운 렌더링 (제목·표·목록·강조 모두 시각화). QuestPDF로 한국어 PDF 출력 — 헤더·계좌 현황·매도 결정 표·AI 본문 모두 구조화
- **자동 영속화 + 화면별 초기화** — 계좌 정보·카탈로그·환경 설정이 모두 자동 저장되어 다음 실행 시 그대로 복원. 각 화면에 초기화 버튼 (확인 다이얼로그 거쳐 해당 슬라이스만 삭제)
- **종신형 연금 옵션** — 체크 시 AI 프롬프트에 "운용사가 삼성생명/삼성인 상품으로 제한"이라는 hard constraint 추가

---

## 화면 구성

좌측 NavigationView로 5개 화면 전환:

1. **환경 설정** — AI 공급자 (Claude/Gemini/GPT) · 공급자별 API Key (3개 PasswordBox) · 공급자별 모델 ID (3개 TextBox + 모델 목록 가져오기 버튼) · 사고 수준 (끔/낮음/보통/최상, 기본값 최상) · 종신형 연금 옵션
2. **상품 데이터 준비** — HTML 불러오기(덮어쓰기) / 다른 기간 HTML 추가 머지 / CSV 폴더에서 불러오기 / CSV 폴더로 저장 / 카탈로그 초기화. TabView로 원리금보장형·펀드 두 표 미리보기
3. **내 계좌 정보** — 총/입금/운용수익 입력 + AutoSuggestBox로 카탈로그에서 보유 상품 검색·추가 + 인라인 편집 가능한 행 + 계좌 정보 초기화 버튼
4. **매도 대상 및 시점 설정** — 즉시 / 만기 예약 라디오 + (만기 예약일 때) 실행 날짜 CalendarDatePicker + 보유 상품마다 "매도 가능" 체크박스 + 매도 가능/금지 합계 라이브 표시
5. **AI 포트폴리오 리밸런싱** — 자유 텍스트 추가 요구사항 + "포트폴리오 제안 받기" + 응답을 WebView2 마크다운 렌더링 + "PDF로 내보내기"

---

## 동작 흐름

```text
환경 설정      →  상품 데이터 준비  →  내 계좌 정보  →  매도 대상 및 시점 설정  →  AI 포트폴리오 리밸런싱
(API Key/모델)   (HTML/CSV 임포트)    (보유 상품 등록)    (매도 가능 여부 + 실행일)   (제안 → PDF 출력)
```

각 단계 데이터는 자동으로 디스크에 저장되어 다음 실행 시 그대로 복원되므로 매번 다시 입력할 필요가 없습니다.

---

## 기술 스택

- **플랫폼**: WinUI 3 (Packaged), .NET 8 (`net8.0-windows10.0.19041.0`), C#, XAML
- **UI 패턴**: MVVM (CommunityToolkit.Mvvm 8.4)
- **HTML 파싱**: HtmlAgilityPack 1.11
- **AI 호출**: 순수 `HttpClient` + `System.Text.Json` (공급자 SDK 없음)
- **마크다운**: Markdig 1.1
- **PDF**: QuestPDF 2026.2 (Community 라이선스)
- **테스트**: xUnit (Core 라이브러리 27개 테스트)

상세 아키텍처 / 데이터 모델 / HTML 파서 동작은 [CLAUDE.md](CLAUDE.md)를 참고하세요.

---

## 사전 요구사항

- **Windows 11** (Windows 10 1809 / 빌드 17763 이상도 가능하지만 11 권장)
- **Visual Studio 2026** (또는 2022 17.10+) — 다음 워크로드 포함 설치:
  - .NET 데스크톱 개발
  - Universal Windows Platform 개발 (WinUI 3 / Windows App SDK)
- **.NET 8 SDK** (Visual Studio 설치 시 자동 포함됨)
- **WebView2 런타임** — 응답 마크다운 렌더링용. Windows 11에는 기본 탑재
- **사용할 AI 공급자의 API Key** 한 개 이상 (Anthropic / Google AI Studio / OpenAI)

---

## 빌드 / 실행

### 1. Visual Studio에서 (권장)

```text
1. PensionCompass.slnx 열기
2. 솔루션 플랫폼을 x64 (또는 ARM64)로 변경
3. PensionCompass 프로젝트를 시작 프로젝트로 지정
4. F5 (디버그 실행) 또는 Ctrl+F5 (디버그 없이 실행)
```

### 2. 명령줄에서

```pwsh
# 전체 솔루션 복원 + 빌드
dotnet build PensionCompass.slnx -p:Platform=x64

# Core 라이브러리 단위 테스트 실행
dotnet test tests/PensionCompass.Core.Tests/PensionCompass.Core.Tests.csproj

# WinUI 앱만 빌드
dotnet build PensionCompass/PensionCompass.csproj -p:Platform=x64
```

> **참고:** WinUI 3 Packaged 앱은 `dotnet run`으로 실행되지 않습니다. Visual Studio의 디버거로 실행하거나, 빌드한 후 `PensionCompass\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\` 의 실행 파일을 직접 실행해야 합니다.

### 3. 첫 실행 시 설정

1. **환경 설정** 화면에서 사용할 공급자 선택 → 그 공급자의 API Key 입력
2. (선택) **모델 목록 가져오기** 버튼으로 사용 가능한 모델 확인 후 선택
3. 사고 수준은 기본값 "최상" 유지 권장 (자주 하는 작업이 아니라 깊이 있는 답변이 더 가치 있음)

---

## 데이터 저장 위치

모든 사용자 데이터는 패키지된 앱의 per-user LocalState 폴더에 저장됩니다:

```text
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\
├── account.json   # 계좌 정보 + 보유 상품 + 매도 결정
├── catalog.json   # 임포트된 상품 카탈로그
└── (LocalSettings) # API Key, 모델 ID, 사고 수준 등
```

API Key는 평문으로 저장되니 공용 PC에서는 사용을 피해주세요. 개인 PC라면 Windows 사용자 격리 수준에서 안전합니다.

---

## 디렉터리 구조

```text
PensionCompass/                 # WinUI 3 앱 (UI 레이어)
├── Views/                          # 5개 화면 + Reset/PDF 다이얼로그
├── ViewModels/                     # 화면별 VM + 행 VM
├── Services/                       # AppState (싱글톤), StateStore (JSON), SettingsService (LocalSettings)
└── Converters/                     # WonFormatConverter, BoolToVisibilityConverter
src/PensionCompass.Core/        # UI 의존성 없는 Core 라이브러리
├── Models/                         # 데이터 모델 (record + enum)
├── Parsing/                        # 삼성생명 HTML 파서
├── Csv/                            # CSV 읽기/쓰기
├── Ai/                             # IAiClient + 3개 공급자 + PromptBuilder
├── Markdown/                       # Markdig → HTML
└── Pdf/                            # QuestPDF 출력
tests/PensionCompass.Core.Tests/ # xUnit 테스트
doc/                                # 설계서 + 프롬프트 사례
reference/                          # 실제 삼성생명 HTML 스냅샷 (테스트용)
```

---

## 개발 진행

- `Initial commit` — 빈 저장소
- `상세 설계서 작성` — Gemini로 생성한 설계서 + 사용자의 원 요청 정리
- `CLAUDE.md 추가` — Claude Code용 작업 가이드
- `1차 동작 버전 완성` — 5개 화면 모두 기본 동작 (HTML/CSV 파싱, 계좌 입력, 매도 결정, AI 호출, PDF 출력) + 자동 영속화 + 초기화 버튼
- `AI 선택 관련 옵션 추가` — 공급자별 모델 ID 입력 + 사고 수준 선택 + 모델 목록 조회 + 공급자별 API Key 분리

---

## 알려진 제한사항

- **WinUI 3 데이터그리드 미사용** — Windows App SDK 2.0 호환 DataGrid 패키지가 없어 `ListView` + 정렬된 컬럼 템플릿으로 대체. 기능상 문제는 없으나 정렬·필터링 같은 폴리시는 없음
- **AI 모델 기본값** — 코드 내 `DefaultModel` 상수 (`claude-opus-4-7` / `gemini-2.5-pro` / `gpt-5`). 새 모델은 환경 설정 화면에서 직접 입력하거나 모델 목록 조회로 발견
- **API Key 평문 저장** — `Windows.Storage.ApplicationData.LocalSettings`에 평문. 공용 PC 미권장. 추후 `Windows.Security.Credentials.PasswordVault` 마이그레이션 가능
- **개인 사용 목적** — 단일 사용자, 단일 IRP 계좌를 가정. 공유 환경 / 다중 계좌는 고려되지 않음

---

## 면책

이 앱이 생성하는 리밸런싱 제안은 **AI의 분석 결과일 뿐 투자 권유가 아닙니다.** 실제 매수·매도 결정은 본인의 책임 하에, 추가적인 자료 검토와 전문가 상담을 거쳐 진행하시기 바랍니다. 개발자는 이 도구의 사용으로 인한 손익에 대해 어떠한 책임도 지지 않습니다.
