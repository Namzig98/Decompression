# Player & Death Systems — Design

**Project:** Decompression v2 (s&box multiplayer horror game)
**Group:** B — Player & death foundations
**Date:** 2026-04-30
**Status:** Approved, ready for implementation plan

This is the first of three design groups for Decompression v2. It establishes the player and death primitives that the decompression mechanic (group A) and match flow (group C) will build on.

---

## 1. Goals and scope

Build the player foundation and death system so that any other system can kill a player by calling a single API, and the death — including ragdoll, vacuum tumble, spectator camera, dead chat, and HUD — is fully driven from that one call. Networking must be correct: state is server-authoritative, late joiners see a consistent world, and one-shot effects don't replay or get lost.

**In scope**
- Player prefab built on stock Facepunch `PlayerController`
- Single death entry point: `Player.Kill(DeathCause, Vector3)`
- Two death causes: `Decompression`, `Generic`
- Ragdoll corpses with cause-specific behavior (vacuum tumble vs normal fall)
- Spectator system (5s on corpse → cycle living players)
- Dead chat (voice + text, dead-only channel)
- Death HUD (cause-specific overlay + sound)
- Late-join handling (joins as spectator)

**Out of scope (handled in later groups)**
- Section/zone occupancy detection — group A
- Hatch venting, panel hacks, decompression sequencing — group A
- Role assignment, tasks, win conditions, round flow — group C
- Emergency button (the trigger that cleans up generic corpses) — group C
- Respawn within a round — explicitly excluded; dead is dead until next round

---

## 2. Key decisions and rationale

| Decision | Choice | Rationale |
|---|---|---|
| Player controller | Facepunch stock `PlayerController` | Movement, eye angles, Use input, animation already solved. Don't burn budget on movement. |
| Death cause taxonomy | `enum { Generic, Decompression }` | Among-Us-style instant death, easy to extend later if needed. |
| HP / damage model | None. Death is instant. | Genre-appropriate (Among Us / murder games). One method on the API. |
| Corpse strategy | Single ragdoll component, branches on cause | Simpler than two component types. Same code path with different physics config. |
| Decompression impulse direction | From the hatch position | Realistic look — body sucked toward the breach. Caller passes hatch position. |
| Generic corpse persistence | Until "emergency button" cleanup signal | Set dressing, atmosphere. Cleaned up via `CorpseCleanupSignal.RaiseGenericCleanup()` (group C will trigger). |
| Decompression corpse persistence | Despawn 30s after spawn | Prevents physics-tick load when many sections are vented in one round. |
| Spectator mode | 5s locked to corpse → cycle living players (third-person) | Sells the kill, then gives spectator something useful to do. |
| Dead chat | Voice + text, dead-only | User preference. Note: voice can leak info to nearby living players IRL — that's a player-side concern, not a design problem. |
| Ghost visibility | N/A | Spectator follows living players, no roaming ghost avatars exist. |
| Respawn within round | None | Genre-standard. Med-bay revive can be added in group C without changing this API. |
| Camera perspective | First-person while alive, third-person while spectating | Horror immersion alive, situational awareness dead. Stock controller supports both. |
| Death cam | Hybrid: 0.5s redout flash → third-person on corpse for the rest of the 5s | Sells the kill without making first-person players nauseous from a tumbling-corpse view. |
| Death UI | Subtle cause-specific text overlay (`VACUUM EXPOSURE`, `DECEASED`) | Horror-tone presentation; full "YOU DIED" Souls overlay is too loud for the genre. |
| Death audio | Cause-specific sting | Vacuum death is the game's signature; should sound nothing like a stab. |
| Component architecture | Decomposed, single-responsibility components (Approach 2) | Six small files instead of one god class. Each piece grows independently. |
| Authority model | Server-authoritative | `Kill()` is `[Rpc.Host]`. Only host writes `IsAlive` and spawns corpses. |

---

