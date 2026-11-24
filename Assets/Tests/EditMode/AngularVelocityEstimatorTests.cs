// =====================================================================
// AngularVelocityEstimatorTests.cs（自動テスト）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   AngularVelocityEstimator.cs（角速度 ω の推定：直近 N フレームの単純平均）が正しいかをチェックします。
//
// 【確認していること】
//   ・一定の速さで回したら、その速さが ω として出てくる
//   ・179° → −179° のように角度が一周してつながる所をまたいでも正しい
//   ・フレームの間隔がバラバラでも正しい
//   ・データが少ない最初のうちもエラーにならない
//   ・すべてのフレームを同じ重みで平均する（元研究の SteamVR VelocityEstimator と同じ）
//   ・頭が止まると、ちょうど N フレーム後に ω = 0 になる
//   ・時間が進んでいないデータは無視する
// =====================================================================

using DynamicHighlights.Core;
using NUnit.Framework;

namespace DynamicHighlights.Tests
{
    public class AngularVelocityEstimatorTests
    {
        private const float Tolerance = 1e-2f;

        [Test]
        public void 一定の速さで回すとその速さがωになる()
        {
            var estimator = new AngularVelocityEstimator(5);
            const double dt = 1.0 / 90.0; // 90Hz（Quest の画面の更新頻度の例）
            const float speed = 30f;      // 30°/秒

            for (int i = 0; i < 20; i++)
            {
                double time = i * dt;
                estimator.AddSample(time, (float)(speed * time));
            }
            Assert.AreEqual(speed, estimator.Omega, Tolerance);
        }

        [Test]
        public void 左回りではωが負になる()
        {
            var estimator = new AngularVelocityEstimator(5);
            for (int i = 0; i < 20; i++)
            {
                double time = i / 72.0;
                estimator.AddSample(time, (float)(-60.0 * time));
            }
            Assert.AreEqual(-60f, estimator.Omega, Tolerance);
        }

        [Test]
        public void 角度が180度をまたいでも正しい()
        {
            var estimator = new AngularVelocityEstimator(5);
            const double dt = 0.01;
            // 170° から 1フレームに 2° ずつ右に回す → 180° を超えると −178° … と表される
            for (int i = 0; i < 15; i++)
            {
                float yaw = HighlightMapping.WrapAngle(170f + 2f * i);
                estimator.AddSample(i * dt, yaw);
            }
            Assert.AreEqual(200f, estimator.Omega, 0.1f); // 2° / 0.01秒 = 200°/秒
        }

        [Test]
        public void フレーム間隔がバラバラでも等速なら正しい()
        {
            var estimator = new AngularVelocityEstimator(5);
            double[] intervals = { 0.011, 0.014, 0.009, 0.020, 0.012, 0.016, 0.010 };
            double time = 0.0;
            estimator.AddSample(time, 0f);
            foreach (double interval in intervals)
            {
                time += interval;
                estimator.AddSample(time, (float)(45.0 * time)); // 45°/秒
            }
            Assert.AreEqual(45f, estimator.Omega, Tolerance);
        }

        [Test]
        public void データが1つだけならωは0()
        {
            var estimator = new AngularVelocityEstimator(5);
            estimator.AddSample(0.0, 10f);
            Assert.AreEqual(0f, estimator.Omega, Tolerance);
        }

        [Test]
        public void すべてのフレームを同じ重みで平均する()
        {
            // 前半はゆっくり（10°/秒）、最後の1区間だけ速い（100°/秒）
            var estimator = new AngularVelocityEstimator(5);
            const double dt = 0.01;
            float yaw = 0f;
            for (int i = 0; i < 6; i++)
            {
                estimator.AddSample(i * dt, yaw);
                yaw += 0.1f; // 10°/秒
            }
            yaw += 0.9f; // 最後の区間は 1.0° / 0.01秒 = 100°/秒
            estimator.AddSample(6 * dt, yaw);

            // 直近5フレーム（10, 10, 10, 10, 100）の単純平均 = (10×4 + 100) / 5 = 28
            Assert.AreEqual(28f, estimator.Omega, Tolerance);
        }

        [Test]
        public void データがN個そろうまではそろっている分で平均する()
        {
            var estimator = new AngularVelocityEstimator(5);
            estimator.AddSample(0.00, 0f);
            estimator.AddSample(0.01, 0.2f); // 20°/秒
            estimator.AddSample(0.02, 0.8f); // 60°/秒
            Assert.AreEqual(40f, estimator.Omega, Tolerance); // (20 + 60) / 2
        }

        [Test]
        public void 止まるとちょうどNフレーム後にωが0になる()
        {
            var estimator = new AngularVelocityEstimator(5);
            const double dt = 0.01;
            int frame = 0;
            for (; frame < 10; frame++) estimator.AddSample(frame * dt, frame * 1f); // 100°/秒で回す

            // ここで止まる（r が変わらない）
            float stoppedYaw = (frame - 1) * 1f;
            for (int i = 1; i <= 4; i++)
            {
                estimator.AddSample(frame++ * dt, stoppedYaw);
                Assert.AreEqual(100f * (5 - i) / 5f, estimator.Omega, Tolerance, $"止まってから {i} フレーム目");
            }
            estimator.AddSample(frame * dt, stoppedYaw);
            Assert.AreEqual(0f, estimator.Omega, Tolerance, "5 フレーム目で 0");
        }

        [Test]
        public void 時間が進んでいないデータは無視する()
        {
            var estimator = new AngularVelocityEstimator(5);
            for (int i = 0; i < 10; i++) estimator.AddSample(i * 0.01, i * 0.5f); // 50°/秒
            estimator.AddSample(0.09, 30f); // 同じ時刻のデータ（一時停止中など）
            Assert.AreEqual(50f, estimator.Omega, Tolerance);
        }

        [Test]
        public void リセットすると履歴が消える()
        {
            var estimator = new AngularVelocityEstimator(5);
            for (int i = 0; i < 10; i++) estimator.AddSample(i * 0.01, i * 1f);
            estimator.Reset();
            Assert.AreEqual(0f, estimator.Omega, Tolerance);
            estimator.AddSample(1.0, 50f);
            Assert.AreEqual(0f, estimator.Omega, Tolerance, "リセット後の最初の1つだけでは速さは分からない");
        }
    }
}
