#!/bin/sh

# This CGI serves the Duplicati.js file, injecting the SynoToken variable.
# This is required as the Duplicati.js file is static and cannot read its own query string.

CGI_DIR="$(dirname "$SCRIPT_FILENAME")"
REAL_JS="${CGI_DIR}/Duplicati.js"

# Extract SynoToken from QUERY_STRING (raw URL-encoded value)
TOKEN="$(printf '%s' "${QUERY_STRING:-}" | sed -n 's/.*\(^\|&\)SynoToken=\([^&]*\).*/\2/p')"

echo "Content-Type: application/javascript"
echo "Cache-Control: no-store"
echo

echo "window.SYNO_SDS_DUPLICATI_SYNOTOKEN = \"${TOKEN}\";"
if [ -r "$REAL_JS" ]; then
  cat "$REAL_JS"
else
  echo "throw new Error('Duplicati.js not readable at: $REAL_JS');"
fi