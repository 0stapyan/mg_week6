# Week 09 — Dedicated Server Build & Deployment
**Multiplayer Games Development · Unity Track · 9 pts**

**Repository:** https://github.com/0stapyan/mg_week6

## Path Chosen: Path A — True Headless Dedicated Server

Built and ran a genuine headless dedicated server (macOS Dedicated Server Build
Support), tested locally with two separate client instances connecting directly by
IP:port. The cloud-deployment bonus (+1 pt) was deliberately skipped this round —
prioritized getting the core dedicated-server path fully correct over adding cloud
infrastructure on top.

## Architecture

| Piece | Role |
|---|---|
| `ServerBootstrap.cs` | Entry point for the dedicated server build only. All logic guarded with `#if UNITY_SERVER` **inside** the method body (not around the whole class), so the component stays attachable to the same `NetworkManager` GameObject in every build target without Unity flagging a "missing script" on non-server builds. |
| `SessionBootstrap.cs` (from Week 08) | Guarded with `#if !UNITY_SERVER` — its UGS sign-in/Relay/Lobby logic and OnGUI buttons have no reason to run on a headless server. Extended with a **Direct Connect** field (IP + port) so a client build can connect straight to the dedicated server, bypassing Relay entirely — that's the whole point of this week's test. |
| `ScoreboardUI.cs` | Guarded with `#if UNITY_SERVER` early-return in `Start()` — a headless server has no display, so subscribing to replicated-state events just to update UI text that's never seen is pointless overhead. This is the same "camera/audio/input" guarding pattern from the brief, applied to UI instead. |

Both `ServerBootstrap` and `SessionBootstrap` live on the **same** `NetworkManager`
GameObject in the **same** scene — which script actually does anything is determined
entirely by the `UNITY_SERVER` compile-time define, itself set by which Build Profile
is active at build time. No separate scene or prefab variant was needed.

## Build Process

1. **File → Build Profiles** → added and switched to the **Dedicated Server** (macOS)
   profile.
2. Confirmed `UNITY_SERVER` was actually active by opening `ServerBootstrap.cs` in the
   editor and checking that the code inside `#if UNITY_SERVER` was no longer greyed out.
3. **File → Build** (not Build And Run — a headless server doesn't need to auto-launch
   with a display) → output to `~/Desktop/Week09_Server/build/`.
4. **Switched the active Build Profile back to standard macOS Standalone** immediately
   after — leaving Dedicated Server active would silently make Play Mode itself compile
   as a headless server (no `SessionBootstrap` UI), which would break normal client
   testing.
5. Built a separate, ordinary Standalone client with **File → Build And Run**.

## Launch Command

```bash
cd ~/Desktop/Week09_Server/build
./Week06_Deathmatch -batchmode -nographics -port 7777 -maxplayers 4
```

- `-batchmode` / `-nographics` — Unity's built-in headless flags (no rendering, no
  update-loop frame skipping).
- `-port` / `-maxplayers` — custom arguments parsed by `ServerBootstrap.ParseCommandLineArgs()`
  via `Environment.GetCommandLineArgs()`. A `-map` argument is also parsed (defaults to
  `"default"`) but isn't acted on yet, since the project currently has a single scene —
  reserved for future multi-map support.
- `Application.targetFrameRate = 30` is set in `ServerBootstrap.Start()` so the headless
  loop doesn't run uncapped and pin a CPU core.

## Bootstrap Logic Summary

`ServerBootstrap.Start()` (not `Awake()` — per the brief's own warning, calling
`NetworkManager.Singleton.StartServer()` too early can race against NetworkManager's own
`Awake()`, since script execution order across different GameObjects isn't guaranteed):

1. Parses `-port`, `-map`, `-maxplayers` from the command line.
2. Sets `Application.targetFrameRate`.
3. Configures `UnityTransport.SetConnectionData("0.0.0.0", port)` so the server listens
   on all interfaces.
4. Subscribes to `NetworkManager.OnServerStarted` for a confirmation log.
5. Calls `NetworkManager.Singleton.StartServer()`.

## Test Results

Two separate standalone client instances connected via **Direct Connect** (IP
`127.0.0.1`, port `7777`) to the running dedicated server. Confirmed from the server's
own console output:

- Both clients connected and were assigned to opposing teams:
  ```
  [SessionManager] Client 1 assigned to team 0
  [SessionManager] Client 2 assigned to team 1
  ```
- Full Week 06/07 gameplay worked identically to previous weeks' Host-mode testing —
  weapon fire, lag-compensated hit detection, and kill processing all functioned
  correctly against the dedicated server as the sole authority:
  ```
  [Weapon] HIT (torso) — shooter 1 -> target 2, age 0.070s
  [SessionManager] Kill processed — killer: 1, victim: 2
  ```
- Server ran stably across the full test session with both clients playing back and
  forth (multiple kills recorded in both directions), no crashes or disconnects.

Screenshot/recording: see attached video showing both client windows plus the server
terminal output side by side.

## Known Note

The server log during this test still shows the temporary `[Weapon][debug] origin=...`
diagnostic line added while debugging Week 07's hit detection. It doesn't affect
correctness here — just leftover verbosity that should be (and has since been) removed
from `WeaponBehaviour.cs` before it carries forward into Week 10.

## Bonus (Cloud Deployment)

Not attempted this round — scoped out in favor of a solid, well-documented local
dedicated-server path. `ServerBootstrap`'s command-line argument handling and the
`UNITY_SERVER`-guarded architecture would carry over directly to a Linux cloud VM with
no code changes, only a Linux Dedicated Server build instead of macOS.

## Tech Stack
- Unity 6.3 LTS (6000.3.10f1)
- Netcode for GameObjects (NGO) 2.12.0
- Build target: macOS Dedicated Server
- Platform: macOS Apple Silicon (host machine for both server and clients)