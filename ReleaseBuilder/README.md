# Release Builder tool

This folder contains the `ReleaseBuilder` tool which builds installers and packages for all supported operating systems.

The tool is tested and used only on MacOS, but can be configured for other operating systems, including WSL and to some extent Windows.

The setup is a mix of environment variables, settings files and commandline arguments.

# Required environment

The environment variables used by the tool are:

- `UPDATER_KEYFILE`: The file containing the key used to sign the manifest. Create one with the `create-key` command.
- `GPG_KEYFILE`: A file encrypted with AESCrypt and the general password. The file has two lines: `GPGID` and `GPGPassphrase` which are given to the `GPG` command.
- `AUTHENTICODE_PFXFILE`: A PFX file containing a signing key for Authenticode signing of Windows executables (needs to be purchased at a certificate vendor).
- `AUTHENTICODE_PASSWORD`: A file encrypted with AESCrypt and the general password. The file contains a single line, which is the password used for `osslsigncode`
- `GITHUB_TOKEN_FILE`: A personal access token for Github API used to create releases and upload packages to Github.
- `DISCOURSE_TOKEN_FILE`: A personal access token for Discourse, used to create release posts.
- `CODESIGN_IDENTITY`: The identity used for MacOS `codesign` when signing the binary packages.
- `NOTARIZE_PROFILE`: The name of the profile used for notarizing MacOS packages.
- `AWS_UPLOAD_PROFILE`: The name of the AWS profile (from `aws-cli`) used to upload packages.
- `AWS_UPLOAD_BUCKET`: The name of the bucket where the package are uploaded to.
- `RELOAD_UPDATES_API_KEY`: A key used to refresh the manifest files being cached by the update servers.

# Tools used

These tools are used by the build process, each tool can be changed by using the environment variable name:

- `DOTNET=dotnet`: Mandatory. Used to build the binary contents that will be packaged.
- `GPG=gpg`: Optional. Used to created signed release files.
- `SIGNTOOL=osslsigncode`: Optional. Used to make Authenticode signing of Windows executables (using `SIGNTOOL=signtool.exe` on Windows)
- `CODESIGN=codesign`: Optional. Used to sign MacOS binaries and packages.
- `PRODUCTSIGN=productsign`: Optional. Used to sign MacOS packages.
- `WIX=wixl`: Optional. Required to build the Windows MSI packages.
- `DOCKER=docker`: Optional. Required to build some Linux packages.

# Invoking the tool

The tool has commandline documentation, which can be invoked with `dotnet run help`.

The two commands that are currently implemented are:

- `dotnet run create-key`
- `dotnet run build <channel>`

The `create-key` command creates a RSA 1024 bit signing, and encrypts it with the given password.

The `build` command creates the packages. An example command that builds all packages would be:

```
dotnet run build debug --version 1.0.0.1
```

To build only a single platform relase, disable all the features and choose the architectures:

```
dotnet run build debug --version 1.0.0.1 \
    --disable-docker-push true \
    --keep-builds true \
    --disable-authenticode true \
    --disable-signcode true \
    --disable-notarize-signing true \
    --disable-gpg-signing true \
    --disable-s3-upload true \
    --disable-github-upload true \
    --disable-update-server-reload true \
    --disable-discourse-announce true \
    --targets win-x86-gui.zip \
    --targets win-x64-gui.zip \
```
