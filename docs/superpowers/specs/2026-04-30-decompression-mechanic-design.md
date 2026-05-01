# Decompression Mechanic — Design

**Project:** Decompression v2 (s&box multiplayer horror game)
**Group:** A — Decompression mechanic
**Date:** 2026-04-30
**Status:** Approved, ready for implementation plan
**Builds on:** Group B (Player & Death systems) — `docs/superpowers/specs/2026-04-30-player-death-systems-design.md`

This is the second of three design groups. Group A delivers the centerpiece sabotage feature — saboteurs hack wall panels to vent station sections, killing everyone trapped inside. It depends on Group B's `Player.Kill(DeathCause.Decompression, hatchPosition)` API to actually kill players.

---

## 1. Goals and scope

Build the decompression sabotage loop end-to-end: a saboteur holds Use on a wall panel for 5 seconds, after which the targeted section enters a 4-second warning phase, then the hatch blows + perimeter doors slam shut + everyone still inside dies, then 10 seconds later a permanent blast door seals the breach and the section becomes traversable again. Each section can only be vented once.

**In scope**
- `Section` component with volume-based occupancy tracking and venting state machine
- `Hatch` visual component with three poses (closed, open-breach, blast-door-sealed)
- `SectionDoor` perimeter door visual that slams during venting
- `Panel` wall interaction with 5s hold-Use, synced red-glow visible to all clients
- `Player.IsSaboteur` flag (for Panel role gating; Group C populates it from real role assignment)
- Static `Section.Vented` cross-system event (for Group C win-checks and kill feed)
- One-shot rule per hatch: a sealed section cannot be vented again
- Test scaffold (one fully wired section in `minimal.scene`)
- Debug ConCmds: `decompv2_set_saboteur`, `decompv2_request_vent`, `decompv2_complete_hack`, `decompv2_section_state`

**Out of scope (handled in later groups)**
- Real role assignment (saboteur vs crew at round start) — Group C
- Tasks for crew to complete in sections — Group C
- Win conditions and round flow — Group C
- Vacuum exposure damage for players entering a venting section through a missing/disabled door — left to a future polish pass
- Animated transitions on Hatch and SectionDoor — placeholders use instant pose swaps; cinematic animation is polish

---

## 2. Key decisions and rationale

| Decision | Choice | Rationale |
|---|---|---|
| Section shape | Volume-based, multi-collider supported | Easiest to author and debug. L-shaped rooms can stack multiple BoxColliders inside one Section. |
| Hatch:Section ratio | Strict 1:1 | Brief explicitly ties "hatch vented" → "section sealed forever". Multiple hatches per section adds level-design surface without adding gameplay clarity. |
| Permanent seal vs reopen | Reopen — section traversable again after blast door | User design change from original brief. Tasks need to happen in every room; permanent seal would lock crew out. |
| Vent timeline | 5s hack → 4s warning (escape window) → hatch blows + doors slam + kill → 10s vacuum → blast door + doors reopen | Warning is the dramatic escape window. Doors slam at vent moment (not warning start) so the warning truly is escapable. |
| Section perimeter doors during vent | Slam at vent moment, reopen when blast door seals | Physically prevents rescue runs into vacuum. Creates the visual beat of "doors slamming as the hatch blows". |
| Panel:Hatch ratio | Strict 1:1 | Single `Panel.TargetSection` reference is cleaner. Level designers can place panels in same section as hatch (high-risk-saboteur) or elsewhere (stealth) for tactical variety. |
| Hack interruption rule | Reset to 0% on saboteur release/walk-away. No crew counterplay via the panel itself. | On-genre for horror — crew witnesses but can't actively cancel via the panel. Group C may add melee or other counterplay paths. |
| Hack progress visibility | Synced via `(HackingConnectionId, HackStartTime)` so all clients see the glow | Crew needs to see "this panel is being hacked" to react. Per-frame RPC traffic is avoided by syncing only the start time and computing the ramp client-side. |
| Role gating | `Player.IsSaboteur` on the existing Player component, populated by Group C, read by Panel | Single source of truth for role state. Forward-compatible with whatever role-assignment system Group C builds. |
| Architecture | Section-centric state machine; Hatch/SectionDoor/Panel are reactive followers | One owner of lifecycle. Visuals derive from synced state — same pattern that worked in Group B. |
| Authority model | Server-authoritative throughout; `[Sync(SyncFlags.FromHost)]` for state, `[Rpc.Host]` for triggers | Same as Group B. Owner-authoritative `[Sync]` would let saboteurs forge state. |
| Saboteur immunity | None — saboteur dies in their own vent if they're still in the section | Tactical: panel placement and timing matter. Saboteur must escape during warning if their panel is in their target section. |
| Late-joiner handling | Reactive visuals derive from synced `Section.State` — no replay of one-shot effects | Same as Group B. Joiners see the right world; they don't hear klaxons that already played. |

