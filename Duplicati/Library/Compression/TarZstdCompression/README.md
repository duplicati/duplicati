# Tar-Based Compression Format

This compression module provides an ICompression implementation using Tar+Compression with an custom EOF (End-of-File) header entry for fast random access, emulating a Zip-like file format. This format relies more heavily on temporary files as the compression is done on the full tar file for best compression. This approach also means that most generated volumes will be better compressed but file size may be significantly lower that the target volume size.

Two compression variants are available:
- **Tar+GZip** (`.tgz` extension) - Uses GZip compression
- **Tar+Zstd** (`.tzstd` extension) - Uses Zstd compression (coming soon)

The archives can be decompressed with standard tar tools, and the `.eof-header` entry will appear as a regular file in the archive for non-Duplicati tools. In case the header is missing, the library will fall back to scanning the entire archive to find the files.

## File Extensions

- `.tgz` - Tar+GZip format
- `.tzstd` - Tar+Zstd format

## File Format Specification

### Overall Structure

```
[GZip Compressed Stream]
  └─ [Tar Archive]
       ├─ [File Entry 1]
       ├─ [File Entry 2]
       ├─ ...
       └─ [.eof-header Entry]  <-- Regular tar entry with special content
            ├─ [Tar Header (512 bytes)]
            ├─ [JSON Dictionary content]
            ├─ [Padding (to make JSON+Trailer fit in 512-byte blocks)]
            ├─ [8 bytes: offset of .eof-header tar header start]
            └─ [6 bytes: "EOFHD1" magic]
            ↑
            All of this is INSIDE the entry content, properly padded
```

### EOF Header Format

The `.eof-header` entry enables O(1) file lookups without scanning the entire archive. It is stored as a **regular tar entry** (not external to the tar), which means:

- Standard tar tools can extract the archive normally
- The `.eof-header` file appears as a regular file in the archive
- Standard compression tools can decompress the archive

The `.eof-header` entry contains:

1. **Tar Header (512 bytes)**: Standard tar header for `.eof-header` file
2. **Entry Content** (padded to 512-byte boundary):
    - **JSON Dictionary**: Maps filenames to their metadata (offset, size, modification time)
    - **Padding**: Padding bytes to align the trailer to the 512-byte boundary
    - **Header Trailer (14 bytes)**:
        - **Offset (8 bytes)**: Little-endian long pointing to start of `.eof-header` tar header
        - **Magic (6 bytes)**: Literal `"EOFHD1"` marking the end

**Important**: The trailer is INSIDE the tar entry content, not after it. The total entry content (JSON + padding + trailer) is padded to a multiple of 512 bytes as required by the TAR format.

#### JSON Dictionary Format

```json
{
    "file1.txt": {
        "offset": 512,
        "size": 1024,
        "lastWriteTime": 1686832200
    },
    "file2.txt": {
        "offset": 2048,
        "size": 2048,
        "lastWriteTime": 1686832260
    }
}
```

The `lastWriteTime` field is stored as a Unix timestamp in seconds since epoch (January 1, 1970 UTC).

### Reading Process

1. **Decompress**: Decompress the entire gzip stream to a temporary file
2. **Read Trailer**: Seek to end of file and read the last 14 bytes (offset + magic)
3. **Verify Magic**: Check for literal `"EOFHD1"` magic string at the end
4. **Load Dictionary**: If valid, use the offset to seek to the start of `.eof-header` entry and parse the JSON dictionary
5. **Fallback to Scanning**: If header invalid/missing, scan entire tar file to build dictionary

**Note**: Since `.eof-header` is always the last entry and its content is padded to 512 bytes, the trailer (14 bytes) is always at the very end of the file. No searching is needed - just read the last 14 bytes.

### Writing Process

