# DS4Xbox - 取扱説明書

DS4Xbox は、PS5 DualSense コントローラーを Xbox 360 コントローラーとしてシステムに認識させるための軽量な常駐型アプリケーションです。
ネイティブな Xbox コントローラー対応ゲーム（Forza Horizon シリーズなど）を DualSense で快適に遊ぶことができます。

## 使い方

### 配布 ZIP から使う場合

1. `ds4xbox_release.zip` を好きなフォルダに解凍します。
2. `start_gamepad.bat` を起動します。（管理者権限の許可ダイアログが出た場合は「はい」を押してください）
3. タスクトレイ（画面右下）に灰色のコントローラーアイコンが表示されます。
4. アイコンをダブルクリックするか、右クリックして「変換 ON」を選択します。
5. アイコンがカラーになり、変換が開始されます。これ以降は Xbox コントローラーとしてゲームが遊べます。
6. 反応確認は `open_game_controllers.bat` を起動し、`Controller (XBOX 360 For Windows)` を開いて行います。

### ソースから使う場合

`setup_gamepad.bat` を起動してください。Release publish を作成したあと、自動で DS4Xbox を起動します。

※タスクトレイアイコンを右クリックし「起動時に自動ON」にチェックを入れると、次回以降は起動するだけで自動的に変換が始まります。

## ビルドと診断

通常は `setup_gamepad.bat` を実行すれば、Release publish と起動まで自動で行います。手動で確認したい場合は以下を実行します。

```powershell
dotnet build -c Release
dotnet test -c Release
dotnet publish .\DS4Xbox.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

`start_gamepad.bat` は、配布フォルダでは同じ場所にある `DS4Xbox.exe`、ソースチェックアウトでは `bin\Release\net8.0-windows\win-x64\publish\DS4Xbox.exe` を起動します。
ドライバや実機状態を段階的に確認する場合は `diagnose_gamepad.bat` を実行してください。管理者権限が必要な場合は UAC ダイアログが出ます。診断結果はコマンド画面に残り、起動した `DS4Xbox.exe` と同じフォルダの `diagnostic_result.txt` と `ds4xbox.log` に出力されます。

---

## キー・ボタン対応表

DualSense の入力は、以下のように Xbox 360 コントローラーの入力として変換されます。

| DualSense (PS5) | Xbox 360 変換後 | 備考 |
| :--- | :--- | :--- |
| 左スティック | 左スティック | |
| 右スティック | 右スティック | |
| 方向キー (十字キー) | D-Pad (十字キー) | |
| □ボタン | X ボタン | |
| ×ボタン | A ボタン | 決定 / アクセル |
| 〇ボタン | B ボタン | キャンセル / ブレーキ |
| △ボタン | Y ボタン | |
| L1 | LB | |
| R1 | RB | |
| L2 (トリガー) | LT (左トリガー) | 押し込み量（アナログ）対応 |
| R2 (トリガー) | RT (右トリガー) | 押し込み量（アナログ）対応 |
| L3 (左スティック押し込み) | LS (左スティック押し込み) | |
| R3 (右スティック押し込み) | RS (右スティック押し込み) | |
| SHARE (クリエイト) ボタン | BACK ボタン | |
| OPTIONS ボタン | START ボタン | |
| PS ボタン | GUIDE (Xbox) ボタン | |
| タッチパッド押し込み | GUIDE (Xbox) ボタン | |

---

## よくある質問・トラブルシューティング

### Q. ゲーム内でコントローラーが2つ認識されてしまう（二重入力になる）
**A.** DS4Xbox は起動時に自動で「元のDualSense」を隠す処理（HidHideの自動設定）を行いますが、稀にシステム状況によって隠しきれない場合があります。
**解決策:** 
1. タスクトレイの DS4Xbox を一度「終了」してください。
2. コントローラーを抜き差し（またはBluetooth再接続）してから、再度 DS4Xbox を起動して ON にしてください。

### Q. アプリを起動してもタスクトレイにアイコンが出ない
**A.** 「管理者権限」のダイアログ（画面が暗くなり「許可しますか？」と聞かれる画面）が裏に隠れている可能性があります。タスクバーに点滅している盾のアイコンがないか確認し、許可（はい）を押してください。

### Q. 「DS4Xbox は既に起動しています」と表示される
**A.** すでにアプリが裏で動いています。画面右下のタスクトレイ（^マークの中など）に DS4Xbox のアイコンが隠れていないか確認してください。

### Q. コントローラーを繋いでいるのに一切反応しない
**A.** 以下の点をご確認ください。
1. DS4Xbox のアイコンが「カラー（ON）」になっているか確認してください。灰色（OFF）の場合はダブルクリックでONにしてください。
2. `diagnose_gamepad.bat` を実行し、`ViGEmClient SubmitReport succeeded` まで進むか確認してください。
3. `open_game_controllers.bat` を実行し、「Controller (XBOX 360 For Windows)」の状態が「OK」になっているか、プロパティからボタンが反応するか確認してください。

### Q. アンインストールしたい
**A.** DS4Xbox はインストール不要のソフトですので、フォルダごと削除するだけで完了です。
ただし、初回実行時に裏で動作に必要なドライバ（ViGEmBus、HidHide）をシステムにインストールしています。これらを完全に削除したい場合は、Windows の「設定」＞「アプリ」＞「インストールされているアプリ」から以下の2つをアンインストールしてください。
* `Nefarius Virtual Gamepad Emulation Bus`
* `Nefarius HidHide`
