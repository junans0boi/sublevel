# Fish-Net 4.7.3: server + client in ONE process, headless PlayMode test (Issue #7)

Status: research only. Nothing here was executed; claims are from package source and official docs, with file/URL cited. Items marked **UNVERIFIED** need a first spike run.

Sources
- PKG = `Library/PackageCache/com.firstgeargames.fishnet@382980e7eed4/` (resolved from `Packages/manifest.json` `...FishNet.git?path=/Assets/FishNet#4.7.3`).
  - Note: `PKG/package.json` says `"version": "4.7.2"` and `NetworkManager.FISHNET_VERSION = "4.7.2"` (`PKG/Runtime/Managing/NetworkManager.cs:~L235`) even though the git tag is 4.7.3. Upstream versioning quirk; do not treat it as a mismatch.
- DOCS = https://fish-networking.gitbook.io/docs/llms-full.txt (full docs corpus; individual page URLs cited by title).

## 1. Two designs

| Design | What it is | Verdict |
|---|---|---|
| A. Host (1 NetworkManager, `ServerManager.StartConnection()` then `ClientManager.StartConnection()`) | Docs "Starting FishNet's Connections": `StartHost(){ StartServer(); StartClient(); }` (DOCS L~2490, `ConnectionManager.cs` sample). | Simplest. Server and client share the same NetworkObject instances, so "observed on the client" is not a real replication test (SyncVar is same object). `ClientManager.ParseAuthenticated` skips most work when `IsServerStarted` (`PKG/Runtime/Managing/Client/ClientManager.cs:692-744`). Good for smoke tests only. |
| B. Two NetworkManagers (one server-only, one client-only) in one process, Tugboat over localhost UDP | Docs: "Fish-Networking allows an infinite number of NetworkManagers" (DOCS L71). Code: `PersistenceType.AllowMultiple` (`NetworkManager.cs:48-62, 450`). | Required for real replication assertions (client gets its own instantiated copy via `ClientObjects`). Needs reflection to set persistence (see 2). |

Recommendation: B for replication/SyncVar tests; A only for "does the stack boot".

## 2. Minimal setup via code (design B)

Facts that dictate the shape:
1. `NetworkManager.Awake()` runs immediately on `AddComponent` (`NetworkManager.cs:257`). It calls `ValidateSpawnablePrefabs`, which logs an error and aborts if `SpawnablePrefabs == null` and the object is in a scene (`NetworkManager.cs:489-512`, abort at L260). So the GameObject must be **inactive** while configuring, then `SetActive(true)`.
2. `_persistence` is `[SerializeField] private`, default `DestroyNewest` (`NetworkManager.cs:225-227`); there is no public setter (grep of `PKG/Runtime`: only the field and `NetworkManagerEditor`). With default, the second NetworkManager is `DestroyImmediate`d (`CanPersist`, L446-482). Set it to `AllowMultiple` by reflection while the GO is inactive (before Awake).
3. `SpawnablePrefabs` has a public setter (`NetworkManager.QOL.cs:62-66`). A `SinglePrefabObjects` (`PKG/Runtime/Managing/Object/PrefabObjects/SinglePrefabObjects.cs`, `AddObject(...)`) is a `ScriptableObject` collection; `Awake` calls `SpawnablePrefabs.InitializePrefabRange(0)` which assigns `PrefabId` (`ManagedObjects.InitializePrefab`, `ManagedObjects.cs:364-381`). Give each manager its own instance, but the same prefab list in the same order so ids match.
4. Transport: `TransportManager.Transport` is a public field (`TransportManager.cs:65`); if null, `TryAddDefaultTransport` adds `FishNet.Transporting.Tugboat.Tugboat` (`TransportManager.cs:265-269`). Add Tugboat yourself to set port/address. Tugboat defaults: port 7770, client address "localhost", `_enableIpv6 = true`, `_reuseAddress = false`, `_stopSocketsOnThread = false` (`PKG/Runtime/Transporting/Transports/Tugboat/Tugboat.cs:20-80`). Public setters: `SetPort(ushort)`, `SetClientAddress(string)`, `SetServerBindAddress` (Tugboat.cs:382-430).
5. Managers (Server/Client/Time/Scene/Observer/etc.) are auto-created by `GetOrCreateComponent` (`NetworkManager.cs:322-343`). No ObserverManager conditions are required (the docs add SceneCondition only for scene-based filtering, DOCS L~2460).
6. `Start()` calls `ServerManager.StartForHeadless()`, which only auto-starts the server under `UNITY_SERVER && !UNITY_EDITOR` (`ServerManager.cs:~290-303`). Not applicable to Editor PlayMode tests (the Editor never defines it), so no accidental autostart.

