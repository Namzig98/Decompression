using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Decompression;

public static class TaskAssigner
{
	// Assigns up to `tasksPerCrew` TaskObjects to each non-saboteur alive
	// player. Pre-condition: caller is host. Saboteurs and dead players get
	// no tasks. If pool is too small for everyone to get the full count,
	// each crew gets as many as available (round-robin) and a warning logs.
	public static void Assign(
		IEnumerable<Player> alivePlayers,
		IEnumerable<TaskObject> availableTasks,
		int tasksPerCrew )
	{
		var crew = alivePlayers
			.Where( p => p.IsValid() && p.IsAlive && !p.IsSaboteur )
			.ToList();
		var pool = availableTasks
			.Where( t => t.IsValid() )
			.ToList();

		if ( crew.Count == 0 )
		{
			Log.Warning( "TaskAssigner.Assign: no alive crew, skipping" );
			return;
		}

		// Fisher-Yates shuffle so assignment isn't deterministic round-to-round.
		for ( int i = pool.Count - 1; i > 0; i-- )
		{
			int j = Game.Random.Int( 0, i );
			(pool[i], pool[j]) = (pool[j], pool[i]);
		}

		var totalNeeded = crew.Count * tasksPerCrew;
		if ( pool.Count < totalNeeded )
		{
			Log.Warning( $"TaskAssigner.Assign: only {pool.Count} tasks for {totalNeeded} needed " +
				$"(crew={crew.Count} × tasksPerCrew={tasksPerCrew}). Each crew will get fewer than requested." );
		}

		// Hand out tasks in round-robin order so under-supply is fairly
		// distributed across crew. AssignTo broadcasts the assignment to all
		// clients (works around [Sync] propagation flakiness).
		var index = 0;
		for ( int i = 0; i < tasksPerCrew; i++ )
		{
			foreach ( var player in crew )
			{
				if ( index >= pool.Count ) return;
				pool[index].AssignTo( player.OwnerConnectionId );
				index++;
			}
		}
	}

	public static void ClearAll( IEnumerable<TaskObject> allTasks )
	{
		foreach ( var task in allTasks )
		{
			if ( task.IsValid() ) task.Reset();
		}
	}
}
