using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Client-side input for firing the weapon. Sends origin/direction plus the
/// client's own reading of NetworkManager.ServerTime.Time — the same
/// synchronized clock on client and server — so the server can rewind to
/// the exact moment this client saw the shot happen. Never uses Time.time,
/// which is a local, unsynchronized clock and unusable for cross-machine
/// timestamps.
/// </summary>
public class WeaponInput : NetworkBehaviour
{
    [SerializeField] private WeaponBehaviour weapon;
    [SerializeField] private Transform firePoint; // optional; defaults to a point above this transform

    private void Update()
    {
        if (!IsOwner) return;
        if (weapon == null) return;

        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
        {
            Vector3 origin = firePoint != null ? firePoint.position : transform.position + Vector3.up * 1.5f;
            Vector3 direction = transform.forward;
            double fireTime = NetworkManager.Singleton.ServerTime.Time;

            weapon.FireServerRpc(origin, direction, fireTime);
        }
    }
}
