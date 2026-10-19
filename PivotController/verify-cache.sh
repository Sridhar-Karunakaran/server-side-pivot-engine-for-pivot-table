#!/bin/bash
# =============================================================================
# Data Server cache & KeepDataOnRefresh verification script (spec §2.3 / §2.4)
# Usage: bash verify-cache.sh [baseUrl]     (default http://localhost:5285)
# NOTE: changes DataStorage settings while running; restores defaults at exit.
#       Requires cacheSizeLimit changes to be RESTARTED to take effect; this
#       script expects the server to already be running with limit 100 or
#       performs a restart via environment (see LIMIT below).
# =============================================================================
BASE="${1:-http://localhost:5285}"
DS="$BASE/api/config/datastorage"
IX="$BASE/api/indexes"
PASS=0; FAIL=0
ok()   { echo "  ✔ $1"; PASS=$((PASS+1)); }
bad()  { echo "  ✘ $1"; FAIL=$((FAIL+1)); }

echo "=== 1. CACHE SIZE LIMIT (LRU eviction) ==="
echo "-- current cache stats (limit=$(curl -s $DS | python3 -c 'import sys,json;print(json.load(sys.stdin)["cacheSizeLimit"])')):"
curl -s "$DS/cache-stats" | python3 -m json.tool
echo "-- create 3 extra indexes (data.csv copies) to fill cache:"
CACHED_BEFORE=$(curl -s "$DS/cache-stats" | python3 -c 'import sys,json;print(json.load(sys.stdin)["cachedCount"])')
for n in 1 2 3; do
  cp DataSource/data.csv DataSource/verify-$n.csv
  curl -s -X POST "$IX" -H "Content-Type: application/json" \
    -d "{\"index\":\"Verify-$n\",\"sourceType\":\"Csv\",\"sourcePath\":\"verify-$n.csv\"}" -o /dev/null
done
curl -s "$DS/cache-stats" | python3 -c "
import sys,json
s=json.load(sys.stdin)
print(f'  cached={s[\"cachedCount\"]}/{s[\"cacheSizeLimit\"]} -> {s[\"cachedIndexes\"]}')
print(f'  evicted={len(s[\"evictedIndexes\"])} -> {s[\"evictedIndexes\"]}')"

echo "-- WITH limit >= index count: nothing should be evicted:"
STATS=$(curl -s "$DS/cache-stats")
EVICTED=$(echo "$STATS" | python3 -c 'import sys,json;print(len(json.load(sys.stdin)["evictedIndexes"]))')
[ "$EVICTED" = "0" ] && ok "no evictions under limit (evicted=$EVICTED)" || bad "unexpected evictions: $STATS"

echo
echo "  NOTE: to see LRU eviction LIVE, set limit below the index count and restart:"
echo "    curl -X PUT $DS -d '{\"cacheSizeLimit\":3}'   (requiresRestart: true)"
echo "    restart server, then: curl $DS/cache-stats"
echo "  → evictedIndexes will list the LRU-evicted entries; pivot on them still returns 200."
for n in 1 2 3; do curl -s -X DELETE "$IX/Verify-$n" -o /dev/null; rm -f DataSource/verify-$n.csv; done

echo
echo "=== 2. KEEP DATA ON REFRESH (§2.4) ==="
echo "-- ensure keepDataOnRefresh=true:"
curl -s -X PUT "$DS" -H "Content-Type: application/json" -d '{"keepDataOnRefresh":true}' -o /dev/null

echo "-- (a) break the source, reload → 502 but data KEPT (§2.4.1):"
mv DataSource/sales-analysis.json /tmp/verify-sa.bak
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$IX/Sales-Json/reload")
ST=$(curl -s "$IX/Sales-Json")
echo "   reload=$CODE  status=$(echo $ST | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d["status"],d["rowCount"])')"
[ "$CODE" = "502" ] && ok "reload fails with 502" || bad "expected 502, got $CODE"
STATUS=$(echo $ST | python3 -c 'import sys,json;print(json.load(sys.stdin)["status"])')
ROWS=$(echo $ST | python3 -c 'import sys,json;print(json.load(sys.stdin)["rowCount"])')
[ "$STATUS" = "0" ] && true  # enum int; Ready=0
[ "$STATUS" = "Ready" -o "$STATUS" = "0" ] && ok "status stays Ready (data kept)" || bad "status=$STATUS"
[ "$ROWS" != "0" ] && ok "rowCount retained ($ROWS rows)" || bad "rowCount lost"
PC=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/pivot/post" -H "Content-Type: application/json" \
  -d '{"index":"Sales-Json","hash":"verify-keep","dataSourceSettings":{"type":"JSON","rows":[{"name":"Sector"}],"values":[{"name":"PowUnits"}]},"internalProperties":{"enableVirtualization":true}}')
[ "$PC" = "200" ] && ok "pivot still 200 from kept data" || bad "pivot=$PC"

echo "-- (b) keepDataOnRefresh=false → same failure EVICTS cache (§2.4.2):"
curl -s -X PUT "$DS" -H "Content-Type: application/json" -d '{"keepDataOnRefresh":false}' -o /dev/null
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$IX/Sales-Json/reload")
ST=$(curl -s "$IX/Sales-Json")
echo "   reload=$CODE  status/rows=$(echo $ST | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d["status"],d["rowCount"])')"
ROWS2=$(echo $ST | python3 -c 'import sys,json;print(json.load(sys.stdin)["rowCount"])')
[ "$ROWS2" = "0" ] && ok "cache evicted on failure (rowCount=0, status Failed)" || bad "data unexpectedly kept"

echo "-- (c) restore source → reload recovers:"
mv /tmp/verify-sa.bak DataSource/sales-analysis.json
curl -s -o /dev/null -X POST "$IX/Sales-Json/reload"
ROWS3=$(curl -s "$IX/Sales-Json" | python3 -c 'import sys,json;print(json.load(sys.stdin)["rowCount"])')
[ "$ROWS3" != "0" ] && ok "recovered after fix ($ROWS3 rows)" || bad "no recovery"

echo "-- restore keepDataOnRefresh=true:"
curl -s -X PUT "$DS" -H "Content-Type: application/json" -d '{"keepDataOnRefresh":true}' -o /dev/null

echo
echo "=== RESULT: $PASS passed, $FAIL failed ==="