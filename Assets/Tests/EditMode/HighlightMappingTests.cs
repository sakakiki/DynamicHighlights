// =====================================================================
// HighlightMappingTests.cs（自動テスト）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   HighlightMapping.cs の数式（スライド4・5・7）が正しく実装されているかを、
//   自動でチェックする「テスト」です。
//
// 【テストとは】
//   「この値を入れたら、この値が返ってくるはず」という確認をプログラムで書いておくものです。
//   数式を書き換えたときにテストを実行すれば、うっかり壊していないかすぐに分かります。
//   Unity では Window → General → Test Runner を開き、EditMode タブの「Run All」で実行できます。
//
// 【書き方】
//   [Test] を付けた関数が1つのテストです。
//   Assert.AreEqual(期待する値, 実際の値, 許す誤差) で「同じになっているか」を確認します。
//   ずれていたらテストは失敗（赤）、合っていれば成功（緑）になります。
//
// 【ここで使う値】
//   スライド8と同じ φ = 45°, w = 20°, s₀ = 0.5, ω₀ = 90°/s を使います。
// =====================================================================

using DynamicHighlights.Core;
using NUnit.Framework;

namespace DynamicHighlights.Tests
{
    public class HighlightMappingTests
    {
        private const float Phi = 45f;
        private const float W = 20f;
        private const float S0 = 0.5f;
        private const float Omega0 = 90f;
        private const float Tolerance = 1e-3f; // 許す誤差

        // ---------------- スライド4：端点 ----------------

        [Test]
        public void 端点がスライド4の式どおりに計算される()
        {
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(Phi, W, S0);
            Assert.AreEqual(35f, e.V1, Tolerance, "v₁ = φ − w/2 = 35");
            Assert.AreEqual(55f, e.V2, Tolerance, "v₂ = φ + w/2 = 55");
            Assert.AreEqual(25f, e.R1, Tolerance, "r₁ = φ − w/(2s) = 25");
            Assert.AreEqual(65f, e.R2, Tolerance, "r₂ = φ + w/(2s) = 65");
        }

        // ---------------- スライド5：折れ線 ----------------

        [Test]
        public void 折れ線がr1とr2と90度でつながっている()
        {
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(Phi, W, S0);
            const float d = 1e-3f;

            // 境目のすぐ左とすぐ右で、v がほぼ同じ値になる（＝線が途切れていない）
            Assert.AreEqual(e.V1, Map(e.R1 - d), 0.01f);
            Assert.AreEqual(e.V1, Map(e.R1 + d), 0.01f);
            Assert.AreEqual(e.V2, Map(e.R2 - d), 0.01f);
            Assert.AreEqual(e.V2, Map(e.R2 + d), 0.01f);
            Assert.AreEqual(90f, Map(90f), Tolerance);
            Assert.AreEqual(0f, Map(0f), Tolerance);
        }

        [Test]
        public void 見どころ方向では実視線と仮想視線が一致する()
        {
            Assert.AreEqual(Phi, Map(Phi), Tolerance);
        }

        [Test]
        public void スライド8の図と同じ点を通る()
        {
            // |ω| ≧ 90 のグラフ（s = 0.5）：P₁ = (25, 35)、P₂ = (65, 55)
            Assert.AreEqual(35f, Map(25f), Tolerance);
            Assert.AreEqual(55f, Map(65f), Tolerance);
            // 見どころ範囲内では傾き 0.5：r = 35 → v = 0.5 × (35 − 45) + 45 = 40
            Assert.AreEqual(40f, Map(35f), Tolerance);
        }

        [Test]
        public void 傾きが1なら制御なしと同じになる()
        {
            for (float r = -180f; r <= 180f; r += 2.5f)
            {
                float v = HighlightMapping.MapAbsolute(r, Phi, W, 1f, true);
                Assert.AreEqual(r, v, Tolerance, $"r = {r}");
            }
        }

        [Test]
        public void 折れ線は単調に増加する()
        {
            // r が増えれば v も必ず増える（視界が逆戻りしない）
            float previous = Map(-90f);
            for (float r = -89.5f; r <= 90f; r += 0.5f)
            {
                float v = Map(r);
                Assert.Greater(v, previous, $"r = {r}");
                previous = v;
            }
        }

