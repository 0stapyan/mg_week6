# Week 08 — Session-Based Host/Join Over the Internet
**Multiplayer Games Development · Unity Track · 9 pts**

**Repository:** https://github.com/0stapyan/mg_week6

## Overview

Continued from the Week 07 project. Replaced the local `NetworkBootstrap` with
`SessionBootstrap`, which integrates Unity Gaming Services (UGS) — Authentication,
Lobby, and Relay — to host and join matches without manual IP entry or port
forwarding, using Unity's Relay servers for NAT traversal.

## Why UGS (Unity Gaming Services)

Chosen because it's Unity's first-party solution with the most direct NGO integration
(the transport layer, `UnityTransport.SetRelayServerData`, is built for exactly this),
free tier is sufficient for class testing (50 CCU / 25 lobbies), and it avoids
introducing a third-party account system (Steam/EOS) for what is fundamentally a
learning exercise rather than a shipping product.

## Authentication Method

`AuthenticationService.Instance.SignInAnonymouslyAsync()` — anonymous sign-in, gated so
`UnityServices.InitializeAsync()` is never called twice (which throws). Sufficient for
testing; a real product would use Unity Player Accounts or a platform identity provider
instead.

## Architecture

| Piece | Role |
|---|---|
| `SessionBootstrap.Awake()` | Initializes UGS once, signs in anonymously |
| `HostGameAsync()` | Allocates a Relay allocation → gets a join code → configures `UnityTransport` → `StartHost()` → creates a public Lobby with the join code stored in its `Data` dictionary |
| `RefreshLobbiesAsync()` | `QueryLobbiesAsync()` to list open public lobbies |
| `JoinGameAsync(lobbyId)` | Joins the lobby → reads the stored join code → joins the matching Relay allocation → configures transport → `StartClient()` |
| Heartbeat loop (`Update` + `HeartbeatLobbyAsync`) | Host-only; pings `SendHeartbeatPingAsync` every 15s |

## Bug Found and Fixed: Missing Lobby Heartbeat

The first draft of `SessionBootstrap` created a lobby once and never touched it again.
Unity's Lobby service automatically removes a lobby from public listings if the host
doesn't heartbeat it periodically (documented expiry behavior, ~30s of inactivity).
On localhost this is easy to miss — browsing and joining happens within seconds — but
it's exactly the kind of bug that would silently break a real cross-network test, where
more time passes between hosting and a remote player browsing/joining. Fixed by adding
a 15-second heartbeat loop that runs only on the host, keyed off `NetworkManager.IsHost`.

## Test Results

**Local test (Editor Host + standalone build Client, same machine):**
- ✅ Anonymous sign-in confirmed on both instances (`Signed in as <playerId>` shown in UI)
- ✅ Host flow: Relay allocation → join code → Lobby created successfully, status
  progressed through `Allocating relay... → Creating lobby... → Hosting — lobby '...'`
- ✅ Lobby browser: `Refresh Lobbies` correctly listed the open lobby with player count
- ✅ Join flow: client joined the Relay allocation and connected successfully
  (`Mode: Client` shown, matching NetworkManager state)
- ✅ Gameplay from Weeks 06–07 (team scoreboard, kills/deaths, lag-compensated weapon)
  confirmed working identically over the Relay-based connection as it did over direct
  local connections in previous weeks

**Cross-network test (peer on a different network): NOT completed.**

## Honest Note on the Untested Scenario

We did not have access to a second person or a second physical device to genuinely test
from a different network/NAT, and documenting this honestly rather than fabricating a
result. One technical mitigating factor worth noting: **even the "local" test above did
not use a localhost/LAN connection** — `UnityTransport.SetRelayServerData` routes all
traffic through Unity's Relay infrastructure in the cloud regardless of where the two
processes physically run. Both the host and client genuinely dialed out to Unity's Relay
servers over the real internet and back, which exercises the actual NAT-traversal code
path this assignment is about. What specifically remains unverified is a client
connecting from a *distinct* external NAT/ISP — we have no reason to expect this would
behave differently given Relay already abstracts that away, but we can't claim to have
observed it directly.

## Known Limitations
- Anonymous auth only (no persistent accounts/identity)
- No lobby cleanup/`DeleteLobbyAsync` on host disconnect — a stale lobby could
  theoretically linger briefly after a crash (heartbeat expiry still removes it
  eventually)
- Cross-network test unverified (see above)

## Security / Credential Hygiene
No API keys, service account secrets, or credential files are used or committed — the
client-side UGS SDK with anonymous sign-in requires none. `.gitignore` carried over from
Week 06/07 unchanged.

## Tech Stack
- Unity 6.3 LTS (6000.3.10f1)
- Netcode for GameObjects (NGO) 2.12.0 + Unity Transport
- Unity Gaming Services: Authentication, Lobby, Relay
- Platform: macOS Apple Silicon