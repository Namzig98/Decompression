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

	[ConCmd( "decompv2_vent_self" )]
	public static void VentSelf()
	{
		var localPlayer = Game.ActiveScene?.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.OwnerConnection == Connection.Local );

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
