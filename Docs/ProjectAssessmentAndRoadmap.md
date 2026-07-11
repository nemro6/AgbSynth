# AgbSynth 現状評価と実装ロードマップ

更新日: 2026-07-12

## 1. この文書の目的

この文書は、現在のAgbSynthのコード、プロジェクト形式、MP2K解析、再生エンジン、UI、テストを横断して確認し、以下を整理したものです。

- 現在どこまで実装できているか
- 現状の問題点と将来問題になり得る設計
- 今後の実装順序
- 各段階を完了と判断する条件
- 品質を維持しながら機能を増やすための改善案
- AgbSynthをより使いやすくする追加アイデア

この評価では、スピーカーや本体筐体による音質変化は対象にしません。目標はMP2KドライバとGBA音源回路のデジタル出力を可能な範囲で正確に再現し、編集したデータを再構築可能にすることです。特殊PCMとCry Editorは後段の対象とします。

## 2. 現状の要約

AgbSynthは、単なる試作段階よりかなり先まで進んでいます。ROM解析、資産抽出、各種エディタ、MIDI再生、ミキサー、PSG/PCM再生、設定保存、録音まで一通りの操作経路があります。一方で、現時点では「ROMから抽出して再生・編集するツール」としての機能が先行しており、「編集結果を安全に保存し、MP2Kデータへ可逆変換し、ROMへ再配置するツール」としての基盤が未完成です。

最重要課題は次の3点です。

1. Save、Dirty管理、Undo/Redoの基盤は実装済み。未保存確認と全編集操作の履歴網羅を継続検証する。
2. MIDIとMidi2agbをSongHeaderごとに切り替えるシーケンス管理とImport/Compile処理が未実装。
3. ROMコンパイラ、配置、ポインタ解決、出力ROM検証が未実装。

現在のテストはRelease構成で115件すべて成功しています。特に音源エンジンには比較的多くの単体テストがあります。ただし、UIを介した編集保存、プロジェクト往復、実ROMとの音声比較、ROM再構築のテストは不足しています。

## 3. 機能別の現状

| 領域 | 状態 | 現状 |
| --- | --- | --- |
| 新規プロジェクト | 実装済み | `.agbsynth`と資産フォルダ群を作成できる。 |
| プロジェクト読込・Refresh | 実装済み | フォルダを走査し、手動追加した対応資産も読み込める。 |
| ROM読込 | 実装済み | `.gba`を読み込み、手動指定またはAutoでSongTableを探せる。 |
| SongTable検出 | 暫定 | 一部既知ゲームの固定位置とヒューリスティックを併用している。誤検出・取りこぼしの余地がある。 |
| SongTable/SongHeader抽出 | 実装済み | 空エントリを含むテーブル、ヘッダ、グループ情報を資産化できる。 |
| MP2KからMIDIへの変換 | 実装済み・暫定 | Note、Wait、Tempo、GOTO、PATT、REPT、TIE/EOT、主要CC、XCMDの一部を処理する。MEMACCは消費のみ。 |
| MIDIからMP2Kへの変換 | 未実装 | MIDI編集結果をMP2Kへ戻せない。 |
| MP2KとMidi2agbの相互変換 | 未実装 | 高度なMP2K命令を保持する`.s`形式の抽出、読込、再生、コンパイルが必要。 |
| VoiceGroup抽出・編集 | 実装済み | 128音色、DirectSound、PSG、KeySplit、DrumSetを扱える。 |
| KeySplit/DrumSet | 実装済み・要検証 | 独立資産化、共有、編集、試聴が可能。レンジ・参照整合性の継続検証が必要。 |
| WaveData | 実装済み | signed 8-bit PCM、ループ、波形表示、試聴を扱える。 |
| WaveMemory | 実装済み | 16バイト波形、編集、プレビュー、試聴を扱える。 |
| MIDI入力 | 実装済み | 選択デバイスを次回起動時に復元し、CC/Program/Bendを受け取れる。 |
| シーケンス再生 | 実装済み・暫定 | MIDIを読み、トラック単位ループと各種制御を反映して再生する。 |
| MP2K PCM再生 | 高度な暫定実装 | 固定レート処理、量子化、DMAブロック遅延、補間、リバーブ等を近似・一部整数化している。 |
| PSG再生 | 高度な暫定実装 | Square 1/2、Wave、Noise、位相、ADSR、Len、Sweep、Pitch/LFOを実装している。 |
| Mixer | 実装済み | 16トラック表示、メータ、Mute/Solo、Pan、CC/LFO/Bend表示、発音警告、鍵盤表示がある。 |
| 設定 | 実装済み | 音声出力、バッファ、Hz、Stereo/Mono、PCM、Reverb、音量、メータ、テーマ、MIDI CC等を永続化する。 |
| 録音 | 実装済み・要強化 | エミュレーション出力をfloat WAVへ保存できる。長時間録音はメモリ使用量に注意が必要。 |
| Save Project | 実装済み・検証中 | 全管理資産を一時ファイルへ書き、成功後に一括置換する。削除も同一トランザクションで確定する。 |
| Undo/Redo | 実装済み・検証中 | 行操作、通常フィールド、Voice、KeySplit、波形編集を履歴化。保存地点とDirty状態を同期する。 |
| Compile to ROM | 未実装 | ボタンのみで、コンパイラ・配置・パッチ処理はない。 |
| Cry Editor/特殊PCM | 未実装 | 後段で対応予定。 |

