// =====================================================================
// AngularVelocityEstimator.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   HMD の角速度 ω（頭がどれくらいの速さで左右に回っているか）を推定するクラスです。
//   スライド7の式をそのまま実装しています。
//
// 【スライド7の式】
//   現在のフレームから k フレーム前の実視線方向を r_k、
//   k フレーム前から (k−1) フレーム前までの時間を Δt_k、
//   k フレーム前の中間時刻から現在の時刻までの時間を a_k とすると、
//
//       v_k = (r_{k−1} − r_k) / Δt_k          … 区間 k での角速度
//       w_k = Δt_k · e^(−a_k / τ)             … 区間 k の重み（古いほど小さい）
//       ω   = Σ(w_k · v_k) / Σ w_k  （k = 1〜N） … 重み付き平均
//
//   ※ スライドでは N = 5（直近5フレーム分）です。
//   ※ τ（タウ）は「どれくらい古いデータまで重視するか」を決める時定数 [秒]。
//     τ が小さいほど新しいデータだけを重視し（反応が速いがガタつきやすい）、
//     τ が大きいほど古いデータも使います（なめらかだが反応が遅い）。
//
// 【なぜ1フレームの差だけで計算しないのか】
//   HMD のセンサー値には細かい揺れ（ノイズ）があるので、1フレームだけで計算すると
//   ω がガタガタに揺れてしまいます。数フレーム分を平均することで安定させています。
//
// 【他のファイルとの関係】
//   GazeDirectionController.cs が毎フレーム AddSample() を呼び、
//   その結果の Omega を使って傾き s を決めます。
// =====================================================================

using UnityEngine;

namespace DynamicHighlights.Core
{
    /// <summary>
    /// 直近 N フレームの頭の向きから角速度 ω を推定するクラスです（スライド7）。
    /// <para>
    /// 使い方：
    /// <code>
    /// var estimator = new AngularVelocityEstimator(5, 0.05f); // N=5, τ=0.05秒
    /// estimator.AddSample(時刻, 実視線方向r);                // 毎フレーム呼ぶ
    /// float omega = estimator.Omega;                          // 推定された角速度 [度/秒]
    /// </code>
    /// </para>
    /// </summary>
    public class AngularVelocityEstimator
    {
        // ---- 過去のデータを入れておく「リングバッファ」 ----
        // リングバッファとは、決まった大きさの配列を「輪っか」のように使い回す仕組みです。
        // 新しいデータを入れるたびに一番古いデータを上書きするので、
        // 常に「直近 N+1 個」のデータだけが残ります（メモリを無駄に増やさない）。
        private readonly double[] _times; // 各サンプルの時刻 [秒]
        private readonly float[] _yaws;   // 各サンプルの実視線方向 r [度]
        private int _newestIndex = -1;    // 一番新しいデータが入っている位置
        private int _count;               // いま入っているデータの数

        /// <summary>平均に使うフレーム数 N（スライドでは 5）</summary>
        public int FrameCount { get; }

        /// <summary>時間減衰係数 τ [秒]</summary>
        public float Tau { get; set; }

        /// <summary>最後に計算した角速度 ω [度/秒]。右回りがプラス、左回りがマイナス。</summary>
        public float Omega { get; private set; }

        /// <summary>
        /// コンストラクタ（new したときに最初に呼ばれる関数）です。
        /// </summary>
        /// <param name="frameCount">平均に使うフレーム数 N（1以上）</param>
        /// <param name="tau">時間減衰係数 τ [秒]</param>
        public AngularVelocityEstimator(int frameCount, float tau)
        {
            FrameCount = Mathf.Max(1, frameCount);
            Tau = tau;

            // N 個の「区間」を作るには、N+1 個の「点（サンプル）」が必要です。
            _times = new double[FrameCount + 1];
            _yaws = new float[FrameCount + 1];
        }

        /// <summary>
        /// 過去のデータをすべて消します（リセンター時や条件の切り替え時に使う）。
        /// </summary>
        public void Reset()
        {
            _newestIndex = -1;
            _count = 0;
            Omega = 0f;
        }

        /// <summary>
        /// 新しいサンプル（いまの時刻と実視線方向）を追加し、角速度 ω を計算し直します。
        /// 1フレームに1回呼んでください。
        /// </summary>
        /// <param name="time">いまの時刻 [秒]</param>
        /// <param name="yaw">いまの実視線方向 r [度]</param>
        public void AddSample(double time, float yaw)
        {
            // ① リングバッファに追加（一番古いデータを上書き）
            _newestIndex = (_newestIndex + 1) % _times.Length;
            _times[_newestIndex] = time;
            _yaws[_newestIndex] = yaw;
            if (_count < _times.Length) _count++;

            // ② ω を計算し直す
            Omega = Compute();
        }

        /// <summary>
        /// 「k フレーム前」のデータが配列のどこにあるかを返します（k = 0 が現在）。
        /// </summary>
        private int IndexOf(int k)
        {
            int length = _times.Length;
            return ((_newestIndex - k) % length + length) % length;
        }

        /// <summary>
        /// スライド7の式で ω を計算します。
        /// </summary>
        private float Compute()
        {
            // 使える区間の数（データが少ない最初のうちは N より少ない）
            int intervals = Mathf.Min(_count - 1, FrameCount);
            if (intervals <= 0) return 0f;

            double now = _times[IndexOf(0)]; // 現在の時刻 t₀

            double sumWeightedVelocity = 0.0; // Σ w_k · v_k
            double sumWeight = 0.0;           // Σ w_k

            for (int k = 1; k <= intervals; k++)
            {
                int older = IndexOf(k);     // k フレーム前
                int newer = IndexOf(k - 1); // (k−1) フレーム前

                // Δt_k：k フレーム前から (k−1) フレーム前までの時間
                double deltaT = _times[newer] - _times[older];
                if (deltaT <= 1e-6) continue; // 時間が進んでいないデータは使わない（0 で割るのを防ぐ）

                // v_k = (r_{k−1} − r_k) / Δt_k
                // 角度の差は Mathf.DeltaAngle で計算します。
                // （そのまま引き算すると 179° → −179° のとき −358° になってしまうが、
                //   DeltaAngle なら正しく +2° になる）
                double velocity = Mathf.DeltaAngle(_yaws[older], _yaws[newer]) / deltaT;

                // a_k：k フレーム前の区間の「中間時刻」から現在までの時間
                double midTime = (_times[older] + _times[newer]) / 2.0;
                double age = now - midTime;

                // w_k = Δt_k · e^(−a_k / τ)
                // τ が 0 以下の場合は減衰なし（区間の長さ Δt_k だけで重み付け）とします。
                double weight = Tau > 0f ? deltaT * System.Math.Exp(-age / Tau) : deltaT;

                sumWeightedVelocity += weight * velocity;
                sumWeight += weight;
            }

            if (sumWeight <= 0.0) return 0f;

            // ω = Σ(w_k · v_k) / Σ w_k
            return (float)(sumWeightedVelocity / sumWeight);
        }
    }
}
