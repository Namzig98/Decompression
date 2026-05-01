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

		// 7. Broadcast to every client with the saboteur IDs in the payload.
		//    Race-free role reveal: clients read role from payload, not from
		//    Player.IsSaboteur (which may not have synced yet).
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

	// Filled in by Task 7.
	private void EnterRoundEnd( MatchOutcome winner, string reason ) { }
	private void EnterLobby() { }
}
