# 신규 기능 계획 — 운용사 가이드북 PDF를 추천 참고자료로 첨부

> 상태: **계획 수립 완료, 구현 대기.** 사용자 결정: A로 분류된 보안 기능(API 키 암호화, OAuth 브랜딩 검증)을 먼저 구현한 뒤 본 기능을 개발. 잠정 버전 marker: **v1.4.0** (A 기능이 v1.3.0이 된다고 가정).

## 1. 동기 — 사용자 아이디어

자산운용사들이 공개하는 연금투자 가이드북(예: `미래에셋 연금투자 가이드북_웹용 최종.pdf`, reference 폴더, 약 19 MB)을 AI 추천 시 컨텍스트로 첨부하면, 한국 연금시장 특화 지식(IRP 70/30 규제, 안정/위험자산 분류, 생애주기 자산배분, 환헤지 펀드 특성 등)을 반영한 더 정교한 리밸런싱 제안을 받을 수 있다.

사용자 핵심 아이디어 2가지:
1. 가이드북 PDF를 추천 프롬프트에 참고자료로 첨부
2. 이 PDF를 **Google 계정(drive.appdata)에 연동**하여 다른 PC에서도 자동으로 따라오게 (파일 교체는 사용자가 직접)

## 2. 기술 타당성 (2026-06 기준)

세 공급자 모두 PDF 네이티브 입력 지원. 19 MB는 모두 한도 이내.

| 공급자 | PDF 처리 방식 | 단일 PDF 한도 | 비고 |
|---|---|---|---|
| Anthropic (Claude) | `document` content block (base64 또는 Files API `file_id`) | 32 MB / ~100페이지 | 페이지를 텍스트+이미지로 동시 분석, 표·차트 이해 우수 |
| Gemini | `inline_data` 또는 File API | 2 GB | 2M 컨텍스트 — 여러 PDF 동시 첨부도 여유 |
| OpenAI (GPT) | Files API → `file_id`, 또는 vision 페이지 이미지 | 25 MB (Files API) | 19 MB는 빠듯, 더 큰 PDF는 분할 필요 가능 |

1회 호출 추가 비용(100페이지 가정): Claude Opus ~\$0.5–1.0, Gemini ~\$0.3, GPT ~\$0.4. 본 앱은 분기 1회 사용이라 부담 미미.

## 3. 품질 기대 효과와 핵심 리스크

**기대 효과 (중–상):**
- 한국 연금시장 특수성을 AI가 정확히 반영
- 운용사가 정제한 한국형 자산배분 가이드라인 직접 참조
- 학습 데이터로 본 적 있더라도, 사용자 컨텍스트로 명시되면 우선 적용

**핵심 리스크 — 운용사 편향 (반드시 가드레일 필요):**
- 미래에셋 가이드북 → 미래에셋 펀드를 미묘하게 우호적으로 그릴 가능성
- **방어 1 (필수):** 프롬프트에 명시 — "첨부 가이드북은 일반 원칙·시장 상황 이해용으로만 활용하고, 특정 운용사 상품을 추천하거나 배제하지 말 것. 운용사 선택은 사용자가 제공한 카탈로그의 수익률·보수율·자산구분만으로 판단할 것."
- **방어 2 (권장):** 여러 운용사 PDF 동시 첨부 지원 → 단일 운용사 편향 분산
- **방어 3:** PDF 첨부 시 결과 화면/PDF에 "본 추천은 첨부 가이드북(N개)을 참고했습니다" 출처 표기

## 3.5. 자료 분류(카테고리) — 2026-06-09 추가 요구

사용자가 두 번째 PDF(대신증권 월간 리포트 `57723_DaishinTV_0611.pdf`, 3.7 MB)를 추가하면서, **자료 성격이 다르므로 분류해서 등록하고 그 분류를 LLM에 명시**해 달라고 요청. 같은 "참고 PDF"라도 용도가 다름:

