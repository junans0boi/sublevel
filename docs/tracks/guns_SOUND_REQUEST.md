# 신규 총기 소리 요청 — 사운드 트랙 인수용 (2026-11-16)

## 현재 구조(확인)
- `CombatantAudio`: 발사음 `_shotByWeapon`(id 인덱스 `AudioClip[]`)·재장전 `_reloadByWeapon`·피치 `_shotPitchByWeapon = {1,1,1,1,1,0.72}`, 원거리 층 `FarShotNames = {null,pistol,smg,rifle,shotgun,sniper}`(id 0~5만). `ClipFor`는 범위 밖 id면 **null → 소리 없음**. 그래서 id 6~16·31~35는 지금 **무음**이다.
- 보유 소리: `Resources/Shots/shot_{pistol,smg,rifle,shotgun,sniper}(_far).wav`(S-0001), 재장전 3종(S-0002).

## 요청
1. 위 배열 3종(+`FarShotNames`)을 **id→소리 분류 표**(데이터 또는 클래스 매핑)로 바꾸고 id 6~16·31~35를 채운다. 아래 표의 "권장 분류"는 기존 5종 재사용 + 피치/볼륨 차이 제안(새 파일 없이 가능). 총마다 같은 소리면 구분이 안 되니 최소한 **피치 차이**는 둔다.
2. 재장전: 탄창식(권총·SMG·AR·DMR·SR·Breacher·LMG)은 탄창 교체음, Duo는 "총을 꺾어 두 발 장전" 음(없으면 샷건 코킹 재사용).
3. 연사 속도(RPM)가 총별로 다르다. 소리 길이가 RPM보다 길면 겹침 → 짧은 꼬리 사용.
4. LMG(Bastion·Anvil)는 연사가 길어 총구음 + 반복 꼬리 처리 권장.

| id | 이름 | 종류 | 탄 | RPM | 탄창 | 권장 분류 |
|---|---|---|---|---|---|---|
| 1 | VK-9 | 권총 | 9mm | 333 | 12 | shot_pistol |
| 2 | Ordo | SMG | 9mm | 800 | 30 | shot_smg |
| 3 | Warden | AR 5.56 | 5.56 | 545 | 30 | shot_rifle |
| 4 | Breacher | 샷건(반자동) | 12ga | 109 | 5 | shot_shotgun |
| 5 | Sentinel | SR | 7.62 | 46 | 5 | shot_sniper(현재 rifle pitch 0.72) |
| 6 | Vesper | 권총 | 9mm | 180 | 6 | shot_pistol(저음·무겁게) |
| 7 | Brisk | SMG | 9mm | 949 | 32 | shot_smg |
| 8 | Sable | SMG | 9mm | 650 | 25 | shot_smg |
| 9 | Vanguard | AR 5.56 | 5.56 | 700 | 30 | shot_rifle |
| 10 | Bulwark | AR 7.62 | 7.62 | 500 | 25 | shot_rifle(pitch↓) |
| 11 | Hauler | AR 7.62 | 7.62 | 430 | 20 | shot_rifle(pitch↓) |
| 12 | Lancer | DMR 5.56 | 5.56 | 300 | 10 | shot_rifle |
| 13 | Tracer | DMR 5.56 | 5.56 | 360 | 15 | shot_rifle |
| 14 | Arbiter | DMR 7.62 | 7.62 | 240 | 10 | shot_sniper 혹은 rifle pitch 0.8 |
| 15 | Obelisk | SR | 7.62 | 40 | 5 | shot_sniper |
| 16 | Duo | 샷건(더블) | 12ga | 500 | 2 | shot_shotgun(두 발 간격 0.12s) |
| 31 | Bastion | LMG 5.56 | 5.56 | 650 | 75 | shot_rifle(+저역, 연사 길게) |
| 32 | Anvil | LMG 7.62 | 7.62 | 480 | 60 | shot_rifle(pitch↓) |
| 33 | Marshal | DMR 7.62(엘리트) | 7.62 | 400 | 15 | shot_sniper(짧게) |
| 34 | Fang | AR 5.56(3점사) | 5.56 | 850 | 30 | shot_rifle(3점사 간격) |
| 35 | Hornet | 기관권총 9mm | 9mm | 1000 | 33 | shot_pistol/smg(가볍게) |

(RPM은 `weapons.json`의 FireInterval에서 계산, 단발 총은 선택 연사 상한.)
