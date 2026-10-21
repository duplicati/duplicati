# This folder contains updated binaries

The NuGet package of `Mono.Posix` and `Mono.Unix` contains a library for 32-bit Arm that is built without large file support.
This causes it to fail when processing files that are larger than the 32bit values (i.e., 4 GiB).

The binary in this folder is built from [the `Mono.Posix` source](https://github.com/mono/mono.posix) on Ubuntu, which default enables large file support for Arm cross-compiles.

The [issue is reported to `Mono.Posix`](https://github.com/mono/mono.posix/issues/49) and this folder should be deleted once upstream is fixed, but at this point the package has not been updated in 3 years, so it is unclear if an update will ever happen.

# Updated builds for Debian Buster

The `libMono.Unix.so` file is built on Debian Buster to link against `GLIBC_2.28` ensuring compatibility with older Linux distros.

The native SQLite library (`libe_sqlite3.so`) is no longer replaced here; it comes from the `SQLite` NuGet package, which is pinned in `Duplicati.Library.SQLiteHelper.csproj` to a version built against `GLIBC_2.28`.
