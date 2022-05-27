# [Duplicati](https://www.duplicati.com)
Duplicati is a free, open source, backup client that securely stores encrypted, incremental, compressed backups on cloud storage services and remote file servers. It works with:

*Amazon S3, IDrive e2, OneDrive, Google Drive, Rackspace Cloud Files, HubiC, Backblaze (B2), Swift / OpenStack, WebDAV, SSH (SFTP), FTP, and more!*

Duplicati is licensed under LGPL and available for Windows, OSX and Linux (.NET 4.7.1+ or Mono 4.8.0+ required).

## Available tags

  * `beta` - the most recent beta release
  * `experimental` - the most recent experimental release
  * `canary` - the most recent canary release
  * `latest` - an alias for `beta`
  * specific versions like `2.0.2.1_beta_2017-08-01`

Images for the following OS/architecture combinations are available using Docker's multi-arch support:

  * `linux/amd64`
  * `linux/arm/v7` - 32-bit ARMv7 devices like the Raspberry Pi 2
  * `linux/arm64` - 64-bit ARMv8 devices like the Raspberry Pi 4 (when running a 64-bit OS)

## How to use this image

```console
$ docker run -p 8200:8200 -v /some/path:/some/path duplicati/duplicati
```

Then, open [http://localhost:8200](http://localhost:8200) on the host to access the Duplicati web interface and configure backups. Any host directory that you want to back up needs to be mounted into the container using the `-v` option.

### Preserving configuration

All configuration is stored in `/data` inside the container, so you can mount a volume at that path to preserve the configuration:

```console
$ docker run --name=duplicati -v duplicati-data:/data duplicati/duplicati
```

This allows you to delete and recreate the container without losing your configuration:

```console
$ docker rm duplicati
$ docker run --name=duplicati -v duplicati-data:/data duplicati/duplicati
```

### Using Duplicati CLI

Run the `duplicati-cli` command to use the Duplicati command-line interface:

```console
$ docker run --rm duplicati/duplicati duplicati-cli help
See duplicati.commandline.exe help <topic> for more information.
  General: example, changelog
...
$ docker run --rm -v /home:/backup/home duplicati/duplicati duplicati-cli backup ssh://user@host /backup/home
```

### Specifying server arguments

To launch the Duplicati server with additional arguments, run the `duplicati-server` command:

```console
$ docker run duplicati/duplicati duplicati-server --log-level=debug
```
