using System.Linq;
using Sandbox;

namespace Decompression;

public static class DebugCommands
{
	[ConCmd( "decompv2_kill_self" )]
	public static void KillSelf()
	{
		var localPlayer = Game.ActiveScene?.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner == Connection.Local );

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
			.FirstOrDefault( p => p.Network.Owner?.DisplayName == connectionDisplayName );

		if ( target is null )
		{
			Log.Warning( $"decompv2_kill: no player named '{connectionDisplayName}'" );
			return;
		}

		target.Kill( cause, Vector3.Zero );
	}

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

	[ConCmd( "decompv2_complete_my_tasks" )]
	public static void CompleteMyTasks()
	{
		var localId = Connection.Local?.Id ?? System.Guid.Empty;
		if ( localId == System.Guid.Empty )
		{
			Log.Warning( "decompv2_complete_my_tasks: no local connection" );
			return;
		}

		var tasks = Game.ActiveScene?.GetAllComponents<TaskObject>()
			.Where( t => t.AssignedConnectionId == localId )
			.ToList();

		if ( tasks is null || tasks.Count == 0 )
		{
			Log.Warning( "decompv2_complete_my_tasks: no tasks assigned to local player" );
			return;
		}

		foreach ( var task in tasks )
		{
			task.ForceComplete();
		}
		Log.Info( $"decompv2_complete_my_tasks: requested completion of {tasks.Count} task(s)" );
	}

	[ConCmd( "decompv2_list_tasks" )]
	public static void ListTasks()
	{
		var scene = Game.ActiveScene;
		if ( scene is null )
		{
			Log.Warning( "decompv2_list_tasks: no active scene" );
			return;
		}

		var tasks = scene.GetAllComponents<TaskObject>().ToList();
		if ( tasks.Count == 0 )
		{
			Log.Info( "decompv2_list_tasks: no TaskObjects in scene" );
			return;
		}

		var players = scene.GetAllComponents<Player>().ToList();
		foreach ( var task in tasks )
		{
			var assigneeName = task.AssignedConnectionId == System.Guid.Empty
				? "(unassigned)"
				: players
					.FirstOrDefault( p => p.OwnerConnectionId == task.AssignedConnectionId )
					?.Network.Owner?.DisplayName ?? "(unknown)";
			var status = task.IsCompleted ? "DONE" : "pending";
			Log.Info( $"  [{task.LocationLabel}] {task.DisplayName} → {assigneeName} ({status})" );
		}
	}

	[ConCmd( "decompv2_complete_task" )]
	public static void CompleteTask( string displayName )
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "decompv2_complete_task: host only" );
			return;
		}

		var task = Game.ActiveScene?.GetAllComponents<TaskObject>()
			.FirstOrDefault( t => t.DisplayName == displayName );

		if ( task is null )
		{
			Log.Warning( $"decompv2_complete_task: no TaskObject named '{displayName}'" );
			return;
		}

		task.ForceComplete();
		Log.Info( $"decompv2_complete_task: '{displayName}' marked complete" );
	}

	[ConCmd( "decompv2_complete_hack" )]
	public static void CompleteHack()
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "decompv2_complete_hack: host only" );
			return;
		}

		var panel = Game.ActiveScene?.GetAllComponents<Panel>()
			.FirstOrDefault( p => p.HackingConnectionId != System.Guid.Empty );

		if ( panel is null )
		{
			Log.Warning( "decompv2_complete_hack: no panel is being hacked" );
			return;
		}

		// Force-vent the panel's target section directly. (The previous
		// approach of rewinding HackStartTime no longer works since the
		// property is private-set and synced via broadcast.)
		panel.TargetSection?.RequestVent();
	}

	[ConCmd( "decompv2_section_state" )]
	public static void SectionState( string sectionDisplayName )
	{
		var section = Game.ActiveScene?.GetAllComponents<Section>()
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

	[ConCmd( "decompv2_request_vent" )]
	public static void RequestVent( string sectionDisplayName )
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "decompv2_request_vent: host only" );
			return;
		}

		var section = Game.ActiveScene?.GetAllComponents<Section>()
			.FirstOrDefault( s => s.DisplayName == sectionDisplayName );

		if ( section is null )
		{
			Log.Warning( $"decompv2_request_vent: no section named '{sectionDisplayName}'" );
			return;
		}

		section.RequestVent();
	}

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

	[ConCmd( "decompv2_set_saboteur" )]
	public static void SetSaboteur( string connectionDisplayName, bool value )
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "decompv2_set_saboteur: host only" );
			return;
		}

		var target = Game.ActiveScene?.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner?.DisplayName == connectionDisplayName );

		if ( target is null )
		{
			Log.Warning( $"decompv2_set_saboteur: no player named '{connectionDisplayName}'" );
			return;
		}

		target.SetSaboteur( value );
		Log.Info( $"{connectionDisplayName}.IsSaboteur = {value}" );
	}

	[ConCmd( "decompv2_round_in_progress" )]
	public static void SetRoundInProgress( bool value )
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "decompv2_round_in_progress: host only" );
			return;
		}
		var spawner = Game.ActiveScene?.GetAllComponents<PlayerSpawner>().FirstOrDefault();
		if ( spawner is null )
		{
			Log.Warning( "decompv2_round_in_progress: no PlayerSpawner in scene" );
			return;
		}
		spawner.RoundInProgress = value;
		Log.Info( $"PlayerSpawner.RoundInProgress = {value}" );
	}

	[ConCmd( "decompv2_vent_self" )]
	public static void VentSelf()
	{
		var localPlayer = Game.ActiveScene?.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner == Connection.Local );

		if ( localPlayer is null )
		{
			Log.Warning( "decompv2_vent_self: no local player found" );
			return;
		}

		// Place the synthetic hatch source 100u below the player so the
		// impulse direction is meaningful (corpse pushed upward, away from
		// the "breach"). Real hatches in group A will pass their own position.
		var hatchPos = localPlayer.WorldPosition + Vector3.Down * 100f;
		localPlayer.Kill( DeathCause.Decompression, hatchPos );
	}
}
