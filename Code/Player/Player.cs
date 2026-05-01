using System;
using System.Linq;
using Sandbox;

namespace Decompression;

public sealed class Player : Component
{
	[Property] public GameObject CorpsePrefab { get; set; }
	[Property] public SkinnedModelRenderer ModelRenderer { get; set; }
	[Property] public SoundEvent DeathSoundDecompression { get; set; }
	[Property] public SoundEvent DeathSoundGeneric { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsAlive { get; private set; } = true;
	[Sync( SyncFlags.FromHost )] public Guid OwnerConnectionId { get; set; }
	[Sync( SyncFlags.FromHost )] public bool IsSaboteur { get; private set; }

	// DIAGNOSTIC: revert to host-only [Sync] write, no broadcast. Tests
	// whether the per-player BroadcastIsSaboteur was disrupting client→
	// client position sync. Saboteurs may not see their local role with
	// this version, but Panel.BeginHack already validates host-side, so
	// the gameplay still works — only the visual role display might be
	// off on non-host clients during this test.
	public void SetSaboteur( bool value )
	{
		if ( !Networking.IsHost ) return;
		IsSaboteur = value;
	}

	// Host-only entry. Picks a spawn point on the host and broadcasts a
	// respawn (IsAlive + transform) to every client so:
	//   1. IsAlive flips reliably on every client without depending on
	//      [Sync(SyncFlags.FromHost)] (which has been unreliable for our
	//      scene-static and replicated objects).
	//   2. The owning client's local WorldPosition/WorldRotation write wins
	//      through the normal owner-authoritative physics sync — non-owner
	//      writes briefly set a position then get reconciled on the next
	//      sync tick from the owner.
	// Caller (Match) provides the spawn point so it can coordinate unique
	// spawn assignments across all players (avoids two players spawning at
	// the same point and colliding into each other).
	public void RespawnForNewRound( Vector3 position, Rotation rotation )
	{
		if ( !Networking.IsHost ) return;
		BroadcastRespawn( position, rotation );
	}

	[Rpc.Broadcast]
	private void BroadcastRespawn( Vector3 position, Rotation rotation )
	{
		// DIAGNOSTIC: temporarily skip the position teleport to isolate
		// whether the WorldPosition write is what's breaking client-to-client
		// sync. If sync is restored, the teleport mechanism is the issue and
		// we'll find an alternative. If still broken, the teleport isn't the
		// cause.
		IsAlive = true;

		// (position/rotation params are intentionally unused for this test)
		_ = position;
		_ = rotation;
	}

	public static event Action<Player, DeathCause> Died;

	// Called by PlayerSpawner on the host when a connection joins mid-round.
	// Marks the player dead-on-arrival (no corpse, no death event broadcast)
	// and asks the owning client's Spectator to begin in FollowingLiving
	// mode (skipping the corpse-lock phase).
	public void SpawnAsLateJoiner()
	{
		if ( !Networking.IsHost ) return;
		IsAlive = false;
		NotifyLateJoinerLocal();
	}

	[Rpc.Owner]
	private void NotifyLateJoinerLocal()
	{
		var spectator = Components.Get<Spectator>();
		spectator?.Begin( null );
	}

	[Rpc.Host]
	public void Kill( DeathCause cause, Vector3 sourcePosition )
	{
		if ( !IsAlive ) return;
		IsAlive = false;

		var corpse = SpawnCorpse( cause, sourcePosition );
		var corpseId = corpse?.GameObject?.Id ?? Guid.Empty;
		OnPlayerDied( cause, corpseId, sourcePosition );
	}

	protected override void OnUpdate()
	{
		// Mirror the synced IsAlive flag onto local presentation/input every
		// frame, on every client. Doing it imperatively in Kill() only flipped
		// flags on the host — visibility/movability didn't propagate.
		if ( ModelRenderer != null && ModelRenderer.Enabled != IsAlive )
		{
			ModelRenderer.Enabled = IsAlive;
		}

		if ( Network.IsOwner )
		{
			var controller = Components.Get<PlayerController>( includeDisabled: true );
			if ( controller != null && controller.Enabled != IsAlive )
			{
				controller.Enabled = IsAlive;
			}
		}
	}

	private Corpse SpawnCorpse( DeathCause cause, Vector3 sourcePosition )
	{
		if ( CorpsePrefab is null )
		{
			Log.Warning( "Player.Kill: CorpsePrefab not set" );
			return null;
		}

		// Lift the spawn position off the floor for Decompression deaths so the
		// ragdoll's lower bones don't initialize inside the floor geometry and
		// then fight to escape. Generic deaths spawn at the player's exact
		// position so they fall and settle naturally.
		var spawnTransform = WorldTransform;
		if ( cause == DeathCause.Decompression )
		{
			spawnTransform = spawnTransform.WithPosition( spawnTransform.Position + Vector3.Up * 40f );
		}

		var corpseGo = CorpsePrefab.Clone( spawnTransform, name: $"Corpse ({GameObject.Name})" );

		var corpse = corpseGo.Components.Get<Corpse>();
		if ( corpse is null )
		{
			Log.Warning( "Player.Kill: corpse prefab missing Corpse component" );
			corpseGo.Destroy();
			return null;
		}

		// Set the synced state BEFORE NetworkSpawn so the initial state
		// replicated to clients (and inspected by the host's OnStart) already
		// contains the correct cause and source position. Setting it after
		// NetworkSpawn races against OnStart and the empty default Cause
		// (Generic) gets read instead.
		corpse.Cause = cause;
		corpse.SourcePosition = sourcePosition;
		corpse.OriginalOwnerConnectionId = OwnerConnectionId;

		corpseGo.NetworkSpawn();

		if ( ModelRenderer != null )
		{
			var corpseRenderer = corpseGo.Components.Get<SkinnedModelRenderer>( includeDisabled: true );
			if ( corpseRenderer != null && ModelRenderer.Model != null )
			{
				corpseRenderer.WorldTransform = ModelRenderer.WorldTransform;
			}
		}

		return corpse;
	}

	[Rpc.Broadcast]
	private void OnPlayerDied( DeathCause cause, Guid corpseId, Vector3 sourcePosition )
	{
		// Fire the cross-system static event on every client so subscribers
		// like DeathHud (which only render for the local player) work without
		// caring whether their host is the one who initiated the kill.
		// Group C subscribers that need host-only logic (e.g. win-condition
		// checks) gate on Networking.IsHost themselves.
		Died?.Invoke( this, cause );

		// Cause-specific death sting plays positionally on every client so
		// dead-too-late spectators and nearby living players hear it.
		var sound = cause == DeathCause.Decompression
			? DeathSoundDecompression
			: DeathSoundGeneric;
		if ( sound is not null )
		{
			Sound.Play( sound, WorldPosition );
		}

		if ( !IsLocallyControlled() ) return;

		Corpse corpseRef = null;
		if ( corpseId != Guid.Empty )
		{
			var go = Scene.Directory?.FindByGuid( corpseId );
			corpseRef = go?.Components.Get<Corpse>();
		}

		var spectator = Components.Get<Spectator>();
		spectator?.Begin( corpseRef );
	}

	private bool IsLocallyControlled()
	{
		return Network.Owner == Connection.Local;
	}
}