| 카테고리 | 예시 | LLM에 전달할 활용 지침(framing) |
|---|---|---|
| **펀드/ETF 가이드(`FundGuide`)** | 미래에셋 연금투자 가이드북 | "특정 펀드·ETF의 성격/분류/장기 활용법 이해용. 단, 특정 운용사 상품을 편애·배제하지 말 것 — 상품 선택은 카탈로그 수치로." |
| **시장 분석 리포트(`MarketReport`)** | 대신증권 월간 리포트 | "현재 거시·시장 환경과 자산군 전망 이해용. 시점 의존 정보이니 발행일을 감안하고, 개별 종목 추천이 아니라 환경 판단의 보조 자료로만." |
| **기타(`Other`)** | 그 외 | "일반 참고 자료. 본 IRP 리밸런싱 맥락에서 적절히 취사선택." |

각 첨부 PDF는 카테고리를 함께 등록하고, 프롬프트의 "## 참고 자료" 섹션에 **파일명 + 카테고리 + 카테고리별 활용 지침**을 명시한다. (대부분의 LLM은 내용을 보고 판단하지만, 분류를 명시하면 용도 혼동/편향을 줄인다.)

## 3.6. 공급자별 PDF 입력 제약 (구현 단계에서 확정)

| 공급자 | 입력 방식 | 한도 / 주의 |
|---|---|---|
| Anthropic | `messages[].content`에 base64 `document` 블록 | 32 MB / ~100p. **base64는 원본의 약 1.33배**라 19 MB 원본 ≈ 25 MB 인코딩 |
| OpenAI | `messages[].content`에 `file`(`file_data` data URL) 블록 | 요청 크기 한도 내 inline. 19 MB는 빠듯 → 가드 필요 |
| **Gemini** | **File API 업로드 → `file_data`(file_uri) 참조** | inline(`inline_data`)은 **요청 전체 20 MB** 한도라 19 MB 원본(base64 ≈ 25 MB) 단일 파일도 초과 → **attachments가 있으면 항상 File API 사용**(업로드→ACTIVE 폴링→참조). 크기 무관 |

→ 구현: Anthropic/OpenAI는 inline base64 + 공급자별 총량 가드(초과 시 명확한 한국어 오류). Gemini는 attachments가 있으면 File API 경유. 사용자 주 공급자가 Gemini이고 주력 파일이 19 MB이므로 File API는 필수.

## 4. 결정 사항 (사용자 승인 완료)

| # | 항목 | 결정 |
|---|---|---|
| D1 | 첨부 기능 자체 | **구현** — 한국 시장 특화 품질 향상 효과 큼 |
| D2 | 단일/다중 PDF | **다중 (2~5개)** — 운용사 편향 분산에 결정적 |
| D3 | 저장 위치 | 로컬(`LocalState/References/`) + **drive.appdata 동기는 문서별 opt-in**(기본 꺼짐). PDF는 용량이 커서 Google 계정 저장공간을 쓰므로, 각 자료의 "클라우드" 체크박스로 켤 때만 업로드. 다른 PC는 `<id>.json` 사이드카 메타 + `<id>.pdf`로 자동 내려받기. 업로드는 백그라운드 best-effort(쿼터 부족 시 다른 PC에 안 나타날 뿐 로컬은 안전) |
| D4 | 편향 가드레일 | **프롬프트에 명시 필수** |
| D5 | 버전 | **A 기능(v1.3.0) 다음, v1.4.0** |
| D6 | 분류 | **카테고리(FundGuide/MarketReport/Other) 등록 + LLM에 명시** (2026-06-09 요구) |
| D7 | 공급자 | Anthropic/OpenAI inline + **Gemini File API** |

## 5. 아키텍처 설계

### 5.1 자료 저장소 — 기존 인프라 재활용

v1.1.0 `ISyncProvider`(drive.appdata) + v1.2.0 `ApiKeySyncService` 패턴을 그대로 따른다.

