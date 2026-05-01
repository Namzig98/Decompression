using System;
using System.Collections.Generic;
using System.Linq;
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

	// Force the section back to a fresh Idle state. Called by Match (C1) at
	// round start and round end so vented sections from the prior round are
	// reset before the next round begins.
	[Rpc.Host]
	public void Reset()
	{
		if ( !Networking.IsHost ) return;
		State = VentingState.Idle;
		StateEnteredAt = Time.Now;
		occupants.Clear();
	}

	private void EnterState( VentingState next )
	{
		var prev = State;
		State = next;
		StateEnteredAt = Time.Now;

		if ( prev == VentingState.Warning && next == VentingState.Venting )
		{
			OnEnterVenting();
		}
	}

	private void OnEnterVenting()
	{
		if ( !Networking.IsHost ) return;
		if ( Hatch is null )
		{
			Log.Warning( $"Section '{DisplayName}': cannot vent — Hatch not wired." );
			return;
		}

		var killedSnapshot = occupants
			.Where( p => p.IsValid() )
			.ToList();
		var hatchPos = Hatch.WorldPosition;

		foreach ( var player in killedSnapshot )
		{
			player.Kill( DeathCause.Decompression, hatchPos );
		}

		var killedIds = killedSnapshot
			.Select( p => p.OwnerConnectionId )
			.ToArray();

		BroadcastVented( killedIds );
	}

	[Rpc.Broadcast]
	private void BroadcastVented( Guid[] killedConnectionIds )
	{
		var killed = new List<Player>();
		var scene = Game.ActiveScene;
		if ( scene is not null && killedConnectionIds is not null )
		{
			var allPlayers = scene.GetAllComponents<Player>().ToList();
			foreach ( var id in killedConnectionIds )
			{
				var match = allPlayers.FirstOrDefault( p => p.OwnerConnectionId == id );
				if ( match is not null ) killed.Add( match );
			}
		}
		Vented?.Invoke( this, killed );
	}

	protected override void OnEnabled()
	{
		occupants.Clear();
	}

	// Public hooks called by SectionVolume relay components on child GameObjects
	// that hold the actual BoxCollider triggers. Host-only — clients don't
	// track occupancy.
	public void OnVolumeEnter( GameObject other )
	{
		if ( !Networking.IsHost ) return;
		var player = ResolvePlayer( other );
		if ( player is null ) return;
		occupants.Add( player );
	}

	public void OnVolumeExit( GameObject other )
	{
		if ( !Networking.IsHost ) return;
		var player = ResolvePlayer( other );
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
}
