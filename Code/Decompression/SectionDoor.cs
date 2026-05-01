using Sandbox;

namespace Decompression;

public sealed class SectionDoor : Component
{
	[Property] public Section Section { get; set; }
	[Property] public GameObject DoorMesh { get; set; }
	[Property] public Vector3 OpenLocalOffset { get; set; } = Vector3.Up * 100f;

	// Open/close lerp — Task 14.
}
