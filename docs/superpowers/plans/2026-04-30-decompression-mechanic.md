# Decompression Mechanic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the decompression sabotage mechanic for Decompression v2: saboteur holds Use 5s on a wall panel → 4s warning → hatch blows + perimeter doors slam + occupants die → 10s vacuum → blast door seals + doors reopen. Each section is one-shot, traversable again after seal.

**Architecture:** Section-centric state machine with reactive Hatch / SectionDoor / Panel followers. Server-authoritative state via `[Sync(SyncFlags.FromHost)]`. `[Rpc.Host]` for state-changing triggers, `[Rpc.Broadcast]` for cross-client events. Hack progress visibility synced via `(HackingConnectionId, HackStartTime)` so the panel glow is visible to all clients without per-frame RPC traffic. Builds on Group B's `Player.Kill(DeathCause.Decompression, hatchPosition)` API.

**Tech Stack:** s&box (C#, Sandbox.* APIs, scene/component system, multiplayer @ 50Hz), `[Sync(SyncFlags.FromHost)]` for state, `[Rpc.Host]` / `[Rpc.Broadcast]` for networking, BoxCollider triggers for occupancy, `Component.IPressable` (or current s&box equivalent) for hold-Use interaction.

**Spec:** `docs/superpowers/specs/2026-04-30-decompression-mechanic-design.md`

---

## Notes for the implementing engineer

1. **s&box API drift.** As with Group B, several APIs may have current names that differ from what's written here. Verify at compile time and use the current name with the same structural intent. Most likely candidates:
   - `Component.IPressable` — the current pressable interface. May be `IUse`, `IPressable`, `Sandbox.IPressable`, or pattern via `[Press]`-attributed methods. Use the canonical s&box approach for Use-button interaction.
   - `Time.Now` — current global time. Verify availability vs. `RealTime.Now` / `Game.RealTime`.
   - `OnTriggerEnter` / `OnTriggerExit` — Component callbacks for collider trigger events. Verify names; could be `OnTriggerEnter(GameObject other)` or `OnTriggerEnter(Collider other)`.
   - `Connection.Find(Guid)` — lookup connection by id. Could be `Connection.All.FirstOrDefault(c => c.Id == id)`.

2. **Where to test.** Same as Group B: launch from editor, use developer console for ConCmds, "Launch Second Instance" for multi-client.

3. **Builds.** Editor auto-compile or `dotnet build Code/decompressionv2.csproj`.

4. **Commits.** Each task ends with a commit. Group A continues on `main`.

---

## File structure

Files **created** in this plan:

| Path | Responsibility |
|---|---|
| `Code/Decompression/VentingState.cs` | The `VentingState` enum (`Idle`, `Warning`, `Venting`, `Sealed`) |
| `Code/Decompression/Section.cs` | Orchestrator: occupancy tracking + state machine + `RequestVent` API + `Vented` event |
| `Code/Decompression/Hatch.cs` | Visual-only: three pose states, reactive to parent Section |
| `Code/Decompression/SectionDoor.cs` | Visual-only perimeter door, slides between open/closed |
| `Code/Decompression/Panel.cs` | Hold-Use interaction; synced hack state for cross-client glow visibility |
| `Assets/prefabs/section_door.prefab` | Reusable door prefab |
| `Assets/prefabs/hatch.prefab` | Reusable hatch prefab |
| `Assets/prefabs/sabotage_panel.prefab` | Reusable panel prefab |

Files **modified**:

| Path | Change |
|---|---|
| `Code/Player/Player.cs` | Add `[Sync(SyncFlags.FromHost)] IsSaboteur` property + `SetSaboteur` Rpc.Host setter |
| `Code/Debug/DebugCommands.cs` | Add `decompv2_set_saboteur`, `decompv2_request_vent`, `decompv2_complete_hack`, `decompv2_section_state` |
| `Assets/scenes/minimal.scene` | Add a test Section with Hatch, 1-2 SectionDoors, and a Panel |

---

## Task 1: Add the VentingState enum

**Files:**
- Create: `Code/Decompression/VentingState.cs`

- [ ] **Step 1: Write the enum**

`Code/Decompression/VentingState.cs`:

```csharp
namespace Decompression;

public enum VentingState
{
	Idle,
	Warning,
	Venting,
	Sealed
}
```

- [ ] **Step 2: Verify compile**

Build via editor or `dotnet build Code/decompressionv2.csproj`. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/VentingState.cs
git commit -m "feat(decomp): add VentingState enum"
```

---

## Task 2: Section component skeleton

**Files:**
- Create: `Code/Decompression/Section.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Decompression/Section.cs`:

```csharp
using System;
using System.Collections.Generic;
using Sandbox;

namespace Decompression;

public sealed class Section : Component
{
	[Property] public string DisplayName { get; set; } = "";
	[Property] public Hatch Hatch { get; set; }
	[Property] public List<SectionDoor> Doors { get; set; } = new();
	[Property] public float WarningDuration { get; set; } = 4f;
	[Property] public float VacuumDuration { get; set; } = 10f;

	[Sync( SyncFlags.FromHost )] public VentingState State { get; private set; } = VentingState.Idle;
	[Sync( SyncFlags.FromHost )] public float StateEnteredAt { get; private set; }

	public IReadOnlyCollection<Player> Occupants => occupants;
	private readonly HashSet<Player> occupants = new();

	public static event Action<Section, IReadOnlyList<Player>> Vented;

	[Rpc.Host]
	public void RequestVent()
	{
		if ( State != VentingState.Idle ) return;
		EnterState( VentingState.Warning );
	}

	private void EnterState( VentingState next )
	{
		State = next;
		StateEnteredAt = Time.Now;
	}

	// Occupancy tracking, state-machine update, kill loop, and Vented broadcast
	// are added in later tasks.
}
```

This compiles even without `Hatch` and `SectionDoor` types — just leave the property types unresolved temporarily; the compiler will be happy once Tasks 3 and 4 land.

Wait — actually the compiler will fail on `Hatch` and `SectionDoor` references. Stub them with placeholder types in this task to allow compilation, or order the tasks so the dependent types exist first.

**Better approach: comment out the `Hatch` and `Doors` properties until Tasks 3 and 4 land.** Re-enable in Task 5.

- [ ] **Step 2: Comment out type-dependent properties for now**

In the file above, comment out lines:
```csharp
//[Property] public Hatch Hatch { get; set; }
//[Property] public List<SectionDoor> Doors { get; set; } = new();
```

The full version comes back in Task 5.

- [ ] **Step 3: Verify compile**

Build. Expected: green.

- [ ] **Step 4: Commit**

```bash
git add Code/Decompression/Section.cs
git commit -m "feat(decomp): Section component skeleton with state machine"
```

---

## Task 3: Hatch component skeleton

**Files:**
- Create: `Code/Decompression/Hatch.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Decompression/Hatch.cs`:

```csharp
using Sandbox;

namespace Decompression;

public sealed class Hatch : Component
{
	[Property] public Section Section { get; set; }
	[Property] public GameObject ClosedVisual { get; set; }
	[Property] public GameObject OpenBreachVisual { get; set; }
	[Property] public GameObject BlastDoorVisual { get; set; }

	// Visual swapping logic added in Task 11.
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Hatch.cs
git commit -m "feat(decomp): Hatch component skeleton"
```

---

## Task 4: SectionDoor component skeleton

**Files:**
- Create: `Code/Decompression/SectionDoor.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Decompression/SectionDoor.cs`:

```csharp
using Sandbox;

namespace Decompression;

public sealed class SectionDoor : Component
{
	[Property] public Section Section { get; set; }
	[Property] public GameObject DoorMesh { get; set; }
	[Property] public Vector3 OpenLocalOffset { get; set; } = Vector3.Up * 100f;

	// Open/close lerp logic added in Task 12.
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/SectionDoor.cs
git commit -m "feat(decomp): SectionDoor component skeleton"
```

---

## Task 5: Re-enable Hatch and Doors properties on Section

**Files:**
- Modify: `Code/Decompression/Section.cs`

- [ ] **Step 1: Restore the commented properties**

In `Code/Decompression/Section.cs`, uncomment the two property lines:

```csharp
[Property] public Hatch Hatch { get; set; }
[Property] public List<SectionDoor> Doors { get; set; } = new();
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Section.cs
git commit -m "feat(decomp): wire Section.Hatch and Section.Doors property types"
```

---

## Task 6: Panel component skeleton with synced hack state

**Files:**
- Create: `Code/Decompression/Panel.cs`

- [ ] **Step 1: Write the skeleton**

`Code/Decompression/Panel.cs`:

```csharp
using System;
using Sandbox;

namespace Decompression;

public sealed class Panel : Component
{
	[Property] public Section TargetSection { get; set; }
	[Property] public ModelRenderer GlowRenderer { get; set; }
	[Property] public float HoldDuration { get; set; } = 5f;

	[Sync( SyncFlags.FromHost )] public Guid HackingConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public float HackStartTime { get; set; }

	// Behavior added in Tasks 13–15:
	//   - Press/Release handlers (IPressable)
	//   - BeginHack / EndHack [Rpc.Host] methods
	//   - Host-side timer + IsSaboteur check
	//   - Glow rendering on every client
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Panel.cs
git commit -m "feat(decomp): Panel component skeleton with synced hack state"
```

---

## Task 7: Player.IsSaboteur addition

**Files:**
- Modify: `Code/Player/Player.cs`

- [ ] **Step 1: Add the IsSaboteur property and setter**

In `Code/Player/Player.cs`, add these inside the `Player` class, near the other `[Sync]` properties:

```csharp
[Sync( SyncFlags.FromHost )] public bool IsSaboteur { get; private set; }

[Rpc.Host]
public void SetSaboteur( bool value )
{
	IsSaboteur = value;
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Player/Player.cs
git commit -m "feat(player): add IsSaboteur [Sync] flag and SetSaboteur Rpc"
```

---

## Task 8: decompv2_set_saboteur ConCmd

**Files:**
- Modify: `Code/Debug/DebugCommands.cs`

- [ ] **Step 1: Write the failing test (compile-failing call)**

This task uses TDD-by-compile: we add the ConCmd that exercises the new `SetSaboteur` API. Compile already succeeds because Task 7 added it.

In `Code/Debug/DebugCommands.cs`, add inside the `DebugCommands` class:

```csharp
[ConCmd( "decompv2_set_saboteur" )]
public static void SetSaboteur( string connectionDisplayName, bool value )
{
	if ( !Networking.IsHost )
	{
		Log.Warning( "decompv2_set_saboteur: host only" );
		return;
	}

	var target = Game.ActiveScene.GetAllComponents<Player>()
		.FirstOrDefault( p => p.Network.Owner?.DisplayName == connectionDisplayName );

	if ( target is null )
	{
		Log.Warning( $"decompv2_set_saboteur: no player named '{connectionDisplayName}'" );
		return;
	}

	target.SetSaboteur( value );
	Log.Info( $"{connectionDisplayName}.IsSaboteur = {value}" );
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Smoke-run**

Launch the game with two clients. From host console:
```
decompv2_set_saboteur Antho true
```
Expected log: `Antho.IsSaboteur = true`. Run again with `false` to flip back.

- [ ] **Step 4: Commit**

```bash
git add Code/Debug/DebugCommands.cs
git commit -m "feat(debug): add decompv2_set_saboteur ConCmd"
```

---

## Task 9: Section occupancy tracking via collider triggers

**Files:**
- Modify: `Code/Decompression/Section.cs`

- [ ] **Step 1: Add OnTriggerEnter/OnTriggerExit handlers**

In `Code/Decompression/Section.cs`, add inside the class:

```csharp
protected override void OnEnabled()
{
	occupants.Clear();
}

protected override void OnTriggerEnter( Collider other )
{
	if ( !Networking.IsHost ) return;

	var player = other.GameObject.Components.Get<Player>( includeDisabled: true )
		?? other.GameObject.Root.Components.Get<Player>( includeDisabled: true );
	if ( player is null ) return;

	occupants.Add( player );
}

protected override void OnTriggerExit( Collider other )
{
	if ( !Networking.IsHost ) return;

	var player = other.GameObject.Components.Get<Player>( includeDisabled: true )
		?? other.GameObject.Root.Components.Get<Player>( includeDisabled: true );
	if ( player is null ) return;

	occupants.Remove( player );
}
```

The component looks for a `Player` on the collided object or its root, since the player's collider is on a child `Colliders` GameObject.

If `OnTriggerEnter`/`OnTriggerExit` aren't the correct callback names in your s&box revision, use the current equivalents (might be `OnCollisionStart` or via `IInteractable` callbacks). The intent is: when a player enters the BoxCollider trigger volume, add them to `occupants`; when they exit, remove them. Host-only.

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Section.cs
git commit -m "feat(decomp): Section occupancy via OnTriggerEnter/Exit"
```

---

## Task 10: Section state-machine OnUpdate transitions (Warning → Venting → Sealed)

**Files:**
- Modify: `Code/Decompression/Section.cs`

- [ ] **Step 1: Add OnUpdate state machine**

In `Section.cs`, add inside the class:

```csharp
protected override void OnUpdate()
{
	if ( !Networking.IsHost ) return;

	var elapsed = Time.Now - StateEnteredAt;

	switch ( State )
	{
		case VentingState.Warning:
			if ( elapsed >= WarningDuration )
				EnterState( VentingState.Venting );
			break;

		case VentingState.Venting:
			if ( elapsed >= VacuumDuration )
				EnterState( VentingState.Sealed );
			break;

		// Idle and Sealed are stationary — no transition out.
	}
}
```

The kill loop on `Warning → Venting` is added in Task 12. For now this just transitions the state machine on time.

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Section.cs
git commit -m "feat(decomp): Section state-machine OnUpdate transitions"
```

---

## Task 11: decompv2_request_vent ConCmd

**Files:**
- Modify: `Code/Debug/DebugCommands.cs`

- [ ] **Step 1: Add the ConCmd**

In `Code/Debug/DebugCommands.cs`, add inside `DebugCommands`:

```csharp
[ConCmd( "decompv2_request_vent" )]
public static void RequestVent( string sectionDisplayName )
{
	if ( !Networking.IsHost )
	{
		Log.Warning( "decompv2_request_vent: host only" );
		return;
	}

	var section = Game.ActiveScene.GetAllComponents<Section>()
		.FirstOrDefault( s => s.DisplayName == sectionDisplayName );

	if ( section is null )
	{
		Log.Warning( $"decompv2_request_vent: no section named '{sectionDisplayName}'" );
		return;
	}

	section.RequestVent();
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Debug/DebugCommands.cs
git commit -m "feat(debug): add decompv2_request_vent ConCmd"
```

---

## Task 12: Section kill loop + Vented broadcast on Warning → Venting

**Files:**
- Modify: `Code/Decompression/Section.cs`

- [ ] **Step 1: Add kill-on-vent and broadcast**

In `Section.cs`, modify `EnterState` to handle the Warning → Venting transition specifically:

```csharp
private void EnterState( VentingState next )
{
	var prev = State;
	State = next;
	StateEnteredAt = Time.Now;

	if ( prev == VentingState.Warning && next == VentingState.Venting )
	{
		OnEnterVenting();
	}
}

private void OnEnterVenting()
{
	if ( !Networking.IsHost ) return;
	if ( Hatch is null )
	{
		Log.Warning( $"Section '{DisplayName}': cannot vent — Hatch not wired." );
		return;
	}

	var killedSnapshot = new List<Player>( occupants );
	var hatchPos = Hatch.WorldPosition;

	foreach ( var player in killedSnapshot )
	{
		if ( !player.IsValid() ) continue;
		player.Kill( DeathCause.Decompression, hatchPos );
	}

	var killedIds = killedSnapshot
		.Where( p => p.IsValid() )
		.Select( p => p.OwnerConnectionId )
		.ToArray();

	BroadcastVented( killedIds );
}

[Rpc.Broadcast]
private void BroadcastVented( Guid[] killedConnectionIds )
{
	var killed = new List<Player>();
	if ( Game.ActiveScene is not null )
	{
		foreach ( var id in killedConnectionIds )
		{
			var p = Game.ActiveScene.GetAllComponents<Player>()
				.FirstOrDefault( pl => pl.OwnerConnectionId == id );
			if ( p is not null ) killed.Add( p );
		}
	}
	Vented?.Invoke( this, killed );
}
```

Also add `using System.Linq;` to the top of the file if not already present.

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Smoke-run**

Launch the game (single instance for now — proper test scaffold lands in Task 17). From host console:
```
decompv2_request_vent test_section
```
This will fail with "no section named 'test_section'" because we haven't added one to the scene yet — that's expected. The state machine has nothing to act on without a Section in the scene; deeper testing is gated on Task 17.

- [ ] **Step 4: Commit**

```bash
git add Code/Decompression/Section.cs
git commit -m "feat(decomp): kill occupants on Warning -> Venting + Vented event"
```

---

## Task 13: Hatch reactive visual swapping

**Files:**
- Modify: `Code/Decompression/Hatch.cs`

- [ ] **Step 1: Add OnUpdate visual logic**

`Code/Decompression/Hatch.cs` — add to the class:

```csharp
protected override void OnUpdate()
{
	if ( Section is null ) return;

	var pose = Section.State switch
	{
		VentingState.Idle => HatchPose.Closed,
		VentingState.Warning => HatchPose.Closed,
		VentingState.Venting => HatchPose.OpenBreach,
		VentingState.Sealed => HatchPose.BlastDoorSealed,
		_ => HatchPose.Closed,
	};

	SetPose( pose );
}

private HatchPose currentPose = HatchPose.Closed;

private void SetPose( HatchPose pose )
{
	if ( currentPose == pose ) return;
	currentPose = pose;

	if ( ClosedVisual is not null ) ClosedVisual.Enabled = (pose == HatchPose.Closed);
	if ( OpenBreachVisual is not null ) OpenBreachVisual.Enabled = (pose == HatchPose.OpenBreach);
	if ( BlastDoorVisual is not null ) BlastDoorVisual.Enabled = (pose == HatchPose.BlastDoorSealed);
}

private enum HatchPose { Closed, OpenBreach, BlastDoorSealed }
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Hatch.cs
git commit -m "feat(decomp): Hatch reactive pose swap based on Section.State"
```

---

## Task 14: SectionDoor open/close lerp

**Files:**
- Modify: `Code/Decompression/SectionDoor.cs`

- [ ] **Step 1: Add OnUpdate lerp logic**

`Code/Decompression/SectionDoor.cs` — add to the class:

```csharp
private const float LerpSpeed = 1f / 0.4f; // ~0.4s open/close

protected override void OnUpdate()
{
	if ( DoorMesh is null || Section is null ) return;

	// Closed only during Venting — open in all other states (including Sealed
	// so the section is traversable again after the blast door seals).
	var shouldBeClosed = Section.State == VentingState.Venting;
	var targetOffset = shouldBeClosed ? Vector3.Zero : OpenLocalOffset;

	var current = DoorMesh.LocalPosition;
	DoorMesh.LocalPosition = Vector3.Lerp(
		current,
		targetOffset,
		Time.Delta * LerpSpeed
	);
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/SectionDoor.cs
git commit -m "feat(decomp): SectionDoor open/close lerp on Section.State"
```

---

## Task 15: Panel pressable behavior + BeginHack/EndHack RPCs

**Files:**
- Modify: `Code/Decompression/Panel.cs`

- [ ] **Step 1: Implement IPressable + RPCs**

Replace the contents of `Code/Decompression/Panel.cs` with the full version:

```csharp
using System;
using Sandbox;

namespace Decompression;

public sealed class Panel : Component, Component.IPressable
{
	[Property] public Section TargetSection { get; set; }
	[Property] public ModelRenderer GlowRenderer { get; set; }
	[Property] public float HoldDuration { get; set; } = 5f;

	[Sync( SyncFlags.FromHost )] public Guid HackingConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public float HackStartTime { get; set; }

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		// Find the Player that pressed this. e.Source is typically the
		// PlayerController or a related component; resolve to the Player
		// component on the same GameObject hierarchy.
		var player = e.Source?.GameObject.Components.Get<Player>( includeDisabled: true )
			?? e.Source?.GameObject.Root.Components.Get<Player>( includeDisabled: true );

		if ( player is null ) return false;
		if ( !player.IsSaboteur ) return false;

		BeginHack();
		return true;
	}

	void Component.IPressable.Release( Component.IPressable.Event e )
	{
		EndHack();
	}

	[Rpc.Host]
	public void BeginHack()
	{
		if ( HackingConnectionId != Guid.Empty ) return;
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		HackingConnectionId = caller.Id;
		HackStartTime = Time.Now;
	}

	[Rpc.Host]
	public void EndHack()
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		if ( HackingConnectionId != caller.Id ) return;

		HackingConnectionId = Guid.Empty;
	}
}
```

If the s&box pressable interface in your version is named differently (`IUse`, plain `[Press]`-attributed methods, etc.), swap the interface and method names. The intent is: on Use-button press, the panel calls `BeginHack`; on release, `EndHack`. Both RPCs run on the host.

If `Rpc.Caller` isn't the current way to identify the calling connection inside an `[Rpc.Host]` body, substitute the current API (could be `Rpc.CallerConnection`, `Rpc.Caller`, or a `Connection` parameter). The intent is to know which connection initiated the RPC.

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Panel.cs
git commit -m "feat(decomp): Panel IPressable handlers + BeginHack/EndHack Rpcs"
```

---

## Task 16: Panel host-side timer + saboteur-validity check + glow render

**Files:**
- Modify: `Code/Decompression/Panel.cs`

- [ ] **Step 1: Add OnUpdate logic**

In `Code/Decompression/Panel.cs`, add inside the class:

```csharp
protected override void OnUpdate()
{
	UpdateGlow();

	if ( !Networking.IsHost ) return;

	if ( HackingConnectionId == Guid.Empty ) return;

	// Defense in depth: if the hacker is no longer a saboteur or no longer
	// connected, drop the hack.
	var hackerConnection = Connection.Find( HackingConnectionId );
	if ( hackerConnection is null )
	{
		HackingConnectionId = Guid.Empty;
		return;
	}

	var hackerPlayer = Game.ActiveScene?
		.GetAllComponents<Player>()
		.FirstOrDefault( p => p.Network.Owner?.Id == HackingConnectionId );
	if ( hackerPlayer is null || !hackerPlayer.IsSaboteur )
	{
		HackingConnectionId = Guid.Empty;
		return;
	}

	// If the target section is no longer Idle (someone else got there first,
	// or it's already sealed), drop the hack.
	if ( TargetSection is not null && TargetSection.State != VentingState.Idle )
	{
		HackingConnectionId = Guid.Empty;
		return;
	}

	// Hack complete — trigger the vent.
	if ( Time.Now - HackStartTime >= HoldDuration )
	{
		HackingConnectionId = Guid.Empty;
		TargetSection?.RequestVent();
	}
}

private void UpdateGlow()
{
	if ( GlowRenderer is null ) return;

	float progress = 0f;
	if ( HackingConnectionId != Guid.Empty )
	{
		progress = Math.Clamp( (Time.Now - HackStartTime) / HoldDuration, 0f, 1f );
	}

	GlowRenderer.Tint = new Color( 1f, 0f, 0f, progress );
}
```

`Connection.Find(Guid)` may need to be replaced with a manual search through `Connection.All` or similar — verify.

`GlowRenderer.Tint` with alpha-controlled visibility assumes the renderer's material respects alpha for emissive. If your placeholder material doesn't, simply toggle `GlowRenderer.Enabled = (progress > 0f)` instead, and accept binary visibility for now.

- [ ] **Step 2: Verify compile**

Build. Expected: green. (May need `using System;` for Math, `using System.Linq;` for FirstOrDefault.)

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Panel.cs
git commit -m "feat(decomp): Panel host timer + saboteur validity + glow render"
```

---

## Task 17: Test scaffold — extend minimal.scene with one wired Section

This task is **editor work only** — no code changes. The implementing engineer (or user, if working hybrid) extends `minimal.scene` to give us something to test against.

**Files:**
- Modify: `Assets/scenes/minimal.scene`

- [ ] **Step 1: Add the Section GameObject**

In the s&box editor:
1. Open `Assets/scenes/minimal.scene`.
2. Right-click the scene root → **New GameObject**, name it `TestSection`.
3. Add component `Decompression.Section`. Set `DisplayName = "test_section"`.
4. Add a child GameObject named `Volume` to `TestSection`. On it, add a `BoxCollider` component, mark it `IsTrigger = true`, scale it to enclose ~half the flatgrass map (e.g., scale `1000 x 1000 x 200`, position above the floor).

- [ ] **Step 2: Add the Hatch GameObject**

1. Right-click `TestSection` → **New GameObject** named `Hatch`. Position it at one corner of the section's volume (a wall position).
2. Add component `Decompression.Hatch`.
3. Drag `TestSection` into the Hatch component's `Section` field.
4. Add three child GameObjects to `Hatch`:
   - `ClosedVisual` — add a `ModelRenderer`, model = any simple cube (`models/dev/box.vmdl` or similar). Tint it gray. Set Enabled = true.
   - `OpenBreachVisual` — same setup but tint it dark red. Set Enabled = false.
   - `BlastDoorVisual` — same setup but tint it metallic / lighter gray. Set Enabled = false.
5. On the Hatch component, drag each child into its corresponding `[Property]` slot (`ClosedVisual`, `OpenBreachVisual`, `BlastDoorVisual`).

- [ ] **Step 3: Add a SectionDoor GameObject**

1. Right-click `TestSection` → **New GameObject** named `Door1`. Position it at the boundary of the section volume (where the section meets the rest of the map).
2. Add component `Decompression.SectionDoor`.
3. Drag `TestSection` into its `Section` field.
4. Add a child GameObject `DoorMesh` with a `ModelRenderer` (a tall vertical cube — a wall-shaped door). Set Tint to dark gray.
5. Drag `DoorMesh` into the `DoorMesh` slot on the SectionDoor component. Leave `OpenLocalOffset` at the default `(0, 0, 100)` (door slides up when open).

- [ ] **Step 4: Wire the door into the Section**

1. Select `TestSection`. On the Section component, in the `Doors` list field, click `+` to add an entry, drag `Door1` into it.

- [ ] **Step 5: Add a sabotage Panel GameObject**

1. Right-click the scene root → **New GameObject** named `TestPanel`. Position it somewhere visible — could be inside or outside the Section's volume (level-design choice for testing).
2. Add component `Decompression.Panel`.
3. Drag `TestSection` into its `TargetSection` field.
4. Add a child GameObject `PanelMesh` with a `ModelRenderer` (a small cube). Tint it dark.
5. Add another child `Glow` with a `ModelRenderer` (a slightly larger cube wrapping `PanelMesh`). Material can be a simple emissive red, or just any default material — the Tint will be set programmatically.
6. Drag the `Glow`'s ModelRenderer into the Panel component's `GlowRenderer` slot.
7. Make sure the panel has some kind of collider so `IPressable` can detect Use-button traces against it. Add a `BoxCollider` to `TestPanel` if needed.

- [ ] **Step 6: Wire the Hatch into the Section**

1. Select `TestSection`. On the Section component, drag `Hatch` into its `Hatch` field.

- [ ] **Step 7: Save the scene**

Save (Ctrl+S, **not** "Save As").

- [ ] **Step 8: Smoke run**

Launch the game (single instance). Walk into and out of the `TestSection` volume to verify occupancy tracking (no immediate visible output — but next task verifies via console).

From host console:
```
decompv2_request_vent test_section
```
Expected: 4s of nothing visible (warning state has no jitter animation yet — that's polish), then visual swap on the Hatch (closed → open-breach), the Door1 mesh slides down to closed (or stays at its position, depending on lerp config), and 10s later the Hatch swaps to BlastDoor and the door slides back up.

If you're inside the section when you fire the vent, you should die when it transitions to Venting (corpse drifts toward the Hatch's position).

- [ ] **Step 9: Commit**

```bash
git add Assets/scenes/minimal.scene
git commit -m "test(decomp): scaffold a TestSection with hatch, door, panel for manual testing"
```

---

## Task 18: decompv2_complete_hack and decompv2_section_state ConCmds

**Files:**
- Modify: `Code/Debug/DebugCommands.cs`

- [ ] **Step 1: Add the ConCmds**

In `Code/Debug/DebugCommands.cs`, add inside `DebugCommands`:

```csharp
[ConCmd( "decompv2_complete_hack" )]
public static void CompleteHack()
{
	if ( !Networking.IsHost )
	{
		Log.Warning( "decompv2_complete_hack: host only (host is the authority on Panel state)" );
		return;
	}

	// Find a Panel currently being hacked by anyone — there should be at most
	// one in dev. Force-complete it by jumping HackStartTime backward.
	var panel = Game.ActiveScene?
		.GetAllComponents<Panel>()
		.FirstOrDefault( p => p.HackingConnectionId != Guid.Empty );

	if ( panel is null )
	{
		Log.Warning( "decompv2_complete_hack: no panel is being hacked" );
		return;
	}

	panel.HackStartTime = Time.Now - panel.HoldDuration - 0.1f;
}

[ConCmd( "decompv2_section_state" )]
public static void SectionState( string sectionDisplayName )
{
	var section = Game.ActiveScene?
		.GetAllComponents<Section>()
		.FirstOrDefault( s => s.DisplayName == sectionDisplayName );

	if ( section is null )
	{
		Log.Warning( $"decompv2_section_state: no section named '{sectionDisplayName}'" );
		return;
	}

	Log.Info( $"Section '{sectionDisplayName}': State={section.State}, " +
		$"StateEnteredAt={section.StateEnteredAt:F1}, Now={Time.Now:F1}, " +
		$"Occupants={section.Occupants.Count}" );
}
```

`Panel.HackStartTime` is the synced setter — modifying it on the host will trigger sync and the Panel's `OnUpdate` will detect "hold complete" on its next frame.

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Smoke-run**

Launch the game. Set yourself as saboteur:
```
decompv2_set_saboteur Antho true
```

Hold Use on the test panel briefly (just a tap). Then quickly run:
```
decompv2_complete_hack
```
Expected: vent triggers immediately on `test_section`. (You can also just hold Use for 5s — same outcome.)

```
decompv2_section_state test_section
```
Expected log line showing current State, time, occupants.

- [ ] **Step 4: Commit**

```bash
git add Code/Debug/DebugCommands.cs
git commit -m "feat(debug): decompv2_complete_hack + decompv2_section_state ConCmds"
```

---

## Task 19: Final manual integration test pass

This task does no new implementation. It runs the full manual matrix from §8 of the spec.

**Files:** none

- [ ] **Step 1: Test 1 — Solo vent**

Host alone. `decompv2_request_vent test_section`. Verify:
- 4s warning (visual changes on the hatch may be subtle — primarily the time delay)
- Host dies; corpse drifts toward the hatch
- 10s later, blast door visual appears, door reopens

- [ ] **Step 2: Test 2 — Bystander vent**

Two clients. Host inside section, A outside. Vent. Host dies; A unaffected; A sees the visual sequence on the hatch + door.

- [ ] **Step 3: Test 3 — Escape vent**

`decompv2_request_vent` while host is inside, but host walks out before the 4s warning ends. Host should NOT die.

- [ ] **Step 4: Test 4 — Mid-warning entry**

Vent on empty section. Host walks IN during the warning. Host dies.

- [ ] **Step 5: Test 5 — Saboteur self-kill**

`decompv2_set_saboteur Antho true`. Hack the panel for the section host is standing in. Host dies in own vent.

- [ ] **Step 6: Test 6 — Crew can't hack**

Host = crew (default). Hold Use on panel. No glow, no progress.

- [ ] **Step 7: Test 7 — Saboteur full hack**

Host = saboteur. Hold Use 5s. Glow ramps up over 5s. At completion, vent triggers.

- [ ] **Step 8: Test 8 — Hack interrupt**

Hold 3s, release. Glow disappears. Re-hold full 5s. Vent triggers.

- [ ] **Step 9: Test 9 — Multi-client glow visibility**

Host = saboteur. A = crew. Host holds Use on panel. **A sees the panel glowing** ramping over 5s.

- [ ] **Step 10: Test 10 — Two saboteurs same section**

Skip if you only have one panel — needs two panels both targeting the same section. If you've got time, add a second panel to the scene and verify first-completion-wins.

- [ ] **Step 11: Test 11 — Late join during vent**

Host vents. While in Venting state, third client B joins. B should see hatch open + doors closed. After Sealed, B sees blast door + doors reopen.

- [ ] **Step 12: Test 12 — Section traversable after seal**

After a section is sealed, walk into it. Doors should be open, no kill, no audio. Visible blast door cosmetic on the hatch.

- [ ] **Step 13: Test 13 — One-shot enforcement**

On a sealed section, `decompv2_request_vent test_section` again. No effect (early-out).

- [ ] **Step 14: Sign off or file bugs**

If all pass:
```bash
git commit --allow-empty -m "test: group A decompression mechanic verified manually"
```

Otherwise, file each failure as a follow-up task with reproduction steps.

---

## Self-review (writer's notes)

After writing the plan I checked it against the spec:

**Spec coverage:**
- §1 in-scope items → all covered. Section (Tasks 2, 5, 9, 10, 12), Hatch (Tasks 3, 13), SectionDoor (Tasks 4, 14), Panel (Tasks 6, 15, 16), `Player.IsSaboteur` (Task 7), `Section.Vented` event (Task 12), test scaffold (Task 17), debug ConCmds (Tasks 8, 11, 18).
- §2 decisions → reflected throughout: 1:1 hatch-section in spec, panels-via-IsSaboteur in Task 15, host-authoritative sync in skeletons, saboteur-not-immune by virtue of just being an occupant.
- §3 architecture → matches Task 2's Section as the orchestrator with reactive followers.
- §4 components → one or more tasks per component.
- §5 lifecycle state machine → Task 10 implements the OnUpdate transitions; Task 12 handles the Warning → Venting kill loop.
- §6 networking → all `[Sync(SyncFlags.FromHost)]` and `[Rpc.Host]` / `[Rpc.Broadcast]` patterns specified, defense-in-depth saboteur check in Task 16.
- §7 failure modes → addressed in code (idempotency in `RequestVent`, null Hatch warning in Task 12, disconnect/role-loss check in Task 16, second-saboteur drop in Task 16's Section.State check).
- §8 testing → Tasks 17–19 cover scaffold, ConCmds, and the manual matrix.
- §9 public surface → exposed at the right places: `Player.SetSaboteur` (Task 7), `Section.Vented` (Task 12), `Section.RequestVent` (Task 2 with body filled in throughout).

**Placeholder scan:** No "TBD", "TODO", or "implement later" in any code step. Two notes acknowledge s&box API drift (pressable interface name in Task 15, `Connection.Find` in Task 16, `OnTriggerEnter`/`Exit` in Task 9) — these are honest implementation-time verifications, not placeholders.

**Type consistency:** `VentingState` enum values match across Section, Hatch, SectionDoor. `Section.State`, `Section.StateEnteredAt`, `Panel.HackingConnectionId`, `Panel.HackStartTime` consistently named. Method signatures `Section.RequestVent`, `Panel.BeginHack`, `Panel.EndHack`, `Player.SetSaboteur` consistent throughout.

No issues found that need a re-pass.