---

## 3. Architecture overview

The Decompression mechanic lives in `Code/Decompression/`. Server-authoritative state, `[Sync(SyncFlags.FromHost)]` for persistent state, `[Rpc.*]` for one-shot triggers, visual components react locally to synced state.

Four components, clear responsibilities:

- **`Section`** — the orchestrator. Owns the venting state machine (`Idle → Warning → Venting → Sealed`), tracks occupants via box-collider triggers, references its `Hatch` and list of `SectionDoor`s. State is `[Sync(SyncFlags.FromHost)]`. `RequestVent()` is `[Rpc.Host]`.
- **`Hatch`** — visual only. Has a back-reference to its Section. Three pose states. `OnUpdate` reads the Section's state and switches visuals.
- **`SectionDoor`** — visual-only perimeter door. Two states: open, closed. Reads its Section's state.
- **`Panel`** — wall interaction surface implementing s&box's pressable interface. Local 5s use-timer per-player; on completion calls `TargetSection.RequestVent()`. Red-glow material parameter visible to all clients via synced hack state.

Cross-system interface (used by Group C):
- `Section.Vented` static event — fires once when a section enters Venting, with the section + list of killed players.
- `Panel.Enabled` — Group C disables for non-saboteurs (although `IsSaboteur` gating is inside Panel itself).
- `Section.RequestVent()` — Group C can also trigger via alternative paths.
- `Player.IsSaboteur` `[Sync(SyncFlags.FromHost)]` — set by Group C role assignment, read by Panel.

---

## 4. Components

### `Section.cs` — Code/Decompression/Section.cs

```csharp
public enum VentingState { Idle, Warning, Venting, Sealed }

public sealed class Section : Component
{
    [Property] public string DisplayName { get; set; } = "";
    [Property] public Hatch Hatch { get; set; }
    [Property] public List<SectionDoor> Doors { get; set; } = new();
    [Property] public float WarningDuration { get; set; } = 4f;
    [Property] public float VacuumDuration { get; set; } = 10f;

    [Sync(SyncFlags.FromHost)] public VentingState State { get; private set; } = VentingState.Idle;
    [Sync(SyncFlags.FromHost)] public float StateEnteredAt { get; private set; }

    public IReadOnlyCollection<Player> Occupants => occupants;
    private readonly HashSet<Player> occupants = new();

    public static event Action<Section, IReadOnlyList<Player>> Vented;

    [Rpc.Host]
    public void RequestVent();
}
```

Internal logic:
- Volume(s): one or more `BoxCollider` triggers as children of the Section GameObject.
- `OnTriggerEnter/Exit` (host-only) updates `occupants`.
- `OnUpdate` (host-only) drives the state machine: when `Time.Now - StateEnteredAt >= WarningDuration` in `Warning`, transition to `Venting`; etc.
- `RequestVent` early-outs if `State != Idle`.
- On entering `Venting`, host iterates `occupants` and calls `Player.Kill(DeathCause.Decompression, Hatch.WorldPosition)` for each, then fires `Section.Vented` via a `[Rpc.Broadcast] OnSectionVented(...)` so the static event triggers on every client.

### `Hatch.cs` — Code/Decompression/Hatch.cs

```csharp
public sealed class Hatch : Component
{
    [Property] public Section Section { get; set; }
    [Property] public GameObject ClosedVisual { get; set; }
    [Property] public GameObject OpenBreachVisual { get; set; }
    [Property] public GameObject BlastDoorVisual { get; set; }
}
```

`OnUpdate` reads `Section.State` and toggles which child visual is enabled:
- `Idle` or `Warning` → `ClosedVisual` enabled, others disabled
- `Venting` → `OpenBreachVisual` enabled
- `Sealed` → `BlastDoorVisual` enabled

### `SectionDoor.cs` — Code/Decompression/SectionDoor.cs

```csharp
public sealed class SectionDoor : Component
{
    [Property] public Section Section { get; set; }
    [Property] public GameObject DoorMesh { get; set; }
    [Property] public Vector3 OpenLocalOffset { get; set; } = Vector3.Up * 100f;
}
```

`OnUpdate` lerps `DoorMesh.LocalPosition` between `OpenLocalOffset` (open) and zero (closed) over ~0.4s. Closed during `Venting`; open in all other states.

### `Panel.cs` — Code/Decompression/Panel.cs