        [Test]
        public void 左右対称の設定では負の側も対称になる()
        {
            for (float r = 0f; r <= 90f; r += 5f)
            {
                Assert.AreEqual(-Map(r), Map(-r), Tolerance, $"r = {r}");
            }
        }

        [Test]
        public void 左右対称でない設定では負の側は制御なし()
        {
            for (float r = -90f; r < 0f; r += 5f)
            {
                Assert.AreEqual(r, HighlightMapping.MapAbsolute(r, Phi, W, S0, false), Tolerance);
            }
            // 正の側はちゃんと制御される
            Assert.AreEqual(35f, HighlightMapping.MapAbsolute(25f, Phi, W, S0, false), Tolerance);
        }

        [Test]
        public void 九十度より外側は制御なし()
        {
            Assert.AreEqual(120f, Map(120f), Tolerance);
            Assert.AreEqual(-150f, Map(-150f), Tolerance);
        }

        // ---------------- スライド7：s(ω) ----------------

        [Test]
        public void 角速度から傾きsがスライド7の式どおりに決まる()
        {
            Assert.AreEqual(1f, HighlightMapping.SlopeFromOmega(0f, Omega0, S0), Tolerance, "ω=0 → s=1");
            Assert.AreEqual(0.75f, HighlightMapping.SlopeFromOmega(45f, Omega0, S0), Tolerance, "ω=45 → s=0.75");
            Assert.AreEqual(0.5f, HighlightMapping.SlopeFromOmega(90f, Omega0, S0), Tolerance, "ω=90 → s=s₀");
            Assert.AreEqual(0.5f, HighlightMapping.SlopeFromOmega(200f, Omega0, S0), Tolerance, "ω>ω₀ → s=s₀");
            Assert.AreEqual(0.75f, HighlightMapping.SlopeFromOmega(-45f, Omega0, S0), Tolerance, "左回り（負）でも絶対値で計算");
        }

        [Test]
        public void 制御強度cは0から1の範囲になる()
        {
            Assert.AreEqual(0f, HighlightMapping.ControlStrength(1f, S0), Tolerance, "s=1 → c=0（制御なし）");
            Assert.AreEqual(1f, HighlightMapping.ControlStrength(S0, S0), Tolerance, "s=s₀ → c=1（元研究と同じ）");
            Assert.AreEqual(0.5f, HighlightMapping.ControlStrength(0.75f, S0), Tolerance);
        }

        // ---------------- パラメータのチェック ----------------

        [Test]
        public void sの下限が正しく計算される()
        {
            // s > w / (2·min(φ, 90−φ)) = 20 / 90 = 0.222…
            Assert.AreEqual(20f / 90f, HighlightMapping.MinimumValidSlope(Phi, W), Tolerance);
        }

        [Test]
        public void 正しくないパラメータを見つけられる()
        {
            Assert.IsTrue(HighlightMapping.Validate(Phi, W, S0, out _), "スライドの値は正しい");
            Assert.IsFalse(HighlightMapping.Validate(Phi, W, 0.2f, out _), "s₀ が下限より小さい");
            Assert.IsFalse(HighlightMapping.Validate(10f, W, S0, out _), "w/2 が φ 以上");
            Assert.IsFalse(HighlightMapping.Validate(95f, W, S0, out _), "φ が 90 以上");
        }

        // ---------------- 角度の便利関数 ----------------

        [Test]
        public void 角度がマイナス180から180に収まる()
        {
            Assert.AreEqual(-170f, HighlightMapping.WrapAngle(190f), Tolerance);
            Assert.AreEqual(160f, HighlightMapping.WrapAngle(-200f), Tolerance);
        }

        [Test]
        public void 回転からヨー角を取り出せる()
        {
            var rotation = UnityEngine.Quaternion.Euler(-20f, 30f, 0f); // 上を 20° 向きながら右に 30°
            Assert.AreEqual(30f, HighlightMapping.YawOf(rotation, 0f), 0.01f);
            Assert.AreEqual(20f, HighlightMapping.PitchOf(rotation), 0.01f);
        }

        // テストを短く書くための補助関数（左右対称・s = s₀ の絶対写像）
        private static float Map(float r)
        {
            return HighlightMapping.MapAbsolute(r, Phi, W, S0, true);
        }
    }
}
