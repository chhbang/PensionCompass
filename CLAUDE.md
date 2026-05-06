# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository status

All six screens are functional end-to-end: HTML/CSV import → account input → sell-decision → AI proposal → PDF export, plus a 이력 (history) screen for browsing past sessions. State (account + catalog + settings) auto-persists across launches; AI recommendations save explicitly per the user opt-in policy. With an optional sync folder configured, state and history are also mirrored across PCs via the user's own cloud client (OneDrive / Google Drive desktop / Dropbox). The Core library is covered by 50+ xUnit tests against the real reference HTML.

Design docs are in Korean. Source of truth: [doc/PensionCompass_상세_설계서.md](doc/PensionCompass_상세_설계서.md) (Gemini-generated from the user's brief in [doc/prompt.txt](doc/prompt.txt)). A real saved snapshot of the Samsung Life product page lives at [reference/퇴직연금 전체 상품 - 퇴직연금 - 삼성생명.html](reference/퇴직연금%20전체%20상품%20-%20퇴직연금%20-%20삼성생명.html) and is used by the parser tests. A sample of the kind of proposal the AI should return is in [doc/삼성생명 IRP 펀드 리밸런싱 전략 제안.md](doc/삼성생명%20IRP%20펀드%20리밸런싱%20전략%20제안.md) — useful as a target for prompt design and PDF layout.

The user's actual implementation choices have diverged from the original Gemini-generated spec in two notable places — see "Cross-cutting constraints" below. Treat the spec as historical context, not an authoritative bible.

## Solution layout

```text
PensionCompass.slnx                     # solution (new XML format, .NET 10 SDK)
PensionCompass/                         # WinUI 3 app, net8.0-windows10.0.19041.0
├── App.xaml{.cs}                           # entry point — sets QuestPDF Community license, instantiates MainWindow
├── MainWindow.xaml{.cs}                    # NavigationView shell with 6 menu items + Frame
├── Views/                                  # one Page per screen (Settings, DataPreparation, MyAccount, SellTargets, AiRebalance, History, About)
├── ViewModels/                             # one ObservableObject per screen + row VMs (FundRow, OwnedProductRow, SellTargetsRow)
├── Services/                               # AppState (singleton), StateStore (JSON persist + sync mirroring), SettingsService (LocalSettings + PasswordVault), WindowHelper (HWND interop)
└── Converters/                             # WonFormatConverter (₩N0), BoolToVisibilityConverter
src/PensionCompass.Core/                # net8.0 class library (UI-free)
├── Models/                                 # records + enums (AccountStatusModel, OwnedProductModel, FundProduct, ProductCatalog, ReturnPeriod, RebalanceTiming, PortfolioSnapshot)
├── Parsing/                                # SamsungLifeHtmlParser (catalog), SamsungLifePortfolioHtmlParser (my account), AssetManagerResolver, ProductCatalogMerger
├── Csv/                                    # CsvWriter / CsvCatalogLoader (catalog), PortfolioCsvWriter / PortfolioCsvLoader (my account)
├── History/                                # RebalanceSession + RebalanceHistoryStore (per-session JSON archive)
├── Ai/                                     # IAiClient + AnthropicClient, OpenAiClient, GeminiClient, AiClientFactory, PromptBuilder
├── Markdown/MarkdownToHtml.cs              # Markdig → HTML for WebView2 display
└── Pdf/                                    # PdfReport (data) + PdfExporter (QuestPDF, walks Markdig AST)
tests/PensionCompass.Core.Tests/        # xUnit, runs against the real reference HTML
```

The `Core` library is deliberately framework-free — parser, CSV, AI clients, prompt builder, PDF generator are all testable from the CLI without spinning up WinUI. The WinUI app references Core via `ProjectReference`.

## Build, test, run

```pwsh
# Restore + build everything (Core, Tests, WinUI app)
dotnet build PensionCompass.slnx -p:Platform=x64

# Run all unit tests — preferred for fast iteration on Core
dotnet test tests/PensionCompass.Core.Tests/PensionCompass.Core.Tests.csproj

# Run the WinUI app
dotnet build PensionCompass/PensionCompass.csproj -p:Platform=x64
# Then F5 in Visual Studio. `dotnet run` does NOT work for packaged WinUI 3 apps.
```

The WinUI 3 app cannot be launched via `dotnet run` because it is packaged (MSIX). Use Visual Studio's debugger, or for a one-off non-deployment run, build then execute the produced `.exe` from `PensionCompass/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/`.

## Sideload release packaging (CLI alternative to VS wizard)

Visual Studio의 Solution Explorer → 우클릭 → **Package and Publish → Create App Packages...** 마법사가 사이드로드 .msix 빌드의 정공법입니다 (UI에서 인증서 생성·platform 선택·자동 업데이트 설정까지 한 번에). CLI로 동등한 결과를 내는 백업 경로:

```pwsh
# 1) 새 빌드를 내기 직전: 버전 한 칸 올리기 (기본은 build 자리, -Major / -Minor / -Revision 옵션)
.\tools\Bump-Version.ps1

# 2) 사이드로드 모드로 .msix 번들 생성 (인증서는 PensionCompass_TemporaryKey.pfx 사용)
dotnet build PensionCompass\PensionCompass.csproj `
    -c Release `
    -p:Platform=x64 `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=true `
    -p:AppxBundle=Always `
    -p:AppxBundlePlatforms=x64 `
    -p:UapAppxPackageBuildMode=SideloadOnly `
    -p:AppxPackageDir=AppPackages\
```

산출물은 `PensionCompass\AppPackages\PensionCompass_<version>_Test\` 안에 `.msixbundle` + `.cer`로 떨어집니다. 다른 PC에 설치할 땐 `.cer`을 **로컬 컴퓨터 / 신뢰할 수 있는 루트 인증 기관(Trusted Root Certification Authorities)** 저장소에 등록한 뒤 `.msixbundle`을 더블클릭하면 됩니다 — `Import-Certificate -FilePath <path>.cer -CertStoreLocation Cert:\LocalMachine\Root` (관리자 PowerShell). 자체 서명 인증서는 자기 자신이 루트라 게시자(Trusted Publishers)나 사용자(Trusted People) 저장소에 넣으면 체인 검증이 끊겨 `0x800B010A` (CERT_E_CHAINING) 오류로 설치가 실패합니다.

CLI 경로는 CI/스크립트화에 유리하지만 마법사와 비교하면 두 가지 차이가 있습니다:

- **`Add-AppDevPackage.ps1` / `Install.ps1` 같은 설치 도우미 스크립트는 마법사에서만 생성됩니다.** 받는 PC에서 헬퍼로 자동 신뢰 등록까지 한 번에 시키고 싶다면 마법사를 쓰세요. CLI 산출물(.cer + .msixbundle)만으로도 수동 설치는 정상 동작합니다.
- **자동 업데이트(.appinstaller) 설정도 마법사에서만 잡을 수 있습니다** — 자동 업데이트가 필요하면 마법사를 쓰세요.

또 `AppxPackageDir`은 **csproj 디렉터리 기준 상대경로로 해석되는** 점에 주의 — csproj가 `PensionCompass\` 안에 있으므로 위 명령은 `AppPackages\`만 적어 리포 루트 기준 `PensionCompass\AppPackages\`에 떨어지게 합니다. 과거에 `PensionCompass\AppPackages\`로 적어두면 `PensionCompass\PensionCompass\AppPackages\`로 한 단계 더 깊어졌습니다.

인증서가 아직 없는 새 클론에서 CLI를 처음 돌리려면 인증서 자체는 한 번 마법사로 만들어야 합니다 (.pfx는 git ignore).

## What the app does

`PensionCompass` is a Windows desktop app that produces AI-generated rebalancing proposals for a Samsung Life Insurance IRP (개인형 퇴직연금 / Korean individual retirement pension) account:

1. **Data preparation.** User imports the Samsung Life product-list HTML (or pre-exported CSVs) → app parses into `ProductCatalog` (principal-guaranteed + funds, with dynamic 수익률(N개월/년) columns). Multiple HTML snapshots can be merged by product code to populate all five return periods.
2. **Account input.** User enters total balance + per-holding amounts. AutoSuggestBox feeds from the parsed catalog.
3. **Sell decisions + timing.** Per holding: `IsSellable` checkbox (default true). At account level: 즉시 vs 만기 예약 radio + execution date picker (only when 만기 예약).
4. **AI proposal.** App builds a structured Korean markdown prompt and queries Claude / Gemini / GPT via the user's API key. Response renders in WebView2 via Markdig.
5. **PDF export.** QuestPDF walks the Markdig AST to produce a structured PDF with header, account/sell-decision tables, and the AI body — Korean text via Malgun Gothic.

## Architecture

- **Platform:** WinUI 3 Blank App (Packaged), .NET 8 (`net8.0-windows10.0.19041.0`), C# + XAML, Visual Studio 2026, Windows App SDK 2.0.1.
- **Pattern:** MVVM via `CommunityToolkit.Mvvm`. The 5 screens are hosted in a single `NavigationView` shell.
- **Libraries:**
  - `HtmlAgilityPack` — Samsung Life HTML parsing
  - `Markdig` — markdown → HTML / AST
  - `QuestPDF` — PDF generation (Community license, set in `App.xaml.cs`)
  - `HttpClient` + `System.Text.Json` — AI REST calls (no provider SDKs)
  - `CommunityToolkit.Mvvm` — ObservableObject, ObservableProperty
- **Five screens:** Settings (AI provider + API key, lifelong-annuity flag) → Data Preparation (HTML/CSV → catalog) → My Account → Sell Targets & Timing → AI Rebalance.

### Cross-cutting constraints

These shaped the design materially and are easy to miss when reading code in isolation:

- **운용사 (asset manager) is load-bearing — and the lifelong-annuity rule is STRICT.** If the user checks "lifelong annuity payout" in Settings, the prompt constrains the AI to recommend only products where the catalog 운용사 column is the entity 삼성생명보험주식회사 — i.e. `AssetManagerResolver.IsSamsungLifeInsurance(...)` returns true. Two surface forms exist for that single legal entity: PG products fall through `AssetManagerResolver`'s prefix table to the `"삼성생명"` fallback, while funds get whatever the HTML's `div.desc-sum > span` literally says — which for Samsung Life's own funds is `"삼성생명보험"`. The predicate accepts both. Affiliates like 삼성자산운용㈜ (Samsung Asset Management — a separate legal entity) are explicitly excluded. **Funds are NOT blanket-excluded** — Samsung Life directly manages a meaningful set of funds (S Selection 주식형/혼합형, 삼성그룹주식형, 인덱스주식형, 일반주식형, 채권형 등) and these are valid candidates under the lifelong-annuity rule; the prompt explicitly tells the AI so. The Data Preparation screen surfaces counts of qualifying PG and qualifying funds so the user can see the universe size at a glance.
- **Per-holding `IsSellable` flag, not granular sell amounts.** The user marks which holdings are off-limits ("절대 매도하지 않을 상품"); everything else is in the AI's pool to size on its own. The prompt hard-constrains the AI to never sell holdings where `IsSellable == false`. This intentionally diverges from the original spec (which had FullSell / PartialSell / Keep with explicit sell amounts) — the AI is the expert on sizing, the user only sets boundaries.
- **Rebalance timing + execution date.** The 즉시 vs 만기 예약 radio is required. When MaturityReservation is selected, `AccountStatusModel.ExecutionDate` (DateOnly) is also required and represents the planned execution date — typically the maturity date of the product that triggered the rebalance. Both flow into the prompt as framing.

### HTML parser — what the saved page actually looks like

A real saved snapshot is checked in at [reference/퇴직연금 전체 상품 - 퇴직연금 - 삼성생명.html](reference/퇴직연금%20전체%20상품%20-%20퇴직연금%20-%20삼성생명.html). It is a Vue SSR dump from `samsunglife.com/.../MDP-MYRET080910M`. Key facts that diverge from the spec — read these before touching the parser:

- **No `<table>` tags.** Products are rendered as `<li class="data-list-item">` cards. Iterate `li.data-list-item` instead of table rows. Both lists (principal-guaranteed and funds) use the *same* card class — distinguish them by the badge inside `p.flag-group`: principal-guaranteed cards contain `<span class="flag badge gray">원리금보장형</span>`; funds carry a risk-grade badge like `<span class="flag badge grade1">매우높은위험</span>` plus an asset-class badge (`위험자산` / `안정자산`).
- **Product code lives in the cart checkbox** at `input[name="productCheckCart"]`'s `id` / `value` — e.g. `C02011`, `G04783`, `C03049`. Both `C` and `G` prefixes appear for both product types, so do *not* infer category from the code prefix.
- **Product name** is `a.desc-title > strong`. **Applied rate / return value** is `ul.desc-list li.desc-item div.desc span` (last `<span>` — there can be a tooltip button before it).
- **Funds expose 운용사 explicitly** at `div.desc-sum > span:first-child` (e.g. `미래에셋자산운용㈜`, `한화자산운용`). Read it directly. The spec's "extract manager from product name prefix" approach is wrong for funds.
- **Principal-guaranteed products do *not* have `desc-sum`.** 운용사 is derived from the product name prefix via `AssetManagerResolver` (small mapping table for `메리츠`, `푸본현대생명`, `고려저축은행`, etc.). For Samsung Life's own products like `이율보증형(3년)` (no recognizable prefix), it falls back to `삼성생명`.
- **Maturity term (만기) for principal-guaranteed is embedded in the name**, not a separate field. Multiple formats appear: `(IRP 3년)` suffix, `1년_IRP` suffix (with leading space in source), `이율보증형(3년)`. Regex `(\d+)\s*년` over the name is sufficient.
- **Applied rate may carry a parenthesised second number**, e.g. `3.65%(3.78%)`. The first is annual compound, the second (in parens) is the simple-interest equivalent. We store the first.
- **Critical: only ONE return-period populates per saved HTML.** Each fund card carries exactly one `<em>수익률(N개월)</em>` row, where N is whatever sort the user had selected when they saved (the radio options are 1개월/3개월/6개월/1년/3년). To populate all five columns of the fund CSV, the user must save and import five separate snapshots; `ProductCatalogMerger` folds them by product code. The spec implies all periods come from one file — they don't. The Data Preparation screen has a "다른 기간 HTML 추가 머지" button to surface this.

### Data models

Live in `src/PensionCompass.Core/Models/`:

- `AccountStatusModel` — `TotalAmount` (required), `DepositAmount?`, `ProfitAmount?`, `RebalanceTiming`, `ExecutionDate?` (DateOnly, required when timing is MaturityReservation), subscriber info (`CurrentAge?`, `DesiredAnnuityStartAge?`, `WantsLifelongAnnuity`), `OwnedItems: List<OwnedProductModel>`. Subscriber info drives the prompt's "## 가입자 정보" section and (when `WantsLifelongAnnuity == true`) activates the manager-restriction constraint.
- `OwnedProductModel` — `ProductName`, `CurrentValue` (required), optional `ReturnRate`, `AnnualizedReturn`, `InvestedDays`, `TotalShares`, plus `IsSellable` (default true; false locks the holding from the AI's sell pool).
- `PrincipalGuaranteedProduct`, `FundProduct`, `ProductCatalog` — output of the parser. `FundProduct.Returns` is `IReadOnlyDictionary<ReturnPeriod, string>` so a single-snapshot import populates only one entry per fund (see HTML parser section above).

## Persistence

Multiple independent stores keep state across launches; the Reset buttons are screen-scoped on purpose so the user can wipe one slice without touching the others. Each store has a different sensitivity profile — non-secret prefs go to LocalSettings, account/catalog state mirrors to the optional sync folder for cross-device pickup, and API keys live in PasswordVault and never touch disk.

| Slice | Backing store | Path | Save trigger | Reset trigger |
| --- | --- | --- | --- | --- |
| AI provider, per-provider model id, thinking level, sync folder path | `Windows.Storage.ApplicationData.LocalSettings` | per-user package settings | every property setter (immediate) | manual edit on Settings page (no reset button) |
| Per-provider API keys (Claude/Gemini/GPT) | `Windows.Security.Credentials.PasswordVault` | OS credential vault, encrypted with user logon credentials | property setter via `WriteVault()` | clear via Settings PasswordBox |
| Catalog | `StateStore` → JSON in `LocalFolder/catalog.json`; mirrored to `<syncFolder>/catalog.json` when sync configured | `%LOCALAPPDATA%\Packages\<pfn>\LocalState\` (+ sync folder) | `AppState.Catalog` setter via `OnCatalogChanged` partial method | "카탈로그 초기화" button on Data Preparation (with ContentDialog confirm) |
| Account (totals, subscriber info incl. lifelong-annuity flag, OwnedItems with IsSellable, RebalanceTiming, ExecutionDate) | `StateStore` → JSON in `LocalFolder/account.json`; mirrored to sync folder | same | every VM mutation calls `AppState.Instance.SaveAccount()` (My Account VM + Sell Targets VM, including row PropertyChanged) | "계좌 정보 초기화" button on My Account (with ContentDialog confirm) |
| Rebalance history (per-session JSON: input snapshot + AI markdown response) | `RebalanceHistoryStore` → `<syncFolder or LocalState>\History\<timestamp>_<provider>.json` | one file per saved session | **explicit** "이력에 저장" button on AI Rebalance — not auto-saved, because users typically iterate across multiple AI/model combos before keeping one | "삭제" button per row on the 이력 screen (with ContentDialog confirm) |

**Sync folder mirroring** (StateStore): when `Settings.SyncFolder` is non-empty, every save writes to BOTH `LocalState` and the sync folder, and every load picks whichever copy has the newer mtime. This is what makes cross-device handoff work without explicit "open file" UI — PC1 saves → cloud client uploads → PC2's sync folder copy ages newer than its LocalState → PC2's next launch loads the cloud version. The provider lookup is `Func<string?>` rather than a captured value so changing the SyncFolder setting takes effect without restarting the app. Mirror writes are best-effort: a locked/offline sync folder doesn't block LocalState progress.

**History save policy is explicit, not auto** — the user's own answer to "should this auto-save?" was no, because it's normal to run the same input through multiple providers/models before deciding which recommendation to keep. The "이력에 저장" button on AI Rebalance is the only entry point that writes a session file. Listing scans BOTH `<LocalState>\History\` and `<syncFolder>\History\` (when configured) and dedupes by canonical path, so a user changing the SyncFolder setting later doesn't appear to lose old history.

**HTML imports are never persisted anywhere** — they're parsed in-memory and the file path isn't kept. This is intentional because the raw HTML carries subscriber name + account number even though the parser ignores those areas.

`AppState`'s constructor runs two one-shot migrations: lifelong-annuity flag moves from LocalSettings to `AccountStatusModel.WantsLifelongAnnuity`, and any plaintext API keys still in LocalSettings (from builds before v1.0.4) sweep into PasswordVault. Both migrations are idempotent.

`AppState.Instance` is a process-wide singleton that loads both Account and Catalog in its constructor by passing `Settings.SyncFolder` to `StateStore` via a lazy provider. Catalog round-trips through a private DTO (`CatalogDto` / `FundProductDto`) inside `StateStore.cs` because `IReadOnlyList<T>` and `Dictionary<ReturnPeriod, string>` don't deserialize cleanly through System.Text.Json defaults. Best-effort I/O — corrupt JSON snapshots are silently ignored on load and the app starts fresh, rather than crashing.

When wiring a new VM that mutates `AppState.Account`, remember to call `AppState.Instance.SaveAccount()` after the mutation. Catalog is automatic. History is opt-in via the AI Rebalance VM's `SaveCurrentResponseToHistory()`.

## Working notes

- **Korean is the user-facing language.** UI strings, error messages, prompts to the AI, and PDF content should be Korean. Code identifiers stay English.
- **API keys are per-provider** (`ClaudeApiKey`, `GeminiApiKey`, `GptApiKey`) so a Gemini key isn't accidentally sent to Anthropic when the user switches providers. `SettingsService.GetActiveApiKey()` resolves the right one. Stored in `Windows.Security.Credentials.PasswordVault` (resource `"PensionCompass.ApiKey"`, userName = provider) so they're encrypted at rest by Windows under the user's logon credentials — much safer than the previous LocalSettings plaintext if a stealer scrapes the package's local data folder. Two one-shot migrations sit in the constructor: `MigrateLegacyApiKey()` lifts the older single-`ApiKey` slot into the currently-selected per-provider LocalSettings slot, then `MigratePlaintextApiKeysToVault()` sweeps any plaintext per-provider entries (from builds prior to v1.0.4) into the vault and deletes the LocalSettings copies. Caveat: vault-stored credentials are still readable by any code running as the same user via the same WinRT API — true secret-at-rest protection against in-session malware would require a master-password UX which we judged not worth the per-launch friction for a single-user tool.
- **AI model defaults** live as `DefaultModel` constants on each client (`AnthropicClient`, `OpenAiClient`, `GeminiClient`). The Settings screen exposes a per-provider TextBox so the user can override without code changes — values persist via `SettingsService.{Claude,Gemini,Gpt}Model`. `SettingsService.GetActiveModel()` resolves the model for the currently-selected provider.
- **Model discovery**: `IAiClient.ListModelsAsync()` hits each provider's list endpoint (Anthropic `/v1/models`, OpenAI `/v1/models`, Gemini `/v1beta/models?key=...`). Gemini returns `models/...`-prefixed names which the client strips, and only those advertising `generateContent` in `supportedGenerationMethods` are returned. OpenAI's catalog includes embedding/audio/moderation models that aren't usable here, so the client filters to ids starting with `gpt-`/`o1`/`o3`/`o4`/`chatgpt-` (falls back to the unfiltered list if the heuristic empties everything). The Settings screen has a "현재 공급자의 모델 목록 가져오기" button that opens a ContentDialog with the fetched list — useful when a model id like `gemini-3.1-pro` 404s and the user needs to see it's actually `gemini-3.1-pro-preview`.
- **Thinking / reasoning effort** is a single user-facing slider (Off / Low / Medium / High, default High because rebalancing is an infrequent, high-stakes call). Each client maps the level onto its provider's native knob: Anthropic → `thinking.budget_tokens` (also requires a larger `max_tokens` ceiling, which the client adjusts in lockstep), OpenAI → `reasoning_effort` ("low" / "medium" / "high"; omitted entirely when Off), Gemini → `generationConfig.thinkingConfig.thinkingBudget` (-1 = dynamic on High). Off bypasses the field on all three. With Anthropic extended thinking enabled, the response's `content` array can interleave thinking blocks with text blocks, so `AnthropicClient` filters for `type == "text"` only.
- **CommunityToolkit.Mvvm partial-property AOT warnings (MVVMTK0045)** are suppressed via `<NoWarn>$(NoWarn);MVVMTK0045</NoWarn>` in the WinUI csproj. The newer partial-property syntax was tried and didn't bind to the source generator in this combo (.NET 10 SDK + net8 target + WindowsAppSDK 2.0.1) — re-investigate when bumping the toolkit or moving the WinUI app to net9/net10.
- **WinUI 3 file/folder pickers** need the main window's HWND or they crash. Always go through `WindowHelper.Initialize(picker, App.Window)` from `Services/WindowHelper.cs`.
- **PasswordBox.Password is intentionally not bindable** in WinUI 3 (security policy) — set the initial value imperatively in the View constructor and forward changes through `PasswordChanged` (see `SettingsView.xaml.cs`).
