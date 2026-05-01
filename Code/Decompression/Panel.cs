using System;
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

	// Host-side timer + saboteur validity check + glow render — Task 16.
}
