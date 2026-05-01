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

	// Host-side timer + completion validation in Task 6.
	// UpdateGlow in Task 7.
}
