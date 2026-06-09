# Google OAuth 브랜딩/검증 정리 — "다른 사람도 쓸 가능성" 대비

> 목적: 본인 외 사용자가 본 앱으로 Google 로그인을 할 때 신뢰할 수 있는 동의 화면을 보이도록 OAuth 브랜딩을 정리한다. 이미 2026-06-03 프로덕션 게시는 완료된 상태(메모리 `gcp-oauth-published` 참조).

## 0. 먼저: 우리는 "정식 검증(verification)"이 필요 없다

본 앱이 요청하는 OAuth 권한은 **`https://www.googleapis.com/auth/drive.appdata` 하나뿐이고, 이는 non-sensitive(민감하지 않은) scope**다. 따라서:

- **보안 평가(security assessment)·데모 영상·CASA 심사 같은 무거운 검증 절차는 적용되지 않는다.** 이건 sensitive/restricted scope를 쓸 때만 요구된다.
- 흔히 말하는 풀스크린 **"Google에서 확인하지 않은 앱입니다(unverified app)" 경고는 주로 sensitive/restricted scope 미검증 앱에서 뜬다.** non-sensitive scope만 쓰는 게시된 앱은 보통 이 인터스티셜 없이 일반 동의 화면이 바로 나온다.

→ 즉, **이미 게시된 현재 상태로 다른 사람이 로그인해도 대체로 문제없이 동작**할 가능성이 높다. 아래 작업은 "필수 검증"이 아니라 **동의 화면을 더 신뢰감 있게 만들고, 혹시 모를 경고/추후 sensitive scope 도입에 대비**하는 정리 작업이다.

## 1. 지금 해두면 좋은 것 — Branding 탭 채우기

GCP Console → **Google 인증 플랫폼(APIs & Services → OAuth) → 브랜딩** 탭에서:

| 필드 | 입력값 | 비고 |
| --- | --- | --- |
| 앱 이름 | `연금나침반` (또는 PensionCompass) | 동의 화면에 표시 |
| 사용자 지원 이메일 | 본인 Gmail(saewoom0801@gmail.com) | 동의 화면 하단 표시 |
| 앱 로고 | `PensionCompass/Assets`의 아이콘(120×120 PNG) | 선택. 로고를 넣으면 검증이 트리거될 수 있음(아래 §3) |
| 앱 홈페이지 | `https://github.com/chhbang/PensionCompass` | README가 홈페이지 역할 |
| 개인정보처리방침 URL | `https://github.com/chhbang/PensionCompass/blob/main/PRIVACY.md` | 본 작업에서 추가한 [PRIVACY.md](../PRIVACY.md) |
| 서비스 약관 URL | (선택) 비워도 됨 | |
| 승인된 도메인(Authorized domains) | `github.com` | 위 URL들의 도메인 |

> **개인정보처리방침은 이번에 리포에 추가했다.** GitHub에서 바로 렌더링되는 URL이므로 그대로 붙여 넣으면 된다. 더 깔끔한 URL을 원하면 §2의 GitHub Pages를 쓴다.

## 2. (선택) 더 깔끔한 URL — GitHub Pages

`github.com/.../blob/main/PRIVACY.md` 대신 `https://chhbang.github.io/PensionCompass/privacy` 같은 URL을 원하거나, 도메인 소유권을 직접 인증하고 싶다면:

1. GitHub 저장소 → **Settings → Pages** → Source를 `Deploy from a branch`, 브랜치 `main` / 폴더 `/docs`(또는 `/root`)로 설정.
2. `docs/` 아래에 `index.md`(홈페이지)와 `privacy.md`를 두면 `https://chhbang.github.io/PensionCompass/` 와 `.../privacy`로 게시된다.
3. 도메인 소유권 인증이 필요하면 [Google Search Console](https://search.google.com/search-console)에서 `chhbang.github.io`를 추가·인증한 뒤, GCP Branding의 "승인된 도메인"에 `chhbang.github.io`를 넣는다.

> non-sensitive scope만 쓰는 현재로서는 GitHub의 기본 URL(`github.com`)로도 충분하다. Pages는 어디까지나 가독성/도메인 인증을 원할 때의 선택지다.

## 3. 로고를 넣으면 생길 수 있는 일

동의 화면에 **커스텀 로고**를 표시하려면 Google이 브랜드 검증(상표/스푸핑 확인 수준의 가벼운 검토)을 요구할 수 있다. 이건 sensitive-scope 보안 평가와는 다른, 비교적 가벼운 절차다. 1인/소수 사용 도구라면:

- 로고 없이(기본 아이콘) 두면 검증 트리거 없이 앱 이름만 깔끔하게 표시된다 — **가장 마찰 없는 선택.**
- 로고를 꼭 넣고 싶으면 Branding에서 제출 후 며칠 기다린다.

## 4. 그래도 경고가 보일 때의 임시 우회

혹시 다른 사람 PC에서 "확인되지 않은 앱" 화면이 뜨면(정책 변동 등):

1. 화면 하단 **"고급(Advanced)"** 클릭
2. **"<앱이름>(으)로 이동(안전하지 않음)"** 클릭
3. 정상 동의 화면으로 진행

본인/지인이 본인 도구를 쓰는 상황이므로 안전하다. 다만 일반 배포까지 고려하면 §1을 채워 두는 게 신뢰도에 좋다.

## 5. 결론(권장 액션)

1. **[필수 아님, 권장]** Branding 탭에 앱 이름·지원 이메일·홈페이지(README)·개인정보처리방침(PRIVACY.md)·승인 도메인(`github.com`) 채우기 — 동의 화면 신뢰도 ↑, 향후 대비.
2. **[선택]** 로고는 마찰을 피하려면 생략. 넣으려면 검증 제출.
3. **[선택]** 더 깔끔한 URL/도메인 인증이 필요하면 GitHub Pages + Search Console.
4. 정식 보안 검증은 **현재 scope(drive.appdata)에서는 불필요**하다. 추후 사용자 Drive의 일반 파일 접근(`drive.file`/`drive` 등 sensitive scope)을 도입하면 그때 검증 절차를 다시 검토한다.