```csharp
public sealed class Panel : Component
{
    [Property] public Section TargetSection { get; set; }
    [Property] public ModelRenderer Renderer { get; set; }
    [Property] public float HoldDuration { get; set; } = 5f;

    [Sync(SyncFlags.FromHost)] public Guid HackingConnectionId { get; set; }
    [Sync(SyncFlags.FromHost)] public float HackStartTime { get; set; }

    [Rpc.Host]
    public void BeginHack();

    [Rpc.Host]
    public void EndHack();
}
```

Implements s&box's pressable interface (current name verified at implementation time). Behavior:
- On `Press` from a player whose `IsSaboteur == true`: call `BeginHack()`. Host sets `HackingConnectionId` to caller (only if currently null) and `HackStartTime = Time.Now`.
- On `Release` or walk-out-of-reach: call `EndHack()`. Host clears `HackingConnectionId` (only if caller matches).
- `OnUpdate` on every client: if `HackingConnectionId` is set, render glow scaled to `clamp01((Time.Now - HackStartTime) / HoldDuration)`. If null, no glow.
- `OnUpdate` on host: if `(Time.Now - HackStartTime) >= HoldDuration`, clear `HackingConnectionId` and call `TargetSection.RequestVent()`.
- `OnUpdate` on host (defense in depth): if `HackingConnectionId` set but the connection is invalid, or that connection's player has `IsSaboteur == false`, clear `HackingConnectionId`.

Crew pressing Use is silently ignored by `BeginHack` (early-out if caller is not a saboteur).

### `Player.cs` addition (in Code/Player/Player.cs)

```csharp
[Sync(SyncFlags.FromHost)] public bool IsSaboteur { get; private set; }

[Rpc.Host]
public void SetSaboteur(bool value) => IsSaboteur = value;
```

### Debug commands (in Code/Debug/DebugCommands.cs)

- `decompv2_set_saboteur <connectionDisplayName> <true|false>` — host-only. Calls `target.SetSaboteur(value)`.
- `decompv2_request_vent <sectionDisplayName>` — host-only. Finds the named section, calls `RequestVent()`.
- `decompv2_complete_hack` — local player. Force-completes whichever panel they're aiming at (useful to skip the 5s wait).
- `decompv2_section_state <sectionDisplayName>` — any client. Logs the synced state for replication verification.

---

## 5. Lifecycle state machine

```
                                     Section.RequestVent() [Rpc.Host]
                                     (early-out if not Idle)
                              ┌──────────────────────────────────────┐
                              │                                      │
                              ▼                                      │
                        ┌──────────┐                                 │
                        │   Idle   │                                 │
                        └────┬─────┘                                 │
                             │ Panel hold-Use 5s reaches HoldDuration│
                             ▼                                       │
                        ┌──────────┐  duration = WarningDuration     │
                        │ Warning  │  • klaxon plays everywhere       │
                        │          │  • Hatch jitters                 │
                        │          │  • doors stay open               │
                        └────┬─────┘                                 │
                             │ host: stateTimer >= WarningDuration   │
                             ▼                                       │
                        ┌──────────┐                                 │
                        │ Venting  │  duration = VacuumDuration       │
                        │          │  on entry (host):                │
                        │          │  • Player.Kill each occupant     │
                        │          │  • Section.Vented broadcast      │
                        │          │  on entry (every client):        │
                        │          │  • Hatch → OpenBreachVisual      │
                        │          │  • SectionDoors slam closed      │
                        │          │  • vacuum SFX loop               │
                        └────┬─────┘                                 │
                             │ host: stateTimer >= VacuumDuration    │
                             ▼                                       │
                        ┌──────────┐                                 │
                        │  Sealed  │  TERMINAL                        │
                        │          │  on entry (every client):        │
                        │          │  • Hatch → BlastDoorVisual       │
                        │          │  • SectionDoors open             │
                        │          │  • vacuum SFX stops              │
                        └──────────┘                                 │
```

### Per-state cue table

| State | Host-side | Every-client visual | Every-client audio |
|---|---|---|---|
| `Idle` | tracks occupants | Hatch closed, doors open, panels idle | none |
| `Idle` (panel pressed by saboteur) | tracks `HackingConnectionId` | panel glows red proportional to `(Time.Now - HackStartTime) / HoldDuration` | local panel hum (loops while held) |
| `Warning` | runs 4s timer | Hatch plays jitter loop animation | klaxon loop, plays from Section center |
| `Venting` (entry) | calls `Player.Kill` on each occupant, fires `Section.Vented` | Hatch swaps to OpenBreach; SectionDoors slam | rushing-wind vacuum loop starts; door slam thud once |
| `Venting` (during) | runs 10s timer | OpenBreach visible, doors closed | vacuum loop continues |
| `Sealed` (entry, terminal) | sets state, stops timer | Hatch swaps to BlastDoor; SectionDoors open | vacuum loop stops; metal-grind blast door slam once |

