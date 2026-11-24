# 2026-11-24 차종별 계기판 4스타일 (ADR-558a)

준환이 고른 HTML 시안(A 쌍원형 / B 반달 / C 디지털 스포츠 / D 클래식 아날로그)을 uGUI로 옮겼다.

## 바뀐 것
- **배치**: 타코미터는 체력바 왼쪽, 속도계는 오른쪽, 시프트 라이트 7개는 체력바 바로 위. 무기 패널 자리를 비워 줌(`TakesWeaponPanel` 삭제, `Hud`의 무기 패널은 예전처럼).
- **`ClusterLayout`**(VehicleCluster.cs): 자리 계산만 하는 순수 함수. 틈이 좁은 16:10에서는 타코를 틈에 맞게 줄이고, 속도계는 핫바 위·미니맵 왼쪽으로 올린다.
- **`VehicleCluster`**: 스타일 하나로 만들어지고, 차가 바뀌면(행·레드라인·최고속) `VehicleHud`가 다시 만든다. 링 텍스처는 캐시. 단수 펀치·시프트 라이트 깜빡임·테스트 API(Speed/Fuel/Condition/FuelLow)는 그대로.
- **데이터**: `VehicleHandling`에 `Cluster`·`ClusterAccent`·`ClusterCompact`, `Motorcycle` 행 추가(레이스카는 자기 모델 행을 따름, 준환 확인)(rules.json과 Defaults 같은 값). `Vehicle.ModelName` 공개.
- **캡처**: `VehicleHandlingTests.CaptureCluster`가 네 차종(SW84=A, Shvan92=B, GoblinV12=C, LT80=D)을 1920x1080·16:10(1728x1080)·2560x1440으로 찍는다. `Snap`은 RT 크기에 맞게 CanvasScaler를 다시 계산하고 계기판을 다시 배치한다.

## 확인
- EditMode `VehicleClusterTests`(스타일 매핑·rules.json 일치·다이얼 눈금·3개 폭 x 4스타일 x 컴팩트 안 겹침·스타일별 생성) + `VehicleHandlingRulesTests`: 38/38.
- PlayMode `VehicleFuelTests` 3개 + `CaptureCluster` 4개 통과.
- 캡처 판정: 1차에서 B 양끝 숫자가 바닥선에 걸림, C 연료 기둥이 빨간 강조색이라 "부족"처럼 보임 → 둘 다 고침.

## 남은 것
- 16:10에서 B·C 타코가 약 0.73배로 줄어 B의 작은 글자(x1000 rpm, 차체 %)가 작다.
- 바이크(C 컴팩트)·레이스카 캡처는 안 찍음(매핑은 테스트로만 확인).
