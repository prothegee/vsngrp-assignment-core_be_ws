# Core BE WS

[![ci](https://github.com/prothegee/vsngrp-assignment-core_be_ws/actions/workflows/ci.yml/badge.svg)](https://github.com/prothegee/vsngrp-assignment-core_be_ws/actions/workflows/ci.yml)
[![cd](https://github.com/prothegee/vsngrp-assignment-core_be_ws/actions/workflows/cd.yml/badge.svg)](https://github.com/prothegee/vsngrp-assignment-core_be_ws/actions/workflows/cd.yml)

Core BE WS is the chat service for the chat-bot product. It owns the WebSocket connection a signed-in account uses to talk to the chat bot: creating and managing conversations, streaming replies from DeepSeek, and keeping a bounded chat log per conversation.

<br>

## What it does

- Accepts a WebSocket connection at `/ws/chat`, the first message on any connection must be an auth frame carrying a Core BE-issued access token.
- Lets an authenticated connection create, list, rename, and delete named conversations.
- Sends a message to DeepSeek (`deepseek-v4-flash`) and streams the reply back as it is generated.
- Stores both sides of every exchange, capped at the most recent 100 messages per conversation, and replays that history when a conversation is reopened.
- Enforces a daily per-account token budget, since DeepSeek itself only limits concurrency, not spend.
- `GET /health`: status, version, and the exact commit deployed.

Conversations, chat logs, and the token budget live in this service's own Redis. It never writes to Core BE's session store, only reads it to confirm a session is still active.

<br>

## Prerequisites

- .NET 10 SDK
- Docker (or a Docker-compatible engine, for example Podman)
- Core BE running locally first, its session Redis is what this service checks on every auth frame (see `tasks.md` Local dev order)

<br>

## Setup

1. Copy the config template and fill in real values:
   ```
   cp config/config.json.template config/config.json
   ```
   `jwtSecret` must match Core BE's `config.json` exactly. `deepSeek.apiKey` comes from `api-key-deepseek.txt` at the repository root.
2. Run the service:
   ```
   ./debug.sh
   ```
   `debug.sh` warns and copies the config template for you if `config/config.json` is missing, checks its own datastore container and starts it if not already running, then starts the service.

The service listens on the port set in `config.json` (`9002` by default). See `development.md` for a full local setup walkthrough and `deployment.md` for production setup.

<br>

## Managing the datastore

```
./containers.sh {up|down|start|stop|cleanup}
```

`up` and `down` create/destroy the container, `start`/`stop` pause and resume it without losing data, `cleanup` permanently wipes the Redis data directory, it asks for a typed `YES` confirmation first since it cannot be undone.

<br>

## Testing

```
./run-tests.sh
```

Runs the full unit, edge, and integration suite (`dotnet test`). Integration tests spin up their own throwaway Redis containers via Testcontainers and clean up after themselves, no manual setup needed.

<br>

## More documentation

- `hld.md`: how the service fits into the wider system.
- `lld.md`: project structure, WS protocol, and internal design.
- `adr.md`: why specific technical choices were made.
- `development.md`: local development guide.
- `deployment.md`: production deployment guide.
