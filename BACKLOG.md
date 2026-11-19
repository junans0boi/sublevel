- [S4] 방향별 사망 클립·래그돌(CharacterJoint 생성 에디터 스크립트), 1인칭 팔 실제 메시 교체(#237 차량 1인칭 손 포함) — 슬라이스 후 (ADR-186)
- [S5] 탄흔·관통(`BulletSurfaces.Of`)의 건물 이름 파싱을 `SurfaceIdTag`로 교체: 집 전체=Wood 규칙이 바뀌어 관통 판정이 달라지므로 ADR 먼저(#341). 발소리 쪽 파싱은 제거됨(ADR-183).
- [캐릭터] UBC 의상(헬멧·조끼·장갑 CC0 소품) 반입으로 맨몸 단색 재질 탈피, 얼굴·손 피부색 복원. Mixamo 로스터 7~11종은 준환이 다운로드(HUMAN_TODO).
- [캐릭터] 정확한 머리 비율(턱~정수리)은 얼굴 랜드마크가 없어 목관절 기준으로만 측정함. 로스터 확정 시 모델별 보정.
- [캐릭터] 팀 완장·머리띠 소품과 `BackSocket` 배낭 연결은 애니·UI 트랙 병합 뒤.
- [애니] 격차 클립 시트(Mixamo/UAL): 스프린트 로우레디, 점프 3단, 방향 이동 8방향(전·후 대각), 제자리 회전, 기어가기·눕기·일어나기, 피격 4방향, 권총 세트, 수영, 난간, 앉기, 펀치 — 도착하면 `CharacterControllerBuilder`·`ProceduralClips`의 임시를 교체 (ADR-234)
- [애니] 발 IK 계단(단차) 측정, 장전 중 마그 모델(`ShowMag`)을 Action 클립 손에 붙이기, `Animator.cullingMode` 서버 전용 CullCompletely 측정 (ADR-232·233)
- [애니] 플레이어 모델 선택(로비→`Visual` 교체): 지금 플레이어 프리팹은 MixamoSwat 하나, 봇만 12모델 (ADR-230, UI 트랙)

- [S5] 환경음 베드(AmbientBeds 합성 바람·새·귀뚜라미)를 승인된 Freesound 실녹음(527499·683420·683433·522299·826326)으로 교체, 폭발·사이렌·가이거·방송 연결(S6 훅 이후)

- UI: 게임 착용 장비(ArmorVisual·BackpackVisual, LootMeshes)가 반구·상자 모양이라 캐릭터와 어울리지 않음 — 문제 목록·제안 `docs/tracks/ui_GEAR_VISUAL_NOTES.md`
- 차량: 오토바이 R0·고개 근처 가중 배치(ADR-198) 미구현
- [애니] 스윙 발 클리어런스 IK: 계단·경사 걷기에서 스윙 발목이 단 모서리·경사면 안으로 들어감(15모델 5~17cm, FootPlantSurveyTests), 월드 Terrain 위 직접 측정 (ADR-262)
- 훈련장: TrainingRange.unity·ZoneD.unity의 Match에 없는 스크립트(guid 5381414d…) — 실행 시 'referenced script is missing' 경고
