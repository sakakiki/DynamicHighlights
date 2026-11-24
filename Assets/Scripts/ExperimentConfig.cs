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
        [Header("■ 見どころの設定（スライド4・6）")]

        [Tooltip("φ：見どころ方向 [度]。開始時の正面を 0°、右回りをプラスとした角度。")]
        [Range(1f, 89f)]
        public float highlightDirection = 45f;

        [Tooltip("w：見どころ範囲 [度]（仮想視線方向上の幅）。w/2 が φ と (90−φ) より小さい必要があります。")]
        [Range(1f, 88f)]
        public float highlightWidth = 20f;

        [Tooltip("ON：左右対称に制御する（−φ の位置にも見どころ範囲ができる）。OFF：0°〜90° の右側だけ制御する。")]
        public bool symmetric = true;

        // -----------------------------------------------------------------
        [Header("■ 動的制御のパラメータ（スライド6・7）")]

        [Tooltip("s₀：傾き s の最小値。制御一定の条件ではこの値で固定されます（元にした研究の最適値に合わせる）。")]
        [Range(0.05f, 1f)]
        public float minSlope = 0.5f;

        [Tooltip("ω₀ [度/秒]：|ω| がこの値以上になると s = s₀（最も強い制御）になります。")]
        [Min(1f)]
        public float omegaThreshold = 90f;

        [Tooltip("N：角速度の計算に使うフレーム数。直近 N フレームの角速度を単純平均します（元研究と同じく 5）。")]
        [Range(1, 30)]
        public int velocityFrameCount = 5;

        // -----------------------------------------------------------------
        [Header("■ 実験の進行")]

        [Tooltip("ON：本番の前に「通常のVR視聴」で練習試行を1回行う（練習のログも保存されます）。")]
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

        /// <summary>ω₀：s が変化する |ω| の上限 [度/秒]</summary>
        public float Omega0 => omegaThreshold;

        /// <summary>
        /// パラメータが正しいかを調べます。問題があれば message に理由が入ります。
        /// </summary>
        public bool IsValid(out string message)
        {
            return HighlightMapping.Validate(highlightDirection, highlightWidth, minSlope, out message);
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
