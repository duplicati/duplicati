# [Duplicati](https://duplicati.com)

Duplicati is a free, open source, backup client that securely stores encrypted, incremental, compressed backups on cloud storage services and remote file servers. It works with:

_Amazon S3, IDrive e2, OneDrive, Google Drive, Backblaze (B2), Swift / OpenStack, WebDAV, SSH (SFTP), FTP, and more!_

Duplicati is licensed under the MIT license and available for Windows, OSX and Linux.

## Available tags

-   `latest` - the most recent stable release
-   `beta` - the most recent beta release
-   `experimental` - the most recent experimental release
-   `canary` - the most recent canary release
-   specific versions like `2.0.2.1_beta_2017-08-01`

Images for the following OS/architecture combinations are available using Docker's multi-arch support:

-   `linux/amd64`
-   `linux/arm/v7` - 32-bit ARMv7 devices like the Raspberry Pi 2
-   `linux/arm64` - 64-bit ARMv8 devices like the Raspberry Pi 4 (when running a 64-bit OS)

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

If you use the first option, you can start once with the new password, and then remove it from the Docker config. The password will be persisted in the data folder and does not need to be in the config after starting once. If you keep the environment variable in Docker, a container restart will reset the password, even if you changed it from within Duplicati.

If you use the second option, the changed password is persisted, and you will not use the signin link afterwards. Note that this option only works if there is no previous password set.

### Preserving configuration

All configuration is stored in `/data` inside the container, so you need to mount a volume at that path to preserve the configuration. Note that the name `data` refers to Duplicati's _settings data_, not the data that you want to back up.

```console
$ docker run --name=duplicati -v /host/duplicati-data:/data duplicati/duplicati
```

This allows you to delete and recreate the container without losing your configuration:

```console
$ docker rm duplicati
$ docker run --name=duplicati -v /host/duplicati-data:/data duplicati/duplicati
```

### Using a different UID/GID

By default, Duplicati will run as the root user. While this can be a security issue, it is often required to grant Duplicati access to system files, such as the `/etc` folder. If you would like to use a different user to run Duplicati, you can supply the `UID` and `GID` values as environment variables. As an example, if you would like to run as the current user, you can supply:

```console
docker run --name=duplicati -e UID=$(id -u) -e GID=$(id -g) -v /host/duplicati-data:/data duplicati/duplicati
```

When running with a different UID/GID the data folder will be `chown`'ed by that user on the host system.

The UID/GID settings are also applied when invoking a different command, such as:

```
docker run --rm -e UID=$(id -u) -e GID=$(id -g) duplicati/duplicati duplicati-cli help
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

Note: All commands passed to the container are executed through a wrapper script (`run-as-user.sh`) such that a custom UID/GID can be set via environment variables.

### Specifying server arguments

To launch the Duplicati server with additional arguments, run the `duplicati-server` command:

```console
$ docker run duplicati/duplicati duplicati-server --log-level=debug
```

### Supplying environment variables

All commandline arguments can also be provided as environment variables, if both an environment variable and a commandline argument is supplied for the same setting, the commandline arguments are used.

The commandline arguments are mapped to environment variables by prefixing with `DUPLICATI__` and transforming `-` to `_`.
For example, the commandline argument `--webservice-password` can be provided with the environment variable `DUPLICATI__WEBSERVICE_PASSWORD`.

### Notes on usage and security features

Duplicati has a number of security features that are configured differently for Docker images compared to the other builds. The reason for these changes is to make the Docker images work similar to other Docker images.

The features that are disabled are:

-   `DUPLICATI__WEBSERVICE_INTERFACE=any`: This setting disables locking communication only to a single adapter, as the Docker network interface is expected to be guarded in other ways with explicit routing.

To increase security, the following steps are recommended:

-   Set `DUPLICATI__WEBSERVICE_ALLOWED_HOSTNAMES=<hostname1>;<hostname2>`
    This will enable using desired hostnames instead of IP addresses only. The hostname `*` will disable the protection, but is not recommended.

-   Set `SETTINGS_ENCRYPTION_KEY=<key>`:
    This will enable database encryption using the supplied key and reduce the risk of leaking credentials from the database. Note that the `SETTINGS_ENCRYPTION_KEY` is not the password used to connect to the UI.
