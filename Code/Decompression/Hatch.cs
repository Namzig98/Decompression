using Sandbox;

namespace Decompression;

public sealed class Hatch : Component
{
	[Property] public Section Section { get; set; }
	[Property] public GameObject ClosedVisual { get; set; }
	[Property] public GameObject OpenBreachVisual { get; set; }
	[Property] public GameObject BlastDoorVisual { get; set; }

	// Visual swapping logic — Task 13.
}
