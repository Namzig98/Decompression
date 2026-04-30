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

	protected override void OnStart()
	{
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
	}

	private void ConfigureVacuumPhysics()
	{
		if ( Physics?.PhysicsGroup is null ) return;
		foreach ( var body in Physics.PhysicsGroup.Bodies )
		{
			body.GravityEnabled = false;
			body.LinearDrag = 0.05f;
			body.AngularDrag = 0.05f;
		}
	}

	private void ConfigureNormalPhysics()
	{
		// Filled in by Task 10.
	}

	private void ApplyVacuumImpulse()
	{
		if ( Physics?.PhysicsGroup is null ) return;

		const float impulseStrength = 600f;
		foreach ( var body in Physics.PhysicsGroup.Bodies )
		{
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
