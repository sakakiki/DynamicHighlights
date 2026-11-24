# 方式B（増分ゲイン方式）アーカイブ

このフォルダには、**使わないことにした「方式B（増分ゲイン方式）」** のコードと解説を保管しています。
`Assets/` の外にあるので、Unity はこのフォルダの `.cs` ファイルを読み込みません（コンパイルもされません）。

## 廃止した理由（2026-11-24）

教授からのフィードバック：

> 方式Aは急停止による不具合の発生の可能性はあるが、今回の研究の焦点は「見どころ制御の動的変化」であって「完全に実用的なシステムの構築」ではない。口頭注意程度で済むなら方式Aでいい。

この判断により、動的制御は **方式A（絶対写像：スライドの式どおり v = F(r; s(ω))）だけ** を使うことにしました。
方式A で頭を急に止めると視界が少し戻る（最大 10° 程度）という既知の挙動については、実験の前に被験者へ口頭で伝えます（`Docs/使い方ガイド.md` の説明文の例を参照）。

## このフォルダの中身

| ファイル | 内容 |
|---|---|
| `方式B_解説.md` | 方式B の考え方と数式の詳しい解説（廃止前に作った説明） |
| `当時のコード/HighlightMapping.cs` | 方式B の計算（`StepIncremental`、`FindSegment`、`GainAt`）が入っていた版 |
| `当時のコード/GazeDirectionController.cs` | 方式A/B を切り替えていた版（`MethodOverride`、`CurrentMethod`、Δr の計算） |
| `当時のコード/ExperimentConfig.cs` | 方式を選ぶ `DynamicMethod` 列挙型と `dynamicMethod` 設定があった版 |
| `当時のコード/IncrementalModeTests.cs` | 方式B の自動テスト（EditMode）。テスト一覧からは外しました |

※ `当時のコード/` は「廃止した時点のファイルをそのまま写したもの」です。いまの `Assets/Scripts/` のファイルとは中身が違います。

## 本体から削除したもの

| 場所 | 削除した内容 |
|---|---|
| `Core/HighlightMapping.cs` | `StepIncremental`、`FindSegment`、`GainAt` |
| `ExperimentConfig.cs` | `DynamicMethod` 列挙型、`dynamicMethod` 設定 |
| `GazeDirectionController.cs` | `MethodOverride`、`CurrentMethod`、前回の r（Δr）の記録、方式の分岐 |
| `ExperimentFlow.cs` | `DescribeMethod`、画面の「方式」表示、試行開始時の `MethodOverride` リセット |
| `ExperimentLogger.cs` | まとめCSV の `dynamic_method` 列 |
| `Debug/DebugHud.cs` | ゲイン g の表示 |
| `Editor/CameraCheckWindow.cs` | 「動的・方式B」ボタン（「動的制御」ボタン1つに統合） |
| `Tests/PlayMode/ExperimentSceneSmokeTests.cs` | 「方式B で止まっていれば v は動かない」の確認 |
| `Tests/EditMode/IncrementalModeTests.cs` | ファイルごとこのフォルダへ移動 |

## 元に戻したいとき

方式B が入っていた最後のコミットは **`214d0eb`（挙動テストの一時停止機能修正）** です。
上の表のファイルは、次のコマンドで当時の版を確認できます。

```powershell
git show 214d0eb:Assets/Scripts/Core/HighlightMapping.cs
```

ただし、その後の変更（方式B の削除以外の修正）も消えてしまうので、ファイルを丸ごと戻すのではなく、
必要な部分だけを `当時のコード/` から書き写してください。