Audio assets are placeholder `.sound` events for now (silent), same approach as Group B's death stings.

---

## 6. Networking and data flow

### Sync classification

| Data | Mechanism | Reason |
|---|---|---|
| `Section.State` | `[Sync(SyncFlags.FromHost)]` | Persistent state. Late joiners must see correct visuals. |
| `Section.StateEnteredAt` | `[Sync(SyncFlags.FromHost)]` | Lets clients compute "how long has this state been active" for animation interpolation. |
| `Section` occupancy | host-only | Only host needs to enumerate occupants at kill moment. |
| `Player.IsSaboteur` | `[Sync(SyncFlags.FromHost)]` | Read by Panel on each client to gate hold-Use. |
| `Panel.HackingConnectionId` | `[Sync(SyncFlags.FromHost)]` | Tells every client which player is currently hacking — drives glow visibility for spectators. |
| `Panel.HackStartTime` | `[Sync(SyncFlags.FromHost)]` | Clients compute `progress = (Time.Now - HackStartTime) / HoldDuration` for the glow ramp. |
| `Section.Vented` event | static event, fired on every client via `[Rpc.Broadcast] OnSectionVented` | Cross-system notification. Group C subscribers gate on `Networking.IsHost` if they need host-only logic. |

### Hack flow on the wire

```
[saboteur client]   Panel.OnPress(player)
                       if !player.IsSaboteur: ignore
                       call Panel.BeginHack()  // [Rpc.Host]
                                  │
                                  ▼  on host
[host]              if HackingConnectionId is null:
                       HackingConnectionId = caller
                       HackStartTime = Time.Now
                                  │
                                  ▼  via [Sync]
[every client]      Panel.OnUpdate sees HackingConnectionId set
                    → renders glow at (Time.Now - HackStartTime) / HoldDuration

[saboteur client]   if Use no longer pressed OR walked out of reach:
                    call Panel.EndHack()        // [Rpc.Host]
                                  │
                                  ▼  on host
[host]              if caller is the current hacker: clear HackingConnectionId

[host OnUpdate]     if HackingConnectionId set AND
                       (Time.Now - HackStartTime) >= HoldDuration:
                       clear HackingConnectionId
                       call TargetSection.RequestVent()
```

### Section state-change flow

```
[any client]        Panel completes hack → TargetSection.RequestVent()  // [Rpc.Host]
                                  │
                                  ▼  on host
[host]              if Section.State != Idle: return (one-shot guard)
                    State = Warning
                    StateEnteredAt = Time.Now
                                  │
                                  ▼  via [Sync]
[every client]      OnUpdate sees state flip → klaxon plays, hatch jitters

[host OnUpdate]     when Time.Now - StateEnteredAt >= WarningDuration:
                       State = Venting
                       StateEnteredAt = Time.Now
                       for each occupant in occupants:
                           player.Kill( Decompression, Hatch.WorldPosition )
                       OnSectionVented( killedConnectionIds )       // [Rpc.Broadcast]
                                  │
                                  ▼  via [Sync] + broadcast
[every client]      OnUpdate sees Venting → swap Hatch to OpenBreach,
                                            SectionDoors slam closed,
                                            vacuum loop starts
                    OnSectionVented body fires Section.Vented event

[host OnUpdate]     when Time.Now - StateEnteredAt >= VacuumDuration:
                       State = Sealed
                                  │
                                  ▼  via [Sync]
[every client]      OnUpdate sees Sealed → swap Hatch to BlastDoor,
                                           SectionDoors open,
                                           vacuum loop stops
```

### Late-join behavior

A connection joining mid-round receives the current state of every Section via `[Sync]`. Their local Hatch, SectionDoor, and Panel components run their `OnUpdate` reactions on the next frame and snap to the correct visual. They miss any one-shot RPC that already fired (klaxon start, vent moment) — same trade-off as Group B.

### Edge case: saboteur loses role mid-hack

Host-side `Panel.OnUpdate` checks each frame: if `HackingConnectionId` is set but the corresponding player has `IsSaboteur == false` (or the connection is invalid), clear `HackingConnectionId`. Glow stops everywhere; hack does not complete.

---

## 7. Failure modes

