using System.Collections.Generic;
using Sandbox;

namespace Decompression;

public sealed class PlayerSpawner : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public List<GameObject> SpawnPoints { get; set; } = new();

	public bool RoundInProgress { get; set; }

	void Component.INetworkListener.OnActive( Connection connection )
	{
		if ( !Networking.IsHost ) return;
		if ( PlayerPrefab is null )
		{
			Log.Warning( "PlayerSpawner: PlayerPrefab is not set." );
			return;
		}

		var spawnTransform = PickSpawnTransform();
		var player = PlayerPrefab.Clone( spawnTransform, name: $"Player ({connection.DisplayName})" );
		player.NetworkSpawn( connection );

		var playerComponent = player.Components.Get<Player>();
		if ( playerComponent != null )
		{
			playerComponent.OwnerConnectionId = connection.Id;
			// Late-join behavior wired in Task 18.
		}
	}

	private Transform PickSpawnTransform()
	{
		if ( SpawnPoints.Count == 0 ) return WorldTransform;
		var pick = SpawnPoints[Game.Random.Int( 0, SpawnPoints.Count - 1 )];
		return pick.WorldTransform;
	}
}
