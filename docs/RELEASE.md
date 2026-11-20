# 릴리스 — 게임 클라이언트 배포 절차 (#402)

로그인한 사람만 런처로 받는 클라이언트 zip을 만드는 절차. 비밀 값은 이 문서에 쓰지 않는다(서버 접속은 저장소 밖 접속 스크립트, 서명 키는 서버 환경변수).

## 흐름 한눈에
```
tools/release.sh (Mac)        빌드 → zip → sha256·크기 → manifest.json      Build/release/<버전>/
  └ --upload 또는 직접 ↓
sublevel-web/scripts/release_upload.sh <버전 폴더>
                              로컬 sha256 대조 → ssh 표준입력 스트림 → 서버 sha256 재검증 → 원자적 이동
서버 /var/www/sublevel-downloads/<버전>/{manifest.json, sublevel-<플랫폼>-<버전>.zip}
백엔드 GET /api/launcher/manifest   로그인 필요, 최신 유효 버전 + 10분 서명 URL
Caddy  /downloads/*                 forward_auth → /api/launcher/verify(200/403) → file_server(Range, 압축 없음)
런처                                 받은 파일의 sha256·size를 manifest 값과 대조
```

## 1. 빌드·패키징 — `tools/release.sh`
```bash
tools/release.sh [--mac] [--win] [--upload] [--version v<bundleVersion>[-접미사]]   # 플랫폼 생략 시 mac만
```
- Unity 빌드(`tools/unity.sh build mac` / `build win`)를 부르므로 무겁다. **한 번에 하나**, 측정 같은 Unity 독점 작업과 겹치지 않게 돌린다.
- 버전 이름 기본값: `v<bundleVersion>`(5절).
- mac: `ditto -c -k --sequesterRsrc --keepParent Build/mac/Sublevel.app`(번들 속성·링크 유지). win: `Build/win` 전체를 zip, `*_BackUpThisFolder_ButDontShipItWithYourGame`·`*_BurstDebugInformation_DoNotShip` 제외.
- 출력: `Build/release/<버전>/sublevel-<플랫폼>-<버전>.zip` + `manifest.json`(`version` = 폴더 이름, `released_at`, 플랫폼별 `file`·`sha256`·`size`). 스키마는 `sublevel-backend/docs/launcher-api.md`.
- `--upload`면 끝에 아래 2단계를 바로 실행한다.

## 2. 업로드 — `sublevel-web/scripts/release_upload.sh`
```bash
../sublevel-web/scripts/release_upload.sh Build/release/<버전>
```
1. 로컬 검사: `manifest.version` = 폴더 이름, 파일명·sha256 형식, 실제 크기·sha256이 manifest와 같은지.
2. 전송: ssh 표준입력으로 파일마다 base64 스트림(수백 MB도 scp 없이). 접속 명령은 환경변수 `SSH_CMD`(기본값은 저장소 밖 개인 접속 스크립트, 내용은 읽지 않고 실행만).
3. 서버 쪽: `/var/www/.sublevel-incoming/<버전>/`에 받고 파일마다 `sha256sum -c`. 하나라도 틀리면 폴더를 지우고 실패.
4. 검증이 끝난 뒤에만 `manifest.json`을 넣고 `mv -T`로 `/var/www/sublevel-downloads/<버전>/`에 원자적으로 옮긴다(백엔드가 반쯤 올라간 릴리스를 보지 않는다). 같은 버전 폴더가 이미 있으면 `EXISTS`로 거절(덮어쓰기 없음).
5. 끝에 `UPLOADED <버전>`과 `du -sh`(폴더 용량)를 출력한다.

