# 08. 총기 모델 후보 시트 + 상표 검토 목록 (S7 사전 조사)

조사일 2026-11-16 · 조사 전용(다운로드·설치·수정 없음). 후보 선택·상표 판단은 준환(사람)이 한다(CLAUDE.md 규칙 8, ADR-172).
표기: **[확인]** = 오늘 해당 페이지에서 본 것 / **[추정]** = 페이지에 없어 이름·경험으로 짐작 / **[미확인]** = 못 봄.
제약: OpenGameArt는 자동 접근 403이라 못 열었다(Chrome 미사용). 폴리 수는 어느 페이지에도 없어 대부분 [미확인] → 후보 선택 후 FBX 삼각형 수를 Unity에서 재야 한다.

## 1. 소스 요약

| 코드 | 소스 | 라이선스 | 내용 | 비고 |
|---|---|---|---|---|
| Q | [Quaternius Ultimate Guns Pack](https://quaternius.com/packs/ultimategun.html) (이미 사용 중, W-0001) | CC0 [확인] (페이지+itch 문구, 2026-11-16) | 공식 "40종", itch "50+", Poly Pizza 번들 25개(권총4·리볼버3·AR3·불펍1·SMG2·저격4·샷건2+쏘드오프1+쇼트스톡1·부착물 bipod/bayonet/scope/tripod) [확인]. 개수는 출처마다 달라 **zip을 열어봐야 정확** | 단색·각인 없음(W-0001 기록). 부착물은 별도 모델 [확인]. 총 본체는 단일 메시 [추정]. 폴리 [미확인] |
| P | Poly Pizza의 Pichuliru "West/East" 세트 (2022-03-08) | 확인한 3개(AR West, SMG Compact West, Shotgun Auto West)는 "Public Domain (CC0)" [확인]. 나머지 같은 작가 모델은 [미확인](같을 가능성 [추정]) | AR/SMG/샷건/저격/소총 다수. FBX/GLTF | 서양식·동양식 총 모양 [추정] = 실총 닮음 가능 |
| CT | Poly Pizza CreativeTrio | Shotgun Double Barrel = CC0 [확인]. 그 외 [미확인] | 더블 배럴·소총·저격·SMG 등 | |
| CD | [chilly-durango Low Poly Firearms](https://chilly-durango.itch.io/low-poly-firearms) | CC0 1.0, AI 미사용 명시 [확인] | 16종: 권총4(Luger·Detective Revolver·1911·Handcannon), 샷건4(쏘드오프·사냥·Stubby Pump·Stock Pump), SMG4(Sterling·UZI·Vektor·UMP), AR4(Semi-Auto·AUG·AK·M4). Guns.blend 1.3MB | **슬라이드·방아쇠·실린더·펌프·탄창이 별도 모델**(애니메이션용) [확인]. 무텍스처, AK 270삼각형 [확인]. 형상이 실총 복제 [확인: 이름이 실총] |
| CB | CC-BY 3.0 (Poly Pizza) austincford / J-Toastie / Zsky / portomat | CC-BY 3.0 [확인: M21 EBR·SRSA1·MK14·AKM·Zsky AR·J-Toastie/portomat 더블배럴] | DMR형(M21 EBR, MK14, SRSA1), 리깅된 AKM·더블배럴(J-Toastie "fully rigged") | 크레딧 필수(LICENSE_POLICY 허용, 크레딧 화면 자동 반영 필요). 모델명이 실총 |
| S | [Stein Classic Weapons Pack](https://stein-indie.itch.io/classic-weapons-pack) | CC0 [확인] | AK47·Mac10·MP5·M700·1911·Tec9·레버액션·M14. 고폴리·LOD 없음·PBR PNG. 개발 중(일부 FAB로 이동) | 실총 복제, 폴리 과다 → 리토폴 필요. 후순위 |
| G | [3dmodelscc0 Guns & Explosives](https://3dmodelscc0.itch.io/free-cc0-guns-explosives-pack) | CC0 [확인] | 총 9종(AK-47·M4A1·M3·Luger·Makarov·Suomi·샷건·저격·Flare) + 폭발물 10. 154MB | 실총 복제, 원 사이트 폐쇄(출처 추적 약함). 투척물 후보로는 유효 |
| K | Kenney Blaster Kit | CC0 [확인] | 블래스터(SF/장난감풍) 40파일 | 스타일이 안 맞음 [추정]. **제외 권장** |

## 2. 슬롯별 후보 시트 (16종)

위험도: 낮=오리지널 느낌 / 중=일반 형상이나 실총 연상 가능 / 높=실총 복제 이름·형상. 모든 CC-BY·실총형은 수정(윤곽 변경·각인 제거)을 전제로 한다.

| 슬롯(가칭) | 후보 모델 (링크/출처) | 라이선스 | 폴리 | 분리 파트/리깅 | 복제도 | 필요한 수정 |
|---|---|---|---|---|---|---|
| **SMG ×3** (Ordo, Brisk, Sable) | Q: Submachine Gun ×2 ([예](https://poly.pizza/m/7ehatxr7FY)) — Ordo가 이미 SubmachineGun_1 | CC0 [확인] | [미확인] | 단일 메시 [추정] | 낮 [추정] | 비율·색 조정 |
| | P: [Smg Compact West](https://poly.pizza/m/RmWDMB5bI9), [Smg West](https://poly.pizza/m/7Dh5JSbZcp) | CC0 [확인]/[미확인] | [미확인] | [미확인] | 중 [추정] | 색·실루엣 |
| | CD: Sterling/UZI/Vektor/UMP | CC0 [확인] | 저폴리 | 탄창 등 분리 [확인] | 높 | 형상 변경 필수 |
| **AR 5.56 ×2** (Warden, Vanguard) | Q: Bullpup(Warden 사용 중), Assault Rifle ×3 ([예](https://poly.pizza/m/Bgvuu4CUMV)) | CC0 [확인] | [미확인] | 단일 [추정] | 낮~중 | 총열·개머리판 변형으로 차별화 |
| | P: [Assault Rifle West](https://poly.pizza/m/ss1nz3gJnx) | CC0 [확인] | [미확인] | [미확인] | 중 [추정] | |
| **AR 7.62 ×2** (Bulwark, Hauler) | Q: Assault Rifle 나머지 변형(AssaultRifle_1..) — 5.56용과 겹치므로 길이/스케일 키워 구분 | CC0 | [미확인] | 단일 | 낮~중 | 스케일·실루엣 차별 |
| | P: Rifle Assault East / Rifle West / Rifle Battle East ([예](https://poly.pizza/m/tJaWNYo064)) | [미확인](작가 기준 CC0 가능성 [추정]) | [미확인] | [미확인] | 중~높 [추정: AK류 가능] | 라이선스 페이지 재확인 |
| | CD: AK/M4/AUG/Semi-Auto | CC0 [확인] | ≤270삼각형 | 탄창 분리 | 높 | 형상 변경 필수 |
| **DMR ×3** (Lancer, Tracer, Arbiter) | **CC0 전용 DMR 없음** [확인된 범위]. 대용: Q 저격 ×4 중 짧은 것/Q AR + 스코프 부착물(Scope 모델 [확인]) | CC0 | [미확인] | 스코프 별도 [확인] | 낮~중 | 스코프 장착 AR형으로 DMR 연출 |
| | CB: austincford [M21 EBR](https://poly.pizza/m/iZPuYSKNrm), [MK14](https://poly.pizza/m/lNGPW2NGPZ), [SRSA1](https://poly.pizza/m/4FyY7r5sjB) | CC-BY 3.0 [확인] | [미확인] | [미확인] | **높**(M21/MK14는 실총 이름) | 크레딧 + 윤곽 변경. 선택 시 법무 확인 부담 큼 |
| | P: Rifle West 등 | [미확인] | | | 중 | |
| **SR ×2** (Sentinel, Obelisk) | Q: Sniper Rifle ×4 ([A](https://poly.pizza/m/TKaBjAEofL), [B](https://poly.pizza/m/XCIC2Ae4Rb), [C](https://poly.pizza/m/ASOMZIErq3), [D](https://poly.pizza/m/i65hEldsw6)) | CC0 [확인: A] | [미확인] | 스코프 별도 가능 | 낮~중 | 현재 임시(AssaultRifle 늘림) 교체 |
| | P: Sniper Rifle West/East ([예](https://poly.pizza/m/kwJawENuvA)) | [미확인] | | | 중 | |
| **샷건 더블배럴 ×1** (Duo) | CT: [Shotgun Double Barrel](https://poly.pizza/m/k0fA37Awl8) | CC0 [확인] | [미확인] | [미확인] | 낮~중 | 색 조정 |
| | Q: Shotgun Sawed Off ([링크](https://poly.pizza/m/29FXKu7G91)) — 더블배럴 형인지 [미확인] | CC0 | | | | |
| | CB: J-Toastie [Double Barrel](https://poly.pizza/m/Emvvx56omx) (fully rigged), portomat | CC-BY 3.0 [확인] | | **리깅됨** [확인] | 중 | 크레딧 |
| **샷건 5발 반자동 ×1** (Breacher) | P: [Shotgun Auto West](https://poly.pizza/m/f0USjc13vj) | CC0 [확인] | [미확인] | [미확인] | 중~높 [추정: 반자동 12G 형상] | 윤곽 변경 |
| | Q: Shotgun ×2 (현재 Breacher는 Shotgun_1 펌프형) + 탄창 파트 개조 | CC0 | | 단일 | 낮 | 모델에 박스 탄창 추가(Blender) |
| **권총 ×2** (VK-9, Vesper) | Q: Pistol ×4, Revolver ×3, 9mm Pistol ([링크](https://poly.pizza/m/BoZWhFdsj4)) — VK-9가 Pistol_1 사용 중. Vesper(대구경 한 발 권총)=Revolver 변형 | CC0 [확인] | [미확인] | 단일 | 낮~중 | |
| | CD: Handcannon, Detective Revolver, Luger, 1911 | CC0 | 저폴리 | 슬라이드·실린더 분리 [확인] | 높(Luger/1911) | |
| **보급 LMG ×4~6** | **CC0 후보 못 찾음** [확인된 범위]. 폴리피자 "Gun Turret Minigun"(Thardoc)은 라이선스 [미확인]. arowx LMG = 유료 [확인]. CGTrader/RenderHub LMG = 로열티프리지만 CC0 아님·유료 | — | — | — | — | 대안: Q Assault Rifle + Q Bipod 부착물 + 확대 탄창(Blender 키트배시), 또는 직접 모델링(오리지널). 보급 전용이라 placeholder/ 유지도 가능 |

## 3. 슬롯 충족도 (CC0 확인 기준 vs 전체 후보)

| 슬롯 | 필요 | CC0 후보(확인+추정) | CC-BY 포함 | 상태 |
|---|---|---|---|---|
| SMG | 3 | 6+ | — | 충족 |
| AR 5.56 | 2 | 4+ | — | 충족(Q 4개로 5.56/7.62 4종 가능, 모양 비슷한 게 문제) |
| AR 7.62 | 2 | 위와 공유 | — | 충족(차별화 작업 필요) |
| DMR | 3 | 0~2(대용품: Q 저격·AR+스코프) | +3(austincford) | **약함** |
| SR | 2 | 4+ | — | 충족 |
| 샷건 | 2 | 더블 1+ / 반자동 1(CT, P) | +2 | 충족(검증 후) |
| 권총 | 2 | 7+ | — | 충족 |
| 보급 LMG | 4~6 | 0 | — | **빈 슬롯** |

## 4. 권장

1. 기본은 Quaternius(이미 W-0001) + Pichuliru/CreativeTrio(CC0)로 16종을 채우고, 모델이 비슷해 보이는 문제는 스케일·색·부착물(스코프/바이포드/소음기)로 차별화한다.
2. DMR은 CC0 단독 후보가 부족 → Q 저격/AR + 스코프 조합으로 시작, 부족하면 CC-BY(austincford)는 실총 이름·크레딧 부담이 커서 비권장.
3. LMG는 CC0 부재 → 키트배시 또는 placeholder 유지(보급 전용이라 우선순위 낮음).
4. 실총 복제도가 높은 팩(CD, S, G)은 형상 변경 없이 쓰지 말 것. CD는 "분리 파트" 때문에 리깅 참고용으로 가치가 있다.
5. 다운로드 전 확인: Quaternius zip 실제 모델 목록·삼각형 수, Pichuliru 나머지 모델 라이선스(Poly Pizza 모델 페이지), OpenGameArt 업로드본(미접속).
6. 라이선스 증빙은 사용 시점에 ASSET_LEDGER.md에 URL·확인일 기록(규칙 1).

## 5. 상표 검토용 목록 (법적 판단 아님)

아래 "겹칠 가능성 메모"는 일반 상식에 기반한 **추정**이며 이번에 검색으로 확인하지 않았다. 판단은 준환/전문가. 검색 대상 예: KIPRIS(한국, 상품류 13 화기·9 게임SW·28 완구·12 차량), USPTO TESS, WIPO Global Brand DB, EUIPO, Steam/Google("이름 + gun / weapon / game").

| 가칭 | 슬롯 | 겹칠 가능성 메모 [추정] | 검색 키워드 |
|---|---|---|---|
| VK-9 | 권총 | 'VK'+숫자 형식의 제품 코드명, 사실 확인 못 함. 앞 문서에서도 검토 플래그 | "VK-9", "VK9", "VK-9 pistol" |
| Vesper | 권총(대구경) | 일반 영단어(저녁). 영화 속 인물/상품명 등 다용도 | "Vesper pistol", "Vesper firearm", "Vesper game weapon" |
| Ordo | SMG | 라틴어(질서). 제품·게임 용어 사용 여부 미확인 | "Ordo SMG", "Ordo firearm" |
| Brisk | SMG | 점화플러그 등 실존 브랜드명 가능 | "Brisk" 상표, "Brisk weapon" |
| Sable | SMG | 자동차 모델명(미국 세단)·일반 단어 | "Sable" class 12/13, "Sable SMG" |
| Warden | AR(5.56) | 일반 단어, 게임·상품에서 흔함 | "Warden rifle", "Warden weapon game" |
| Vanguard | AR(5.56) | 흔한 상표(금융, 전쟁 게임 타이틀, 일부 소총 라인 명칭 가능성) | "Vanguard rifle", "Vanguard" game/firearm |
| Bulwark | AR(7.62) | 일반 단어, 방어 장비 제품명 가능 | "Bulwark rifle", "Bulwark firearm" |
| Hauler | AR(7.62) | 차량·운송 상품명 가능, 총기 명칭으로는 어색 | "Hauler" 상표, "Hauler rifle" |
| Lancer | DMR(5.56) | 실존 자동차 모델명, 유명 게임의 소총 이름과 겹칠 가능성 높음(기억 기반) | "Lancer rifle", "Lancer game weapon", "Lancer car" |
| Tracer | DMR(5.56) | 유명 게임 캐릭터명, 일반 용어(예광탄) | "Tracer game", "Tracer rifle" |
| Arbiter | DMR(7.62) | 유명 게임 속 인물명과 겹칠 가능성(기억 기반) | "Arbiter" game character/weapon |
| Sentinel | SR(7.62) | 일반 단어. 게임 속 저격 무기/직업명, 제품명으로 흔함 | "Sentinel sniper", "Sentinel rifle", "Sentinel game weapon" |
| Obelisk | SR(7.62) | 일반 단어, 게임 구조물/무기명 가능 | "Obelisk sniper", "Obelisk weapon" |
| Breacher | 샷건(반자동) | 일반 용어(문 돌파 요원). 제품·게임 총기명 가능 | "Breacher shotgun", "Breacher weapon" |
| Duo | 샷건(더블배럴) | 매우 일반적 단어, 상표 다수 | "Duo shotgun", "Duo" class 13/9 |
| (참고) Rattler | 제외됨 | 실존 총기 제품명 가능성 지적으로 Ordo로 교체됨(RISKS) | — |

SPEC에 있는 다른 이름(보급 LMG 가칭 등)은 아직 weapons.json에 없어 이 표에 넣지 않았다. 실총 이름이 들어간 에셋 파일명(AK, M4, UZI, M21 EBR, MK14 등)은 프로젝트에 파일명·에셋 이름으로 넣지 말고 import 시 개명한다(LICENSE_POLICY 상표 위생, S-0001 선례).