Sketch (unverified, adapt to your `Game.Tests.PlayMode`):

```csharp
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

static NetworkManager CreateNm(string name, ushort port, NetworkObject[] prefabs)
{
    var go = new GameObject(name);
    go.SetActive(false);                                   // block Awake
    var nm = go.AddComponent<NetworkManager>();            // Awake deferred while inactive
    typeof(NetworkManager).GetField("_persistence",
        BindingFlags.Instance | BindingFlags.NonPublic)
        .SetValue(nm, NetworkManager.PersistenceType.AllowMultiple);
    typeof(NetworkManager).GetField("_dontDestroyOnLoad",
        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(nm, false);   // optional
    var po = ScriptableObject.CreateInstance<SinglePrefabObjects>();
    po.AddObjects(prefabs, checkForDuplicates: false, initializeAdded: false);
    nm.SpawnablePrefabs = po;
    var tugboat = go.AddComponent<Tugboat>();
    tugboat.SetPort(port);
    go.SetActive(true);                                    // Awake runs now
    return nm;
}
```

Port: 7770 is the Tugboat default (Tugboat.cs:~L64). Use a fixed non-default port per test run (e.g. 7770+n) or `StartConnection(ushort port)` on the server (`ServerManager.cs:367-372`) and `ClientManager.StartConnection(string address, ushort port)` (`ClientManager.cs:340-346`). `_reuseAddress` is false by default, so a leaked server from a previous test makes the next bind fail; see teardown.

## 3. StartConnection order and awaiting "connected"

Order (docs + code): start server first, wait until it is Started, then start client.
- `ServerManager.StartConnection()` / `ClientManager.StartConnection()` just forward to `Transport.StartConnection(bool server)` (`ServerManager.cs:357-372`, `ClientManager.cs:323-346`). Both return `bool` (false = could not start).
- Docs' host order: `StartServer(); StartClient();` (DOCS "Starting FishNet's Connections").
- State events (all `LocalConnectionState` = Stopped/Stopping/Starting/Started, `PKG/Runtime/Transporting/ConnectionStates.cs:7-24`):
  - `ServerManager.OnServerConnectionState(ServerConnectionStateArgs)` (`ServerManager.cs:39`), `ServerManager.Started` (L51).
  - `ClientManager.OnClientConnectionState(ClientConnectionStateArgs)` (`ClientManager.cs:47`), `ClientManager.Started` (L61).
  - `ServerManager.OnRemoteConnectionState` (L47) fires per remote client.
  - `ClientManager.OnAuthenticated` (`ClientManager.cs:38`, invoked L749).
- Caution: `NetworkManager.IsClientStarted` is `ClientManager.Started && ClientManager.Connection.IsAuthenticated` (`NetworkManager.QOL.cs:38`), so it is false between transport-Started and authentication. Without an Authenticator, authentication is automatic once the server accepts the connection (`ServerManager.InitializeOnce_Internal` only wires an authenticator if present, L270-273). For a "fully connected" wait use `clientNm.IsClientStarted` (or `ClientManager.OnAuthenticated`), not only `ClientManager.Started`.
- Everything is pumped from Unity `Update` through `TimeManager` (`Tugboat.Initialize` subscribes `TimeManager.OnUpdate`, Tugboat.cs:119-123; `TimeManager.TickUpdate` L368-390). Therefore connect-waits must yield frames, never block the main thread.

Await pattern (UnityTest coroutine, **UNVERIFIED** timing):
```csharp
[UnityTest] public IEnumerator Connects() {
    server = CreateNm("S", 7771, prefabs); client = CreateNm("C", 7771, prefabs);
    Assert.IsTrue(server.ServerManager.StartConnection(7771));
    yield return WaitUntil(() => server.IsServerStarted, 5f);
    Assert.IsTrue(client.ClientManager.StartConnection("127.0.0.1", 7771));
    yield return WaitUntil(() => client.IsClientStarted, 5f);   // transport started + authenticated
}
```
`WaitUntil` = a helper that yields `null` until predicate or `Time.realtimeSinceStartup` timeout, then `Assert.Fail`. Use realtime, not `WaitForSeconds`, since the frame rate in batchmode is not guaranteed.

## 4. Spawn a NetworkObject and observe it on the client

