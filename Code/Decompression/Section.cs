using System;
using System.Collections.Generic;
using Sandbox;

namespace Decompression;

public sealed class Section : Component
{
	[Property] public string DisplayName { get; set; } = "";
	[Property] public Hatch Hatch { get; set; }
	[Property] public List<SectionDoor> Doors { get; set; } = new();
	[Property] public float WarningDuration { get; set; } = 4f;
	[Property] public float VacuumDuration { get; set; } = 10f;

	[Sync( SyncFlags.FromHost )] public VentingState State { get; private set; } = VentingState.Idle;
	[Sync( SyncFlags.FromHost )] public float StateEnteredAt { get; private set; }

	public IReadOnlyCollection<Player> Occupants => occupants;
	private readonly HashSet<Player> occupants = new();

	public static event Action<Section, IReadOnlyList<Player>> Vented;

	[Rpc.Host]
	public void RequestVent()
	{
		if ( State != VentingState.Idle ) return;
		EnterState( VentingState.Warning );
	}

	private void EnterState( VentingState next )
	{
		State = next;
		StateEnteredAt = Time.Now;
	}

	// Occupancy tracking — Task 9.
	// State-machine OnUpdate transitions — Task 10.
	// Kill loop + Vented broadcast on Warning -> Venting — Task 12.
}
