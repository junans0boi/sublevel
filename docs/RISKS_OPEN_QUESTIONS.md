# RISKS_OPEN_QUESTIONS — 리스크와 미결정 사항

## 리스크
| 리스크 | 영향 | 완화 |
|---|---|---|
| Unity가 GUI 중심 → 헤드리스 자동화 한계 | 핵심 방식 실패 | M0에서 증명, 실패 시 Godot 4 폴백 ADR |
| M1 13" 성능/발열 | 개발 속도, 예산 왜곡 | 순차 빌드, 낮은 해상도 텍스처, 실측 |
| 에셋 라이선스/상표 | 출시 불가 | LICENSE_POLICY, 대장, 한국법 검토 |
| 세미리얼 톤 통일(CC0 팩 혼합) | 품질 | 머티리얼 재작업, 포스트 톤 통일 |
| 총기 형상의 실존 유사성 | 상표/디자인권 | 형상 수정, 각인 제거, 오리지널 명칭 |
| Mixamo/MetaHuman 약관 | 법적 | 기본 보류 |
| 싱글→멀티 전환 비용 | 일정 | 서버 권위 구조 초기부터 |
| 호스팅 비용 | 운영 | VPS/Edgegap, M6에서 산정 |
| Unity 자격(고용 상태) 미확인 | 라이선스 | 사람이 확인 |
| AI 에이전트 자율성의 품질 편차 | 품질 | DoD, code-review, 성능 게이트 |
| 한국 법 기준 상표·디자인권 미조사, 국산차 무료 에셋 개별 검색 안 함 | 법적 | 릴리스 전 전문가 검토, 차량은 오리지널 설계 우선 |
| 로우폴리가 아닌 사실적 차량·총기는 무료·무리스크 에셋 부족 | 품질/비용 | 직접 제작(Blender) 또는 일부 구매 예산(사람 승인) |

## 미결정 (사람이 결정/확인)
- [ ] 게임 이름(상표 검색 포함)
- [ ] 최종 엔진 확정(M0 결과 후)
- [ ] 한국 저작권/상표 법적 검토 시점과 담당
- [ ] 유료 에셋 예산 한도
- [ ] Windows 테스트 PC 확보
- [ ] 동시 접속 규모(플레이어 수), 팀 인원
- [ ] 출시 플랫폼(Steam 등)과 가격 모델
- [ ] Unity Personal 자격 확인
- [ ] 이슈 트래커 최종(GitHub Issues vs 로컬)

## M0 중 누적할 것
Unity 헤드리스로 못 하는 작업 목록, 스킬 이름/동작 차이, 네트워크로 막힌 다운로드 목록.

## M0 중 누적 (2026-11-12)
- Fish-Net 라이선스는 MIT/Apache/BSD가 아닌 **커스텀**(무료·로열티 프리, 게임 사용 허용, 경쟁 네트워킹 제품 금지, 플로리다 관할). ADR-003 확정에 따라 사용 중이나 **법적 검토는 사람 몫** → needs-license-review 이슈.
- (해소) Windows/IL2CPP 모듈 이슈: 6000.3.28f1 전환으로 win(Mono)·mac(IL2CPP) 빌드 성공. 남은 일: 사람이 windows-il2cpp 모듈 추가 후 win 백엔드를 IL2CPP로 전환(별도 티켓).
- Unity 헤드리스 라이선스: 자격 확인은 통과("Access token is unavailable" 경고는 있으나 빌드/테스트 정상).

## M1 중 누적 (2026-11-12)
- [ ] **무기 이름 상표 검토(사람)**: 현재 임시 이름 VK-9, Ordo, Warden, Breacher. 에이전트는 상표 충돌 여부를 판단하지 않는다. (초안의 "Rattler"는 실존 총기 제품명과 겹칠 수 있다는 리뷰 지적으로 "Ordo"로 교체.) 릴리스 전 전문가 확인.
- 이동은 M6까지 소유자 권위(ADR-029)라 이동 치트 방지 없음. 멀티 공개 전 M6 필수.

## M1.5 중 누적 (2026-11-12) — 릴리스 전 사람이 확인
- 무기 모델 4종(Quaternius Ultimate Guns: Pistol_1, SubmachineGun_1, Bullpup_1, Shotgun_1)은 각인·상표·텍스처가 없는 로우폴리지만 **실존 총기와 형상이 닮았는지는 검토하지 않았다**(디자인권 판단은 사람 몫).
- 총성(S-0001, The Free Firearm Sound Library)은 OpenGameArt 페이지에 CC0로 표기돼 있으나 **압축 파일 안에 라이선스 문서가 없다**. 페이지 사본은 `docs/licenses/S-0001/`. 원 배포처 표기 확인 필요. 원본은 실총 녹음이며 프로젝트 파일명에는 실총 명칭을 넣지 않았다.
- OpenGameArt "Gunshot Sounds"(Tabasco)는 페이지는 CC0, 압축 파일 안 문서는 CC-BY 3.0으로 **표기가 엇갈려 사용하지 않았다**.
- 캐릭터가 Universal Base Characters가 아닌 Ultimate Modular Men(Swat)이다(ADR-036). Humanoid 리타깃은 하지 않았다(애니메이션이 같은 파일에 내장).

