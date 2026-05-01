using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Corpse : Component
{
	[Sync( SyncFlags.FromHost )] public DeathCause Cause { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector3 SourcePosition { get; set; }
	[Sync( SyncFlags.FromHost )] public Guid OriginalOwnerConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public bool ImpulseApplied { get; set; }

	[Property] public ModelPhysics Physics { get; set; }

	private const float DecompressionDespawnSeconds = 30f;

	private bool configurationDone;
	private TimeSince timeSinceSpawn;

	protected override void OnStart()
	{
		if ( Physics is null )
		{
			Physics = Components.Get<ModelPhysics>( includeDisabled: true );
		}
		timeSinceSpawn = 0f;
		TryConfigurePhysics();
	}

	protected override void OnUpdate()
	{
		if ( !configurationDone ) TryConfigurePhysics();

		if ( Networking.IsHost
			&& Cause == DeathCause.Decompression
			&& timeSinceSpawn >= DecompressionDespawnSeconds )
		{
			Cleanup();
		}
	}

	// ModelPhysics generates Rigidbody components on per-bone GameObjects when
	// it builds the ragdoll. Component start order between Corpse and
	// ModelPhysics on the same GameObject is not guaranteed, so retry each
	// frame until at least one ragdoll Rigidbody exists, configure it once,
	// then stop.
	private void TryConfigurePhysics()
	{
		var rigidbodies = GetRagdollRigidbodies();
		if ( rigidbodies.Count == 0 ) return;

		if ( Cause == DeathCause.Decompression )
		{
			ConfigureVacuumPhysics( rigidbodies );
			if ( Networking.IsHost && !ImpulseApplied )
			{
				ApplyVacuumImpulse( rigidbodies );
				ImpulseApplied = true;
			}
		}
		else
		{
			ConfigureNormalPhysics( rigidbodies );
		}

		Log.Info( $"Corpse configured: Cause={Cause}, Rigidbodies={rigidbodies.Count}, IsHost={Networking.IsHost}, SourcePos={SourcePosition}" );
		configurationDone = true;
	}

	private List<Rigidbody> GetRagdollRigidbodies()
	{
		return GameObject.Components
			.GetAll<Rigidbody>( FindMode.EverythingInSelfAndDescendants )
			.ToList();
	}

	private void ConfigureVacuumPhysics( List<Rigidbody> rigidbodies )
	{
		foreach ( var rb in rigidbodies )
		{
			rb.Gravity = false;
			rb.LinearDamping = 0.05f;
			rb.AngularDamping = 0.05f;
		}
	}

	private void ConfigureNormalPhysics( List<Rigidbody> rigidbodies )
	{
		foreach ( var rb in rigidbodies )
		{
			rb.Gravity = true;
			rb.LinearDamping = 0.5f;
			rb.AngularDamping = 0.5f;
		}
	}

	private void ApplyVacuumImpulse( List<Rigidbody> rigidbodies )
	{
		const float impulseStrength = 600f;
		foreach ( var rb in rigidbodies )
		{
			var pos = rb.WorldPosition;
			var offset = pos - SourcePosition;
			var direction = offset.LengthSquared > 0.0001f
				? offset.Normal
				: Vector3.Random.Normal;

			var mass = rb.PhysicsBody?.Mass ?? 1f;
			rb.ApplyImpulse( direction * impulseStrength * mass );

			var spin = new Vector3(
				Game.Random.Float( -100f, 100f ),
				Game.Random.Float( -100f, 100f ),
				Game.Random.Float( -100f, 100f )
			);
			rb.ApplyTorque( spin * mass );
		}
	}

	public void Cleanup()
	{
		if ( !Networking.IsHost ) return;
		GameObject.Destroy();
	}
}
