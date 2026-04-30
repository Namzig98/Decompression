using System;
using Sandbox;

namespace Decompression;

public sealed class Player : Component
{
	[Sync] public bool IsAlive { get; private set; } = true;
	[Sync] public Guid OwnerConnectionId { get; set; }

	public static event Action<Player, DeathCause> Died;

	[Rpc.Host]
	public void Kill( DeathCause cause, Vector3 sourcePosition )
	{
		if ( !IsAlive ) return;
		IsAlive = false;

		// Corpse spawning, controller disable, model hide, broadcast — wired in Task 8.

		Died?.Invoke( this, cause );
	}
}
