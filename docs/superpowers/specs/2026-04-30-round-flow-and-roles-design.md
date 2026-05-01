# Round Flow & Roles — Design (C1)

**Project:** Decompression v2 (s&box multiplayer horror game)
**Group:** C1 — Round flow and role assignment (first sub-project of Group C)
**Date:** 2026-04-30
**Status:** Approved, ready for implementation plan
**Builds on:**
- Group B — Player & Death systems (`docs/superpowers/specs/2026-04-30-player-death-systems-design.md`)
- Group A — Decompression mechanic (`docs/superpowers/specs/2026-04-30-decompression-mechanic-design.md`)

This is the third design overall (after Groups A and B). Group C is decomposed into three sub-projects:

- **C1 (this design)** — Round lifecycle, role assignment, win-condition framework + the saboteur-side win conditions.
- **C2** — Tasks. Placeable task objects + crew win condition via "all tasks complete".
- **C3** — Meetings & voting. Emergency button → call meeting → vote → eject; crew win via "all saboteurs voted out". Consumes the original "emergency button" idea.

---

## 1. Goals and scope

Build the round lifecycle end-to-end so that a host can start a match, players are assigned saboteur or crew roles, the round plays out with all four saboteur-side win conditions, the round ends with a results screen, and the cycle returns to lobby for another match. Out of the box this gives a working asymmetric horror game without tasks or voting — saboteur(s) try to kill crew, crew try to survive until the timer.

**In scope**
- `Match` component as the round-lifecycle orchestrator (Lobby → Round → RoundEnd → Lobby state machine)
- Manual host-trigger start + auto-start countdown after a configurable delay
- Saboteur count: hybrid auto-scale (1 for 3-7, 2 for 8+) with explicit override
- All four saboteur-side win conditions:
  - All crew dead
  - Time expired
  - 1 crew + 1+ saboteurs (1v1 endgame)
  - 0 saboteurs alive (becomes crew win — falls out of same check)
