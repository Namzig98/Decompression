# Player & Death Systems Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Player & Death foundation for Decompression v2 so that any system can call `Player.Kill(DeathCause, Vector3)` and the death — ragdoll, vacuum tumble, spectator camera, dead chat, HUD, and sound — is fully driven from that one call, correctly synced across all clients including late joiners.

**Architecture:** Six small single-responsibility components on the Player prefab plus a separate Corpse prefab. Server-authoritative: only the host writes `IsAlive` and spawns corpses. Persistent state (`IsAlive`, corpse existence, cause) is `[Sync]`'d. One-shot reactions (HUD flash, death sting, spectator camera transitions) are `[Rpc.Broadcast]`. Late joiners spectate the active lobby until the next round.

**Tech Stack:** s&box (C#, Sandbox.* APIs, scene/component system), `[Sync]` for state, `[Rpc.Host]` / `[Rpc.Broadcast]` for networking, Razor `PanelComponent` for HUD, stock Facepunch `PlayerController`.

**Spec:** `docs/superpowers/specs/2026-04-30-player-death-systems-design.md`

---

## Notes for the implementing engineer

1. **s&box API drift.** Some attribute and API names have changed across s&box revisions. Where the plan specifies `[Rpc.Host]`, `[Rpc.Broadcast]`, `[Sync]`, `Network.Spawn(...)`, `Connection.Voice` etc., **verify the exact name in the current s&box API** before assuming the plan is wrong. If a name has changed, use the current name and keep the same structural intent. Common examples:
   - `[Rpc.Host]` may also appear as `[Authority]` or `[Rpc.Owner]` in older builds
   - `[Sync]` is current; older code used `[Net]`
   - Voice channel API has been renamed several times

2. **Where to test.** s&box launches a game directly from the editor. ConCmd-based smoke tests run in the launched game's developer console. To run multi-client tests, use the editor's "Test in standalone" / "Launch second instance" options.

3. **Builds.** Build via the editor (auto-compile on save) or, from a CLI in the project directory:
   ```
   dotnet build Code/decompressionv2.csproj
   ```
   The editor surfaces compile errors in its compile log panel.

4. **Commits.** This repo is not yet a git repository. Task 0 initializes it. If the user has already initialized git outside of this plan, skip Task 0.

5. **Strict TDD where possible.** Logic-heavy tasks have failing-test-first steps using ConCmds that exercise the not-yet-implemented method (compile fails → implement → compile passes → smoke-run passes). Structural tasks (defining an enum, creating a prefab) skip TDD and use compile + manual verification.

---

## File structure

Files **created** in this plan (in order of first appearance):

| Path | Responsibility |
|---|---|
| `Code/Death/DeathCause.cs` | The `DeathCause` enum (`Generic`, `Decompression`) |
| `Code/Death/Corpse.cs` | Ragdoll component spawned at death; cause-specific physics; cleanup |
| `Code/Death/CorpseCleanupSignal.cs` | Static signal for "emergency button" cleanup of generic corpses |
| `Code/Player/Player.cs` | Player state, `Kill()` API, `OnPlayerDied` broadcast, static `Died` event |
| `Code/Player/PlayerSpawner.cs` | Scene-level component that spawns Players on connect |
| `Code/Player/Spectator.cs` | Spectator state machine (CorpseLock → FollowingLiving) |
| `Code/Player/DeadChat.cs` | Voice + text channel gating by `IsAlive` |
| `Code/Player/DeathHud.razor` | Razor panel for redout flash + cause-specific text overlay |
| `Code/Player/DeathHud.razor.scss` | Styles for the death HUD |
| `Code/Debug/DebugCommands.cs` | ConCmd debug helpers used as test entry points and stand-ins for groups A/C |
| `Assets/prefabs/player.prefab` | Player GameObject prefab (stock controller + our 5 components) |
| `Assets/prefabs/corpse.prefab` | Corpse GameObject prefab (ModelPhysics + Corpse component) |
| `Assets/sounds/death_decompression.sound` | Vacuum-death sting |
| `Assets/sounds/death_generic.sound` | Generic-death sting |

Files **modified**:

| Path | Change |
|---|---|
| `Assets/scenes/minimal.scene` | Add a PlayerSpawner and at least 4 spawn points |

Files **deleted**:

| Path | Reason |
|---|---|
| `Code/MyComponent.cs` | Template stub, no longer needed |

---

## Task 0: Initialize git and project plumbing

**Files:**
- Create: (none — repo init only)
- Modify: (none)
- Delete: `Code/MyComponent.cs`

- [ ] **Step 1: Initialize git if not already initialized**

```bash
git init
git add -A
git commit -m "chore: initial import of s&box template + design docs"
```

If git is already initialized, just commit the spec/plan docs:

```bash
git add docs/
git commit -m "docs: add Player & Death design + plan"
```

- [ ] **Step 2: Delete the unused template component**

Delete `Code/MyComponent.cs`. The template stub is not used anywhere.

- [ ] **Step 3: Create the directory structure**

Create empty directories (placeholders not needed; create them when the first file lands):
- `Code/Death/`
- `Code/Player/`
- `Code/Debug/`
- `Assets/prefabs/`
- `Assets/sounds/`

- [ ] **Step 4: Verify the project still builds**

Open the project in s&box editor. Wait for the compile log to settle. Expected: green build, no errors.

Or from CLI:
```
dotnet build Code/decompressionv2.csproj
```
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: remove template stub, prepare folder structure"
```

---

## Task 1: Define the DeathCause enum

**Files:**
- Create: `Code/Death/DeathCause.cs`

- [ ] **Step 1: Write the enum**

`Code/Death/DeathCause.cs`:

```csharp
namespace Decompression;

public enum DeathCause
{
    Generic,
    Decompression
}
```

- [ ] **Step 2: Verify it compiles**

Build via editor or `dotnet build Code/decompressionv2.csproj`. Expected: green build.

- [ ] **Step 3: Commit**

```bash
git add Code/Death/DeathCause.cs
git commit -m "feat(death): add DeathCause enum"
```

---

## Task 2: Create the Player component skeleton

**Files:**
- Create: `Code/Player/Player.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Player/Player.cs`:

```csharp
using System;
using Sandbox;

namespace Decompression;

public sealed class Player : Component
{
    [Sync] public bool IsAlive { get; private set; } = true;
    [Sync] public Guid OwnerConnectionId { get; set; }

    public static event Action<Player, DeathCause> Died;

    [Rpc.Host]
    public void Kill( DeathCause cause, Vector3 sourcePosition )
    {
        if ( !IsAlive ) return;
        IsAlive = false;

        // Corpse spawning, controller disable, model hide, broadcast — added in later tasks.

        Died?.Invoke( this, cause );
    }
}
```

The `Kill()` body is intentionally minimal — corpse spawning, controller disable, model hide, and the broadcast are added in later tasks. The idempotency guard (`if ( !IsAlive ) return;`) is here from the start because tests in later tasks rely on it.

- [ ] **Step 2: Verify it compiles**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Player/Player.cs
git commit -m "feat(player): add Player component with Kill() skeleton"
```

---

## Task 3: Create the Corpse component skeleton

**Files:**
- Create: `Code/Death/Corpse.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Death/Corpse.cs`:

```csharp
using System;
using Sandbox;

namespace Decompression;

public sealed class Corpse : Component
{
    [Sync] public DeathCause Cause { get; set; }
    [Sync] public Vector3 SourcePosition { get; set; }
    [Sync] public Guid OriginalOwnerConnectionId { get; set; }
    [Sync] public bool ImpulseApplied { get; set; }

    [Property] public ModelPhysics Physics { get; set; }

    protected override void OnStart()
    {
        if ( Cause == DeathCause.Decompression )
        {
            ConfigureVacuumPhysics();
            if ( Networking.IsHost && !ImpulseApplied )
            {
                ApplyVacuumImpulse();
                ImpulseApplied = true;
            }
        }
        else
        {
            ConfigureNormalPhysics();
        }
    }

    private void ConfigureVacuumPhysics()
    {
        // Filled in by Task 9.
    }

    private void ConfigureNormalPhysics()
    {
        // Filled in by Task 10.
    }

    private void ApplyVacuumImpulse()
    {
        // Filled in by Task 9.
    }

    public void Cleanup()
    {
        if ( !Networking.IsHost ) return;
        GameObject.Destroy();
    }
}
```

- [ ] **Step 2: Verify it compiles**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Death/Corpse.cs
git commit -m "feat(death): add Corpse component skeleton"
```

---

## Task 4: Create the Corpse prefab

**Files:**
- Create: `Assets/prefabs/corpse.prefab`

- [ ] **Step 1: Create the prefab in the editor**

In s&box editor:
1. Right-click `Assets/prefabs/` in the Asset Browser → **New Prefab**.
2. Name it `corpse`.
3. Open the prefab. The root GameObject is empty.
4. Add `SkinnedModelRenderer` component. Leave the model empty for now — the Player component will pose-copy at spawn time.
5. Add `ModelPhysics` component. Set its `Renderer` field to the `SkinnedModelRenderer`.
6. Add the `Corpse` component (created in Task 3).
7. On the `Corpse` component, set the `Physics` field reference to the `ModelPhysics` component.
8. Mark the prefab as **networked** (the prefab inspector has a "Network" / "NetworkSpawn" section — set it so spawned instances replicate to all clients).

- [ ] **Step 2: Verify the prefab loads**

Save and reopen the prefab. Expected: no warnings, all references intact.

- [ ] **Step 3: Commit**

```bash
git add Assets/prefabs/corpse.prefab
git commit -m "feat(death): add corpse prefab"
```

---

## Task 5: Create the Player prefab

**Files:**
- Create: `Assets/prefabs/player.prefab`

- [ ] **Step 1: Create the prefab using stock PlayerController**

In s&box editor:
1. Right-click `Assets/prefabs/` → **New Prefab** named `player`.
2. Open it. On the root GameObject:
   - Add the stock Facepunch `PlayerController` component (search for "PlayerController" in the Add Component menu — it should be the one from `Sandbox` / Citizen kit).
   - Configure the controller's defaults: enable first-person view (eye height ~64u), enable Use input, set walk speed and other movement values to controller defaults.
3. Add the `Player` component (from Task 2).
4. Mark the prefab as **networked**, with **owner-spawned by connection** (each connecting player owns their own player GameObject).

- [ ] **Step 2: Verify the prefab loads**

Save, reopen. Expected: no warnings.

- [ ] **Step 3: Commit**

```bash
git add Assets/prefabs/player.prefab
git commit -m "feat(player): add player prefab using stock PlayerController"
```

---

## Task 6: PlayerSpawner — spawn on connect

**Files:**
- Create: `Code/Player/PlayerSpawner.cs`
- Modify: `Assets/scenes/minimal.scene`

- [ ] **Step 1: Write the PlayerSpawner component**

`Code/Player/PlayerSpawner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class PlayerSpawner : Component, Component.INetworkListener
{
    [Property] public GameObject PlayerPrefab { get; set; }
    [Property] public List<GameObject> SpawnPoints { get; set; } = new();

    public bool RoundInProgress { get; set; }

    public void OnActive( Connection connection )
    {
        if ( !Networking.IsHost ) return;
        if ( PlayerPrefab is null )
        {
            Log.Warning( "PlayerSpawner: PlayerPrefab is not set." );
            return;
        }

        var spawnPoint = PickSpawnPoint();
        var player = PlayerPrefab.Clone( spawnPoint, name: $"Player ({connection.DisplayName})" );
        player.NetworkSpawn( connection );

        var playerComponent = player.Components.Get<Player>();
        if ( playerComponent != null )
        {
            playerComponent.OwnerConnectionId = connection.Id;
            // Late-join behavior wired in Task 16.
        }
    }

    private Transform PickSpawnPoint()
    {
        if ( SpawnPoints.Count == 0 ) return Transform.Zero;
        var pick = SpawnPoints[Random.Shared.Int( 0, SpawnPoints.Count - 1 )];
        return pick.WorldTransform;
    }
}
```

Note: the `OnActive(Connection)` signature comes from `Component.INetworkListener`. Verify against current s&box API — in some versions it is `OnConnect` or `OnPlayerJoined`. Use the equivalent and keep the body identical.

- [ ] **Step 2: Wire the spawner into the scene**

Open `Assets/scenes/minimal.scene` in the editor:
1. Add an empty GameObject named `PlayerSpawner`.
2. Add the `PlayerSpawner` component to it.
3. Set `PlayerPrefab` to `Assets/prefabs/player.prefab`.
4. Create at least 4 empty GameObjects named `SpawnPoint_1` through `SpawnPoint_4`, place them in different positions on the map.
5. Drag each into the `SpawnPoints` list on the `PlayerSpawner`.
6. Save the scene.

- [ ] **Step 3: Verify by launching the game**

Press Play in the editor. Expected: a player GameObject is spawned, you can move with the stock controller, first-person camera works.

- [ ] **Step 4: Verify multi-client spawning**

Launch a second instance (editor's "Launch Second Instance" or equivalent). Expected: two player GameObjects exist, each owned by its connection.

- [ ] **Step 5: Commit**

```bash
git add Code/Player/PlayerSpawner.cs Assets/scenes/minimal.scene
git commit -m "feat(player): add PlayerSpawner and wire into scene"
```

---

## Task 7: Debug commands — `decompv2_kill_self` and `decompv2_kill`

This task installs the debug ConCmds we'll use to drive every subsequent test. They get progressively more useful as we implement more of the system. We start them now so the next task can write its tests through them.

**Files:**
- Create: `Code/Debug/DebugCommands.cs`

- [ ] **Step 1: Write the failing test (compile-failing call site)**

`Code/Debug/DebugCommands.cs`:

```csharp
using System.Linq;
using Sandbox;

namespace Decompression;

public static class DebugCommands
{
    [ConCmd( "decompv2_kill_self" )]
    public static void KillSelf()
    {
        var localPlayer = Game.ActiveScene?.GetAllComponents<Player>()
            .FirstOrDefault( p => p.Network.OwnerConnection == Connection.Local );
        if ( localPlayer is null )
        {
            Log.Warning( "decompv2_kill_self: no local player found" );
            return;
        }
        localPlayer.Kill( DeathCause.Generic, Vector3.Zero );
    }

    [ConCmd( "decompv2_kill" )]
    public static void Kill( string connectionDisplayName, string causeName )
    {
        if ( !Networking.IsHost )
        {
            Log.Warning( "decompv2_kill: host only" );
            return;
        }
        if ( !System.Enum.TryParse<DeathCause>( causeName, ignoreCase: true, out var cause ) )
        {
            Log.Warning( $"decompv2_kill: unknown cause '{causeName}'. Use Generic or Decompression." );
            return;
        }
        var target = Game.ActiveScene.GetAllComponents<Player>()
            .FirstOrDefault( p => p.Network.OwnerConnection?.DisplayName == connectionDisplayName );
        if ( target is null )
        {
            Log.Warning( $"decompv2_kill: no player named '{connectionDisplayName}'" );
            return;
        }
        target.Kill( cause, Vector3.Zero );
    }
}
```

Verify against current s&box API: `Game.ActiveScene`, `Connection.Local`, `Network.OwnerConnection`, `[ConCmd(...)]` (some s&box revisions used `[ConCmd.Server]`/`[ConCmd.Client]`). Use the current equivalents.

- [ ] **Step 2: Verify it compiles**

Build. Expected: green.

- [ ] **Step 3: Smoke-run**

Launch the game, open the developer console, run:
```
decompv2_kill_self
```
Expected: console message — no death visuals yet because we haven't wired corpse spawning. The Player's `IsAlive` flips to `false` (which you can confirm by re-running `decompv2_kill_self`; the second invocation hits the idempotency guard and silently does nothing).

- [ ] **Step 4: Commit**

```bash
git add Code/Debug/DebugCommands.cs
git commit -m "feat(debug): add decompv2_kill and decompv2_kill_self ConCmds"
```

---

## Task 8: Spawn the corpse prefab on Kill()

**Files:**
- Modify: `Code/Player/Player.cs`

- [ ] **Step 1: Write the failing test**

We can't write a literal unit test, but we can establish a smoke test plan we'll verify after implementing.

Smoke test (run after Step 3): launch game, run `decompv2_kill_self`. Expected: a corpse GameObject appears at the player's position. Currently this fails because `Kill()` doesn't spawn a corpse.

- [ ] **Step 2: Add corpse-spawning fields and logic to Player**

`Code/Player/Player.cs` — replace the file with:

```csharp
using System;
using Sandbox;

namespace Decompression;

public sealed class Player : Component
{
    [Property] public GameObject CorpsePrefab { get; set; }
    [Property] public SkinnedModelRenderer ModelRenderer { get; set; }

    [Sync] public bool IsAlive { get; private set; } = true;
    [Sync] public Guid OwnerConnectionId { get; set; }

    public static event Action<Player, DeathCause> Died;

    [Rpc.Host]
    public void Kill( DeathCause cause, Vector3 sourcePosition )
    {
        if ( !IsAlive ) return;
        IsAlive = false;

        var corpse = SpawnCorpse( cause, sourcePosition );
        DisableLivingPlayer();

        OnPlayerDied( cause, corpse?.GameObject.Id ?? Guid.Empty, sourcePosition );

        Died?.Invoke( this, cause );
    }

    private Corpse SpawnCorpse( DeathCause cause, Vector3 sourcePosition )
    {
        if ( CorpsePrefab is null )
        {
            Log.Warning( "Player.Kill: CorpsePrefab not set" );
            return null;
        }
        var corpseGo = CorpsePrefab.Clone( WorldTransform, name: $"Corpse ({GameObject.Name})" );
        corpseGo.NetworkSpawn();

        var corpse = corpseGo.Components.Get<Corpse>();
        if ( corpse != null )
        {
            corpse.Cause = cause;
            corpse.SourcePosition = sourcePosition;
            corpse.OriginalOwnerConnectionId = OwnerConnectionId;
        }

        // Pose-copy: align the corpse renderer to current player pose.
        if ( ModelRenderer != null )
        {
            var corpseRenderer = corpseGo.Components.Get<SkinnedModelRenderer>();
            if ( corpseRenderer != null )
            {
                corpseRenderer.Model = ModelRenderer.Model;
                corpseRenderer.WorldTransform = ModelRenderer.WorldTransform;
            }
        }

        return corpse;
    }

    private void DisableLivingPlayer()
    {
        var controller = Components.Get<PlayerController>();
        if ( controller != null ) controller.Enabled = false;
        if ( ModelRenderer != null ) ModelRenderer.Enabled = false;
    }

    [Rpc.Broadcast]
    private void OnPlayerDied( DeathCause cause, Guid corpseId, Vector3 sourcePosition )
    {
        // Local effects (HUD, sound, spectator) wired in later tasks.
    }
}
```

Note: `PlayerController` is the stock Facepunch component type. If the type name differs in your s&box revision, substitute the correct one — the intent is to disable input + locomotion.

- [ ] **Step 3: Wire references on the player prefab**

Open `Assets/prefabs/player.prefab` in the editor:
1. Set the `CorpsePrefab` field on the `Player` component to `Assets/prefabs/corpse.prefab`.
2. Set the `ModelRenderer` field to the `SkinnedModelRenderer` component on the player (the one provided by stock `PlayerController` — drag it from the hierarchy).

Save the prefab.

- [ ] **Step 4: Verify it compiles**

Build. Expected: green.

- [ ] **Step 5: Smoke-run**

Launch the game. Console: `decompv2_kill_self`. Expected:
- A corpse GameObject appears at your last living position.
- Your player model is hidden.
- Your `PlayerController` is disabled (you can no longer move).
- Running `decompv2_kill_self` a second time does nothing (idempotency).

- [ ] **Step 6: Smoke-run multi-client**

Launch a second instance. Have the host kill themselves via `decompv2_kill_self`. Expected:
- Corpse appears for **both** clients at the same position.
- Both clients see the host's player model hidden.
- Host's `IsAlive` is `false` on both clients.

- [ ] **Step 7: Commit**

```bash
git add Code/Player/Player.cs Assets/prefabs/player.prefab
git commit -m "feat(player): spawn corpse prefab and disable controller on Kill"
```

---

## Task 9: Decompression-cause vacuum physics + impulse

**Files:**
- Modify: `Code/Death/Corpse.cs`

- [ ] **Step 1: Write the failing test**

Smoke test: kill self with `decompv2_kill <name> Decompression`. Currently the corpse spawns but uses default physics — falls under gravity, doesn't tumble. After this task, it should drift in zero-G with an impulse away from `SourcePosition`.

- [ ] **Step 2: Implement vacuum physics and impulse**

`Code/Death/Corpse.cs` — replace `ConfigureVacuumPhysics` and `ApplyVacuumImpulse`:

```csharp
private void ConfigureVacuumPhysics()
{
    if ( Physics is null ) return;
    foreach ( var body in Physics.PhysicsGroup?.Bodies ?? System.Array.Empty<PhysicsBody>() )
    {
        body.GravityEnabled = false;
        body.LinearDrag = 0.05f;
        body.AngularDrag = 0.05f;
    }
}

private void ApplyVacuumImpulse()
{
    if ( Physics is null ) return;
    var rng = new System.Random();
    foreach ( var body in Physics.PhysicsGroup?.Bodies ?? System.Array.Empty<PhysicsBody>() )
    {
        var direction = (body.Position - SourcePosition).Normal;
        if ( direction.IsNearZeroLength ) direction = Vector3.Random.Normal;

        const float impulseStrength = 600f;
        body.ApplyImpulse( direction * impulseStrength * body.Mass );

        var spin = new Vector3(
            (float)(rng.NextDouble() * 200 - 100),
            (float)(rng.NextDouble() * 200 - 100),
            (float)(rng.NextDouble() * 200 - 100)
        );
        body.ApplyAngularImpulse( spin * body.Mass );
    }
}
```

Verify the API against current s&box: `Physics.PhysicsGroup.Bodies`, `body.GravityEnabled`, `body.LinearDrag`, `body.ApplyImpulse`, `body.ApplyAngularImpulse`. The intent is: gravity off, very low drag, single linear impulse outward from `SourcePosition` and a random angular impulse — all per body in the ragdoll.

The `impulseStrength` constant (`600f`) is a starting tuning value; expect to adjust it in playtesting.

- [ ] **Step 3: Verify it compiles**

Build. Expected: green.

- [ ] **Step 4: Smoke-run — Decompression cause**

Launch the game. Pick a position you can reference (e.g., near the world origin). Run:
```
decompv2_kill_self
```
…wait — `decompv2_kill_self` always uses `Generic`. We need a Decompression-causing variant for testing. Add it temporarily (will be cleaned up by tuning later):

In `Code/Debug/DebugCommands.cs`, add:

```csharp
[ConCmd( "decompv2_vent_self" )]
public static void VentSelf()
{
    var localPlayer = Game.ActiveScene?.GetAllComponents<Player>()
        .FirstOrDefault( p => p.Network.OwnerConnection == Connection.Local );
    if ( localPlayer is null ) return;

    // Source position is offset from the player so the impulse direction is meaningful.
    var hatchPos = localPlayer.WorldPosition + Vector3.Down * 100f;
    localPlayer.Kill( DeathCause.Decompression, hatchPos );
}
```

Then run `decompv2_vent_self`. Expected: corpse spawns, no gravity, drifts upward (away from the hatch position 100u below), tumbles randomly.

- [ ] **Step 5: Smoke-run — Generic cause unaffected**

Run `decompv2_kill_self`. Expected: corpse spawns with normal gravity, falls to the floor, settles within a few seconds. No impulse, no zero-G.

- [ ] **Step 6: Smoke-run — late-join guard**

Two clients. Host runs `decompv2_vent_self` and waits 3 seconds. Then a third client joins (or have client 2 disconnect/reconnect to simulate). Expected: the new client sees the corpse in mid-flight, **velocity inherited from physics sync**, no re-applied impulse (no visible "double-punt"). Verify by checking `Corpse.ImpulseApplied == true` on the late-joining client.

- [ ] **Step 7: Commit**

```bash
git add Code/Death/Corpse.cs Code/Debug/DebugCommands.cs
git commit -m "feat(death): vacuum physics + impulse for Decompression-cause corpses"
```

---

## Task 10: Generic-cause normal physics + persistent corpse

**Files:**
- Modify: `Code/Death/Corpse.cs`

- [ ] **Step 1: Implement normal physics config**

In `Code/Death/Corpse.cs`, replace `ConfigureNormalPhysics`:

```csharp
private void ConfigureNormalPhysics()
{
    if ( Physics is null ) return;
    foreach ( var body in Physics.PhysicsGroup?.Bodies ?? System.Array.Empty<PhysicsBody>() )
    {
        body.GravityEnabled = true;
        body.LinearDrag = 0.5f;
        body.AngularDrag = 0.5f;
    }
}
```

These are reasonable defaults; tune if corpses feel too floaty or too lead-like.

- [ ] **Step 2: Verify it compiles**

Build. Expected: green.

- [ ] **Step 3: Smoke-run**

Launch the game. Run `decompv2_kill_self`. Expected: corpse falls under gravity, settles on the floor, stays there indefinitely. No despawn.

- [ ] **Step 4: Commit**

```bash
git add Code/Death/Corpse.cs
git commit -m "feat(death): normal physics for Generic-cause corpses"
```

---

## Task 11: Decompression-corpse 30s despawn timer

**Files:**
- Modify: `Code/Death/Corpse.cs`

- [ ] **Step 1: Add despawn timer**

In `Code/Death/Corpse.cs`, modify `OnStart` and add an update:

```csharp
private TimeSince timeSinceSpawn;
private const float DecompressionDespawnSeconds = 30f;

protected override void OnStart()
{
    timeSinceSpawn = 0f;

    if ( Cause == DeathCause.Decompression )
    {
        ConfigureVacuumPhysics();
        if ( Networking.IsHost && !ImpulseApplied )
        {
            ApplyVacuumImpulse();
            ImpulseApplied = true;
        }
    }
    else
    {
        ConfigureNormalPhysics();
    }
}

protected override void OnUpdate()
{
    if ( !Networking.IsHost ) return;
    if ( Cause != DeathCause.Decompression ) return;
    if ( timeSinceSpawn >= DecompressionDespawnSeconds )
    {
        Cleanup();
    }
}
```

`TimeSince` is the s&box stopwatch helper that auto-increments with `Time.Delta`. Verify the type name in current API.

- [ ] **Step 2: Verify it compiles**

Build. Expected: green.

- [ ] **Step 3: Smoke-run**

Launch the game. Run `decompv2_vent_self`. Wait 30 seconds. Expected: corpse despawns automatically, both on host and client.

Run `decompv2_kill_self` (Generic). Wait 60 seconds. Expected: corpse stays.

- [ ] **Step 4: Commit**

```bash
git add Code/Death/Corpse.cs
git commit -m "feat(death): 30s despawn timer for Decompression corpses"
```

---

## Task 12: CorpseCleanupSignal — emergency button hook

**Files:**
- Create: `Code/Death/CorpseCleanupSignal.cs`
- Modify: `Code/Debug/DebugCommands.cs`

- [ ] **Step 1: Write the failing test**

We add the ConCmd that calls `CorpseCleanupSignal.RaiseGenericCleanup()` first. It will not compile until Step 2.

In `Code/Debug/DebugCommands.cs`, add:

```csharp
[ConCmd( "decompv2_cleanup_corpses" )]
public static void CleanupCorpses()
{
    if ( !Networking.IsHost )
    {
        Log.Warning( "decompv2_cleanup_corpses: host only" );
        return;
    }
    CorpseCleanupSignal.RaiseGenericCleanup();
}
```

Build. Expected: **fail** with "type CorpseCleanupSignal does not exist". This is the failing test.

- [ ] **Step 2: Implement CorpseCleanupSignal**

`Code/Death/CorpseCleanupSignal.cs`:

```csharp
using Sandbox;

namespace Decompression;

public static class CorpseCleanupSignal
{
    public static void RaiseGenericCleanup()
    {
        if ( !Networking.IsHost ) return;
        var corpses = Game.ActiveScene.GetAllComponents<Corpse>();
        foreach ( var corpse in corpses )
        {
            if ( corpse.Cause == DeathCause.Generic )
            {
                corpse.Cleanup();
            }
        }
    }
}
```

- [ ] **Step 3: Verify it compiles**

Build. Expected: green.

- [ ] **Step 4: Smoke-run**

Launch the game. Run `decompv2_kill_self` three times in different positions (re-enabling the player between deaths is hard right now — easier: launch two clients and kill each, leaving two Generic corpses). Then run on the host:
```
decompv2_cleanup_corpses
```
Expected: all Generic corpses despawn on both clients.

Run `decompv2_vent_self` to make a Decompression corpse, then `decompv2_cleanup_corpses`. Expected: Decompression corpse stays (only Generic is cleaned up).

- [ ] **Step 5: Commit**

```bash
git add Code/Death/CorpseCleanupSignal.cs Code/Debug/DebugCommands.cs
git commit -m "feat(death): CorpseCleanupSignal for emergency-button hook"
```

---

## Task 13: Spectator component — corpse-lock phase

**Files:**
- Create: `Code/Player/Spectator.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Player/Spectator.cs`:

```csharp
using System;
using Sandbox;

namespace Decompression;

public sealed class Spectator : Component
{
    public enum Phase { Inactive, CorpseLock, FollowingLiving }

    [Property] public CameraComponent Camera { get; set; }

    public Phase CurrentPhase { get; private set; } = Phase.Inactive;
    public Corpse LockedCorpse { get; private set; }

    private const float CorpseLockDuration = 5f;
    private TimeSince phaseStarted;

    public void Begin( Corpse corpse )
    {
        LockedCorpse = corpse;
        if ( corpse is null )
        {
            EnterFollowingLiving();
        }
        else
        {
            EnterCorpseLock();
        }
    }

    private void EnterCorpseLock()
    {
        CurrentPhase = Phase.CorpseLock;
        phaseStarted = 0f;
    }

    private void EnterFollowingLiving()
    {
        CurrentPhase = Phase.FollowingLiving;
        phaseStarted = 0f;
        // Picks first available living player on next OnUpdate.
    }

    protected override void OnUpdate()
    {
        switch ( CurrentPhase )
        {
            case Phase.CorpseLock:
                UpdateCorpseLock();
                if ( phaseStarted >= CorpseLockDuration )
                {
                    EnterFollowingLiving();
                }
                break;
            case Phase.FollowingLiving:
                UpdateFollowingLiving();
                break;
        }
    }

    private void UpdateCorpseLock()
    {
        if ( Camera is null || LockedCorpse is null ) return;
        var corpsePos = LockedCorpse.WorldPosition;
        Camera.WorldPosition = corpsePos + Vector3.Up * 64f + Vector3.Backward * 96f;
        Camera.WorldRotation = Rotation.LookAt( (corpsePos - Camera.WorldPosition).Normal );
    }

    private void UpdateFollowingLiving()
    {
        // Filled in by Task 14.
    }

    public void CycleNext() { /* Task 14 */ }
    public void CyclePrevious() { /* Task 14 */ }
}
```

- [ ] **Step 2: Wire Spectator into the Player prefab**

Open `Assets/prefabs/player.prefab`:
1. Add the `Spectator` component to the player root.
2. Set the `Camera` field to the `CameraComponent` already present (from stock PlayerController). Drag the camera reference in.
3. Save the prefab.

- [ ] **Step 3: Trigger Spectator from `OnPlayerDied`**

In `Code/Player/Player.cs`, replace the empty `OnPlayerDied`:

```csharp
[Rpc.Broadcast]
private void OnPlayerDied( DeathCause cause, Guid corpseId, Vector3 sourcePosition )
{
    if ( !IsLocallyControlled() ) return;

    Corpse corpseRef = null;
    if ( corpseId != Guid.Empty )
    {
        var go = Game.ActiveScene.Directory.FindByGuid( corpseId );
        corpseRef = go?.Components.Get<Corpse>();
    }

    var spectator = Components.Get<Spectator>();
    spectator?.Begin( corpseRef );
}

private bool IsLocallyControlled()
{
    return Network.OwnerConnection == Connection.Local;
}
```

`Game.ActiveScene.Directory.FindByGuid` — verify the lookup-by-guid API in current s&box; the intent is to resolve the spawned corpse GameObject by its network GUID.

- [ ] **Step 4: Verify it compiles**

Build. Expected: green.

- [ ] **Step 5: Smoke-run**

Launch the game. Run `decompv2_vent_self`. Expected:
- For 5 seconds, the camera locks on the tumbling corpse from a third-person fixed angle.
- After 5 seconds, the spectator transitions to `FollowingLiving` (which is a stub — the camera will get stuck in space until Task 14 completes).

- [ ] **Step 6: Commit**

```bash
git add Code/Player/Spectator.cs Code/Player/Player.cs Assets/prefabs/player.prefab
git commit -m "feat(player): Spectator component with corpse-lock phase"
```

---

## Task 14: Spectator FollowingLiving — cycle living players

**Files:**
- Modify: `Code/Player/Spectator.cs`

- [ ] **Step 1: Implement FollowingLiving and cycling**

In `Code/Player/Spectator.cs`, replace `UpdateFollowingLiving`, `CycleNext`, `CyclePrevious`:

```csharp
private Player followedPlayer;

private void UpdateFollowingLiving()
{
    if ( Camera is null ) return;

    // Drop dead targets.
    if ( followedPlayer != null && !followedPlayer.IsAlive )
    {
        followedPlayer = null;
    }

    // Pick first available if none.
    if ( followedPlayer is null )
    {
        followedPlayer = PickFirstLiving();
    }

    if ( followedPlayer is null )
    {
        // No living players left — fall back to map overview.
        Camera.WorldPosition = Vector3.Up * 1500f;
        Camera.WorldRotation = Rotation.LookAt( Vector3.Down );
        return;
    }

    var targetPos = followedPlayer.WorldPosition + Vector3.Up * 64f;
    Camera.WorldPosition = targetPos + (-followedPlayer.WorldRotation.Forward) * 128f + Vector3.Up * 32f;
    Camera.WorldRotation = Rotation.LookAt( (targetPos - Camera.WorldPosition).Normal );
}

private Player PickFirstLiving()
{
    return Game.ActiveScene.GetAllComponents<Player>()
        .Where( p => p.IsAlive )
        .FirstOrDefault();
}

public void CycleNext()
{
    if ( CurrentPhase != Phase.FollowingLiving ) return;
    CycleBy( +1 );
}

public void CyclePrevious()
{
    if ( CurrentPhase != Phase.FollowingLiving ) return;
    CycleBy( -1 );
}

private void CycleBy( int direction )
{
    var living = Game.ActiveScene.GetAllComponents<Player>()
        .Where( p => p.IsAlive )
        .ToList();
    if ( living.Count == 0 ) { followedPlayer = null; return; }

    var idx = followedPlayer != null ? living.IndexOf( followedPlayer ) : -1;
    idx = (idx + direction + living.Count) % living.Count;
    followedPlayer = living[idx];
}
```

Add `using System.Linq;` at the top if not already there.

- [ ] **Step 2: Bind input to cycling**

In `Spectator.cs`, in `OnUpdate`:

```csharp
protected override void OnUpdate()
{
    if ( CurrentPhase == Phase.FollowingLiving )
    {
        if ( Input.Pressed( "attack1" ) ) CycleNext();
        if ( Input.Pressed( "attack2" ) ) CyclePrevious();
    }
    // … existing switch on CurrentPhase below
    switch ( CurrentPhase )
    {
        case Phase.CorpseLock: /* … */
        case Phase.FollowingLiving: UpdateFollowingLiving(); break;
    }
}
```

(Adjust `"attack1"` / `"attack2"` to whatever input action names exist in `ProjectSettings/Input.config`. If they don't exist as named actions, use the raw mouse-button enum.)

- [ ] **Step 3: Verify it compiles**

Build. Expected: green.

- [ ] **Step 4: Smoke-run**

Two clients. Host kills self via `decompv2_kill_self`. Expected:
- Corpse-lock for 5s.
- Then camera flips to following the other living client (third-person over-shoulder).
- Mouse-1 / mouse-2 cycle through any remaining living players.
- If the followed player dies (have client 2 also `decompv2_kill_self`), camera auto-cycles or falls back to map overview.

- [ ] **Step 5: Commit**

```bash
git add Code/Player/Spectator.cs
git commit -m "feat(player): Spectator FollowingLiving cycles living players"
```

---

## Task 15: DeathHud — redout flash + cause text

**Files:**
- Create: `Code/Player/DeathHud.razor`
- Create: `Code/Player/DeathHud.razor.scss`
- Modify: `Code/Player/Player.cs`

- [ ] **Step 1: Create the Razor panel**

`Code/Player/DeathHud.razor`:

```razor
@using Sandbox;
@using Sandbox.UI;
@inherits PanelComponent

<root>
    @if ( showFlash )
    {
        <div class="redout"></div>
    }
    @if ( showText )
    {
        <div class="cause-text">@causeText</div>
    }
</root>

@code {
    bool showFlash;
    bool showText;
    string causeText = "";
    TimeSince flashStarted;
    TimeSince textStarted;

    protected override void OnAwake()
    {
        Player.Died += OnPlayerDied;
    }

    protected override void OnDestroy()
    {
        Player.Died -= OnPlayerDied;
    }

    private void OnPlayerDied( Player player, DeathCause cause )
    {
        if ( player.Network.OwnerConnection != Connection.Local ) return;
        causeText = cause switch
        {
            DeathCause.Decompression => "VACUUM EXPOSURE",
            _ => "DECEASED",
        };
        showFlash = true;
        flashStarted = 0f;
        showText = true;
        textStarted = 0f;
    }

    protected override int BuildHash() => System.HashCode.Combine( showFlash, showText, causeText );

    protected override void OnUpdate()
    {
        if ( showFlash && flashStarted > 0.5f ) showFlash = false;
        // Text fades on its own via SCSS animation; hide after 4s.
        if ( showText && textStarted > 4f ) showText = false;
    }
}
```

`Code/Player/DeathHud.razor.scss`:

```scss
.redout {
    position: absolute;
    width: 100%;
    height: 100%;
    background-color: rgba(170, 0, 0, 0.7);
    pointer-events: none;
    animation: redout-fade 0.5s ease-out forwards;
}

@keyframes redout-fade {
    0%   { opacity: 1; }
    100% { opacity: 0; }
}

.cause-text {
    position: absolute;
    bottom: 30%;
    left: 0;
    right: 0;
    text-align: center;
    color: #ddd;
    font-family: monospace;
    font-size: 48px;
    letter-spacing: 8px;
    opacity: 0;
    animation: cause-text-fade 4s ease-out forwards;
}

@keyframes cause-text-fade {
    0%   { opacity: 0; }
    20%  { opacity: 1; }
    80%  { opacity: 1; }
    100% { opacity: 0; }
}
```

Verify Razor panel boilerplate against current s&box: `@inherits PanelComponent`, `<root>`, `OnAwake`/`OnDestroy`, `BuildHash`, etc. Keep the structural intent: subscribe to `Player.Died` for the local player, render redout + text with CSS animations.

- [ ] **Step 2: Wire DeathHud into the Player prefab**

Open `Assets/prefabs/player.prefab`:
1. Add the `DeathHud` Razor panel component to the player root (or to a child screen-space UI GameObject).
2. Save the prefab.

- [ ] **Step 3: Verify it compiles**

Build. Expected: green.

- [ ] **Step 4: Smoke-run — Decompression**

Launch the game. Run `decompv2_vent_self`. Expected:
- 0.5s redout flash on the local screen.
- "VACUUM EXPOSURE" text fades in and stays for ~3s, then fades out.
- Other clients do not see this.

- [ ] **Step 5: Smoke-run — Generic**

Run `decompv2_kill_self`. Expected: same flash, but text reads "DECEASED".

- [ ] **Step 6: Commit**

```bash
git add Code/Player/DeathHud.razor Code/Player/DeathHud.razor.scss Assets/prefabs/player.prefab
git commit -m "feat(player): DeathHud Razor panel for redout + cause text"
```

---

## Task 16: Cause-specific death sounds

**Files:**
- Create: `Assets/sounds/death_decompression.sound`
- Create: `Assets/sounds/death_generic.sound`
- Modify: `Code/Player/Player.cs`

- [ ] **Step 1: Create the sound assets**

In the s&box editor:
1. In `Assets/sounds/`, right-click → **New Sound Event**, name it `death_decompression`.
2. Add a placeholder source clip (anything — a hull-rush + ear-pop sound if you have one, otherwise any wav). Actual sound design is a polish pass.
3. Repeat for `death_generic` with a muted heartbeat-stop or generic sting.

- [ ] **Step 2: Add sound playback to OnPlayerDied**

In `Code/Player/Player.cs`, modify `OnPlayerDied`:

```csharp
[Property] public SoundEvent DeathSoundDecompression { get; set; }
[Property] public SoundEvent DeathSoundGeneric { get; set; }

[Rpc.Broadcast]
private void OnPlayerDied( DeathCause cause, Guid corpseId, Vector3 sourcePosition )
{
    var sound = cause == DeathCause.Decompression ? DeathSoundDecompression : DeathSoundGeneric;
    if ( sound != null )
    {
        Sound.Play( sound, WorldPosition );
    }

    if ( !IsLocallyControlled() ) return;

    Corpse corpseRef = null;
    if ( corpseId != Guid.Empty )
    {
        var go = Game.ActiveScene.Directory.FindByGuid( corpseId );
        corpseRef = go?.Components.Get<Corpse>();
    }

    var spectator = Components.Get<Spectator>();
    spectator?.Begin( corpseRef );
}
```

- [ ] **Step 3: Wire sound references on the prefab**

Open `Assets/prefabs/player.prefab`:
1. Set `DeathSoundDecompression` to `Assets/sounds/death_decompression.sound`.
2. Set `DeathSoundGeneric` to `Assets/sounds/death_generic.sound`.
3. Save.

- [ ] **Step 4: Verify it compiles**

Build. Expected: green.

- [ ] **Step 5: Smoke-run**

Launch the game. Run `decompv2_vent_self`. Expected: vacuum sting plays. Run `decompv2_kill_self`. Expected: generic sting plays.

- [ ] **Step 6: Commit**

```bash
git add Code/Player/Player.cs Assets/sounds/death_decompression.sound Assets/sounds/death_generic.sound Assets/prefabs/player.prefab
git commit -m "feat(player): cause-specific death stings"
```

---

## Task 17: DeadChat — voice + text gating

**Files:**
- Create: `Code/Player/DeadChat.cs`

- [ ] **Step 1: Write the component**

`Code/Player/DeadChat.cs`:

```csharp
using Sandbox;

namespace Decompression;

public sealed class DeadChat : Component
{
    private bool lastKnownAlive = true;
    private Player player;

    protected override void OnStart()
    {
        player = Components.Get<Player>();
        ApplyChannelRouting( player?.IsAlive ?? true );
    }

    protected override void OnUpdate()
    {
        if ( player is null ) return;
        if ( player.IsAlive != lastKnownAlive )
        {
            ApplyChannelRouting( player.IsAlive );
            lastKnownAlive = player.IsAlive;
        }
    }

    private void ApplyChannelRouting( bool isAlive )
    {
        // Gate this connection's voice + text so:
        //   alive ↔ alive only
        //   dead  ↔ dead only
        //
        // Exact API surface depends on s&box revision. Common shape:
        //   Connection.WantsToHear( otherConnection ) callback that returns alive==alive.
        //   Or: voice channel id assignment via Voice.Channel = isAlive ? 0 : 1.
        //
        // Implement this against the current s&box voice/text APIs. The intent is
        // a one-way gate that re-evaluates whenever IsAlive changes.

        // Placeholder for implementer to fill in with current API. Do not commit
        // a no-op — this must actually reroute channels.
        Log.Info( $"DeadChat: routing this connection as {(isAlive ? "alive" : "dead")}" );
    }
}
```

This is the one task in the plan with a true implementation gap: the voice/text channel API in s&box has churned heavily, and pinning a specific call here would likely be wrong. The contract is:
- When the local Player's `IsAlive` flips (via `[Sync]`), reconfigure voice and text routing so dead can only chat with dead and alive can only chat with alive.
- Re-evaluate reactively on state change, not on a poll.

The implementer should fill in `ApplyChannelRouting` against the current s&box voice/text API. Verify by playing two-client tests.

- [ ] **Step 2: Wire DeadChat into the Player prefab**

Open `Assets/prefabs/player.prefab`:
1. Add the `DeadChat` component.
2. Save.

- [ ] **Step 3: Verify it compiles**

Build. Expected: green.

- [ ] **Step 4: Smoke-run**

Two clients. Both speak. Expected: both hear each other (both alive). Host kills client 2 via `decompv2_kill <name> Generic`. Now:
- Alive host speaks. Expected: dead client 2 does NOT hear.
- Dead client 2 speaks. Expected: alive host does NOT hear.

If the two-client smoke test fails, the implementation of `ApplyChannelRouting` is wrong and must be fixed before continuing.

- [ ] **Step 5: Commit**

```bash
git add Code/Player/DeadChat.cs Assets/prefabs/player.prefab
git commit -m "feat(player): DeadChat voice + text gating"
```

---

## Task 18: Late-join handling — spawn as spectator if round in progress

**Files:**
- Modify: `Code/Player/PlayerSpawner.cs`
- Modify: `Code/Player/Player.cs`

- [ ] **Step 1: Add a private SpawnAsLateJoiner path on Player**

In `Code/Player/Player.cs`, add:

```csharp
public void SpawnAsLateJoiner()
{
    if ( !Networking.IsHost ) return;
    IsAlive = false;
    DisableLivingPlayer();
    NotifyLateJoinerLocal();
}

[Rpc.Owner]
private void NotifyLateJoinerLocal()
{
    var spectator = Components.Get<Spectator>();
    spectator?.Begin( null );
}
```

`[Rpc.Owner]` runs on the connection that owns this player GameObject. Verify the attribute name in current s&box (older revisions: `[Authority]` with different semantics, or `[ClientRpc]` patterns).

- [ ] **Step 2: Use SpawnAsLateJoiner in PlayerSpawner**

In `Code/Player/PlayerSpawner.cs`, modify `OnActive`:

```csharp
public void OnActive( Connection connection )
{
    if ( !Networking.IsHost ) return;
    if ( PlayerPrefab is null )
    {
        Log.Warning( "PlayerSpawner: PlayerPrefab is not set." );
        return;
    }

    var spawnPoint = PickSpawnPoint();
    var player = PlayerPrefab.Clone( spawnPoint, name: $"Player ({connection.DisplayName})" );
    player.NetworkSpawn( connection );

    var playerComponent = player.Components.Get<Player>();
    if ( playerComponent != null )
    {
        playerComponent.OwnerConnectionId = connection.Id;
        if ( RoundInProgress )
        {
            playerComponent.SpawnAsLateJoiner();
        }
    }
}
```

- [ ] **Step 3: Verify it compiles**

Build. Expected: green.

- [ ] **Step 4: Smoke-run**

Two clients. From the host, set `RoundInProgress = true` on the spawner. The simplest way (since group C doesn't exist yet) is via a debug ConCmd — add to `DebugCommands.cs`:

```csharp
[ConCmd( "decompv2_round_in_progress" )]
public static void SetRoundInProgress( bool value )
{
    var spawner = Game.ActiveScene.GetAllComponents<PlayerSpawner>().FirstOrDefault();
    if ( spawner != null ) spawner.RoundInProgress = value;
}
```

Run on host: `decompv2_round_in_progress true`. Then have a third client connect. Expected:
- Late-joiner's player has `IsAlive = false` from the start.
- Their `PlayerController` is disabled.
- Their model is hidden.
- Their Spectator goes straight to `FollowingLiving` (no 5s corpse-lock).
- They can mouse-cycle the host and the existing client.

Disconnect everyone. Re-launch with `RoundInProgress = false` (default). Expected: new joiners spawn alive normally.

- [ ] **Step 5: Commit**

```bash
git add Code/Player/Player.cs Code/Player/PlayerSpawner.cs Code/Debug/DebugCommands.cs
git commit -m "feat(player): late-join spawns as spectator when round in progress"
```

---

## Task 19: Final integration test pass

This task does no new implementation — it runs the full manual test matrix from the spec and either signs off the work or files concrete bugs.

**Files:** none

- [ ] **Step 1: Test 1 — basic two-client kill**

Two clients (host + A). Host runs `decompv2_kill <A> Generic`. Verify:
- A's body appears as a corpse on both clients at A's position.
- A's player model hidden on both clients.
- A's `PlayerController` disabled on A's screen (A can't move).
- A hears the generic death sting.
- A sees the redout + "DECEASED" text.
- Host hears the death sting (positional).
- A's spectator: 5s corpse-lock, then follows the host.

- [ ] **Step 2: Test 2 — self-kill via console**

Two clients. Client A runs `decompv2_kill_self`. Verify same flow as Test 1, including that the `[Rpc.Host]` round-trip works (the RPC is initiated from a non-host client and runs on the host).

- [ ] **Step 3: Test 3 — simultaneous kills**

Three clients (host + A + B). Host runs `decompv2_kill <A> Generic` and `decompv2_kill <B> Generic` rapidly. Verify:
- Both A and B die.
- Two corpses, one each.
- A and B both spectate independently.

- [ ] **Step 4: Test 4 — late join**

Host + A. Host kills A. Wait 10 seconds. Client B joins.
With `RoundInProgress = false` (default for this manual test): B spawns alive. Verify B sees A's corpse on the floor, A's `IsAlive == false`, but B does **not** hear the death sting and does **not** see A's HUD effects.

With `RoundInProgress = true`: B spawns as a spectator (already covered in Task 18 Step 4). Verify the entire late-join flow including spectator skipping corpse-lock.

- [ ] **Step 5: Test 5 — Decompression vacuum tumble**

Host + A. Host runs `decompv2_kill <A> Decompression` (the second arg passes a `Vector3.Zero` source position; for a more visible test add a temp ConCmd that lets you target a hatch position, or just observe that the corpse drifts in zero-G). Verify:
- A's corpse has zero gravity.
- Tumbling motion (random spin).
- Despawns after 30 seconds on both clients.

- [ ] **Step 6: Test 6 — Generic corpse cleanup**

Host + A. Host kills A via Generic. Corpse falls and stays. Host runs `decompv2_cleanup_corpses`. Verify the corpse despawns on both clients.

- [ ] **Step 7: Test 7 — Decompression late-join physics**

Host runs `decompv2_kill_self Decompression` (after temporarily wiring decompv2_kill_self to Decompression for this test, or use `decompv2_vent_self`). After 2 seconds, client B joins. Verify B sees the corpse mid-flight, no double-impulse / "snap" — corpse motion is continuous.

- [ ] **Step 8: Test 8 — voice routing**

Two clients, both alive. Host kills client A. Verify (with mics):
- Host speaking: A does NOT hear.
- A speaking: Host does NOT hear.
- If a third client B is also dead, A and B can hear each other.

- [ ] **Step 9: Test 9 — last-living-player fallback**

Two clients. Host dies, then the other player dies. The first dead player's spectator currently follows the second; when the second dies, verify the camera falls back to map-overview (no living players to follow).

- [ ] **Step 10: Sign off or file bugs**

If all tests pass, commit a sign-off note:

```bash
git commit --allow-empty -m "test: group B player & death systems verified manually"
```

If any tests fail, file each failure as a concrete bug-fix task with reproduction steps and add to a follow-up plan.

---

## Self-review (writer's notes)

After writing the plan I checked it against the spec:

**Spec coverage:**
- §1 goals → Tasks 1–18 cover all in-scope items.
- §2 decisions → Each decision is reflected in a specific task (controller in T5, enum in T1, no HP because no `TakeDamage` ever defined, vacuum tumble in T9, generic persistence in T10/T12, spectator in T13/T14, dead chat in T17, no respawn — never implemented anywhere, camera in T5/T13/T14, hybrid death cam handled by T13's corpse-lock + T15's redout, cause-specific text in T15, cause-specific audio in T16, decomposed components throughout, host authority via `[Rpc.Host]` in T2 and reinforced in every subsequent task).
- §3 architecture → matches T2/T8/T13.
- §4 components → one task per component (T2 Player, T3 Corpse, T6 PlayerSpawner, T13/T14 Spectator, T15 DeathHud, T17 DeadChat, T12 CorpseCleanupSignal, T1 DeathCause).
- §5 networking → `[Sync]`/`[Rpc.Host]`/`[Rpc.Broadcast]` classifications applied throughout. `ImpulseApplied` guard in T9.
- §6 failure modes → idempotency (T2 Step 1), late-join guard (T9 Step 6), spectator target dies (T14), no-living fallback (T14, T19 Test 9), HUD non-local gate (T15), voice on `IsAlive` change (T17).
- §7 testing → console commands in T7/T9/T12/T18; manual matrix in T19.
- §8 public surface → `Kill` (T2/T8), `CorpseCleanupSignal.RaiseGenericCleanup` (T12), `Player.Died` (T2), `PlayerSpawner.RoundInProgress` (T6/T18). All present.

**Placeholder scan:** the only deliberate gap is `DeadChat.ApplyChannelRouting` in T17, which is documented as an implementation gap due to s&box API churn and accompanied by a concrete two-client smoke test that will catch any wrong implementation.

**Type consistency:** signatures of `Kill`, `Begin`, `Cleanup`, `RaiseGenericCleanup`, `SpawnAsLateJoiner` are consistent across all tasks. `IsAlive`, `Cause`, `SourcePosition`, `ImpulseApplied`, `OwnerConnectionId` field names match across Player and Corpse uses.

No issues found that need a re-pass.