## 3. 서버 경로와 보호 다운로드
| 무엇 | 경로·값 |
|---|---|
| 릴리스 저장 | `/var/www/sublevel-downloads/<버전>/` (ubuntu 쓰기, backend·caddy 읽기) |
| 업로드 임시 | `/var/www/.sublevel-incoming/<버전>/` |
| 백엔드 | `sublevel-backend`, 포트 3100. `DOWNLOADS_DIR` 기본 `/var/www/sublevel-downloads` |
| manifest | `GET /api/launcher/manifest`: 쿠키 `sid` 또는 `Authorization: Bearer`. 비로그인 401, 릴리스 없음 404. 가장 큰 `released_at`의 **유효한** manifest가 최신(스키마가 틀린 폴더는 건너뜀) |
| 서명 URL | 쿼리 `p`·`acct`·`exp`·`sig`, 만료 10분(`url_ttl_s` 600). 플랫폼·파일 경로·계정·만료가 서명에 묶여 하나라도 바뀌면 403. 규칙 상세는 `launcher-api.md` |
| Caddy | `/downloads/*` → `forward_auth 127.0.0.1:3100`(`/api/launcher/verify`) → `uri strip_prefix /downloads` → `file_server`(압축 없음, Range 기본, no-store) |

게임 **서버** 빌드 교체(`/opt/sublevel-game/releases/<이름>` + `current` 링크)는 `docs/DEPLOY_GAME_SERVER.md`.

## 4. sha256 검증 절차
세 번 대조한다.
1. **릴리스 직후(Mac)**: `release.sh`가 계산한 값이 `manifest.json`에 들어간다. 업로드 스크립트가 올리기 전에 다시 계산해 대조.
2. **서버 도착 후**: 업로드 스크립트가 서버에서 `sha256sum -c`. 실패하면 릴리스가 생기지 않는다.
3. **받은 뒤(런처·사람)**: manifest 응답의 `sha256`·`size`와 받은 파일을 대조. 손으로 확인할 때:
   ```bash
   shasum -a 256 sublevel-mac-<버전>.zip     # Windows: certutil -hashfile <파일> SHA256
   ```
   값이 manifest의 `files.<플랫폼>.sha256`과 같고 크기(바이트)가 `size`와 같아야 한다.

## 5. 버전 규칙 (ADR-535)
- 원천은 `ProjectSettings` `bundleVersion` = `X.Y.Z` 하나(지금 `0.2.0`).
- 게임 저장소 태그 `vX.Y.Z` = 런처 manifest `version`(서버 폴더 이름) `vX.Y.Z`[`-접미사`]. `release.sh`가 기본값으로 쓰고 다른 이름은 거절한다.
- 게임 서버의 접속 허용 값 `LobbyNet.CurrentVersion` = `X.Y.Z/<Protocol>`(지금 `0.2.0/7`). 다르면 로비가 거절한다. 같은 `bundleVersion`에서 나오므로 라벨과 접속 값은 한 빌드에서 함께 정해진다.
- **`Protocol`을 올리면 `bundleVersion`도 올린다**(같은 라벨은 업로드가 `EXISTS`로 거절). `bundleVersion`이 바뀐 릴리스는 서버와 클라이언트를 같은 빌드로 연달아 배포한다(`DEPLOY_GAME_SERVER.md`).

## 6. 체크리스트 (릴리스 1회)
1. `main`이 테스트 통과 상태인지 확인(`docs/PROGRESS.md` 최신 줄).
2. `tools/release.sh --mac [--win]` → `Build/release/<버전>/manifest.json` 확인.
3. `release_upload.sh` → `UPLOADED <버전>`과 용량 확인.
4. 로그인한 브라우저(또는 런처)로 `/api/launcher/manifest` 200, 받은 zip의 sha256 일치.
5. 서버 교체가 필요하면 `DEPLOY_GAME_SERVER.md`(접속자 확인 후).
6. 디스크: 서버에서 `df -h /var/www` — 오래된 릴리스 폴더는 사람이 판단해 지운다(스크립트는 지우지 않음).

## 7. GitHub Releases (태그 → 릴리스 노트, ADR-530)
런처 배포(위 1~6)와 별개로, 저장소마다 **소스 기준 마일스톤**을 GitHub Release로 남긴다.

| 저장소 | 태그 | 워크플로 | 첨부 |
|---|---|---|---|
| `sublevel`(공개) | `vX.Y.Z` | `.github/workflows/release.yml` | 없음(아래 조건 전까지) |
| `sublevel-launcher` | `vX.Y.Z` = `package.json`·`tauri.conf.json` 버전 | `build.yml`(태그면 Release 생성) | Windows NSIS 설치 파일 |
| `sublevel-backend` | `vX.Y.Z` = `package.json` 버전 | `release.yml`(lint·test 통과 후) | 없음(서버는 `scripts/deploy.sh deploy <태그>`) |
| `sublevel-web` | `vX.Y.Z` = `package.json` 버전 | `release.yml`(typecheck·build 통과 후) | 없음(서버가 main을 빌드) |

