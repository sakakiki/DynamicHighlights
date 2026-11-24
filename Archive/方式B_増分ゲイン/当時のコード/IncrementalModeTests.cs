// =====================================================================
// IncrementalModeTests.cs（自動テスト）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   方式B（増分ゲイン方式：HighlightMapping.StepIncremental）が正しいかをチェックします。
//
// 【確認していること】
//   ・頭が止まっていれば（Δr = 0）、s が変わっても v は動かない   ← 方式B の一番大事な性質
//   ・s が一定なら、少しずつ回しても一気に回しても、方式A と同じ v になる
//   ・逆向きに回して戻れば、元の位置に戻る
//   ・角度が 180° をまたいでもつながる
//   ・方式A では、止まったまま s が変わると v が動いてしまう（問題の確認）
// =====================================================================

using DynamicHighlights.Core;
using NUnit.Framework;

namespace DynamicHighlights.Tests
{
    public class IncrementalModeTests
    {
        private const float Phi = 45f;
        private const float W = 20f;
        private const float S0 = 0.5f;
        private const float Tolerance = 1e-2f;

        [Test]
        public void 頭が止まっていればsが変わってもvは動かない()
        {
            float v = 37.5f;
            foreach (float s in new[] { 0.5f, 0.6f, 0.8f, 1f })
            {
                float next = HighlightMapping.StepIncremental(v, 0f, Phi, W, s, true);
                Assert.AreEqual(v, next, 1e-5f, $"s = {s}");
            }
        }

        [Test]
        public void sが一定なら少しずつ回しても方式Aと一致する()
        {
            float v = 0f;
            float r = 0f;
            const float step = 0.37f; // 区間の境目にぴったり乗らない半端な刻み
            while (r + step <= 90f)
            {
                v = HighlightMapping.StepIncremental(v, step, Phi, W, S0, true);
                r += step;
                float expected = HighlightMapping.MapAbsolute(r, Phi, W, S0, true);
                Assert.AreEqual(expected, v, Tolerance, $"r = {r}");
            }
        }

        [Test]
        public void 一気に90度回しても境目を正しくまたげる()
        {
            float v = HighlightMapping.StepIncremental(0f, 90f, Phi, W, S0, true);
            Assert.AreEqual(90f, v, Tolerance);

            float v2 = HighlightMapping.StepIncremental(0f, 30f, Phi, W, S0, true);
            Assert.AreEqual(HighlightMapping.MapAbsolute(30f, Phi, W, S0, true), v2, Tolerance);
        }

        [Test]
        public void 逆向きに回すと元に戻る()
        {
            float v = HighlightMapping.StepIncremental(0f, 70f, Phi, W, S0, true);
            v = HighlightMapping.StepIncremental(v, -70f, Phi, W, S0, true);
            Assert.AreEqual(0f, v, Tolerance);
        }

        [Test]
        public void 左側でも方式Aと一致する()
        {
            float v = 0f;
            for (int i = 1; i <= 90; i++)
            {
                v = HighlightMapping.StepIncremental(v, -1f, Phi, W, S0, true);
                Assert.AreEqual(HighlightMapping.MapAbsolute(-i, Phi, W, S0, true), v, Tolerance, $"r = {-i}");
            }
        }

        [Test]
        public void 傾きが1ならvはrと同じだけ動く()
        {
            float v = HighlightMapping.StepIncremental(20f, 13f, Phi, W, 1f, true);
            Assert.AreEqual(33f, v, Tolerance);
        }

        [Test]
        public void 角度が180度をまたいでもつながる()
        {
            float v = HighlightMapping.StepIncremental(175f, 10f, Phi, W, S0, true);
            Assert.AreEqual(-175f, v, Tolerance);
        }

        [Test]
        public void 方式Aでは止まったままsが戻るとvが動いてしまう()
        {
            // r = 25° で s = 0.5 → v = 35°。そのまま止まって s = 1 に戻ると v = 25° になる（10° のズレ）
            float vFast = HighlightMapping.MapAbsolute(25f, Phi, W, 0.5f, true);
            float vStopped = HighlightMapping.MapAbsolute(25f, Phi, W, 1f, true);
            Assert.AreEqual(10f, vFast - vStopped, Tolerance);
        }
    }
}
