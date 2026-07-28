using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Minimal on-screen Host/Client buttons for testing in Play Mode and in
/// standalone builds. A fresh NGO project has no built-in UI for this — this
/// mirrors the GameManager button pattern used in the Week 03 project.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUI.Button(new Rect(10, 10, 100, 30), "Host"))
            {
                NetworkManager.Singleton.StartHost();
            }

            if (GUI.Button(new Rect(120, 10, 100, 30), "Client"))
            {
                NetworkManager.Singleton.StartClient();
            }
        }
        else
        {
            string mode = NetworkManager.Singleton.IsHost ? "Host"
                : NetworkManager.Singleton.IsServer ? "Server"
                : "Client";
            GUI.Label(new Rect(10, 10, 300, 30), $"Mode: {mode}");
        }
    }
}
