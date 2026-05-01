using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Decompression;

public static class RoleAssigner
{
	// Auto-scale: 1 saboteur for 3-7 players, 2 for 8+.
	// Override (positive) wins and is clamped to player count - 1
	// (must always leave at least one crew).
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

		// Fisher-Yates shuffle.
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
