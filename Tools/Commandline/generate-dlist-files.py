#!/usr/bin/env python3
"""
Script to generate dlist files from Duplicati SQLite database.

This script reads from a Duplicati local backup database and generates
dlist (fileset) files for each backup version found.

Usage:
    python generate_dlist_files.py <database_path> [options]

Example:
    python generate_dlist_files.py /path/to/backup.sqlite --output-dir ./dlist_files
"""

import argparse
import json
import os
import sqlite3
import sys
import zipfile
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple


# Constants matching Duplicati's format
SERIALIZED_DATE_TIME_FORMAT = "%Y%m%dT%H%M%SZ"
FILELIST_FILENAME = "filelist.json"
FILESET_FILENAME = "fileset"
MANIFEST_FILENAME = "manifest"
MANIFEST_VERSION = 2
MANIFEST_ENCODING = "utf8"

# Special blockset IDs
FOLDER_BLOCKSET_ID = -100
SYMLINK_BLOCKSET_ID = -200

# Unix epoch starts at 1970-01-01, .NET ticks start at 0001-01-01
# The difference is 621355968000000000 ticks
TICKS_TO_1970 = 621355968000000000
TICKS_PER_SECOND = 10000000


def convert_timestamp(ts: int) -> datetime:
    """
    Convert a Duplicati timestamp to Python datetime.
    Duplicati may store timestamps as:
    - Unix seconds (values around 1-2 billion for dates in 2020s)
    - Unix milliseconds (values around 1-2 trillion)
    - .NET DateTime ticks (values around 6-7 quadrillion for dates in 2020s)
    """
    if ts is None:
        return datetime.now(timezone.utc)
    
    # Detect format based on magnitude
    if ts > TICKS_TO_1970:
        # .NET DateTime ticks (100-nanosecond intervals since 0001-01-01)
        unix_seconds = (ts - TICKS_TO_1970) / TICKS_PER_SECOND
        return datetime.fromtimestamp(unix_seconds, tz=timezone.utc)
    elif ts > 1000000000000:  # 1 trillion
        # Likely Unix milliseconds
        return datetime.fromtimestamp(ts / 1000, tz=timezone.utc)
    else:
        # Likely Unix seconds
        return datetime.fromtimestamp(ts, tz=timezone.utc)


def serialize_datetime(dt: datetime) -> str:
    """Serialize datetime to Duplicati format (UTC)."""
    return dt.astimezone(timezone.utc).strftime(SERIALIZED_DATE_TIME_FORMAT)


def deserialize_datetime(s: str) -> datetime:
    """Deserialize datetime from Duplicati format."""
    return datetime.strptime(s, SERIALIZED_DATE_TIME_FORMAT).replace(tzinfo=timezone.utc)


def generate_filename(
    filetype: str,
    prefix: str,
    timestamp: datetime,
    compression_module: str = "zip",
    encryption_module: Optional[str] = None
) -> str:
    """Generate a dlist filename matching Duplicati's naming convention."""
    volumename = f"{prefix}-{serialize_datetime(timestamp)}.{filetype}.{compression_module}"
    if encryption_module:
        volumename += f".{encryption_module}"
    return volumename


def create_manifest_data(blocksize: int, blockhash: str, filehash: str) -> Dict[str, Any]:
    """Create manifest data matching Duplicati's format."""
    return {
        "Version": MANIFEST_VERSION,
        "Created": serialize_datetime(datetime.now(timezone.utc)),
        "Encoding": MANIFEST_ENCODING,
        "Blocksize": blocksize,
        "BlockHash": blockhash,
        "FileHash": filehash,
        "AppVersion": "2.0.0.0"  # Placeholder
    }


def create_fileset_data(is_full_backup: bool) -> Dict[str, Any]:
    """Create fileset data matching Duplicati's format."""
    return {
        "IsFullBackup": is_full_backup
    }


