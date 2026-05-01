using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class TaskCoordinator : Component
{
	[Property] public int TasksPerCrew { get; set; } = 5;

	protected override void OnAwake()
	{
		Match.RoundStarted += OnRoundStarted;
		Match.RoundEnded += OnRoundEnded;
	}

	protected override void OnDestroy()
	{
		Match.RoundStarted -= OnRoundStarted;
		Match.RoundEnded -= OnRoundEnded;
	}

	private void OnRoundStarted( Match match, bool localIsSaboteur )
	{
		if ( !Networking.IsHost ) return;

		var scene = Game.ActiveScene;
		if ( scene is null ) return;

		var allPlayers = scene.GetAllComponents<Player>().ToList();
		var allTasks = scene.GetAllComponents<TaskObject>().ToList();

		if ( allTasks.Count == 0 )
		{
			Log.Warning( "TaskCoordinator.OnRoundStarted: no TaskObjects in scene — round will run on saboteur-side wins only" );
			return;
		}

		TaskAssigner.Assign( allPlayers, allTasks, TasksPerCrew );
	}

	private void OnRoundEnded( Match match, MatchOutcome outcome, string reason )
	{
		if ( !Networking.IsHost ) return;

		var scene = Game.ActiveScene;
		if ( scene is null ) return;

		var allTasks = scene.GetAllComponents<TaskObject>();
		TaskAssigner.ClearAll( allTasks );
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		var match = Match.Current;
		if ( match is null || match.State != MatchState.Round ) return;

		var scene = Game.ActiveScene;
		if ( scene is null ) return;

		var alivePlayers = scene.GetAllComponents<Player>()
			.Where( p => p.IsAlive )
			.ToList();
		var aliveConnectionIds = alivePlayers
			.Select( p => p.OwnerConnectionId )
			.ToHashSet();

		int totalAssigned = 0;
		int pendingCount = 0;
		foreach ( var task in scene.GetAllComponents<TaskObject>() )
		{
			if ( task.AssignedConnectionId == System.Guid.Empty ) continue;
			if ( !aliveConnectionIds.Contains( task.AssignedConnectionId ) ) continue;
			totalAssigned++;
			if ( !task.IsCompleted ) pendingCount++;
		}

		// Guard: don't crew-win on round start with zero assigned tasks.
		if ( totalAssigned > 0 && pendingCount == 0 )
		{
			match.EndRound( MatchOutcome.Crew, "all tasks complete" );
		}
	}
}
