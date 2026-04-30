using System.Linq;
using Sandbox;

namespace Decompression;

public static class CorpseCleanupSignal
{
	// Despawns every Generic-cause corpse currently in the scene. Host-only.
	// Group C calls this from the emergency-button interaction. For now it
	// is also exposed via the decompv2_cleanup_corpses ConCmd for testing.
	public static void RaiseGenericCleanup()
	{
		if ( !Networking.IsHost ) return;

		var scene = Game.ActiveScene;
		if ( scene is null ) return;

		var corpses = scene.GetAllComponents<Corpse>()
			.Where( c => c.Cause == DeathCause.Generic )
			.ToList();

		foreach ( var corpse in corpses )
		{
			corpse.Cleanup();
		}
	}
}
