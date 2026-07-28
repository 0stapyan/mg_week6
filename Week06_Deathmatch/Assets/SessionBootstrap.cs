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
/// Week 08 — Session-Based Host/Join Over the Internet (UGS Relay + Lobby).
/// Week 09 addition — a Direct Connect field, so a client build can also
/// connect straight to a dedicated server's IP:port instead of going through
/// Relay (that's the whole point of Week 09's test).
///
/// All logic here is guarded with #if !UNITY_SERVER inside method bodies —
/// a dedicated server build has no business signing into UGS or drawing
/// connect buttons; that's ServerBootstrap's job instead. Guarding inside
/// the methods (not around the class) keeps this component safely attachable
/// to the same GameObject in every build target.
/// </summary>
public class SessionBootstrap : MonoBehaviour
{
    private const int MaxConnections = 4; // 1 host + 3 clients; adjust as needed
    private const string RelayJoinCodeKey = "RelayJoinCode";
    private const float HeartbeatIntervalSeconds = 15f;

    private bool _isSignedIn;
    private bool _isBusy;
    private string _statusMessage = "Signing in...";

    private List<Lobby> _lobbies = new();
    private Lobby _joinedLobby;
    private float _heartbeatTimer;

    // Direct Connect (Week 09) fields
    private string _directIp = "127.0.0.1";
    private string _directPort = "7777";

    private async void Awake()
    {
#if !UNITY_SERVER
        await EnsureSignedInAsync();
#endif
    }

    private void Update()
    {
#if !UNITY_SERVER
        // Only the host owns/heartbeats the lobby it created.
        if (_joinedLobby == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        _heartbeatTimer += Time.deltaTime;
        if (_heartbeatTimer >= HeartbeatIntervalSeconds)
        {
            _heartbeatTimer = 0f;
            _ = HeartbeatLobbyAsync();
        }
#endif
    }

    private async Task HeartbeatLobbyAsync()
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(_joinedLobby.Id);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionBootstrap] Heartbeat failed: {e.Message}");
        }
    }

    private async Task EnsureSignedInAsync()
    {
        try
        {
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
#if !UNITY_SERVER
        if (NetworkManager.Singleton == null) return;

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

        // --- Direct Connect (Week 09): join a dedicated server by IP:port,
        // bypassing UGS Relay/Lobby entirely. Doesn't require sign-in. ---
        GUI.Label(new Rect(10, 40, 60, 24), "IP:");
        _directIp = GUI.TextField(new Rect(70, 40, 140, 24), _directIp);
        GUI.Label(new Rect(220, 40, 45, 24), "Port:");
        _directPort = GUI.TextField(new Rect(265, 40, 70, 24), _directPort);
        if (GUI.Button(new Rect(345, 40, 130, 24), "Direct Connect"))
        {
            ConnectDirect();
        }

        if (!_isSignedIn)
        {
            return; // Relay/Lobby buttons appear once sign-in completes
        }

        GUI.enabled = !_isBusy;

        if (GUI.Button(new Rect(10, 74, 150, 30), "Host (Relay)"))
        {
            _ = HostGameAsync();
        }

        if (GUI.Button(new Rect(170, 74, 150, 30), "Refresh Lobbies"))
        {
            _ = RefreshLobbiesAsync();
        }

        GUI.enabled = true;

        float y = 114;
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
#endif
    }

    private void ConnectDirect()
    {
        if (!ushort.TryParse(_directPort, out var port))
        {
            _statusMessage = "Invalid port.";
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(_directIp, port);
        NetworkManager.Singleton.StartClient();
        _statusMessage = $"Connecting directly to {_directIp}:{port}...";
    }

    private async Task HostGameAsync()
    {
        _isBusy = true;
        _statusMessage = "Allocating relay...";
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            NetworkManager.Singleton.StartHost();

            _statusMessage = "Creating lobby...";

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            string playerIdShort = AuthenticationService.Instance.PlayerId.Substring(0, 6);
            _joinedLobby = await LobbyService.Instance.CreateLobbyAsync(
                $"{playerIdShort}'s match", MaxConnections, options);

            _heartbeatTimer = 0f;

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
            Lobby lobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
            string joinCode = lobby.Data[RelayJoinCodeKey].Value;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

            NetworkManager.Singleton.StartClient();

            _joinedLobby = lobby;
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
