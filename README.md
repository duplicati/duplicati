# Duplicati
Store securely encrypted backups on cloud storage services!

[![Build Status](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![Issue Stats](http://www.issuestats.com/github/duplicati/duplicati/badge/pr)](http://www.issuestats.com/github/duplicati/duplicati/)
[![Issue Stats](http://www.issuestats.com/github/duplicati/duplicati/badge/issue)](http://www.issuestats.com/github/duplicati/duplicati/)



Duplicati is a free, open source, backup client that securely stores encrypted, incremental, compressed backups on cloud storage services and remote file servers. It works with:
  * Amazon S3
  * OneDrive
  * Google Drive (Google Docs)
  * Rackspace Cloud Files
  * HubiC
  * Backblaze (B2)
  * Amazon Cloud Drive (AmzCD)
  * Swift / OpenStack
  * WebDAV
  * SSH (SFTP)
  * FTP
  * and more

Duplicati is licensed under LGPL and available for Windows, OSX and Linux (.NET 4.5+ or Mono required). 

Download
========

The latest version of Duplicati is a preview for the Duplicati 2.0 release. 

[Click here to download the latest Duplicati 2.0 preview release.](http://updates.duplicati.com/preview/latest.zip)

The preview release will automatically notify you and allows you to upgrade with a single click (or command in the terminal).

As of version 2.0.0.93, all releases are GPG signed with the public key [3DAC703D](https://pgp.mit.edu/pks/lookup?op=get&search=0xC20E90473DAC703D). The [latest signature file](http://updates.duplicati.com/preview/latest.zip) and [latest ASCII signature file](http://updates.duplicati.com/preview/latest.zip.sig.asc) are also available.

For even more [bleeding edge access, check the latest releases](https://github.com/duplicati/duplicati/releases).

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

Store your backup far away! The best backup is useless when it is destroyed together with it's original data. Just assume that a fire destroys your office - would your backup survive? Duplicati stores backups on various remote file servers and it supports incremental backups so that only changed parts need to be transfered. This makes it easy to use a destination far away from the original data.

Backup regularly! The worst case is that your backup is outdated simply because someone forgot to make a backup at the right time. Duplicati has a built-in scheduler, so that it's easy to have a regular, up-to-date backup. Furthermore, Duplicati uses file compression and is able to store incremental backups to save storage space and bandwidth.
