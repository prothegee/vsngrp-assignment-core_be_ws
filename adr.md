# Architecture Decision Records: Core BE WS

Each entry is a decision, the reason for it, and what was traded away.

<br>

## ADR 001: WS auth: first-message auth frame, not a query-param token

Decision: the WS connection opens unauthenticated. The client's first message must be an `auth` frame carrying the JWT. The server verifies it before accepting any other message type.

Reason: a token in a query string ends up in proxy access logs and browser history. A first-message frame keeps it out of both.

<br>

## ADR 002: WS transport: raw ASP.NET Core WebSockets, not SignalR

Decision: use `System.Net.WebSockets` middleware directly instead of SignalR.

Reason: the spec calls for a plain WebSocket API service. SignalR adds its own framing and client library requirement that the Vue frontend does not need.

<br>

## ADR 003: DeepSeek response delivery: token-by-token streaming

Decision: forward each DeepSeek SSE chunk to the client as its own `message_chunk` frame, instead of waiting for the full completion and sending one message.

Reason: a live, incrementally-appearing reply reads as a real chat bot. A loading spinner followed by one large message does not.

<br>

## ADR 004: Chat scope: multiple named conversations per account

Decision: an account can hold any number of conversations, each with its own title and message history.

Reason: matches how a real chat product is used, one continuous unnamed thread does not fit a "create, rename, delete" conversation feature.

<br>

## ADR 005: Conversation persistence: no TTL, explicit delete only

Decision: conversations and their chat logs live in Redis indefinitely. Only an explicit `delete_conversation` removes them.

Reason: a conversation the user deliberately named and kept should not silently expire. TTL-based session data (see below) is a different concern from user-owned content.

<br>

## ADR 006: Chat log cap: 100 messages per conversation, oldest trimmed first

Decision: `ChatLogService` trims each conversation's Redis list to the most recent 100 messages on every append.

Reason: bounds both Redis memory per conversation and the context sent to DeepSeek on every new message, without needing a separate cleanup job.

<br>

## ADR 007: Model: `deepseek-v4-flash`

Decision: use `deepseek-v4-flash` for all completions.

Reason: `deepseek-chat` and `deepseek-reasoner` deprecate 2026/07/24, confirmed against `https://api-docs.deepseek.com/`. Building against a model already scheduled for removal would need a follow-up migration almost immediately.

<br>

## ADR 008: Consumption limiting: per-account daily token budget, not DeepSeek's own limits

Decision: `TokenBudgetService` keeps a Redis counter of `usage.total_tokens` per account per day, checked before every DeepSeek call and rejected with an error frame once the configured `dailyTokenBudgetPerAccount` is reached.

Reason: DeepSeek's own documented limit (`https://api-docs.deepseek.com/quick_start/rate_limit`) is a 2,500 concurrent-request cap, account-wide, with no spend limit at all. Nothing on DeepSeek's side bounds actual token cost, so bounding it is entirely this service's responsibility.

<br>

## ADR 009: Concurrency cap: in-process semaphore sized well under DeepSeek's account-wide limit

Decision: `DeepSeekClient` acquires a slot from a `SemaphoreSlim` sized to `maxConcurrentRequests` before calling DeepSeek, and queues rather than errors once the cap is reached.

Reason: turns a burst of requests into a queue instead of a wall of `429` responses, and leaves headroom under DeepSeek's 2,500 concurrent-request account-wide cap in case the same API key is ever shared with another service.

<br>

## ADR 010: Two Redis connection strings: `redis` (own) and `sessionRedis` (Core BE's)

Decision: `config.json` has two separate Redis connection strings, `redis` for this service's own conversations and chat log, and `sessionRedis` for Core BE's session store, checked read-only on every auth frame.

Reason: `tasks.md`'s architecture diagram for this service shows two distinct Redis instances (its own, and Core BE's), and the auth-frame handshake needs to check session state that only Core BE writes to. A single shared connection string could not express both roles.

