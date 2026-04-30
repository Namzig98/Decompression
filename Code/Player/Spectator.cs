using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Spectator : Component
{
	public enum Phase { Inactive, CorpseLock, FollowingLiving }

	public Phase CurrentPhase { get; private set; } = Phase.Inactive;
	public Corpse LockedCorpse { get; private set; }

	private const float CorpseLockDuration = 5f;
	private TimeSince phaseStarted;
	private CameraComponent cachedCamera;

	private CameraComponent Camera
	{
		get
		{
			if ( cachedCamera is not null && cachedCamera.IsValid() )
				return cachedCamera;

			cachedCamera = Game.ActiveScene?
				.GetAllComponents<CameraComponent>()
				.FirstOrDefault( c => c.IsMainCamera );
			return cachedCamera;
		}
	}

	// Called from Player.OnPlayerDied on the locally-controlled player.
	// A null corpse means "joined as late spectator" (Task 18) — skip the
	// 5s corpse-lock phase and go straight to FollowingLiving.
	public void Begin( Corpse corpse )
	{
		LockedCorpse = corpse;
		if ( corpse is null )
		{
			EnterFollowingLiving();
		}
		else
		{
			EnterCorpseLock();
		}
	}

	private void EnterCorpseLock()
	{
		CurrentPhase = Phase.CorpseLock;
		phaseStarted = 0f;
	}

	private void EnterFollowingLiving()
	{
		CurrentPhase = Phase.FollowingLiving;
		phaseStarted = 0f;
		// Cycling/follow logic implemented in Task 14.
	}

	protected override void OnUpdate()
	{
		switch ( CurrentPhase )
		{
			case Phase.CorpseLock:
				UpdateCorpseLock();
				if ( phaseStarted >= CorpseLockDuration )
				{
					EnterFollowingLiving();
				}
				break;

			case Phase.FollowingLiving:
				// Filled in by Task 14.
				break;
		}
	}

	private void UpdateCorpseLock()
	{
		if ( Camera is null || LockedCorpse is null ) return;

		var corpsePos = LockedCorpse.WorldPosition;
		var cameraPos = corpsePos + Vector3.Up * 64f + Vector3.Backward * 96f;
		Camera.WorldPosition = cameraPos;
		Camera.WorldRotation = Rotation.LookAt( (corpsePos - cameraPos).Normal );
	}

	// Cycling stubs — implemented in Task 14.
	public void CycleNext() { }
	public void CyclePrevious() { }
}
