#!/bin/sh

# Simple DSM UI entrypoint that redirects to the proxied Duplicati UI.
echo "Status: 302 Found"
echo "Location: /duplicati/"
echo "Content-Type: text/html"
echo
echo "<html><head><meta http-equiv=\"refresh\" content=\"0;url=/duplicati/\"></head><body>Redirecting to Duplicati ...</body></html>"