<br>

## ADR 011: Redis access: keyed dependency injection, not two wrapper interfaces

Decision: both `IConnectionMultiplexer` instances are registered as .NET keyed services (`"own"` and `"session"`), and each service takes the one it needs via `[FromKeyedServices]` on its constructor.

Reason: a plain interface already fully describes what each service needs from Redis, wrapping it in a second interface just to disambiguate two instances of the same type would be an extra layer with no behavior of its own.

<br>

## ADR 012: Oversized WS message: reject with an error frame, keep the connection open

Decision: `ChatWebSocketHandler` caps any single WS message at 64 KB. A message over that cap gets an `error` frame with `message_too_large` and the connection stays open for the next message.

Reason: one oversized frame is a client mistake, not a reason to drop a connection the account already paid an auth handshake to establish.

<br>

## ADR 013: DeepSeek failure: error frame, not a dropped connection

Decision: a DeepSeek call that throws (timeout, non-success status, malformed stream) is caught around the whole streaming loop and turned into an `error` frame with `deepseek_unavailable`, the connection itself is never closed because of it.

Reason: DeepSeek being briefly unavailable is an external, expected failure mode. The user should be able to try again on the same connection instead of reconnecting and re-authenticating.

<br>

## ADR 014: JWT claim reading: disable inbound claim type mapping

Decision: `JwtVerifyService` validates tokens with `new JwtSecurityTokenHandler { MapInboundClaims = false }`.

Reason: the legacy JWT handler in `System.IdentityModel.Tokens.Jwt` remaps short claim names to long XML-namespace claim type URIs during `ValidateToken` by default (for example `sub` becomes `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`), which silently breaks a direct `FindFirst(JwtRegisteredClaimNames.Sub)` lookup after validation. Disabling the mapping keeps the claim types exactly as Core BE issued them.

<br>

## ADR 015: Datastore project isolation: named Compose projects

Decision: both this service's and Core BE's `containers/docker-compose.yml` declare an explicit top-level `name:` (`vsngrp-core-be-ws` and `vsngrp-core-be`).

Reason: Compose derives a default project name from the directory holding the compose file, and both services happen to name that directory `containers`. Without an explicit name, both stacks resolved to the same default project and the same default network, so bringing one service's stack up or down could interfere with the other's already-running containers. This surfaced directly while first bringing both services' stacks up together during Core BE WS's own implementation, exactly the "Core BE, then Core BE WS" local dev order `tasks.md` already describes as the normal case, and applies retroactively to Core BE as well since the bug lives in the shared naming pattern, not in either service alone.

<br>

## ADR 016: Health check versioning: two separate signals

Decision: `/health` reports both `version` (semver, manually bumped per release) and `gitSha` (automatic, from a `GIT_SHA` Docker build-arg).

Reason: `version` answers "which release is this," `gitSha` answers "which exact commit is this." Same reasoning as Core BE's own health check design, kept consistent across both services.

<br>

## ADR 017: Reverse proxy: containerized, this service owns and deploys its own conf file

Decision: the public-facing reverse proxy (`vsngrp-reverse-proxy`, `nginx:alpine`, `network_mode: host`) is a separate container this service does not own, but this repo commits and deploys its own server block, `nginx/vsngrp-bews.conf` (carrying the `Upgrade`/`Connection: upgrade` headers and long `proxy_read_timeout` this service's WS connections need), copied into the proxy's shared `conf.d` and reloaded on every `cd.yml` run.

Reason: `network_mode: host` lets the proxy container reach `127.0.0.1:9002` exactly like a host-installed nginx would, so nothing about this service's own port binding or Compose network needed to change to support it. Each service owning exactly 1 conf file, written by exactly 1 pipeline, keeps 3 independent deploy pipelines from racing on or overwriting a shared file, one truth source per domain even though the proxy itself is shared. Full detail in `tasks.md`'s Deployment infrastructure section.
