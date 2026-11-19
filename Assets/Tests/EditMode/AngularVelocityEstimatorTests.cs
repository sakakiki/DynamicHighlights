// =====================================================================
// AngularVelocityEstimatorTests.cs（自動テスト）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   AngularVelocityEstimator.cs（スライド7の角速度 ω の推定）が正しいかをチェックします。
//
// 【確認していること】
//   ・一定の速さで回したら、その速さが ω として出てくる
//   ・179° → −179° のように角度が一周してつながる所をまたいでも正しい
//   ・フレームの間隔がバラバラでも正しい
//   ・データが少ない最初のうちもエラーにならない
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
            var estimator = new AngularVelocityEstimator(5, 0.05f);
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
            var estimator = new AngularVelocityEstimator(5, 0.05f);
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
            var estimator = new AngularVelocityEstimator(5, 0.05f);
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
            var estimator = new AngularVelocityEstimator(5, 0.05f);
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
            var estimator = new AngularVelocityEstimator(5, 0.05f);
            estimator.AddSample(0.0, 10f);
            Assert.AreEqual(0f, estimator.Omega, Tolerance);
        }

        [Test]
        public void 新しいデータほど重視される()
        {
            // 前半はゆっくり（10°/秒）、最後の1区間だけ速い（100°/秒）
            var estimator = new AngularVelocityEstimator(5, 0.02f);
            const double dt = 0.01;
            float yaw = 0f;
            for (int i = 0; i < 6; i++)
            {
                estimator.AddSample(i * dt, yaw);
                yaw += 0.1f; // 10°/秒
            }
            yaw += 0.9f; // 最後の区間は 1.0° / 0.01秒 = 100°/秒
            estimator.AddSample(6 * dt, yaw);

            // 単純な平均なら (10×4 + 100) / 5 = 28 だが、新しいデータの重みが大きいので 28 より大きくなる
            Assert.Greater(estimator.Omega, 28f);
            Assert.Less(estimator.Omega, 100f);
        }

        [Test]
        public void リセットすると履歴が消える()
        {
            var estimator = new AngularVelocityEstimator(5, 0.05f);
            for (int i = 0; i < 10; i++) estimator.AddSample(i * 0.01, i * 1f);
            estimator.Reset();
            Assert.AreEqual(0f, estimator.Omega, Tolerance);
            estimator.AddSample(1.0, 50f);
            Assert.AreEqual(0f, estimator.Omega, Tolerance, "リセット後の最初の1つだけでは速さは分からない");
        }
    }
}