## 4. 現在できている良い部分

### 4.1 ROM非依存の資産再生へ移行できている

WaveDataとWaveMemoryは抽出ファイルから再生され、開いたプロジェクトが元ROMなしでも通常資産を試聴できる方向になっています。元ROMのアドレスはJSON出力から除外されており、将来の再配置コンパイルと相性が良い設計です。

### 4.2 資産の分離方針は妥当

SongTable、SongHeader、VoiceGroup、KeySplit、DrumSet、WaveData、WaveMemoryを別ファイルに分ける方針は、共有、差し替え、手動追加、将来の再配置に向いています。特に共有されるWaveData、KeySplit、DrumSetを重複抽出しない仕組みは維持すべきです。

### 4.3 音源エンジンに回帰テストがある

PCM量子化、固定調DirectSound、PSGチャンネル数、位相、ADSR、Sweep、LFO、Noise、WaveMemoryなどにテストがあります。今後、実機差を修正するときに既存挙動を意図せず壊しにくい状態です。

### 4.4 UIの対象範囲が明確

Figmaを基準に、Mixer、SongTable、SongHeader、VoiceGroup、KeySplit、DrumSet、WaveMemory、Voice、Settings、共通PlayAreaが揃っています。今後は新規画面を増やすより、共通部品化と編集ワークフローの完成を優先できます。

## 5. 重大な問題点

### P0-1. Saveと編集トランザクション [実装進行中]

2026-07-12時点で、Project Session、Dirty tracking、atomic Save、管理資産の削除確定、Undo/Redo、保存チェックポイント、`Ctrl+S/Z/Y`、未保存確認を実装しました。残作業は全編集操作の履歴網羅確認、依存資産の削除検証、クラッシュリカバリです。

当初はSave ProjectとUndo/Redoが未接続で、資産ごとに保存方法も不統一でした。現在は全管理資産をProject Sessionから一括保存し、履歴と保存チェックポイントを同期する基盤へ移行済みです。

残っている事故要因は次の通りです。

- 削除した資産を参照するファイルが残る
- 外部アプリで同時編集された資産を競合確認なしで置換する
- 未保存状態でプロセスが異常終了した場合に復元できない
- 履歴化されていない特殊編集操作が残っている可能性がある

実装方針:

- 全編集をまずメモリ上のProject Sessionへ反映する
- 資産単位とプロジェクト全体のDirty状態を持つ
- Save時にDirty資産を一括検証し、一時ファイルへ書いた後に置換する
- プロジェクト切替、Refresh、終了時に未保存確認を出す
- コマンド単位のUndo/Redoを導入する
- 波形ドラッグは1サンプルごとではなく、1ドラッグを1 Undo単位にする

### P0-2. MIDIとMidi2agbの二系統シーケンス管理が未実装

通常の作曲とサウンドデザインはMIDIで行い、PATT、REPT、MEMACC、XCMDなど高度なMP2K命令を直接扱う場合はMidi2agb assembly sourceを使用します。独自MIDI Meta Eventへ全命令を詰め込む方針は採用しません。

確定方針:

