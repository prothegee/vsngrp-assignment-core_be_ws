# Low-Level Design: Core BE WS

<br>

## Project structure

```
vsngrp-assignment-core_be_ws
|
|___/src
|   |___/Models
|   |   |___AppConfig.cs
|   |   |___ChatMessage.cs
|   |   |___Conversation.cs
|   |   |___HealthResponse.cs
|   |   |___WsProtocolMessages.cs      (WS frame types, WsJson serializer options)
|   |
|   |___/Services
|   |   |___ChatLogService.cs          (Redis chat log, 100-msg cap, no TTL)
|   |   |___ConversationService.cs     (create, rename, delete, list)
|   |   |___DeepSeekClient.cs          (deepseek-v4-flash, SSE streaming, semaphore-capped)
|   |   |___JwtVerifyService.cs        (verify-only, shared HS256 secret)
|   |   |___SessionService.cs          (Core BE's session Redis, read-only check)
|   |   |___TokenBudgetService.cs      (Redis daily token counter per account)
|   |
|   |___/WebSockets
|   |   |___ChatWebSocketHandler.cs    (accept, auth-frame, message loop)
|   |
|   |___Program.cs
|   |___VsngrpCoreBeWs.csproj
|
|___/tests
|   |___/Unit
|   |   |___ChatLogServiceTests.cs
|   |   |___DeepSeekClientTests.cs     (fake HttpMessageHandler, concurrency-cap queuing)
|   |   |___JwtVerifyServiceTests.cs
|   |   |___RedisTestFixture.cs
|   |   |___TokenBudgetServiceTests.cs
|   |
|   |___/Integration
|   |   |___ChatWebSocketTestFixture.cs
|   |   |___ChatWebSocketTests.cs      (real WS client, Testcontainers Redis)
|   |   |___CoreBeWsWebApplicationFactory.cs
|   |   |___FakeDeepSeekClient.cs
|   |
|   |___VsngrpCoreBeWs.Tests.csproj
|
|___/containers
|   |___/redis
|   |   |___/data                     (bind mount, gitignored)
|   |
|   |___docker-compose.yml            (Redis 8, 127.0.0.1-bound, own instance)
|
|___/config
|   |___config.json.template
|
|___/.github
|   |___/workflows
|       |___ci.yml
|       |___cd.yml
|
|___VsngrpCoreBeWs.slnx
|___Directory.Build.props
|___Dockerfile
|___containers.sh
|___debug.sh
|___run-tests.sh
|___verify-deploy.sh
|___.dockerignore
|___.gitignore
|___README.md
|___hld.md
|___lld.md
|___adr.md
|___development.md
|___deployment.md
|___LICENSE
```

<br>

## Scripts

| Script | Purpose |
| :- |
| `containers.sh` | datastore lifecycle: `up`, `down`, `start`, `stop`, `cleanup` (destructive, requires typed `YES`) |
| `debug.sh` | local dev entrypoint, warns and copies the config template if `config/config.json` is missing, starts the datastore container if it is not already running, then runs the service |
| `run-tests.sh` | runs `dotnet test` (unit, edge, integration) |
| `verify-deploy.sh` | post-deploy smoke test, TLS, `/health`, CORS, and WS handshake, run from `cd.yml` |

This service has no database migrations (Redis only, no schema), so unlike Core BE there is no `dotnet-tools.json`/`dotnet-ef` local tool and no separate `run-tests-<name>.sh` integrity runner, the integration test suite already exercises real Redis directly through Testcontainers.

<br>

## Config schema (`config/config.json`)

| Field | Type | Notes |
| :- |
| `port` | number | Kestrel binds to `0.0.0.0:<port>` |
| `version` | string | semver, manually bumped per release, read by `/health` |
| `jwtSecret` | string | HS256 shared secret, must match Core BE |
| `redis.connectionString` | string | this service's own Redis, conversations and chat log |
| `sessionRedis.connectionString` | string | Core BE's Redis, checked read-only on the auth frame |
| `deepSeek.apiKey` | string | from `api-key-deepseek.txt` at the repo root |
| `deepSeek.baseUrl` | string | `https://api.deepseek.com` |
| `deepSeek.model` | string | `deepseek-v4-flash` |
| `deepSeek.dailyTokenBudgetPerAccount` | number | daily cap enforced by `TokenBudgetService` |
| `deepSeek.maxConcurrentRequests` | number | semaphore size in `DeepSeekClient` |
| `corsAllowedOrigins` | string array | must include the Core FE origin |