1. **Collect Entries**: Store all file entries in memory with their data
2. **Write Tar Entries**: Write all file entries to tar archive using System.Formats.Tar
3. **Build Dictionary**: Create JSON dictionary mapping filenames to their offsets
4. **Write EOF Header Entry**: Write `.eof-header` as regular tar entry containing:
    - Tar header (standard 512-byte tar header)
    - JSON dictionary content
    - Padding to 512-byte boundary
    - Trailer with offset (pointing to start of this entry's header) + magic
5. **Compress**: Compress the entire tar file with the desired method

## Tar Format Implementation

The module uses `System.Formats.Tar` with the Pax format:

- **Block Size**: 512 bytes
- **Format**: Pax (POSIX.1-2001 extended format)
- **Compatibility**: Standard tar tools can read the archive

## Compression Options

### GZip Compression (`.tgz`)

- **Implementation**: `System.IO.Compression.GZipStream`
- **Default Level**: 2 (Optimal)
- **Level Range**: 0-3
- **Level Mapping**:
    - **0**: NoCompression
    - **1**: Fastest
    - **2**: Optimal
    - **3**: SmallestSize
- **Option Name**: `tgz-compression-level`

### Zstd Compression (`.tzstd`)

- **Implementation**: Zstd compression
- **Default Level**: 10
- **Level Range**: 1-22
- **Option Name**: `tzstd-compression-level`

## Performance Characteristics

### Write Performance

- **Memory Usage**: Proportional to total file sizes (stores in memory before writing)
- **Compression**: Single-pass after all files collected
- **I/O**: Writes tar to temp file, then compresses to output

### Read Performance

- **With EOF Header**: O(1) file lookup
- **Without EOF Header**: O(n) scan through entire archive
- **Decompression**: Full stream decompression required (cannot seek in gzip)

## Command-Line Options

### tgz-compression-level (Tar+GZip)

Sets the compression level for GZip compression.

- **Type**: Enumeration
- **Default**: 2
- **Range**: 0-3
- **Description**: Higher values provide better compression but are slower. 0 = no compression, 3 = maximum compression.

```bash
duplicati backup ... --tgz-compression-level=2
```

### tzstd-compression-level (Tar+Zstd)

Sets the compression level for Zstd compression.

- **Type**: Enumeration
- **Default**: 3
- **Range**: 1-22
- **Description**: Higher values provide better compression but are slower

```bash
duplicati backup ... --tzstd-compression-level=9
```

## Compatibility

### With Standard Tools

The format is designed to be compatible with standard Unix tools:

**Tar+GZip (`.tgz`):**
```bash
# Decompress and extract with standard tools
gunzip -c backup.tgz | tar -xf -

# List contents
gunzip -c backup.tgz | tar -tf -

# The .eof-header file will be extracted like any other file
# It contains the JSON dictionary with file offsets
```

**Tar+Zstd (`.tzstd`):**
```bash
# Decompress and extract with standard tools
zstd -d -c backup.tzstd | tar -xf -

# List contents
zstd -d -c backup.tzstd | tar -tf -

# The .eof-header file will be extracted like any other file
# It contains the JSON dictionary with file offsets
```

### Archive Structure

When extracted with standard tar, you'll see:

- All your files (file1.txt, file2.txt, etc.)
- An extra `.eof-header` file containing the metadata dictionary

This design ensures:

1. **No data loss**: Standard tools can fully extract the archive
2. **Self-documenting**: The `.eof-header` is human-readable JSON
3. **Forward compatibility**: Future versions can extend the format

## Advantages

1. **Fast Random Access**: EOF header enables O(1) file lookups
2. **Standard Format**: Based on standard tar+gzip for universal compatibility
3. **Fallback Support**: Can read archives even if EOF header is corrupted
4. **Self-Documenting**: JSON dictionary is human-readable
5. **Tool Compatible**: Works with standard tar and gzip tools

## Limitations

1. **Full Decompression Required**: Must decompress entire archive to read any file
2. **Memory Usage During Write**: All file data held in memory until disposal
3. **No Streaming Write**: Cannot stream large files without memory overhead

## File Structure Diagram

```
┌─────────────────────────────────────────────────────────────┐
│              Compressed Stream (GZip or Zstd)               │
├─────────────────────────────────────────────────────────────┤
│                         Tar Archive                         │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Entry 1: file1.txt                                   │  │
│  │  ├─ Tar Header (512 bytes)                            │  │
│  │  └─ File Content (padded to 512 boundary)             │  │
│  ├───────────────────────────────────────────────────────┤  │
│  │  Entry 2: file2.txt                                   │  │
│  │  ├─ Tar Header (512 bytes)                            │  │
│  │  └─ File Content (padded to 512 boundary)             │  │
│  ├───────────────────────────────────────────────────────┤  │
│  │  Entry N: .eof-header (REGULAR TAR ENTRY)             │  │
│  │  ├─ Tar Header (512 bytes)                            │  │
│  │  ├─ Content (ALL within 512-byte blocks):             │  │
│  │  │  ├─ JSON Dictionary                                │  │
│  │  │  │   {"file1.txt":{"offset":512,...}               │  │
│  │  │  ├─ Padding (to align trailer)                     │  │
│  │  │  ├─ Header Offset (8 bytes) ───┐                   │  │
│  │  │  └─ "EOFHD1" Magic (6 bytes)   │                   │  │
│  │  │                                │                   │  │
│  │  └────────────────────────────────│───────────────────┘  │
│  │                                   │                      │
│  └───────────────────────────────────┘                      │
│         The offset points back to the start                 │
│         of THIS entry's tar header                          │
└─────────────────────────────────────────────────────────────┘
```

## Implementation Notes

1. The `.eof-header` is a **regular tar entry** - it has a tar header just like any other file
2. The trailer (offset + magic) is **inside the entry content**, at the end, before the 512-byte padding
3. The total entry content size (JSON + trailer) is a multiple of 512 bytes as per TAR spec
4. The offset in the trailer points to the start of the `.eof-header` tar header
5. When using standard tar tools, the `.eof-header` file is extracted normally (as a file containing JSON + binary trailer)
6. The JSON dictionary excludes the `.eof-header` file itself
7. File paths are stored with forward slashes but support both slash types on read

## Usage Examples

### Creating an Archive

```csharp
using var stream = File.Create("backup.tzstd");
using var archive = new FileArchiveTarZstd(
    stream,
    ArchiveMode.Write,
    new Dictionary<string, string?> { ["tzstd-compression-level"] = "9" }
);

using (var entry = archive.CreateFile("documents/file1.txt", CompressionHint.Compressible, DateTime.Now))
{
    entry.Write(data, 0, data.Length);
}
```

### Reading an Archive

```csharp
using var stream = File.OpenRead("backup.tzstd");
using var archive = new FileArchiveTarZstd(stream, ArchiveMode.Read, new Dictionary<string, string?>());

var files = archive.ListFiles(null);
foreach (var file in files)
{
    using var fileStream = archive.OpenRead(file);
    // Read file content
}
```

### Extracting with Standard Tools

**Tar+GZip (`.tgz`):**
```bash
# The archive is valid tar+gzip
gunzip -c backup.tgz > backup.tar
tar -xf backup.tar

# You'll see:
# - documents/file1.txt
# - .eof-header (contains the JSON offset map)
```

**Tar+Zstd (`.tzstd`):**
```bash
# The archive is valid tar+zstd
zstd -d -c backup.tzstd > backup.tar
tar -xf backup.tar

# You'll see:
# - documents/file1.txt
# - .eof-header (contains the JSON offset map)
```