- SongHeaderごとに`MIDI`または`Midi2agb`のSequence Formatを持つ
- MIDIではNote、Tempo、Program、Volume、Pan、BendとカスタムCCを扱う
- Midi2agbではラベル、分岐、パターン、反復、MEMACC、XCMD、raw `.byte`を扱う
- Sequence欄のファイル選択は`.mid`とMidi2agb `.s`の両方を認識する
- フォルダへ手動追加したファイルはRefreshで認識する
- ROM抽出時は`MIDI`、`Midi2agb`、`Both`から既定出力を選択できる
- BothではSongHeaderが再生・コンパイルに使うActive Sequenceを明示する
- Midi2agb sourceは直接イベント列へ解析し、MIDIへ変換しなくても試聴できるようにする
- 未知命令は削除せずraw `.byte`として保持し、Compile時に警告する

コンパイラ内部ではMIDIとMidi2agbを共通の一時IRへ変換して構いませんが、ユーザーが管理する資産は`.mid`または`.s`です。

### P0-3. ROMコンパイル基盤がない

現在はCompile to ROMボタンだけがあり、実処理はありません。編集ツールとして完成させるには、個々のバイナリ化だけでなく、依存関係を含むリンク工程が必要です。

必要な処理:

1. 全資産と参照の検証
2. MIDIまたはMidi2agb/内部Compiler IRからMP2Kトラックへのコンパイル
3. SongHeader、VoiceGroup、KeySplit、DrumSet、WaveData、WaveMemoryのシリアライズ
4. 同一資産の重複排除
5. アラインメント付き配置
6. 仮ポインタのfixup
7. SongTableの再構築
8. 元ROMではなく出力先コピーへの書込み
9. 出力後の再解析検証
10. 配置結果`layout.json`と警告レポートの出力

想定するCompileウィンドウ:

1. 出力元となるROMを選択する
2. AutoまたはManualの配置方法を選択する
3. Autoでは開始アドレスを入力し、アラインメントを守りながら全資産を順番に配置する
4. Manualでは各資産または資産カテゴリの配置アドレスを個別指定する
5. 事前検証と配置Previewを表示する
6. 出力先ROM名を指定してコンパイルする
7. 成功、警告、失敗、使用アドレス、残容量を結果画面に表示する

Manualでも未指定資産をAuto配置できる混合モードを用意すると、大規模プロジェクトで使いやすくなります。

### P0-4. プロジェクト形式の契約が弱い

プロジェクト本体のVersionと各資産のVersionは別形式の番号なので、数字が異なること自体は問題ではありません。問題は、それぞれの対応可能Versionが定義されておらず、LoaderがFormat、Version、Engineを検証していないことです。また、Loader、Exporter、AssetWriterに似たprivate DTOが重複しており、片方だけ変更して形式がずれる危険があります。

改善方針:

- 資産DTOを1か所へ集約する
- `Format`、`Version`、`Engine`を読込時に必ず検証する
- バージョンごとのMigrationを用意する
- 壊れた資産を消えたように扱わず、ファイル名と理由をProject Diagnosticsへ表示する
- JSON Schemaまたは同等の仕様書をDocsへ追加する
- 不要な旧Manifestを削除するか、現行形式へ統合する

Version違いの扱いは次の3段階を推奨します。

- 対応中のVersion: 通常どおり開く
- 古いVersion: 元ファイルをバックアップして自動Migrationする
- 新しい未知Version: 警告後にbest-effortの読み取り専用で開き、上書き保存は禁止する

「警告して無理やり開き、そのまま上書き保存」は未知フィールドを消す危険があります。読み取り専用またはSave Asに限定する方が安全です。

### P0-5. Git基準点の作成は完了、CIは未実装

2026-07-12に主要なAudio、Controls、MIDI、MP2K、ViewModel、テストをコミット`87b5b50`へ追加し、`nemro6/AgbSynth`の`main`へpushしました。Docsの作業ファイル、未使用アイコン、生成物は基準コミットから除外しています。

残作業:

- GitHub ActionsでRelease buildとtestを実行する
- 機能単位で小さくコミットする運用を続ける
- ROM、抽出プロジェクト、ユーザー設定、録音出力を誤って追加しないignore規則を確認する

## 6. 重要な懸念点

### P1-1. クラスとXAMLが巨大化している

主な規模は次の通りです。

- `MainWindowViewModel.cs`: 約4,700行
- `MainWindow.axaml`: 約4,000行
- `AgbAudioEngine.cs`: 約2,800行
- `MainWindow.axaml.cs`: 約1,300行
- `ViewModels/MainWindow/InstrumentPreview.cs`: 約1,200行

