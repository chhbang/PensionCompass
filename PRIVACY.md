# 개인정보 처리방침 / Privacy Policy — 연금나침반 (PensionCompass)

_최종 업데이트: 2026-06-09_

연금나침반(PensionCompass, 이하 "본 앱")은 삼성생명 IRP(개인형 퇴직연금) 계좌의 리밸런싱을 돕는 **개인용 Windows 데스크톱 도구**입니다. 본 앱은 삼성생명보험주식회사와 무관한 비공식(third-party) 도구이며, AI가 생성한 제안은 참고용 정보일 뿐 투자 권유가 아닙니다.

본 문서는 본 앱이 어떤 데이터를 다루고, 어디로 전송하며, 무엇은 전송하지 않는지를 설명합니다.

## 1. 개발자가 수집하는 정보 — 없음

**본 앱 개발자는 사용자의 어떠한 데이터도 수집·전송·저장하지 않습니다.** 본 앱에는 분석(analytics)·원격 측정(telemetry)·광고·추적 기능이 없습니다. 개발자가 운영하는 서버는 존재하지 않습니다.

사용자가 입력한 모든 데이터는 기본적으로 **사용자 본인의 PC에만** 저장됩니다.

## 2. 본 앱이 다루는 데이터

| 데이터 | 저장 위치 | 비고 |
| --- | --- | --- |
| 계좌 현황·보유 상품·매도 결정 (`account.json`) | 사용자 PC의 앱 로컬 폴더 (`LocalState`) | 사용자가 직접 입력 |
| 상품 카탈로그 (`catalog.json`) | 동일 | 삼성생명 상품목록 HTML을 사용자가 저장해 임포트한 결과 |
| 리밸런싱 이력 (`History/*.json`) | 동일 | 사용자가 "이력에 저장"을 누른 회차만 |
| API Key (Claude/Gemini/GPT) | Windows 자격증명 보관소(PasswordVault), 사용자 계정으로 암호화 | 평문이 디스크에 남지 않음 |
| 공급자·모델·사고 수준 등 환경 설정 | Windows 앱 LocalSettings | 비밀 정보 아님 |

삼성생명 페이지에서 저장한 원본 HTML에는 가입자 실명·계좌번호가 포함될 수 있으나, **본 앱은 이를 파싱하지 않으며 어디에도 저장하지 않습니다.** HTML은 메모리에서 상품 정보만 추출한 뒤 폐기되고 파일 경로도 보관하지 않습니다.

## 3. 데이터가 외부로 전송되는 경우 — 사용자가 명시적으로 선택할 때만

본 앱이 데이터를 사용자 PC 밖으로 보내는 경우는 **다음 두 가지뿐이며, 모두 사용자의 선택으로만** 발생합니다.

### 3.1 선택한 AI 공급자 (리밸런싱 제안 요청 시)

사용자가 "포트폴리오 제안 받기"를 누르면, 입력한 포트폴리오 정보(적립금·보유 상품·수익률·가입자 연령 등)와 상품 카탈로그가 **사용자가 선택한 AI 공급자**(Anthropic Claude / Google Gemini / OpenAI GPT)의 API로 전송되어 분석됩니다. 이 데이터는 해당 공급자의 개인정보 처리방침에 따라 처리됩니다. 본 앱 개발자는 이 전송 내용을 볼 수 없습니다. 전송은 사용자가 등록한 API Key로 이루어집니다.

### 3.2 사용자 본인의 Google Drive (클라우드 동기화를 켠 경우)

사용자가 환경 설정에서 "Google 계정 연동"을 켜면, 위 로컬 데이터(`account.json`, `catalog.json`, 이력, 그리고 선택 시 API Key)가 **사용자 본인의 Google Drive 안 숨겨진 앱 전용 폴더(`drive.appdata`)** 에 보관되어 다른 PC와 자동으로 공유됩니다.

- 본 앱은 **`https://www.googleapis.com/auth/drive.appdata` 권한 하나만** 요청합니다. 이는 본 앱이 만든 숨겨진 폴더에만 접근하는 권한으로, **사용자 Drive의 다른 어떤 파일도 보거나 수정할 수 없습니다.**
- 이 폴더는 Google Drive 웹 UI에 표시되지 않으며, 본 앱의 OAuth 클라이언트 ID + 본인 Google 계정 조합에서만 접근 가능합니다.
- **API Key를 클라우드에 동기화하는 경우, 키는 사용자가 설정한 "동기화 암호구문"으로 암호화(PBKDF2-SHA256 + AES-256-GCM)된 뒤에만 업로드됩니다.** 암호구문은 각 PC의 로컬 자격증명 보관소에만 저장되고 클라우드로 전송되지 않으므로, 설령 Google 계정이 노출되더라도 키 자체는 보호됩니다.
- 데이터는 사용자의 Google 계정에 저장되며 Google의 개인정보 처리방침이 적용됩니다. 개발자는 이 데이터에 접근할 수 없습니다.

본 앱은 Google API로 받은 정보를 위에 명시된 동기화 목적 외의 어떤 용도로도 사용·전송하지 않으며, 제3자에게 제공하지 않습니다. 본 앱의 Google 사용자 데이터 이용은 [Google API 서비스 사용자 데이터 정책](https://developers.google.com/terms/api-services-user-data-policy)(제한적 사용 요건 포함)을 준수합니다.

## 4. 데이터 삭제 / 연결 해제

- **로컬 데이터:** 각 화면의 초기화 버튼으로 계좌·카탈로그·이력을 개별 삭제할 수 있습니다. 앱을 제거하면 로컬 데이터도 함께 제거됩니다.
- **클라우드 데이터:** 환경 설정에서 "연결 해제"를 누르면 이 PC의 클라우드 캐시·동기화 암호구문이 즉시 삭제됩니다. Google 계정에 저장된 데이터까지 지우려면 [Google 계정 권한 페이지](https://myaccount.google.com/permissions)에서 본 앱의 접근을 철회하시면 됩니다.

## 5. 아동 개인정보

본 앱은 만 14세 미만 아동을 대상으로 하지 않으며, 아동의 정보를 의도적으로 수집하지 않습니다.

## 6. 변경 고지

본 처리방침이 변경되면 본 문서의 "최종 업데이트" 날짜가 갱신됩니다.

## 7. 문의

본 앱 관련 문의나 개인정보 관련 요청은 GitHub 저장소의 이슈로 남겨 주세요: <https://github.com/chhbang/PensionCompass/issues>

---

## English Summary

**PensionCompass** is a personal, single-user Windows desktop tool for rebalancing a Samsung Life IRP (Korean individual retirement pension) account. It is an unofficial third-party tool, not affiliated with Samsung Life.

- **The developer collects nothing.** No analytics, no telemetry, no developer-operated server. All data you enter stays on your own PC by default.
- **Data leaves your PC only when you choose:** (a) when you request a recommendation, your portfolio data is sent to **your chosen AI provider** (Claude/Gemini/GPT) via your own API key; (b) if you enable cloud sync, your data is stored in **your own Google Drive's hidden app folder** (`drive.appdata`).
- **Google access is limited to the `drive.appdata` scope** — the app can only touch its own hidden folder and cannot see or modify any other file in your Drive.
- **API keys synced to the cloud are encrypted** (PBKDF2-SHA256 + AES-256-GCM) with a passphrase that never leaves your PC.
- The app's use of Google user data complies with the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.
- **Deletion:** reset data in-app, disconnect to wipe the local cloud cache, and revoke access at <https://myaccount.google.com/permissions>.
- **Contact:** <https://github.com/chhbang/PensionCompass/issues>
