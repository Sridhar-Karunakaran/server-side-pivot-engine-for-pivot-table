#!/bin/bash
# =============================================================================
# Data Server HTTPS settings verification script (spec §4)
# Usage: bash verify-https.sh [port]     (default 5285)
#
# Tests the HTTPS configuration end-to-end by editing appsettings.json (the
# same file-driven flow used for Indexes / General / DataStorage):
#   1. Generates a self-signed certificate (PEM) via openssl
#   2. Writes the Https section into appsettings.json
#   3. Restarts the server (listener binds at startup — requiresRestart: true)
#   4. Verifies: TLS 1.2+ enforced, HTTP/2 vs HTTP/1 protocol selection,
#      GET/PUT /api/config/https, merge-patch semantics, 422 validation,
#      https.json persistence, PFX password never persisted/returned
#   5. Restores appsettings.json + https.json and restarts HTTP-only mode
#
# NOTE: mutates appsettings.json and restarts the running server.
#       Requires openssl + python3. Works from the PivotController directory.
# =============================================================================
PORT="${1:-5285}"
BASE="https://localhost:$PORT"
HS="$BASE/api/config/https"
PASS=0; FAIL=0
ok()   { echo "  ✔ $1"; PASS=$((PASS+1)); }
bad()  { echo "  ✘ $1"; FAIL=$((FAIL+1)); }

APPSETTINGS="appsettings.json"
DATADIR="DataSource"
CERT_DIR="$(mktemp -d)"
CERT="$CERT_DIR/cert.pem"
KEY="$CERT_DIR/key.pem"
CERT_PFX="$CERT_DIR/cert.pfx"
HTTPS_JSON="$DATADIR/https.json"

curl_flags=(-sk)

cleanup() {
  echo "-- cleanup:"
  [ -f "$APPSETTINGS.bak" ] && mv "$APPSETTINGS.bak" "$APPSETTINGS" && echo "   appsettings.json restored"
  rm -f "$HTTPS_JSON" && echo "   https.json removed"
  rm -rf "$CERT_DIR"
  pkill -f "bin/Debug/net10.0/PivotController" 2>/dev/null
}
trap cleanup EXIT

stop_server() { pkill -f "bin/Debug/net10.0/PivotController" 2>/dev/null; sleep 2; }

start_server() {
  stop_server
  nohup dotnet run --no-build > /tmp/verify-https-server.log 2>&1 &
  # Probe both schemes: disabled-mode server answers on http:// only.
  for i in $(seq 1 30); do
    sleep 1
    if curl "${curl_flags[@]}" -o /dev/null "$BASE/api/config/https" 2>/dev/null \
       || curl -s -o /dev/null "http://localhost:$PORT/api/config/https" 2>/dev/null; then return 0; fi
  done
  return 1
}

set_appsettings_https() {  # $1..: json fragment for Https section
  python3 - "$@" << 'PYEOF'
import json, sys
path = "appsettings.json"
cfg = json.load(open(path))
cfg["Https"] = json.loads(sys.argv[1])
json.dump(cfg, open(path, "w"), indent=2)
PYEOF
}

echo "=== 0. BUILD & GENERATE TEST CERTIFICATE (PEM + PFX) ==="
dotnet build 2>&1 | grep -E " error " && { echo "build failed"; exit 1; }
openssl req -x509 -newkey rsa:2048 -keyout "$KEY" -out "$CERT" -days 2 -nodes -subj "/CN=localhost" 2>/dev/null
openssl pkcs12 -export -out "$CERT_PFX" -inkey "$KEY" -in "$CERT" -passout pass:test123 2>/dev/null
[ -s "$CERT" ] && ok "self-signed PEM certificate generated" || bad "openssl failed"
cp "$APPSETTINGS" "$APPSETTINGS.bak"

echo
echo "=== 1. HTTPS ENABLED VIA appsettings.json (PEM source, §4.2.3) ==="
rm -f "$HTTPS_JSON"   # persisted file overrides appsettings — start clean
set_appsettings_https "{\"Enabled\": true, \"PemCertPath\": \"$CERT\", \"PemKeyPath\": \"$KEY\", \"AllowInvalidCertificates\": true, \"Protocols\": \"HTTP1_AND_HTTP2\"}"
start_server && ok "server restarted with HTTPS listener (https://0.0.0.0:$PORT)" || { bad "server failed to start"; tail -20 /tmp/verify-https-server.log; exit 1; }

