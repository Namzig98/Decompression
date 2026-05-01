using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public abstract class TaskObject : Component
{
	[Property] public string DisplayName { get; set; } = "";
	[Property] public string LocationLabel { get; set; } = "";

	[Sync( SyncFlags.FromHost )] public Guid AssignedConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public bool IsCompleted { get; set; }

	public bool IsAssignedToLocal =>
		AssignedConnectionId != Guid.Empty
		&& AssignedConnectionId == ( Connection.Local?.Id ?? Guid.Empty );

	public bool IsAssignedAndAlive
	{
		get
		{
			if ( AssignedConnectionId == Guid.Empty ) return false;
			var assignee = Game.ActiveScene?
				.GetAllComponents<Player>()
				.FirstOrDefault( p => p.OwnerConnectionId == AssignedConnectionId );
			return assignee is not null && assignee.IsAlive;
		}
	}

	[Rpc.Host]
	protected void MarkComplete()
	{
		if ( !Networking.IsHost ) return;
		if ( IsCompleted ) return;
		IsCompleted = true;
	}

	[Rpc.Host]
	public void Reset()
	{
		if ( !Networking.IsHost ) return;
		AssignedConnectionId = Guid.Empty;
		IsCompleted = false;
		OnReset();
	}

	// Public host-side completion entry point for debug tooling. Bypasses
	// the gameplay-specific gating in subclasses — flips IsCompleted directly.
	[Rpc.Host]
	public void ForceComplete()
	{
		if ( !Networking.IsHost ) return;
		IsCompleted = true;
	}

	protected virtual void OnReset() { }
}
