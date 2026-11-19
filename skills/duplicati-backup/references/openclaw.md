# Connecting Duplicati to OpenClaw

These are steps for the user. Do not perform them yourself.

1. Create a Duplicati backup that covers the OpenClaw folders, for example `~/.openclaw`, with a destination on another machine or in the cloud.
2. Issue a token for OpenClaw, limited to that backup:

    ```bash
    duplicati-server-util issue-mcp-token --name="OpenClaw" --level=Restore --backup="OpenClaw-Backup" --expires-in=90D
    ```

3. Add the server to OpenClaw and verify it:

    ```bash
    openclaw mcp set duplicati '{"url":"http://localhost:8200/mcp","transport":"streamable-http","headers":{"Authorization":"Bearer <token>"}}'
    ```

    ```bash
    openclaw mcp doctor duplicati --probe
    ```
