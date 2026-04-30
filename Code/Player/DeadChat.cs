using Sandbox;

namespace Decompression;

// Gates voice + text channels by alive/dead status: alive ↔ alive only,
// dead ↔ dead only. Reactive to the [Sync] IsAlive flag on the sibling
// Player component.
//
// IMPORTANT: the actual voice/text gating call is left as a TODO inside
// ApplyChannelRouting — modern s&box voice/text APIs have churned across
// revisions and a wrong guess here would silently route wrong. The
// architecture (listening for IsAlive flips and reacting once-per-change)
// is correct; only the per-platform call needs to be filled in once the
// implementer verifies what's available in their s&box revision.
public sealed class DeadChat : Component
{
	private bool? lastKnownAlive;
	private Player player;

	protected override void OnStart()
	{
		player = Components.Get<Player>();
		ApplyChannelRouting( player?.IsAlive ?? true );
		lastKnownAlive = player?.IsAlive;
	}

	protected override void OnUpdate()
	{
		if ( player is null ) return;

		if ( !lastKnownAlive.HasValue || player.IsAlive != lastKnownAlive.Value )
		{
			ApplyChannelRouting( player.IsAlive );
			lastKnownAlive = player.IsAlive;
		}
	}

	private void ApplyChannelRouting( bool isAlive )
	{
		// TODO: actual voice + text gating against current s&box API.
		//
		// The shape we want:
		//   alive players hear/read alive players
		//   dead players hear/read dead players
		//   no cross-traffic between living and dead
		//
		// In s&box, typical entry points (verify in your revision):
		//   Network.Owner.Voice.WantsToHear = (other) => other.IsAlive == this.IsAlive
		//   Or: per-connection voice channel id assigned to alive vs dead pool
		//   Or: chat event filtering via ChatBox / a global text router
		//
		// Until this is filled in, dead and alive can hear each other
		// freely. The integration test in §19 Step 8 will fail until this
		// is wired correctly.

		Log.Info( $"DeadChat[{Network.Owner?.DisplayName ?? "?"}]: routing as {(isAlive ? "alive" : "dead")}" );
	}
}
