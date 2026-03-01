# HTTPS Certificate Security

Duplicati can automatically generate HTTPS certificates for secure web UI access. This section documents the security model and best practices.

## Certificate Approach

Browsers currently reject certificates that have a validity period for more than 90 days, with the logic that rotation should be frequent and automated to prevent long-term exposure to potential vulnerabilities. Since Duplicati uses localhost serving by default, there is no Certificate Authority (CA) to request certificates from.

If Duplicati creates a self-signed certificate, it will would need to be re-authorized after 90 days, which would require admin permissions and possibly manual intervention every 90 days. To avoid this, Duplicati generates its own Certificate Authority (CA) and uses it to sign server certificates.

The CA private key is stored in the Duplicati database file, which is encrypted with the database encryption key. When a new certificate is needed, Duplicati generates a new certificate signed by the CA. Since the CA is local and not shared with any external service, this approach provides a secure way to manage certificates without requiring external dependencies. As the CA is trusted by the user or system, this enables Duplicati to automatically renew certificates without user intervention, in a way that is trusted by the user's browser.

Be aware that while the CA is local, it is still a CA and can be used to sign certificates for other domains. If someone gains access to the Duplicati database, they can use the CA to sign certificates for other domains, essentially providing an undetected man-in-the-middle attack.

If you prefer providing your own certificate, you can do so by setting the `server-ssl-certificate` and `server-ssl-certificatepassword` settings. This will not activate auto-renewal or generate a CA.

## Certificate Authority (CA) Security Model

When HTTPS is configured, Duplicati creates a local Certificate Authority (CA) with the following security properties:

### Local-Only CA

- The CA is generated locally on your machine and is not shared with any external service
- The CA certificate is installed only in your system's local trust store
- Other machines do not trust this CA unless explicitly configured to do so
- The CA should never be exported or shared with other systems, unless you understand the security implications

### pathLenConstraint=0

The CA certificate has `pathLenConstraint=0` in its Basic Constraints extension, which means:

- The CA can sign end-entity (server) certificates
- The CA **cannot** sign subordinate/intermediate CA certificates
- This limits the scope of trust to only the certificates directly signed by this CA

### Certificate Validity Periods

- **CA Certificate:** Valid for approximately 10 years
- **Server Certificate:** Valid for 90 days
- **Auto-renewal:** Server certificates are automatically renewed 30 days before expiration

## CA Key Storage Security

The CA private key is stored with multiple layers of protection:

1. **Encryption:** The private key is encrypted using AES-256-GCM with a password-derived key (PBKDF2 with 100,000 iterations)
2. **Password Separation:** The encryption password is stored separately from the encrypted key
3. **Database Encryption:** If database field encryption is enabled, an additional encryption layer is applied

**Important:** The security of your HTTPS certificates depends on the security of your Duplicati database file.

## Database Encryption Recommendation

For maximum security, enable database field encryption

```bash
# During initial setup
duplicati-server --settings-encryption-key=<strong-password>

# Or via environment variable
export DUPLICATI__SETTINGS_ENCRYPTION_KEY=<strong-password>
```

When database encryption is enabled:

- Sensitive settings, including certificate data, receive additional encryption
- A warning notification is avoided (the system emits a warning if certificates are stored without database encryption)
- The encryption key is derived from your server passphrase

## Revocation and Compromise Response

If you suspect your CA private key has been compromised:

1. **Immediate Action:** Remove the certificates using `duplicati-configure https remove`
2. **Regenerate:** Create a new CA with `duplicati-configure https regenerate-ca`
3. **Review:** Check recent server logs and backup history for unauthorized access
4. **Monitor:** Set up alerts for unexpected certificate changes

## Trust Store Installation

The CA certificate is installed in platform-specific trust stores:

- **Windows:** `Cert:\CurrentUser\Root` (or `Cert:\LocalMachine\Root` when run as Administrator)
- **Linux:** Distribution-specific paths (`/usr/local/share/ca-certificates/`, `/etc/pki/ca-trust/source/anchors/`, etc.)
- **macOS:** System keychain via `security add-trusted-cert`

**Security Note:** Installing a CA in the system trust store gives it broad trust. While the Duplicati CA is constrained (pathLenConstraint=0, no sub-CAs), you should:

- Only install CAs you trust
- Remove the CA when no longer needed (`duplicati-configure https remove`)
- Be cautious of any CA installation prompts from unknown sources
- Understand that access to the generated CA private key will allow signing arbitrary certificates, defeating any TLS/SSL/https security for your machine

## Certificate Pinning Considerations

For high-security environments, consider implementing certificate pinning:

- Extract the CA certificate thumbprint after generation
- Configure clients or monitoring systems to expect only this specific CA
- This prevents acceptance of certificates signed by other CAs that might be installed on the system
