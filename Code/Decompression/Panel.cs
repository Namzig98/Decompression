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
	}

	[Rpc.Host]
	public void EndHack()
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		if ( HackingConnectionId != caller.Id ) return;

		HackingConnectionId = Guid.Empty;
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
			return;
		}

		if ( TargetSection is not null && TargetSection.State != VentingState.Idle )
		{
			HackingConnectionId = Guid.Empty;
			return;
		}

		if ( Time.Now - HackStartTime >= HoldDuration )
		{
			HackingConnectionId = Guid.Empty;
			TargetSection?.RequestVent();
		}
	}

	private bool IsHackerStillValid()
	{
		if ( !Connection.All.Any( c => c.Id == HackingConnectionId ) ) return false;

		var hackerPlayer = Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.Network.Owner?.Id == HackingConnectionId );

		return hackerPlayer is not null && hackerPlayer.IsSaboteur;
	}

	private void UpdateGlow()
	{
		if ( GlowRenderer is null ) return;

		float progress = 0f;
		if ( HackingConnectionId != Guid.Empty )
		{
			progress = Math.Clamp( (Time.Now - HackStartTime) / HoldDuration, 0f, 1f );
		}

		GlowRenderer.Tint = new Color( 1f, 0f, 0f, progress );
	}
}
