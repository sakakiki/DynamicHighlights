// =====================================================================
// ExperimentConfig.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   実験のパラメータ（φ, w, s₀, ω₀, N など）をまとめて保存しておく「設定ファイル」です。
//
// 【ScriptableObject（スクリプタブルオブジェクト）とは】
//   Unity で「データだけを入れておくファイル（アセット）」を作るための仕組みです。
//   普通のスクリプト（MonoBehaviour）はシーン内の GameObject に付けて使いますが、
//   ScriptableObject は Project ウィンドウに .asset ファイルとして保存されます。
//   → 「パラメータを変えるときはこのファイルだけ触ればよい」状態にできます。
//   → 設定違いのファイル（例：s₀=0.5 版と s₀=0.7 版）を複数作って切り替えることもできます。
//
//   作り方：Project ウィンドウで右クリック → Create → DynamicHighlights → 実験設定
//   （メニュー「DynamicHighlights → 実験シーンを作成」を実行すると自動で1つ作られます）
//
// 【他のファイルとの関係】
//   GazeDirectionController / ExperimentFlow / ExperimentLogger / DebugHud が
//   このファイルの値を読み取って動きます。
// =====================================================================

using DynamicHighlights.Core;
using UnityEngine;

namespace DynamicHighlights
{
    /// <summary>
    /// 視聴条件（実験で比較する3つの条件）です。
    /// <para>enum（列挙型）は「決まった選択肢のどれか」を表す型です。</para>
    /// </summary>
    public enum ViewingCondition
    {
        /// <summary>通常のVR視聴（視線方向制御なし。v = r）</summary>
        NoControl = 0,

        /// <summary>制御の強さが一定（元にした研究のシステム。s = s₀ で固定）</summary>
        FixedControl = 1,

        /// <summary>制御の強さが角速度で変わる（本研究で新しく作るシステム）</summary>
        DynamicControl = 2,
    }