- **노트**: 주석 태그(`git tag -a`)의 메시지 + 이전 `v*` 태그 이후 커밋 목록(최대 100줄)을 워크플로가 붙인다. 비밀 값 없음, `GITHUB_TOKEN`만.
- **게임 태그와 다른 버전의 관계**: 태그는 소스 마일스톤 이름일 뿐이다. 접속 호환은 계속 `LobbyNet.CurrentVersion`(`bundleVersion`/`Protocol`)이 정하고, 런처 manifest 라벨은 `v<bundleVersion>`이라 태그와 같다(5절, ADR-535).
- `v0.*`와 `-` 접미사 태그는 pre-release로 표시한다(게임 워크플로).

### 절차 (게임)
```bash
git tag -a v0.2.0 -F notes.md      # notes.md: 요약(한국어) — 태그 메시지가 릴리스 노트 첫머리가 된다
git push origin v0.2.0             # → Actions가 Release 생성
```

### 게임 클라이언트 zip을 GitHub Release에 첨부하기 (지금은 하지 않음)
`tools/release.sh --mac --win --github v0.2.0 --version v0.2.0`이면 빌드 → zip → 파일당 **2 GiB 미만** 확인 → `gh release upload`. 공개 저장소의 Release는 **누구나 받을 수 있는 공개 배포**이므로 아래가 모두 풀리기 전에는 쓰지 않는다(판단은 준환, `RISKS_OPEN_QUESTIONS.md`):
1. Mixamo 약관 원문 확인(빌드에 Mixamo 캐릭터·애니메이션 포함), Fish-Net 라이선스 검토
2. 게임물 등급분류 필요 여부(무료 공개 배포)
3. 개발용 실행 인자(`-feelcapture`·`-autoplay`·`--simulate-ping`) 제거, `placeholder/` 비우기
4. 크기: Mac 빌드는 압축 전 약 3.5 GB(2026-11-18)라 zip이 2 GiB를 넘을 수 있다. 넘으면 스크립트가 멈춘다

## 기록
| 날짜 | 무엇 | 결과 |
|---|---|---|
| 2026-11-18 | 더미 1 MB 릴리스(0.0.0-test) 업로드 | 서버 sha256 OK, 원자적 이동 확인 후 삭제. 디스크 여유 약 70 GiB |
| 2026-11-18 | 보호 다운로드 시험 | 유효 200, Range 206, 만료·변조·플랫폼 변경·다른 파일·토큰 없음 403, 받은 파일 sha256·크기 일치, 비로그인 manifest 401 |
| 2026-11-20 | 첫 실제 릴리스 **v0.2.0**(Mac, ADR-535) | `.app` 1.1 GB → zip 531,420,326 B(507 MiB), sha256 `6a825aab72a08277763208c37ee769d21de2f9cce745ad08d152933ce965292e`. 업로드 488초(Mac→서버 약 1.0 MiB/s, base64라 회선 약 11 Mbit/s), 서버 sha256 OK, `UPLOADED v0.2.0`. 서버 디스크 96G 중 35G 사용, 여유 62G |
| 2026-11-20 | 런처 0.1.7 종단(Mac, 준환 로그인 세션) | manifest 조회 → v0.2.0 표시 → 다운로드 약 3.0 MiB/s(약 25 Mbit/s, 507 MiB 약 3분) → 받은 zip sha256 = manifest → `game/v0.2.0/`(`.installed`, `.files.json`, 최근 2개만 유지) → '게임 시작'으로 `v0.2.0/Sublevel.app` 실행 확인 |
| 2026-11-20 | 대역폭 추정 | 테스터 1명 = 507 MiB/버전. 10명이면 약 5 GiB/릴리스(Oracle 무료 송신 10 TB/월 대비 여유 큼). 서버 송신 속도 상한은 미측정(측정한 3.0 MiB/s는 이 Mac 회선 기준) |