class DuplicatiDatabase:
    """Wrapper for Duplicati SQLite database access."""

    def __init__(self, db_path: str):
        self.db_path = db_path
        self.conn = sqlite3.connect(db_path)
        self.conn.row_factory = sqlite3.Row

    def close(self):
        """Close database connection."""
        self.conn.close()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()

    def get_configuration(self) -> Dict[str, str]:
        """Get configuration settings from the database."""
        cursor = self.conn.execute('SELECT "Key", "Value" FROM "Configuration"')
        return {row["Key"]: row["Value"] for row in cursor.fetchall()}

    def get_filesets(self) -> List[Dict[str, Any]]:
        """
        Get all filesets (backup versions) from the database.

        Returns list of dicts with: id, operation_id, volume_id, is_full_backup, timestamp
        """
        cursor = self.conn.execute('''
            SELECT 
                "ID" as id,
                "OperationID" as operation_id,
                "VolumeID" as volume_id,
                "IsFullBackup" as is_full_backup,
                "Timestamp" as timestamp
            FROM "Fileset"
            ORDER BY "Timestamp" DESC
        ''')

        filesets = []
        for row in cursor.fetchall():
            filesets.append({
                "id": row["id"],
                "operation_id": row["operation_id"],
                "volume_id": row["volume_id"],
                "is_full_backup": bool(row["is_full_backup"]),
                "timestamp": convert_timestamp(row["timestamp"])
            })
        return filesets

    def get_files_for_fileset(self, fileset_id: int) -> List[Dict[str, Any]]:
        """
        Get all files belonging to a specific fileset.

        Returns list of file entries with all metadata needed for dlist.
        """
        cursor = self.conn.execute('''
            SELECT 
                f."Path" as path,
                fe."Lastmodified" as lastmodified,
                fl."BlocksetID" as blockset_id,
                fl."MetadataID" as metadata_id,
                CASE 
                    WHEN fl."BlocksetID" = ? THEN 'Folder'
                    WHEN fl."BlocksetID" = ? THEN 'Symlink'
                    ELSE 'File'
                END as entry_type
            FROM "FilesetEntry" fe
            JOIN "File" f ON fe."FileID" = f."ID"
            JOIN "FileLookup" fl ON f."ID" = fl."ID"
            WHERE fe."FilesetID" = ?
            ORDER BY f."Path"
        ''', (FOLDER_BLOCKSET_ID, SYMLINK_BLOCKSET_ID, fileset_id))

        files = []
        for row in cursor.fetchall():
            entry = {
                "path": row["path"],
                "lastmodified": convert_timestamp(row["lastmodified"]),
                "blockset_id": row["blockset_id"],
                "metadata_id": row["metadata_id"],
                "type": row["entry_type"]
            }

            # Get file content info (for files and alternate streams)
            if entry["type"] == "File":
                content_info = self._get_blockset_info(entry["blockset_id"])
                if content_info:
                    entry.update(content_info)

            # Get metadata info
            metadata_info = self._get_metadata_info(entry["metadata_id"])
            if metadata_info:
                entry["metadata"] = metadata_info

            files.append(entry)

        return files

    def _get_blockset_info(self, blockset_id: int) -> Optional[Dict[str, Any]]:
        """Get blockset information (hash, size, blocklists)."""
        if blockset_id is None or blockset_id <= 0:
            return None

        cursor = self.conn.execute('''
            SELECT "Length" as length, "FullHash" as fullhash
            FROM "Blockset"
            WHERE "ID" = ?
        ''', (blockset_id,))

        row = cursor.fetchone()
        if not row:
            return None

        result = {
            "size": row["length"],
            "hash": row["fullhash"]
        }

        # Get blocklist hashes for large files (files with multiple blocks)
        cursor = self.conn.execute('''
            SELECT "Hash" as hash
            FROM "BlocklistHash"
            WHERE "BlocksetID" = ?
            ORDER BY "Index"
        ''', (blockset_id,))

        blocklist_hashes = [r["hash"] for r in cursor.fetchall()]
        if blocklist_hashes:
            result["blocklists"] = blocklist_hashes
        else:
            # Small file stored as single block - get block hash directly
            cursor = self.conn.execute('''
                SELECT b."Hash" as hash, b."Size" as size
                FROM "BlocksetEntry" be
                JOIN "Block" b ON be."BlockID" = b."ID"
                WHERE be."BlocksetID" = ?
                ORDER BY be."Index"
                LIMIT 1
            ''', (blockset_id,))

            block_row = cursor.fetchone()
            if block_row:
                result["blockhash"] = block_row["hash"]
                result["blocksize"] = block_row["size"]

        return result

    def _get_metadata_info(self, metadata_id: int) -> Optional[Dict[str, Any]]:
        """Get metadata blockset information."""
        if metadata_id is None or metadata_id <= 0:
            return None

        cursor = self.conn.execute('''
            SELECT "BlocksetID" as blockset_id, "Content" as content
            FROM "Metadataset"
            WHERE "ID" = ?
        ''', (metadata_id,))

        row = cursor.fetchone()
        if not row:
            return None

        result = {}

        # Get metadata blockset info
        if row["blockset_id"]:
            cursor = self.conn.execute('''
                SELECT "Length" as length, "FullHash" as fullhash
                FROM "Blockset"
                WHERE "ID" = ?
            ''', (row["blockset_id"],))

            bs_row = cursor.fetchone()
            if bs_row:
                result["metasize"] = bs_row["length"]
                result["metahash"] = bs_row["fullhash"]

                # Get metablocklist hashes
                cursor = self.conn.execute('''
                    SELECT "Hash" as hash
                    FROM "BlocklistHash"
                    WHERE "BlocksetID" = ?
                    ORDER BY "Index"
                ''', (row["blockset_id"],))

                metablocklist_hashes = [r["hash"] for r in cursor.fetchall()]
                if metablocklist_hashes:
                    result["metablocklists"] = metablocklist_hashes
                else:
                    # Small metadata stored as single block
                    cursor = self.conn.execute('''
                        SELECT b."Hash" as hash
                        FROM "BlocksetEntry" be
                        JOIN "Block" b ON be."BlockID" = b."ID"
                        WHERE be."BlocksetID" = ?
                        ORDER BY be."Index"
                        LIMIT 1
                    ''', (row["blockset_id"],))

                    meta_block_row = cursor.fetchone()
                    if meta_block_row:
                        result["metablockhash"] = meta_block_row["hash"]

        return result if result else None


