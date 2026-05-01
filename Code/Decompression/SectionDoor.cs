using Sandbox;

namespace Decompression;

public sealed class SectionDoor : Component
{
	[Property] public Section Section { get; set; }
	[Property] public GameObject DoorMesh { get; set; }
	[Property] public Vector3 OpenLocalOffset { get; set; } = Vector3.Up * 100f;

	// ~0.4s open/close transition.
	private const float LerpSpeed = 1f / 0.4f;

	protected override void OnUpdate()
	{
		if ( DoorMesh is null || Section is null ) return;

		// Closed only during Venting; open in all other states (including
		// Sealed so the section is traversable again after the blast door).
		var shouldBeClosed = Section.State == VentingState.Venting;
		var targetOffset = shouldBeClosed ? Vector3.Zero : OpenLocalOffset;

		DoorMesh.LocalPosition = Vector3.Lerp(
			DoorMesh.LocalPosition,
			targetOffset,
			Time.Delta * LerpSpeed
		);
	}
}
