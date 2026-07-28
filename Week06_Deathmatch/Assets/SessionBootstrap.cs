using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Week 08 — Session-Based Host/Join Over the Internet.
/// Replaces NetworkBootstrap.cs. Handles UGS init/anonymous sign-in, hosting
/// via Relay + a public Lobby carrying the join code, browsing open lobbies,
/// and joining via the stored Relay join code. Deliberately kept as a single
/// OnGUI script (no Canvas UI) to match the rest of the project's style.
/// </summary>
public class SessionBootstrap : MonoBehaviour
{
    private const int MaxConnections = 4; // 1 host + 3 clients; adjust as needed
    private const string RelayJoinCodeKey = "RelayJoinCode";

    // Unity Lobby Service removes a lobby from public listings if the host
    // doesn't heartbeat it periodically (documented behavior — lobbies expire
    // after ~30s of no heartbeat). 15s keeps well within that window.
    private const float HeartbeatIntervalSeconds = 15f;

    private bool _isSignedIn;
    private bool _isBusy;
    private string _statusMessage = "Signing in...";

    private List<Lobby> _lobbies = new();
    private Lobby _joinedLobby;
    private float _heartbeatTimer;

    private async void Awake()
    {
        await EnsureSignedInAsync();
    }

    private void Update()
    {
        // Only the host owns/heartbeats the lobby it created.
        if (_joinedLobby == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        _heartbeatTimer += Time.deltaTime;
        if (_heartbeatTimer >= HeartbeatIntervalSeconds)
        {
            _heartbeatTimer = 0f;
            _ = HeartbeatLobbyAsync();
        }
    }

    private async Task HeartbeatLobbyAsync()
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(_joinedLobby.Id);
        }
        catch (Exception e)
        {
            // Non-fatal — log and keep trying on the next interval rather
            // than tearing down the session over a single failed ping.
            Debug.LogWarning($"[SessionBootstrap] Heartbeat failed: {e.Message}");
        }
    }

    private async Task EnsureSignedInAsync()
    {
        try
        {
            // Gate InitializeAsync — calling it twice throws.
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            _isSignedIn = true;
            _statusMessage = $"Signed in as {AuthenticationService.Instance.PlayerId}";
        }
        catch (Exception e)
        {
            _statusMessage = $"Auth error: {e.Message}";
            Debug.LogError($"[SessionBootstrap] UGS init/auth failed: {e}");
        }
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        // Already in a session — just show status, no buttons.
        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            string mode = NetworkManager.Singleton.IsHost ? "Host"
                : NetworkManager.Singleton.IsServer ? "Server"
                : "Client";
            GUI.Label(new Rect(10, 10, 400, 24), $"Mode: {mode}");
            GUI.Label(new Rect(10, 34, 600, 24), _statusMessage);
            return;
        }

        GUI.Label(new Rect(10, 10, 600, 24), _statusMessage);

        if (!_isSignedIn)
        {
            return; // buttons appear once sign-in completes
        }

        GUI.enabled = !_isBusy;

        if (GUI.Button(new Rect(10, 40, 150, 30), "Host"))
        {
            _ = HostGameAsync();
        }

        if (GUI.Button(new Rect(170, 40, 150, 30), "Refresh Lobbies"))
        {
            _ = RefreshLobbiesAsync();
        }

        GUI.enabled = true;

        float y = 80;
        GUI.Label(new Rect(10, y, 300, 22), $"Lobbies found: {_lobbies.Count}");
        y += 24;

        foreach (var lobby in _lobbies)
        {
            GUI.Label(new Rect(10, y, 260, 24), $"{lobby.Name} ({lobby.Players.Count}/{lobby.MaxPlayers})");
            GUI.enabled = !_isBusy;
            if (GUI.Button(new Rect(280, y, 90, 24), "Join"))
            {
                _ = JoinGameAsync(lobby.Id);
            }
            GUI.enabled = true;
            y += 26;
        }
    }

    private async Task HostGameAsync()
    {
        _isBusy = true;
        _statusMessage = "Allocating relay...";
        try
        {
            // (a) allocate relay
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);

            // (b) shareable join code
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // (c) configure transport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            // (d) start hosting
            NetworkManager.Singleton.StartHost();

            _statusMessage = "Creating lobby...";

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    // Store the join code so browsing clients can reach the relay.
                    { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            string playerIdShort = AuthenticationService.Instance.PlayerId.Substring(0, 6);
            _joinedLobby = await LobbyService.Instance.CreateLobbyAsync(
                $"{playerIdShort}'s match", MaxConnections, options);

            _heartbeatTimer = 0f; // start the heartbeat clock now that we own a lobby

            _statusMessage = $"Hosting — lobby '{_joinedLobby.Name}' (code {joinCode})";
        }
        catch (Exception e)
        {
            _statusMessage = $"Host failed: {e.Message}";
            Debug.LogError($"[SessionBootstrap] Host failed: {e}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task RefreshLobbiesAsync()
    {
        _isBusy = true;
        _statusMessage = "Querying lobbies...";
        try
        {
            var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions());
            _lobbies = response.Results;
            _statusMessage = $"Found {_lobbies.Count} open lobbies.";
        }
        catch (Exception e)
        {
            _statusMessage = $"Query failed: {e.Message}";
            Debug.LogError($"[SessionBootstrap] Query lobbies failed: {e}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task JoinGameAsync(string lobbyId)
    {
        _isBusy = true;
        _statusMessage = "Joining lobby...";
        try
        {
            // (a) join the lobby
            Lobby lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);

            // (b) read the relay join code
            string joinCode = lobby.Data[RelayJoinCodeKey].Value;

            // (c) join the relay allocation
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // (d) configure transport (mirrors host) and start client
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

            NetworkManager.Singleton.StartClient();

            _joinedLobby = lobby; // joining clients don't heartbeat — only the host does (see Update)
            _statusMessage = $"Joined '{lobby.Name}'";
        }
        catch (Exception e)
        {
            _statusMessage = $"Join failed: {e.Message}";
            Debug.LogError($"[SessionBootstrap] Join failed: {e}");
        }
        finally
        {
            _isBusy = false;
        }
    }
}
