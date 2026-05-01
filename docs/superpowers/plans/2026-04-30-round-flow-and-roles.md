# Round Flow & Roles Implementation Plan (C1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement C1: the round lifecycle (Lobby → Round → RoundEnd → Lobby state machine), role assignment, all four saboteur-side win conditions, the role reveal flash + persistent badge + saboteur-sees-saboteur markers, the round timer + lobby countdown HUDs, and the round-end results screen. After this, the project is a playable horror game (no tasks or voting yet — those come in C2/C3).

**Architecture:** Single `Match` component orchestrates the state machine; six small Razor panel components are reactive HUD followers reading `Match.State` and `Player.IsSaboteur`. `[Sync(SyncFlags.FromHost)]` for persistent state, `[Rpc.Host]` for `StartRound`/`EndRound` triggers, `[Rpc.Broadcast]` for cross-client events that include the saboteur connection IDs (race-free role reveal). Builds on Group A's `Player.IsSaboteur`/`SetSaboteur` and `Section.Reset` (small Group A addition this plan adds), plus Group B's `Player.IsAlive`, corpse cleanup, and `PlayerSpawner.RoundInProgress` hooks.

**Tech Stack:** s&box (C# / Sandbox.* APIs, scene/component system, multiplayer @ 50Hz). Razor `PanelComponent` for HUD. `Component.IPressable` (existing from Group A) is unused here.

**Spec:** `docs/superpowers/specs/2026-04-30-round-flow-and-roles-design.md`

---

## Notes for the implementing engineer

1. **s&box API drift.** Same caveats as A and B. Verify at compile time: `Component.IPressable` (not used here but already established), `[Sync(SyncFlags.FromHost)]`, `[Rpc.Host]`, `[Rpc.Broadcast]`, `Time.Now`, `Connection.Local`, `Game.ActiveScene.GetAllComponents<T>()`, `Network.Owner`, `Networking.IsHost`. If anything's renamed, use the current name with the same intent.

2. **Where to test.** Same as before: launch from editor, console for ConCmds, "Launch Second Instance" for multi-client.

3. **Builds.** Editor auto-compile or `dotnet build Code/decompressionv2.csproj`.

4. **Commits.** Each task ends with a commit on `main`.

---

## File structure

Files **created** in this plan:

| Path | Responsibility |
|---|---|
| `Code/Match/MatchState.cs` | `MatchState` and `MatchOutcome` enums |
| `Code/Match/RoleAssigner.cs` | Static helpers for saboteur count + assignment |
| `Code/Match/Match.cs` | The orchestrator — state machine, win checks, public API |
| `Code/Match/RoleRevealOverlay.razor` + `.scss` | 3s flash on round start |
| `Code/Match/RoleHud.razor` + `.scss` | Persistent corner role badge |
| `Code/Match/RoundTimerHud.razor` + `.scss` | Top-center round countdown |
| `Code/Match/LobbyCountdownHud.razor` + `.scss` | Top-center lobby auto-start countdown |
| `Code/Match/RoundEndOverlay.razor` + `.scss` | Full-screen results banner |
| `Code/Match/SaboteurNametagOverlay.razor` + `.scss` | Red markers over other saboteurs (saboteur-only) |

Files **modified**:

| Path | Change |
|---|---|
| `Code/Decompression/Section.cs` | Add `[Rpc.Host] Reset()` method (small Group A modification) |
| `Code/Debug/DebugCommands.cs` | Add `decompv2_start_round`, `decompv2_end_round`, `decompv2_match_state` |
| `Assets/scenes/minimal.scene` | Add a `Match` GameObject with the Match component, `PlayerSpawner` ref wired |
| `Assets/prefabs/player.prefab` | Add the six new Razor panel components as children of the existing `Hud` GameObject |

---

## Task 1: MatchState and MatchOutcome enums

**Files:**
- Create: `Code/Match/MatchState.cs`

- [ ] **Step 1: Write the enums**

`Code/Match/MatchState.cs`:

```csharp
namespace Decompression;

public enum MatchState
{
	Lobby,
	Round,
	RoundEnd
}

public enum MatchOutcome
{
	None,
	Crew,
	Saboteur
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/MatchState.cs
git commit -m "feat(match): add MatchState and MatchOutcome enums"
```

---

## Task 2: Section.Reset() (Group A modification)

**Files:**
- Modify: `Code/Decompression/Section.cs`

- [ ] **Step 1: Add the Reset method**

In `Code/Decompression/Section.cs`, add inside the class (placement near `RequestVent` is natural):

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

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Decompression/Section.cs
git commit -m "feat(decomp): add Section.Reset() Rpc.Host method for C1"
```

---

## Task 3: RoleAssigner static helper

**Files:**
- Create: `Code/Match/RoleAssigner.cs`

- [ ] **Step 1: Write the helper**

`Code/Match/RoleAssigner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Decompression;

public static class RoleAssigner
{
	// Auto-scale: 1 saboteur for 3-7 players, 2 for 8+. Override (positive)
	// wins and is clamped to player count.
	public static int ResolveSaboteurCount( int playerCount, int @override )
	{
		if ( @override > 0 )
		{
			return Math.Min( @override, Math.Max( 0, playerCount - 1 ) );
		}

		if ( playerCount <= 2 ) return 0;
		if ( playerCount <= 7 ) return 1;
		return 2;
	}

	// Picks `count` random players from the input, calls SetSaboteur(true) on
	// each. Returns the connection IDs of the assigned saboteurs so the caller
	// can include them in a [Rpc.Broadcast] payload (race-free role reveal).
	// Pre-condition: caller is host.
	public static Guid[] Assign( IEnumerable<Player> alivePlayers, int count )
	{
		var pool = alivePlayers.Where( p => p.IsValid() ).ToList();
		if ( pool.Count == 0 || count <= 0 )
		{
			Log.Warning( $"RoleAssigner.Assign: no eligible players or count<=0 (pool={pool.Count}, count={count})" );
			return Array.Empty<Guid>();
		}

		count = Math.Min( count, pool.Count );

		// Shuffle by Fisher-Yates with Game.Random.
		for ( int i = pool.Count - 1; i > 0; i-- )
		{
			int j = Game.Random.Int( 0, i );
			(pool[i], pool[j]) = (pool[j], pool[i]);
		}

		var picked = pool.Take( count ).ToList();
		foreach ( var p in picked )
		{
			p.SetSaboteur( true );
		}

		return picked.Select( p => p.OwnerConnectionId ).ToArray();
	}

	public static void ClearAll( IEnumerable<Player> allPlayers )
	{
		foreach ( var p in allPlayers )
		{
			if ( p.IsValid() ) p.SetSaboteur( false );
		}
	}
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/RoleAssigner.cs
git commit -m "feat(match): RoleAssigner static helper for saboteur count + assignment"
```

---

## Task 4: Match component skeleton

**Files:**
- Create: `Code/Match/Match.cs`

- [ ] **Step 1: Write the skeleton with public API stubs**

`Code/Match/Match.cs`:

```csharp
using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Match : Component
{
	[Property] public PlayerSpawner PlayerSpawner { get; set; }

	[Property] public float LobbyAutoStartSeconds { get; set; } = 30f;
	[Property] public float RoundDuration { get; set; } = 480f;     // 8 minutes
	[Property] public float RoundEndDuration { get; set; } = 10f;
	[Property] public int MinPlayersToStart { get; set; } = 3;       // 4 recommended
	[Property] public int SaboteurCountOverride { get; set; } = 0;  // 0 = auto-scale

	[Sync( SyncFlags.FromHost )] public MatchState State { get; private set; } = MatchState.Lobby;
	[Sync( SyncFlags.FromHost )] public float StateEnteredAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public MatchOutcome LastOutcome { get; private set; }
	[Sync( SyncFlags.FromHost )] public string LastOutcomeReason { get; private set; } = "";

	public static event Action<Match, bool /* localIsSaboteur */> RoundStarted;
	public static event Action<Match, MatchOutcome, string> RoundEnded;

	// Convenience accessor for any subsystem to find the match in the scene.
	public static Match Current => Game.ActiveScene?.GetAllComponents<Match>().FirstOrDefault();

	[Rpc.Host]
	public void StartRound()
	{
		if ( State != MatchState.Lobby ) return;
		EnterRound();
	}

	[Rpc.Host]
	public void EndRound( MatchOutcome winner, string reason )
	{
		if ( State != MatchState.Round ) return;
		EnterRoundEnd( winner, reason );
	}

	public float SecondsLeftInState()
	{
		var elapsed = Time.Now - StateEnteredAt;
		return State switch
		{
			MatchState.Lobby => Math.Max( 0f, LobbyAutoStartSeconds - elapsed ),
			MatchState.Round => Math.Max( 0f, RoundDuration - elapsed ),
			MatchState.RoundEnd => Math.Max( 0f, RoundEndDuration - elapsed ),
			_ => 0f,
		};
	}

	// Filled in by Tasks 5-9.
	private void EnterRound() { }
	private void EnterRoundEnd( MatchOutcome winner, string reason ) { }
	private void EnterLobby() { }
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/Match.cs
git commit -m "feat(match): Match component skeleton with [Sync] state and public API"
```

---

## Task 5: Match state machine — basic transitions

**Files:**
- Modify: `Code/Match/Match.cs`

- [ ] **Step 1: Add OnUpdate state machine**

In `Code/Match/Match.cs`, add inside the class:

```csharp
protected override void OnUpdate()
{
	if ( !Networking.IsHost ) return;

	switch ( State )
	{
		case MatchState.Lobby:
			TickLobby();
			break;

		case MatchState.Round:
			// Win-condition checks added in Task 8.
			break;

		case MatchState.RoundEnd:
			if ( SecondsLeftInState() <= 0f ) EnterLobby();
			break;
	}
}

private void TickLobby()
{
	var playerCount = Game.ActiveScene?.GetAllComponents<Player>().Count() ?? 0;

	if ( playerCount < MinPlayersToStart )
	{
		// Hold the countdown at full duration while we wait for more players.
		StateEnteredAt = Time.Now;
		return;
	}

	if ( SecondsLeftInState() <= 0f )
	{
		EnterRound();
	}
}
```

This handles auto-start countdown + auto-transition out of RoundEnd. Round-state win checks come in Task 8. The empty `EnterRound`/`EnterRoundEnd`/`EnterLobby` bodies still no-op; full sequences come in Tasks 6-9.

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/Match.cs
git commit -m "feat(match): state-machine OnUpdate with lobby tick + RoundEnd auto-transition"
```

---

## Task 6: EnterRound — full world reset + role assignment + broadcast

**Files:**
- Modify: `Code/Match/Match.cs`

- [ ] **Step 1: Implement EnterRound and OnRoundStarted**

In `Code/Match/Match.cs`, replace the empty `EnterRound` stub and add `OnRoundStarted`:

```csharp
private void EnterRound()
{
	if ( !Networking.IsHost ) return;

	var scene = Game.ActiveScene;
	if ( scene is null ) return;

	var allPlayers = scene.GetAllComponents<Player>().ToList();
	if ( allPlayers.Count < MinPlayersToStart )
	{
		Log.Warning( $"Match.EnterRound: only {allPlayers.Count} players, need {MinPlayersToStart}" );
		return;
	}

	// 1. Clear corpses (generic + decompression).
	CorpseCleanupSignal.RaiseGenericCleanup();
	foreach ( var corpse in scene.GetAllComponents<Corpse>().ToList() )
	{
		corpse.Cleanup();
	}

	// 2. Reset every section to Idle.
	foreach ( var section in scene.GetAllComponents<Section>() )
	{
		section.Reset();
	}

	// 3. Reset all players: clear roles, mark alive, respawn.
	RoleAssigner.ClearAll( allPlayers );
	foreach ( var p in allPlayers )
	{
		// Player.IsAlive setter is private; the Kill flow handles death.
		// For respawn we need to flip alive back true. Player exposes no
		// direct setter today — we add one in this task.
		p.RespawnForNewRound();
	}

	// 4. Lock late joiners into spectator from this point onward.
	if ( PlayerSpawner is not null )
	{
		PlayerSpawner.RoundInProgress = true;
	}

	// 5. Assign saboteurs from the alive pool.
	var saboteurCount = RoleAssigner.ResolveSaboteurCount( allPlayers.Count, SaboteurCountOverride );
	var saboteurIds = RoleAssigner.Assign( allPlayers, saboteurCount );

	// 6. Update synced state.
	State = MatchState.Round;
	StateEnteredAt = Time.Now;
	LastOutcome = MatchOutcome.None;
	LastOutcomeReason = "";

	// 7. Broadcast to every client with the saboteur IDs in the payload
	//    (race-free role reveal — see spec §6).
	OnRoundStarted( saboteurIds );
}

[Rpc.Broadcast]
private void OnRoundStarted( Guid[] saboteurConnectionIds )
{
	var localId = Connection.Local?.Id ?? Guid.Empty;
	var localIsSaboteur = saboteurConnectionIds is not null
		&& saboteurConnectionIds.Contains( localId );
	RoundStarted?.Invoke( this, localIsSaboteur );
}
```

This depends on a new `Player.RespawnForNewRound()` method that flips IsAlive back to true and respawns the player at a SpawnPoint. The plan adds it next step.

- [ ] **Step 2: Add Player.RespawnForNewRound to Player.cs**

In `Code/Player/Player.cs`, add inside the class (near `Kill`):

```csharp
[Rpc.Host]
public void RespawnForNewRound()
{
	if ( !Networking.IsHost ) return;
	IsAlive = true;

	// Repick a spawn point via the scene's PlayerSpawner if available.
	var spawner = Game.ActiveScene?.GetAllComponents<PlayerSpawner>().FirstOrDefault();
	if ( spawner is not null && spawner.SpawnPoints.Count > 0 )
	{
		var pick = spawner.SpawnPoints[Game.Random.Int( 0, spawner.SpawnPoints.Count - 1 )];
		WorldTransform = pick.WorldTransform;
	}
}
```

Add `using System.Linq;` to the top of Player.cs if not already present.

- [ ] **Step 3: Verify compile**

Build. Expected: green.

- [ ] **Step 4: Commit**

```bash
git add Code/Match/Match.cs Code/Player/Player.cs
git commit -m "feat(match): EnterRound full-reset sequence + Player.RespawnForNewRound"
```

---

## Task 7: EnterRoundEnd + EnterLobby + OnRoundEnded broadcast

**Files:**
- Modify: `Code/Match/Match.cs`

- [ ] **Step 1: Implement EnterRoundEnd and EnterLobby**

In `Code/Match/Match.cs`, replace the empty `EnterRoundEnd` and `EnterLobby` stubs and add `OnRoundEnded`:

```csharp
private void EnterRoundEnd( MatchOutcome winner, string reason )
{
	if ( !Networking.IsHost ) return;

	State = MatchState.RoundEnd;
	StateEnteredAt = Time.Now;
	LastOutcome = winner;
	LastOutcomeReason = reason ?? "";

	// IsSaboteur is intentionally NOT cleared here — the RoundEndOverlay
	// reads IsSaboteur to display who the saboteurs were.
	// PlayerSpawner.RoundInProgress stays true so late joiners during
	// RoundEnd still spawn as spectators.

	OnRoundEnded( winner, LastOutcomeReason );
}

[Rpc.Broadcast]
private void OnRoundEnded( MatchOutcome winner, string reason )
{
	RoundEnded?.Invoke( this, winner, reason );
}

private void EnterLobby()
{
	if ( !Networking.IsHost ) return;

	var scene = Game.ActiveScene;
	if ( scene is null ) return;

	var allPlayers = scene.GetAllComponents<Player>().ToList();

	// 1. Clear roles for the next round.
	RoleAssigner.ClearAll( allPlayers );

	// 2. Respawn every player alive at a SpawnPoint.
	foreach ( var p in allPlayers )
	{
		p.RespawnForNewRound();
	}

	// 3. Reset sections + clear corpses (in case any leftover from RoundEnd).
	foreach ( var section in scene.GetAllComponents<Section>() )
	{
		section.Reset();
	}
	CorpseCleanupSignal.RaiseGenericCleanup();
	foreach ( var corpse in scene.GetAllComponents<Corpse>().ToList() )
	{
		corpse.Cleanup();
	}

	// 4. Now late joiners spawn alive again.
	if ( PlayerSpawner is not null )
	{
		PlayerSpawner.RoundInProgress = false;
	}

	// 5. Update synced state.
	State = MatchState.Lobby;
	StateEnteredAt = Time.Now;
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/Match.cs
git commit -m "feat(match): EnterRoundEnd + EnterLobby + RoundEnded broadcast"
```

---

## Task 8: Win condition checks during Round

**Files:**
- Modify: `Code/Match/Match.cs`

- [ ] **Step 1: Add win-condition tick**

In `Code/Match/Match.cs`, replace the `case MatchState.Round:` body in `OnUpdate`:

```csharp
case MatchState.Round:
	TickRoundWinChecks();
	break;
```

And add the method:

```csharp
private void TickRoundWinChecks()
{
	// Cheapest check first — timeout is just a single subtraction.
	if ( SecondsLeftInState() <= 0f )
	{
		EnterRoundEnd( MatchOutcome.Saboteur, "time expired" );
		return;
	}

	var scene = Game.ActiveScene;
	if ( scene is null ) return;

	var alivePlayers = scene.GetAllComponents<Player>().Where( p => p.IsAlive ).ToList();
	int crewAlive = alivePlayers.Count( p => !p.IsSaboteur );
	int sabsAlive = alivePlayers.Count( p => p.IsSaboteur );

	if ( crewAlive == 0 )
	{
		EnterRoundEnd( MatchOutcome.Saboteur, "all crew dead" );
		return;
	}

	if ( sabsAlive == 0 )
	{
		EnterRoundEnd( MatchOutcome.Crew, "all saboteurs eliminated" );
		return;
	}

	if ( crewAlive == 1 && sabsAlive >= 1 )
	{
		EnterRoundEnd( MatchOutcome.Saboteur, "1v1 endgame — saboteur wins by default" );
		return;
	}

	// C2 (tasks complete) and C3 (saboteurs voted out) call EndRound directly.
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/Match.cs
git commit -m "feat(match): win-condition checks (timeout, all-crew-dead, all-sabs-eliminated, 1v1)"
```

---

## Task 9: Debug ConCmds — start_round, end_round, match_state

**Files:**
- Modify: `Code/Debug/DebugCommands.cs`

- [ ] **Step 1: Add the three ConCmds**

In `Code/Debug/DebugCommands.cs`, add inside `DebugCommands`:

```csharp
[ConCmd( "decompv2_start_round" )]
public static void StartRound()
{
	if ( !Networking.IsHost )
	{
		Log.Warning( "decompv2_start_round: host only" );
		return;
	}
	var match = Match.Current;
	if ( match is null )
	{
		Log.Warning( "decompv2_start_round: no Match component in scene" );
		return;
	}
	match.StartRound();
}

[ConCmd( "decompv2_end_round" )]
public static void EndRound( string winner )
{
	if ( !Networking.IsHost )
	{
		Log.Warning( "decompv2_end_round: host only" );
		return;
	}
	if ( !System.Enum.TryParse<MatchOutcome>( winner, ignoreCase: true, out var outcome )
		|| outcome == MatchOutcome.None )
	{
		Log.Warning( "decompv2_end_round: winner must be 'Crew' or 'Saboteur'" );
		return;
	}
	var match = Match.Current;
	if ( match is null )
	{
		Log.Warning( "decompv2_end_round: no Match component in scene" );
		return;
	}
	match.EndRound( outcome, "debug command" );
}

[ConCmd( "decompv2_match_state" )]
public static void MatchState()
{
	var match = Match.Current;
	if ( match is null )
	{
		Log.Warning( "decompv2_match_state: no Match component in scene" );
		return;
	}
	Log.Info( $"Match state: {match.State}, " +
		$"timeInState={Time.Now - match.StateEnteredAt:F1}s, " +
		$"secondsLeft={match.SecondsLeftInState():F1}, " +
		$"lastOutcome={match.LastOutcome}, " +
		$"lastReason='{match.LastOutcomeReason}'" );
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Debug/DebugCommands.cs
git commit -m "feat(debug): decompv2_start_round, decompv2_end_round, decompv2_match_state"
```

---

## Task 10: Test scaffold — add Match GameObject to minimal.scene

This is **editor work**.

**Files:**
- Modify: `Assets/scenes/minimal.scene`

- [ ] **Step 1: Add the Match GameObject**

In the s&box editor:
1. Open `Assets/scenes/minimal.scene`.
2. Right-click scene root → **New GameObject** named `Match`.
3. Add component **Decompression.Match**.
4. On the Match component, drag the existing `PlayerSpawner` GameObject into the **Player Spawner** field.
5. (Optional) For fast iteration, lower these in the inspector:
   - `Lobby Auto Start Seconds` → `5`
   - `Round Duration` → `30`
   - `Round End Duration` → `5`
6. Save.

- [ ] **Step 2: Smoke test**

Launch the game (single instance). Open console and run:
```
decompv2_match_state
```
Expected log: `Match state: Lobby, timeInState=...s, secondsLeft=...s, lastOutcome=None, lastReason=''`

The Lobby countdown is "stuck" because there's only 1 player (below `MinPlayersToStart=3`). That's correct.

Run:
```
decompv2_start_round
```
Expected log warning: "Match.EnterRound: only 1 players, need 3" (since we're under threshold).

Run:
```
decompv2_match_state
```
Still in Lobby. Correct.

- [ ] **Step 3: Commit**

```bash
git add Assets/scenes/minimal.scene
git commit -m "test(match): scaffold Match GameObject in minimal.scene"
```

---

## Task 11: RoleHud — persistent corner role badge

**Files:**
- Create: `Code/Match/RoleHud.razor`
- Create: `Code/Match/RoleHud.razor.scss`

- [ ] **Step 1: Write the Razor panel**

`Code/Match/RoleHud.razor`:

```razor
@using Sandbox;
@using Sandbox.UI;
@namespace Decompression
@inherits PanelComponent

<root>
	@if ( showBadge )
	{
		<div class="badge @badgeClass">@badgeText</div>
	}
</root>

@code {
	bool showBadge;
	string badgeText = "";
	string badgeClass = "";

	protected override void OnUpdate()
	{
		var match = Match.Current;
		var localPlayer = Game.ActiveScene?
			.GetAllComponents<Decompression.Player>()
			.FirstOrDefault( p => p.Network.Owner == Connection.Local );

		if ( match is null || localPlayer is null )
		{
			showBadge = false;
			return;
		}

		// Only show during Round and RoundEnd.
		if ( match.State == MatchState.Lobby )
		{
			showBadge = false;
			return;
		}

		showBadge = true;
		if ( localPlayer.IsSaboteur )
		{
			badgeText = "SABOTEUR";
			badgeClass = "sab";
		}
		else
		{
			badgeText = "CREW";
			badgeClass = "crew";
		}
	}

	protected override int BuildHash() => System.HashCode.Combine( showBadge, badgeText, badgeClass );
}
```

`Code/Match/RoleHud.razor.scss`:

```scss
.badge {
	position: absolute;
	bottom: 24px;
	left: 24px;
	padding: 8px 18px;
	font-family: monospace;
	font-size: 24px;
	letter-spacing: 4px;
	border: 2px solid;
	background-color: rgba(0, 0, 0, 0.6);
	pointer-events: none;
}

.badge.sab {
	color: #ff4444;
	border-color: #ff4444;
}

.badge.crew {
	color: #4488ff;
	border-color: #4488ff;
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/RoleHud.razor Code/Match/RoleHud.razor.scss
git commit -m "feat(match): RoleHud persistent corner role badge"
```

---

## Task 12: RoleRevealOverlay — 3s flash on round start

**Files:**
- Create: `Code/Match/RoleRevealOverlay.razor`
- Create: `Code/Match/RoleRevealOverlay.razor.scss`

- [ ] **Step 1: Write the Razor panel**

`Code/Match/RoleRevealOverlay.razor`:

```razor
@using Sandbox;
@using Sandbox.UI;
@namespace Decompression
@inherits PanelComponent

<root>
	@if ( showOverlay )
	{
		<div class="overlay @overlayClass">
			<div class="title">@titleText</div>
			<div class="subtitle">@subtitleText</div>
		</div>
	}
</root>

@code {
	bool showOverlay;
	string titleText = "";
	string subtitleText = "";
	string overlayClass = "";
	TimeSince shownAt;
	const float OverlayDuration = 3f;

	protected override void OnAwake()
	{
		Decompression.Match.RoundStarted += OnRoundStarted;
	}

	protected override void OnDestroy()
	{
		Decompression.Match.RoundStarted -= OnRoundStarted;
	}

	private void OnRoundStarted( Decompression.Match match, bool localIsSaboteur )
	{
		if ( localIsSaboteur )
		{
			titleText = "YOU ARE THE SABOTEUR";
			subtitleText = "Vent the crew before they finish their tasks.";
			overlayClass = "sab";
		}
		else
		{
			titleText = "YOU ARE A CREWMATE";
			subtitleText = "Survive. Trust no one.";
			overlayClass = "crew";
		}
		showOverlay = true;
		shownAt = 0f;
	}

	protected override int BuildHash() => System.HashCode.Combine( showOverlay, titleText, overlayClass );

	protected override void OnUpdate()
	{
		if ( showOverlay && shownAt > OverlayDuration ) showOverlay = false;
	}
}
```

`Code/Match/RoleRevealOverlay.razor.scss`:

```scss
.overlay {
	position: absolute;
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	justify-content: center;
	align-items: center;
	background-color: rgba(0, 0, 0, 0.7);
	pointer-events: none;
	animation: reveal-fade 3s ease-out forwards;
}

.overlay.sab {
	background-color: rgba(80, 0, 0, 0.7);
}

.overlay.crew {
	background-color: rgba(0, 30, 80, 0.7);
}

.title {
	font-family: monospace;
	font-size: 64px;
	letter-spacing: 12px;
	color: #eeeeee;
}

.subtitle {
	font-family: monospace;
	font-size: 24px;
	color: #cccccc;
	margin-top: 16px;
}

@keyframes reveal-fade {
	0%   { opacity: 0; }
	8%   { opacity: 1; }
	75%  { opacity: 1; }
	100% { opacity: 0; }
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/RoleRevealOverlay.razor Code/Match/RoleRevealOverlay.razor.scss
git commit -m "feat(match): RoleRevealOverlay — 3s flash on round start"
```

---

## Task 13: RoundTimerHud — top-center round countdown

**Files:**
- Create: `Code/Match/RoundTimerHud.razor`
- Create: `Code/Match/RoundTimerHud.razor.scss`

- [ ] **Step 1: Write the Razor panel**

`Code/Match/RoundTimerHud.razor`:

```razor
@using Sandbox;
@using Sandbox.UI;
@namespace Decompression
@inherits PanelComponent

<root>
	@if ( showTimer )
	{
		<div class="timer">@timerText</div>
	}
</root>

@code {
	bool showTimer;
	string timerText = "";

	protected override void OnUpdate()
	{
		var match = Decompression.Match.Current;
		if ( match is null || match.State != MatchState.Round )
		{
			showTimer = false;
			return;
		}
		showTimer = true;

		var seconds = (int)System.Math.Ceiling( match.SecondsLeftInState() );
		var minutes = seconds / 60;
		var s = seconds % 60;
		timerText = $"{minutes}:{s:D2}";
	}

	protected override int BuildHash() => System.HashCode.Combine( showTimer, timerText );
}
```

`Code/Match/RoundTimerHud.razor.scss`:

```scss
.timer {
	position: absolute;
	top: 24px;
	left: 50%;
	transform: translateX(-50%);
	padding: 6px 18px;
	font-family: monospace;
	font-size: 32px;
	letter-spacing: 4px;
	color: #eeeeee;
	background-color: rgba(0, 0, 0, 0.5);
	border: 1px solid #888888;
	pointer-events: none;
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/RoundTimerHud.razor Code/Match/RoundTimerHud.razor.scss
git commit -m "feat(match): RoundTimerHud — top-center MM:SS round timer"
```

---

## Task 14: LobbyCountdownHud — top-center lobby auto-start countdown

**Files:**
- Create: `Code/Match/LobbyCountdownHud.razor`
- Create: `Code/Match/LobbyCountdownHud.razor.scss`

- [ ] **Step 1: Write the Razor panel**

`Code/Match/LobbyCountdownHud.razor`:

```razor
@using Sandbox;
@using Sandbox.UI;
@namespace Decompression
@inherits PanelComponent

<root>
	@if ( showHud )
	{
		<div class="lobby-countdown">@hudText</div>
	}
</root>

@code {
	bool showHud;
	string hudText = "";

	protected override void OnUpdate()
	{
		var match = Decompression.Match.Current;
		if ( match is null || match.State != MatchState.Lobby )
		{
			showHud = false;
			return;
		}

		var playerCount = Game.ActiveScene?
			.GetAllComponents<Decompression.Player>()
			.Count() ?? 0;

		showHud = true;
		if ( playerCount < match.MinPlayersToStart )
		{
			hudText = $"Waiting for players ({match.MinPlayersToStart} minimum, 4 recommended)";
		}
		else
		{
			var seconds = (int)System.Math.Ceiling( match.SecondsLeftInState() );
			hudText = $"Round starting in {seconds}s";
		}
	}

	protected override int BuildHash() => System.HashCode.Combine( showHud, hudText );
}
```

`Code/Match/LobbyCountdownHud.razor.scss`:

```scss
.lobby-countdown {
	position: absolute;
	top: 24px;
	left: 50%;
	transform: translateX(-50%);
	padding: 6px 18px;
	font-family: monospace;
	font-size: 24px;
	color: #cccccc;
	background-color: rgba(0, 0, 0, 0.5);
	border: 1px solid #555555;
	pointer-events: none;
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/LobbyCountdownHud.razor Code/Match/LobbyCountdownHud.razor.scss
git commit -m "feat(match): LobbyCountdownHud — auto-start countdown + waiting-for-players"
```

---

## Task 15: RoundEndOverlay — full-screen results banner

**Files:**
- Create: `Code/Match/RoundEndOverlay.razor`
- Create: `Code/Match/RoundEndOverlay.razor.scss`

- [ ] **Step 1: Write the Razor panel**

`Code/Match/RoundEndOverlay.razor`:

```razor
@using System.Linq;
@using Sandbox;
@using Sandbox.UI;
@namespace Decompression
@inherits PanelComponent

<root>
	@if ( showOverlay )
	{
		<div class="results @resultsClass">
			<div class="winner-banner">@winnerText</div>
			<div class="reason">@reasonText</div>
			<div class="reveal">
				<div class="reveal-title">SABOTEUR(S):</div>
				<div class="reveal-list">@saboteurList</div>
			</div>
		</div>
	}
</root>

@code {
	bool showOverlay;
	string winnerText = "";
	string reasonText = "";
	string saboteurList = "";
	string resultsClass = "";

	protected override void OnUpdate()
	{
		var match = Decompression.Match.Current;
		if ( match is null || match.State != MatchState.RoundEnd )
		{
			showOverlay = false;
			return;
		}
		showOverlay = true;

		winnerText = match.LastOutcome switch
		{
			MatchOutcome.Crew => "CREW WINS",
			MatchOutcome.Saboteur => "SABOTEUR WINS",
			_ => "ROUND OVER",
		};
		resultsClass = match.LastOutcome == MatchOutcome.Saboteur ? "sab" : "crew";
		reasonText = match.LastOutcomeReason ?? "";

		var saboteurs = Game.ActiveScene?
			.GetAllComponents<Decompression.Player>()
			.Where( p => p.IsSaboteur )
			.Select( p => p.Network.Owner?.DisplayName ?? "?" )
			.ToList();

		saboteurList = saboteurs is null || saboteurs.Count == 0
			? "(none)"
			: string.Join( ", ", saboteurs );
	}

	protected override int BuildHash() => System.HashCode.Combine( showOverlay, winnerText, reasonText, saboteurList );
}
```

`Code/Match/RoundEndOverlay.razor.scss`:

```scss
.results {
	position: absolute;
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	justify-content: center;
	align-items: center;
	background-color: rgba(0, 0, 0, 0.75);
	pointer-events: none;
}

.results.sab .winner-banner {
	color: #ff4444;
}
.results.crew .winner-banner {
	color: #44aaff;
}

.winner-banner {
	font-family: monospace;
	font-size: 72px;
	letter-spacing: 16px;
	margin-bottom: 16px;
}

.reason {
	font-family: monospace;
	font-size: 28px;
	color: #cccccc;
	margin-bottom: 48px;
}

.reveal {
	font-family: monospace;
	color: #aaaaaa;
}

.reveal-title {
	font-size: 18px;
	letter-spacing: 4px;
	margin-bottom: 8px;
}

.reveal-list {
	font-size: 24px;
	color: #ff8888;
}
```

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/RoundEndOverlay.razor Code/Match/RoundEndOverlay.razor.scss
git commit -m "feat(match): RoundEndOverlay — winner banner + reason + saboteur reveal"
```

---

## Task 16: SaboteurNametagOverlay — red marker over other saboteurs (saboteur-only)

**Files:**
- Create: `Code/Match/SaboteurNametagOverlay.razor`
- Create: `Code/Match/SaboteurNametagOverlay.razor.scss`

This panel renders one screen-space marker per *other* saboteur, positioned at the world-to-screen projection of their head. Only visible to the local saboteur; invisible to crew clients.

- [ ] **Step 1: Write the Razor panel**

`Code/Match/SaboteurNametagOverlay.razor`:

```razor
@using System.Linq;
@using Sandbox;
@using Sandbox.UI;
@namespace Decompression
@inherits PanelComponent

<root>
	@foreach ( var marker in markers )
	{
		<div class="marker" style="left: @(marker.X)px; top: @(marker.Y)px;">●</div>
	}
</root>

@code {
	private record struct MarkerPos( int X, int Y );
	private List<MarkerPos> markers = new();

	protected override void OnUpdate()
	{
		markers.Clear();

		var match = Decompression.Match.Current;
		if ( match is null || match.State != MatchState.Round )
		{
			return;
		}

		var localPlayer = Game.ActiveScene?
			.GetAllComponents<Decompression.Player>()
			.FirstOrDefault( p => p.Network.Owner == Connection.Local );
		if ( localPlayer is null || !localPlayer.IsSaboteur ) return;

		var camera = Game.ActiveScene?
			.GetAllComponents<CameraComponent>()
			.FirstOrDefault( c => c.IsMainCamera );
		if ( camera is null ) return;

		var others = Game.ActiveScene
			.GetAllComponents<Decompression.Player>()
			.Where( p => p != localPlayer && p.IsSaboteur && p.IsAlive )
			.ToList();

		foreach ( var other in others )
		{
			var headPos = other.WorldPosition + Vector3.Up * 80f;
			var screen = camera.PointToScreenNormalized( headPos );
			if ( screen.z <= 0f ) continue;   // behind the camera
			var screenSize = Screen.Size;
			var px = (int)(screen.x * screenSize.x);
			var py = (int)(screen.y * screenSize.y);
			markers.Add( new MarkerPos( px, py ) );
		}
	}

	protected override int BuildHash()
	{
		// Re-render whenever the marker list changes meaningfully.
		var hash = markers.Count;
		foreach ( var m in markers )
		{
			hash = System.HashCode.Combine( hash, m.X / 4, m.Y / 4 );  // 4px granularity to avoid every-frame rebuild
		}
		return hash;
	}
}
```

`Code/Match/SaboteurNametagOverlay.razor.scss`:

```scss
.marker {
	position: absolute;
	color: #ff3333;
	font-size: 24px;
	transform: translate(-50%, -50%);
	pointer-events: none;
	text-shadow: 0 0 4px #000000;
}
```

Note: `CameraComponent.PointToScreenNormalized` and `Screen.Size` are best-effort API names. If your s&box revision uses different names (e.g. `WorldToScreen`, `ScreenSize`), substitute. The intent is "given a world-space Vector3, return normalized 0..1 screen coordinates plus a Z component for behind-the-camera detection".

- [ ] **Step 2: Verify compile**

Build. Expected: green.

- [ ] **Step 3: Commit**

```bash
git add Code/Match/SaboteurNametagOverlay.razor Code/Match/SaboteurNametagOverlay.razor.scss
git commit -m "feat(match): SaboteurNametagOverlay — red marker over other saboteurs (saboteur-only)"
```

---

## Task 17: Wire HUD components into the player prefab

This is **editor work**.

**Files:**
- Modify: `Assets/prefabs/player.prefab`

- [ ] **Step 1: Add the six HUD components**

In the s&box editor:
1. Open `Assets/prefabs/player.prefab`.
2. Select the existing `Hud` child GameObject (created in Group B for DeathHud).
3. Add components in this order (each is a Razor PanelComponent):
   - `Decompression.RoleRevealOverlay`
   - `Decompression.RoleHud`
   - `Decompression.RoundTimerHud`
   - `Decompression.LobbyCountdownHud`
   - `Decompression.RoundEndOverlay`
   - `Decompression.SaboteurNametagOverlay`
4. Save the prefab.

- [ ] **Step 2: Smoke run (single instance)**

Launch the game alone. Expected:
- LobbyCountdownHud shows "Waiting for players (3 minimum, 4 recommended)" at top center.
- No role badge (Lobby state, no roles yet).
- No round timer.

- [ ] **Step 3: Commit**

```bash
git add Assets/prefabs/player.prefab
git commit -m "feat(match): wire six HUD components into player prefab"
```

---

## Task 18: Multi-client lobby countdown smoke test

This task does no implementation — it verifies the lobby auto-start with multiple instances.

**Files:** none

- [ ] **Step 1: Launch host + 2 client instances**

Total 3 players (meets `MinPlayersToStart = 3`).

- [ ] **Step 2: Verify countdown ticks down**

LobbyCountdownHud shows "Round starting in N seconds" and counts down.

- [ ] **Step 3: Verify auto-start fires the round**

When countdown hits 0, every client sees the role-reveal overlay (3s flash) showing their assigned role. RoundTimerHud appears at top showing the round timer (e.g., "0:30" if you set RoundDuration=30 for testing). LobbyCountdownHud disappears. RoleHud appears in the corner.

- [ ] **Step 4: Verify saboteur identity**

If you're the saboteur, you see a red marker over the head of any other saboteur (only matters with 8+ players). With one saboteur in a 3-4 player lobby, no markers visible (correct).

- [ ] **Step 5: Commit a verification note**

```bash
git commit --allow-empty -m "test(match): multi-client lobby auto-start verified"
```

---

## Task 19: Win condition smoke tests

This task verifies each of the four saboteur-side win conditions plus the manual end-round command. Run as multi-client.

**Files:** none

- [ ] **Step 1: All-crew-dead win**

Start a round with 1 saboteur + 2-3 crew. Saboteur kills all crew (use existing decompression mechanics or `decompv2_kill <name> Generic` from the host). Within ~1 frame after the last crew dies, RoundEndOverlay appears with "SABOTEUR WINS — all crew dead".

- [ ] **Step 2: 1v1-endgame win**

Start with 1 saboteur + 3 crew (4 players total). Saboteur kills 2 crew, leaving 1 crew + 1 saboteur. RoundEndOverlay should fire immediately with "SABOTEUR WINS — 1v1 endgame — saboteur wins by default".

- [ ] **Step 3: Time-expired win**

In the inspector, set `Match.RoundDuration = 30`. Start a round with 4 players, all stay alive. Wait 30 seconds. RoundEndOverlay fires with "SABOTEUR WINS — time expired".

- [ ] **Step 4: All-saboteurs-eliminated win**

Start a round with 1 saboteur + 3 crew. Host runs `decompv2_kill <saboteurName> Generic` to kill the saboteur. RoundEndOverlay fires with "CREW WINS — all saboteurs eliminated".

- [ ] **Step 5: Manual round end**

In any active round, host runs `decompv2_end_round Crew`. RoundEndOverlay fires with "CREW WINS — debug command".

- [ ] **Step 6: RoundEnd → Lobby auto-transition**

Wait `RoundEndDuration` seconds (default 10s, or whatever you've set). RoundEndOverlay disappears. LobbyCountdownHud reappears with the auto-start countdown. All players are alive at SpawnPoints.

- [ ] **Step 7: Commit**

```bash
git commit --allow-empty -m "test(match): all four saboteur-win conditions + manual end verified"
```

---

## Task 20: Late-joiner and multi-round smoke tests

**Files:** none

- [ ] **Step 1: Late join during Round**

Host + 2 clients in a round (3 players, round in progress). Launch a 4th instance. The new player spawns as spectator (Group B late-join behavior). They see the RoundTimerHud counting, no RoleRevealOverlay flash (that already fired), no role badge (they don't have a role).

- [ ] **Step 2: Late join during RoundEnd**

End the current round (`decompv2_end_round Saboteur`). Within the 10s RoundEnd window, launch another late-joiner. They spawn as spectator and see the RoundEndOverlay with the synced winner + reason + saboteur reveal.

- [ ] **Step 3: Late join during Lobby**

Wait for the auto-transition back to Lobby. New joiners now spawn alive normally (Group B late-join with `RoundInProgress = false`).

- [ ] **Step 4: Multi-round full cycle**

Run a full Lobby → Round → RoundEnd → Lobby → Round cycle with 3-4 players. Verify on each round-start:
- All players are alive at SpawnPoints (no leftover dead players)
- No corpses from previous round
- Sections that were vented in the prior round are reset to Idle (Hatch shows ClosedVisual, doors are open)
- New saboteur assignment may or may not be the same player (random)

- [ ] **Step 5: Commit**

```bash
git commit --allow-empty -m "test(match): late-joiner phase handling + multi-round cycle verified"
```

---

## Self-review (writer's notes)

After writing the plan I checked it against the spec:

**Spec coverage:**
- §1 in-scope items: Match component (Tasks 4-8), state machine + transitions (Tasks 5-7), saboteur count + assignment (Task 3, Task 6), all four win conditions (Task 8), `EndRound` API (Task 4 with body in Task 7), `Player.IsSaboteur` populated by RoleAssigner.Assign (Task 3 + 6), role reveal flash + persistent badge + saboteur markers (Tasks 11-12, 16), round timer + lobby countdown HUDs (Tasks 13-14), round end results overlay (Task 15), full reset on round-start (Task 6), round-end → lobby auto-transition (Task 7 + Task 5's RoundEnd tick), late-joiner phase-aware spawning via PlayerSpawner.RoundInProgress (Tasks 6 and 7), debug ConCmds (Task 9), Section.Reset addition (Task 2).
- §2 decisions: every key decision has an implementing task — manual + auto-start trigger (Task 5 lobby tick + Task 9 ConCmd), hybrid saboteur count (Task 3), 8m default (Task 4 property), MinPlayersToStart=3 (Task 4 + Task 5 guard), 1v1 rule (Task 8), full reset (Task 6), role reveal payload-embedded (Task 6 OnRoundStarted broadcast).
- §3 architecture: Match component as orchestrator (Task 4), reactive followers (Tasks 11-16).
- §4 components: each component has a task. RoleAssigner static helper (Task 3). All [Property] tunables defined in Task 4.
- §5 lifecycle: every transition is implemented in Tasks 6-8. Reset sequence in Task 6 covers all 9 spec'd steps.
- §6 networking: [Sync(SyncFlags.FromHost)] used consistently, `[Rpc.Host]` on StartRound/EndRound, `[Rpc.Broadcast]` on OnRoundStarted/OnRoundEnded with race-free saboteur ID payload.
- §7 failure modes: idempotency guards in StartRound/EndRound (Task 4), MinPlayersToStart re-check in EnterRound (Task 6), null-checks throughout the UI components, Match.Current null-safe.
- §8 testing: Tasks 18-20 cover the manual matrix.
- §9 public surface: Match.RoundStarted/RoundEnded events, Match.EndRound API, Match.Current accessor, Section.Reset all present.

**Placeholder scan:** "best-effort API names" notes for `PointToScreenNormalized` and `Screen.Size` in Task 16 are honest implementer-time verifications, not placeholders. No "TBD" / "implement later" in any code step.

**Type consistency:** `MatchState`, `MatchOutcome`, `Match.State`, `Match.StateEnteredAt`, `Match.LastOutcome`, `Match.LastOutcomeReason`, `Match.RoundDuration`, `Match.MinPlayersToStart`, `Match.SecondsLeftInState()`, `Match.Current`, `Match.RoundStarted`, `Match.RoundEnded`, `RoleAssigner.Assign`, `RoleAssigner.ClearAll`, `RoleAssigner.ResolveSaboteurCount`, `Player.SetSaboteur`, `Player.IsSaboteur`, `Player.RespawnForNewRound`, `Section.Reset`, `PlayerSpawner.RoundInProgress` — all consistently named across tasks.

No issues found that need a re-pass.
