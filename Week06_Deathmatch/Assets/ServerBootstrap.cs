using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Entry point for the dedicated server build. All logic is guarded with
/// #if UNITY_SERVER inside the method body (rather than around the whole
/// class) so this component can stay attached to the same NetworkManager
/// GameObject in every build target without Unity complaining about a
/// "missing script" on non-server builds.
///
/// Reads custom command-line arguments (-port, -map, -maxplayers) — these are
/// OUR args, Unity itself doesn't interpret them. -batchmode and -nographics
/// are Unity's own built-in headless flags and don't need parsing here.
/// </summary>
public class ServerBootstrap : MonoBehaviour
{
    private void Start()
    {
#if UNITY_SERVER
        var args = ParseCommandLineArgs();

        int port = args.TryGetValue("port", out var portStr) && int.TryParse(portStr, out var p)
            ? p : 7777;
        string map = args.TryGetValue("map", out var mapArg) ? mapArg : "default";
        int maxPlayers = args.TryGetValue("maxplayers", out var maxStr) && int.TryParse(maxStr, out var m)
            ? m : 4;

        Debug.Log($"[ServerBootstrap] Starting dedicated server — port={port} map={map} maxPlayers={maxPlayers}");

        // Uncapped loop otherwise pins CPU to 100% on a headless build.
        Application.targetFrameRate = 30;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData("0.0.0.0", (ushort)port);

        NetworkManager.Singleton.OnServerStarted += () =>
        {
            Debug.Log("[ServerBootstrap] Server started successfully and is accepting connections.");
        };

        NetworkManager.Singleton.StartServer();
#endif
    }

#if UNITY_SERVER
    private Dictionary<string, string> ParseCommandLineArgs()
    {
        var result = new Dictionary<string, string>();
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                result[args[i].TrimStart('-')] = args[i + 1];
            }
        }

        return result;
    }
#endif
}