def create_filelist_json(files: List[Dict[str, Any]]) -> str:
    """
    Create the filelist.json content as a JSON array string.

    Each file entry follows Duplicati's format:
    {
        "type": "File" | "Folder" | "Symlink" | "AlternateStream",
        "path": "/path/to/file",
        "hash": "abc123...",
        "size": 12345,
        "time": "20240115T120000Z",
        "metahash": "def456...",
        "metasize": 123,
        "metablockhash": "ghi789..." | "metablocklists": ["hash1", "hash2"],
        "blockhash": "jkl012...",
        "blocksize": 102400,
        "blocklists": ["hash1", "hash2"]
    }
    """
    entries = []

    for file_info in files:
        entry = {
            "type": file_info["type"],
            "path": file_info["path"]
        }

        # Add file-specific fields
        if file_info["type"] in ("File", "AlternateStream"):
            if "hash" in file_info:
                entry["hash"] = file_info["hash"]
            if "size" in file_info:
                entry["size"] = file_info["size"]
            if "lastmodified" in file_info:
                entry["time"] = serialize_datetime(file_info["lastmodified"])

            # Add block info (either blocklists for large files or single blockhash)
            if "blocklists" in file_info:
                entry["blocklists"] = file_info["blocklists"]
            elif "blockhash" in file_info:
                entry["blockhash"] = file_info["blockhash"]
                entry["blocksize"] = file_info.get("blocksize", 0)

        # Add metadata fields
        if "metadata" in file_info and file_info["metadata"]:
            meta = file_info["metadata"]
            if "metahash" in meta:
                entry["metahash"] = meta["metahash"]
            if "metasize" in meta:
                entry["metasize"] = meta["metasize"]
            if "metablocklists" in meta:
                entry["metablocklists"] = meta["metablocklists"]
            elif "metablockhash" in meta:
                entry["metablockhash"] = meta["metablockhash"]

        entries.append(entry)

    # Return as JSON array (without newlines between entries to match Duplicati's compact format)
    return json.dumps(entries, separators=(',', ':'))


def generate_dlist_file(
    output_path: str,
    fileset: Dict[str, Any],
    files: List[Dict[str, Any]],
    config: Dict[str, str],
    compression: str = "zip",
    blocksize_override: Optional[int] = None,
    blockhash_override: Optional[str] = None,
    filehash_override: Optional[str] = None,
    prefix_override: Optional[str] = None
) -> str:
    """
    Generate a dlist file for a specific fileset.

    Args:
        output_path: Directory where the file will be written
        fileset: Fileset metadata (id, timestamp, is_full_backup, etc.)
        files: List of file entries for this fileset
        config: Database configuration (blocksize, blockhash, filehash)
        compression: Compression module to use (default: zip)
        blocksize_override: Optional override for blocksize from command line
        blockhash_override: Optional override for blockhash from command line
        filehash_override: Optional override for filehash from command line
        prefix_override: Optional override for prefix from command line

    Returns:
        Path to the generated file
    """
    # Get configuration values with defaults, allowing command-line overrides
    blocksize = blocksize_override if blocksize_override is not None else int(config.get("blocksize", "102400"))
    blockhash = blockhash_override if blockhash_override is not None else config.get("block-hash-algorithm", "SHA256")
    filehash = filehash_override if filehash_override is not None else config.get("file-hash-algorithm", "SHA256")
    prefix = prefix_override if prefix_override is not None else config.get("prefix", "duplicati")

    # Generate filename
    filename = generate_filename(
        "dlist",
        prefix,
        fileset["timestamp"],
        compression
    )

    filepath = os.path.join(output_path, filename)

    # Create the zip file
    with zipfile.ZipFile(filepath, 'w', compression=zipfile.ZIP_DEFLATED) as zf:
        # Add manifest file
        manifest_data = create_manifest_data(blocksize, blockhash, filehash)
        zf.writestr(MANIFEST_FILENAME, json.dumps(manifest_data))

        # Add fileset file
        fileset_data = create_fileset_data(fileset["is_full_backup"])
        zf.writestr(FILESET_FILENAME, json.dumps(fileset_data))

        # Add filelist.json
        filelist_json = create_filelist_json(files)
        zf.writestr(FILELIST_FILENAME, filelist_json)

    return filepath