MainWindow XAMLには約170個のイベント接続があり、Voice系の表・詳細パネルも似た定義が複数あります。このまま機能を増やすと、1か所の修正で複数ページのレイアウトや保存処理がずれる可能性が高くなります。

改善方針:

- ページをUserControlへ分割する
- Voice編集パネルをVoiceGroup、KeySplit、DrumSetで共有する
- 表の行操作を共通Commandへまとめる
- MainWindowViewModelをProject、Assets、Transport、Mixer、Settingsへ分割する
- AudioEngineをMixer、DirectSound、PSG、Envelope/LFO、Outputへ分割する
- UIコードビハインドのイベントをCommandへ段階的に移す

### P1-2. 再生タイミングがオーディオクロックと独立している

MIDIイベントはStopwatch、Task.Delay、短いSpinWaitで時刻を待ち、音源エンジンへ命令を送っています。以前よりジッタ対策されていますが、OSスケジューラ、UI負荷、GCの影響を受けます。ライブ再生で一定テンポを完全保証する方式ではありません。

これは以前、タイミング処理の変更後にMIDI再生が崩れた問題と同じ領域です。オーディオクロック化そのものが原因ではなく、MIDI tick、Tempo、MP2K update tick、PSG/PCM開始遅延を一度に変更したことが危険でした。現在の再生を維持したまま、イベント投入部だけを段階的に置き換える必要があります。

改善方針:

- MIDIイベントをサンプル位置へ変換する
- Audio callback内のサンプルクロックでイベントを消費する
- テンポ変更も絶対サンプル位置へ積算する
- UIは再生状態を読むだけにし、発音タイミングをDispatcherTimerへ依存させない
- リアルタイム再生とオフラインレンダリングで同じスケジューラを使う
- 変更前後でイベントの予定サンプル位置を比較し、音源式は同時に変更しない

### P1-3. 実機精度を証明する比較基盤がない

音源エンジンには詳細なテストがありますが、多くは実装した式や期待値に対する単体テストです。実機または信頼できるMP2K実装のデジタル出力と、同一入力をサンプル単位で比較する回帰基盤はありません。

改善方針:

- 合成用の小さなMP2KテストROMまたはバイナリfixtureを作る
- m4aSoundMain呼出し単位の期待状態を記録する
- PCM、PSG、LFO、ADSR、Sweep、Reverb、チャンネル奪取を個別fixture化する
- 出力WAVのハッシュだけでなく、最初に差が出るサンプルと最大誤差を表示する
- 実ROMは配布せず、ローカルfixture登録で比較できるようにする

テストは常時大量実行するのではなく、用途別に分けます。現在の115件はReleaseでも約1秒で完了しているため維持します。重い音声比較は音源エンジン変更時または明示実行時だけにし、通常のUI作業では高速単体テストだけを使います。

### P1-4. 資産の識別がパスと走査順に依存している

KeySplit、DrumSet、WaveData、WaveMemoryのIDはフォルダ列挙順から再採番される部分があります。参照の中心は相対パスなので通常は動きますが、手動rename、同名資産、別フォルダへの移動でリンクが壊れます。

推奨案:

- フォルダ走査方式は維持する
- 各JSON資産へ永続的な`AssetId`を持たせる
- 参照は`AssetId`を正本、相対パスを場所のヒントとして持つ
- binaryのWaveMemoryには同名sidecarまたは小さなヘッダ付き形式を使う
- Refresh時に移動したAssetIdを再発見してリンクを修復する
- UIからファイル名を変更できるRename操作を追加し、参照パスも同一トランザクションで更新する
- OS側で手動renameされた場合も、AssetIdが一致すればRefresh時に再リンクする

これなら「並び順が不要な資産はリストファイルを作らずフォルダを走査する」という現在の方針を維持できます。

### P1-5. 依存資産の削除・変更検証が不足している

VoiceGroupがKeySplit、DrumSet、WaveData、WaveMemoryを参照し、SongHeaderがVoiceGroupとMIDIを参照します。しかし削除時に参照元を一覧表示したり、孤立資産を検出したりする仕組みがありません。

改善方針:

- Project Asset Graphを構築する
- 削除前に参照元を表示する
- `Delete only`、`Replace references`、`Delete unused children`を選べるようにする
- 未参照資産、欠落資産、循環参照、不正TypeをDiagnosticsで検出する

これはROMコンパイル前に必須です。まず削除時の参照警告だけを実装し、AssetGraph完成後に置換・連鎖削除へ拡張します。

