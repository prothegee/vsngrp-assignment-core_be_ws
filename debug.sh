#!/bin/bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

CONFIG_FILE="config/config.json"
CONFIG_TEMPLATE="config/config.json.template"

if [ ! -f "$CONFIG_FILE" ]; then
    echo "debug: WARNING, ${CONFIG_FILE} not found, copying ${CONFIG_TEMPLATE}"
    cp "$CONFIG_TEMPLATE" "$CONFIG_FILE"
    echo "debug: WARNING, ${CONFIG_FILE} still has CHANGE_THIS placeholders, chat will fail until real values are filled in"
fi

source ./containers.sh

echo "debug: checking datastore containers"
if all_running; then
    echo "debug: datastore containers already running"
else
    echo "debug: datastore containers not fully up, starting them"
    cmd_up
fi

cd src
dotnet run
