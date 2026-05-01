using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class HoldUseTaskObject : TaskObject, Component.IPressable
{
	[Property] public float HoldDuration { get; set; } = 3f;
	[Property] public ModelRenderer GlowRenderer { get; set; }

	// State updated via [Rpc.Broadcast] BroadcastHoldStart/End. HoldStartTime
	// is the LOCAL Time.Now on each client (set inside the broadcast handler)
	// so glow progress doesn't suffer from host/client clock skew.
	public Guid HoldingConnectionId { get; private set; }
	public float HoldStartTime { get; private set; }

	private Color initialTint;
	private bool initialTintCaptured;
	private Player cachedHolder;

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		// Always route to host. Host validates and decides whether the hold
		// counts (assigned crew = real, others = visual-only fake).
		BeginHold();
		return true;
	}

	void Component.IPressable.Release( Component.IPressable.Event e )
	{
		EndHold();
	}

	[Rpc.Host]
	public void BeginHold()
	{
		if ( HoldingConnectionId != Guid.Empty ) return;
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		cachedHolder = ResolvePlayerByConnectionId( caller.Id );
		BroadcastHoldStart( caller.Id );
	}

	[Rpc.Host]
	public void EndHold()
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		if ( HoldingConnectionId != caller.Id ) return;

		cachedHolder = null;
		BroadcastHoldEnd();
	}

	[Rpc.Broadcast]
	private void BroadcastHoldStart( Guid connectionId )
	{
		HoldingConnectionId = connectionId;
		HoldStartTime = Time.Now;   // LOCAL clock on each client
	}

	[Rpc.Broadcast]
	private void BroadcastHoldEnd()
	{
		HoldingConnectionId = Guid.Empty;
		HoldStartTime = 0f;
	}

	protected override void OnReset()
	{
		if ( Networking.IsHost && HoldingConnectionId != Guid.Empty )
		{
			BroadcastHoldEnd();
		}
	}

	private static Player ResolvePlayerByConnectionId( Guid connectionId )
	{
		return Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.OwnerConnectionId == connectionId );
	}

	protected override void OnUpdate()
	{
		UpdateGlow();

		if ( !Networking.IsHost ) return;
		if ( HoldingConnectionId == Guid.Empty ) return;

		// Defense in depth: drop the hold if the holder is no longer connected
		// or no longer alive.
		if ( !IsHolderStillValid() )
		{
			cachedHolder = null;
			BroadcastHoldEnd();
			return;
		}

		// Completion check.
		var elapsed = Time.Now - HoldStartTime;
		if ( elapsed >= HoldDuration )
		{
			var holder = cachedHolder;

			// Real completion ONLY when:
			//   - holder still alive (already verified by validity check)
			//   - holder is NOT a saboteur
			//   - holder is the assigned crew for THIS task
			// All other cases: visual-only fake completion (glow snaps but no
			// MarkComplete fires).
			bool shouldComplete =
				holder is not null
				&& holder.IsAlive
				&& !holder.IsSaboteur
				&& AssignedConnectionId == HoldingConnectionId;

			if ( shouldComplete )
			{
				MarkComplete();
			}

			cachedHolder = null;
			BroadcastHoldEnd();
		}
	}

	private bool IsHolderStillValid()
	{
		if ( cachedHolder is null || !cachedHolder.IsValid() )
		{
			cachedHolder = ResolvePlayerByConnectionId( HoldingConnectionId );
		}
		if ( cachedHolder is null ) return false;
		if ( !cachedHolder.IsAlive ) return false;

		return Connection.All.Any( c => c.Id == HoldingConnectionId );
	}

	private void UpdateGlow()
	{
		if ( GlowRenderer is null ) return;

		// Capture the renderer's idle tint once on first update so the lerp
		// returns to it cleanly when no hold is in progress.
		if ( !initialTintCaptured )
		{
			initialTint = GlowRenderer.Tint;
			initialTintCaptured = true;
		}

		float progress = 0f;
		if ( HoldingConnectionId != Guid.Empty )
		{
			progress = Math.Clamp( ( Time.Now - HoldStartTime ) / HoldDuration, 0f, 1f );
		}

		GlowRenderer.Tint = Color.Lerp( initialTint, Color.Red, progress );
	}
}
