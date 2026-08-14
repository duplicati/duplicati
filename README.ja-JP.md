# Duplicati

[English](./README.md) | [中文](./README.zh-CN.md) | **日本語**

暗号化したバックアップを、クラウドストレージサービスで安全に保管しましょう！

[![Open Collectiveでのサポーター](https://opencollective.com/duplicati/backers/badge.svg)](#backers) [![Open Collectiveでのスポンサー](https://opencollective.com/duplicati/sponsors/badge.svg)](#sponsors) [![Travis-CIでのビルドの状況](https://travis-ci.org/duplicati/duplicati.svg?branch=master)](https://travis-ci.org/duplicati/duplicati)
[![カバレッジの状況](https://coveralls.io/repos/github/duplicati/duplicati/badge.svg?branch=HEAD)](https://coveralls.io/github/duplicati/duplicati?branch=HEAD)
[![ライセンス](https://img.shields.io/github/license/duplicati/duplicati.svg)](https://github.com/duplicati/duplicati/blob/master/LICENSE)
[![Gurubase](https://img.shields.io/badge/Gurubase-Ask%20Duplicati%20Guru-006BFF)](https://gurubase.io/g/duplicati)

Duplicati は、フリー（自由）でオープンソースのバックアップ用クライアントです。圧縮し、暗号化した増分バックアップを、クラウドストレージサービスや遠隔のファイルサーバー上に安全に保存できます。Duplicati は、主に以下のサービスやソフトウェアで使うことができます。

&nbsp;&nbsp; _Amazon S3、[IDrive e2](https://www.idrive.com/e2/duplicati "Using Duplicati with IDrive e2")、[Backblaze (B2)](https://www.backblaze.com/blog/duplicati-backups-cloud-storage/ "Duplicati with Backblaze B2 Cloud Storage")、Box、Dropbox、FTP、Google クラウド、Google ドライブ、MEGA、Microsoft Azure、Microsoft OneDrive、Rackspace Cloud Files、OpenStack Storage (Swift)、Storj DCS、SSH (SFTP)、WebDAV、Tencent クラウドオブジェクトストレージ（COS）、Aliyun OSS、[その他にも対応しています！](https://docs.duplicati.com/backup-destinations/destination-overview)_

Duplicati は MIT ライセンスで公開されており、Windows、macOS、Linux で利用できます。

# ダウンロード

[ここをクリックすると、Duplicati の最新リリースをダウンロードできます。](https://duplicati.com/download)

ベータ版では、アップデートがある場合に自動的に通知を行い、1 クリック（またはターミナルでのコマンド入力）でアップグレードできます。
より新しい[テスト版に関しては、最新のリリースを確認](https://github.com/duplicati/duplicati/releases)するか、ソフトウェア上の画面またはコマンドラインで、別のアップデートチャンネルを選択してください。

全てのリリースは、GPG で署名されます。署名に使われる公開鍵は[3DAC703D](https://keys.openpgp.org/search?q=0xC20E90473DAC703D)となります。最新の署名ファイル（バイナリー版と ASCII 版）については、[Duplicati のダウンロード用ページ](https://github.com/duplicati/duplicati/releases)から入手できます。

# サポート

Duplicati は、活発なコミュニティーによってサポートが行われています。コミュニティーには[フォーラム](https://forum.duplicati.com)からご参加ください。

[Duplicati のマニュアル](https://docs.duplicati.com)もあります。マニュアルの作成や維持にぜひ[ご参加](https://github.com/duplicati/documentation)ください。

# 機能

- 全てのデータについて、アップロードする前に AES-256（または GNU Privacy Guard）による暗号化を行い、データの安全性を確保します。
- 最初に全体のフルバックアップを行い、その後、小さな増分のバックアップを送信することにより、回線の帯域幅と保存領域の使用量を節約します。
- スケジュールの設定機能により、バックアップを最新のものに自動的に維持します。
- 新しいリリースが公開された際に、通知を行います。
- 暗号化したバックアップのファイルを、FTP、WebDAV、SSH (SFTP)、Amazon S3 などのサービスに送信します。
- フォルダー、ドキュメントや画像などファイルの種類、ユーザー定義のフィルターを指定して、バックアップを実行できます。
- 簡単に使える操作画面と、コマンドラインのツールを備えています。
- Windows のボリュームシャドウコピーサービス（VSS）や、Linux の論理ボリューム管理（LVM）によって、プログラムによって開かれているファイルや、ロックされているファイルを適切にバックアップできます。
- フィルター、削除に関するルール、転送や帯域幅に関する設定などを行えます。

# Duplicati の利点

データを安全に保ち、離れたところに保管し、定期的にバックアップしましょう！　多くのバックアップ手段は、これら不可欠な要件を満たせません。しかし Duplicati は、この 3 つすべてに優れています。

- **データを安全に保つ:** Duplicati は強力な暗号化を使い、あなたのデータのプライバシーを守ります。よく検討したパスワードを使えば、あなたのバックアップファイルは、公開されているウェブサーバー上にあっても、自宅にある暗号化されていないファイルより安全です。
- **バックアップを遠隔に保管する:** バックアップを遠隔のサーバーに保存することで、火事のようなローカルの災害からデータを守ります。Duplicati は増分バックアップに対応しているため、遠く離れた保存先も効率的に利用できます。
- **定期的にバックアップする:** 古くなったバックアップは、バックアップが無いのと同じです。Duplicati の内蔵スケジューラが、バックアップを常に最新の状態に保ちます。さらに、圧縮と増分バックアップを利用して、保存領域と帯域幅を節約します。

# 開発に参加

## 不具合を報告

バグの管理には GitHub を使っています。不具合を発見した場合は https://github.com/duplicati/duplicati/issues で既存の Issue がないか検索して、もしまだ報告されていないようであれば、新しい Issue を作成してください。

## 翻訳に参加

Duplicati の翻訳に興味がある場合は、[Transifex](https://explore.transifex.com/duplicati/duplicati/)で翻訳作業にご参加ください。

## 開発作業に参加

開発環境を設定して Duplicati をビルドする方法については、[ドキュメント](https://docs.duplicati.com/installation-details/developer)をご覧ください。不具合を修正したり、Duplicati を改善したりするプルリクエストについては、いつでも歓迎します。

修正すべき問題を探している場合は、[minor change](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22minor+change%22)の Issue を確認してみてください。ウェブ UI の開発に慣れている場合は、 [「UI」でタグ付けされた Issue](https://github.com/duplicati/duplicati/issues?q=is%3Aissue+is%3Aopen+label%3A%22UI%22)を見てみてください。

貢献していただいた皆様に感謝いたします！
<a href="https://github.com/duplicati/duplicati/graphs/contributors"><img src="https://opencollective.com/duplicati/contributors.svg?width=890" /></a>

## 後援

後援いただいている皆様に感謝いたします！🙏

<a href="https://opencollective.com/duplicati#backers" target="_blank"><img src="https://opencollective.com/duplicati/backers.svg?width=890"></a>

## スポンサー

このオープンソースプロジェクトを支援してくださるスポンサーの皆様に、特別な感謝を捧げます。

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
