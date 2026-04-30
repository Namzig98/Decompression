using System;
using Sandbox;

namespace Decompression;

public sealed class Corpse : Component
{
	[Sync] public DeathCause Cause { get; set; }
	[Sync] public Vector3 SourcePosition { get; set; }
	[Sync] public Guid OriginalOwnerConnectionId { get; set; }
	[Sync] public bool ImpulseApplied { get; set; }

	[Property] public ModelPhysics Physics { get; set; }

	private bool configurationDone;

	protected override void OnStart()
	{
		if ( Physics is null )
		{
			Physics = Components.Get<ModelPhysics>( includeDisabled: true );
		}
		TryConfigurePhysics();
	}

	protected override void OnUpdate()
	{
		if ( !configurationDone ) TryConfigurePhysics();
	}

	// ModelPhysics may not have generated the ragdoll bodies by the time
	// our OnStart fires (component start order is not guaranteed). Keep
	// retrying each frame until the body list is non-empty, then run the
	// cause-specific configuration once and stop.
	private void TryConfigurePhysics()
	{
		if ( Physics is null ) return;

		var bodyCount = 0;
		foreach ( var _ in Physics.Bodies ) bodyCount++;
		if ( bodyCount == 0 ) return;

		if ( Cause == DeathCause.Decompression )
		{
			ConfigureVacuumPhysics();
			if ( Networking.IsHost && !ImpulseApplied )
			{
				ApplyVacuumImpulse();
				ImpulseApplied = true;
			}
		}
		else
		{
			ConfigureNormalPhysics();
		}

		Log.Info( $"Corpse configured: Cause={Cause}, Bodies={bodyCount}, IsHost={Networking.IsHost}, SourcePos={SourcePosition}" );
		configurationDone = true;
	}

	private void ConfigureVacuumPhysics()
	{
		if ( Physics is null ) return;
		foreach ( var modelBody in Physics.Bodies )
		{
			var body = modelBody.PhysicsBody;
			if ( body is null ) continue;
			body.GravityEnabled = false;
			body.LinearDamping = 0.05f;
			body.AngularDamping = 0.05f;
		}
	}

	private void ConfigureNormalPhysics()
	{
		// Filled in by Task 10.
	}

	private void ApplyVacuumImpulse()
	{
		if ( Physics is null ) return;

		const float impulseStrength = 600f;
		foreach ( var modelBody in Physics.Bodies )
		{
			var body = modelBody.PhysicsBody;
			if ( body is null ) continue;

			var offset = body.Position - SourcePosition;
			var direction = offset.LengthSquared > 0.0001f
				? offset.Normal
				: Vector3.Random.Normal;

			body.ApplyImpulse( direction * impulseStrength * body.Mass );

			var spin = new Vector3(
				Game.Random.Float( -100f, 100f ),
				Game.Random.Float( -100f, 100f ),
				Game.Random.Float( -100f, 100f )
			);
			body.ApplyAngularImpulse( spin * body.Mass );
		}
	}

	public void Cleanup()
	{
		if ( !Networking.IsHost ) return;
		GameObject.Destroy();
	}
}