- `Match.EndRound(winner, reason)` API for C2/C3 to plug in their crew win conditions
- `Player.IsSaboteur` populated by C1 via the existing `Player.SetSaboteur` (Group A's API)
- Role reveal flash + persistent role badge + saboteur-sees-saboteur nametag highlighting
- Round timer HUD visible to all players
- Lobby auto-start countdown HUD
- Round end results overlay with winner + reason + role reveal
- Round-start full reset: corpses cleared, sections reset, players respawned alive, roles assigned
- Round-end → Lobby auto-transition after a brief results screen
- Late-joiner phase-aware spawning (spectator during Round/RoundEnd, alive in Lobby)
- Player-disconnect handling falls out of existing infrastructure (no special code needed)
- Debug ConCmds: `decompv2_start_round`, `decompv2_end_round`, `decompv2_match_state`
- Small Group A modification: `Section.Reset()` method so C1 can clean up vented sections at round start

**Out of scope (handled in later C-group sub-projects)**
- Tasks for crew to complete — C2
- Crew win via task completion — C2
- Emergency button / meeting / voting — C3
- Crew win via voting out all saboteurs — C3
- Audio polish (round-start sting, round-end fanfares) — placeholder/silent for now
- Map cycling between rounds — not planned; rounds always run on the same scene

---

## 2. Key decisions and rationale

| Decision | Choice | Rationale |
|---|---|---|
| Round-start trigger | Manual host trigger + auto-start countdown after lobby-entry delay | First version uses the manual trigger; auto-start layers on a countdown without changing the API surface. Either way `Match.StartRound()` is the single entry point. |
| Saboteur count | Hybrid: `SaboteurCountOverride = 0` means auto-scale (1 for 3-7 players, 2 for 8+); positive value forces explicit count | Auto-scale is the right default; override is the testing knob. |
| Round time limit | Configurable per-match, default 480s (8 minutes) | Genre-standard. Tunable for playtest. |
| Min players to start | `MinPlayersToStart = 3` (hard floor); 4 recommended | With 3 players, saboteur kills one and the 1v1 rule fires immediately — almost-always win. 4 is the smallest fair size. 3 is allowed for edge testing. |
| 1v1 endgame rule | Saboteur wins when 1 crew + 1+ saboteurs remain | Prevents endless stalling, locks in the "everyone except one" psychological-horror moment. |
| Phase model | Synced state machine: Lobby → Round → RoundEnd → Lobby | One owner of lifecycle. UI components are reactive followers. Same pattern as Group A's Section. |
| Round-end → Lobby transition | Auto-transition after a configurable RoundEnd duration (default 10s) | No manual action required. Host's `decompv2_start_round` shortcuts the next lobby countdown if desired. |
| Lobby auto-start countdown | Configurable, default 30s | Long enough for new joiners to settle; short enough that the wait isn't tedious. Host can short-circuit via `StartRound`. |
| World state reset | Full reset on each Round entry: corpses cleared, sections reset to Idle, players respawned alive, roles cleared and reassigned | Each round is a clean slate. Avoids "ghost state" persisting across rounds. |
| Role reveal | 3s full-screen flash at Round entry + persistent corner badge + saboteur-sees-saboteur red name marker | Sells the moment, keeps players oriented, supports coordinated multi-saboteur play. |
| Role indicator visibility | Persistent throughout Round and RoundEnd; hidden in Lobby | "Lobby = no roles" is intuitive. RoundEnd shows badge so players see their role on the results screen. |
| Saboteur visibility to other saboteurs | Yes — small red marker over other saboteurs' heads, only visible to saboteurs | Standard genre move. Required for coordinated 2-saboteur play. |
| Round timer visibility | Visible to all players | Symmetric info — both sides see the timer. Saboteur knowing time pressure is fine; crew planning around it is essential. |
| Late-joiner during Round | Spawn as spectator (uses Group B's existing late-join hook by setting `PlayerSpawner.RoundInProgress = true` during Round and RoundEnd) | Already-built infrastructure; consistent UX. |
| `RoundInProgress` flag transition timing | Set `true` on Round entry; stays `true` through RoundEnd; set `false` only on Lobby entry | Late joiners during RoundEnd should still spawn as spectators (so they see the results overlay), not pop in alive into a half-finished match. |
| Architecture | Single `Match` component as orchestrator; UI components reactive followers | Same shape as Group A's Section. Rejected the "split into Lobby / RoundController / EndScreen controllers" approach as overkill for one state machine. |
| Authority | Server-authoritative throughout: `[Sync(SyncFlags.FromHost)]` for state, `[Rpc.Host]` for triggers, `[Rpc.Broadcast]` for cross-client events | Same as Groups A and B. |
| Role-reveal sync race | Embed saboteur connection IDs in the broadcast event payload (`OnRoundStarted(Guid[] saboteurConnectionIds)`); RoleRevealOverlay reads from the payload, not from `Player.IsSaboteur` | Eliminates the race where the broadcast arrives before the [Sync] flag propagates. Persistent components (`RoleHud`, `Panel`) still read `IsSaboteur` since by the time they matter the flag has settled. |

---

## 3. Architecture overview

C1 lives in `Code/Match/`. Single `Match` component is the orchestrator; UI components are reactive followers.

```
                Match.StartRound()      Match.EndRound(winner, reason)
                       │                              │
                       ▼                              ▼
                 ┌──────────┐                  ┌──────────┐
                 │  Lobby   │ ────────────────▶│  Round   │
                 └──────────┘                  └──────────┘
                       ▲                              │
                       │                              ▼
                       │                       ┌──────────┐
                       └──── (auto, ~10s) ─────│ RoundEnd │
                                               └──────────┘
```

State machine drives everything. State is `[Sync(SyncFlags.FromHost)]`. UI components read it and render appropriate overlays.

**Cross-system integration points:**

- C1 calls `Player.SetSaboteur(bool)` (Group A's API) at round start (assign) and round end (clear).
- C1 calls `CorpseCleanupSignal.RaiseGenericCleanup()` and despawns Decompression corpses on round entry.
- C1 calls a new `Section.Reset()` method (small Group A addition) on every Section in the scene at round start.
- C1 sets `PlayerSpawner.RoundInProgress` so late joiners spawn as spectators.
- **C2 will call `Match.EndRound(MatchOutcome.Crew, "all tasks complete")`** when its task tracker fires.
- **C3 will call `Match.EndRound(MatchOutcome.Crew, "all saboteurs voted out")`** after a successful vote.
- C1 emits a static `Match.RoundStarted(Match, bool localIsSaboteur)` event on every client.
- C1 emits a static `Match.RoundEnded(Match, MatchOutcome, string reason)` event on every client.

Three load-bearing decisions:

1. **Single `Match` component** at scene root — one in the scene at startup.
2. **Phases as a synced enum**, not separate controllers.
3. **`Section.Reset()` is added to Group A** so C1 can wipe vented sections at round start.

---

## 4. Components

All in `Code/Match/`.

### `MatchState.cs`

```csharp
public enum MatchState { Lobby, Round, RoundEnd }
public enum MatchOutcome { None, Crew, Saboteur }
```

### `Match.cs`

The orchestrator. One per scene.

```csharp
public sealed class Match : Component
{
    [Property] public PlayerSpawner PlayerSpawner { get; set; }

    [Property] public float LobbyAutoStartSeconds { get; set; } = 30f;
    [Property] public float RoundDuration { get; set; } = 480f;     // 8 min
    [Property] public float RoundEndDuration { get; set; } = 10f;
    [Property] public int MinPlayersToStart { get; set; } = 3;
    [Property] public int SaboteurCountOverride { get; set; } = 0;  // 0 = auto-scale

    [Sync(SyncFlags.FromHost)] public MatchState State { get; private set; } = MatchState.Lobby;
    [Sync(SyncFlags.FromHost)] public float StateEnteredAt { get; private set; }
    [Sync(SyncFlags.FromHost)] public MatchOutcome LastOutcome { get; private set; }
    [Sync(SyncFlags.FromHost)] public string LastOutcomeReason { get; private set; }

    public static event Action<Match, bool /* localIsSaboteur */> RoundStarted;
    public static event Action<Match, MatchOutcome, string> RoundEnded;

    [Rpc.Host]
    public void StartRound();   // skips Lobby countdown, transitions Lobby → Round

    [Rpc.Host]
    public void EndRound( MatchOutcome winner, string reason );

    public float SecondsLeftInState();   // for HUD countdowns

    // Static convenience for UI components: returns the active scene's Match, or null.
    public static Match Current => Game.ActiveScene?.GetAllComponents<Match>().FirstOrDefault();
}
```

Internal:
- Host-side `OnUpdate` runs the state machine and the four win-condition checks per frame.
- `EnterRound()` does full world reset, assigns roles, broadcasts `OnRoundStarted` with saboteur connection IDs.
- `EndRound()` body sets state and outcome, broadcasts `OnRoundEnded`, clears nothing yet (RoundEndOverlay needs `IsSaboteur` to display roles).
- `EnterLobby()` clears `IsSaboteur` on all players, respawns alive, resets sections, clears corpses, sets `PlayerSpawner.RoundInProgress = false`.

### `RoleAssigner.cs`

```csharp
public static class RoleAssigner
{
    public static int ResolveSaboteurCount(int playerCount, int @override);
    public static Guid[] Assign(IEnumerable<Player> alivePlayers, int saboteurCount);  // returns assigned saboteur connection IDs
    public static void ClearAll(IEnumerable<Player> allPlayers);
}
```

Auto-scale: 1 saboteur for 3-7 players, 2 for 8+. Override is honored when positive (clamped to player count if too high).

### Section.cs addition (Group A modification)

```csharp
[Rpc.Host]
public void Reset()
{
    if ( !Networking.IsHost ) return;
    State = VentingState.Idle;
    StateEnteredAt = Time.Now;
    occupants.Clear();
}
```

### UI components (Razor panels)

Each is a `Component` derived from `PanelComponent`:

- **`RoleRevealOverlay.razor`** — listens for `Match.RoundStarted` event. On fire, shows a 3s full-screen flash with cause-specific text ("YOU ARE THE SABOTEUR" red / "YOU ARE A CREWMATE" blue). Reads `localIsSaboteur` from event payload (race-free).
- **`RoleHud.razor`** — persistent corner badge. Reads local `Player.IsSaboteur` + `Match.State`. Hidden in Lobby; shows role badge during Round/RoundEnd.
- **`RoundTimerHud.razor`** — top-center countdown. Visible during Round only. Reads `Match.SecondsLeftInState()`.
- **`LobbyCountdownHud.razor`** — top-center "Round starting in 30…" during Lobby. If player count < `MinPlayersToStart`, shows "Waiting for players (3 minimum, 4 recommended)" instead.
- **`RoundEndOverlay.razor`** — full-screen results banner during RoundEnd. Shows winner banner ("SABOTEUR WINS" / "CREW WINS"), the reason text, and reveals all saboteur identities (reads `Player.IsSaboteur` for every Player and lists those who were saboteurs).
- **`SaboteurNametagOverlay.razor`** — local-saboteur-only. During Round, iterates other living players; if their `IsSaboteur == true`, draws a small red marker over their head in 3D space.

### Debug commands (in `Code/Debug/DebugCommands.cs`)

- `decompv2_start_round` — host-only. Calls `Match.Current?.StartRound()` to skip the lobby countdown.
- `decompv2_end_round <Crew|Saboteur>` — host-only. Force-ends the current round.
- `decompv2_match_state` — any client. Logs `Match.State`, time-in-state, last outcome.

### Scene wiring

`minimal.scene` extension:
- One new GameObject `Match` at scene root with the `Match` component.
- Drag the existing `PlayerSpawner` GameObject into the Match's `PlayerSpawner` field.
- Player prefab: add the six new HUD Razor components as children of the existing `Hud` GameObject (which already has a `ScreenPanel`).

---

## 5. Lifecycle and timing

### Phase transitions, in order

```
[Lobby state]
    ├─ host OnUpdate every frame:
    │   countdown_remaining = LobbyAutoStartSeconds - (Time.Now - StateEnteredAt)
    │   if PlayerCount >= MinPlayersToStart AND countdown_remaining <= 0:
    │       EnterRound()
    │   else if PlayerCount < MinPlayersToStart:
    │       reset countdown each frame (StateEnteredAt = Time.Now)
    │
    └─ HUD: LobbyCountdownHud
        if PlayerCount < 3: "Waiting for players (3 minimum, 4 recommended)"
        else: "Round starting in N seconds"
```

```
[Round state — entry sequence on host (EnterRound)]
    1. CorpseCleanupSignal.RaiseGenericCleanup()             // generic corpses
    2. for each Corpse with Cause == Decompression: Cleanup() // vacuum corpses
    3. for each Section in scene: Section.Reset()
    4. for each Player: SetSaboteur(false), IsAlive = true
    5. for each Player: respawn at random SpawnPoint
    6. PlayerSpawner.RoundInProgress = true
    7. saboteurIds = RoleAssigner.Assign(alive players, ResolveSaboteurCount(...))
    8. State = MatchState.Round, StateEnteredAt = Time.Now
    9. OnRoundStarted(saboteurIds)  // [Rpc.Broadcast]
        each client computes localIsSaboteur and invokes Match.RoundStarted

[Round state — host OnUpdate, win condition checks]
    elapsed = Time.Now - StateEnteredAt
    if elapsed >= RoundDuration:
        EndRound(Saboteur, "time expired")
    else:
        crewAlive = Players.Count(p => p.IsAlive && !p.IsSaboteur)
        sabsAlive = Players.Count(p => p.IsAlive && p.IsSaboteur)

        if crewAlive == 0:
            EndRound(Saboteur, "all crew dead")
        else if sabsAlive == 0:
            EndRound(Crew, "all saboteurs eliminated")
        else if crewAlive == 1 && sabsAlive >= 1:
            EndRound(Saboteur, "1v1 endgame — saboteur wins by default")

    // C2 (when shipped) calls EndRound(Crew, "all tasks complete")
    // C3 (when shipped) calls EndRound(Crew, "all saboteurs voted out")
```

```
[Round → RoundEnd transition (EndRound body)]
    1. Set LastOutcome = winner, LastOutcomeReason = reason
    2. State = MatchState.RoundEnd, StateEnteredAt = Time.Now
    3. (Roles NOT cleared — RoundEndOverlay needs IsSaboteur to display them)
    4. OnRoundEnded(winner, reason)  // [Rpc.Broadcast]
       UI overlays react and reveal roles

[RoundEnd state — host OnUpdate]
    elapsed = Time.Now - StateEnteredAt
    if elapsed >= RoundEndDuration:
        EnterLobby()

[RoundEnd → Lobby transition (EnterLobby)]
    1. RoleAssigner.ClearAll(all players)                    // wipe IsSaboteur
    2. for each Player: IsAlive = true, respawn at SpawnPoint
    3. for each Section: Reset()
    4. clear corpses (same as round-start cleanup)
    5. PlayerSpawner.RoundInProgress = false
    6. State = MatchState.Lobby, StateEnteredAt = Time.Now
```

### Round timer formula

```csharp
public float SecondsLeftInState()
{
    var elapsed = Time.Now - StateEnteredAt;
    return State switch
    {
        MatchState.Lobby => Math.Max(0f, LobbyAutoStartSeconds - elapsed),
        MatchState.Round => Math.Max(0f, RoundDuration - elapsed),
        MatchState.RoundEnd => Math.Max(0f, RoundEndDuration - elapsed),
        _ => 0f,
    };
}
```

This uses each peer's local `Time.Now`, which can drift slightly from the host's. For the round timer that's acceptable (visible drift would be sub-second, invisible to humans). If C3's voting timer needs tighter sync we'll switch to a synced absolute timestamp later.

### Late-joiner behavior

| Phase joiner arrives | What happens |
|---|---|
| Lobby | Spawned alive at a SpawnPoint. Counts toward `MinPlayersToStart`. |
| Round | `RoundInProgress = true`, spawned as spectator (Group B). No role-reveal flash (already fired). |
| RoundEnd | Same — spawn as spectator, see the synced results overlay. |
| Lobby (after round end) | `RoundInProgress = false`, spawned alive again like normal Lobby joiners. |

### Player leaves

When a Player GameObject is destroyed on disconnect, it's no longer in `GetAllComponents<Player>()`. Win-condition counts naturally adjust. If the disconnected player was the last saboteur, `sabsAlive == 0` next frame → crew wins. No special disconnect handling needed.

---

## 6. Networking and data flow

### Sync classification

| Data | Mechanism | Reason |
|---|---|---|
| `Match.State` | `[Sync(SyncFlags.FromHost)]` | Drives every UI component on every client. |
| `Match.StateEnteredAt` | `[Sync(SyncFlags.FromHost)]` | Lets clients compute time-in-state for HUD countdowns. |
| `Match.LastOutcome` | `[Sync(SyncFlags.FromHost)]` | RoundEndOverlay reads this. |
| `Match.LastOutcomeReason` | `[Sync(SyncFlags.FromHost)]` | RoundEndOverlay reads this. |
| `Player.IsSaboteur` | already `[Sync(SyncFlags.FromHost)]` (Group A) | Read by RoleHud, Panel, RoundEndOverlay. |
| `Player.IsAlive` | already `[Sync(SyncFlags.FromHost)]` (Group B) | Used in win-check counts (host) and HUD (clients). |
| `RoundStarted` event | fired on every client via `[Rpc.Broadcast] OnRoundStarted(Guid[])` | Cross-system notification. Includes saboteur connection IDs (race-free role reveal). |
| `RoundEnded` event | fired on every client via `[Rpc.Broadcast] OnRoundEnded(MatchOutcome, string)` | Cross-system notification. |
| Player counts for win checks | host-only, derived from `GetAllComponents<Player>()` | Clients don't need them. |
| Saboteur count for current round | not synced — derived per-frame from `IsSaboteur` flags | No separate state needed. |

### Round-start flow on the wire

```
[any client]        decompv2_start_round (or auto-start countdown hits 0 on host)
                                  │
                                  ▼  [Rpc.Host]
[host]              EnterRound():
                       cleanup corpses, reset sections, respawn players
                       RoleAssigner.Assign(...) → SetSaboteur(true) on N players
                       State = Round, StateEnteredAt = Time.Now
                       PlayerSpawner.RoundInProgress = true
                                  │
                                  ▼  [Rpc.Broadcast] OnRoundStarted(saboteurIds)
                    Each client:
                       localIsSaboteur = saboteurIds.Contains(Connection.Local.Id)
                       Match.RoundStarted?.Invoke(this, localIsSaboteur)
                                  │
                                  ▼
[every client]      RoleRevealOverlay shows 3s flash (uses event payload)
                    LobbyCountdownHud hides
                    RoundTimerHud appears
                    RoleHud snaps to "saboteur" or "crewmate" (reads IsSaboteur, settled by now)
                    SaboteurNametagOverlay starts drawing markers
```

### Round-end flow

```
[host OnUpdate]     win condition fires (e.g., crewAlive == 0)
                       EndRound(Saboteur, "all crew dead")
                                  │
                                  ▼  on host
[host]              State = RoundEnd, StateEnteredAt = Time.Now
                    LastOutcome = Saboteur, LastOutcomeReason = "all crew dead"
                                  │
                                  ▼  [Rpc.Broadcast] OnRoundEnded(winner, reason)
[every client]      Match.RoundEnded?.Invoke(this, winner, reason)
                    RoundEndOverlay appears with winner + reason + role reveal
                    RoundTimerHud hides
                    SaboteurNametagOverlay hides
```

### `Match.EndRound` is callable from anywhere

Because it's `[Rpc.Host]`, any client can call it — the call routes to the host. C2 will call from a non-host context (a crew member completes the last task on their own client), the host runs the body. Same for C3.

```csharp
// C2 example:
if ( allTasksComplete )
{
    Match.Current?.EndRound( MatchOutcome.Crew, "all tasks complete" );
}
```

### Late-joiner sync

A connection joining mid-Round receives the synced `Match.State`, `StateEnteredAt`, `LastOutcome`, `LastOutcomeReason`. UI components react reactively. They never see the role-reveal flash (already-fired RPC), correctly. If they joined during RoundEnd, `RoundEndOverlay` reads the synced outcome fields and displays the results.

---

## 7. Failure modes

| Failure | Mitigation |
|---|---|
| `StartRound` while not in Lobby | `EnterRound` precondition check; logs warning. |
| `EndRound` while not in Round | Body early-out at `[Rpc.Host]` validation; idempotent. |
| Auto-start fires below `MinPlayersToStart` | Lobby tick resets countdown each frame the threshold isn't met. |
| Two clients race to call `EndRound` | First-wins via `State != Round` check on second call. |
| Host disconnects mid-round | s&box host migration preserves `[Sync]` state. New host computes `Time.Now - StateEnteredAt` to pick up where the old left off. |
| All saboteurs disconnect | `sabsAlive == 0` next frame → crew wins via "all saboteurs eliminated". |
| All crew disconnect | `crewAlive == 0` → saboteur wins. |
| Below-floor player count after round-start | `EnterRound` re-checks `PlayerCount >= MinPlayersToStart`; aborts transition if too few. |
| `RoleAssigner.Assign` called with empty list | No-op + warning. Round transitions anyway and immediately ends via `crewAlive == 0`. |
| `EnterRound` runs while sections still vented | `Section.Reset()` forces all sections back to Idle. Vacuum corpses cleared via cleanup signal. |
| Player still spectating from prior round when new round starts | Round-start respawn loop iterates ALL Players and resets `IsAlive = true` + respawns. Spectator state machine sees alive=true and stops following. |
| `Match.RoundDuration` set absurdly low | Round transitions to RoundEnd almost immediately. Annoying but not broken. |
| `SaboteurCountOverride` larger than player count | `RoleAssigner.Assign` clamps to `min(override, playerCount)` + warning. |
| Player joins during RoundEnd | `PlayerSpawner.RoundInProgress` stays `true` through RoundEnd; new joiners spawn as spectators and see the synced results overlay. |
| `Match.RoundEnded` event has zero subscribers | Null-conditional invoke. |
| Match component missing from scene | `Match.Current` returns null; UI components null-check and gracefully no-op. |
| Player.SetSaboteur sync lag for role-reveal flash | Eliminated. RoleRevealOverlay reads role from broadcast event payload directly — no race. RoleHud / Panel use the synced flag and tolerate a one-frame settle. |

---

## 8. Testing

### Debug commands

- `decompv2_start_round` — host-only. Skips the Lobby countdown.
- `decompv2_end_round <Crew|Saboteur>` — host-only. Force-ends the current round.
- `decompv2_match_state` — any client. Logs current Match state, time, outcome.

### Required test scaffold

`minimal.scene` extension:
- One new `Match` GameObject at scene root with `Match` component, `PlayerSpawner` field wired.
- Tunables can be lowered for testing (e.g., `LobbyAutoStartSeconds = 5`, `RoundDuration = 30` for fast iteration).

Player prefab extension:
- Six new Razor components on the existing `Hud` child: RoleRevealOverlay, RoleHud, RoundTimerHud, LobbyCountdownHud, RoundEndOverlay, SaboteurNametagOverlay.

### Manual test matrix

1. **Lobby waiting (under-threshold)** — Host alone. LobbyCountdownHud shows "Waiting for players". Countdown does not advance.
2. **Lobby auto-start (threshold met)** — Add clients to reach 3. Countdown ticks down and fires the round at 0.
3. **Manual start** — Host runs `decompv2_start_round` with ≥3 players. Skips countdown.
4. **Role reveal** — Round entry: every client sees a 3s flash with their role.
5. **Persistent role badge** — During Round, every client has a corner badge. Local-only — can't see another's.
6. **Saboteur name tag** — Two saboteurs see each other's red marker. Crew clients do not see it.
7. **Round timer** — RoundTimerHud counts down each second.
8. **Win: kill all crew** — Saboteur kills all crew. Round transitions to RoundEnd within ~1 frame; "SABOTEUR WINS — all crew dead".
9. **Win: 1v1** — 4 players (3 crew + 1 saboteur), saboteur kills 2 crew, 1v1 fires. "SABOTEUR WINS — 1v1 endgame".
10. **Win: timeout** — Set `RoundDuration = 30`. Wait 30s. "SABOTEUR WINS — time expired".
11. **Win: 0 saboteurs alive** — Host kills the saboteur (via `decompv2_kill`). "CREW WINS — all saboteurs eliminated".
12. **Manual round end** — `decompv2_end_round Crew` while in Round. Round transitions; reason = "debug command".
13. **RoundEnd → Lobby auto-transition** — Wait 10s after round end. Match transitions to Lobby; players respawn alive.
14. **Late joiner during Round** — 4th client joins mid-round. Spawned as spectator. Cycles through living players.
15. **Late joiner during RoundEnd** — Joins during 10s results screen. Spawned as spectator, sees the synced results overlay.
16. **Late joiner during Lobby (after round end)** — Stays connected across the transition. Respawned alive on Lobby entry.
17. **Disconnect during Round** — Crew player disconnects. Win-check counts naturally adjust.
18. **Multiple rounds back-to-back** — Full Lobby → Round → RoundEnd → Lobby → Round cycle. World fully reset each round-start.

---

## 9. Public surface other groups depend on

```csharp
// Group A modification
section.Reset();   // [Rpc.Host], wipes section back to Idle

// C1 fires events on every client:
Match.RoundStarted += (match, localIsSaboteur) => { /* C2: reset task progress; C3: reset meeting cooldowns */ };
Match.RoundEnded += (match, outcome, reason) => { /* C2/C3 cleanup */ };

// C2 calls when all tasks done (from any client; routes to host):
Match.Current?.EndRound( MatchOutcome.Crew, "all tasks complete" );

// C3 calls when voting concludes:
Match.Current?.EndRound( MatchOutcome.Crew, "all saboteurs voted out" );
// or
Match.Current?.EndRound( MatchOutcome.Saboteur, "wrong player ejected" );  // depends on C3 design

// Convenience accessor for any subsystem:
var match = Match.Current;
if ( match is not null && match.State == MatchState.Round ) { /* gameplay only during round */ }
```

That is the entire surface. C2 and C3 should not reach into `Match` internals beyond this.