- Server: `serverNm.ServerManager.Spawn(GameObject or NetworkObject, NetworkConnection owner = null, Scene scene = default)` (`ServerManager.QOL.cs:118-140`). Requirements: the NetworkObject must be spawnable (`GetIsSpawnable()`, default `_isSpawnable`, `NetworkObject.cs:201`), must be a prefab registered in `SpawnablePrefabs` (PrefabId from step 2.3), and `Instantiate` it first (the object passed to `Spawn` is the instance, not the asset).
- Client side lookup: `clientNm.ClientManager.Objects.Spawned` is `IReadOnlyDictionary<int, NetworkObject>` keyed by ObjectId (`ManagedObjects.cs:31`; `ClientManager.Objects` at `ClientManager.cs:69`). Await `Spawned.TryGetValue(serverNob.ObjectId, out var clientNob)` over frames.
- Callbacks: `NetworkBehaviour.OnStartServer()` (`NetworkBehaviour.Callbacks.cs:133`), `OnStartClientCalled`/`OnStartServerCalled` flags (L17-22), `IsSpawned` (`NetworkBehaviour.cs:28`).
- Prefab source: Fish-Net registers *prefab assets*. Simplest: a real prefab asset under `Assets/_Project/Tests/PlayMode/` (or Resources) with a NetworkObject + a test NetworkBehaviour, referenced from the test via `Resources.Load`/`AssetDatabase`. Using an inactive scene template GameObject as a "prefab" (`SetActive(false)` + `Instantiate`) is **UNVERIFIED**; `NetworkObject` also validates `SceneId`/prefab state in editor via ID generators. Prefer a real asset. Any new prefab/asset goes in the ASSET_LEDGER only if it is third-party (it is not).
- Design A pitfall: with host there is a single instance, so `clientNob == serverNob`.

## 5. SyncVar replication

- API: `SyncVar<T>` (`PKG/Runtime/Object/Synchronizing/SyncVar.cs:18`), `Value` getter/setter (L77-81), `event OnChange(T prev, T next, bool asServer)` (L114-116), ctor `SyncVar(T initial, SyncTypeSettings settings = new())` (L150).
- Declare as a field on a `NetworkBehaviour`: `private readonly SyncVar<int> _hp = new();`. Default writes are server-authoritative: set `.Value` on the server instance only after the object is spawned/initialised (`IsSpawned`/`OnStartServer`). Replication happens on the next send tick, not instantly, so await frames: `yield return WaitUntil(() => clientNob.GetComponent<T>().Hp.Value == 42, 5f)`.
- Weaver: the Fish-Net ILPP (`PKG/CodeGenerating/ILCore/FishNetILPP.cs:24-46`) processes any assembly that references `FishNet.Runtime`, skipping names starting with `Unity.`/`UnityEngine.`/`UnityEditor.` or containing `Editor`. So a test NetworkBehaviour inside `Game.Tests.PlayMode` IS weaved provided the asmdef references FishNet.Runtime. (Name check: do not name the test asmdef with "Editor"; `Game.Tests.EditMode` would be skipped, which is fine.)
- Gameplay assemblies in `Assets/_Project/Scripts/{Core,Net,...}` that define NetworkBehaviours/SyncVars must also reference FishNet.Runtime.

## 6. Teardown between tests

- Stop client first, then server: `Tugboat.Shutdown()` does `StopConnection(false); StopConnection(true)` (Tugboat.cs:488-493) and is also called from `Tugboat.OnDestroy` (L125-130). Explicit order in `[UnityTearDown]`: `client.ClientManager.StopConnection(); server.ServerManager.StopConnection(sendDisconnectMessage: true);` then yield until both `Started == false` (frames), then `Object.Destroy(nm.gameObject)` for both, plus `Object.Destroy(scriptableObject prefab collections)` you created.
- `NetworkManager.OnDestroy` removes the instance from the static `_instances` list (`NetworkManager.cs:~L369`); `Instances` also prunes null entries (L76-95). `InstanceFinder.NetworkManager` caches and, with multiple managers, returns the first one and logs a warning (`PKG/Runtime/InstanceFinder.cs:27-45`). In gameplay code use the injected/owned `NetworkManager` (e.g. `NetworkBehaviour.NetworkManager`), not `InstanceFinder`, so both worlds work in the test.
- `Tugboat._reuseAddress` default false and the socket stops asynchronously only if `_stopSocketsOnThread` is true (default false) (Tugboat.cs:20-36), so with defaults the stop is synchronous. Use a fresh port per test anyway, to avoid TIME_WAIT/leak flakiness.
- `[UnitySetUp]`/`[UnityTearDown]` must be used (coroutine), not plain `[SetUp]`, since cleanup needs frames.
- Statics to reset: `NetworkManager._instances` (self-cleaning), `NetworkManager.LaunchTimestamp`/`StartingRpcLinkIndex` (static internals, set when `_instances.Count == 0`, `NetworkManager.cs:181-191, 268-273`); no manual reset is needed.

## 7. asmdef references