## M1.6a-fix 중 누적 (2026-11-13)
- [ ] **릴리스 전 제거(S5, #399, ADR-300)**: 클라이언트 `--simulate-ping <ms>`·`--simulate-jitter <ms>`(체감용 지연 주입). 파일 `Core/SimulatedPing.cs`, `Net/PingSimulation.cs`, `LobbyNet.StartClientSide`·`MatchDirector`의 `PingSimulation.Attach` 호출, `NetStatusView`의 표시 한 줄, 테스트 `SimulatedPingTests`. 인자가 없으면 아무것도 하지 않는다.
- [ ] **릴리스(특히 멀티 공개) 전 제거**: 플레이어 빌드의 개발용 실행 인자 `-feelcapture`(대본 입력 + 화면 저장, 호스트가 자기에게 무기 지급), `-perfcapture`, `-autoplay`(#77, 빠른 봇 매치 관전 촬영). 지금은 검수를 위해 일반 빌드에 들어 있다.
- 옆·대각선 이동은 전용 애니메이션이 없어 다리를 돌리고 허리를 비트는 방식이다(ADR-049). 8방향 이동·점프 애니메이션 에셋을 확보하면 교체한다(가입형 소스는 사람이 받아야 함 — HUMAN_TODO).

## 차량 모델 (2026-11-14)
- [ ] **GoblinV12 텍스처의 상표·번호판 여부(사람 판단)**: 차 뒤쪽 빨간 엠블럼과 "27 345" 번호판 모양이 원본 텍스처(`Art/Vehicles/GoblinV12/Goblin2017_albedo_edit.png`)에 그려져 있다. 실존 브랜드·로고를 닮았는지, 그대로 둬도 되는지 릴리스 전 법적 검토 대상으로 올린다(에이전트는 판단하지 않음).

## 에셋 라운드 (2026-11-16)
- [ ] **Mixamo 사용 확정(사람 판단, 규칙 8)**: Adobe 공식 FAQ는 상업 게임 포함을 허용하나 원본 FBX 단독 재배포는 금지(커뮤니티 근거). Adobe Additional Terms 원문 PDF를 읽지 못함. 저장소(PRIVATE)에는 원본 FBX가 있으므로 저장소 공개·템플릿 배포 전 반드시 확인. 근거 기록: docs/ASSET_ROUND_2026-11-16.md, ASSET_LEDGER C-0005.
- [ ] Asset Store 에셋은 이번 라운드에서 받지 않음(EULA §5.1 복사 제한 때문).

## S7 총기 (2026-11-16) — 릴리스 전 사람이 확인 (에이전트는 법적 판단 안 함)
- [ ] **총기 이름 상표·상품명 검토(사람)**: 아래 가칭 전부. 검색 예: KIPRIS(상품류 13 화기·9 게임SW·28 완구·12 차량), USPTO TESS, WIPO Global Brand DB, EUIPO, Steam/Google("이름 + gun / weapon / game"). "겹칠 가능성"은 일반 상식 기반 추정이며 검색으로 확인하지 않았다(출처: `docs/research/08_gun_model_candidates.md` §5).
  - VK-9(권총, 'VK'+숫자 코드명 형식, 기존 플래그) · Vesper(권총) · Ordo · Brisk(점화플러그 브랜드 가능) · Sable(자동차 모델명 가능) · Warden · Vanguard(흔한 상표) · Bulwark · Hauler(차량·운송 상품명 가능) · Lancer(자동차 모델명, 게임 소총명 가능) · Tracer · Arbiter(게임 인물명 가능) · Sentinel · Obelisk · Breacher · Duo(상표 다수)
  - 모델 윤곽 주의(ADR-217): AK형 모델은 쓰지 않도록 교체했으나, 현재 AR 계열(Vanguard·Bulwark·Lancer·Hauler·Fang·Bastion·Anvil)은 AR-15형 윤곽, Bulwark·Tracer·Warden은 불펍형(실총 연상 가능, 디자인권은 사람 몫). 목록 `placeholder/guns_PLACEHOLDERS.md`
  - 08 시트에 없던 추가분(미검토): Bastion · Anvil · Marshal · Fang · Hornet (ADR-182)
- [ ] **총기 모델 형상의 실존 유사성**: 임시 모델(Quaternius·Poly Pizza CC0)은 각인·로고가 없지만 실총과 닮았는지 검토 안 함(디자인권은 사람 몫). 실총형 팩(chilly-durango·Stein·3dmodelscc0)과 CC-BY 후보(M21 EBR·MK14 등)는 쓰지 않기로 함(ADR-182). 실총 이름이 든 파일명은 import 시 개명.
- [ ] **임시 모델·placeholder 교체**: DMR 3종·보급 5종 등 `placeholder/`에 둔 것은 릴리스 전 제거하거나 확정 모델로 교체(CLAUDE.md 규칙 2, ASSET_LEDGER 반입 시 표시).

## 대기 섬 (2026-11-18) — 릴리스 전 사람이 확인
- [ ] **대기 섬 봇을 '실제 인원처럼' 보이게 하는 연출(사람 판단)**: 매칭 후 대기 섬에 봇이 모여 현황판 인원이 사람+봇 합계로 표시되고 점차 늘어나 보인다. 봇이 사람이 아님을 어떻게/언제 알릴지(표기·약관·스토어 정책·한국 표시광고 관련 여부)는 사람이 정한다. 에이전트는 판단하지 않는다. 스펙: `docs/WAITING_ISLAND_SPEC.md`.