### P1-6. ROM自動検出は一般化されていない

Auto検出には既知ゲームコードの固定SongTable位置とヒューリスティックがあります。最大128個の無効スロットを許容することで空白区間には対応していますが、複数エンジン、改造ROM、異なるPlayerTable、偶然似たデータへの耐性は限定的です。

確定方針:

- XML profileをGameCode、Revision、必要に応じてCRCで照合し、既知ROMでは最優先する
- XMLのアドレスが有効か、SongTable、PlayerTable、SoundModeを実ROM上で再検証する
- XMLがない、CRCが違う、指定位置が壊れている場合は現在のヒューリスティック検出へfallbackする
- 改造ROMではスコア付き候補一覧と検出根拠を表示する
- 手動アドレス指定は常に残す

### P1-7. 録音が長時間利用向けではない

録音サンプルは停止までメモリへ蓄積し、停止後に32-bit float WAVとして一括保存します。短時間の確認には十分ですが、長時間録音ではメモリを消費します。

改善方針:

- 一時WAVへストリーミングする
- 保存時にfloat WAVと16-bit PCM WAVを選択可能にする
- 録音時間、推定ファイルサイズ、クリップを表示する
- 保存キャンセル時に再保存する選択肢を出す

現状の利用想定では優先度を下げ、実際に長時間録音が必要になった時点で対応します。

## 7. 中程度の問題と改善案

### P2-1. エラーが見えない

Loaderや設定保存には例外を無言で無視する箇所があります。アプリが落ちない点は良いものの、ファイルが壊れたのか、形式が違うのか、権限がないのか判別できません。

推奨案:

- 通常の情報や回復可能な問題ではダイアログを出さず、StatusとDiagnosticsへ表示する
- データ破損、保存失敗、コンパイル失敗、復旧不能な音声初期化失敗のみポップアップにする
- 現在混在している不要な情報ダイアログを削除する
- Severity、File、Asset、Message、Suggested Fixを持つ
- ログファイルを`AppData/AgbSynth/logs`へ保存する
- ユーザー向け警告と開発用スタックトレースを分ける

### P2-2. プラットフォーム方針が曖昧

UIはAvaloniaですが、音声・MIDIはNAudioとwinmmに依存しており、実質Windows向けです。これは問題ではありませんが、READMEとビルド設定でWindows専用と明示するか、将来クロスプラットフォーム化するかを決める必要があります。

将来のmacOS対応を前提にします。MP2K合成、PSG、PCM、Mixer、録音処理は純粋なC#側へ維持し、OS依存部分だけを`IAudioOutputBackend`と`IMidiInputBackend`へ分離します。Windowsでは現在のNAudio出力をそのまま使い、macOSでは別backendへ差し替えます。オフラインレンダリング結果を共通テストにすれば、Mac対応で現在の音を崩さずに済みます。

### P2-3. READMEとOverviewが実装に追いついていない

READMEは初期段階の説明のままで、現在のUI、再生機能、資産形式、録音、設定、既知制限が反映されていません。Overviewにも旧`project.json`例や現在と異なるVersion記述があります。

推奨案:

- READMEは利用者向けに更新する
- Overviewは目標設計に限定する
- この文書は進捗に合わせて更新する
- `ProjectFormat.md`、`PlaybackAccuracy.md`、`BuildFormat.md`へ仕様を分離する

### P2-4. UIの自動テストがない

現在のテストはParser、Exporter、MIDI、Audio中心です。表の編集、選択維持、ページ切替、Save、Refresh、テーマ切替、ファイル選択は手動確認に依存しています。

推奨案:

- ViewModel操作をUIから分離し、保存や参照更新などデータ損失に直結する部分だけ単体テストを追加する
- 全画面の見た目を自動テストする方針にはしない
- New/Open/Edit/Save/Reloadの短いsmoke testを優先する
- レイアウトとテーマは基本的に手動確認とする

### P2-5. 設定保存のI/O頻度

Slider変更などで設定JSONを繰り返し読み書きします。現状規模では大きな問題ではありませんが、将来は短いdebounceを入れ、終了時にも確実にflushする方が安全です。

Settingsを整理する段階で300～500 ms程度のdebounceを入れます。音への反映は即時のまま、ディスク保存だけをまとめます。

## 8. 推奨アーキテクチャ

