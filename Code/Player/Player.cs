using System;
using Sandbox;

namespace Decompression;

public sealed class Player : Component
{
	[Property] public GameObject CorpsePrefab { get; set; }
	[Property] public SkinnedModelRenderer ModelRenderer { get; set; }

	[Sync] public bool IsAlive { get; private set; } = true;
	[Sync] public Guid OwnerConnectionId { get; set; }

	public static event Action<Player, DeathCause> Died;

	[Rpc.Host]
	public void Kill( DeathCause cause, Vector3 sourcePosition )
	{
		if ( !IsAlive ) return;
		IsAlive = false;

		var corpse = SpawnCorpse( cause, sourcePosition );
		var corpseId = corpse?.GameObject?.Id ?? Guid.Empty;
		OnPlayerDied( cause, corpseId, sourcePosition );

		Died?.Invoke( this, cause );
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

		var corpseGo = CorpsePrefab.Clone( WorldTransform, name: $"Corpse ({GameObject.Name})" );
		corpseGo.NetworkSpawn();

		var corpse = corpseGo.Components.Get<Corpse>();
		if ( corpse is null )
		{
			Log.Warning( "Player.Kill: corpse prefab missing Corpse component" );
			return null;
		}

		corpse.Cause = cause;
		corpse.SourcePosition = sourcePosition;
		corpse.OriginalOwnerConnectionId = OwnerConnectionId;

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
		// Local effects (HUD, sound, spectator) wired in later tasks.
	}
}
