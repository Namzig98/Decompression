using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class HoldUseTaskObject : TaskObject
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

	// IPressable handlers + Begin/End RPCs added in Task 5.
	// Host-side completion + validity checks added in Task 6.
	// UpdateGlow added in Task 7.

	protected override void OnReset()
	{
		// If a hold is in flight when the round resets, broadcast end so all
		// clients clear their glow.
		if ( Networking.IsHost && HoldingConnectionId != Guid.Empty )
		{
			BroadcastHoldEnd();
		}
	}

	[Rpc.Broadcast]
	private void BroadcastHoldEnd()
	{
		HoldingConnectionId = Guid.Empty;
		HoldStartTime = 0f;
	}
}
