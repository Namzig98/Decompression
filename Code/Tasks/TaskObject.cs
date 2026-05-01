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

	// Local-only flag set by TaskHoverOutline when this task is currently
	// under the local player's interact reticle. NOT synced — each client
	// tracks their own hover state. Subclasses use this for hover visuals.
	public bool IsLocallyHovered { get; set; }

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

	// State transitions go through [Rpc.Broadcast] writers so they propagate
	// reliably in editor multi-instance (where [Sync(SyncFlags.FromHost)]
	// has been observed to be unreliable). Host-only entry points wrap them.

	protected void MarkComplete()
	{
		if ( !Networking.IsHost ) return;
		if ( IsCompleted ) return;
		BroadcastCompleted();
	}

	public void Reset()
	{
		if ( !Networking.IsHost ) return;
		// OnReset runs first so subclasses can read pre-reset state
		// (e.g. HoldingConnectionId) before BroadcastReset clears the
		// synced fields.
		OnReset();
		BroadcastReset();
	}

	// Public host-side completion entry point for debug tooling. Bypasses
	// the gameplay-specific gating in subclasses — flips IsCompleted directly.
	public void ForceComplete()
	{
		if ( !Networking.IsHost ) return;
		BroadcastCompleted();
	}

	// Public host-side assignment entry point. TaskAssigner uses this.
	public void AssignTo( Guid connectionId )
	{
		if ( !Networking.IsHost ) return;
		BroadcastAssigned( connectionId );
	}

	[Rpc.Broadcast]
	private void BroadcastAssigned( Guid connectionId )
	{
		AssignedConnectionId = connectionId;
	}

	[Rpc.Broadcast]
	private void BroadcastCompleted()
	{
		IsCompleted = true;
	}

	[Rpc.Broadcast]
	private void BroadcastReset()
	{
		AssignedConnectionId = Guid.Empty;
		IsCompleted = false;
	}

	protected virtual void OnReset() { }
}