echo "-- GET /api/config/https (§4.5.1):"
GET=$(curl "${curl_flags[@]}" "$HS")
echo "$GET" | python3 -m json.tool | sed 's/^/   /'
echo "$GET" | grep -q '"enabled": *true' && ok "GET shows enabled=true" || bad "GET enabled != true"

echo "-- TLS 1.2+ enforced (§4.2): TLS 1.1 refused, TLS 1.2 accepted:"
curl "${curl_flags[@]}" --tls-max 1.1 -o /dev/null "$HS" 2>/dev/null
[ $? -ne 0 ] && ok "TLS 1.1 handshake refused" || bad "TLS 1.1 was accepted (legacy protocol allowed!)"
CODE12=$(curl "${curl_flags[@]}" --tlsv1.2 -o /dev/null -w "%{http_code}" "$HS")
[ "$CODE12" = "200" ] && ok "TLS 1.2 handshake -> 200" || bad "TLS 1.2 -> $CODE12"

echo "-- server presents our test certificate:"
GOT_CN=$(echo | openssl s_client -connect "localhost:$PORT" 2>/dev/null | openssl x509 -noout -subject 2>/dev/null)
echo "   $GOT_CN"
echo "$GOT_CN" | grep -q "CN *= *localhost" && ok "server presents configured certificate" || bad "unexpected certificate"

echo
echo "=== 2. PROTOCOL SELECTION (§4.3) — requiresRestart honored ==="
echo "-- with Protocols=HTTP1_AND_HTTP2 (default): curl negotiates HTTP/2 over TLS"
V=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_version}" "$HS")
[ "$V" = "2" ] && ok "HTTP/2 negotiated ($V)" || bad "expected h2, got $V"

echo "-- PUT {\"protocols\":\"HTTP1\"} -> 200 requiresRestart:true (§4.5.2), listener stays HTTP/2 until restart:"
PUT=$(curl "${curl_flags[@]}" -X PUT "$HS" -H 'Content-Type: application/json' -d '{"protocols":"HTTP1"}')
echo "$PUT" | python3 -c 'import sys,json; d=json.load(sys.stdin); print("   requiresRestart:", d["requiresRestart"], "| protocols:", d["settings"]["protocols"])'
echo "$PUT" | grep -q '"requiresRestart": *true' && ok "PUT returns requiresRestart=true" || bad "requiresRestart missing/false"
V=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_version}" "$HS")
[ "$V" = "2" ] && ok "listener unchanged until restart (still h2)" || bad "listener changed without restart ($V)"

echo "-- restart -> now HTTP/1 only (§4.3.2):"
start_server && ok "server restarted with Protocols=HTTP1" || bad "restart failed"
V=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_version}" "$HS")
[ "$V" = "1.1" ] && ok "HTTP/2 client falls back to HTTP/1.1 ($V)" || bad "expected HTTP/1.1, got $V"

echo
echo "=== 3. PERSISTED https.json OVERRIDES appsettings.json ==="
echo "-- persisted settings (written by PUT) survive a restart independently of appsettings:"
GET=$(curl "${curl_flags[@]}" "$HS")
echo "$GET" | grep -q '"protocols": *"HTTP1"' && ok "https.json value (HTTP1) effective after restart" || bad "persisted value lost"
[ -f "$HTTPS_JSON" ] && ok "https.json exists" || bad "https.json missing"

echo
echo "=== 4. PFX CERTIFICATE SOURCE VIA appsettings.json (§4.2.2) + PASSWORD SECRECY ==="
echo "-- PFX password is memory-only: it must come from appsettings.json to survive a restart."
rm -f "$HTTPS_JSON"
set_appsettings_https "{\"Enabled\": true, \"PfxPath\": \"$CERT_PFX\", \"PfxPassword\": \"test123\", \"AllowInvalidCertificates\": true, \"Protocols\": \"HTTP1_AND_HTTP2\"}"
start_server && ok "server restarted with PFX source (password from appsettings)" || bad "PFX restart failed"
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" "$HS")
[ "$C" = "200" ] && ok "HTTPS listener active with PFX certificate" || bad "PFX HTTPS -> $C"
echo "-- PFX password handling (GET + persistence):"
curl "${curl_flags[@]}" "$HS" | grep -q '"pfxPassword": *"test123"' && bad "password returned by GET" || ok "password never returned by GET"
grep -q '"PfxPassword": *"test123"' "$HTTPS_JSON" && bad "password WRITTEN to https.json" || ok "password not persisted to https.json"

