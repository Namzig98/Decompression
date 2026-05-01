using Sandbox;

namespace Decompression;

// Relay placed alongside a BoxCollider trigger to forward enter/exit events
// to the parent Section. Allows a Section to span multiple collider volumes
// (e.g. L-shaped rooms) while the Section component itself lives on a clean
// "logical" parent GameObject.
public sealed class SectionVolume : Component, Component.ITriggerListener
{
	[Property] public Section Section { get; set; }

	void Component.ITriggerListener.OnTriggerEnter( GameObject other )
	{
		Section?.OnVolumeEnter( other );
	}

	void Component.ITriggerListener.OnTriggerExit( GameObject other )
	{
		Section?.OnVolumeExit( other );
	}
}