## 3. Architecture overview

Three GameObject types active during a round:

1. **Player prefab** — one per connected player. Stock `PlayerController` plus the six components below.
2. **Corpse prefab** — spawned at death from the player's pose. `ModelPhysics` + `Corpse` component. Lives in the scene independently of the Player GameObject.
3. **Spectator camera** — logical, not a separate GameObject. The dying player's `Spectator` component drives the existing `CameraComponent`.

Server-authoritative throughout. Game-state truth (alive/dead, corpses, cause) lives on the host and is `[Sync]`'d. One-shot reactions (sounds, screen flashes, camera transitions) run client-locally, triggered by `[Rpc.Broadcast]` or by `[Sync]` callbacks.

### Death flow

```
[wherever]   Player.Kill(cause, sourcePos)            // Rpc.Host — runs on host only
   │
[host]       if (!IsAlive) return                     // idempotency guard
             IsAlive = false                          // [Sync] → all clients
             Spawn networked Corpse prefab
             Set Corpse.Cause, Corpse.SourcePosition  // [Sync]
             Disable PlayerController, hide model
             Broadcast OnPlayerDied(cause, corpseId, sourcePos)
   │
[every       Player.Died?.Invoke(this, cause)         // static event; subscribers react
 client]     if (IsLocalPlayer) {
                Spectator.Begin(corpse)
                DeathHud.Play(cause)
             }
             PlayCauseSpecificSound(cause, transform.position)
   │
[every       Corpse.OnStart() runs locally
 client]     if (cause == Decompression)
                ApplyVacuumImpulseAndZeroG(SourcePosition)  // host only; ImpulseApplied flag
             else
                ConfigureNormalRagdoll()
```

---

## 4. Components

All components live under `Code/Player/` and `Code/Death/`.

### `Player.cs` — `Code/Player/Player.cs`

The aggregator and state holder on the player GameObject. Owns the public death API.

```csharp
public sealed class Player : Component
{
    [Sync] public bool IsAlive { get; private set; } = true;
    [Sync] public Guid OwnerConnectionId { get; set; }

    [Rpc.Host]
    public void Kill( DeathCause cause, Vector3 sourcePosition );

    [Rpc.Broadcast]
    private void OnPlayerDied( DeathCause cause, Guid corpseId, Vector3 sourcePosition );

    public static event Action<Player, DeathCause> Died;
}
```

`Kill()` is the only public death entry point. `sourcePosition` is the hatch position for `Decompression` and is required for the vacuum impulse direction. For `Generic` it is unused by the corpse — callers may pass `Vector3.Zero`, the attacker position, or anything else; the value is still synced to the corpse but `Corpse.OnStart()` only reads it on the `Decompression` branch. `Died` is the cross-system event channel — kill feeds, scoring, and saboteur win-checks subscribe to it without poking at `Player` internals.

### `DeathCause.cs` — `Code/Death/DeathCause.cs`

```csharp
public enum DeathCause { Generic, Decompression }
```

### `Corpse.cs` — `Code/Death/Corpse.cs`

Lives on the spawned ragdoll GameObject. Network-owned by the host.

```csharp
public sealed class Corpse : Component
{
    [Sync] public DeathCause Cause { get; set; }
    [Sync] public Vector3 SourcePosition { get; set; }
    [Sync] public Guid OriginalOwnerConnectionId { get; set; }
    [Sync] public bool ImpulseApplied { get; set; }

    protected override void OnStart();   // configures physics by cause; applies impulse on host once
    public void Cleanup();               // host-only despawn, idempotent
}

public static class CorpseCleanupSignal
{
    public static void RaiseGenericCleanup();   // despawns all Generic-cause corpses; host-only
}
```

For `Decompression`: gravity off, drag near zero, impulse `(transform.position - SourcePosition).Normal * F` plus a random spin torque, applied on host only when `!ImpulseApplied`, then `ImpulseApplied = true`. Despawns 30s after spawn.

