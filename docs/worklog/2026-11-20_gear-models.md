# 가방·조끼·헬멧 실제 모델 + 레벨별 디자인

- 날짜: 2026-11-20
- 이슈: #431
- ADR: ADR-520
- 브랜치: `claude/gear-models-cc9a47` (커밋 67fdcbf)
- main 머지: a063833

## 한 일
- Sketchfab CC BY 4.0 모델 9개, 등급(Lv1~3)마다 다른 모델. 원장 G-0001~G-0009, 크레딧 9건.
- 조끼는 로스터 15체에 맞춰 감싸고 몸통 뼈에 스키닝, 가방은 등(UpperChest)에 고정, 헬멧은 머리에 고정. 바닥 아이템도 같은 모델.
- Lv2 조끼는 준환 위임으로 다른 모델을 올리브로 재색.

## 검증
- EditMode 1642/1642, RosterRenderPerf 2/2(최악 p99 9.75 ms·1.12M 삼각형·761 드로우콜), 장비 관련 PlayMode 8/8, AssetValidator 0건.

## 남은 것·주의
- 성능 여유가 얇음(PERF_BUDGET에 기록).
- 캐릭터가 바뀌면 `tools/unity.sh run GearKit.MeasureBodies` 재실행.