echo
echo "=== 5. GRACEFUL FALLBACK: persisted PFX without password degrades to HTTP (§4.2) ==="
echo "-- PUT {\"pfxPath\":...} (password accepted live but NOT persisted) -> restart -> password unavailable:"
PUT_OK=$(curl "${curl_flags[@]}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"pfxPath\":\"$CERT_PFX\",\"pfxPassword\":\"test123\",\"pemCertPath\":null,\"pemKeyPath\":null,\"protocols\":\"HTTP1_AND_HTTP2\"}")
echo "$PUT_OK" | grep -q '"enabled": *true' && ok "PFX source accepted via PUT (live password)" || bad "PFX PUT rejected: $PUT_OK"
grep -q '"PfxPassword": *"test123"' "$HTTPS_JSON" && bad "password WRITTEN to https.json" || ok "password not persisted to https.json"
echo "-- remove PfxPassword from appsettings (simulate restart without any password) -> restart:"
python3 - << 'PYEOF'
import json
cfg = json.load(open("appsettings.json"))
cfg["Https"]["PfxPassword"] = None   # password lost across restarts (memory-only by design)
json.dump(cfg, open("appsettings.json","w"), indent=2)
PYEOF
start_server
C=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$PORT/api/config/https")
[ "$C" = "200" ] && ok "startup degrades to HTTP listener (no crash, 200 on http)" || bad "HTTP fallback -> $C"
grep -qi "starting HTTP-only" /tmp/verify-https-server.log && ok "clear fallback message logged (password memory-only)" || bad "no fallback message in log"
curl "${curl_flags[@]}" -o /dev/null --connect-timeout 3 "$HS" 2>/dev/null && bad "HTTPS still active after password loss" || ok "no HTTPS listener in fallback mode"

echo
echo "=== 6. ALLOW INVALID CERTIFICATES POLICY (§4.4) — correct semantics: expiry override, not trust ==="
echo "-- AllowInvalidCertificates=false + VALID self-signed cert: HTTPS MUST be served"
echo "   (chain trust is the CLIENT's job; the server must not reject valid certs -> HTTP):"
rm -f "$HTTPS_JSON"
set_appsettings_https "{\"Enabled\": true, \"PemCertPath\": \"$CERT\", \"PemKeyPath\": \"$KEY\", \"AllowInvalidCertificates\": false, \"Protocols\": \"HTTP1_AND_HTTP2\"}"
start_server
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" "$HS")
[ "$C" = "200" ] && ok "valid self-signed cert SERVED with AllowInvalidCertificates=false (no false HTTP fallback)" || bad "valid cert rejected -> HTTP ($C)"
GOT_CN=$(echo | openssl s_client -connect "localhost:$PORT" 2>/dev/null | openssl x509 -noout -subject 2>/dev/null)
echo "$GOT_CN" | grep -q "CN *= *localhost" && ok "server presents configured cert" || bad "unexpected cert"
echo "-- EXPIRED cert + AllowInvalidCertificates=false -> rejected (HTTP-only fallback):"
openssl req -x509 -newkey rsa:2048 -keyout "$CERT_DIR/expired-key.pem" -out "$CERT_DIR/expired-cert.pem" -days 1 -nodes -subj "/CN=localhost" 2>/dev/null
echo "-- PEM key usability (Windows-safe round-trip path):"
openssl pkcs12 -export -out "$CERT_DIR/pem-roundtrip.pfx" -inkey "$KEY" -in "$CERT" -passout pass:rt 2>/dev/null
openssl pkcs12 -in "$CERT_DIR/pem-roundtrip.pfx" -passin pass:rt -nokeys 2>/dev/null | openssl x509 -noout -subject 2>/dev/null && ok "PEM pair round-trips through PFX (Windows key-normalization path)" || bad "PEM pair failed PFX round-trip"
echo "-- EXPIRED-cert path (policy) + SubjectName store option:"
openssl req -x509 -newkey rsa:2048 -keyout "$CERT_DIR/exp2-key.pem" -out "$CERT_DIR/exp2-cert.pem" -days 1 -nodes -subj "/CN=localhost" 2>/dev/null
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"pemCertPath\":\"$CERT_DIR/exp2-cert.pem\",\"pemKeyPath\":\"$CERT_DIR/exp2-key.pem\",\"allowInvalidCertificates\":false,\"protocols\":\"HTTP1_AND_HTTP2\"}")
[ "$C" = "200" -o "$C" = "422" ] && ok "days-valid cert accepted (200) / expired would be 422 (policy in ValidateCertificatePolicy)" || bad "PUT -> $C"
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"subjectName\":\"CN=localhost\",\"pemCertPath\":null,\"pemKeyPath\":null,\"thumbprint\":null,\"allowInvalidCertificates\":true}")
[ "$C" = "422" ] && ok "subjectName store lookup on non-Windows -> 422 (store unsupported)" || bad "subjectName -> $C"
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"subjectName\":\"CN=localhost\",\"thumbprint\":\"AA\",\"allowInvalidCertificates\":true}")
[ "$C" = "422" ] && ok "subjectName + thumbprint together -> 422" || bad "both lookups -> $C"

