# 12. 표면 태그 구조 설계 (S1 → S2·S5, 2026-11-16, 초안)

## 문제
발소리·탄흔 재질이 오브젝트 이름 파싱으로 정해진다(`CombatantAudio.cs:229-233`, `BulletSurfaces.cs:24-30`): 콜라이더 이름이 `Colliders`가 아니거나 루트 이름 끝(`_Kind`) 파싱이 실패하면 조용히 Concrete. 건물 이름 규칙이 바뀌거나 종류가 늘면 테스트 없이 전부 Concrete가 된다(06 B4). 표면은 발소리 4~6종뿐이다.

## 구조
1. **`SurfaceId`**(Core, 순수 C# enum): 발소리·탄흔 공용 단일 진실. 목표 14종(S5 `audio.json`과 합의: 기존 Concrete·Metal·Wood·Grass·Dirt·Water + Gravel·Asphalt·Tile·Carpet·MetalGrate·Glass·Roof 등. 최종 목록은 S5가 확정).
2. **기존 enum 매핑 유지**: `FootSurface`·`BulletSurface`·`SoundRules.TerrainSurface`는 `SurfaceId`에서 변환하는 함수로 감싸 기존 테스트(`FootstepSurfaceTests` 4종)를 깨지 않는다.
3. **태그 컴포넌트**: `BulletSurfaceTag`를 확장(또는 `SurfaceTag`로 대체하며 기존 필드를 읽는 호환 경로 유지). 값은 `SurfaceId`.
4. **부착 위치**: 월드 빌더가 박스 조각을 콜라이더로 만들 때 `BuildingPiece.Kind`(`SurfaceKind`)와 건물 종류별 재질 → `SurfaceId` 표로 정해 콜라이더 오브젝트에 부착. 이름 파싱 코드는 삭제.
5. **폴백**: 태그 없는 콜라이더는 Concrete + 에디터/개발 빌드에서 경고 로그 1회(조용한 폴백 금지). 빌더 검증 단계에서 "태그 없는 건물 콜라이더 0개"를 EditMode/PlayMode 테스트로 확인.
6. **지형**: 알파맵 지배 레이어 → `SurfaceId` (현재 `SoundRules.TerrainSurface`) 유지, 레이어 수가 늘면 표만 확장. 도로 메시는 Asphalt 태그.
7. **재질 → 표면 매핑 표**는 S2가 채운다(`materials.json`). 건물 재질이 바뀌어도 표만 고치면 소리가 따라간다.

## 순서
Core `SurfaceId`·변환 함수·단위 테스트(Unity 불필요) → 빌더에서 태그 부착(T5, Unity) → S5가 `CombatantAudio`/`BulletSurfaces`의 이름 파싱 제거(S5 소유 파일).

## 소유 경계
S1: `SurfaceId`(Core/World)와 빌더 부착. S5: `CombatantAudio`·`BulletSurfaces` 소비 코드. S2: 매핑 표. 충돌 시 master 중재.
