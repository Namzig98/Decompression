using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Match : Component
{
	[Property] public PlayerSpawner PlayerSpawner { get; set; }

	[Property] public float LobbyAutoStartSeconds { get; set; } = 30f;
	[Property] public float RoundDuration { get; set; } = 480f;       // 8 minutes
	[Property] public float RoundEndDuration { get; set; } = 10f;
	[Property] public int MinPlayersToStart { get; set; } = 3;        // 4 recommended
	[Property] public int SaboteurCountOverride { get; set; } = 0;    // 0 = auto-scale

	[Sync( SyncFlags.FromHost )] public MatchState State { get; private set; } = MatchState.Lobby;
	[Sync( SyncFlags.FromHost )] public float StateEnteredAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public MatchOutcome LastOutcome { get; private set; }
	[Sync( SyncFlags.FromHost )] public string LastOutcomeReason { get; private set; } = "";

	public static event Action<Match, bool /* localIsSaboteur */> RoundStarted;
	public static event Action<Match, MatchOutcome, string> RoundEnded;

	// Convenience accessor for any subsystem to find the active match.
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

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		switch ( State )
		{
			case MatchState.Lobby:
				TickLobby();
				break;

			case MatchState.Round:
				TickRoundWinChecks();
				break;

			case MatchState.RoundEnd:
				if ( SecondsLeftInState() <= 0f ) EnterLobby();
				break;
		}
	}

	private void TickRoundWinChecks()
	{
		// Cheapest check first.
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

	private TimeSince timeSinceLobbyBroadcast = 9999f;

	private void TickLobby()
	{
		var playerCount = Game.ActiveScene?.GetAllComponents<Player>().Count() ?? 0;

		if ( playerCount < MinPlayersToStart )
		{
			// Hold the countdown at full duration while we wait for more players.
			StateEnteredAt = Time.Now;
		}
		else if ( SecondsLeftInState() <= 0f )
		{
			EnterRound();
			return;
		}

		// Heartbeat: re-broadcast lobby state ~once per second so newly-
		// connected clients receive the correct StateEnteredAt without
		// waiting for the next explicit transition. Round/RoundEnd states
		// don't need this — they always have a transition that broadcasts.
		if ( timeSinceLobbyBroadcast >= 1f )
		{
			BroadcastStateUpdate( State, StateEnteredAt, LastOutcome, LastOutcomeReason );
			timeSinceLobbyBroadcast = 0f;
		}
	}

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
			section.ResetSection();
		}

		// 3. Reset all players: clear roles, mark alive, respawn at unique points.
		RoleAssigner.ClearAll( allPlayers );
		RespawnAllAtUniqueSpawnPoints( allPlayers );

		// 4. Lock late joiners into spectator from this point onward.
		if ( PlayerSpawner is not null )
		{
			PlayerSpawner.RoundInProgress = true;
		}

		// 5. Assign saboteurs from the alive pool.
		var saboteurCount = RoleAssigner.ResolveSaboteurCount( allPlayers.Count, SaboteurCountOverride );
		var saboteurIds = RoleAssigner.Assign( allPlayers, saboteurCount );

		// 6. Update synced state — broadcast to all clients (host included).
		BroadcastStateUpdate( MatchState.Round, Time.Now, MatchOutcome.None, "" );

		// 7. Broadcast role reveal with the saboteur IDs in the payload.
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

	private void EnterRoundEnd( MatchOutcome winner, string reason )
	{
		if ( !Networking.IsHost ) return;

		// IsSaboteur intentionally NOT cleared — the RoundEndOverlay reads
		// IsSaboteur to display who the saboteurs were.
		// PlayerSpawner.RoundInProgress stays true so late joiners during
		// RoundEnd still spawn as spectators.

		BroadcastStateUpdate( MatchState.RoundEnd, Time.Now, winner, reason ?? "" );
		OnRoundEnded( winner, reason ?? "" );
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

		// 2. Respawn every player alive at a unique SpawnPoint.
		RespawnAllAtUniqueSpawnPoints( allPlayers );

		// 3. Reset sections + clear corpses (anything left from RoundEnd).
		foreach ( var section in scene.GetAllComponents<Section>() )
		{
			section.ResetSection();
		}
		CorpseCleanupSignal.RaiseGenericCleanup();
		foreach ( var corpse in scene.GetAllComponents<Corpse>().ToList() )
		{
			corpse.Cleanup();
		}

		// 4. Late joiners now spawn alive again.
		if ( PlayerSpawner is not null )
		{
			PlayerSpawner.RoundInProgress = false;
		}

		// 5. Update synced state — broadcast to all clients.
		BroadcastStateUpdate( MatchState.Lobby, Time.Now, LastOutcome, LastOutcomeReason );
	}

	// Updates the [Sync] state on every client (host included). Works around
	// scene-static [Sync] not propagating reliably from the host's writes.
	[Rpc.Broadcast]
	private void BroadcastStateUpdate( MatchState state, float enteredAt, MatchOutcome outcome, string reason )
	{
		State = state;
		StateEnteredAt = enteredAt;
		LastOutcome = outcome;
		LastOutcomeReason = reason ?? "";
	}

	// Shuffle the configured SpawnPoints and assign each player a unique one
	// (round-robin if more players than points). Avoids two players spawning
	// at the same coordinates and ragdolling into each other on round start.
	private void RespawnAllAtUniqueSpawnPoints( System.Collections.Generic.List<Player> players )
	{
		var spawnPoints = ( PlayerSpawner?.SpawnPoints ?? new System.Collections.Generic.List<GameObject>() ).ToList();

		// Fisher-Yates shuffle.
		for ( int i = spawnPoints.Count - 1; i > 0; i-- )
		{
			int j = Game.Random.Int( 0, i );
			(spawnPoints[i], spawnPoints[j]) = (spawnPoints[j], spawnPoints[i]);
		}

		for ( int i = 0; i < players.Count; i++ )
		{
			var p = players[i];
			if ( !p.IsValid() ) continue;

			Vector3 pos = p.WorldPosition;
			Rotation rot = p.WorldRotation;
			if ( spawnPoints.Count > 0 )
			{
				var sp = spawnPoints[i % spawnPoints.Count];
				if ( sp is not null )
				{
					pos = sp.WorldPosition;
					rot = sp.WorldRotation;
				}
			}

			p.RespawnForNewRound( pos, rot );
		}
	}
}
