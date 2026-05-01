using Sandbox;

namespace Decompression;

public sealed class Hatch : Component
{
	[Property] public Section Section { get; set; }
	[Property] public GameObject ClosedVisual { get; set; }
	[Property] public GameObject OpenBreachVisual { get; set; }
	[Property] public GameObject BlastDoorVisual { get; set; }

	private enum HatchPose { Closed, OpenBreach, BlastDoorSealed }

	private HatchPose currentPose = HatchPose.Closed;

	protected override void OnUpdate()
	{
		if ( Section is null ) return;

		var pose = Section.State switch
		{
			VentingState.Idle or VentingState.Warning => HatchPose.Closed,
			VentingState.Venting => HatchPose.OpenBreach,
			VentingState.Sealed => HatchPose.BlastDoorSealed,
			_ => HatchPose.Closed,
		};

		SetPose( pose );
	}

	private void SetPose( HatchPose pose )
	{
		if ( currentPose == pose ) return;
		currentPose = pose;

		if ( ClosedVisual is not null ) ClosedVisual.Enabled = pose == HatchPose.Closed;
		if ( OpenBreachVisual is not null ) OpenBreachVisual.Enabled = pose == HatchPose.OpenBreach;
		if ( BlastDoorVisual is not null ) BlastDoorVisual.Enabled = pose == HatchPose.BlastDoorSealed;
	}
}
