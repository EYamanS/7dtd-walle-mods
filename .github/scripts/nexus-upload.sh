#!/usr/bin/env bash
# Upload a mod zip to Nexus Mods via the v3 API.
# Usage: nexus-upload.sh <game_domain> <game_scoped_mod_id> <file_name> <version> <zip_path>
#
# Flow (per the v3 OpenAPI spec):
#   resolve mod  -> GET  /games/{domain}/mods/{scoped_id}          (global mod id)
#   dedupe check -> GET  /mods/{id}/files + /mod-files/{id}/versions
#   upload       -> POST /uploads, PUT file to presigned_url, POST /uploads/{id}/finalise,
#                   poll GET /uploads/{id} until state=available
#   attach       -> POST /mod-files/{file_id}/versions   (existing file: new version)
#                   POST /mod-files                      (first upload: new file)
set -euo pipefail

GAME_DOMAIN="$1"; MOD_SCOPED_ID="$2"; FILE_NAME="$3"; VERSION="$4"; ZIP_PATH="$5"
API="https://api.nexusmods.com/v3"

if [ -z "${NEXUS_API_KEY:-}" ]; then
  echo "NEXUS_API_KEY secret not set — skipping Nexus upload of $FILE_NAME."
  exit 0
fi

api() { # method path [json_body]
  local method="$1" path="$2" body="${3:-}"
  if [ -n "$body" ]; then
    curl -sS -f -X "$method" "$API$path" \
      -H "apikey: $NEXUS_API_KEY" -H "Content-Type: application/json" -d "$body"
  else
    curl -sS -f -X "$method" "$API$path" -H "apikey: $NEXUS_API_KEY"
  fi
}

echo "== $FILE_NAME v$VERSION -> Nexus ($GAME_DOMAIN/mods/$MOD_SCOPED_ID) =="

MOD_JSON=$(api GET "/games/$GAME_DOMAIN/mods/$MOD_SCOPED_ID")
MOD_ID=$(echo "$MOD_JSON" | jq -r '.data.id')
echo "Resolved mod: id=$MOD_ID name=$(echo "$MOD_JSON" | jq -r '.data.name')"

# Find existing mod file (update chain) with our name, and dedupe on version
FILES_JSON=$(api GET "/mods/$MOD_ID/files")
FILE_ID=$(echo "$FILES_JSON" | jq -r --arg n "$FILE_NAME" '.data.mod_files[] | select(.name == $n) | .id' | head -n1)

if [ -n "$FILE_ID" ] && [ "$FILE_ID" != "null" ]; then
  EXISTING=$(api GET "/mod-files/$FILE_ID/versions" | jq -r --arg v "$VERSION" '.data.versions[] | select(.version == $v) | .id' | head -n1)
  if [ -n "$EXISTING" ] && [ "$EXISTING" != "null" ]; then
    echo "Version $VERSION already on Nexus (version id $EXISTING) — nothing to do."
    exit 0
  fi
fi

# Upload session
SIZE=$(stat -c%s "$ZIP_PATH")
BASENAME=$(basename "$ZIP_PATH")
UPLOAD_JSON=$(api POST "/uploads" "{\"size_bytes\": $SIZE, \"filename\": \"$BASENAME\"}")
UPLOAD_ID=$(echo "$UPLOAD_JSON" | jq -r '.data.id')
PRESIGNED=$(echo "$UPLOAD_JSON" | jq -r '.data.presigned_url')
echo "Upload session $UPLOAD_ID created, uploading $SIZE bytes..."

curl -sS -f -X PUT "$PRESIGNED" \
  -H "Content-Disposition: attachment; filename=\"$BASENAME\"" \
  --upload-file "$ZIP_PATH" >/dev/null

api POST "/uploads/$UPLOAD_ID/finalise" >/dev/null
for i in $(seq 1 30); do
  STATE=$(api GET "/uploads/$UPLOAD_ID" | jq -r '.data.state')
  [ "$STATE" = "available" ] && break
  echo "Upload state: $STATE, waiting... ($i)"
  sleep 2
done
if [ "$STATE" != "available" ]; then
  echo "ERROR: upload never became available (state: $STATE)"
  exit 1
fi

BODY_COMMON="\"upload_id\": \"$UPLOAD_ID\", \"name\": \"$FILE_NAME\", \"version\": \"$VERSION\", \"file_category\": \"main\", \"primary_mod_manager_download\": true, \"allow_mod_manager_download\": true, \"update_mod_version\": true"

if [ -n "$FILE_ID" ] && [ "$FILE_ID" != "null" ]; then
  echo "Adding version $VERSION to existing mod file $FILE_ID..."
  RESULT=$(api POST "/mod-files/$FILE_ID/versions" "{$BODY_COMMON, \"archive_existing_file\": true}")
  echo "Created version: $(echo "$RESULT" | jq -c '.data.version')"
else
  echo "Creating first mod file on the mod page..."
  RESULT=$(api POST "/mod-files" "{$BODY_COMMON, \"mod_id\": \"$MOD_ID\"}")
  echo "Created mod file: $(echo "$RESULT" | jq -c '.data')"
fi

echo "== $FILE_NAME v$VERSION uploaded to Nexus successfully =="
