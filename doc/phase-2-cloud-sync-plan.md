# Phase 2 — 클라우드 직접 연동 (앱 내 로그인 방식) 계획

## 1. 문제와 동기

현재 v1.0.7의 동기화는 "사용자가 본인의 클라우드 클라이언트(Google Drive 데스크톱 / OneDrive / Dropbox)를 미리 설치·구성해 놓고, 그 폴더 경로를 앱 설정에 적어 넣는" 방식. **사용자 본인이 v1.0.5 셋업 도중 모드 A(My Drive 미러)/모드 B(폴더 백업) 혼동을 겪을 만큼** UX가 좋지 않음. 다른 PC에서 다시 설정해야 하는 마찰도 큼. 추후 모바일 앱을 추가할 때는 이 방식으로 더 이상 확장 불가.

**목표:** 사용자가 앱 안에서 한 번 로그인하면 PC가 바뀌어도(향후 모바일에서도) 자동으로 데이터가 따라오게.

## 2. 결정 기준

1. **마찰 최소화** — "동기화 폴더 경로" 같은 개념 자체를 사용자 시야에서 제거
2. **한국 사용자 적합도** — 가입자 본인은 Gmail 사용자
3. **모바일 확장성** — 향후 iOS/Android 앱이 같은 데이터에 접근 가능해야 함
4. **앱 데이터 격리** — 사용자가 자기 클라우드 드라이브 메인 영역에서 앱 JSON 파일을 우연히 보거나 삭제하지 않게
5. **개발 ROI** — 솔로 개발자 워크로드, 첫 회차에 1개 공급자만 깊게 구현

## 3. 공급자 비교

| 공급자 | 한국 보급률 | Windows 자연스러움 | 모바일 확장 | API 단순도 | 무료 한도 | OAuth on WinUI |
|---|---|---|---|---|---|---|
| **Google Drive** | ★★★ Gmail 보편 | ★★ 별도 SDK | ★★★ Android·iOS·웹 모두 | ★★ Drive API REST | 15 GB / 앱 데이터는 별도 1 GB | PKCE 로컬 루프백 |
| OneDrive (Graph) | ★★ M365 가입자만 | ★★★ Windows 통합 | ★★★ MSAL 라이브러리 | ★★ Graph API | 5 GB | 비교적 쉬움 (MSAL) |
| Dropbox | ★ 한국 보급 낮음 | ★★ | ★★★ | ★★★ 가장 단순 | 2 GB | 표준 OAuth 흐름 |
| iCloud | — | × Apple 전용 | Apple 한정 | — | — | 부적합 |

## 4. 추천 — **Google Drive (`drive.appdata` scope)**

**근거:**

1. 사용자 본인 Gmail 보유 사실상 확정. 별도 가입 마찰 0
2. **`drive.appdata` scope** — 사용자의 My Drive UI에선 안 보이는 **숨겨진 앱 전용 폴더**에 저장. 가입자 잔고 정보가 사용자 Drive 메인에 안 보여서 안전, 실수 삭제 위험 0
3. `drive.appdata`는 1GB 한도지만 본 앱 데이터는 평생 KB~MB 스케일이라 이슈 없음
4. 모바일 앱 확장 시 같은 Google 계정으로 같은 appdata 폴더 접근 가능 (iOS·Android 모두)
5. 기존 v1.0.5에서 "Google Drive 데스크톱" 셋업 경험이 있어 사용자에게 "Google = 클라우드"라는 멘탈 모델 이미 형성됨

**선택 안 한 이유:**

- **OneDrive**: M365 가입자가 아니면 Windows 로컬 계정 사용자에게도 OneDrive 가입을 요구하게 됨. Korean 가정용 PC는 Google 우세
- **Dropbox**: 한국 사용자 적은 편, 무료 2GB만으로도 본 앱 용도엔 충분하나 가입자 추가 마찰
- **iCloud**: Windows 비호환

## 5. 아키텍처 방향 — 공급자 추상화 + 클라우드-우선 동기

### 5.1 공급자 인터페이스

기존 [StateStore.cs](PensionCompass/Services/StateStore.cs)의 폴더 경로 직접 호출을 **공급자 인터페이스 뒤로 숨김**:

```csharp
public interface ICloudSyncProvider
{
    bool IsConfigured { get; }
    Task<DateTime?> GetRemoteModifiedTimeAsync(string fileName, CancellationToken ct);
    Task<Stream?> ReadAsync(string fileName, CancellationToken ct);
    Task WriteAsync(string fileName, Stream content, CancellationToken ct);
    Task DeleteAsync(string fileName, CancellationToken ct);
    Task<IReadOnlyList<string>> ListAsync(string subfolder, CancellationToken ct);
}
```

