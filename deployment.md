# Deployment Guide: Core BE WS

<br>

## Overview

Core BE WS runs on the same AWS EC2 instance as Core BE and Core FE, each as its own `docker compose` project. A single shared reverse-proxy container (`vsngrp-reverse-proxy`, `network_mode: host`) owns ports 80 and 443 and is the only process reachable from outside the instance, it terminates TLS and reverse-proxies to each service's app container on `127.0.0.1`, including the WS upgrade headers this service needs. This service owns and deploys its own server block, `nginx/vsngrp-bews.conf`, see Deploy flow below and `tasks.md` Deployment infrastructure for the shared proxy itself.

This service's own Redis (conversations, chat log, token budget) binds to `127.0.0.1` only, unreachable from outside the instance. It also reads Core BE's session Redis, over Core BE's own Docker network (this service's app container is attached to it explicitly on every deploy), not over the host loopback, containers on different Docker networks cannot reach each other's `127.0.0.1`-bound ports at all.

<br>

## Ports

| Port | Reachable from | Purpose |
| :- |
| `80` | public internet | certbot ACME challenge, redirects to `443` |
| `443` | public internet | the only public entry point, TLS and the WS upgrade terminate here |
| `9002` | `127.0.0.1` on the EC2 instance only | Core BE WS's own Kestrel process, never exposed directly |
| `6380` | `127.0.0.1` on the EC2 instance only | this service's own Redis |
| `6379` | `127.0.0.1` on the EC2 instance only | Core BE's session Redis, read by this service, owned by Core BE |

The EC2 security group only opens `22` (SSH), `80`, and `443`. Everything else, including `9002` and both Redis ports, is closed to the public internet at the security group level as well as bound to loopback at the container level, two layers protecting the same thing.

<br>

## One-time server setup

1. Clone this repository to the server, on the `main-stable` branch. Core BE must already be deployed and running, this service reads its session Redis.
2. No manual `config.json` step needed, `cd.yml` creates it from `config/config.json.template` on the first deploy and seeds it from the `CORE_BE_WS_SED_*` secrets below (see GitHub Actions secrets and Deploy flow).
3. No manual datastore step needed either, `cd.yml` runs `./containers.sh up` on every deploy, which brings up this service's own Redis if it is not already running, and fails the deploy immediately if it does not stay running afterward.
4. Confirm the shared reverse proxy (`vsngrp-reverse-proxy`) is already up and its certificate for `vsngrp-bews.prothegee.dev` already issued, this is separate, shared infra provisioned once, see `tasks.md` Deployment infrastructure, not a per-service step. From here on, this service's own server block (`nginx/vsngrp-bews.conf`, committed in this repo) deploys into it automatically on every `cd.yml` run, no manual nginx or certbot step needed per service.

<br>

## GitHub Actions secrets

`cd.yml` needs these repository secrets configured before it can deploy:

| Secret | Value |
| :- |
| `EC2_HOST` | the EC2 instance's address |
| `EC2_SSH_USER` | the SSH user used for deploys |
| `EC2_SSH_KEY` | the private half of a deploy key, the matching public key must be authorized on the instance |
| `CORE_BE_WS_DEPLOY_PATH` | absolute path to this repository's clone on the instance |
| `CORE_BE_WS_CONFIG_PATH` | absolute path to the real `config.json` on the instance, mounted read-only into the container |
| `PROXY_CONF_D_PATH` | absolute path to the shared reverse proxy's `conf.d` folder on the instance, this service's own `nginx/vsngrp-bews.conf` is copied there on every deploy |
| `CORE_BE_WS_SED_CONFIG_JWT_SECRET` | the shared HS256 secret, must match the value used for Core BE |
| `CORE_BE_WS_SED_CONFIG_RD` | this service's own Redis connection string, `vsngrp-core-be-ws-redis:6379,password=...`, requires a password in production (see ADR 021) |
| `CORE_BE_WS_SED_CONFIG_SESSION_RD` | Core BE's Redis connection string, used only to check sessions, `vsngrp-core-be-redis:6379,password=...`, password must match Core BE's own `CORE_BE_SED_CONFIG_RD` |
| `CORE_BE_WS_SED_DEEPSEEK_API_KEY` | the DeepSeek API key |
| `CORE_BE_WS_SED_ALLOWED_ORIGINS` | the `corsAllowedOrigins` JSON array, `["https://vsngrp-fec.prothegee.dev"]` in prod |

