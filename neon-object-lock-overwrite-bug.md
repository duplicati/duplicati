# A PUT overwrites an object under Object Lock retention

Neon Object Storage lets a plain `PutObject` replace an object that has an Object Lock retention date in the future. The upload returns `200`, the original bytes are gone, and the retention is removed with them. This happens in both `GOVERNANCE` and `COMPLIANCE` mode.

On AWS S3, Object Lock requires versioning, so an overwrite creates a new version and the locked version stays. On Neon, `GetBucketVersioning` returns `Enabled` for a lock-enabled bucket, but `ListObjectVersions` only ever shows a single `null` version. Deletes are enforced correctly (`403`), so the gap is only in overwrites.

Backup tools such as Duplicati (`--remote-file-lock-duration`) rely on this to protect backups from ransomware: an attacker holding the access keys must not be able to destroy locked files. On Neon they can, by uploading over each file.

## Steps to reproduce

1. Export a branch credential (`storage:read` and `storage:write`):

   ```bash
   export AWS_ENDPOINT_URL_S3="https://<branch-id>.storage.c-<n>.<region>.aws.neon.tech"
   export AWS_ACCESS_KEY_ID="nak_live_..."
   export AWS_SECRET_ACCESS_KEY="nsk_live_..."
   export AWS_REGION="us-east-2"
   export BUCKET="lock-repro"
   SIG=(--aws-sigv4 "aws:amz:${AWS_REGION}:s3" --user "${AWS_ACCESS_KEY_ID}:${AWS_SECRET_ACCESS_KEY}" \
     -H "x-amz-content-sha256: UNSIGNED-PAYLOAD")
   ```

2. Create a bucket with Object Lock enabled, and upload an object:

   ```bash
   curl -s -w "%{http_code}\n" "${SIG[@]}" -X PUT \
     -H "x-amz-bucket-object-lock-enabled: true" "${AWS_ENDPOINT_URL_S3}/${BUCKET}"
   # 200
   echo original | curl -s -w "%{http_code}\n" "${SIG[@]}" -X PUT --data-binary @- \
     "${AWS_ENDPOINT_URL_S3}/${BUCKET}/f.txt"
   # 200
   ```

3. Put a `COMPLIANCE` retention one day out:

   ```bash
   UNTIL=$(date -u -v+1d +%Y-%m-%dT%H:%M:%SZ)   # GNU date: date -u -d '+1 day' ...
   BODY="<Retention xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><Mode>COMPLIANCE</Mode><RetainUntilDate>${UNTIL}</RetainUntilDate></Retention>"
   MD5=$(printf '%s' "$BODY" | openssl dgst -md5 -binary | base64)
   curl -s -w "%{http_code}\n" "${SIG[@]}" -H "Content-MD5: ${MD5}" -X PUT \
     --data-binary "$BODY" "${AWS_ENDPOINT_URL_S3}/${BUCKET}/f.txt?retention"
   # 200
   ```

4. Deleting is blocked, as expected:

   ```bash
   curl -s -o /dev/null -w "%{http_code}\n" "${SIG[@]}" -X DELETE "${AWS_ENDPOINT_URL_S3}/${BUCKET}/f.txt"
   # 403
   ```

5. Overwrite the key, then read it back:

   ```bash
   echo replaced | curl -s -w "%{http_code}\n" "${SIG[@]}" -X PUT --data-binary @- \
     "${AWS_ENDPOINT_URL_S3}/${BUCKET}/f.txt"
   # 200
   curl -s "${SIG[@]}" "${AWS_ENDPOINT_URL_S3}/${BUCKET}/f.txt"
   # replaced
   curl -s "${SIG[@]}" "${AWS_ENDPOINT_URL_S3}/${BUCKET}/f.txt?retention"
   # <Error>... (no retention any more)
   curl -s "${SIG[@]}" "${AWS_ENDPOINT_URL_S3}/${BUCKET}?versions"
   # one <Version> with <VersionId>null</VersionId>
   ```

**Expected:** step 5 keeps the original object as a non-current version with its retention intact, as on AWS S3. If Neon cannot keep versions yet, it should reject `x-amz-bucket-object-lock-enabled` and `PutObjectLockConfiguration` with `501 NotImplemented`, so clients know their data is not protected.

**Actual:** the overwrite succeeds, the original bytes and the lock are lost, and no prior version exists.

Tested 2026-10-21 against an `aws-us-east-2` branch, with curl and with Duplicati's `--remote-file-lock-duration=1D` (AWSSDK.S3 client).