구현체:
- `GoogleDriveSyncProvider` — 본 회차 핵심 (OAuth + appdata REST)
- `FilesystemFolderSyncProvider` — 폐기 예정. v1.1.0에선 마이그레이션 도우미로만 잠시 잔존, v1.2.0 이후 제거
- (장래) `OneDriveSyncProvider`, `DropboxSyncProvider`

### 5.2 동기 모델 — 클라우드 우선 (Cloud-as-source-of-truth)

**사용자 결정 반영**: 폴더 동기 시절의 "로컬 + 폴더 mtime 비교" 모델 폐기. 일단 로그인하면 **클라우드가 진실의 원천**. 로컬은 캐시 + 오프라인 버퍼 역할.

3가지 상태 머신:

| 상태 | 트리거 | 읽기 | 쓰기 |
|---|---|---|---|
| `Disconnected` | 동기화 미설정 | LocalState 파일 | LocalState 파일 |
| `CloudAuthoritative` | 로그인 + 네트워크 OK | 로컬 캐시 (백그라운드 sync로 fresh) | 클라우드 → 로컬 캐시 |
| `OfflineCache` | 로그인됐지만 네트워크 단절 | 로컬 캐시 | 로컬 큐 + 다음 연결 시 flush |

전이:
- `Disconnected` → `CloudAuthoritative`: 사용자가 Google 연결. 로컬에 데이터가 있으면 **업로드 마이그레이션** 후 클라우드 truth로 전환
- `CloudAuthoritative` ↔ `OfflineCache`: 네트워크 단절·복구
- `CloudAuthoritative` → `Disconnected`: 사용자가 연결 해제 시 "클라우드에 두고 갈까요 / 로컬로 가져올까요?" 선택

### 5.3 충돌 해소

**KISS — last-write-wins via `modifiedTime`** (Google Drive 메타데이터). 본 앱 사용 패턴(분기 1회, 사실상 1인)에선 충돌 매우 드물어 fancy 병합 불필요. 추후 conflict UI는 P3로 미룸.

### 5.4 마이그레이션 — 첫 로그인 시 자동 업로드

사용자 결정 반영: "기존 PC에서 사용하던 데이터를 클라우드로 전송하는 기능도 필요". 흐름:

1. 사용자가 환경 설정 → "Google 연결" 클릭
2. OAuth 완료
3. 앱이 Drive `appdata` 영역의 파일 목록 조회
   - **비어 있으면**: 로컬 LocalState (그리고 v1.0.x 사용자라면 기존 `SyncFolder` 위치도) 의 데이터를 모두 한 번에 업로드. 사용자에겐 "기존 데이터를 클라우드에 옮겼습니다 (N개 파일)" 간단 토스트
   - **이미 데이터가 있으면**: 로컬과 어느 쪽이 더 새로운지 비교해서 더 새로운 쪽을 양쪽에 맞춤
4. 그 시점부터 `CloudAuthoritative` 상태로 운영

기존 v1.0.x `SyncFolder` 설정은 이 마이그레이션 직후 자동 비움 (사용자에게 "더 이상 필요 없음" 안내).

## 5.5 API Key 클라우드 동기화 (사용자 제기 항목)

**사용자 결정 반영**: API Key도 결국 클라우드로 가야 (PC 옮길 때마다 재입력 마찰). 단, 암호화 구현은 어려우니 **v1.1.0에선 평문 저장 허용**.

**보안 분석 — 평문 저장이 허용 가능한 이유:**

| 위협 | 영향 | 방어 |
|---|---|---|
| `drive.appdata`에서 다른 앱이 우연히 읽음 | 발생 불가 | `appdata`는 OAuth client ID 단위로 격리. 다른 앱은 본 앱의 appdata 못 봄 |
| 사용자 자신이 Drive 웹에서 열어봄 | 발생 불가 | `appdata` 폴더는 Drive UI에 안 보임 |
| 전송 중 가로채기 | 무효화 | Google API는 HTTPS 강제 |
| Google 측 데이터 유출 | 매우 낮음 | Google이 보관 시 자체 암호화 |
| 사용자 Google 계정 자체 탈취 | 가능 | 이 경우 API Key뿐 아니라 더 큰 문제 발생. Google 2FA 사용 권장 |

