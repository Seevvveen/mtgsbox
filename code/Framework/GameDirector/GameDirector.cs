#nullable enable

using Sandbox.Classes.Cards;
using Sandbox.Framework.GameInfo;
using Sandbox.Framework.Table;
using Sandbox.Framework.UI;
using Sandbox.Network;
namespace Sandbox.Framework;

/// <summary>
/// CONTROLS THE LIFETIMES OF MATCH OBJECTS
/// </summary>
public sealed class GameDirector : Component, Component.INetworkListener
{
	[Property] public bool IsMultiplayer { get; set; } = true;
	[Property] public string LobbyName { get; set; } = "Magic: The Gathering";
	[Property, Sync( SyncFlags.FromHost )] public MTGFormat? FormatFile { get; set; }

	public Match? ActiveMatch => Scene.Get<Match>();

	void INetworkListener.OnActive( Connection channel ) => ActiveMatch?.AddPlayer( channel );
	void INetworkListener.OnDisconnected( Connection channel ) => ActiveMatch?.RemovePlayer( channel );


	protected override void OnStart()
	{
		base.OnStart();

		if ( Application.IsHeadless )
			return;

		GameMenu? menu = Scene.Components.Get<GameMenu>( FindMode.EverythingInDescendants );

		if ( menu is null )
		{
			ScreenPanel? screenPanel = Scene.Components.Get<ScreenPanel>( FindMode.EverythingInDescendants );
			GameObject screen;

			if ( screenPanel is null )
			{
				screen = new GameObject( true, "Initial Game Screen" );
				screen.Components.Create<ScreenPanel>();
			}
			else
			{
				screen = screenPanel.GameObject;
			}

			screen.Enabled = true;
			menu = screen.Components.Create<GameMenu>();
		}

		menu.GameObject.Enabled = true;
		menu.Enabled = true;

		InGameStatePanel? gameStatePanel = Scene.Components.Get<InGameStatePanel>( FindMode.EverythingInDescendants );

		if ( gameStatePanel is null )
			gameStatePanel = menu.GameObject.Components.Create<InGameStatePanel>();

		gameStatePanel.GameObject.Enabled = true;
		gameStatePanel.Enabled = true;
		GetOrAddComponent<CardHover>();

		if ( Scene.Camera is { } camera && camera.GameObject.Components.Get<TableCamera>() is null )
			camera.GameObject.Components.Create<TableCamera>();

		Mouse.Visibility = MouseVisibility.Visible;
	}

	/// <summary>
	///     Opens a public lobby and creates the match players will ready up for.
	/// </summary>
	public void StartLobby()
	{
		if ( !Networking.IsHost )
			return;

		if ( ActiveMatch is not null )
			return;

		if ( FormatFile is null || FormatFile.Prefab is null )
		{
			Log.Warning( "A format and match prefab must be assigned before starting a lobby." );
			return;
		}

		if ( IsMultiplayer && !Networking.IsActive )
		{
			Networking.CreateLobby(
				new LobbyConfig
				{
					Name = LobbyName,
					Privacy = LobbyPrivacy.Public,
					AutoSwitchToBestHost = false,
					DestroyWhenHostLeaves = true
				}
			);
		}

		if ( SpawnMatch( FormatFile ) is not { } match )
			return;

		if ( IsMultiplayer )
		{
			Networking.ServerName = LobbyName;
			Networking.SetData( "state", "joinable" );
		}

		// OnActive normally adds the host. Keeping this explicit also makes the
		// lobby work in local/offline sessions where no network callback is raised.
		match.AddPlayer( Connection.Local );

		if ( !IsMultiplayer )
			match.Begin();
	}


	/// <summary>
	/// Take a Format and Spawn it
	/// </summary>
	private Match? SpawnMatch(MTGFormat format)
	{
		var go = GameObject.Clone( format.Prefab );

		if ( go.Components.Get<Match>() is not { } match )
		{
			Log.Warning( "Format prefab does not contain a Match component." );
			go.Destroy();
			return null;
		}

		go.NetworkSpawn();
		return match;
	}


	public void EndMatch()
	{
		if ( !Networking.IsHost )
			return;

		if ( ActiveMatch is not { } match )
			return;

		match.Conclude();
		match.GameObject.Destroy();

	}


	public void LeaveGame()
	{
		if ( Networking.IsHost )
		{
			EndMatch();
			return;
		}

		Networking.Disconnect();

		if ( ActiveMatch is not { } match )
			return;

		match.GameObject.Destroy();
	}
}
