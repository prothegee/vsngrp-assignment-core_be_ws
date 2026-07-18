# Development Guide: Core BE WS

<br>

## Prerequisites

- .NET 10 SDK
- Docker (or a Docker-compatible engine, for example Podman)
- Core BE checked out alongside this repository and running first, see `tasks.md` Local dev order

<br>

## First-time setup

1. Copy the config template:
   ```
   cp config/config.json.template config/config.json
   ```
   `jwtSecret` must be the exact same value as Core BE's `config/config.json`. `deepSeek.apiKey` comes from `api-key-deepseek.txt` at the repository root. Leave `redis` and `sessionRedis` pointing at `localhost` matching the ports below.

2. Create the bind-mounted data directory if it is not already writable by the container engine's user:
   ```
   chmod 777 containers/redis/data
   ```
   This is only needed once. It matters most on rootless Podman, where the container's internal user id does not map to the host user that owns the directory.

3. Start Core BE first (its own `containers.sh up`, from the Core BE repository), then start this service's own datastore:
   ```
   ./containers.sh up
   ```
   This starts this service's own Redis on `6380`, bound to `127.0.0.1`. Core BE's session Redis stays on its own `6379`, this service only reads from it.

<br>

## Managing the datastore

```
./containers.sh {up|down|start|stop|cleanup}
```

| Command | Effect |
| :- |
| `up` | create and start the container |
| `down` | stop and remove the container, the data directory is left alone |
| `start` | start the existing container back up without recreating it |
| `stop` | stop the running container without removing it |
| `cleanup` | permanently wipe the Redis data directory, prints an all-caps warning and refuses to proceed unless typed exactly `YES` |

`cleanup` also runs `down` first, and wipes the data directory from inside a throwaway container rather than a plain `rm`, since Redis creates some files under its own container user id, which the host user cannot always delete directly, even with permissive directory permissions.

Each service that ships a `containers/` stack declares its own Compose project name (`name:` in `docker-compose.yml`), so bringing this service's stack up or down never touches Core BE's containers even though both live under a directory literally called `containers`, see `adr.md`.

<br>

## Running the service

```
./debug.sh
```

`debug.sh` checks that `config/config.json` exists first. If it is missing, it copies `config/config.json.template` in its place and prints a warning, since the copied file still has `CHANGE_THIS` placeholders that will not actually authenticate against anything, it only prevents an immediate crash on a fresh checkout. It then checks whether the datastore container is running, and starts it (`./containers.sh up`) if it is not. It then runs the service the same way `dotnet run` would.

The service reads `config/config.json` (one directory above `src`) and listens on the configured `port` (`9002` by default). Set the `CONFIG_PATH` environment variable to point somewhere else if needed.

<br>

## Running tests

```
./run-tests.sh
```

Runs `dotnet test`, the full unit, edge, and integration suite. Integration tests use Testcontainers to start their own throwaway Redis containers (both a stand-in for this service's own Redis and a stand-in for Core BE's session Redis), they do not touch the container started by `docker compose`.

If using rootless Podman instead of Docker Engine, point the test run at the Podman API socket first:

```
systemctl --user start podman.socket
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
./run-tests.sh
```

<br>

## Trying the WS protocol by hand

With the service running, any WebSocket client works. Using `wscat`:

```
wscat -c ws://localhost:9002/ws/chat
> {"type":"auth","token":"<a real access token from Core BE signin>"}
> {"type":"create_conversation","title":"first chat"}
> {"type":"send_message","conversationId":"<id from the previous response>","content":"hello"}
```

The access token needs to belong to an account that actually signed in through Core BE first, since this service checks the session against Core BE's Redis, not just the JWT signature.

<br>

## Formatting

```
dotnet format
```

`ci.yml` runs `dotnet format --verify-no-changes`, run it locally before opening a pull request.

<br>

## Troubleshooting

- **Redis container exits immediately with a permission error**: the bind-mounted data directory is not writable by the container's user. See step 2 above.
- **`config.json failed to bind to AppConfig`**: `config/config.json` is missing or not valid JSON. Re-copy it from `config/config.json.template`.
- **Every auth frame gets `invalid_or_expired_session` even with a fresh token**: `sessionRedis.connectionString` is not pointing at Core BE's actual Redis, or Core BE is not running. This service never writes sessions itself, it can only be wrong about where to read them.
- **`error MSB4242: SDK Resolver Failure` mentioning a workload set version**: this is a broken or incomplete .NET workload manifest install on the machine, unrelated to this project (this project has no workload dependencies at all, no MAUI, no mobile, no wasm-tools). `Directory.Build.props` at the repository root already sets `MSBuildEnableWorkloadResolver` to `false` for exactly this reason, so this should not happen. If it does, confirm `Directory.Build.props` exists at the repository root and was not accidentally removed.