現段階で全面的な作り直しは不要です。既存コードを動かしながら、次の境界へ段階的に分離するのが現実的です。

```text
AgbSynth.App
  Avalonia View / ViewModel / dialogs
       |
AgbSynth.Project
  ProjectSession / AssetGraph / Save / Undo / Diagnostics
       |
AgbSynth.Mp2k
  ROM discovery / parser / MIDI command model / compiler IR / linker
       |
AgbSynth.Audio
  event scheduler / MP2K mixer / PSG / output / offline render
       |
AgbSynth.Core
  asset models / IDs / validation / shared primitives
```

最初から別assemblyへ分ける必要はありません。まず現在のプロジェクト内でnamespaceとクラス責務を分離し、安定した段階でclass libraryへ移す方法が安全です。

### 8.1 ProjectSession

ProjectSessionを編集状態の唯一の入口にします。

- CurrentProjectPath
- AssetRegistry
- DirtyAssets
- UndoStack / RedoStack
- Diagnostics
- Save / Reload / Close

ViewModelから直接`File.WriteAllText`を呼ばず、ProjectSession経由で保存します。

### 8.2 AssetGraph

資産間リンクを文字列比較だけに任せず、読込時にグラフ化します。

```text
SongTable entry -> SongHeader -> Sequence
                            -> VoiceGroup
VoiceGroup -> WaveData
           -> WaveMemory
           -> KeySplit -> child voices
           -> DrumSet -> child voices
```

このグラフをSave、Delete、Compile、未使用資産検出、依存表示で共用します。

### 8.3 MIDI/Midi2agbと内部Compiler IR

SongHeaderはSequence FormatとSequence Fileを持ち、通常はMIDI、高度なMP2K操作ではMidi2agb `.s`を参照します。再生・検証・MP2Kコンパイル時だけ共通IRへ変換し、内部IR自体は別資産として保存しません。

内部IRは最低限、次の情報を保持します。

- Trackとイベント順
- MP2K tick
- Note/Wait/Controller/Tempo
- LabelとJump target
- Pattern call/return
- Repeat
- Tie/EOT
- XCMDとMEMACC
- 解析不能命令のraw bytes
- Midi2agb label、directive、macro、raw `.byte`
- MIDI/Midi2agb ImportおよびCompile時のloss report

### 8.4 Build Pipeline

Build処理は直接ROMを書き換える1メソッドにせず、中間成果物を作ります。

```text
Validate -> Compile assets -> Deduplicate -> Layout -> Fixup -> Emit -> Reparse verify
```

各段階の結果を保存できれば、アドレスずれや壊れたポインタを調査しやすくなります。

## 9. 推奨実装ロードマップ

### Phase 0: 現在地点の保全

目的: 今の動作を失わず、安全に改修できる状態にする。

進捗（2026-07-12）:

- `MainWindowViewModel.cs`に混在していた行モデルと資産選択モデルを`Rows`、`Assets`、`Common`、`Options`へ分離
- MainWindowのpartial ViewModelを`ViewModels/MainWindow`へ集約し、役割名へ変更
- Settingsページを独立UserControlへ分離
- 分離後のRelease buildと115件のテストが成功
- 主要ソースとテストをコミット`87b5b50`として`nemro6/AgbSynth`の`main`へpush

実装内容:

- MainWindow、ViewModel、行モデル、音源エンジンを責務別フォルダとファイルへ分割
- 重複するVoice編集UIを共通UserControl化
- 未追跡の主要ソースとテストをGit管理へ追加
- `.gitignore`整理
- READMEの最低限更新
- GitHub ActionsでRelease build/test
- 既知の手動確認曲と操作をチェックリスト化

完了条件:

- clean cloneからbuild/testできる
- 115件以上の現行テストがCIで成功する
- 現在のプロジェクトを開き、代表曲と各音源を再生できる
- 構造整理の前後で公開型、binding、再生結果が変わらない

### Phase 1: Save、Dirty、Undo/Redo

進捗: Project Session、Dirty、atomic Save、Undo/Redo、未保存確認、基本smoke testは実装済み。依存検証とクラッシュリカバリは未実装。

目的: AgbSynthを安全な編集ツールにする。

実装内容:

- ProjectSessionとDirty tracking
- Save Projectボタン実装
- atomic saveと保存失敗時のrollback
- 終了、Open、Refresh時の未保存確認
- 行操作、フィールド編集、波形編集のUndo/Redo
- 削除前の参照チェック