| 구성 요소 | 신규 | 재활용 |
|---|---|---|
| 로컬 저장 | `LocalState/References/<id>.pdf` + `references.json`(메타: id, 파일명, 크기, 추가일, enabled) | `StateStore` 패턴 |
| 클라우드 저장 | `appdata://References/<id>.pdf` + `appdata://references.json` | `ISyncProvider`, drive.appdata 매핑 |
| 신규 서비스 | `ReferenceLibraryService` (Core) — 목록/추가/삭제/동기 | `ApiKeySyncService` 구조 |
| Settings/전용 화면 UI | "참고 자료(PDF)" 카드 — 추가/삭제/목록/활성 토글 | 기존 카드 디자인 |
| AI 호출 | 각 `IAiClient`에 `IReadOnlyList<DocumentAttachment>` 인자 추가 + 공급자별 PDF block 변환 | `IAiClient` 인터페이스 |
| 프롬프트 | 첨부 직전 가드레일 문장 + 출처 안내 | `PromptBuilder` |

> 주의: drive.appdata 한도는 1 GB. 19 MB PDF 기준 약 50개까지 가능하나 실제 2~5개면 충분. 동기 대역폭을 고려해 큰 PDF는 업로드 진행률 표시.

### 5.2 IAiClient 확장

```csharp
public sealed record DocumentAttachment(string FileName, string MediaType, byte[] Content);

Task<string> GetRebalanceProposalAsync(
    string prompt,
    IReadOnlyList<DocumentAttachment> attachments,   // 신규, 비어 있으면 기존 동작
    ThinkingLevel thinking,
    CancellationToken ct);
```

공급자별 변환:
- Anthropic: `messages[].content`에 `{type:"document", source:{type:"base64", media_type:"application/pdf", data:...}}` block 추가
- Gemini: `contents[].parts`에 `{inline_data:{mime_type:"application/pdf", data:...}}` (>20 MB는 File API 경유)
- OpenAI: Files API 업로드 → `file_id` 참조 (responses/assistants 경로) 또는 페이지 이미지 vision

기존 호출부는 빈 리스트를 넘기면 동작 불변 → 점진적 도입 가능.

### 5.3 비용·UX 가드

- PDF 첨부는 토큰·비용을 크게 늘리므로 **명시적 체크박스**("이번 추천에 참고 자료 PDF 포함") + 추정 비용/페이지 수 캡션
- 활성 PDF가 공급자 한도를 초과하면 사전 경고 (특히 OpenAI 25 MB)
- 프롬프트 캐싱 가능 공급자(Anthropic)는 PDF block에 `cache_control`을 붙여 반복 호출 비용 절감 검토

## 6. 구현 마일스톤 (대략, 단일 v1.4.0)

| # | 범위 | 노력 |
|---|---|---|
| 4.0a | `ReferenceLibraryService` + `references.json` 모델 + 로컬 저장 + 단위 테스트 | M |
| 4.0b | drive.appdata 동기(업로드 진행률 포함) + 다른 PC 자동 다운로드 | M |
| 4.0c | `IAiClient`에 `DocumentAttachment` 인자 추가 + 3개 공급자 변환 구현 | L |
| 4.0d | `PromptBuilder` 편향 가드레일 + 출처 표기 | S |
| 4.0e | UI(참고 자료 카드 + 추천 시 포함 체크박스 + 비용/한도 경고) | M |
| 4.0f | 끝단 테스트 + 릴리스 노트 + v1.4.0 | S |

## 7. 본 회차 비-목표

- PDF 텍스트 추출/요약 사전처리(RAG) — 첫 회차는 원본 PDF 직접 첨부로 단순화. 토큰 비용이 문제되면 후속에서 청크/임베딩 검토.
- 운용사별 자동 큐레이션(앱이 가이드북을 자동 수집) — 사용자가 직접 추가
- 모바일

## 8. 다음 단계

1. (선행) A 기능(API 키 암호화 + OAuth 브랜딩) 완료 및 v1.3.0
2. 4.0a부터 단계별 구현 (Auto Mode)
3. 미래에셋 가이드북으로 첨부 유/무 추천 결과 A/B 비교 → 품질 향상 실측
4. 마일스톤 완료마다 짧은 verification 보고
5. v1.4.0 release
