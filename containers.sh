#!/bin/bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

COMPOSE_FILE="containers/docker-compose.yml"
CONTAINER_NAMES=(
    "vsngrp-core-be-ws-redis"
)
DATA_DIRS=(
    "containers/redis/data"
)

all_running() {
    for name in "${CONTAINER_NAMES[@]}"; do
        if [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" != "true" ]; then
            return 1
        fi
    done

    return 0
}

usage() {
    echo "Usage: $0 {up|down|start|stop|cleanup}"
    exit 1
}

cmd_up() {
    docker compose -f "$COMPOSE_FILE" up -d

    if ! all_running; then
        echo "containers: ERROR, not all containers are running after up" >&2
        for name in "${CONTAINER_NAMES[@]}"; do
            if [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" != "true" ]; then
                echo "containers: $name is not running, last 20 log lines:" >&2
                docker logs --tail 20 "$name" >&2 || true
            fi
        done
        exit 1
    fi
}

cmd_down() {
    docker compose -f "$COMPOSE_FILE" down
}

cmd_start() {
    docker compose -f "$COMPOSE_FILE" start
}

cmd_stop() {
    docker compose -f "$COMPOSE_FILE" stop
}

cmd_cleanup() {
    local border
    border=$(printf '%.0s!' {1..81})

    echo "$border"
    printf '!! %-77s!!\n' "WARNING: THIS PERMANENTLY DELETES ALL CHAT DATA FOR CORE BE WS"
    printf '!! %-77s!!\n' "THE FOLLOWING DIRECTORIES WILL BE WIPED:"
    for dir in "${DATA_DIRS[@]}"; do
        printf '!! %-77s!!\n' "  - ${dir}"
    done
    printf '!! %-77s!!\n' "THIS CANNOT BE UNDONE"
    echo "$border"

    read -r -p "Type YES (all caps) to confirm: " CONFIRMATION
    if [ "$CONFIRMATION" != "YES" ]; then
        echo "containers: cleanup aborted, no changes made"
        exit 1
    fi

    echo "containers: bringing the stack down"
    docker compose -f "$COMPOSE_FILE" down

    for dir in "${DATA_DIRS[@]}"; do
        echo "containers: wiping ${dir}"
        # Container-owned files (e.g. Redis running as its own uid under
        # rootless engines) are not always deletable directly by the host user, even
        # with 0777 on the parent directory, so the wipe itself also runs in a container.
        docker run --rm -v "$(pwd)/${dir}:/target" alpine sh -c 'rm -rf /target/*'
    done

    echo "containers: cleanup done, data directories are empty"
}

if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
    case "${1:-}" in
        up) cmd_up ;;
        down) cmd_down ;;
        start) cmd_start ;;
        stop) cmd_stop ;;
        cleanup) cmd_cleanup ;;
        *) usage ;;
    esac
fi
