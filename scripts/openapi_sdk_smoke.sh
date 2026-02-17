#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="$(mktemp -d)"
API_URL="http://127.0.0.1:5078"
OPENAPI_FILE="${TMP_DIR}/openapi.json"
SDK_FILE="${TMP_DIR}/CondivaSdkClient.cs"
TOOL_DIR="${TMP_DIR}/tools"

cleanup() {
  if [[ -n "${APP_PID:-}" ]]; then
    kill "${APP_PID}" >/dev/null 2>&1 || true
    wait "${APP_PID}" >/dev/null 2>&1 || true
  fi
  rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

cd "${ROOT_DIR}"

ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project Condiva.Api \
  --configuration Release \
  --no-build \
  --urls "${API_URL}" \
  >"${TMP_DIR}/api.log" 2>&1 &
APP_PID=$!

for _ in $(seq 1 60); do
  if curl -fsS "${API_URL}/swagger/v1/swagger.json" -o "${OPENAPI_FILE}"; then
    break
  fi
  sleep 1
done

if [[ ! -s "${OPENAPI_FILE}" ]]; then
  echo "OpenAPI document was not generated."
  exit 1
fi

grep -q "\"/api/notifications/unread-count\"" "${OPENAPI_FILE}"
grep -q "\"UnreadCountDto\"" "${OPENAPI_FILE}"

dotnet tool install --tool-path "${TOOL_DIR}" NSwag.ConsoleCore --version 14.6.1
"${TOOL_DIR}/nswag" openapi2csclient \
  /input:"${OPENAPI_FILE}" \
  /classname:CondivaSdkClient \
  /namespace:Condiva.Sdk.Smoke \
  /output:"${SDK_FILE}"

if [[ ! -s "${SDK_FILE}" ]]; then
  echo "SDK smoke generation failed: empty output."
  exit 1
fi

grep -q "class CondivaSdkClient" "${SDK_FILE}"