def main():
    parser = argparse.ArgumentParser(
        description="Generate dlist files from Duplicati SQLite database"
    )
    parser.add_argument(
        "database",
        help="Path to the Duplicati SQLite database file"
    )
    parser.add_argument(
        "--output-dir", "-o",
        default=".",
        help="Output directory for generated dlist files (default: current directory)"
    )
    parser.add_argument(
        "--compression", "-c",
        default="zip",
        help="Compression format: zip (default)"
    )
    parser.add_argument(
        "--fileset-id", "-f",
        type=int,
        help="Generate only for specific fileset ID (default: all filesets)"
    )
    parser.add_argument(
        "--list", "-l",
        action="store_true",
        help="List filesets without generating files"
    )
    parser.add_argument(
        "--blocksize", "-b",
        type=int,
        help="Block size in bytes (default: read from database, or 102400)"
    )
    parser.add_argument(
        "--blockhash",
        help="Block hash algorithm (default: read from database, or SHA256)"
    )
    parser.add_argument(
        "--filehash",
        help="File hash algorithm (default: read from database, or SHA256)"
    )
    parser.add_argument(
        "--prefix", "-p",
        help="Filename prefix (default: read from database, or 'duplicati')"
    )

    args = parser.parse_args()

    # Validate database path
    if not os.path.exists(args.database):
        print(f"Error: Database file not found: {args.database}", file=sys.stderr)
        sys.exit(1)

    # Create output directory if needed
    if not args.list and not os.path.exists(args.output_dir):
        os.makedirs(args.output_dir)

    # Open database
    with DuplicatiDatabase(args.database) as db:
        # Get configuration
        config = db.get_configuration()

        # Get all filesets
        filesets = db.get_filesets()

        if not filesets:
            print("No filesets found in database.")
            sys.exit(0)

        # List mode
        if args.list:
            print(f"\nFound {len(filesets)} fileset(s) in database:")
            print("-" * 80)
            for fs in filesets:
                backup_type = "FULL" if fs["is_full_backup"] else "PARTIAL"
                print(f"  ID: {fs['id']}")
                print(f"  Timestamp: {fs['timestamp'].isoformat()}")
                print(f"  Type: {backup_type}")
                print(f"  Volume ID: {fs['volume_id']}")
                print("-" * 80)
            return

        # Filter to specific fileset if requested
        if args.fileset_id is not None:
            filesets = [fs for fs in filesets if fs["id"] == args.fileset_id]
            if not filesets:
                print(f"Error: Fileset ID {args.fileset_id} not found", file=sys.stderr)
                sys.exit(1)

        # Show configuration being used
        print(f"\nGenerating dlist files for {len(filesets)} fileset(s)...")
        print(f"Output directory: {os.path.abspath(args.output_dir)}")
        print("-" * 80)
        print("Configuration:")
        blocksize = args.blocksize if args.blocksize is not None else int(config.get("blocksize", "102400"))
        blockhash = args.blockhash if args.blockhash is not None else config.get("block-hash-algorithm", "SHA256")
        filehash = args.filehash if args.filehash is not None else config.get("file-hash-algorithm", "SHA256")
        prefix = args.prefix if args.prefix is not None else config.get("prefix", "duplicati")
        print(f"  Blocksize: {blocksize} bytes {'(from command line)' if args.blocksize else '(from database)'}")
        print(f"  Blockhash: {blockhash} {'(from command line)' if args.blockhash else '(from database)'}")
        print(f"  Filehash: {filehash} {'(from command line)' if args.filehash else '(from database)'}")
        print(f"  Prefix: {prefix} {'(from command line)' if args.prefix else '(from database)'}")
        print("-" * 80)

        generated_files = []
        for fileset in filesets:
            print(f"\nProcessing fileset ID {fileset['id']} ({fileset['timestamp'].isoformat()})...")

            # Get files for this fileset
            files = db.get_files_for_fileset(fileset["id"])
            print(f"  Found {len(files)} files")

            # Generate dlist file
            filepath = generate_dlist_file(
                args.output_dir,
                fileset,
                files,
                config,
                args.compression,
                blocksize_override=args.blocksize,
                blockhash_override=args.blockhash,
                filehash_override=args.filehash,
                prefix_override=args.prefix
            )

            generated_files.append(filepath)
            file_size = os.path.getsize(filepath)
            print(f"  Generated: {os.path.basename(filepath)}")
            print(f"  Size: {file_size:,} bytes")

        print("\n" + "=" * 80)
        print(f"Successfully generated {len(generated_files)} dlist file(s):")
        for f in generated_files:
            print(f"  - {f}")


if __name__ == "__main__":
    main()
