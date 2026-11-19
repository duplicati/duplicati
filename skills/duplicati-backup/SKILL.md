---
name: duplicati-backup
description: Undo for agents. Take an encrypted offsite checkpoint with Duplicati before risky changes, and restore files from any earlier point in time. Use before bulk edits, deletes, migrations, upgrades, or when the user asks to back up, undo, roll back, or recover files.
version: 1.1.0
metadata:
  hermes:
    tags: [Backup, Restore, Undo, Checkpoint, MCP]
  openclaw:
    emoji: "💾"
    homepage: https://docs.duplicati.com/automation-and-integration/using-duplicati-with-ai-agents-mcp
---

# Duplicati backup and restore

Duplicati is an open-source backup program that keeps encrypted, deduplicated, versioned backups on storage the user controls. This skill uses the Duplicati MCP server so you can take a restore point before you change files, and bring files back if something goes wrong.

The tools come from an MCP server named `duplicati`. Depending on the runtime, tool names may carry a server prefix. Follow each tool's own schema for parameter names.

| Tool                | Needs level | Use                                                        |
| ------------------- | ----------- | ---------------------------------------------------------- |
| `get_server_status` | Read        | Server version, paused state, running and waiting tasks    |
| `list_backups`      | Read        | Backups you can use, with ID, name, source paths, last run |
| `list_versions`     | Read        | Points in time of a backup, newest first                   |
| `list_files`        | Read        | Contents of one folder in a version, paged                 |
| `search_files`      | Read        | Find files in a version with `*` and `?` wildcards         |
| `get_task`          | Read        | Task state without waiting                                 |
| `wait_for_task`     | Read        | Wait for a task (default 45 s, max 300 s per call)         |
| `run_backup`        | Backup      | Queue a backup, returns a task ID at once                  |
| `stop_task`         | Backup      | Stop a task cleanly or immediately                         |
| `restore`           | Restore     | Restore all or selected files from a version               |

## When to use this skill

Take a checkpoint before work that is hard to reverse:

- deleting, moving, or bulk-rewriting files
- migrations, upgrades, dependency or config changes
- edits to your own workspace, memory, skills, or cron files
- any long task where losing progress would be costly

Use the restore flow when the user asks to undo, roll back, recover a file, or see an older version, or when your own change broke something.

Skip the checkpoint for read-only work and for small edits the user can trivially redo.

## Checkpoint before changing files

1. Call `list_backups`. Pick the backup whose source paths cover every file you are about to change. Refer to it by ID or exact name.
2. If no backup covers the files, stop and tell the user. You cannot create, change, or delete backup configurations. Ask them to add the folder to a backup in Duplicati, or to confirm they want to continue without a restore point.
3. Call `run_backup`. Keep the task ID. If the result says `alreadyQueued` is true, a run of this backup was already queued or running and may have read files before your latest changes; wait for it, then call `run_backup` again if you have changed files since it started.
4. Call `wait_for_task` repeatedly until the task is finished. Tasks run one at a time, so yours may wait behind another. A backup can take seconds or hours; if it is still running after a few minutes, tell the user and ask whether to keep waiting. If `serverPaused` is true, the task will not start until someone resumes the server; tell the user instead of waiting.
5. Continue only when the result is Success or Warning. On Warning, tell the user what the warning says. On failure, report it and do not start the risky change unless the user says to.
6. Write down the restore point time the finished task reports, together with the backup name. Store it where you will find it later (your task notes or daily memory). Versions are identified by time, not by number, because numbers shift when new backups run. If the finished task reports no restore point, use `list_versions` to find the version it made.
7. Make the changes.

## Restore

1. Decide what to restore. If you recorded a restore point time, use it. Otherwise call `list_versions` and choose the newest version from before the damage.
2. Find the exact files with `list_files` or `search_files` in that version. Restore the smallest set that fixes the problem, not the whole backup. Give paths in full; a folder path must end with the path separator (`/` or `\`).
3. Choose the target:
    - To inspect or compare, restore to the original location without overwrite. Existing files are kept, and a restored file whose content differs is written next to them with a timestamp added to its name.
    - To keep the restored copies apart, restore to a separate folder. This only works for folders the token was issued with (`--restore-root`).
    - To undo, restore to the original location with overwrite.
4. Before overwriting files in place, tell the user which files and which point in time, and get a yes, unless they already asked for exactly this undo.
5. Call `restore`, then `wait_for_task` until it finishes. Report what was restored and from when.
6. Check the result: confirm the files exist and look right before you say the undo worked.

## Rules

- A restore writes backed-up files back. It does not delete files created after the backup. For a true rollback, list the files created since the restore point and ask the user before removing any of them.
- Only files under a backup's source paths can be restored, and only to local folders.
- Never ask the user for the Duplicati access token or the backup passphrase in chat, and never print either.
- Do not use `stop_task` on a task you did not start unless the user asks. A task shown with the operation "Other" belongs to a backup your token cannot see; leave it alone. A task that is still waiting cannot be stopped; try again once `get_task` reports it as Running.
- If `get_server_status` shows the server is paused, waiting tasks will not start. Tell the user instead of waiting.
- Task IDs are lost when the Duplicati server restarts. If a task ID is unknown, check `list_versions` to see whether the backup completed.
- If a call is refused for permissions, the token level is too low (Read, Backup, Restore). Tell the user which level the action needs. Do not look for another way in.
- A checkpoint is not a reason to take bigger risks. Still confirm destructive actions the way you normally would.

## If the Duplicati tools are missing

Tell the user the Duplicati MCP server is not connected and give them these steps. Do not try to perform them yourself.

1. Install Duplicati 2.4.1 or newer (canary channel at the time of writing) on the machine that holds the files, and create a backup that covers the folders the agent works in, such as its workspace and memory folders. Use a destination on another machine or in the cloud.
2. Turn on the MCP endpoint: Settings → Experimental options → MCP server, or start the server with `--webservice-enable-mcp-server=true`.
3. Issue a token for the agent, limited to the backups it needs. In the Duplicati UI, open Settings → MCP access tokens → Create access token; after creating it, pick the agent under "Set up the token in" to get a ready-made command or configuration. Or use the command line:

    ```bash
    duplicati-server-util issue-mcp-token --name="<agent name>" --level=Restore --backup="<backup name>" --expires-in=90D
    ```

    The token is shown once. Use `--level=Backup` for checkpoints without restore rights. Add `--restore-root=<folder>` (repeatable) to also allow restores into a separate folder; without it the token can only restore files to their original location.

4. Add the server to the agent's MCP configuration:
    - Name: `duplicati`
    - URL: `http://localhost:8200/mcp` (the Duplicati server address followed by `/mcp`)
    - Transport: Streamable HTTP
    - Header: `Authorization: Bearer <token>`

    For an agent that can only start MCP servers as local commands, use the bridge instead: run `duplicati-server-util mcp` with the token in the `DUPLICATI_MCP_TOKEN` environment variable, or pass `--token-file=<file>`.

    The documentation below has the exact setup for Claude Code, Claude Desktop, VS Code, Cursor, Codex CLI, Gemini CLI, GitHub Copilot CLI and opencode. For OpenClaw, see [references/openclaw.md](references/openclaw.md).

5. Restart or reload the agent so it picks up the new server, and check that the Duplicati tools are listed.

A `404` from the endpoint means the MCP server is switched off. A `403` means a host, origin, or pre-shared key check failed. A `401` means the token is missing, expired, or revoked.

Documentation: https://docs.duplicati.com/automation-and-integration/using-duplicati-with-ai-agents-mcp
