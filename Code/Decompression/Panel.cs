using System;
using Sandbox;

namespace Decompression;

public sealed class Panel : Component
{
	[Property] public Section TargetSection { get; set; }
	[Property] public ModelRenderer GlowRenderer { get; set; }
	[Property] public float HoldDuration { get; set; } = 5f;

	[Sync( SyncFlags.FromHost )] public Guid HackingConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public float HackStartTime { get; set; }

	// Behavior added in Tasks 15-16:
	//   - IPressable Press/Release handlers
	//   - BeginHack / EndHack [Rpc.Host] methods
	//   - Host-side timer + IsSaboteur validity check
	//   - Glow rendering on every client
}
