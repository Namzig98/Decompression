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

	protected override void OnEnabled()
	{
		occupants.Clear();
	}

	protected override void OnTriggerEnter( Collider other )
	{
		if ( !Networking.IsHost ) return;

		var player = ResolvePlayer( other?.GameObject );
		if ( player is null ) return;

		occupants.Add( player );
	}

	protected override void OnTriggerExit( Collider other )
	{
		if ( !Networking.IsHost ) return;

		var player = ResolvePlayer( other?.GameObject );
		if ( player is null ) return;

		occupants.Remove( player );
	}

	private static Player ResolvePlayer( GameObject go )
	{
		if ( go is null ) return null;
		return go.Components.Get<Player>( includeDisabled: true )
			?? go.Root?.Components.Get<Player>( includeDisabled: true );
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		var elapsed = Time.Now - StateEnteredAt;

		switch ( State )
		{
			case VentingState.Warning:
				if ( elapsed >= WarningDuration )
					EnterState( VentingState.Venting );
				break;

			case VentingState.Venting:
				if ( elapsed >= VacuumDuration )
					EnterState( VentingState.Sealed );
				break;

			// Idle and Sealed are stationary; no transition out.
		}
	}

	// Kill loop + Vented broadcast on Warning -> Venting — Task 12.
}
