// =====================================================================
// AngularVelocityEstimator.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   HMD の角速度 ω（頭がどれくらいの速さで左右に回っているか）を推定するクラスです。
//   元にした研究と同じく、「直近 N フレームの角速度の単純平均」で求めます。
//
// 【元にした研究の方法】
//   元研究のプロジェクトでは、SteamVR の VelocityEstimator（CenterEyeAnchor に付けて、N = 5）で
//   角速度を求めていました。その計算方法は次のとおりです。
//     ① 毎フレーム「そのフレームで回った角度 ÷ そのフレームの時間」を計算する
//     ② 直近 N フレーム分を足して、個数で割る（単純平均）
//   本研究もこれに合わせています。
//   （以前のスライド7の「指数重み付き平均」は Archive/角速度_指数重み付き平均/ に保管しています）
//
// 【式】
//   k フレーム前の実視線方向を r_k、k フレーム前から (k−1) フレーム前までの時間を Δt_k とすると、
//
//       v_k = (r_{k−1} − r_k) / Δt_k          … 区間 k での角速度 [度/秒]
//       ω   = (1/M) · Σ v_k   （k = 1〜M）    … 単純平均
//       M   = min(これまでに計算した v_k の個数, N)
//
//   ※ N = 5（直近5フレーム分）。すべてのフレームを同じ重みで平均します。
//   ※ 頭が止まると、ちょうど N フレーム後に ω = 0 になります。
//
// 【元研究との違い】
//   元研究の VelocityEstimator は「3次元の回転（上下・首のかしげも含む）」の角速度 [ラジアン/秒] でした。
//   本研究の制御は左右（ヨー）だけなので、左右の角度 r の変化だけで ω [度/秒] を求めています。
//   平均の取り方（単純平均・N フレーム）は同じです。
//
// 【なぜ1フレームの差だけで計算しないのか】
//   HMD のセンサー値には細かい揺れ（ノイズ）があるので、1フレームだけで計算すると
//   ω がガタガタに揺れてしまいます。数フレーム分を平均することで安定させています。
//
// 【他のファイルとの関係】
//   GazeDirectionController.cs が毎フレーム AddSample() を呼び、
//   その結果の Omega を使って傾き s を決めます（スライド7の s(ω) の式）。
// =====================================================================

using UnityEngine;

namespace DynamicHighlights.Core
{
    /// <summary>
    /// 直近 N フレームの角速度の単純平均で ω を推定するクラスです（元研究と同じ方法）。
    /// <para>
    /// 使い方：
    /// <code>
    /// var estimator = new AngularVelocityEstimator(5);   // N = 5
    /// estimator.AddSample(時刻, 実視線方向r);              // 毎フレーム呼ぶ
    /// float omega = estimator.Omega;                     // 推定された角速度 [度/秒]
    /// </code>
    /// </para>
    /// </summary>
    public class AngularVelocityEstimator
    {
        // ---- 各フレームの角速度 v_k を入れておく「リングバッファ」 ----
        // リングバッファとは、決まった大きさの配列を「輪っか」のように使い回す仕組みです。
        // 新しいデータを入れるたびに一番古いデータを上書きするので、
        // 常に「直近 N 個」のデータだけが残ります（メモリを無駄に増やさない）。
        private readonly float[] _velocities; // 各フレームの角速度 v_k [度/秒]
        private int _nextIndex;               // 次にデータを書き込む位置
        private int _velocityCount;           // いま入っている角速度の数（最大 N）

        // ---- 1つ前のサンプル（角速度を計算するのに使う） ----
        private bool _hasPrevious;   // 1つ前のサンプルがあるかどうか
        private double _previousTime; // 1つ前の時刻 [秒]
        private float _previousYaw;   // 1つ前の実視線方向 r [度]

        /// <summary>平均に使うフレーム数 N（元研究では 5）</summary>
        public int FrameCount { get; }

        /// <summary>最後に計算した角速度 ω [度/秒]。右回りがプラス、左回りがマイナス。</summary>
        public float Omega { get; private set; }

        /// <summary>
        /// コンストラクタ（new したときに最初に呼ばれる関数）です。
        /// </summary>
        /// <param name="frameCount">平均に使うフレーム数 N（1以上）</param>
        public AngularVelocityEstimator(int frameCount)
        {
            FrameCount = Mathf.Max(1, frameCount);
            _velocities = new float[FrameCount];
        }

        /// <summary>
        /// 過去のデータをすべて消します（リセンターのときに使う）。
        /// </summary>
        public void Reset()
        {
            _nextIndex = 0;
            _velocityCount = 0;
            _hasPrevious = false;
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
            // ① 最初の1つ目は「前のサンプル」が無いので、記録するだけ（速さはまだ分からない）
            if (!_hasPrevious)
            {
                _previousTime = time;
                _previousYaw = yaw;
                _hasPrevious = true;
                return;
            }

            // ② このフレームの時間 Δt。時間が進んでいないデータは使わない（0 で割るのを防ぐ）
            double deltaT = time - _previousTime;
            if (deltaT <= 1e-6) return;

            // ③ このフレームの角速度 v = (いまの r − 前の r) / Δt
            //    角度の差は Mathf.DeltaAngle で計算します。
            //    （そのまま引き算すると 179° → −179° のとき −358° になってしまうが、
            //      DeltaAngle なら正しく +2° になる）
            float velocity = (float)(Mathf.DeltaAngle(_previousYaw, yaw) / deltaT);

            // ④ リングバッファに追加（N 個たまったら一番古いものを上書き）
            _velocities[_nextIndex] = velocity;
            _nextIndex = (_nextIndex + 1) % FrameCount;
            if (_velocityCount < FrameCount) _velocityCount++;

            _previousTime = time;
            _previousYaw = yaw;

            // ⑤ ω を計算し直す
            Omega = Compute();
        }

        /// <summary>
        /// 単純平均で ω を計算します。 ω = (1/M) · Σ v_k
        /// </summary>
        private float Compute()
        {
            if (_velocityCount == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < _velocityCount; i++)
            {
                sum += _velocities[i];
            }
            return sum / _velocityCount; // データが N 個そろうまでは、そろっている分だけで平均する
        }
    }
}
