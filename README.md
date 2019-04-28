# Duplicati
Store securely encrypted backups on cloud storage services!

<!---
These are currently not working ...
[![Issue Stats](http://www.issuestats.com/github/duplicati/duplicati/badge/pr)](http://www.issuestats.com/github/duplicati/duplicati/)
[![Issue Stats](http://www.issuestats.com/github/duplicati/duplicati/badge/issue)](http://www.issuestats.com/github/duplicati/duplicati/)
-->

<!--
Removed Gitter
[![Join the chat at https://gitter.im/duplicati/Lobby](https://badges.gitter.im/duplicati/Lobby.svg)](https://gitter.im/duplicati/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
-->

[![Backers on Open Collective](https://opencollective.com/duplicati/backers/badge.svg)](#backers) [![Sponsors on Open Collective](https://opencollective.com/duplicati/sponsors/badge.svg)](#sponsors) [![Build Status on Travis-CI](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![Build status on AppVeyor](https://ci.appveyor.com/api/projects/status/h8s5nau9mn311hq0/branch/master?svg=true)](https://ci.appveyor.com/project/kenkendk/duplicati/branch/master)
[![Bountysource](https://www.bountysource.com/badge/tracker?tracker_id=4870652)](https://www.bountysource.com/teams/duplicati?tracker_ids=4870652&utm_medium=shield&utm_campaign=TRACKER_BADGE)
[![Coverage Status](https://coveralls.io/repos/github/duplicati/duplicati/badge.svg?branch=HEAD)](https://coveralls.io/github/duplicati/duplicati?branch=HEAD)
[![License](https://img.shields.io/github/license/duplicati/duplicati.svg)](https://github.com/duplicati/duplicati/blob/master/LICENSE.txt)


Duplicati is a free, open source, backup client that securely stores encrypted, incremental, compressed backups on cloud storage services and remote file servers. It works with:

&nbsp;&nbsp; *Amazon Cloud Drive and S3, [Backblaze (B2)](https://www.backblaze.com/blog/duplicati-backups-cloud-storage/ "Duplicati with Backblaze B2 Cloud Storage"), Box, Dropbox, FTP, Google Cloud and Drive, HubiC, MEGA, Microsoft Azure and OneDrive, Rackspace Cloud Files, OpenStack Storage (Swift), Sia, SSH (SFTP), WebDAV, [and more!](https://duplicati.readthedocs.io/en/latest/01-introduction/#supported-backends)*

Duplicati is licensed under LGPL and available for Windows, OSX and Linux (.NET 4.5+ or Mono required). 

Download
========

The latest version of Duplicati is a beta version for the Duplicati 2.0 release. 

[Click here to download the latest Duplicati 2.0 beta release.](http://www.duplicati.com/download)

The beta release will automatically notify you of updates and allows you to upgrade with a single click (or command in the terminal).
For even more [bleeding edge access, check the latest releases](https://github.com/duplicati/duplicati/releases) or choose another update channel in the UI or on the commandline.

All releases are GPG signed with the public key [3DAC703D](https://pgp.mit.edu/pks/lookup?op=get&search=0xC20E90473DAC703D). The latest signature file and latest ASCII signature file are also available from [the Duplicati download page](https://github.com/duplicati/duplicati/releases).

Support
=======

Duplicati is supported by an [active community and you can reach them via our forum](https://forum.duplicati.com).

We have a great [Duplicati manual](https://docs.duplicati.com) that you can also [contribute to](https://github.com/kees-z/DuplicatiDocs).

Features
========

  * Duplicati uses AES-256 encryption (or GNU Privacy Guard) to secure all data before it is uploaded.
  * Duplicati uploads a full backup initially and stores smaller, incremental updates afterwards to save bandwidth and storage space.
  * A scheduler keeps backups up-to-date automatically.
  * Integrated updater notifies you when a new release is out
  * Encrypted backup files are transferred to targets like FTP, Cloudfiles, WebDAV, SSH (SFTP), Amazon S3 and others.
  * Duplicati allows backups of folders, document types like e.g. documents or images, or custom filter rules. 
  * Duplicati is available as application with an easy-to-use user interface and as command line tool.
  * Duplicati can make proper backups of opened or locked files using the Volume Snapshot Service (VSS) under Windows or the Logical Volume Manager (LVM) under Linux. This allows Duplicati to back up the Microsoft Outlook PST file while Outlook is running.
  * Filters, deletion rules, transfer and bandwidth options, etc

Why use Duplicati?
==================

Keep your data safe, store it far away, update your backup regularly! 
This is a simple rule but many backup solutions do not achieve that today. 
But Duplicati does!

Keep your data safe! Bad guys on the Internet seem to look for interesting data everywhere. But people do not want to see any of their private data revealed anywhere. Duplicati provides strong encryption to make sure that your data looks like garbage to others. With a well chosen password your backup files will be more safe on a public webserver than your unencrypted files at home.

Store your backup far away! The best backup is useless when it is destroyed together with it's original data. Just assume that a fire destroys your office - would your backup survive? Duplicati stores backups on various remote file servers and it supports incremental backups so that only changed parts need to be transferred. This makes it easy to use a destination far away from the original data.

Backup regularly! The worst case is that your backup is outdated simply because someone forgot to make a backup at the right time. Duplicati has a built-in scheduler, so that it's easy to have a regular, up-to-date backup. Furthermore, Duplicati uses file compression and is able to store incremental backups to save storage space and bandwidth.

Contributing
==================

## Contributing Bug reports
We use GitHub for bug tracking. Please search the existing issues for your bug and create a new one if the issue is not yet tracked:
https://github.com/duplicati/duplicati/issues

## Contributing Translations
Interested in helping to translate duplicati? Help is always welcome at transifex:
https://www.transifex.com/duplicati/duplicati/dashboard/

## Contributing Code
Instructions on how to setup your development environment and build duplicati can be found in the [wiki](https://github.com/duplicati/duplicati/wiki/How-to-build-from-source). Pull requests fixing bugs or improving duplicati in another way are appreciated.

If you are looking for an issue to fix, try looking at one of the [minor change](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22minor+change%22) issues. If you are most used to web development, have a look at the [issues marked UI](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22UI%22).


Thank you to all our existing contributors:
<a href="https://github.com/duplicati/duplicati/graphs/contributors"><img src="https://opencollective.com/duplicati/contributors.svg?width=890" /></a>


## Backers

Thank you to all our backers! 🙏 [[Become a backer](https://opencollective.com/duplicati#backer)]

<a href="https://opencollective.com/duplicati#backers" target="_blank"><img src="https://opencollective.com/duplicati/backers.svg?width=890"></a>


## Sponsors

Support this project by becoming a sponsor. Your logo will show up here with a link to your website. [[Become a sponsor](https://opencollective.com/duplicati#sponsor)]

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