For `Generic`: gravity on, normal drag, no impulse. Persists until `CorpseCleanupSignal.RaiseGenericCleanup()` fires.

### `Spectator.cs` — `Code/Player/Spectator.cs`

Lives on the player GameObject. Idle while alive; activates on death.

```csharp
public sealed class Spectator : Component
{
    enum Phase { Inactive, CorpseLock, FollowingLiving }

    public void Begin( Corpse? corpse );   // null = late-joiner, skip CorpseLock
    public void CycleNext();
    public void CyclePrevious();
}
```

State machine: `Inactive` → `CorpseLock` (5s timer, third-person fixed angle on corpse) → `FollowingLiving` (third-person over-shoulder on a living player; mouse-1/mouse-2 cycle). If passed a null corpse (late-joiner), skip `CorpseLock` and go directly to `FollowingLiving`.

If the currently-followed player dies mid-spectate, auto-cycle to the next living player. If no living players remain, fall back to a fixed map-overview camera.

### `DeadChat.cs` — `Code/Player/DeadChat.cs`

Lives on the player GameObject. Reacts to `Player.IsAlive` `[Sync]` changes by reconfiguring voice + text channels: alive ↔ alive only, dead ↔ dead only. No cross-traffic.

### `DeathHud.cs` — `Code/Player/DeathHud.cs`

Razor `PanelComponent` on the player GameObject. Listens for `OnPlayerDied` on the local player and plays:
- 0.5s redout fade.
- Cause-specific text fade-in: `VACUUM EXPOSURE` / `DECEASED`.
- Cause-specific death sting sound.

### `PlayerSpawner.cs` — `Code/Player/PlayerSpawner.cs`

One scene-level component. Spawns the player prefab when a connection joins. If the round is in progress (a flag this group exposes for group C to set later), the player spawns with `IsAlive = false` and `Spectator.Begin(null)` is called immediately — late joiners spectate until next round.

```csharp
public sealed class PlayerSpawner : Component
{
    [Property] public GameObject PlayerPrefab { get; set; }
    [Property] public List<GameObject> SpawnPoints { get; set; }

    public bool RoundInProgress { get; set; }   // set by group C; default false
}
```

---

## 5. Networking and data flow

### Two rules

1. Game-state truth lives on the host. Clients never write to `IsAlive`, never spawn corpses, never decide who's alive.
2. Visuals and one-shot reactions run on each client locally, triggered by either `[Sync]` state or `[Rpc.Broadcast]` events.

### Classification

| Data | Mechanism | Reason |
|---|---|---|
| `Player.IsAlive` | `[Sync]` from host | Persistent state. Late joiners must see it correctly. |
| Corpse GameObject existence | Network spawn (host) | Persistent. Late joiners must see existing bodies. |
| `Corpse.Cause`, `Corpse.SourcePosition`, `Corpse.ImpulseApplied` | `[Sync]` from host | Late joiners need this for correct rendering and to avoid re-applying impulse. |
| Corpse ragdoll bone positions | Engine physics network sync | Built-in. |
| `OnPlayerDied(cause, corpseId, sourcePos)` | `[Rpc.Broadcast]` | One-shot. Doesn't replay for late joiners — playing a death sting for a player who just connected would be incorrect. |
| `CorpseCleanupSignal.RaiseGenericCleanup()` | Host action on networked corpses | Result (corpses despawned) is persistent state. Not an RPC. |
| Spectator camera target / phase | Local only | Each dead player chooses independently. |
| Death HUD overlay state | Local only | Triggered by broadcast, runs client-side. |
| Dead chat membership | Derived from `IsAlive` `[Sync]` | Reactively reconfigured client-side. |

### Late-join flow

