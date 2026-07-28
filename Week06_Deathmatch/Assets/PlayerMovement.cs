using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Minimal WASD movement so a target can strafe for lag-compensation
/// testing. Uses a standard (non-anticipated) NetworkTransform set to
/// Owner Authoritative in the Inspector — the owning client moves its own
/// capsule directly, and NGO replicates the result to everyone else,
/// including the server (whose copy is what HitboxHistoryBehaviour records).
///
/// Movement validation/anti-cheat hardening is Week 10's focus, not this
/// week's — kept deliberately simple here.
/// </summary>
[RequireComponent(typeof(NetworkTransform))]
public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float speed = 4f;

    private void Update()
    {
        if (!IsOwner) return;

        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        transform.position += move * speed * Time.deltaTime;
    }
}
