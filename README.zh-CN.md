# Duplicati

[English](./README.md) | **中文** | [日本語](./README.ja-JP.md)

[中文官网](https://duplicati.com)

在云存储服务上安全地存储加密备份！

[![Open Collective 上的支持者](https://opencollective.com/duplicati/backers/badge.svg)](#backers) [![Open Collective 上的赞助商](https://opencollective.com/duplicati/sponsors/badge.svg)](#sponsors) [![Travis-CI 上的构建状态](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![覆盖率状态](https://coveralls.io/repos/github/duplicati/duplicati/badge.svg?branch=HEAD)](https://coveralls.io/github/duplicati/duplicati?branch=HEAD)
[![许可](https://img.shields.io/github/license/duplicati/duplicati.svg)](https://github.com/duplicati/duplicati/blob/master/LICENSE)
[![Gurubase](https://img.shields.io/badge/Gurubase-Ask%20Duplicati%20Guru-006BFF)](https://gurubase.io/g/duplicati)

Duplicati 是一个免费、开源的备份客户端，可以安全地将加密、增量、压缩的备份存储在云存储服务和远程文件服务器上。它与以下服务兼容：

&nbsp;&nbsp; _亚马逊 S3、[IDrive e2](https://www.idrive.com/e2/duplicati "使用 Duplicati 与 IDrive e2")、[Backblaze (B2)](https://www.backblaze.com/blog/duplicati-backups-cloud-storage/ "Duplicati 与 Backblaze B2 云存储")、Box、Dropbox、FTP、Google Cloud 和 Drive、MEGA、Microsoft Azure 和 OneDrive、Rackspace Cloud Files、OpenStack Storage (Swift)、Storj DCS、SSH (SFTP)、WebDAV、腾讯云对象存储 (COS)、阿里云对象存储(OSS)、[以及更多！](https://docs.duplicati.com/backup-destinations/destination-overview)_

Duplicati 根据 MIT 许可证授权，并可用于 Windows、macOS 和 Linux。

# 下载

[点击此处下载最新的 Duplicati 发布版。](https://duplicati.com/download)

测试版将自动通知您更新，并允许您通过单击（或在终端中的命令）升级。
要获取更多[前沿版本，查看最新发布](https://github.com/duplicati/duplicati/releases)或在 UI 或命令行中选择另一个更新渠道。

所有发布版本都使用公钥 [3DAC703D](https://keys.openpgp.org/search?q=0xC20E90473DAC703D) 进行 GPG 签名。最新的签名文件和最新的 ASCII 签名文件也可以在 [Duplicati 下载页面](https://github.com/duplicati/duplicati/releases) 获取。

# 支持

Duplicati 由一个[活跃的社区支持，您可以通过我们的论坛与他们联系](https://forum.duplicati.com)。

我们有一个很棒的 [Duplicati 手册](https://docs.duplicati.com)，您也可以[为其做出贡献](https://github.com/duplicati/documentation)。

# 功能

- Duplicati 使用 AES-256 加密（或 GNU Privacy Guard）在上传之前加密所有数据。
- Duplicati 最初上传完整备份，之后存储较小的增量更新，以节省带宽和存储空间。
- 计划程序自动保持备份的最新状态。
- 集成的更新器会通知您新版本发布
- 加密的备份文件被传输到像 FTP、WebDAV、SSH (SFTP)、Amazon S3 等目标。
- Duplicati 允许备份文件夹、文档类型（如文档或图像）或自定义过滤规则。
- Duplicati 有易于使用的用户界面和命令行工具。
- Duplicati 可以使用 Windows 下的卷快照服务 (VSS) 或 Linux 下的逻辑卷管理器 (LVM) 对打开或锁定的文件进行正确备份。
- 过滤器、删除规则、传输和带宽选项等

# 为什么使用 Duplicati？

保护您的数据安全，将其存储在远处，并定期备份！许多备份解决方案都无法满足这些基本要求，但 Duplicati 在这三方面都表现出色：

- **保护您的数据安全：** Duplicati 使用强大的加密来确保您的数据保持私密。使用安全的密码，您的备份文件即使放在公共网络服务器上，也比放在家里的未加密文件更安全。
- **将备份存储在远处：** 通过将备份存储在远程服务器上，保护您的数据免受火灾等本地灾难的影响。Duplicati 支持增量备份，因此可以高效地使用较远的存储目标。
- **定期备份：** 过时的备份等同于没有备份。Duplicati 内置的计划程序确保您的备份始终保持最新。它还使用压缩和增量备份来节省存储空间和带宽。

# 贡献

## 贡献错误报告

我们使用 GitHub 进行错误跟踪。请在创建新问题前先搜索已有的问题，看看您的错误是否已被记录：
https://github.com/duplicati/duplicati/issues

## 贡献翻译

对帮助翻译 duplicati 感兴趣吗？欢迎在 transifex 提供帮助：
https://explore.transifex.com/duplicati/duplicati/

## 贡献代码

关于如何设置您的开发环境以及如何构建 duplicati 的说明可以在 [docs](https://docs.duplicati.com/installation-details/developer) 中找到。我们欣赏任何修复错误或以其他方式改进 duplicati 的拉取请求。

如果您正在寻找一个问题来解决，请尝试查看其中一个标记为 [小改动](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22minor+change%22) 的问题。如果您最熟悉的是 Web 开发，请查看标记为 [UI](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22UI%22) 的问题。

感谢我们所有的现有贡献者：
<a href="https://github.com/duplicati/duplicati/graphs/contributors"><img src="https://opencollective.com/duplicati/contributors.svg?width=890" /></a>

## 赞助者

感谢所有的赞助者！🙏

<a href="https://opencollective.com/duplicati#backers" target="_blank"><img src="https://opencollective.com/duplicati/backers.svg?width=890"></a>

## 赞助商

特别感谢我们的赞助商对这个开源项目的支持：

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