| Failure | Mitigation |
|---|---|
| `Section.RequestVent` called when `State != Idle` | Early-out at the top of `RequestVent`. |
| `Section.Hatch` is null when venting fires | Host logs warning at `Warning → Venting` transition; section stays in `Warning`, no kill, no state advance. |
| `Panel.TargetSection` is null | `BeginHack` early-outs with a warning log. Panel does not respond. |
| Saboteur disconnects mid-hack | Host's `OnUpdate` checks `Connection.Find(HackingConnectionId)` is valid each frame; clears if not. |
| Saboteur stops being a saboteur mid-hack | Same `OnUpdate` check verifies `IsSaboteur == true` for the hacker. |
| Player walks into a section mid-vent | They become an occupant after the kill loop already ran — they don't die. Doors should physically prevent this. |
| Two saboteurs hack different panels for the same section | First panel's `RequestVent` wins. Second's is rejected by the `State != Idle` guard. Cosmetic: the second panel's glow keeps ramping until its own host-side timer hits, then triggers a no-op `RequestVent`. Acceptable; could be polished by also watching `TargetSection.State` on the host. |
| Section state replicated to client before its Hatch/Doors have spawned | Visual components null-check parent and skip the frame. |
| `Player.Kill` fails for one occupant | Host logs warning, continues kill loop for remaining occupants. |
| Host migration mid-vent | s&box host migration preserves `[Sync]` state. New host computes `Time.Now - StateEnteredAt` to pick up where the old host left off. |
| `Section.Vented` event has zero subscribers | Null-conditional invoke — no crash. |

---

## 8. Testing

### Debug commands

- `decompv2_set_saboteur <connectionDisplayName> <true|false>` — host-only.
- `decompv2_request_vent <sectionDisplayName>` — host-only, bypass the panel/hack.
- `decompv2_complete_hack` — local player, force-complete whatever panel they're aiming at.
- `decompv2_section_state <sectionDisplayName>` — any client, log synced state.

### Manual two-client test matrix

1. **Solo vent** — Host alone. `decompv2_request_vent test_section`. 4s warning, host dies, corpse drifts toward hatch, blast door 10s later.
2. **Bystander vent** — Host inside, A outside. Host dies; A unaffected; A sees full visual sequence.
3. **Escape vent** — Host walks out during warning. Host should NOT die.
4. **Mid-warning entry** — Host walks IN during warning. Host dies.
5. **Saboteur self-kill** — Saboteur hacks panel for own section. Dies in own vent.
6. **Crew can't hack** — Crew holds Use on a panel. No glow, no progress.
7. **Saboteur full hack** — Hold Use 5s. Glow ramps. Vent triggers.
8. **Hack interrupt** — Hold 3s, release. Glow disappears. Re-hold full 5s, vent triggers.
9. **Multi-client glow visibility** — Saboteur holds Use; crew client sees the panel glowing.
10. **Two saboteurs, same section** — Different panels, simultaneous hack. First to complete wins; second is rejected.
11. **Late join during vent** — Host vents; B joins mid-Venting; B sees correct visuals.
12. **Section traversable after seal** — Walk into sealed section. Doors open, no kill, no audio. Visible blast door cosmetic.
13. **One-shot enforcement** — `decompv2_request_vent` on a sealed section. No effect.

### Required test scaffold

`minimal.scene` extension:
- One `Section` GameObject with one `BoxCollider` child as occupancy volume, sized to enclose ~half the flatgrass map.
- One `Hatch` GameObject (with three child visual GameObjects: Closed, OpenBreach, BlastDoor — placeholder cubes/colored meshes).
- One or two `SectionDoor` GameObjects positioned at the volume boundary.
- One `Panel` GameObject placed inside or outside the section (level-design choice).
- All wired via `[Property]` references.

---

## 9. Public surface other groups depend on

```csharp
// Group C role assignment populates this:
player.SetSaboteur( true );

// Group A's Panel reads it:
//   if ( !pressingPlayer.IsSaboteur ) return;

// Group A fires this when a section vents:
//   Section.Vented?.Invoke( section, killedPlayers );

// Group C subscribes for win-checks and kill-feed:
Section.Vented += (section, killedPlayers) => {
    foreach ( var p in killedPlayers ) {
        // record kill in feed, check win condition
    }
};

// Group C may trigger vents through alternate paths (admin, special weapon):
section.RequestVent();

// Optional: disabling a Panel entirely (e.g., at round-end when no one
// should be able to hack regardless of role). Role-gating itself is
// already handled inside Panel via IsSaboteur — Group C does NOT need
// to disable Panels just because a player is crew.
panel.Enabled = false;
```

That is the entire surface. Group C should not reach into any other component in this design.
