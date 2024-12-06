# Duplicati

[English](./README.md) | [中文](./README.zh-CN.md) | **日本語**

暗号化したバックアップを、クラウドストレージサービスで安全に保管しましょう！

<!---
以下は現在機能していません…
[![Issue Stats](http://www.issuestats.com/github/duplicati/duplicati/badge/pr)](http://www.issuestats.com/github/duplicati/duplicati/)
[![Issue Stats](http://www.issuestats.com/github/duplicati/duplicati/badge/issue)](http://www.issuestats.com/github/duplicati/duplicati/)
-->

<!--
Gitterは削除済
[![Join the chat at https://gitter.im/duplicati/Lobby](https://badges.gitter.im/duplicati/Lobby.svg)](https://gitter.im/duplicati/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
-->

[![Open Collectiveでのサポーター](https://opencollective.com/duplicati/backers/badge.svg)](#backers) [![Open Collectiveでのスポンサー](https://opencollective.com/duplicati/sponsors/badge.svg)](#sponsors) [![Travis-CIでのビルドの状況](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![カバレッジの状況](https://coveralls.io/repos/github/duplicati/duplicati/badge.svg?branch=HEAD)](https://coveralls.io/github/duplicati/duplicati?branch=HEAD)
[![ライセンス](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/duplicati/duplicati/blob/master/LICENSE.txt)


Duplicatiは、フリー（自由）でオープンソースのバックアップ用クライアントです。圧縮し、暗号化した増分バックアップを、クラウドストレージサービスや遠隔のファイルサーバー上に安全に保存できます。Duplicatiは、主に以下のサービスやソフトウェアで使うことができます。

&nbsp;&nbsp; *Amazon S3、[IDrive e2](https://www.idrive.com/e2/duplicati "Using Duplicati with IDrive e2")、[Backblaze (B2)](https://www.backblaze.com/blog/duplicati-backups-cloud-storage/ "Duplicati with Backblaze B2 Cloud Storage")、Box、Dropbox、FTP、Googleクラウド、Googleドライブ、MEGA、Microsoft Azure、Microsoft OneDrive、Rackspace Cloud Files、OpenStack Storage (Swift)、Sia、Storj DCS、SSH (SFTP)、WebDAV、Tencentクラウドオブジェクトストレージ（COS）、Aliyun OSS、[その他にも対応しています！](https://docs.duplicati.com/backup-destinations/destination-overview)*

DuplicatiはMITライセンスで公開されており、Windows、OSX、Linuxで利用できます（.NET 4.7.1以上、またはMono 5.10.0以上が必要です）。

ダウンロード
========

Duplicati 2.0のベータ版がDuplicatiの最新バージョンとなります。

[ここをクリックすると、Duplicati 2.0のベータ版をダウンロードできます。](https://duplicati.com/download)

ベータ版では、アップデートがある場合に自動的に通知を行い、1クリック（またはターミナルでのコマンド入力）でアップグレードできます。
より新しい[テスト版に関しては、最新のリリースを確認](https://github.com/duplicati/duplicati/releases)するか、ソフトウェア上の画面またはコマンドラインで、別のアップデートチャンネルを選択してください。

全てのリリースは、GPGで署名されます。署名に使われる公開鍵は[3DAC703D](https://keys.openpgp.org/search?q=0xC20E90473DAC703D)となります。最新の署名ファイル（バイナリー版とASCII版）については、[Duplicatiのダウンロード用ページ](https://github.com/duplicati/duplicati/releases)から入手できます。

サポート
=======

Duplicatiは、活発なコミュニティーによってサポートが行われています。コミュニティーには[フォーラム](https://forum.duplicati.com)からご参加ください。

[Duplicatiのマニュアル](https://docs.duplicati.com)もあります。マニュアルの作成や維持にぜひ[ご参加](https://github.com/kees-z/DuplicatiDocs)ください。

機能
========

  * 全てのデータについて、アップロードする前にAES-256（またはGNU Privacy Guard）による暗号化を行い、データの安全性を確保します。
  * 最初に全体のフルバックアップを行い、その後、小さな増分のバックアップを送信することにより、回線の帯域幅と保存領域の使用量を節約します。
  * スケジュールの設定機能により、バックアップを最新のものに自動的に維持します。
  * 新しいリリースが公開された際に、通知を行います。
  * 暗号化したバックアップのファイルを、FTP、 Cloudfiles、WebDAV、SSH (SFTP)、Amazon S3などのサービスに送信します。
  * フォルダー、ドキュメントや画像などファイルの種類、ユーザー定義のフィルターを指定して、バックアップを実行できます。
  * 簡単に使える操作画面と、コマンドラインのツールを備えています。
  * Windowsのボリュームシャドウコピーサービス（VSS）や、Linuxの論理ボリューム管理（LVM）によって、プログラムによって開かれているファイルや、ロックされているファイルを適切にバックアップできます。これにより、Microsoft Outlookを使っている際に、OutlookのPSTファイルをバックアップできます。
  * フィルター、削除に関するルール、転送や帯域幅に関する設定などを行えます。

Duplicatiの利点
==================

データを安全に保つこと。離れたところに保管すること。バックアップを定期的に更新すること。
とてもシンプルなルールですが、今日の多くのバックアップ用サービスやソフトウェアは、これを達成していません。
一方、Duplicatiでは、このルールを実践しています！

データを安全に保ちましょう！　悪意をもったインターネット上の人々は、興味を引くデータをあらゆるところで探し回っているようです。しかしユーザーは、自らのプライベートなデータが第三者に暴かれてもよいとは誰も思っていません。Duplicatiでは、強力な暗号を使うことで、あなたのデータが、自分以外には全く意味不明なものになっていることを保証します。よく検討されたパスワードを使うと、あなたのバックアップファイルは、公開されているウェブサーバー上に保管されている場合でも、あなたの自宅にあり、しかし暗号化されずに保管されているファイルと比べて、より安全なものとなります。

バックアップは、離れたところに保管しましょう！　たとえバックアップが完璧だったとしても、それがバックアップ元のデータもろとも失われてしまっては何の意味もありません。職場で火事があった場合を想像してみてください。… バックアップは火事にも負けず生き残りますか？　Duplicatiはバックアップを多様な遠隔のファイルサーバーに保存し、データの更新が必要な部分だけが転送されるよう、増分バックアップをサポートしています。これによって、バックアップ元のデータから遠く離れたところにバックアップを保管しやすくなっています。

定期的にバックアップを行いましょう！　最悪のケースは、しかるべきときにバックアップを行うことをうっかり忘れていたために、バックアップが古くなってしまっていることです。Duplicatiにはスケジュールの設定機能が備わっているので、簡単に、最新の状態のバックアップを定期的に作成できます。また、Duplicatiはファイルの圧縮を実行し、増分バックアップを行えるため、保存領域と帯域幅を節約できます。

開発に参加
==================

## 不具合を報告
バグの管理にはGitHubを使っています。不具合を発見した場合は https://github.com/duplicati/duplicati/issues で既存のIssueがないか検索して、もしまだ報告されていないようであれば、新しいIssueを作成してください。

## 翻訳に参加
Duplicatiの翻訳に興味がある場合は、[Transifex](https://www.transifex.com/duplicati/duplicati/dashboard/)で翻訳作業にご参加ください。

## 開発作業に参加
開発環境を設定してDuplicatiをビルドする方法については、[ウィキ](https://github.com/duplicati/duplicati/wiki/How-to-build-from-source)をご覧ください。不具合を修正したり、Duplicatiを改善したりするプルリクエストについては、いつでも歓迎します。

修正すべき問題を探している場合は、[minor change](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22minor+change%22)のIssueを確認してみてください。ウェブUIの開発に慣れている場合は、 [「UI」でタグ付けされたIssue](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22UI%22)を見てみてください。


貢献していただいた皆様に感謝いたします！
<a href="https://github.com/duplicati/duplicati/graphs/contributors"><img src="https://opencollective.com/duplicati/contributors.svg?width=890" /></a>


## 後援

後援いただいている皆様に感謝いたします！🙏

<a href="https://opencollective.com/duplicati#backers" target="_blank"><img src="https://opencollective.com/duplicati/backers.svg?width=890"></a>


## スポンサー

以下に、Duplicatiに寄付していただいたスポンサーを一覧でご紹介します。

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
