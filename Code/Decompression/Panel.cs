using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Panel : Component, Component.IPressable
{
	[Property] public Section TargetSection { get; set; }
	[Property] public ModelRenderer GlowRenderer { get; set; }
	[Property] public float HoldDuration { get; set; } = 5f;

	// State updated via [Rpc.Broadcast] BroadcastHackStart/End rather than
	// [Sync] (sync wasn't reliably propagating to non-host clients in this
	// project). HackStartTime is the LOCAL Time.Now on each client, set when
	// the broadcast is received — this also sidesteps the host/client clock
	// skew that would otherwise mis-compute glow progress.
	public Guid HackingConnectionId { get; private set; }
	public float HackStartTime { get; private set; }

	private Color initialTint;
	private bool initialTintCaptured;

	// Host-only cache of the resolved Player whose connection is currently
	// hacking this panel. Resolved once on BeginHack; cleared on EndHack and
	// validity-check failures. Avoids a per-frame scene scan in OnUpdate.
	private Player cachedHacker;

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		// Always route to host. Host validates IsSaboteur authoritatively
		// inside BeginHack — relying on the local IsSaboteur flag here was
		// racing against the host→client broadcast that propagates the role.
		BeginHack();
		return true;
	}

	void Component.IPressable.Release( Component.IPressable.Event e )
	{
		EndHack();
	}

	[Rpc.Host]
	public void BeginHack()
	{
		if ( HackingConnectionId != Guid.Empty ) return;
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		// Host-side authoritative role check.
		var hackerPlayer = ResolvePlayerByConnectionId( caller.Id );
		if ( hackerPlayer is null || !hackerPlayer.IsSaboteur )
		{
			return;
		}

		cachedHacker = hackerPlayer;
		BroadcastHackStart( caller.Id );
	}

	[Rpc.Host]
	public void EndHack()
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		if ( HackingConnectionId != caller.Id ) return;

		cachedHacker = null;
		BroadcastHackEnd();
	}

	[Rpc.Broadcast]
	private void BroadcastHackStart( Guid connectionId )
	{
		HackingConnectionId = connectionId;
		HackStartTime = Time.Now;   // local clock on each client
	}

	[Rpc.Broadcast]
	private void BroadcastHackEnd()
	{
		HackingConnectionId = Guid.Empty;
		HackStartTime = 0f;
	}

	private static Player ResolvePlayer( GameObject go )
	{
		if ( go is null ) return null;
		return go.Components.Get<Player>( includeDisabled: true )
			?? go.Root?.Components.Get<Player>( includeDisabled: true );
	}

	protected override void OnUpdate()
	{
		UpdateGlow();

		if ( !Networking.IsHost ) return;
		if ( HackingConnectionId == Guid.Empty ) return;

		// Defense in depth: drop the hack if the hacker is no longer connected,
		// no longer a saboteur, or the target section is no longer Idle.
		if ( !IsHackerStillValid() )
		{
			cachedHacker = null;
			BroadcastHackEnd();
			return;
		}

		if ( TargetSection is not null && TargetSection.State != VentingState.Idle )
		{
			cachedHacker = null;
			BroadcastHackEnd();
			return;
		}

		if ( Time.Now - HackStartTime >= HoldDuration )
		{
			cachedHacker = null;
			BroadcastHackEnd();
			TargetSection?.RequestVent();
		}
	}

	private bool IsHackerStillValid()
	{
		// Use the cached hacker reference if it's still valid; only fall back
		// to a scene scan if the cache was lost (e.g. host migration).
		if ( cachedHacker is null || !cachedHacker.IsValid() )
		{
			cachedHacker = ResolvePlayerByConnectionId( HackingConnectionId );
		}

		if ( cachedHacker is null ) return false;
		if ( !cachedHacker.IsSaboteur ) return false;

		// Cheap connection check — just confirm the connection is still in the
		// list. No second scene scan.
		return Connection.All.Any( c => c.Id == HackingConnectionId );
	}

	private static Player ResolvePlayerByConnectionId( Guid connectionId )
	{
		return Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner?.Id == connectionId );
	}

	private void UpdateGlow()
	{
		if ( GlowRenderer is null ) return;

		// Capture the renderer's idle tint once on first update so the lerp
		// can return to it cleanly when no hack is in progress.
		if ( !initialTintCaptured )
		{
			initialTint = GlowRenderer.Tint;
			initialTintCaptured = true;
		}

		float progress = 0f;
		if ( HackingConnectionId != Guid.Empty )
		{
			progress = Math.Clamp( (Time.Now - HackStartTime) / HoldDuration, 0f, 1f );
		}

		GlowRenderer.Tint = Color.Lerp( initialTint, Color.Red, progress );
	}
}