- Runtime assembly name: `FishNet.Runtime` (`PKG/Runtime/FishNet.Runtime.asmdef`; `autoReferenced: true`, `allowUnsafeCode: true`). Its own references are by GUID: `GameKit.Dependencies` etc.
- Add to `Game.Tests.PlayMode.asmdef` (currently `["Game.Core", "UnityEngine.TestRunner"]`, repo file `Assets/_Project/Tests/PlayMode/Game.Tests.PlayMode.asmdef`): `"FishNet.Runtime"`. Because `GameKit.Dependencies` types (`GameKit.Dependencies.asmdef` in `PKG/Runtime/Plugins/GameKit/Dependencies/`) may appear in signatures, also add `"GameKit.Dependencies"` if the compiler asks (only when you use those types directly). Also add `"UnityEngine.TestRunner"` is already there; `"UnityEditor.TestRunner"` is NOT needed for PlayMode runtime tests.
- Transport namespace/types: `FishNet.Transporting.Tugboat.Tugboat` lives in `FishNet.Runtime` (no separate asmdef; `PKG/Runtime/Transporting/Transports/Tugboat/`). Only `Synapse` has its own asmdef.
- The codegen assembly `Unity.FishNet.Codegen` is Editor-only (`PKG/CodeGenerating/Unity.FishNet.CodeGen.asmdef`: `includePlatforms: ["Editor"]`); do not reference it.
- Namespaces used: `FishNet.Managing`, `FishNet.Managing.Object`, `FishNet.Object`, `FishNet.Object.Synchronizing`, `FishNet.Transporting`, `FishNet.Transporting.Tugboat`, `FishNet.Connection`.

## 8. Batchmode notes and known issues

Verified from source:
- No `Application.isBatchMode`/headless branch exists in `PKG/Runtime` (grep). The only "headless" logic is `StartOnHeadless`, compiled for `UNITY_SERVER && !UNITY_EDITOR` (`ServerManager.cs:~290`); and `LoggingConfiguration` "Headless Logging" (DOCS "Logging", L3969). Editor `-batchmode` PlayMode tests therefore run the normal code path.
- `NetworkManager` sets `Application.runInBackground = true` by default and re-asserts it in LateUpdate (`NetworkManager.cs:208, 526-531, 406-415`). Useful: batchmode has no focus, but runInBackground is enforced anyway.
- `UpdateFramerate` only sets `Application.targetFrameRate` when `frameRate > 0` (`NetworkManager.cs:~395-440`); batchmode/`-nographics` has no vsync, so tick rate and frame loop are driven by TimeManager (tick default 30 Hz). Keep timeouts generous (>= 5 s) for CI.
- `Time.timeScale`/`WaitForSeconds`: do not use them for connection waits (see 3).

Unverified / to check in the first spike:
- Two managers in one process with a shared PlayerLoop: each `TimeManager` is a MonoBehaviour driven by its own Update (`TimeManager.TickUpdate`), expected fine. **UNVERIFIED**: any static state shared between two NMs (e.g. `NetworkObject`/prefab `SpawnableCollectionId`/`PrefabId` are written onto the shared prefab asset by `InitializePrefab`; both managers write identical values if the prefab lists are identical, so keep order identical).
- Loopback UDP in macOS sandbox/CI: Tugboat needs a UDP bind on 127.0.0.1/::; `_enableIpv6 = true` by default. If IPv6 is disabled on a CI host, set it false via reflection on `_enableIpv6` (Tugboat.cs ~L56). Use "127.0.0.1" not "localhost" for the client address to avoid v6 resolution.
- Official docs for the multi-process testing flow (MPPM/ParrelSync, "Testing with Multiple Editors", DOCS L3624+) describe two *editors*, not two managers in one process; they do not document in-process dual-NM tests. The in-process dual-NM approach here is derived from source (`PersistenceType.AllowMultiple`, `InstanceFinder` multi-instance warning), not from a docs recipe.
- I found no documented batchmode-specific Fish-Net bug in the package source or docs corpus. (No GitHub issue search was done; only primary package/docs.)

## 9. Suggested first spike (acceptance for issues #8/#9)
1. Add `FishNet.Runtime` to `Game.Tests.PlayMode.asmdef`.
2. Test prefab asset with `NetworkObject` + `NetworkBehaviour` having `SyncVar<int>`.
3. `[UnitySetUp]` creates S and C via `CreateNm`, starts S, waits `IsServerStarted`, starts C, waits `IsClientStarted`.
4. Test: spawn on S, wait for `C.ClientManager.Objects.Spawned`, set SyncVar on S, wait for value on C.
5. `[UnityTearDown]`: stop C, stop S, wait for stopped, destroy GameObjects.
6. Run via `tools/unity.sh` in `-batchmode` once and record result back on #7.
