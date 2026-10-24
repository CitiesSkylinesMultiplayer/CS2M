# CS2M - Cities: Skylines 2 Multiplayer

[![Discord](https://img.shields.io/discord/508902220943851522.svg)](https://discord.gg/RjACPhd)

## Quick Links

- [Discord Server](https://discord.gg/RjACPhd)
- [Developer Resources](https://cs2.paradoxwikis.com/Modding)
- [Patreon](https://www.patreon.com/CSM_MultiplayerMod)

## Introduction

CS2M is an in-development multiplayer / co-op mod for Cities: Skylines 2. This mod aims to provide a simple client-server experience where users can play and build together in a single game.

Feel free to join the development Discord server [here](https://discord.gg/RjACPhd).

## Developer Resources

### Mod Dependencies

Other Cities Skylines 2 mods that are required for CS2M to function properly:

- [I18n EveryWhere](https://mods.paradoxplaza.com/mods/75426/Windows)

## How multiplayer works

One player hosts and the others join. The host's game is the authority for the city:

- **Joining.** A joiner connects to the host (directly by IP address and port, or through NAT hole punching via
  the lobby server), passes the host's checks (game and CS2M version, DLCs, mods, username, password, player
  limit) and downloads the host's city. A **Resync** button in the HUD downloads it again; on the host it sends
  the city to every joiner again.
- **Player actions.** Roads, zoning, buildings, bulldozing, areas, terraforming, water sources, transit lines,
  upgrades, policies, names and their settings are captured on the player's side as they are applied, sent as
  commands, and rebuilt by the other games. A joiner's command goes to the host, which checks who sent it
  and validates it before applying it and relaying it to the other joiners. Command types a joiner may send are
  listed in `CS2M/Networking/CommandPolicy.cs`; every other type only travels from the host to joiners.
- **Simulation.** What the simulation decides stays with the host: buildings that grow, level up or are
  abandoned, money, taxes, budgets, map tiles and progression (XP, milestones, development tree, loans). Joiners
  copy the host's values.
- **Game speed.** Speed changes are shared. Opening a menu or switching away from the game no longer pauses the
  session; the player shows as "in menu" or "away" instead.
- **In-game HUD.** Shows the players in the session with their cursor colours and what they are doing, the chat,
  and for the host a kick/ban action per player. Other players' cursors are drawn in the world with their name.

## Settings

In the game's options, under CS2M:

- **Logging Level**: how much CS2M writes to its log.
- **API Server** / **API Server Port** (advanced): the lobby server's UDP address. The default lobby is
  `relay.shep.rip` port `4242`. Saved settings that still point at the old `api.citiesskylinesmultiplayer.com` are
  moved to the default.

Stored in the mod's settings file but not shown in the options:

- `LobbyUrl`: the lobby server's HTTP address for the server browser list; the default lobby's is
  `https://cs2.shep.rip`.
- The last used join address and port, host port and username; whether the HUD is collapsed; the host's player
  limit; the recently joined sessions; and the addresses the host has banned (these stay on the host's PC).

The host screen has the port, username, an optional password (kept for this game run only) and a player limit
(the host included; empty for no limit). The join screen lists the lobby's sessions in a **Browse** tab and a
**Modded** tab (sessions that use mods other than CS2M's dependencies and client side mods), compares each
session's versions and mods with this game, and has **Direct connect** (IP address and port) and **Recent** tabs.

## Lobby server

`CS2M.Lobby/` is the API server the mod talks to: the server browser list, joining by token through NAT hole
punching, and the port check that tells a host whether its game port is reachable. It is a separate .NET 8 app,
not part of the mod build. The mod's `ApiServer` / `ApiServerPort` settings point at its UDP side and `LobbyUrl`
at its HTTP side.

### Ports

- **UDP 4242**: the mod's API messages (LiteNetLib unconnected messages carrying MessagePack) and NAT
  introductions. Must be reachable directly, not through an HTTP proxy.
- **TCP 8080**: plain HTTP JSON API, meant to sit behind a reverse proxy that terminates TLS.
  - `GET /sessions`: live sessions, `[{token, name, host, players, maxPlayers, hasPassword, gameVersion, modVersion, lastSeen, details}]`.
    `details` is null until the host sends a SessionDetails message (older mods never do), otherwise
    `{population, milestone, year, month, playerList: [{name, color}], mods: [{id, name, version, clientSide}], modCount, updated}`
    (month 0-11; `modCount` is larger than `mods` when the host cut its list short, required mods come first).
    Never contains IP addresses. Built at most every 2 s (cached), and at most `LOBBY_HTTP_CONCURRENCY` requests
    are handled at once (the rest get a 503).
  - `GET /health`: `{status, sessions, uptimeSeconds}`. 503 with `status: "udp stalled"` when the UDP loop
    stops or the socket stops receiving the test packet the lobby sends itself every 10 s, so the Docker
    healthcheck covers both sides.

### Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `LOBBY_UDP_PORT` | `4242` | UDP port for the mod |
| `LOBBY_HTTP_PORT` | `8080` | HTTP port for the JSON API |
| `LOBBY_HTTP_BIND` | `+` (all) | HTTP listen address |
| `LOBBY_SESSION_TTL` | `60` | Seconds without a keep-alive before a session is dropped (hosts send one every 10 s) |
| `LOBBY_MAX_SESSIONS` | `1000` | Limit on confirmed sessions (ones that sent 2 keep-alives after registering) |
| `LOBBY_MAX_PENDING_SESSIONS` | `200` | Limit on sessions not confirmed yet |
| `LOBBY_MAX_SESSIONS_PER_IP` | `5` | Sessions one public address may register |
| `LOBBY_MAX_SESSIONS_PER_SUBNET` | `20` | Sessions one /24 may register |
| `LOBBY_UDP_PPS` | `2000` | Global UDP budget, packets a second (bursts of twice that); past it packets are dropped before decoding |
| `LOBBY_INTRO_TOKENS_PER_IP` | `5` | Distinct sessions (tokens or `ip:` targets) one address may ask to be introduced to per hour; asking again for the same one is free |
| `LOBBY_INTRO_TOKENS_PER_SUBNET` | `20` | The same per /24 |
| `LOBBY_INTROS_PER_SESSION` | `30` | Introductions to one session per minute, so nobody can flood a host through the lobby |
| `LOBBY_INTROS_PER_SECOND` | `50` | Introductions per second in total |
| `LOBBY_PORT_CHECKS_PER_SECOND` | `5` | New port check probes per second in total |
| `LOBBY_PORT_CHECKS_IN_FLIGHT` | `32` | Port check probes running at once |
| `LOBBY_TOMBSTONE_HOURS` | `6` | How long a closed or expired token stays reserved for its owner (0 turns it off) |
| `LOBBY_HTTP_CONCURRENCY` | `32` | HTTP requests handled at once |
| `LOBBY_ALLOW_PRIVATE_PEERS` | off | `1` allows port checks for private and loopback sources (tests and LAN setups only) |
| `LOBBY_LOG_IPS` | off | `1` logs full addresses; otherwise logs show only the first two octets |

Logs: one summary line a minute with counters (packets, drops, registrations, introductions, port checks), plus at
most about ten event lines a minute. Nothing is logged per packet.

### Build and run

```sh
cd CS2M.Lobby
docker build -t cs2m-lobby .
docker run -d --name cs2m-lobby --restart unless-stopped \
  -p 4242:4242/udp -p 127.0.0.1:8080:8080 cs2m-lobby
```

Without Docker: `dotnet run --project CS2M.Lobby -c Release`, with the environment variables above.

`CS2M.Lobby.SmokeTest/` plays a host and a joiner against a running server, serializing exactly like the mod:
`dotnet run --project CS2M.Lobby.SmokeTest -- <lobby host> 4242 8080`. With `--remote` (and the HTTP base URL as
the third argument) it runs only the basic checks, for a deployed lobby. The comment at the top of
`CS2M.Lobby.SmokeTest/Program.cs` explains how to run the full test.

### Protocol

A message is a LiteNetLib unconnected message whose payload is what MessagePack.Attributeless writes:
`int typeKey, int propertyCount, (int propertyKey, value)...`. Type keys come from the registration order in
`ApiCommand.ProtocolOrder` (PortCheckRequest 0, PortCheckResult 1, ServerRegistration 2, SessionInfo 3,
ServerUnregister 4, SessionDetails 5); property keys are the command's properties sorted by name
(case-insensitively). `CS2M.Lobby/Protocol.cs` lists them. New commands go at the end of `ProtocolOrder`. An
existing command may only gain a property whose name sorts after all of its current ones (so no key moves);
anything else needs a new command. The lobby skips keys it doesn't know and treats missing ones as unset, so older
and newer mods and lobbies keep working with each other.

#### What the lobby enforces

- **Ownership.** A host that sends a `TokenSecret` owns its token by that secret: keep-alives, `ServerUnregister`
  and re-registering after an expiry must carry the same secret, from any address. A host without one (older mods)
  owns it by its public address; its port only moves after the new port has sent 3 keep-alives over 20 s with none
  from the old port in between, and `ServerUnregister` / `SessionDetails` must come from exactly its address and
  port. A closed or expired token stays reserved for its owner for `LOBBY_TOMBSTONE_HOURS`.
- **Sessions.** Only confirmed sessions (2 keep-alives after the first) count toward `LOBBY_MAX_SESSIONS`; others
  have their own smaller limit. Per address and per /24 limits apply to both. Names lose control and format
  characters (bidi overrides, zero-width), separators, private use characters and stacks of combining marks.
  A reported LAN address is kept only if it is private IPv4 (10/8, 172.16/12, 192.168/16, 100.64/10).
- **NAT introductions.** Both sides get each other's LAN endpoint only when they share a public address (same
  router); otherwise only public endpoints are exchanged. `ip:` must name the host's exact public address and
  port. Each source address gets a few distinct sessions an hour, each session a limited number of introductions a
  minute.
- **Port checks.** Probed only once the requester has a confirmed session at exactly that address and port (the
  mod asks right after its first keep-alive, so the answer comes about 20 s later), twice per session at most, 3
  connection attempts, with global caps. Requests that don't qualify (including invalid ports) get no reply.

#### Host secret

The mod sends a `TokenSecret` string property with three commands, so that only the host can refresh or close its
session:

| Command | Property | Key on the wire |
|---|---|---|
| `SessionInfoCommand` | `TokenSecret` | 10 (after `Token` = 9) |
| `ServerRegistrationCommand` | `TokenSecret` | 3 (after `Token` = 2) |
| `ServerUnregisterCommand` | `TokenSecret` | 1 (after `Token` = 0) |

`ApiServer.Start` generates it along with each new `Token`: 32 random bytes from a CSPRNG, encoded as base64url
without padding (43 characters). The same value goes with every keep-alive and the final `ServerUnregister` for
that token. It is kept in memory only and never sent to other players. The lobby accepts 22 to 128 characters of
`A-Z a-z 0-9 - _`; an empty or null value counts as no secret, and any other value that doesn't match makes the
lobby refuse the registration.

## Contributors
A full list of contributors can be found [here](https://github.com/CitiesSkylinesMultiplayer/CS2M/graphs/contributors).

## License

This mod and its source code is licensed under the MIT license.