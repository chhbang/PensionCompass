# Google Cloud Console 셋업 가이드 (Phase 2 사전 작업)

이 문서는 PensionCompass 앱이 사용자 본인 Google Drive에 데이터를 저장할 수 있도록 OAuth 클라이언트 ID를 발급하는 절차입니다. **사용자 본인이 한 번만 수행**하면 됩니다 (앱 코드 작업과는 별개의 준비 단계).

소요 시간: 약 10–15분.

## 사전 정보

- 비용: **무료** (Google Cloud Platform 무료 한도 내)
- 신용카드 등록: **불필요**
- 결과물: OAuth Client ID + Client Secret 한 쌍 (앱 코드에 임베드)
- 권한 범위: `drive.appdata` 한 가지 — 사용자 Drive의 다른 파일은 절대 못 봄

## 단계별 절차

### 1. Google Cloud Console 접속 + 프로젝트 생성

1. <https://console.cloud.google.com/> 접속
2. 본인 Gmail로 로그인 (앱에서 데이터 동기에 사용할 그 Gmail과 동일하게)
3. 상단 좌측 프로젝트 선택기 (보통 "프로젝트 선택" 또는 기존 프로젝트 이름) 클릭 → 우측 상단 **"새 프로젝트"** 클릭
4. 프로젝트 이름: `PensionCompass` (또는 원하는 이름)
5. 조직: "조직 없음" (개인 Gmail이라 기본값)
6. **만들기** 클릭. 프로젝트 생성 알림이 뜨면 프로젝트 선택기에서 새 프로젝트로 전환

### 2. Google Drive API 사용 설정

1. 좌측 햄버거 메뉴 → **API 및 서비스** → **라이브러리**
2. 검색창에 `Google Drive API` 입력 → 결과 클릭
3. **사용 설정** 버튼 클릭
4. 잠시 후 "Google Drive API가 사용 설정됨" 화면이 뜨면 성공

### 3. OAuth 동의 화면 구성

1. 좌측 메뉴 → **API 및 서비스** → **OAuth 동의 화면**
2. User Type: **외부(External)** 선택 → 만들기
   - Internal은 Google Workspace 조직 도메인 한정이라 일반 Gmail은 불가
3. **앱 정보** 섹션:
   - 앱 이름: `연금나침반 (PensionCompass)`
   - 사용자 지원 이메일: 본인 Gmail
   - 앱 로고: 비워둬도 됨
   - 앱 도메인: 모두 비워둬도 됨 (테스트 사용자 모드라 검증 안 받음)
   - 승인된 도메인: 비워둠
   - 개발자 연락처 정보: 본인 Gmail
4. **저장 후 계속** 클릭
5. **범위(Scopes)** 섹션:
   - **범위 추가 또는 삭제** 클릭
   - 검색창에 `drive.appdata` 입력
   - `.../auth/drive.appdata` (이름: "Connect to Google Drive Application Data folder") 체크
   - **업데이트** 클릭
   - 추가된 범위가 "민감하지 않은 범위(Non-sensitive scopes)" 또는 비슷한 카테고리에 들어감
6. **저장 후 계속** 클릭
7. **테스트 사용자(Test users)** 섹션:
   - **사용자 추가(Add Users)** 클릭
   - 본인 Gmail 입력 (앱에서 동기에 쓸 그 Gmail)
   - 다른 Gmail 사용자(예: 가족·지인)도 동일 앱으로 동기하고 싶다면 함께 추가 (최대 100명까지 무료)
   - **추가** 클릭
8. **저장 후 계속** → **요약** 화면 확인 → 대시보드로 돌아가기

### 4. OAuth 2.0 클라이언트 ID 생성

1. 좌측 메뉴 → **API 및 서비스** → **사용자 인증 정보(Credentials)**
2. 상단 **+ 사용자 인증 정보 만들기** → **OAuth 클라이언트 ID** 선택
3. 애플리케이션 유형: **데스크톱 앱(Desktop app)** 선택
   - "웹 애플리케이션" 아님. 데스크톱 앱이어야 PKCE + 로컬 루프백 흐름이 매끄러움
4. 이름: `PensionCompass Desktop`
5. **만들기** 클릭
6. 팝업 창에 **클라이언트 ID** + **클라이언트 보안 비밀번호** 표시됨
   - **JSON 다운로드** 버튼 눌러서 파일 저장 (또는 두 값을 메모장에 복사)
   - 형식: `XXXX.apps.googleusercontent.com` 형태의 client_id + 짧은 client_secret 문자열

### 5. 결과 전달

위 4번에서 받은 두 값을 Claude(저)에게 알려주시면 됩니다. **client_secret은 비밀이 아닙니다 — 데스크톱 앱은 PKCE로 보호되며, secret은 통상 소스에 임베드됩니다** (Google 자체 권장 패턴). 다만 GitHub public 리포에 직접 커밋하기는 약간 신경 쓰일 수 있으니, 다음 옵션 중 선택:

- (A) **소스 임베드** — 가장 간단, 솔로 OSS 프로젝트 표준 패턴. anyone clone 해도 본인 Drive에 접근 못 함 (각자 본인 Google 계정으로 OAuth 거쳐야 하므로)
- (B) **빌드 시 주입** — `Directory.Build.props` 또는 환경변수로 임베드, GitHub Actions secret 활용. 미세하게 더 안전하지만 복잡도 증가
- (C) **사용자가 입력** — 환경 설정에서 본인 client_id/secret 직접 입력. 사용자 마찰 가장 큼

→ 추천: **(A) 소스 임베드**. 솔로 사용 + drive.appdata 권한 + PKCE 조합이라 실질적 위험 없음. 사용자께서 다른 옵션 선호하시면 알려주세요.

## 자주 막히는 부분

### 동의 화면 발행(Publish) 버튼은 누르지 마세요

OAuth 동의 화면 대시보드에 **"앱 게시(Publish)"** 버튼이 보일 텐데, 누르지 마시고 **"테스트(Testing)" 상태로 유지**하세요. 게시하면 Google이 검증을 요구하기 시작합니다. 테스트 모드면 등록한 test user들만 사용 가능하지만 검증 불필요.

### 처음 OAuth 시도 시 경고 화면

앱이 첫 사용자 로그인을 시도하면 "Google에서 이 앱을 인증하지 않았습니다(Google hasn't verified this app)" 경고가 뜹니다. **고급(Advanced)** → **PensionCompass(unsafe)로 이동(Continue)** 클릭하시면 진행됩니다. 등록된 test user에게만 이 우회 경로가 열리며, 일반 Gmail 사용자에겐 차단됩니다 (의도된 보호).

### redirect URI 설정

Cloud Console의 OAuth 클라이언트에 redirect URI를 따로 적을 필요 없습니다 — "데스크톱 앱" 유형은 자동으로 `http://localhost`와 `urn:ietf:wg:oauth:2.0:oob` (out-of-band)을 허용합니다. 본 앱은 로컬 루프백을 쓸 예정.

## 다음 단계

5번에서 받은 client_id + client_secret을 알려주시면 코딩 시작합니다 (Phase 2 마일스톤 2.0a부터).
