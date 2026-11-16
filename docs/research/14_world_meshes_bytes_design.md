# 14. #240 World_Meshes 바이너리화 설계 (S1, 2026-11-16, 초안)

준환 승인: `.bytes` + LFS, **앞으로의 파일만(이력 불변)**. Unity 실행은 master 순번 3. 이 문서는 그 전 설계.

## 현재 (코드 확인)
- `WorldMeshes.Store.Add`가 건물·도로·소품 메시를 `World_Meshes.asset` 한 파일의 서브에셋으로 만든다(`AssetDatabase.AddObjectToAsset`). 프로젝트가 ForceText라 정점 바이트가 16진 2자 → 43 MB, 411채는 약 240 MB(A1 추정, 13번 문서).
- ADR-160: ForceBinary 시도는 임포트 때 다시 텍스트가 되어 포기. 그래서 `.asset` 이진화가 아니라 **에셋 밖 자체 포맷**으로 간다.
- 씬(`World.unity`)은 메시를 GUID/fileID로 참조하므로, 메시를 `.bytes`로 옮기면 씬이 메시를 직접 참조할 수 없다 → 런타임에 채우는 구조가 필요하다.

## 제안 구조
1. **포맷 `WorldMeshBlob` (Core, 순수 C#)**: `magic "WMSH" + version(u16) + meshCount`, 메시마다 `name(utf8)`, `vertexCount`, `indexCount`, `subMeshes[(start,count)]`, bounds(6 float), 정점 스트림(BoxBatch의 현재 압축 정점 포맷 그대로: position·normal·uv 등, 이미 바이트 패킹), 인덱스(u16/u32). 리틀 엔디언 고정. `Write(meshes)`/`Read(bytes)`가 순수 C#이라 EditMode에서 Unity 없이 검증한다.
2. **저장**: `Assets/_Project/Prefabs/World/Generated/World_Meshes.bytes`(TextAsset). 예상 크기: 현재 구조에서 16진 2배 + YAML 오버헤드가 사라져 **43 MB → 약 8~12 MB**(추정, 측정 대상).
3. **씬 쪽**: `WorldMeshBank` 컴포넌트(World 루트, `[ExecuteAlways]`)가 `TextAsset` + `MeshFilter[]` + `int[] meshIndex`를 직렬화한다. `OnEnable`에서 blob을 읽어 `Mesh`를 만들고(`SetVertexBufferParams`/`SetVertexBufferData`/`SetIndexBufferData`) 필터에 채운다. 씬 파일에는 메시 참조가 없고(`m_Mesh: {fileID: 0}`) 필터-인덱스 쌍만 남는다.
4. **빌더 변경**(`WorldMeshes.Store`): `Add`가 에셋 생성 대신 리스트에 모으고 `Save()`가 blob을 쓴다. 베이크·충돌 계산이 메시를 필요로 하는 구간에서는 메모리 메시를 필터에 붙여 두고, 씬 저장 직전에 `sharedMesh = null`로 비운 뒤 `WorldMeshBank`에 인덱스를 기록한다(비우지 않으면 씬이 메시를 인라인 저장하므로).
5. **LFS**: `.gitattributes`에 `Assets/_Project/Prefabs/World/Generated/*.bytes filter=lfs diff=lfs merge=lfs -text` 추가(앞으로만, 이력 불변). 같은 경로의 `World.unity`·`World_NavMesh.asset`·`World_Terrain.asset` LFS 추적은 별도 결정(씬은 +90 MB 추정이므로 필요할 가능성 큼). **`.gitattributes`는 commit과 함께 master가 승인 요청.**
6. **런타임 비용**: 로딩 때 메시 수천 개 생성. 411채 기준 약 15,000~25,000 메시 규모면 로딩 시간에 영향 → 병합 규칙(머티리얼당 1메시, 이미 적용 중)을 유지하고, 필요하면 `Mesh.AllocateWritableMeshData`+`ApplyAndDisposeWritableMeshData`로 배치 생성. 측정은 T2 때(로딩 ≤5 s 판정선).
7. **에디터 임포트 부담이 사라진다**: 43 MB(→ 240 MB) YAML 파싱 대신 TextAsset 한 개.

## 테스트 계획 (테스트 먼저)
| # | 테스트 | 종류 | Unity |
|---|---|---|---|
| 1 | `WorldMeshBlob` 쓰기→읽기 왕복 동일(정점·인덱스·서브메시·bounds·이름) | EditMode(Core) | 아니오 |
| 2 | 같은 입력이면 바이트 동일(SHA) | EditMode(Core) | 아니오 |
| 3 | 잘못된 magic·버전·잘린 파일은 예외(조용한 빈 메시 금지) | EditMode(Core) | 아니오 |
| 4 | 같은 시드로 `WorldBuilder`를 두 번 돌려도 `.bytes` SHA 동일 | 에디터 실행 | 예(순번 3) |
| 5 | `WorldMeshBank`가 채운 메시가 생성 시 메시와 정점 수·bounds 일치 | EditMode(Editor) | 예 |
| 6 | 기존 `World_RendersWithinBudget`·`WorldTests` 통과, 전/후 파일 크기 표 | 기존 | 예 |

## 순서
Core `WorldMeshBlob`+테스트 1~3(순수 C#, 지금) → `WorldMeshBank`·`Store` 변경(순번 3) → 크기 전/후·결정성 4·5·6 → `.gitattributes` 승인.
새 파일: `Core/World/WorldMeshBlob.cs`, `Gameplay/WorldMeshBank.cs`(Gameplay 소유 확인 필요: 소유 목록 밖이면 master에 문의).

## 결과 (2026-11-16, Unity 순번 3)
구현: `Core/World/WorldMeshBlob.cs`, `Gameplay/WorldMeshBank.cs`(S1 소유 목록 밖, 기존 폴더, master 승인), `Editor/WorldMeshes.Store`(`AddBlob`·`Finish`), `WorldBuilder`(메시 호출처 6곳 `AddBlob`, 저장 직전 `store.Finish(world)`), `.gitattributes` 한 줄(`World_Meshes.bytes` LFS, 승인·적용, `git check-attr`로 확인). 식생·풀 메시는 컴포넌트가 직접 참조해서 작은 YAML 에셋에 남겼다.

| 파일 | 전 | 후 |
|---|---|---|
| `World_Meshes.asset` | 42,102,976 B (42.1 MB) | 720,664 B (0.7 MB, 식생만) |
| `World_Meshes.bytes` | - | 15,243,953 B (14.5 MiB, 3,485 메시) |
| 합계 | 42.1 MB | 약 16.0 MB (**-62%**, 추정 8~12 MB보다 큼: 정점이 이미 28 B로 압축돼 있고 서브메시 인덱스가 대부분) |
| `World.unity` | 32,066,148 B | 31,942,094 B (거의 같음: 씬이 32 MB인 이유는 메시가 아니라 다른 직렬화 내용, #240의 후속 조사 필요) |

검증:
- EditMode: `WorldMeshBlobTests` 4/4, `SurfaceIdTests` 10/10, `WorldMeshBankTests`(World.unity를 열어 필터 3,485개 전부 메시가 채워지고 정점 수·이름 일치) 통과. 전체 1042건 중 1041 통과, 실패 1건은 `RecoilPatternTests.StandingStill_InSights_...`(S7의 작업 중인 `weapons.json`·`WeaponDef.cs` 변경, 이 작업과 무관).
- 결정성: 같은 시드로 `WorldBuilder.Build`를 두 번 돌려 `World_Meshes.bytes` SHA-256이 **동일**(`e7a6232b...`). 반면 `World.unity`와 식생 YAML `World_Meshes.asset`은 실행마다 해시가 다르다(fileID 무작위, 06 B5에 적힌 기존 비결정성이며 이번 변경 전부터).
- 미검증: 플레이어 빌드·PlayMode에서 `WorldMeshBank`가 `Awake` 순서 때문에 빈 메시를 보는 경우(소리 나는 PlayMode 금지로 안 돌림). 로딩 시간 영향 미측정.
- 재생성 부수 효과: `World_DecorLamp/Glass/ShaftLamp.mat` 3개의 렌더 상태 값이 소폭 바뀜(Unity가 재저장, 기능 영향 없음으로 보이나 확인 필요).