    /// <summary>
    /// 実験のパラメータをまとめた設定ファイル（ScriptableObject）です。
    /// <para>
    /// [Header] は Inspector に見出しを表示し、[Tooltip] はマウスを乗せたときに説明を表示します。
    /// [Range(最小, 最大)] を付けると Inspector にスライダーが出ます。
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "ExperimentConfig", menuName = "DynamicHighlights/実験設定 (ExperimentConfig)")]
    public class ExperimentConfig : ScriptableObject
    {
        // -----------------------------------------------------------------
        [Header("■ 見どころの設定（元研究の論文 2.2〜2.3節。初期値は論文の評価2で使った最適値）")]

        [Tooltip("φ：見どころ方向 [度]。開始時の正面を 0°、右回りをプラスとした角度。\n見どころは1か所だけで、正なら右側、負なら左側の 0°〜90° だけを制御します（反対側は制御なし）。\n実験では「見どころの鑑賞」パターンの角度として使い（左右は試行ごとに交互）、カメラ挙動チェックではこの値（符号つき）を使います。\n元研究の評価2は 45°。")]
        [Range(-89f, 89f)]
        public float highlightDirection = 45f;

        [Tooltip("w：見どころ範囲 [度]（仮想視線方向上の幅）。見どころのオブジェクトが見える横幅に合わせます。\nw/2 が |φ| と (90−|φ|) より小さい必要があります。元研究の評価2は 20°（予備評価で実用的とされた範囲は 10〜20°）。")]
        [Range(1f, 88f)]
        public float highlightWidth = 20f;

        [Tooltip("L：折れ点の近くをベジェ曲線で置き換える長さ [度]（r-v グラフ上の長さ）。0 にすると角のある折れ線になります。\n元研究では、傾きの変化が滑らかに感じられる最小の値として 5 に固定。")]
        [Range(0f, 15f)]
        public float bezierLength = HighlightMapping.DefaultBezierLength;

        // -----------------------------------------------------------------
        [Header("■ 動的制御のパラメータ（スライド6・7）")]

        [Tooltip("s₀：傾き s の最小値。制御一定（元研究）の条件ではこの値で固定されます。\n元研究の最適値（w = 20 で見どころの見やすさを重視したとき）の 0.55。予備評価で実用的とされた範囲は 0.4〜0.85。")]
        [Range(0.05f, 1f)]
        public float minSlope = 0.55f;

        [Tooltip("ω₀ [度/秒]：|ω| がこの値以上になると s = s₀（最も強い制御）になります。")]
        [Min(1f)]
        public float omegaThreshold = 90f;

        [Tooltip("N：角速度の計算に使うフレーム数。直近 N フレームの角速度を単純平均します（元研究と同じく 5）。")]
        [Range(1, 30)]
        public int velocityFrameCount = 5;

        // -----------------------------------------------------------------
        [Header("■ 試行のパターン（角度は正面からの大きさ [度]。左右は試行ごとに交互）")]

        [Tooltip("「見どころより手前の鑑賞」パターンの見どころの角度 [度]。鑑賞対象（青い箱）はこれより手前に置きます。")]
        [Range(1f, 89f)]
        public float beforePatternHighlightAngle = 60f;

        [Tooltip("「見どころより手前の鑑賞」パターンの鑑賞対象（青い箱）の角度 [度]。見どころ範囲の外にしてください。")]
        [Range(1f, 89f)]
        public float beforePatternObjectAngle = 30f;

        [Tooltip("「見どころより奥の鑑賞」パターンの見どころの角度 [度]。鑑賞対象（青い箱）はこれより奥に置きます。")]
        [Range(1f, 89f)]
        public float beyondPatternHighlightAngle = 30f;

        [Tooltip("「見どころより奥の鑑賞」パターンの鑑賞対象（青い箱）の角度 [度]。見どころ範囲の外にしてください。")]
        [Range(1f, 89f)]
        public float beyondPatternObjectAngle = 60f;

        [Tooltip("1セット（同じ条件・同じパターン）の中で、左右それぞれ何回ずつ試行するか。\n1セットの試行数はこの2倍（右左交互）になります。")]
        [Range(1, 10)]
        public int trialsPerSide = 3;

        // -----------------------------------------------------------------
        [Header("■ 実験の進行")]

        [Tooltip("ON：本番の前に「通常のVR視聴」で練習セット（3パターンを1回ずつ）を行う（練習のログも保存されます）。")]
        public bool includePracticeTrial = true;

        [Tooltip("1試行の制限時間 [秒]。0 にすると制限なし（実験者が Space キーで終了する）。")]
        [Min(0f)]
        public float trialDurationSeconds = 0f;

        [Tooltip("オブジェクトを「見ている」と判定する角度 [度]。視野の中心からこの角度以内にあれば注視中とみなします。")]
        [Range(1f, 45f)]
        public float gazeAngleThreshold = 10f;

        [Tooltip("ON：注視中のオブジェクトの色を変える（動作確認用。本番では OFF 推奨：被験者の行動に影響するため）。")]
        public bool highlightGazedTarget = false;

        // -----------------------------------------------------------------
        // 以下は「プロパティ」です。スライドと同じ記号の名前で値を読めるようにしています。
        // 例：config.Phi は config.highlightDirection と同じ値です。

        /// <summary>φ：見どころ方向 [度]</summary>
        public float Phi => highlightDirection;

        /// <summary>w：見どころ範囲 [度]</summary>
        public float W => highlightWidth;

        /// <summary>s₀：傾きの最小値</summary>
        public float S0 => minSlope;

        /// <summary>L：ベジェ曲線で置き換える長さ [度]</summary>
        public float L => bezierLength;

        /// <summary>ω₀：s が変化する |ω| の上限 [度/秒]</summary>
        public float Omega0 => omegaThreshold;

        /// <summary>
        /// パラメータが正しいかを調べます。問題があれば message に理由が入ります。
        /// </summary>
        public bool IsValid(out string message)
        {
            // ① 基本の φ（見どころの鑑賞パターン・カメラ挙動チェック用）
            if (!HighlightMapping.Validate(highlightDirection, highlightWidth, minSlope, bezierLength, out message))
            {
                return false;
            }

            // ② 手前・奥のパターンの見どころの角度でも、制御関数が成り立つか
            //    （φ の値によって s の下限や L の上限が変わるため、パターンごとに調べる）
            foreach (float angle in new[] { beforePatternHighlightAngle, beyondPatternHighlightAngle })
            {
                if (!HighlightMapping.Validate(angle, highlightWidth, minSlope, bezierLength, out string detail))
                {
                    message = $"パターンの見どころの角度 {angle}° では制御関数が作れません：{detail}";
                    return false;
                }
            }

            // ③ 鑑賞対象（青い箱）が見どころ範囲（φ ± w/2）の中に入っていないか
            if (!IsOutsideHighlight(beforePatternObjectAngle, beforePatternHighlightAngle, out message) ||
                !IsOutsideHighlight(beyondPatternObjectAngle, beyondPatternHighlightAngle, out message))
            {
                return false;
            }

            // ④ 手前・奥の向きが名前どおりか
            if (beforePatternObjectAngle >= beforePatternHighlightAngle)
            {
                message = "「手前の鑑賞」では、鑑賞対象の角度を見どころより小さくしてください。";
                return false;
            }
            if (beyondPatternObjectAngle <= beyondPatternHighlightAngle)
            {
                message = "「奥の鑑賞」では、鑑賞対象の角度を見どころより大きくしてください。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        /// <summary>
        /// 鑑賞対象の角度が、見どころ範囲（仮想視線方向で φ − w/2 〜 φ + w/2）の外にあるかを調べます。
        /// </summary>
        private bool IsOutsideHighlight(float objectAngle, float highlightAngle, out string message)
        {
            if (Mathf.Abs(objectAngle - highlightAngle) <= highlightWidth / 2f)
            {
                message = $"鑑賞対象の角度 {objectAngle}° が、見どころ（{highlightAngle}°）の見どころ範囲 w={highlightWidth}° の中に入っています。";
                return false;
            }
            message = string.Empty;
            return true;
        }

        /// <summary>
        /// OnValidate は、Inspector で値を変更したときに Unity が自動で呼ぶ関数です。
        /// ここでパラメータをチェックし、間違っていれば Console に警告を出します。
        /// </summary>
        private void OnValidate()
        {
            if (!IsValid(out string message))
            {
                Debug.LogWarning($"[ExperimentConfig] パラメータに問題があります：{message}", this);
            }
        }
    }
}
