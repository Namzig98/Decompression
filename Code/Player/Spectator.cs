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
	private Player followedPlayer;

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
		// If the local player is alive, the Spectator should not be running —
		// PlayerController owns the camera. This handles the round-restart
		// case where the player respawns alive but Spectator hasn't been
		// reset by anyone else.
		var owner = Components.Get<Player>();
		if ( owner is not null && owner.IsAlive )
		{
			if ( CurrentPhase != Phase.Inactive )
			{
				CurrentPhase = Phase.Inactive;
				LockedCorpse = null;
				followedPlayer = null;
			}
			return;
		}

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
				if ( Input.Pressed( "attack1" ) ) CycleNext();
				if ( Input.Pressed( "attack2" ) ) CyclePrevious();
				UpdateFollowingLiving();
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

	private void UpdateFollowingLiving()
	{
		if ( Camera is null ) return;

		// If our followed target died (or was destroyed), drop them and pick again.
		if ( followedPlayer is not null && (!followedPlayer.IsValid() || !followedPlayer.IsAlive) )
		{
			followedPlayer = null;
		}

		if ( followedPlayer is null )
		{
			followedPlayer = PickFirstLiving();
		}

		if ( followedPlayer is null )
		{
			// No living players left — fall back to a map-overview shot.
			Camera.WorldPosition = Vector3.Up * 1500f;
			Camera.WorldRotation = Rotation.LookAt( Vector3.Down );
			return;
		}

		// Third-person over-shoulder behind the followed player.
		var targetPos = followedPlayer.WorldPosition + Vector3.Up * 64f;
		var followForward = followedPlayer.WorldRotation.Forward;
		var cameraPos = targetPos + (-followForward) * 128f + Vector3.Up * 32f;
		Camera.WorldPosition = cameraPos;
		Camera.WorldRotation = Rotation.LookAt( (targetPos - cameraPos).Normal );
	}

	private Player PickFirstLiving()
	{
		return Game.ActiveScene?
			.GetAllComponents<Player>()
			.FirstOrDefault( p => p.IsAlive );
	}

	public void CycleNext()
	{
		if ( CurrentPhase != Phase.FollowingLiving ) return;
		CycleBy( +1 );
	}

	public void CyclePrevious()
	{
		if ( CurrentPhase != Phase.FollowingLiving ) return;
		CycleBy( -1 );
	}

	private void CycleBy( int direction )
	{
		var living = Game.ActiveScene?
			.GetAllComponents<Player>()
			.Where( p => p.IsAlive )
			.ToList();

		if ( living is null || living.Count == 0 )
		{
			followedPlayer = null;
			return;
		}

		var idx = followedPlayer is not null ? living.IndexOf( followedPlayer ) : -1;
		idx = (idx + direction + living.Count) % living.Count;
		followedPlayer = living[idx];
	}
}
