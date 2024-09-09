# [Duplicati](https://duplicati.com)

Duplicati is a free, open source, backup client that securely stores encrypted, incremental, compressed backups on cloud storage services and remote file servers. It works with:

_Amazon S3, IDrive e2, OneDrive, Google Drive, Rackspace Cloud Files, Backblaze (B2), Swift / OpenStack, WebDAV, SSH (SFTP), FTP, and more!_

Duplicati is licensed under the MIT license and available for Windows, OSX and Linux (.NET 8+ required).

## Available tags

- `beta` - the most recent beta release
- `experimental` - the most recent experimental release
- `canary` - the most recent canary release
- `latest` - an alias for `beta`
- specific versions like `2.0.2.1_beta_2017-08-01`

Images for the following OS/architecture combinations are available using Docker's multi-arch support:

- `linux/amd64`
- `linux/arm/v7` - 32-bit ARMv7 devices like the Raspberry Pi 2
- `linux/arm64` - 64-bit ARMv8 devices like the Raspberry Pi 4 (when running a 64-bit OS)

## How to use this image

```console
$ docker run -p 8200:8200 -v /some/path:/some/path duplicati/duplicati
```

Then, open [http://localhost:8200](http://localhost:8200) on the host to access the Duplicati web interface and configure backups. Any host directory that you want to back up needs to be mounted into the container using the `-v` option.

### First launch

On the first launch, Duplicati will generate the database containing the server settings. This includes a signing key for JWT tokens and a randomly generated password for accessing the UI. Because the password is randomly generated, you cannot sign in with the password.

There are two ways to fix this issue:

1. Set up the enviroment variable `DUPLICATI__WEBSERVICE_PASSWORD=<password>` to change the password on restarts.
2. Find the signin link in the Docker logs. Opening the link will allow you to log in, and you can change the password from there.

If you use the second option, the changed password is persisted, and you will not use the signin link afterwards.

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
See duplicati-cli help <topic> for more information.
  General: example, changelog
...
$ docker run --rm -v /home:/backup/home duplicati/duplicati duplicati-cli backup ssh://user@host /backup/home
```

### Specifying server arguments

To launch the Duplicati server with additional arguments, run the `duplicati-server` command:

```console
$ docker run duplicati/duplicati duplicati-server --log-level=debug
```

### Supplying environment variables

All commandline arguments can also be provided as as environment variables, if both an environment variable and a commandline argument is supplied for the same setting, the commandline arguments are used.

The commandline arguments are mapped to environment variables by prefixing with `DUPLICATI__` and transforming `-` to `_`.
For example, the commandline argument `--webservice-password` can be provided with the environment variable `DUPLICATI__WEBSERVICE_PASSWORD`.

### Notes on usage and security features

Duplicati has a number of security features that are configured differently for Docker images compared to the other builds. The reason for these changes is to make the Docker images work similar to other Docker images.

The features that are disabled are:

- `DUPLICATI__WEBSERVICE_INTERFACE=any`: This setting disables locking communication only to a single adapter, as the Docker network interface is expected to be guarded in other ways with explicit routing.

- `DUPLICATI__DISABLE_DB_ENCRYPTION=true`: This setting disables encrypting data in the database, which should be stored on the host system.

This setting is added to avoid encrypting the database with the default key, which is derived from the physical machine serial number. When moving Docker containers, the serial number could change, making the database inaccesible. Overriding this setting with `false` will cause the container to use the machine serial number to derive an encryption key.

To increase security, the following steps are recommended:

- Set `DUPLICATI__WEBSERVICE_ALLOWED_HOSTNAMES=<hostname1>;<hostname2>`
  This will enable using desired hostnames instead of IP addresses only. The hostname `*` will disable the protection, but is not recommended.

- Set `DUPLICATI__DISABLE_DB_ENCRYPTION=false` and `SETTINGS_ENCRYPTION_KEY=<key>`:
  This will enable database encryption using the supplied key and reduce the risk of leaking credentials from the database. Note that the `SETTINGS_ENCRYPTION_KEY` is not the password used to connect to the UI.