위협 대비 ROI 측면에서 평문 OK. 단 다음 두 가지 보호장치 권장:

1. **opt-in 체크박스로**: 환경 설정에 "Google 연결" 카드와 별도로 "API 키도 클라우드 동기화 (다른 내 PC에 자동으로 동기됨)" 체크박스. **default는 off** — 공용/공유 PC에 무심코 사인하더라도 API 키가 따라가지 않게 보호
2. **명시적 라벨**: 체크박스 옆 "주의: API 키는 평문으로 저장됩니다 (drive.appdata — 본인 Google 계정에서만 접근)"

저장 위치:
- 로컬: 기존대로 `Windows.Security.Credentials.PasswordVault`
- 클라우드 (opt-in): `appdata://api-keys.json` (단순 JSON, 공급자별 키 매핑)

향후 v1.2.0+ 후보: passphrase 기반 클라이언트-사이드 암호화 추가 (앱 시작 시 1회 입력, 클라우드엔 암호문 저장). 본 회차에선 비-목표.

## 6. Google Drive 구현 세부 설계

### 6.1 OAuth 흐름

WinUI 3 데스크톱 앱은 웹뷰나 외부 브라우저 + 로컬 루프백 리다이렉트를 사용:

1. 앱이 임시 HTTP 리스너 시작 (`http://localhost:<random_port>/oauth-callback`)
2. 사용자 기본 브라우저로 Google OAuth URL 열기 (PKCE 포함)
3. 사용자가 Google에 로그인 + 권한 승인
4. Google이 `localhost:<port>/oauth-callback?code=...`로 리다이렉트
5. 로컬 리스너가 코드 받아서 토큰 교환

**대안:** AppInstance custom URI handler (`pensioncompass://oauth-callback`). MSIX 패키지에 등록 가능. 다만 첫 구현은 루프백이 더 간단.

### 6.2 토큰 저장

- Access Token + Refresh Token → `Windows.Security.Credentials.PasswordVault`
  - resource: `"PensionCompass.GoogleDrive"`
  - userName: 사용자 이메일 (또는 Google sub claim)
- 기존 API Key 보관 패턴 그대로 사용

### 6.3 권한 범위

- `https://www.googleapis.com/auth/drive.appdata` 만 요청
- 사용자 본인 Drive의 다른 어떤 파일도 못 봄 — privacy-preserving by design
- "이 앱이 사용자의 모든 Drive 파일에 접근 요청" 같은 무서운 권한 화면이 안 뜸

### 6.4 파일 매핑

| 로컬 파일 | Google Drive 위치 |
|---|---|
| `account.json` | appdata://account.json |
| `catalog.json` | appdata://catalog.json |
| `History/<ts>_<provider>.json` | appdata://History/<ts>_<provider>.json |

(Drive API는 진짜 폴더가 없음 — `parents` 메타데이터로 가상 계층 구성)

### 6.5 충돌 해소

기존 mtime-newer-wins를 그대로 유지. Drive API의 `files.get(fields=modifiedTime)` 사용. **첫 회차에선 fancy CRDT/병합 안 함** — 단일 사용자 다중 디바이스 시나리오에서 mtime은 충분.

미래 작업: 만약 두 PC에서 동시에 입력한 결과가 충돌하면 사용자에게 "PC A: 2026-05-10 13:45 / PC B: 2026-05-10 13:46 — 어느 쪽을 사용?" 다이얼로그.

## 7. UX 흐름 (목표)

**최초 PC 셋업 (또는 v1.0.x → v1.1.0 업그레이드 후 첫 실행):**

1. 환경 설정 → "클라우드 동기화" 섹션
2. 토글 또는 큰 버튼: "**Google 계정 연결**" (default 비활성)
3. 클릭 → 기본 브라우저 열림 → Google 로그인 → 권한 승인 (`drive.appdata`만 요청)
4. 앱 화면에 "✓ gildong@gmail.com 연결됨" 표시
5. 백그라운드: 클라우드 appdata 검사 후
   - 비어 있고 로컬에 데이터 있음 → **자동 업로드 마이그레이션** + 토스트 "기존 데이터를 클라우드로 옮겼습니다"
   - 클라우드에 데이터 있음 → 다운로드해서 로컬 캐시 동기화
6. (옵션) 별도 체크박스: "API 키도 함께 클라우드 동기화 (다른 내 PC에 자동 동기됨)" — default OFF

**다른 PC에서:**

