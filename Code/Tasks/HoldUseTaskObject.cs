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

	private Color initialTint = Color.White;
	private bool initialTintCaptured;
	private Player cachedHolder;

	protected override void OnStart()
	{
		// Capture the renderer's idle tint exactly once at scene-load time.
		// Doing this lazily in UpdateGlow risked sampling a mid-lerp value
		// if something wrote Tint before the first OnUpdate.
		if ( GlowRenderer is not null && !initialTintCaptured )
		{
			initialTint = GlowRenderer.Tint;
			initialTintCaptured = true;
		}
	}

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		// Already-complete tasks can't be re-engaged.
		if ( IsCompleted ) return false;

		// Look up the local player to gate by role.
		var localPlayer = Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner == Connection.Local );
		if ( localPlayer is null || !localPlayer.IsAlive ) return false;

		// Saboteurs can press any task — visual fake-completion (the
		// deception). Crew can only press tasks ASSIGNED to them. Crew
		// pressing E on someone else's task gets no glow, no progress,
		// no anything.
		bool canPress = localPlayer.IsSaboteur || IsAssignedToLocal;
		if ( !canPress ) return false;

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
		cachedHolder = null;
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
		if ( !initialTintCaptured ) return;   // OnStart hasn't fired yet

		Color target = initialTint;

		if ( HoldingConnectionId != Guid.Empty )
		{
			// Active hold takes priority — ramp idle → red.
			var progress = Math.Clamp( ( Time.Now - HoldStartTime ) / HoldDuration, 0f, 1f );
			target = Color.Lerp( initialTint, Color.Red, progress );
		}
		else if ( IsLocallyHovered )
		{
			// Hover (no hold) — soft yellow highlight to indicate "interactable".
			target = Color.Lerp( initialTint, Color.Yellow, 0.4f );
		}

		GlowRenderer.Tint = target;
	}
}
