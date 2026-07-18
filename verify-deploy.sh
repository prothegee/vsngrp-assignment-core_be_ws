#!/bin/bash
set -euo pipefail

DOMAIN="vsngrp-bews.prothegee.dev"
FE_ORIGIN="https://vsngrp-fec.prothegee.dev"
EXPECTED_GIT_SHA="${1:-$(git rev-parse --short HEAD)}"

echo "verify-deploy: checking TLS certificate for ${DOMAIN}"
CERT_END_DATE=$(echo | openssl s_client -servername "$DOMAIN" -connect "${DOMAIN}:443" 2>/dev/null | openssl x509 -noout -enddate | cut -d= -f2)
CERT_END_EPOCH=$(date -d "$CERT_END_DATE" +%s)
NOW_EPOCH=$(date +%s)
if [ "$CERT_END_EPOCH" -le "$NOW_EPOCH" ]; then
    echo "verify-deploy: FAIL, TLS certificate for ${DOMAIN} is expired"
    exit 1
fi
echo "verify-deploy: TLS certificate valid until ${CERT_END_DATE}"

echo "verify-deploy: checking /health"
HEALTH_BODY=$(curl -sf "https://${DOMAIN}/health")
HEALTH_STATUS=$(echo "$HEALTH_BODY" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
HEALTH_GIT_SHA=$(echo "$HEALTH_BODY" | grep -o '"gitSha":"[^"]*"' | cut -d'"' -f4)

if [ "$HEALTH_STATUS" != "ok" ]; then
    echo "verify-deploy: FAIL, /health status was '${HEALTH_STATUS}', expected 'ok'"
    exit 1
fi

if [ "$HEALTH_GIT_SHA" != "$EXPECTED_GIT_SHA" ]; then
    echo "verify-deploy: FAIL, /health gitSha was '${HEALTH_GIT_SHA}', expected '${EXPECTED_GIT_SHA}'"
    exit 1
fi
echo "verify-deploy: /health ok, gitSha matches ${EXPECTED_GIT_SHA}"

echo "verify-deploy: checking corsAllowedOrigins for ${FE_ORIGIN}"
CORS_HEADER=$(curl -s -o /dev/null -D - \
    -X OPTIONS "https://${DOMAIN}/health" \
    -H "Origin: ${FE_ORIGIN}" \
    -H "Access-Control-Request-Method: GET" \
    | grep -i "^access-control-allow-origin:" | tr -d '\r' | cut -d' ' -f2)

if [ "$CORS_HEADER" != "$FE_ORIGIN" ]; then
    echo "verify-deploy: FAIL, Access-Control-Allow-Origin was '${CORS_HEADER}', expected '${FE_ORIGIN}'"
    exit 1
fi
echo "verify-deploy: CORS allowlist ok"

echo "verify-deploy: checking WS handshake"
WS_STATUS=$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 \
    --http1.1 \
    -H "Connection: Upgrade" \
    -H "Upgrade: websocket" \
    -H "Sec-WebSocket-Version: 13" \
    -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
    "https://${DOMAIN}/ws/chat")

if [ "$WS_STATUS" != "101" ]; then
    echo "verify-deploy: FAIL, WS handshake returned '${WS_STATUS}', expected '101'"
    exit 1
fi
echo "verify-deploy: WS handshake ok, 101 Switching Protocols"

echo "verify-deploy: all checks passed"
