# Tasks — Design (C2)

**Project:** Decompression v2 (s&box multiplayer horror game)
**Group:** C2 — Task system + crew win condition (second sub-project of Group C)
**Date:** 2026-05-01
**Status:** Approved, ready for implementation plan
**Builds on:**
- Group B — Player & Death systems
- Group A — Decompression mechanic
- Group C1 — Round flow & roles (`docs/superpowers/specs/2026-04-30-round-flow-and-roles-design.md`)

---

## 1. Goals and scope

Build the task system that gives crew their reason for moving around the map and a path to victory: complete personal task lists before the saboteur eliminates the crew or the timer expires. Saboteurs can fake task completions visually (core social-deduction mechanic). The framework is extensible — adding new task types is a small subclass + Razor panel, no Match-side changes.

**In scope**
- Abstract `TaskObject` base class establishing the contract Match cares about (assignment, completion, location)
- One concrete task type: `HoldUseTaskObject` (hold E for N seconds at a terminal)
- `TaskAssigner` static helper for round-start assignment
- `TaskCoordinator` Match-side glue (subscribes to RoundStarted/RoundEnded, owns the win check)
- Per-crew personal task lists (saboteurs get no tasks)
- Dead-crew tasks are skipped from the win check
- Saboteur fake-completion enforced server-side (visual mimics real completion)
- Authored task placement (level designer drops `TaskObject` GameObjects in scene)
- Configurable tasks-per-crew tuning (default 5)
- HUD: list of own tasks + hover-glow on own assigned tasks (no glow when crew aims at a task that isn't theirs)
- Test scaffold: `TaskCoordinator` + ~10 `HoldUseTaskObject` instances in `minimal.scene`
- Debug ConCmds: `decompv2_complete_my_tasks`, `decompv2_list_tasks`, `decompv2_complete_task`

**Out of scope (future C-group work or polish)**
- More task types (multi-step path, sequence press, calibration dial, mini-games) — each is its own small follow-up feature once the framework ships
- Saboteur sabotage abilities targeting tasks (e.g., "shut off lights for 30s" interrupts task completion)
- Task-completion sound design — placeholder/silent for now
- Visual polish on the hold-glow / progress (acceptable to use the same lerp pattern as Panel)
- Persistent task progress across rounds (each round is a clean slate)
- Per-task progress beyond binary done/not-done (the list shows ✔ or empty; partial progress isn't displayed numerically)

---

## 2. Key decisions and rationale

| Decision | Choice | Rationale |
|---|---|---|
| Task variety | Single uniform mechanical type (`HoldUseTaskObject`) shipped in C2; framework supports arbitrary types via subclassing | Genre needs variety long-term, but C2 ships the framework + 1 type to validate the architecture. New types ship as cheap follow-ups. |
| Task assignment model | Per-crew personal lists | Among Us standard. Adds tactical weight (saboteurs can clue in to player location → likely tasks there). Each crew has identity, not a swarm. |
| Dead crew's tasks | Skipped from win check (Q3-A) | Cleanest implementation; saboteur kills are valuable for other reasons (kill counter, vote pressure) without auto-blocking crew. Crew don't get punished into impossibility for losing teammates. |
| Task placement | Authored placement (`TaskObject` GameObjects in scene) | Crew can learn map (engineering scan is on the back wall), saboteur can plan around known task spots, simplest to implement. Decouples task type creation from level design. |
| Saboteur task interaction | Full fake-completion (visual identical to real, no progress recorded) — server enforced | Core social-deduction mechanic of the genre. Without it, "they didn't even try" tells would let crew ID saboteurs trivially. Server enforcement means cheating clients can't fake their progress. |
| Crew on unassigned task | Same fake-completion as saboteur | Cosmetic only. Visual ramp + snap-back. Avoids "I tried to do that task but it ignored me" confusion. |
| Number of tasks per crew | Configurable, default 5 | Matches Among Us baseline. Tunable for playtest pacing without code changes. |
| Task type rollout | Framework + Hold-Use only in C2; more types as separate follow-up specs | Each new type is a small spec/plan/implementation cycle. C2 doesn't get scope-creep into 4 task types. |
| Round-end task reset | Full reset on `Match.RoundEnded` (clear assignments + IsCompleted + per-type internal state via `OnReset` hook) | Each round is a clean slate. No carryover between rounds. |
| Late-joiner tasks | None (they're spectators per Group B/C1) | Falls out of existing infrastructure. Round-end then round-start gives them a fresh assignment. |
| Hold progress on partial release | Reset to 0% (no resume) | Same as Panel hack — makes interruption meaningful, simpler than persistence. Multi-step tasks (future) can choose differently. |
| Architecture | Abstract `TaskObject` base + concrete subclasses | Open-closed: new task types are isolated, existing code never touches them. Mirrors Group B/A patterns. |
| Authority model | Server-authoritative throughout (`[Sync(SyncFlags.FromHost)]` for state, `[Rpc.Host]` for triggers, `[Rpc.Broadcast]` for cross-client visual events with local-clock timestamps) | Same as A/B/C1. Saboteur fake-completion enforcement requires host-side validation. |
| TaskListHud visibility | Local player only, hidden in Lobby | Standard HUD scope. List shows what's left to do. |
| Hover glow visibility | Only when local player aims at one of their own assigned, incomplete tasks | Saboteurs see no glow. Crew see glow only on theirs. Adds tactical value (saboteurs can't ID which task is whose by watching glows). |
| Win-check edge case (zero tasks) | Guard: `totalAssignedCount > 0` before triggering crew win | Prevents an empty test scene from instantly handing the round to crew. |

---

## 3. Architecture overview

C2 lives in `Code/Tasks/`. Six new files plus a small Match modification.

```
                      ┌────────────────────────────────────┐
                      │        Match (existing C1)         │
                      │  RoundStarted ───┐                 │
                      │  RoundEnded   ───┤ events          │
                      │  EndRound(...)   │                 │
                      └────────┬─────────┘                 │
                               │ subscribes                │
                               ▼                           │
                      ┌────────────────────┐               │
                      │   TaskCoordinator  │               │
                      │ (host-side glue)   │               │
                      └────────┬───────────┘               │
                               │ orchestrates              │
                               ▼                           │
                      ┌────────────────────┐               │
                      │   TaskAssigner     │ static helper │
                      │ (assigns N per     │               │
                      │  alive crew)       │               │
                      └────────┬───────────┘               │
                               │ writes AssignedConnectionId
                               ▼                           │
        ┌──────────────────────────────────────────────┐  │
        │           TaskObject (abstract)              │  │
        │  ┌────────────────────────────────────────┐  │  │
        │  │  HoldUseTaskObject (concrete, only one │  │  │
        │  │                     type C2 ships)     │  │  │
        │  │  ...future types                       │  │  │
        │  └────────────────────────────────────────┘  │  │
        └──────────────────────┬───────────────────────┘  │
                               │ MarkComplete() (host)    │
                               ▼                           │
                      ┌────────────────────┐               │
                      │ TaskCoordinator    │               │
                      │ win check (every   │               │
                      │  frame in Round)   │ if all done → │
                      └────────────────────┘ Match.EndRound│
                                             (Crew, ...)  ─┘

        UI components (every client):
        ┌─────────────────────────────────────┐
        │  TaskListHud (per-player list)      │
        │  TaskHoverOutline (glow on hovered  │
        │                    own task)        │
        └─────────────────────────────────────┘
```

**Cross-system integration:**

- C2 subscribes to `Match.RoundStarted` to call `TaskAssigner.Assign(...)`
- C2 subscribes to `Match.RoundEnded` to clear all task state
- C2 calls `Match.EndRound(MatchOutcome.Crew, "all tasks complete")` when win condition fires
- C2 reads `Player.IsAlive`, `Player.IsSaboteur`, `Player.OwnerConnectionId` (existing synced state)

**Three load-bearing decisions:**

1. **Abstract `TaskObject` base** — future task types subclass this; no Match-side or framework changes per new type.
2. **`AssignedConnectionId` is the assignment marker** — synced from host, drives both UI and gameplay gating. Saboteurs and unassigned crew see Guid.Empty here.
3. **Server enforces saboteur fake-completion** — a saboteur's hold can complete visually but `MarkComplete` only fires when the host validates the holder is an assigned alive crew.

---

## 4. Components

All in `Code/Tasks/`.

### `TaskObject.cs` — abstract base

```csharp
public abstract class TaskObject : Component
{
    [Property] public string DisplayName { get; set; } = "";
    [Property] public string LocationLabel { get; set; } = "";  // "Engineering"

    [Sync(SyncFlags.FromHost)] public Guid AssignedConnectionId { get; set; }
    [Sync(SyncFlags.FromHost)] public bool IsCompleted { get; set; }

    public bool IsAssignedToLocal =>
        AssignedConnectionId != Guid.Empty
        && AssignedConnectionId == Connection.Local?.Id;

    public bool IsAssignedAndAlive =>
        AssignedConnectionId != Guid.Empty
        && Game.ActiveScene?.GetAllComponents<Player>()
            .FirstOrDefault(p => p.OwnerConnectionId == AssignedConnectionId)
            ?.IsAlive == true;

    [Rpc.Host]
    protected void MarkComplete()
    {
        if (!Networking.IsHost) return;
        if (IsCompleted) return;
        IsCompleted = true;
    }

    [Rpc.Host]
    public void Reset()
    {
        if (!Networking.IsHost) return;
        AssignedConnectionId = Guid.Empty;
        IsCompleted = false;
        OnReset();
    }

    protected virtual void OnReset() { }
}
```

### `HoldUseTaskObject.cs` — concrete type

Implements `Component.IPressable`. Hold-Use timer with broadcast-driven glow (mirrors Panel pattern). Host validates completion against `IsSaboteur` + `IsAssignedAndAlive` semantics.

```csharp
public sealed class HoldUseTaskObject : TaskObject, Component.IPressable
{
    [Property] public float HoldDuration { get; set; } = 3f;
    [Property] public ModelRenderer GlowRenderer { get; set; }

    public Guid HoldingConnectionId { get; private set; }
    public float HoldStartTime { get; private set; }   // local clock per client

    bool Component.IPressable.Press(Component.IPressable.Event e);
    void Component.IPressable.Release(Component.IPressable.Event e);

    [Rpc.Host] public void BeginHold();
    [Rpc.Host] public void EndHold();

    [Rpc.Broadcast] private void BroadcastHoldStart(Guid connectionId);
    [Rpc.Broadcast] private void BroadcastHoldEnd();

    protected override void OnUpdate()
    {
        UpdateGlow();
        if (!Networking.IsHost) return;
        if (HoldingConnectionId == Guid.Empty) return;

        // Host validity checks (holder still connected + alive):
        //   - if not, BroadcastHoldEnd() and return
        // Hold complete check:
        //   var elapsed = Time.Now - HoldStartTime;
        //   if (elapsed >= HoldDuration) {
        //     var holder = ResolvePlayer(HoldingConnectionId);
        //     bool shouldComplete = holder is not null
        //         && holder.IsAlive
        //         && !holder.IsSaboteur
        //         && AssignedConnectionId == HoldingConnectionId;
        //     if (shouldComplete) MarkComplete();
        //     BroadcastHoldEnd();
        //   }
    }

    protected override void OnReset()
    {
        if (Networking.IsHost && HoldingConnectionId != Guid.Empty)
            BroadcastHoldEnd();
    }

    // Glow renderer Tint lerp from initial→Color.Red, scaled by progress.
    // Same pattern as Panel.UpdateGlow.
}
```

### `TaskAssigner.cs` — static helper

```csharp
public static class TaskAssigner
{
    public static void Assign(
        IEnumerable<Player> alivePlayers,
        IEnumerable<TaskObject> availableTasks,
        int tasksPerCrew );

    public static void ClearAll(IEnumerable<TaskObject> allTasks);
}
```

Internal: filters out saboteurs, shuffles task pool with Fisher-Yates, gives each crew the first N tasks. If too few tasks for everyone, logs warning and gives each crew as many as possible.

### `TaskCoordinator.cs` — host-side glue

```csharp
public sealed class TaskCoordinator : Component
{
    [Property] public int TasksPerCrew { get; set; } = 5;

    protected override void OnAwake();
    protected override void OnDestroy();

    private void OnRoundStarted(Match match, bool localIsSaboteur);
    private void OnRoundEnded(Match match, MatchOutcome outcome, string reason);

    protected override void OnUpdate();   // host-side win check
}
```

`OnAwake` subscribes to `Match.RoundStarted` and `Match.RoundEnded`. `OnUpdate` (host only, when `Match.State == Round`) runs the win check.

### `TaskListHud.razor` + `.scss`

Corner-of-screen list. Shows `LocationLabel: DisplayName` for each of the local player's assigned tasks. Completed ones strike through or check off. Hidden during Lobby and RoundEnd.

### `TaskHoverOutline.razor` + `.scss`

Every frame on every client: trace from the local camera, find the hit GameObject, look up `TaskObject`. If `task.IsAssignedToLocal && !task.IsCompleted`, render a yellow outline overlay. Single-pixel screen-space glow effect via the same Tint mechanism we use elsewhere.

### Debug commands (in `Code/Debug/DebugCommands.cs`)

- `decompv2_complete_my_tasks` — local-player. Routes through host-side helper that calls `MarkComplete()` on every TaskObject where `AssignedConnectionId == this connection`.
- `decompv2_list_tasks` — any client. Logs all TaskObjects in scene with name, location, assigned (resolved to display name), completed.
- `decompv2_complete_task <displayName>` — host-only. Force-completes a specific task by `DisplayName`.

### Scene wiring

`minimal.scene` extension:
- One `TaskCoordinator` GameObject at scene root, `TasksPerCrew = 3` for fast iteration testing (default 5 in production).
- ~10 `HoldUseTaskObject` GameObjects placed around the map, each with:
  - A small mesh (placeholder cube/cylinder) representing the console
  - A box collider so IPressable trace hits
  - `DisplayName` set ("Calibrate Dial", "Submit Sample", etc.)
  - `LocationLabel` set ("Engineering", "Medbay")
  - `GlowRenderer` reference wired to the mesh's ModelRenderer

Player prefab extension:
- Add `Decompression.TaskListHud` and `Decompression.TaskHoverOutline` Razor components to the existing `Hud` child.

---

## 5. Lifecycle and timing

### Round-start sequence

```
Match.EnterRound finishes (existing C1 reset + role assignment)
    Match fires OnRoundStarted broadcast
        ↓
Every client receives broadcast
    Host-only: TaskCoordinator.OnRoundStarted
        ↓
    Find all TaskObjects in scene
    Find all alive crew (alive AND not saboteur)
    TaskAssigner.Assign(crew, taskPool, TasksPerCrew):
      - Shuffle pool (Fisher-Yates)
      - For each crew: take next N tasks, set AssignedConnectionId = crew.OwnerConnectionId
      - Skip saboteurs (no tasks for them)
        ↓ via [Sync(SyncFlags.FromHost)]
    Each TaskObject's AssignedConnectionId arrives on every client
    UI updates reactively:
      - TaskListHud rebuilds for local player
      - TaskHoverOutline starts glow-on-hover for own tasks
```

### Hold flow

```
Crew aims at TaskObject, presses E
    IPressable.Press → BeginHold() [Rpc.Host]
        ↓ on host
    if HoldingConnectionId already set: ignore
    BroadcastHoldStart(caller.Id) [Rpc.Broadcast]
        ↓ every client
    HoldingConnectionId = connectionId
    HoldStartTime = Time.Now (LOCAL clock — no host/client clock skew)
    Glow ramps locally as (Time.Now - HoldStartTime) / HoldDuration

Host OnUpdate, every frame, when HoldingConnectionId != Empty:
    Validity checks (defense in depth):
      - holder still in Connection.All → if not, BroadcastHoldEnd, return
      - holder.IsAlive → if not, BroadcastHoldEnd, return
    Completion check:
      var elapsed = Time.Now - HoldStartTime
      if (elapsed >= HoldDuration):
        var holder = ResolvePlayer(HoldingConnectionId)
        bool shouldComplete =
          holder.IsAlive
          && !holder.IsSaboteur
          && AssignedConnectionId == HoldingConnectionId
        if (shouldComplete): MarkComplete()
        BroadcastHoldEnd() (always, regardless of shouldComplete)

Release before completion:
    Player releases E → EndHold() [Rpc.Host]
    BroadcastHoldEnd() — glow snaps back, progress reset to 0
```

### Win-check loop (host, every frame in Round)

```
var allTasks = scene.GetAllComponents<TaskObject>().ToList();
var alivePlayers = scene.GetAllComponents<Player>().Where(p => p.IsAlive).ToList();
var aliveConnectionIds = alivePlayers.Select(p => p.OwnerConnectionId).ToHashSet();

int totalAssigned = 0;
int pendingCount = 0;
foreach (var task in allTasks)
{
    if (task.AssignedConnectionId == Guid.Empty) continue;        // unassigned (extras)
    if (!aliveConnectionIds.Contains(task.AssignedConnectionId))  // assignee dead
        continue;                                                  // skip per Q3-A
    totalAssigned++;
    if (!task.IsCompleted) pendingCount++;
}

if (totalAssigned > 0 && pendingCount == 0)
{
    Match.Current?.EndRound(MatchOutcome.Crew, "all tasks complete");
}
```

The `totalAssigned > 0` guard prevents an empty test scene from instantly handing crew the win on round start.

### Round-end sequence

```
Match.EnterRoundEnd fires → Match fires OnRoundEnded
    ↓ every client
Host-only: TaskCoordinator.OnRoundEnded
    TaskAssigner.ClearAll(allTasks):
      For each task: task.Reset()
        - AssignedConnectionId = Guid.Empty
        - IsCompleted = false
        - OnReset() hook → HoldUseTaskObject broadcasts HoldEnd if any in flight
    ↓ via [Sync] / [Rpc.Broadcast]
All clients clear UI:
    TaskListHud sees no assigned tasks for local → hides
    TaskHoverOutline sees no IsAssignedToLocal hits → no outlines
```

When `Match.EnterLobby` fires, `OnRoundEnded` already fired (at the previous Round → RoundEnd transition). Tasks are already clear.

---

## 6. Networking and data flow

### Sync classification

| Data | Mechanism | Reason |
|---|---|---|
| `TaskObject.AssignedConnectionId` | `[Sync(SyncFlags.FromHost)]` | Persistent per-round assignment. UI reads it. |
| `TaskObject.IsCompleted` | `[Sync(SyncFlags.FromHost)]` | Persistent completion state. UI ticks off. |
| `HoldUseTaskObject.HoldingConnectionId` | `[Rpc.Broadcast]` BroadcastHoldStart / BroadcastHoldEnd | One-shot event per hold start/end. Same pattern as Panel. |
| `HoldUseTaskObject.HoldStartTime` | set inside the broadcast handler to local `Time.Now` | Each client's local clock; (Time.Now - HoldStartTime) is locally correct. No host/client clock skew. |
| `Player.IsAlive`, `Player.IsSaboteur`, `Player.OwnerConnectionId` | already `[Sync(SyncFlags.FromHost)]` (B + C1) | Read by win check + UI. |
| `Match.RoundStarted`, `Match.RoundEnded` events | static C# events fired via Match's existing broadcast | C2 subscribes. |

### Late-join behavior

A connection joining mid-Round:
- Spawned as spectator (Group B Task 18 + C1).
- All `TaskObject.AssignedConnectionId` and `IsCompleted` synced via `[Sync]` — they see the world state correctly.
- They have no `Connection.Local.Id` matching any AssignedConnectionId, so TaskListHud is empty (or hidden).
- They miss any in-flight `BroadcastHoldStart` (one-shot) — if a crew is mid-hold when they join, they don't see the glow until the next press. Acceptable.

### Saboteur fake-completion enforcement

Server-side. A saboteur (or unassigned crew) can hold to 100% on their client; the visual is identical to a real completion. Host's `OnUpdate` validates the holder against `AssignedConnectionId == HoldingConnectionId && !IsSaboteur && IsAlive` — only if all three pass does `MarkComplete` fire. `BroadcastHoldEnd` always fires to clear the glow. The deception is that crew watching from across the room can't tell from the visual whether the holder genuinely got progress or didn't.

### Writes that come from non-host clients

By design, **no writes to `TaskObject` state happen from non-host clients**. All state mutations are host-side `[Rpc.Host]` methods. The only client-side writes are to `HoldingConnectionId` / `HoldStartTime` inside `[Rpc.Broadcast]` handlers, which are uniform across all clients (the broadcast contains the value).

---

## 7. Failure modes

| Failure | Mitigation |
|---|---|
| `MarkComplete` called twice | Idempotency: `if (IsCompleted) return;` |
| `BeginHold` while already holding | Early-out via `HoldingConnectionId != Guid.Empty` |
| Holder disconnects mid-hold | Host validity check sees connection invalid → `BroadcastHoldEnd` |
| Holder dies mid-hold | Host check sees `holder.IsAlive == false` → `BroadcastHoldEnd` |
| Round ends mid-hold | Match.RoundEnded → TaskCoordinator clears → `Reset()` → `OnReset` broadcasts HoldEnd |
| Not enough TaskObjects in scene | `TaskAssigner.Assign` logs warning; gives each crew as many as it can |
| Zero TaskObjects in scene | Guard `totalAssigned > 0` prevents instant crew win |
| Zero alive crew at round start | C1's "all crew dead" check fires first |
| Saboteur holds to 100% | Glow snaps back without `MarkComplete`; visual identical to real |
| Multiple crew on same task | First `BeginHold` wins; others early-out |
| Crew on unassigned task | Visual ramp + snap-back, no progress recorded |
| `TaskCoordinator` missing from scene | No-op; tasks never assigned. Match would never crew-win-via-tasks. Saboteur-win still works. Logs warning if RoundStarted fires with no coordinator. |
| Match missing from scene | RoundStarted never fires; tasks never assign. Dev-time issue. |
| Static event has no subscribers | Null-conditional invoke prevents crash. |

---

## 8. Testing

### Debug commands

- `decompv2_complete_my_tasks` — local-player; force-completes all of caller's assigned tasks
- `decompv2_list_tasks` — any client; logs every task's state
- `decompv2_complete_task <displayName>` — host-only; force-completes a specific task

### Required test scaffold

`minimal.scene` extension:
- `TaskCoordinator` GameObject (TasksPerCrew = 3 for fast testing)
- ~10 `HoldUseTaskObject` GameObjects with placeholder mesh + collider + DisplayName + LocationLabel + GlowRenderer wired

Player prefab extension:
- `Decompression.TaskListHud` and `Decompression.TaskHoverOutline` on the existing `Hud` child

### Manual test matrix

1. **Lobby — no tasks visible.** TaskListHud hidden, no hover outlines.
2. **Round start, crew player — list appears, 3 assigned.** Hover own → outline; hover others → no outline.
3. **Round start, saboteur — no list, no outlines.**
4. **Hold-Use completion (crew on own task)** — 3s ramp → ✓ in list, snap glow.
5. **Hold partial release** — release at 50% → glow snaps; re-hold full 3s → completes.
6. **Saboteur fake-task** — holds to 100% → glow snaps back, no completion. Visually identical to a crew completion.
7. **Crew on unassigned task** — same as saboteur fake.
8. **Two crew try same task** — first wins; second sees no glow start.
9. **Win: all tasks done** — all crew complete all theirs (use `decompv2_complete_my_tasks` for fast test) → "CREW WINS — all tasks complete" within ~1 frame.
10. **Win: dead crew tasks skipped** — kill one crew, others complete theirs → CREW WINS.
11. **Round end clears tasks** — round ends → list empty, no assignments, no outlines.
12. **Multi-round reset** — Lobby → Round → RoundEnd → Lobby → Round; tasks freshly assigned each time.
13. **Late-joiner during Round** — spectator, no list, sees other players doing tasks normally.
14. **Win edge: zero tasks in scene** — delete all TaskObjects, start round → win does NOT fire instantly. Round runs to time expiration.

---

## 9. Public surface other groups depend on

```csharp
// Existing API consumed:
Match.RoundStarted        // C2 subscribes for assignment
Match.RoundEnded          // C2 subscribes for clear
Match.EndRound( MatchOutcome.Crew, "all tasks complete" )  // crew win

// New API exposed (for future task types):
public abstract class TaskObject  // subclass + call MarkComplete = new task type
{
    public string DisplayName { get; set; }
    public string LocationLabel { get; set; }
    public Guid AssignedConnectionId { get; }  // synced
    public bool IsCompleted { get; }            // synced
    public bool IsAssignedToLocal { get; }      // computed
    public bool IsAssignedAndAlive { get; }     // computed
    protected void MarkComplete();              // [Rpc.Host]
    public void Reset();                        // [Rpc.Host]
    protected virtual void OnReset();           // type-specific reset hook
}
```

Future task types only need to subclass `TaskObject` and call `MarkComplete()` when their gameplay-specific completion fires. They can implement any interaction style (IPressable, multi-step path, custom UI mini-game, etc.) without touching the framework.

C3 (meetings & voting) will use `Match.EndRound(MatchOutcome.Crew, "all saboteurs voted out")` — same Match API, no C2 dependency.
