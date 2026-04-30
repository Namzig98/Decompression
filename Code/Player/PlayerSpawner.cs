using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Network;

namespace Decompression;

public sealed class PlayerSpawner : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public List<GameObject> SpawnPoints { get; set; } = new();

	public bool RoundInProgress { get; set; }

	protected override async Task OnLoad()
	{
		if ( Scene.IsEditor ) return;
		if ( !Networking.IsActive )
		{
			LoadingScreen.Title = "Creating Lobby";
			await Task.DelayRealtimeSeconds( 0.1f );
			Networking.CreateLobby( new LobbyConfig() );
		}
	}

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
