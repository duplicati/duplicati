# Duplicati 中文

[Duplicati 中文网站](https://duplicati.cn)

在云存储服务上安全地存储加密备份！

<!---
以下功能目前不可用...
[![问题统计](http://www.issuestats.com/github/duplicati/duplicati/badge/pr)](http://www.issuestats.com/github/duplicati/duplicati/)
[![问题统计](http://www.issuestats.com/github/duplicati/duplicati/badge/issue)](http://www.issuestats.com/github/duplicati/duplicati/)
-->

<!--
已移除 Gitter
[![在 https://gitter.im/duplicati/Lobby 聊天](https://badges.gitter.im/duplicati/Lobby.svg)](https://gitter.im/duplicati/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
-->

[![Open Collective 上的支持者](https://opencollective.com/duplicati/backers/badge.svg)](#backers) [![Open Collective 上的赞助商](https://opencollective.com/duplicati/sponsors/badge.svg)](#sponsors) [![Travis-CI 上的构建状态](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![AppVeyor 上的构建状态](https://ci.appveyor.com/api/projects/status/h8s5nau9mn311hq0/branch/master?svg=true)](https://ci.appveyor.com/project/kenkendk/duplicati/branch/master)
[![Bountysource](https://www.bountysource.com/badge/tracker?tracker_id=4870652)](https://www.bountysource.com/teams/duplicati?tracker_ids=4870652&utm_medium=shield&utm_campaign=TRACKER_BADGE)
[![覆盖率状态](https://coveralls.io/repos/github/duplicati/duplicati/badge.svg?branch=HEAD)](https://coveralls.io/github/duplicati/duplicati?branch=HEAD)
[![许可](https://img.shields.io/github/license/duplicati/duplicati.svg)](https://github.com/duplicati/duplicati/blob/master/LICENSE.txt)


Duplicati 是一个免费、开源的备份客户端，可以安全地将加密、增量、压缩的备份存储在云存储服务和远程文件服务器上。它与以下服务兼容：

&nbsp;&nbsp; *亚马逊 S3、[IDrive e2](https://www.idrive.com/e2/duplicati "使用 Duplicati 与 IDrive e2")、[Backblaze (B2)](https://www.backblaze.com/blog/duplicati-backups-cloud-storage/ "Duplicati 与 Backblaze B2 云存储")、Box、Dropbox、FTP、Google Cloud 和 Drive、MEGA、Microsoft Azure 和 OneDrive、Rackspace Cloud Files、OpenStack Storage (Swift)、Sia、Storj DCS、SSH (SFTP)、WebDAV、百度网盘、阿里云盘、腾讯云对象存储 (COS)、[以及更多！](https://duplicati.readthedocs.io/en/latest/01-introduction/#supported-backends)*

Duplicati 根据 LGPL 许可证授权，并可用于 Windows、OSX 和 Linux (.NET 4.7.1+ 或 Mono 5.10.0+ 需要)。

下载
========

Duplicati 的最新版本是 Duplicati 2.0 发布的测试版。

[点击此处下载最新的 Duplicati 2.0 测试版。](http://www.duplicati.com/download)

测试版将自动通知您更新，并允许您通过单击（或在终端中的命令）升级。
要获取更多[前沿版本，查看最新发布](https://github.com/duplicati/duplicati/releases)或在 UI 或命令行中选择另一个更新渠道。

所有发布版本都使用公钥 [3DAC703D](https://pgp.mit.edu/pks/lookup?op=get&search=0xC20E90473DAC703D) 进行 GPG 签名。最新的签名文件和最新的 ASCII 签名文件也可以在 [Duplicati 下载页面](https://github.com/duplicati/duplicati/releases) 获取。

支持
=======

Duplicati 由一个[活跃的社区支持，您可以通过我们的论坛与他们联系](https://forum.duplicati.com)。

我们有一个很棒的 [Duplicati 手册](https://docs.duplicati.com)，您也可以[为其做出贡献](https://github.com/kees-z/DuplicatiDocs)。

功能
========

  * Duplicati 使用 AES-256 加密（或 GNU Privacy Guard）在上传之前加密所有数据。
  * Duplicati 最初上传完整备份，之后存储较小的增量更新，以节省带宽和存储空间。
  * 计划程序自动保持备份的最新状态。
  * 集成的更新器会通知您新版本发布
  * 加密的备份文件被传输到像 FTP、Cloudfiles、WebDAV、SSH (SFTP)、Amazon S3 等目标。
  * Duplicati 允许备份文件夹、文档类型（如文档或图像）或自定义过滤规则。
  * Duplicati 有易于使用的用户界面和命令行工具。
  * Duplicati 可以使用 Windows 下的卷快照服务 (VSS) 或 Linux 下的逻辑卷管理器 (LVM) 对打开或锁定的文件进行正确备份。这允许 Duplicati 在 Outlook 运行时备份 Microsoft Outlook PST 文件。
  * 过滤器、删除规则、传输和带宽选项等

为什么使用 Duplicati？
==================

保护您的数据安全，将其存储在远处，定期更新您的备份！
这是一个简单的规则，但许多备份解决方案今天都无法做到。
但 Duplicati 做到了！

保护您的数据安全！网络上的坏人似乎到处寻找有趣的数据。但人们不想在任何地方看到他们的私人数据。Duplicati 提供强大的加密，确保您的数据对他人看起来像垃圾。通过精心选择的密码，您的备份文件在公共网络服务器上比您在家里的未加密文件更安全。

将您的备份存储在远处！当备份与其原始数据一起被摧毁时，最好的备份也是无用的。假设一场火灾摧毁了您的办公室 - 您的备份能幸存下来吗？Duplicati 将备份存储在各种远程文件服务器上，并支持增量备份，因此只需传输更改的部分。这使得使用远离原始数据的目的地变得容易。

定期备份！最糟糕的情况是，您的备份过时了，仅仅是因为有人忘了在正确的时间备份。Duplicati 有内置的计划程序，因此很容易拥有定期、最新的备份。此外，Duplicati 使用文件压缩，并能够存储增量备份以节省存储空间和带宽。

贡献
==================

## 贡献 Bug 报告
我们使用 GitHub 进行 bug 跟踪。请搜索您的 bug 的现有问题，并在尚未跟踪问题时创建一个新问题：
https://github.com/duplicati/duplicati/issues

## 贡献翻译
有兴趣帮助翻译 duplicati 吗？在 transifex 上的帮助总是受欢迎的：
https://www.transifex.com/duplicati/duplicati/dashboard/

## 贡献代码
关于如何设置您的开发环境和构建 Duplicati 的说明可以在 [wiki](https://github.com/duplicati/duplicati/wiki/How-to-build-from-source) 中找到。欢迎提交修复错误或以其他方式改进 Duplicati 的拉取请求。

如果您正在寻找一个问题来修复，尝试查看其中一个 [小改动](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22minor+change%22) 问题。如果您最熟悉的是 Web 开发，可以看看 [标记为 UI 的问题](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22UI%22)。

感谢我们所有现有的贡献者：
<a href="https://github.com/duplicati/duplicati/graphs/contributors"><img src="https://opencollective.com/duplicati/contributors.svg?width=890" /></a>


## 赞助者

感谢我们所有的赞助者！🙏 [[成为赞助者](https://opencollective.com/duplicati#backer)]

<a href="https://opencollective.com/duplicati#backers" target="_blank"><img src="https://opencollective.com/duplicati/backers.svg?width=890"></a>


## 赞助商

通过成为赞助商支持这个项目。您的徽标将出现在这里，并带有链接到您的网站。[[成为赞助商](https://opencollective.com/duplicati#sponsor)]

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
