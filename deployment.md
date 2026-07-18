# Deployment Guide: Core BE WS

<br>

## Overview

Core BE WS runs on the same AWS EC2 instance as Core BE and Core FE, each as its own `docker compose` project. A single shared reverse-proxy container (`vsngrp-reverse-proxy`, `network_mode: host`) owns ports 80 and 443 and is the only process reachable from outside the instance, it terminates TLS and reverse-proxies to each service's app container on `127.0.0.1`, including the WS upgrade headers this service needs. This service owns and deploys its own server block, `nginx/vsngrp-bews.conf`, see Deploy flow below and `tasks.md` Deployment infrastructure for the shared proxy itself.

This service's own Redis (conversations, chat log, token budget) binds to `127.0.0.1` only. It also reads Core BE's session Redis over the loopback interface, both stay unreachable from outside the instance.

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
3. Provision this service's own datastore once:
   ```
   chmod 777 containers/redis/data
   docker compose -f containers/docker-compose.yml up -d
   ```
   This container is not touched by routine deploys, only by this one-time step (and by manual maintenance later).
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
| `CORE_BE_WS_SED_CONFIG_RD` | this service's own Redis connection string (conversations, chat log) |
| `CORE_BE_WS_SED_CONFIG_SESSION_RD` | Core BE's Redis connection string, used only to check sessions |
| `CORE_BE_WS_SED_DEEPSEEK_API_KEY` | the DeepSeek API key |
| `CORE_BE_WS_SED_ALLOWED_ORIGINS` | the `corsAllowedOrigins` JSON array, `["https://vsngrp-fec.prothegee.dev"]` in prod |

The `CORE_BE_WS_SED_*` secrets are only read once, when `CORE_BE_WS_CONFIG_PATH` does not exist yet and `cd.yml` creates it from the template. To rotate a value later (a leaked key, a rotated DeepSeek key), edit `config.json` directly on the instance, or delete it and let the next deploy recreate and reseed it from the current secrets.

<br>

## Deploy flow

1. Open a pull request into `main`. `ci.yml` must pass (build, lint, tests, Docker build check).
2. Once `main` is green and ready to ship, open a pull request from `main` into `main-stable` and approve it.
3. Merging into `main-stable` triggers `cd.yml`, which connects over SSH and:
   - checks that `PROXY_CONF_D_PATH` (the shared reverse proxy's `conf.d` folder) exists, and fails the deploy immediately if it does not
   - pulls the latest `main-stable`
   - if `CORE_BE_WS_CONFIG_PATH` does not exist yet, creates it from `config/config.json.template` and seeds `jwtSecret`, `redis`, `sessionRedis`, `deepSeek.apiKey`, and `corsAllowedOrigins` from the `CORE_BE_WS_SED_*` secrets
   - checks that `corsAllowedOrigins` in that config includes the production Core FE origin (`https://vsngrp-fec.prothegee.dev`), and fails the deploy immediately if it does not
   - builds the image with `--build-arg GIT_SHA=$(git rev-parse --short HEAD)`
   - stops and replaces the running app container
   - copies this service's own `nginx/vsngrp-bews.conf` into `PROXY_CONF_D_PATH` and reloads the `vsngrp-reverse-proxy` container
   - runs `verify-deploy.sh`

The config file is never baked into the image. It is mounted read-only from the path in `CORE_BE_WS_CONFIG_PATH` at container start. The first deploy creates and seeds it automatically, every deploy after that just reuses the existing file, `cd.yml` never overwrites a `config.json` that already exists.

The app container joins this service's own Compose network (`vsngrp-core-be-ws_default`), not Core BE's, since it only needs to reach Core BE's session Redis over `127.0.0.1`, not over that other stack's internal network.

<br>

## Verifying a deploy manually

```
./verify-deploy.sh
```

This checks the TLS certificate is valid, `GET /health` reports the expected `version` and `gitSha`, the CORS allowlist includes the production Core FE origin, and a WS handshake against `/ws/chat` returns `101 Switching Protocols`. It cannot confirm that port `9002` and the Redis ports are actually unreachable from outside the instance, that check only means something run from an external machine and stays a manual step.
