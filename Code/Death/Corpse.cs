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
		// Filled in by Task 9.
	}

	private void ConfigureNormalPhysics()
	{
		// Filled in by Task 10.
	}

	private void ApplyVacuumImpulse()
	{
		// Filled in by Task 9.
	}

	public void Cleanup()
	{
		if ( !Networking.IsHost ) return;
		GameObject.Destroy();
	}
}
