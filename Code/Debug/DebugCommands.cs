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
