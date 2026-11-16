# 사운드 트랙 DONE (2026-11-16, `track/sound`)

## 준환 협의 요약(S5 기록에서 읽음)
권총 재장전=Glock19_reload(CC BY 크레딧 OK), 유리=Hard Glass Break 2, 저격1·2는 잘라서, 저격3(.50)=소음기 소리, 소총 재장전=AK 장전, 문=Ryding, 차량=GiocoSound 아이들(TRP 금지), 폭발=Distant Explosion, 사이렌 둘 중 하나, 방송=차임, 탄 스침 둘 다 잘라서. 권총2·SMG2~3·AR1~3·DMR2·샷건1~3은 탈락(다른 총·목소리·연발). 이후 "알아서 해라, 권한은 master에게". 불만의 핵심은 총성·걸음·유리가 "스타워즈 같다".

## 한 것
- **총성 21종**: 7종류 매핑(`Core/WeaponSounds`, ADR-250), near/far/소음기/실내꼬리/재장전/빈탄창/탄피/탄스침 클립 25개 + `CombatantAudio` 연결(없으면 옛 클립 폴백).
- **차량·엘리베이터·문·유리**: 합성음 대신 준환이 고른 실녹음(Sfx/Doors), 합성은 폴백(ADR-252).
- **공간음향**: 층·벽 차폐 규칙+프로브, 발소리·총성에 연결, 비용 한도(ADR-251).
- **크레딧·ledger**: S-0023~45, Credits.cs에 CC BY 3.0 1건(ADR-253).
- **도구**: `tools/sound_cut.py`+`sound_shots.json`(재현 가능한 자르기).
- **청취 시트**: `docs/sound/listen_sheet.html`(121개, 판정 복사 버튼). 발소리 14표면 포함.
- ADR-250~254, BACKLOG 1줄. commit은 `track/sound`.

## 못 한 것
- Unity 컴파일·EditMode·임포트(.meta)·PlayMode 전부 미실행(지시). 아래 확인/추정 참고.
- 환경음 베드·폭발·사이렌·가이거·방송 연결, 구역 훅(S6 의존), 탄흔 이름 파싱(#341), 옛 Kenney/OGA 파일 삭제.
- 층 차폐 PlayMode 시나리오(2층 발소리)와 광선 비용 실측.

## 확인한 것 / 추정
- **확인(dotnet 콘솔)**: weapons.json 총 21종 전부 WeaponSounds 항목 있음, 클래스별 near/far/재장전 파일 존재, 같은 종류 피치 중복 없음, 점유 수치(층1=0.5·약2kHz), 예산 8/프레임.
- **확인(분석)**: 소총 원본 AK(201670)·M16(201668)은 만(滿)스케일 클리핑 연발, SMG(201672)는 76ms 간격 연발, 권총2(253736)도 클리핑. 샷건·카빈·저격은 단발.
- **추정(미청취)**: 모든 자르기 범위, 소총 품질, 루프 이음새(엔진·타이어·엘리베이터), 컷 SMG 70ms 클립이 "딱"으로만 들릴 위험.
- **미컴파일**: `CombatantAudio`·`SoundOcclusionProbe`·`VehicleAudio`·`ElevatorAudio`·`GlassFx`·`DoorAudio` 수정은 Unity 컴파일 전. 문제 시 첫 에러는 여기.
- 새 WAV는 `.meta` 없이 커밋됨 → Unity 첫 실행이 생성(master가 같이 커밋).

## master에게 요청
1. **Unity 순번 1회(소리 없는 것)**: 컴파일, EditMode(`WeaponSoundsTests`·`SoundOcclusionTests`·`CreditsTests`·AssetValidator), 임포트로 생긴 `.meta` 커밋.
2. **다운로드(준환 로그인 필요, 다운로드·가입은 내가 못 함)**: 소총·경기관총용 **클리핑 없는 단발 소총 실녹음** 1~2개(CC0, Freesound, 5.56·7.62 단발+반향, 이름에 상표 포함돼도 ledger에만) — 구체 후보는 이 세션이 검색하지 못했음(파일명·URL·용량은 마스터/준환이 검색해 정함). 환경음 베드는 이미 받은 `AssetStaging/audio/`의 527499·683420·683433·522299·826326으로 가능(추가 다운로드 불필요).
3. **준환 청취**: `docs/sound/listen_sheet.html`. 판정 결과를 주면 `tools/sound_shots.json` 범위만 고쳐 재생성.
4. 소유 경계: `VehicleAudio`·`GlassFx`는 차량/월드 소유일 수 있음(각 한 줄 수정). 충돌 시 병합 때 내 쪽 한 줄만 살려 주면 됨.