A connection joining mid-round triggers `PlayerSpawner` to instantiate a Player with `IsAlive = false`, no corpse, no death event broadcast. `Spectator.Begin(null)` skips the corpse-lock phase and goes straight to `FollowingLiving`. The late joiner sees existing corpses (synced), sees correct alive/dead state for everyone (synced), but does not hear or see effects from deaths that happened before they connected.

### Decompression-corpse late-join guard

When a late joiner receives a Decompression corpse, the corpse already has its current velocity from the engine's physics sync. Re-applying the impulse in `OnStart()` would double-punt the body. The `ImpulseApplied` `[Sync]` flag prevents this: host sets it true after the first impulse; late joiners see it true and skip the impulse code path.

---

## 6. Failure modes

| Failure | Mitigation |
|---|---|
| Double-kill in same tick | `Kill()` early-outs if `!IsAlive`. Idempotent. |
| Killed-but-disconnected player | Corpse is a separate networked GameObject. Disconnect destroys Player, leaves Corpse. |
| Decompression corpse re-impulsed on late-join | `ImpulseApplied` `[Sync]` flag. |
| Spectator's target dies mid-spectate | `FollowingLiving` checks target `IsAlive` each frame; auto-cycles. |
| No living players left to spectate | Falls back to map-overview camera. Round-flow code (group C) detects round end. |
| HUD effects play for non-local player | All local-only handlers gate on `Network.IsOwner` before running. |
| Voice chat leaks across alive/dead boundary mid-death | `DeadChat` reacts to `IsAlive` `[Sync]` callback, not on a poll. |
| Corpse despawn race | All despawns go through host-only, idempotent `Corpse.Cleanup()`. |
| Two systems try to kill the same player simultaneously | Idempotency guard handles it; only one corpse spawns. |

---

## 7. Testing

### Unit-level (where possible)

- `Player.Kill()` is idempotent — calling twice produces one corpse.
- `Corpse.OnStart()` configures physics correctly for each `DeathCause`.
- `Spectator` state machine transitions correctly with mock corpses, including `Begin(null)` skipping `CorpseLock`.

### Manual two- and three-client tests (mandatory)

1. Two clients. Host kills client A. Both clients see A's body, both hear the sting once. A enters spectate, host can be cycled to.
2. Same setup, A kills themselves via console — `[Rpc.Host]` routing still works.
3. Three clients. Host kills A and B simultaneously. Both deaths process correctly, two corpses, both spectate.
4. Host kills A. Client C joins late. C sees A's corpse and `IsAlive = false`, no death sting plays for C, `Spectator.Begin(null)` puts C straight into `FollowingLiving`.
5. Host kills A via Decompression with a hatch position. Corpse tumbles away from that position with zero gravity. New late-joiner sees the corpse in flight without re-applied impulse.
6. Host kills A via Generic. Corpse falls normally and stays. `decompv2_cleanup_corpses` (debug command) despawns it.

### Debug console commands

Stand-ins for what groups A and C will eventually call. Stay in the codebase as dev tools.

- `decompv2_kill <playerid> <cause>` — host-only, calls `Player.Kill()`.
- `decompv2_cleanup_corpses` — host-only, fires `CorpseCleanupSignal.RaiseGenericCleanup()`.
- `decompv2_kill_self` — convenience.

---

## 8. Public surface other groups depend on

The contract that groups A and C will build against:

```csharp
// Group A (decompression) calls this for each player in a vented section:
player.Kill( DeathCause.Decompression, hatchWorldPosition );

// Group C (match flow) calls this when a saboteur lands a melee hit:
victim.Kill( DeathCause.Generic, attackerPosition );

// Group C calls this from the emergency button interaction:
CorpseCleanupSignal.RaiseGenericCleanup();

// Group C reacts to deaths for win-condition checks:
Player.Died += (player, cause) => { /* check round end */ };

// Group C tells the spawner the round is in progress:
playerSpawner.RoundInProgress = true;
```

That is the entire surface. Groups A and C should not reach into any other component in this design.