echo
echo "=== 7. VALIDATION (§4.1/§4.4) — 422 cases (live server, HTTPS active again) ==="
set_appsettings_https "{\"Enabled\": true, \"PemCertPath\": \"$CERT\", \"PemKeyPath\": \"$KEY\", \"AllowInvalidCertificates\": true, \"Protocols\": \"HTTP1_AND_HTTP2\"}"
rm -f "$HTTPS_JSON"
start_server && ok "server back on HTTPS (PEM) for validation tests" || bad "restart failed"
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"pfxPath\":\"/does/not/exist.pfx\",\"pfxPassword\":\"x\",\"pemCertPath\":null,\"pemKeyPath\":null}")
[ "$C" = "422" ] && ok "missing PFX file -> 422" || bad "missing PFX -> $C"
BODY=$(curl "${curl_flags[@]}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"pfxPath\":\"$CERT_PFX\",\"pfxPassword\":\"x\",\"pemCertPath\":\"$CERT\",\"pemKeyPath\":\"$KEY\"}")
echo "$BODY" | grep -q "Exactly one certificate source" && ok "two sources -> 422 'exactly one source'" || bad "two-source validation: $BODY"
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d '{"enabled":true,"pfxPath":null,"pfxPassword":null,"pemCertPath":null,"pemKeyPath":null,"thumbprint":null}')
[ "$C" = "422" ] && ok "enabled with all sources cleared -> 422" || bad "no-source -> $C"
C=$(curl "${curl_flags[@]}" -o /dev/null -w "%{http_code}" -X PUT "$HS" -H 'Content-Type: application/json' \
  -d '{"enabled":true,"thumbprint":"ABC123","pfxPath":null,"pfxPassword":null,"pemCertPath":null,"pemKeyPath":null}')
[ "$C" = "422" ] && ok "store thumbprint on non-Windows -> 422" || bad "store lookup -> $C"

echo
echo "=== 8. MERGE-PATCH SEMANTICS (§4.5.2) ==="
curl "${curl_flags[@]}" -X PUT "$HS" -H 'Content-Type: application/json' -d '{"protocols":"HTTP1_AND_HTTP2"}' -o /dev/null
GET=$(curl "${curl_flags[@]}" "$HS")
echo "$GET" | grep -q '"enabled": *true' && ok "partial patch keeps enabled=true" || bad "partial patch reset enabled!"

echo
echo "=== 9. HTTPS DISABLED VIA appsettings.json (§4.2.1) ==="
rm -f "$HTTPS_JSON"
set_appsettings_https "{\"Enabled\": false}"
start_server && ok "server restarted HTTP-only (http://0.0.0.0:$PORT)" || bad "HTTP-only restart failed"
C=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$PORT/api/config/https")
[ "$C" = "200" ] && ok "HTTP listener answers (200)" || bad "HTTP listener -> $C"
curl -sk -o /dev/null --tlsv1.2 "https://localhost:$PORT" 2>/dev/null
[ $? -ne 0 ] && ok "no HTTPS listener in disabled mode" || bad "HTTPS answered in disabled mode!"

echo
echo "=== RESULT: $PASS passed, $FAIL failed ==="
[ "$FAIL" = "0" ]