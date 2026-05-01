using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Panel : Component, Component.IPressable
{
	[Property] public Section TargetSection { get; set; }
	[Property] public ModelRenderer GlowRenderer { get; set; }
	[Property] public float HoldDuration { get; set; } = 5f;

	[Sync( SyncFlags.FromHost )] public Guid HackingConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public float HackStartTime { get; set; }

	private Color initialTint;
	private bool initialTintCaptured;

	// Host-only cache of the resolved Player whose connection is currently
	// hacking this panel. Resolved once on BeginHack; cleared on EndHack and
	// validity-check failures. Avoids a per-frame scene scan in OnUpdate.
	private Player cachedHacker;

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		var player = ResolvePlayer( e.Source?.GameObject );
		if ( player is null ) return false;
		if ( !player.IsSaboteur ) return false;

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

		HackingConnectionId = caller.Id;
		HackStartTime = Time.Now;
		cachedHacker = ResolvePlayerByConnectionId( caller.Id );
	}

	[Rpc.Host]
	public void EndHack()
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		if ( HackingConnectionId != caller.Id ) return;

		HackingConnectionId = Guid.Empty;
		cachedHacker = null;
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
			HackingConnectionId = Guid.Empty;
			cachedHacker = null;
			return;
		}

		if ( TargetSection is not null && TargetSection.State != VentingState.Idle )
		{
			HackingConnectionId = Guid.Empty;
			cachedHacker = null;
			return;
		}

		if ( Time.Now - HackStartTime >= HoldDuration )
		{
			HackingConnectionId = Guid.Empty;
			cachedHacker = null;
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