完了条件:

- 全ページの変更をSave後に再読込して一致する
- Save前のファイルは変更されない
- Undo/Redo後にUI、試聴、保存内容が一致する
- 強制的に保存失敗させても元ファイルが壊れない

### Phase 2: プロジェクト形式の確定

目的: 新規作成、手動追加、将来Migrationに耐える形式にする。

実装内容:

- 共通資産envelopeとDTO
- AssetId導入
- Format/Version/Engine検証
- Version migration
- Project Diagnostics
- Project Format仕様書
- WaveMemory metadataの正式化

完了条件:

- 資産rename後もAssetIdで参照を復旧できる
- 壊れた資産をファイル名付きで報告できる
- 旧プロジェクトをmigrationして読める
- Load -> Save -> Loadで意味的に同一になる

### Phase 3: audio clockと再生回帰基盤

目的: 現在の音源挙動を変えず、MIDIイベントをサンプル位置基準で安定して投入する。

実装内容:

- 現行Stopwatch schedulerの予定時刻をfixture化
- MIDI tickとTempo mapから絶対サンプル位置を作るtimeline
- AudioEngineへ先行投入するevent queue
- イベント処理とUI更新の分離
- リアルタイムとoffline renderの共通化
- 既存のPSG/PCM開始遅延とトラック別loopを維持する回帰テスト

完了条件:

- UI負荷を掛けてもイベントのサンプル位置が変わらない
- 既存代表曲のイベント順、Tempo、loop位置が変更前と一致する
- 同じ入力のoffline renderが毎回同一になる

### Phase 4: MIDI/Midi2agb相互運用

目的: 通常のMIDI制作と高度なMidi2agb編集をSongHeaderごとに選択できるようにする。

実装内容:

- MP2KトラックparserをMIDI exporterから分離
- Sequence FormatとActive Sequence参照
- ROM抽出時の`MIDI / Midi2agb / Both`選択
- Midi2agb `.s` parser、writer、再生用IR変換
- MIDI -> Compiler IR変換
- トラック別loop保持
- 重複フレーズのREPT/PATT候補化
- unsupported/loss report

完了条件:

- ROM -> Midi2agb -> MP2Kで未編集データがbyte-identicalまたは意味的同一になる
- MIDIとMidi2agbのどちらからでも同じ再生pipelineを使用できる
- Tempo、Tie、Loop、Pattern、MEMACC、XCMDのfixtureが通る

### Phase 5: MP2KコンパイラとROM出力

目的: 編集結果を安全にROMへ戻す。

実装内容:

- 各資産serializer
- MIDI/Midi2agb共通Sequence compiler
- alignment rule
- free-space allocatorと明示アドレス配置
- pointer fixup/linker
- Auto/Manual/混合配置に対応するCompileウィンドウ
- 出力ROM作成
- layout.jsonとbuild report
- 出力ROM再解析検証

完了条件:

- 合成fixture ROMへ全資産を書き戻せる
- 出力を再抽出して主要フィールドが一致する
- 元ROMを上書きしない
- 容量不足、参照切れ、範囲外ポインタをビルド前に検出する

### Phase 6: UI分割と操作品質

目的: 機能追加でUIが崩れにくい構造にする。

実装内容:

- 各ページのUserControl化
- Voice detail panel共通化
- 表ツールバー、行メニュー、ファイル選択部品の共通化
- Dark/Light token整理
- keyboard shortcut
- Headless UI test

完了条件:

- 同じボタン・表の見た目と挙動が1つのstyle/componentから決まる
- 主要操作がマウスとキーボードの両方で可能
- Dark/Light両方のsmoke testが通る

### Phase 7: 特殊PCM、Cry Editor、配布準備

目的: 対象データを拡張し、一般利用できる形にする。

実装内容:

- 特殊PCM/DPCM調査と実装
- Cry Editor
- WAV import/export
- midi2agb import/export
- installer/release package
- README、操作ガイド、既知制限

## 10. テスト戦略