1. 앱 설치 → 환경 설정에서 같은 절차로 Google 연결
2. 클라우드 appdata에 이미 데이터가 있으니 자동으로 끌어옴
3. 끝. 폴더 경로 같은 건 묻지 않음. 사용자가 §6 체크박스 켰다면 API 키도 같이 들어옴

**연결 해제 시:**

1. "Google 연결 해제" 클릭
2. ContentDialog: "클라우드의 데이터를 어떻게 할까요?"
   - "로컬에 복사 후 클라우드 연결만 해제" (default)
   - "클라우드 데이터도 모두 삭제 후 해제" (드문 경우)
   - "취소"

## 8. 구현 마일스톤 (대략 5 PR, 단일 v1.1.0으로 묶음)

| # | 범위 | 노력 |
|---|---|---|
| 2.0a | `ICloudSyncProvider` 추상화 도입 + `StateStore` 리팩토링하여 인터페이스 뒤로 옮김 + 테스트 | M |
| 2.0b | `GoogleDriveSyncProvider` — OAuth PKCE 루프백, 토큰 저장 (PasswordVault), REST 클라이언트, refresh 처리 | L |
| 2.0c | `CloudFirstStateStore` — 상태 머신 (Disconnected / CloudAuthoritative / OfflineCache), 첫 로그인 마이그레이션 로직 | L |
| 2.0d | Settings UI — Google 연결 카드 + 상태 표시 + opt-in API 키 동기 체크박스 + 연결 해제 흐름 | M |
| 2.0e | 끝-단 테스트 + 릴리스 노트 + v1.1.0 릴리스 | S |

## 9. 결정 사항 (사용자 승인 완료)

| # | 결정 |
|---|---|
| Q1 OAuth 경고 처리 | **테스트 사용자 모드** — Cloud Console에서 본인 Gmail을 test user로 등록 (절차는 [doc/google-cloud-console-setup.md](google-cloud-console-setup.md) 참조) |
| Q2 동기화 모델 | **클라우드 우선 (cloud-as-source-of-truth)** — 로그인 후엔 클라우드 미러 기준 |
| Q3 마이그레이션 | **첫 로그인 시 자동 업로드** — 기존 PC 로컬 데이터를 사용자 의도에 따라 한 번에 클라우드로 이전 |
| Q4 모바일 범위 | **read-only 동반 앱** — Phase 3 미래 작업, 본 회차 비-목표 |
| Q5 버전 | **v1.1.0** (minor bump — 동기화 메커니즘 변경은 마이너 변경 |
| Q6 API 키 클라우드 동기 (사용자 추가) | **opt-in 평문 저장** — `drive.appdata`에 평문 JSON, default OFF 체크박스로 보호. 암호화는 v1.2.0+ 후보 |

### Q7 (남은 항목). 토큰 만료·갱신·취소 처리

본 항목은 사용자 결정 불필요한 구현 디테일:

- Google OAuth refresh token: 6개월 미사용 시 만료 가능, 사용자가 Google 보안 페이지에서 취소 가능
- 만료 감지 시: "Google 연결이 만료되었습니다. 다시 로그인해 주세요" 카드 + 자동 재인증 버튼
- 본 회차 필수 구현

## 10. 본 v1.1.0의 명시적 비-목표

다음은 **이번 회차에서 안 함**:

- OneDrive·Dropbox 동시 지원 (인터페이스만 추상화, 다른 구현체는 미래)
- 모바일 앱 (Phase 3)
- API 키 클라우드 측 암호화 (v1.2.0+ passphrase 옵션 후보)
- 자동 백업·복원·버전 히스토리 (Google Drive 자체 기능에 위임)
- 다중 사용자 공유 (이 앱은 본질적으로 단일 사용자)
- 동시 편집 충돌 UI (last-write-wins로 충분, conflict diff UI는 미래)

## 11. 다음 단계

1. **(사용자 작업)** [doc/google-cloud-console-setup.md](google-cloud-console-setup.md) 따라 Google Cloud Console에서 OAuth 클라이언트 ID 발급 + 본인 Gmail을 test user로 등록
2. 사용자가 발급된 client_id (와 client_secret)를 알려주면 → 그 값을 코드에 어떻게 임베드할지 (소스 임베드 vs 별도 secrets 파일 vs 빌드 시 주입) 미니 결정
3. 위 끝나면 마일스톤 2.0a부터 단계별 코딩 시작 (Auto Mode 유지)
4. 각 마일스톤 완료마다 짧은 verification 결과 보고
5. 모든 마일스톤 끝나면 v1.1.0 release
