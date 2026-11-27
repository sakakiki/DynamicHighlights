// =====================================================================
// ExperimentLogger.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   実験データを CSV ファイルに保存するスクリプトです。
//   CSV は「カンマ区切りの表」で、Excel や Python（pandas）でそのまま開けます。
//
// 【保存するファイル（1人の被験者につき）】
//   ① フレームごとのログ（試行ごとに1ファイル）
//        例：P01_S3_T2_Beyond_R_DynamicControl_20261127_153000_frames.csv
//            （被験者_セット番号_セット内の試行番号_パターン_向き_条件_日時）
//        1行 = 1フレーム。r, v, ω, s などの値が時系列で並びます。
//        → r-v グラフや、ω の変化のグラフを描くのに使います。
//
//   ② 試行のまとめ（被験者ごとに1ファイル、試行のたびに行が追加される）
//        例：P01_summary.csv
//        1行 = 1試行 × 1オブジェクト（その試行で表示したもの）。
//        セット・パターン・向き・角度、到達時間・注視時間、パラメータが入ります。
//        → 条件ごとの比較（統計）に使います。
//
// 【保存場所】
//   Unity エディタで実行した場合：プロジェクトのフォルダ直下の ExperimentData/
//   （例：C:\Users\…\DynamicHighlights\ExperimentData\）
//   ※ 被験者のデータなので Git には入らないように .gitignore に登録しています。
//
// 【他のファイルとの関係】
//   ExperimentFlow.cs が、試行の開始時に BeginTrial()、毎フレーム LogFrame()、
//   終了時に EndTrial() を呼びます。値は GazeDirectionController から読みます。
// =====================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DynamicHighlights
{
    /// <summary>
    /// 実験データを CSV ファイルに書き出すコンポーネントです。
    /// </summary>
    public class ExperimentLogger : MonoBehaviour
    {
        [Tooltip("値を読み取る視線方向制御のコンポーネント")]
        public GazeDirectionController controller;

        [Tooltip("保存先のフォルダ名")]
        public string outputFolderName = "ExperimentData";

        /// <summary>いまフレームログを書いている途中なら true</summary>
        public bool IsLogging => _frameWriter != null;

        /// <summary>最後に書き出したフレームログのファイルの場所（画面表示用）</summary>
        public string LastFramesFilePath { get; private set; } = "";

        // ファイルに文字を書き込むための道具
        private StreamWriter _frameWriter;

        // 試行の情報（まとめファイルに書くために覚えておく）
        private string _participantId;

        // 数値を書くときの書式。InvariantCulture を使うと、PC の言語設定に関係なく
        // 小数点が必ず「.」になります（国によっては「,」になってしまい CSV が壊れるため）。
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // UTF-8（BOM付き）で保存すると、Excel で開いても日本語が文字化けしません。
        private static readonly Encoding CsvEncoding = new UTF8Encoding(true);

        /// <summary>
        /// 保存先フォルダの場所を返します。
        /// </summary>
        public string OutputDirectory
        {
            get
            {
                // Application.dataPath は「プロジェクト/Assets」フォルダの場所。
                // その1つ上（プロジェクトのフォルダ）に保存します。
                // （ビルドしたアプリの場合は、書き込みが許可されている persistentDataPath を使う）
                string baseDirectory = Application.isEditor
                    ? Path.GetDirectoryName(Application.dataPath)
                    : Application.persistentDataPath;
                return Path.Combine(baseDirectory ?? ".", outputFolderName);
            }
        }

        /// <summary>
        /// 試行の開始時に呼びます。フレームログのファイルを新しく作ります。
        /// </summary>
        /// <param name="participantId">被験者ID（例：P01）</param>
        /// <param name="trial">この試行の計画（セット・パターン・条件・向きなど）</param>
        public void BeginTrial(string participantId, PlannedTrial trial)
        {
            EndFrameLog(); // 前のファイルが開いたままなら閉じる

            _participantId = participantId;

            Directory.CreateDirectory(OutputDirectory); // フォルダが無ければ作る

            // ファイル名：被験者ID_Sセット番号_Tセット内の試行番号_パターン_向き_条件名_日時_frames.csv
            string label = trial.IsPractice ? "Practice" : trial.Condition.ToString();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Invariant);
            string fileName = $"{Sanitize(participantId)}_S{trial.SetNumber}_T{trial.TrialInSet}_" +
                              $"{PatternCode(trial.Pattern)}_{ExperimentPlan.SideCode(trial.Side)}_{label}_{timestamp}_frames.csv";
            LastFramesFilePath = Path.Combine(OutputDirectory, fileName);

            _frameWriter = new StreamWriter(LastFramesFilePath, false, CsvEncoding);

            // 1行目：列の名前（ヘッダー）
            _frameWriter.WriteLine(
                "frame,trial_time_s,r_deg,v_deg,offset_deg,omega_deg_per_s,s,c,pitch_deg,gaze_target");
        }

        /// <summary>ファイル名に使う、パターンの短い名前</summary>
        private static string PatternCode(ViewingPattern pattern)
        {
            switch (pattern)
            {
                case ViewingPattern.BeforeHighlight: return "Before";
                case ViewingPattern.BeyondHighlight: return "Beyond";
                default: return "Highlight";
            }
        }

        /// <summary>
        /// 1フレーム分のデータを書き込みます（試行中、毎フレーム呼ぶ）。
        /// </summary>
        /// <param name="trialTime">試行開始からの時間 [秒]</param>
        /// <param name="gazeTargetId">いま見ているオブジェクトの名前（見ていなければ空文字）</param>
        public void LogFrame(float trialTime, string gazeTargetId)
        {
            if (_frameWriter == null || controller == null) return;

            // string.Format で、値をカンマ区切りの1行にまとめます。{0} {1} … に順番に値が入ります。
            // F3 は「小数点以下3桁」、F4 は「4桁」という書式です。
            _frameWriter.WriteLine(string.Format(Invariant,
                "{0},{1:F4},{2:F3},{3:F3},{4:F3},{5:F3},{6:F4},{7:F4},{8:F3},{9}",
                Time.frameCount,
                trialTime,
                controller.RealYaw,
                controller.VirtualYaw,
                controller.Offset,
                controller.Omega,
                controller.Slope,
                controller.ControlStrength,
                controller.Pitch,
                gazeTargetId));
        }

        /// <summary>
        /// 試行の終了時に呼びます。フレームログを閉じ、まとめファイルに結果を追記します。
        /// </summary>
        /// <param name="trial">終わった試行の計画</param>
        /// <param name="targets">その試行で表示していたオブジェクトの一覧</param>
        /// <param name="trialDuration">試行の長さ [秒]</param>
        /// <param name="config">この試行で使ったパラメータ</param>
        public void EndTrial(PlannedTrial trial, IEnumerable<TargetObject> targets, float trialDuration, ExperimentConfig config)
        {
            string framesFileName = Path.GetFileName(LastFramesFilePath);
            EndFrameLog();

            Directory.CreateDirectory(OutputDirectory);
            string summaryPath = Path.Combine(OutputDirectory, $"{Sanitize(_participantId)}_summary.csv");
            bool isNewFile = !File.Exists(summaryPath);

            // true を渡すと「追記モード」になり、前の試行の結果を消さずに下に書き足します。
            using (var writer = new StreamWriter(summaryPath, true, CsvEncoding))
            {
                if (isNewFile)
                {
                    writer.WriteLine(
                        "participant,trial,set,trial_in_set,is_practice,pattern,condition,side," +
                        "highlight_yaw_deg,object_yaw_deg,target_id,is_intended,label,first_reach_s,dwell_s," +
                        "trial_duration_s,w_deg,s0,omega0_deg_per_s,N,bezier_L_deg,frames_file");
                }

                // 鑑賞対象（青い箱）を置かない「見どころの鑑賞」パターンでは、object_yaw_deg は空欄にする
                string objectYaw = trial.HasObject ? trial.ObjectYaw.ToString("F1", Invariant) : "";

                foreach (TargetObject target in targets)
                {
                    bool isIntended = target.targetId == trial.IntendedTargetId; // この試行で見てもらう対象か
                    writer.WriteLine(string.Format(Invariant,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8:F1},{9},{10},{11},{12},{13:F3},{14:F3},{15:F3}," +
                        "{16},{17},{18},{19},{20},{21}",
                        _participantId,
                        trial.Number,
                        trial.SetNumber,
                        trial.TrialInSet,
                        trial.IsPractice ? 1 : 0,
                        trial.Pattern,
                        trial.Condition,
                        ExperimentPlan.SideCode(trial.Side),
                        trial.HighlightYaw,
                        objectYaw,
                        target.targetId,
                        isIntended ? 1 : 0,
                        target.CurrentLabel,
                        target.FirstReachTime,
                        target.DwellTime,
                        trialDuration,
                        config.W,
                        config.S0,
                        config.Omega0,
                        config.velocityFrameCount,
                        config.L,
                        framesFileName));
                }
            }
        }

        /// <summary>
        /// フレームログのファイルを閉じます。
        /// </summary>
        private void EndFrameLog()
        {
            if (_frameWriter == null) return;
            _frameWriter.Flush(); // まだ書き込まれていないデータを全部ファイルに書き出す
            _frameWriter.Dispose(); // ファイルを閉じる
            _frameWriter = null;
        }

        /// <summary>
        /// アプリ終了時やオブジェクト削除時に、開いたままのファイルを確実に閉じます。
        /// （閉じないとデータが失われることがあります）
        /// </summary>
        private void OnDisable()
        {
            EndFrameLog();
        }

        /// <summary>
        /// ファイル名に使えない文字（\ / : * ? " &lt; &gt; | など）を「_」に置き換えます。
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(c, '_');
            }
            return text.Trim();
        }
    }
}
