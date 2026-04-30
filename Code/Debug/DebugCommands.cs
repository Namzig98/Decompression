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