<br>

## Redis key schema

Own Redis instance:

| Key | Value | Cap / TTL | Written by |
| :- |
| `conversation:<accountId>:<conversationId>` | conversation JSON | none | create, rename |
| `conversations:<accountId>` | sorted set of conversation ids, scored by creation time | none | create, delete |
| `chatlog:<accountId>:<conversationId>` | Redis list of chat message JSON | 100 messages, oldest trimmed | send_message |
| `token_budget:<accountId>:<yyyyMMdd>` | integer token count | 25 hour TTL | send_message (on completion) |

Core BE's Redis instance (read-only from this service):

| Key | Value | Written by |
| :- |
| `session:<sid>` | accountId | Core BE (signin, refresh, extended on use) |

<br>

## JWT verification

Access tokens are the same HS256 tokens Core BE issues. This service never signs one, it only validates:

1. Signature and expiry, using the shared `jwtSecret`.
2. `sub` claim parses as the account id, `sid` claim is present.
3. `session:<sid>` still exists in Core BE's Redis and its stored account id matches `sub`.

Any failure at any of these steps sends an `auth_error` frame and closes the connection with `PolicyViolation`. `JwtVerifyService` validates with `MapInboundClaims = false` so `sub` and `sid` read back exactly as issued, see `adr.md`.

<br>

## WS protocol

All frames are single JSON text messages with a `type` field, camelCase for every other field.

Client to server:

| `type` | Fields | When |
| :- |
| `auth` | `token` | must be the first message on any connection |
| `create_conversation` | `title` | |
| `list_conversations` | (none) | |
| `rename_conversation` | `conversationId`, `title` | |
| `delete_conversation` | `conversationId` | also deletes its chat log |
| `open_conversation` | `conversationId` | replays stored history |
| `send_message` | `conversationId`, `content` | triggers a DeepSeek completion |

Server to client:

| `type` | Fields | Meaning |
| :- |
| `auth_ok` | (none) | handshake accepted, further messages are now processed |
| `auth_error` | `error` | handshake rejected, connection closes right after |
| `error` | `error` | a later message was rejected, connection stays open |
| `conversation_created` | `conversation` | |
| `conversation_list` | `conversations` | |
| `conversation_renamed` | `conversationId`, `title` | |
| `conversation_deleted` | `conversationId` | |
| `conversation_history` | `conversationId`, `messages` | response to `open_conversation` |
| `message_chunk` | `conversationId`, `delta` | one streamed piece of the reply, zero or more per message |
| `message_complete` | `conversationId`, `message` | the fully assembled assistant message |

`error` codes used: `title_required`, `content_required`, `conversation_id_required`, `conversation_not_found`, `daily_token_budget_exceeded`, `deepseek_unavailable`, `message_too_large`, `missing_type`, `unknown_type`, `malformed_message`. A single WS message is capped at 64 KB, an oversized message gets `message_too_large` without closing the connection.

<br>

## Send-message flow

1. Confirm the conversation belongs to the authenticated account.
2. Check `TokenBudgetService`, an account already at or over `dailyTokenBudgetPerAccount` gets `daily_token_budget_exceeded` and DeepSeek is never called.
3. Append the user's message to the chat log immediately, so it is not lost even if the completion later fails.
4. Load the trimmed chat history plus the new message, send it to `DeepSeekClient`.
5. Forward each streamed chunk as `message_chunk`, accumulating the full text.
6. On stream completion, append the assembled assistant message to the chat log, add `usage.total_tokens` to the account's daily counter, and send `message_complete`.
7. Any exception during streaming (timeout, non-success status, malformed stream) sends `deepseek_unavailable` instead of steps 5 to 6, the connection stays open.
