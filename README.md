# Duplicati

**English** | [中文](./README.zh-CN.md) | [日本語](./README.ja-JP.md)

Store securely encrypted backups on cloud storage services!

[![Backers on Open Collective](https://opencollective.com/duplicati/backers/badge.svg)](#backers) [![Sponsors on Open Collective](https://opencollective.com/duplicati/sponsors/badge.svg)](#sponsors) [![Build Status on Travis-CI](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![Coverage Status](https://coveralls.io/repos/github/duplicati/duplicati/badge.svg?branch=HEAD)](https://coveralls.io/github/duplicati/duplicati?branch=HEAD)
[![License](https://img.shields.io/github/license/duplicati/duplicati.svg)](https://github.com/duplicati/duplicati/blob/master/LICENSE)
[![Gurubase](https://img.shields.io/badge/Gurubase-Ask%20Duplicati%20Guru-006BFF)](https://gurubase.io/g/duplicati)

Duplicati is a free, open-source backup client that securely stores encrypted, incremental, and compressed backups on cloud storage services and remote file servers. It supports:

&nbsp;&nbsp; _Amazon S3, [IDrive e2](https://www.idrive.com/e2/duplicati "Using Duplicati with IDrive e2"), [Backblaze (B2)](https://www.backblaze.com/blog/duplicati-backups-cloud-storage/ "Duplicati with Backblaze B2 Cloud Storage"), Box, Dropbox, FTP, Google Cloud and Drive, MEGA, Microsoft Azure and OneDrive, Rackspace Cloud Files, OpenStack Storage (Swift), Storj DCS, SSH (SFTP), WebDAV, Tencent Cloud Object Storage (COS), Aliyun OSS, [and more!](https://docs.duplicati.com/backup-destinations/destination-overview)_

Duplicati is licensed under the MIT license and is available for Windows, macOS, and Linux.

# Download

[Click here to download the latest Duplicati release.](https://duplicati.com/download)

The beta release will automatically notify you of updates and allows you to upgrade with a single click (or command in the terminal). For even more [bleeding edge access, check the latest releases](https://github.com/duplicati/duplicati/releases) or choose another update channel in the UI or on the commandline.

All releases are GPG-signed with the public key [3DAC703D](https://keys.openpgp.org/search?q=0xC20E90473DAC703D). The latest signature file and ASCII signature file are available on [the Duplicati download page](https://github.com/duplicati/duplicati/releases).

# Support

Duplicati is supported by an [active community and you can reach them via our forum](https://forum.duplicati.com).

We also provide a comprehensive [Duplicati manual](https://docs.duplicati.com), which you can [contribute to](https://github.com/duplicati/documentation).

# Features

- Duplicati uses AES-256 encryption (or GNU Privacy Guard) to secure all data before uploading.
- Initial full backup followed by smaller, incremental updates to save bandwidth and storage.
- Built-in scheduler ensures backups stay up-to-date automatically.
- An integrated updater notifies you of new releases.
- Encrypted backups can be transferred to destinations like FTP, WebDAV, SSH (SFTP), Amazon S3, and more.
- Flexible backup options: back up folders, specific file types (e.g., documents or images), or use custom filters.
- Available as a user-friendly application or a command-line tool.
- Supports backing up open or locked files using Volume Snapshot Service (VSS) on Windows or Logical Volume Manager (LVM) on Linux.
- Advanced options for filters, deletion rules, transfer settings, bandwidth limits, and more.

# Why Use Duplicati?

Keep your data safe, store it remotely, and back it up regularly! Many backup solutions fail to meet these essential requirements, but Duplicati excels at all three:

- **Keep your data safe:** Duplicati uses strong encryption to ensure your data remains private. With a secure password, your backup files are safer on a public web server than unencrypted files at home.
- **Store your backup remotely:** Protect your data from local disasters like fires by storing backups on remote servers. Duplicati supports incremental backups, making it efficient to use distant storage destinations.
- **Backup regularly:** Outdated backups are as good as no backups. Duplicati's built-in scheduler ensures your backups are always current. It also uses compression and incremental backups to save storage and bandwidth.

# Contributing

## Reporting Bugs

We use GitHub for bug tracking. Please search existing issues before creating a new one:
<https://github.com/duplicati/duplicati/issues>.

## Contributing Translations

Want to help translate Duplicati? Contributions are welcome on Transifex:
<https://explore.transifex.com/duplicati/duplicati/>.

## Contributing Code

Instructions for setting up your development environment and building Duplicati are available in the [documentation](https://docs.duplicati.com/installation-details/developer). Pull requests for bug fixes or improvements are highly appreciated.

Looking for something to work on? Check out [minor change issues](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22minor+change%22) or [UI-related issues](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22UI%22).

Thank you to all our contributors:
<a href="https://github.com/duplicati/duplicati/graphs/contributors"><img src="https://opencollective.com/duplicati/contributors.svg?width=890" /></a>

## Backers

Thank you to all our backers! 🙏
<a href="https://opencollective.com/duplicati#backers" target="_blank"><img src="https://opencollective.com/duplicati/backers.svg?width=890"></a>

## Sponsors

A special thanks to our sponsors for supporting this open-source project:
<a href="https://opencollective.com/duplicati/sponsor/0/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/0/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/1/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/1/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/2/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/2/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/3/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/3/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/4/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/4/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/5/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/5/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/6/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/6/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/7/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/7/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/8/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/8/avatar.svg"></a>
<a href="https://opencollective.com/duplicati/sponsor/9/website" target="_blank"><img src="https://opencollective.com/duplicati/sponsor/9/avatar.svg"></a>
