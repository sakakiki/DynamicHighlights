// =====================================================================
// MappingGraphExporter.cs（エディタ拡張）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   メニュー「DynamicHighlights → r-v-ω グラフ用CSVを出力」を押すと、
//   いろいろな r と ω の組み合わせについて v を計算し、表（CSV）として保存します。
//
// 【何に使うか】
//   先生のコメント「グラフは r-v-ω の3次元グラフになる。3次元グラフや v-ω グラフを作ってもいい」
//   に対応するためのデータです。Excel や Python でグラフを描けます。
//   （グラフの描き方は Docs/使い方ガイド.md を参照）
//
// 【出力される列】
//   omega_deg_per_s … 角速度 ω [度/秒]
//   s               … その ω のときの傾き s（スライド7の式）
//   c               … 制御強度 c = (1 − s)/(1 − s₀)
//   r_deg           … 実視線方向 r [度]
//   v_deg           … 仮想視線方向 v [度]（方式A の v = F(r; s(ω))）
//
//   ※ 方式B は「頭の動かし方の履歴」で v が変わるので、このような表にはできません。
//     方式B の様子を見たいときは、実験のフレームログ（*_frames.csv）を使います。
// =====================================================================

using System.Globalization;
using System.IO;
using System.Text;
using DynamicHighlights.Core;
using UnityEditor;
using UnityEngine;

namespace DynamicHighlights.EditorTools
{
    /// <summary>
    /// r-v-ω グラフ用の表を CSV に書き出すエディタ拡張です。
    /// </summary>
    public static class MappingGraphExporter
    {
        [MenuItem("DynamicHighlights/r-v-ω グラフ用CSVを出力", priority = 20)]
        public static void Export()
        {
            // ① 使う設定ファイルを決める（Project で選択中のものがあればそれ、無ければ最初に見つかったもの）
            var config = Selection.activeObject as ExperimentConfig;
            if (config == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:ExperimentConfig");
                if (guids.Length > 0)
                {
                    config = AssetDatabase.LoadAssetAtPath<ExperimentConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (config == null)
            {
                EditorUtility.DisplayDialog("CSV出力", "ExperimentConfig が見つかりません。先に「実験シーンを作成」を実行してください。", "OK");
                return;
            }

            // ② 保存先
            string projectFolder = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string folder = Path.Combine(projectFolder, "ExperimentData");
            Directory.CreateDirectory(folder);
            string fileName = string.Format(CultureInfo.InvariantCulture,
                "mapping_grid_phi{0}_w{1}_s0{2}_omega0{3}.csv", config.Phi, config.W, config.S0, config.Omega0);
            string path = Path.Combine(folder, fileName);

            // ③ 表を作る
            //    ω：0 から ω₀ の 1.5 倍まで、ω₀ の 1/18 刻み（ω₀=90 なら 5°/s 刻み）
            //    r：左右対称なら -90〜90、そうでなければ 0〜90 を 1° 刻み
            var builder = new StringBuilder();
            builder.AppendLine("omega_deg_per_s,s,c,r_deg,v_deg");

            float omegaStep = config.Omega0 / 18f;
            int rStart = config.symmetric ? -90 : 0;
            for (int i = 0; i <= 27; i++)
            {
                float omega = i * omegaStep;
                float s = HighlightMapping.SlopeFromOmega(omega, config.Omega0, config.S0);
                float c = HighlightMapping.ControlStrength(s, config.S0);
                for (int r = rStart; r <= 90; r++)
                {
                    float v = HighlightMapping.MapAbsolute(r, config.Phi, config.W, s, config.symmetric);
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F2},{1:F4},{2:F4},{3},{4:F4}", omega, s, c, r, v));
                }
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            Debug.Log($"[MappingGraphExporter] 出力しました：{path}");
            EditorUtility.RevealInFinder(path); // エクスプローラーでファイルの場所を開く
        }

        /// <summary>
        /// 実験データのフォルダをエクスプローラーで開きます。
        /// </summary>
        [MenuItem("DynamicHighlights/実験データのフォルダを開く", priority = 21)]
        public static void OpenDataFolder()
        {
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ExperimentData");
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }
    }
}
