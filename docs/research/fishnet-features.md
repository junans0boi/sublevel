# Fish-Net 4.7.3 (bundled, free edition): what is usable for multiplayer (#158)

Method: read the resolved package `Library/PackageCache/com.firstgeargames.fishnet@382980e7eed4/` (git tag 4.7.3; package.json says 4.7.2). No network access, nothing run. "Real" = method bodies contain logic; "stub" = empty bodies compiled into the free edition.

| Feature | Free edition | Evidence |
|---|---|---|
| Client-side prediction (`[Replicate]`/`[Reconcile]`, replay, `PredictionManager`, `PredictionRigidbody`) | Real, usable | `Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs` (1515 lines of logic), `Managing/Prediction/PredictionManager.cs` (931), `Object/Prediction/PredictionRigidbody*.cs` |
| Interpolation / tick smoothing (`NetworkTransform`, `TickSmoothingManager`, `NetworkTickSmoother`) | Real, usable | `Generated/Component/NetworkTransform/NetworkTransform.cs` (2598), `Generated/Component/TickSmoothing/*` |
| Observer conditions (`NetworkObserver`, `DistanceCondition`, `GridCondition`, `SceneCondition`, `MatchCondition`, `OwnerOnly`, `HostOnly`) | Real, usable | `Observing/Conditions/*.cs` |
| Latency / loss / out-of-order simulator | Real, usable | `Managing/Transporting/LatencySimulator.cs` (public API on `TransportManager.LatencySimulator`) |
| Collider rollback / lag compensation (`RollbackManager`, `ColliderRollback`) | **Stub, Pro only** | `Plugins/ColliderRollback/Scripts/RollbackManager.cs` and `.Threaded.cs`: every `Rollback(...)` and `Return()` has an empty body; `ColliderRollback.cs` is only serialized fields. `NetworkManager.Pro.cs` exposes `RollbackManager`. The editor window links "FishNet Pro" (`Editor/Configuring/FIshNetGettingStartedEditor.cs:96`). |

Licence (#2): `LICENSE.txt` grants a worldwide, no-charge, royalty-free licence to use/modify the Software for a developed game (1.a); exclusion only for competing networking products (2.a), which does not apply to games (2.c). Pro is sold separately via Patreon/GitHub Sponsors (0.f); buying it is out of scope (CLAUDE.md rule 3). Legal fitness for commercial release remains a human check (rule 8, issue #2 stays open).

Verdict: prediction, interpolation, observer conditions and the latency simulator are usable as-is; **lag compensation (rollback) is Pro-only, so we implement our own server-side position history ring and rewind (#162).**