Both Redis instances now require a password in production, local dev stays password-free (see ADR 021), and the host in both connection strings must be the container name from `containers/docker-compose.yml`, not `127.0.0.1`, this service's app container runs on its own Docker network, not host networking. Reaching Core BE's Redis also requires this service's app container to be attached to Core BE's own Docker network, `cd.yml` does that explicitly with `docker network connect` right after starting the container, and fails the deploy immediately if Core BE's network does not exist yet (Core BE must already be deployed). `config.json` is regenerated from `config/config.json.template` and the `CORE_BE_WS_SED_*` secrets on every single deploy, not just the first one, it is a fully derived file, never hand-edited on the instance. If it is still not valid JSON after seeding, the secrets themselves contain invalid JSON syntax, `cd.yml` fails the deploy immediately rather than mounting a broken config into the container. To rotate a value (a leaked key, a rotated DeepSeek key), just update the secret, the next deploy picks it up automatically.

<br>

## Deploy flow

1. Open a pull request into `main`. `ci.yml` must pass (build, lint, tests, Docker build check).
2. Once `main` is green and ready to ship, promote it into `main-stable`, either by merging a pull request from `main` into `main-stable`, or by pushing directly to `main-stable`.
3. Any push to `main-stable` triggers `cd.yml` (a PR merge is itself a push under the hood, so both paths use the same trigger), which connects over SSH and:
   - checks that `PROXY_CONF_D_PATH` (the shared reverse proxy's `conf.d` folder) exists, and fails the deploy immediately if it does not
   - pulls the latest `main-stable`
   - brings up this service's own datastore containers (`./containers.sh up`)
   - regenerates `CORE_BE_WS_CONFIG_PATH` from `config/config.json.template` every single deploy, seeding `jwtSecret`, `redis`, `sessionRedis`, `deepSeek.apiKey`, and `corsAllowedOrigins` from the `CORE_BE_WS_SED_*` secrets
   - attaches the app container to Core BE's own Docker network so it can actually reach Core BE's session Redis by container name
   - fails the deploy immediately if the config is still not valid JSON after reseeding
   - checks that `corsAllowedOrigins` in that config includes the production Core FE origin (`https://vsngrp-fec.prothegee.dev`), and fails the deploy immediately if it does not
   - builds the image with `--build-arg GIT_SHA=$(git rev-parse --short HEAD)`
   - stops and replaces the running app container
   - copies this service's own `nginx/vsngrp-bews.conf` into `PROXY_CONF_D_PATH` and reloads the `vsngrp-reverse-proxy` container
   - runs `verify-deploy.sh`

The config file is never baked into the image. It is mounted read-only from the path in `CORE_BE_WS_CONFIG_PATH` at container start. Every deploy regenerates it fresh from the current secrets, it is never hand-edited or preserved across deploys, the secrets are the only source of truth.

The app container joins this service's own Compose network (`vsngrp-core-be-ws_default`) and is also attached to Core BE's (`vsngrp-core-be_default`) via `docker network connect`, since reaching Core BE's session Redis by container name requires actually being on Core BE's network.

<br>

## Verifying a deploy manually

```
./verify-deploy.sh "" /path/to/config.json
```

This checks the TLS certificate is valid, `GET /health` reports the expected `version` and `gitSha`, the CORS allowlist includes the production Core FE origin, a WS handshake against `/ws/chat` returns `101 Switching Protocols`, and that both the own-Redis and session-Redis connection strings inside `config.json` actually authenticate against the live containers (see ADR 022), not just that the file is valid JSON. It cannot confirm that port `9002` and the Redis ports are actually unreachable from outside the instance, that check only means something run from an external machine and stays a manual step.