| テスト層 | 追加すべき内容 |
| --- | --- |
| Model/Format | 全資産のserialize/deserialize、Version migration、壊れたJSON、未知フィールド |
| Project | New/Open/Refresh/Save、rename、欠落参照、共有資産、削除、atomic save |
| Parser | 空SongTable区間、複数候補、未知Voice type、不正ポインタ、境界サイズ |
| Sequence | 全command、running status、nested PATT、REPT、複数loop、MEMACC、XCMD |
| Compiler | parse-compile-parse、byte-identical、alignment、pointer fixup、容量不足 |
| Audio | sample単位fixture、全PSG、PCM、LFO、ADSR、Reverb、priority、overflow |
| Scheduler | tempo change、track loop、長時間再生、pause/resume、UI高負荷時 |
| UI | 表編集、選択維持、テーマ、Save確認、ショートカット、ファイル選択 |
| Recording | 無音、Stereo/Mono、長時間、キャンセル、16-bit/float出力 |

著作権のあるROMや音声をリポジトリへ含めず、合成fixtureとローカル参照データを分けるべきです。

## 11. 追加すると有用な機能案

### 11.1 Project Diagnostics

常時実行可能なプロジェクト検査です。

- 欠落リンク
- 未使用資産
- 重複AssetId
- 128個でないVoiceGroup/DrumSet
- 不正KeySplit range
- サンプルloop範囲外
- MIDIにProgram Changeがないトラック
- コンパイル不能なcommand

### 11.2 Dependency Inspector

選択中資産について「どこから参照され、何を参照しているか」を表示します。共有KeySplitやWaveDataを安全に編集・削除しやすくなります。

### 11.3 Build Preview

ROMへ書く前に、資産名、サイズ、アラインメント、予定アドレス、空き容量を一覧表示します。配置順を固定できれば、同じプロジェクトから再現可能なROMを生成できます。

### 11.4 Unsupported Command Inspector

解析できないMP2K命令を無視せず、Song/Track/Tick/ROM位置/raw bytesで一覧化します。対応優先度を実データから決められます。

### 11.5 Compare Render

同じ曲を「Hardware Accurate」と「Clean」の両方でオフライン出力し、切替または差分再生できる機能です。現在あるMP2K PCMモードの違いを確認しやすくなります。

### 11.6 Crash Recovery

通常Saveとは別に、未保存編集をAppDataへ定期snapshotします。資産ファイルを勝手に上書きせず、異常終了後だけ復元候補を提示します。

### 11.7 Batch Export

選択または全曲のMIDI/WAV、全WaveDataのWAV、プロジェクト診断結果を一括出力します。ROM調査用途にも有用です。

### 11.8 Reference Capture Tool

実機またはエミュレータ比較用に、短いテストシーケンスと期待条件を生成する補助機能です。音源精度の改善を感覚だけで判断せずに済みます。

## 12. 当面の非目標

- GBA本体スピーカー、アンプ、イヤホン端子、録音環境による劣化の再現
- 全MP2K派生ドライバへの即時対応
- 著作権のあるROMデータやゲーム固有資産の同梱
- 特殊PCM/Cry Editorを通常PCMより先に完成させること
- 初期段階からの完全なクロスプラットフォーム対応

## 13. 次に着手する推奨順序

直近は次の順序が最も安全です。

1. 現在の全主要実装をGit管理し、GitHubへ基準点をpushする。
2. 挙動を変えない範囲でMainWindow、ViewModel、共通UIの構造整理を完了する。
3. Save、Dirty、未保存確認、Undo/Redoを完成させる。
4. Project Formatを整理し、AssetId、Rename、依存検証、Diagnosticsを導入する。
5. 再生イベントをaudio clockへ移し、現在の再生結果を維持する。
6. SongHeader単位のMIDI/Midi2agb切替と相互Import/Exportを実装する。
7. Compiler/LinkerとAuto/Manual配置ウィンドウを完成させる。
8. 実機比較、UI共通化、特殊PCM、Cry Editor、配布機能へ進む。

現状では、新しい編集ページを増やすよりも、Phase 0からPhase 3を先に完成させる方が結果的に開発速度が上がります。特にSaveとMIDI/Midi2agbのSequence Format契約がないままROMコンパイルへ進むと、後でプロジェクト形式と全編集画面を修正する可能性が高くなります。

## 14. 定期的に確認する指標

- Release build/testが成功しているか
- 未追跡の主要ソースがないか
- 保存後の再読込で差分がないか
- 壊れた・欠落した資産が無言で消えていないか
- 1つのクラスまたはXAMLへ責務が再集中していないか
- 実機差を感覚だけでなくfixture差分で説明できるか
- ROM出力が元ROMを変更せず、再解析検証を通るか
- 新機能がMIDI/Midi2agb共通IRとCompilerの両方へ接続されているか
