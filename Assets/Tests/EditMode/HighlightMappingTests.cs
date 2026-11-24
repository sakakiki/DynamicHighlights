// =====================================================================
// HighlightMappingTests.cs（自動テスト）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   HighlightMapping.cs の数式（元研究の論文 2.2〜2.3節、スライド7）が正しく実装されているかを、
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
//   元研究の論文で最終的に使われた φ = 45°, w = 20°, s = 0.55, L = 5 を基本にします。
//   （角のある折れ線の確認だけは、スライド8と同じ s = 0.5 も使います）
//
// 【元研究のコードと比べた値について】
//   「元研究のコードと同じ値になる」テストの期待値は、元研究の RotationController.cs
//   （Assets/360-Image/Scripts/）の計算をそのまま実行して求めた値です。
// =====================================================================

using DynamicHighlights.Core;
using NUnit.Framework;

namespace DynamicHighlights.Tests
{
    public class HighlightMappingTests
    {
        private const float Phi = 45f;
        private const float W = 20f;
        private const float S0 = 0.55f;   // 元研究の最適値
        private const float L = 5f;       // 元研究のベジェ曲線の長さ
        private const float Omega0 = 90f;
        private const float Tolerance = 1e-3f; // 許す誤差

        // ---------------- 論文の式 2.1〜2.5：端点 ----------------

        [Test]
        public void 端点が論文の式どおりに計算される()
        {
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(Phi, W, S0);
            Assert.AreEqual(35f, e.V1, Tolerance, "v₁ = φ − w/2 = 35");
            Assert.AreEqual(55f, e.V2, Tolerance, "v₂ = φ + w/2 = 55");
            Assert.AreEqual(45f - 10f / 0.55f, e.R1, Tolerance, "r₁ = φ − w/(2s) ≒ 26.82");
            Assert.AreEqual(45f + 10f / 0.55f, e.R2, Tolerance, "r₂ = φ + w/(2s) ≒ 63.18");
        }

        [Test]
        public void 見どころが左でも端点の式はそのまま使える()
        {
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(-Phi, W, 0.5f);
            Assert.AreEqual(-55f, e.V1, Tolerance);
            Assert.AreEqual(-35f, e.V2, Tolerance);
            Assert.AreEqual(-65f, e.R1, Tolerance);
            Assert.AreEqual(-25f, e.R2, Tolerance);
        }

        // ---------------- 論文 2.2節：角のある折れ線（L = 0） ----------------

        [Test]
        public void 折れ線がr1とr2と90度でつながっている()
        {
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(Phi, W, S0);
            const float d = 1e-3f;

            // 境目のすぐ左とすぐ右で、v がほぼ同じ値になる（＝線が途切れていない）
            Assert.AreEqual(e.V1, Polyline(e.R1 - d), 0.01f);
            Assert.AreEqual(e.V1, Polyline(e.R1 + d), 0.01f);
            Assert.AreEqual(e.V2, Polyline(e.R2 - d), 0.01f);
            Assert.AreEqual(e.V2, Polyline(e.R2 + d), 0.01f);
            Assert.AreEqual(90f, Polyline(90f), Tolerance);
            Assert.AreEqual(0f, Polyline(0f), Tolerance);
        }

        [Test]
        public void スライド8の図と同じ点を通る()
        {
            // |ω| ≧ 90 のグラフ（s = 0.5）：P₁ = (25, 35)、P₂ = (65, 55)
            Assert.AreEqual(35f, HighlightMapping.MapAbsolute(25f, Phi, W, 0.5f, 0f), Tolerance);
            Assert.AreEqual(55f, HighlightMapping.MapAbsolute(65f, Phi, W, 0.5f, 0f), Tolerance);
            // 見どころ範囲内では傾き 0.5：r = 35 → v = 0.5 × (35 − 45) + 45 = 40
            Assert.AreEqual(40f, HighlightMapping.MapAbsolute(35f, Phi, W, 0.5f, 0f), Tolerance);
        }

        // ---------------- 論文 2.3節：ベジェ曲線で滑らかにした関数 ----------------

        [Test]
        public void 見どころ方向では実視線と仮想視線が一致する()
        {
            Assert.AreEqual(Phi, Map(Phi), Tolerance);
        }

        [Test]
        public void 折れ点から離れた所では折れ線と同じ値になる()
        {
            // ベジェ曲線で置き換えるのは折れ点の近く（L = 5 の範囲）だけ
            foreach (float r in new[] { 5f, 10f, 20f, 35f, 45f, 55f, 70f, 80f })
            {
                Assert.AreEqual(Polyline(r), Map(r), Tolerance, $"r = {r}");
            }
        }

        [Test]
        public void 折れ点の近くは折れ線より内側を通る()
        {
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(Phi, W, S0);
            // P₁ は上に凸の角なので、曲線は折れ線より下を通る。P₂ は下に凸なので上を通る。
            Assert.Less(Map(e.R1), e.V1 - 0.5f, "P₁ の角が丸められている");
            Assert.Greater(Map(e.R2), e.V2 + 0.5f, "P₂ の角が丸められている");
        }

        [Test]
        public void 元研究のコードと同じ値になる()
        {
            // 元研究の RotationController.cs（bezier モード、t=45, w=20, s=0.55, L=5）の計算結果。
            // 元研究のコードは曲線を50点の折れ線で近似しているので、0.001° 程度の差は許す。
            Assert.AreEqual(32.4208f, Map(25f), 2e-3f);
            Assert.AreEqual(34.1493f, Map(26.82f), 2e-3f);
            Assert.AreEqual(36.6603f, Map(30f), 2e-3f);
            Assert.AreEqual(53.3397f, Map(60f), 2e-3f);
            Assert.AreEqual(55.8508f, Map(63.18f), 2e-3f);
        }

        [Test]
        public void 傾きがなめらかに変わる()
        {
            // 隣り合う小さな区間の傾きを比べ、急に変わる所（角）が無いことを確かめる。
            // （r = 0 は見どころと反対側との境目で、元研究どおり角が残るので除く）
            const float h = 0.01f;
            float previousSlope = (Map(1f) - Map(1f - h)) / h;
            for (float r = 1f + h; r <= 90f; r += h)
            {
                float slope = (Map(r) - Map(r - h)) / h;
                Assert.AreEqual(previousSlope, slope, 0.02f, $"r = {r}");
                previousSlope = slope;
            }
        }

        [Test]
        public void 制御関数は単調に増加する()
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
        public void 傾きが1なら制御なしと同じになる()
        {
            for (float r = -180f; r <= 180f; r += 2.5f)
            {
                Assert.AreEqual(r, HighlightMapping.MapAbsolute(r, Phi, W, 1f, L), Tolerance, $"r = {r}");
            }
        }

        // ---------------- 見どころは1か所（片側だけ制御） ----------------

        [Test]
        public void 見どころと反対側は制御なし()
        {
            for (float r = -90f; r <= 0f; r += 5f)
            {
                Assert.AreEqual(r, Map(r), Tolerance, $"r = {r}");
            }
            Assert.AreEqual(0f, Map(0f), Tolerance, "正面を向いたら v = 0（元研究のコードでは 0.31° ずれていた）");
        }

        [Test]
        public void 見どころが左なら左右反転した形になる()
        {
            for (float r = -90f; r <= 90f; r += 2.5f)
            {
                float right = HighlightMapping.MapAbsolute(r, Phi, W, S0, L);
                float left = HighlightMapping.MapAbsolute(-r, -Phi, W, S0, L);
                Assert.AreEqual(-right, left, Tolerance, $"r = {r}");
            }
        }

        [Test]
        public void 九十度より外側は制御なし()
        {
            // 元研究のコード（bezier モード）ではここが NaN になっていた
            Assert.AreEqual(100f, Map(100f), Tolerance);
            Assert.AreEqual(150f, Map(150f), Tolerance);
            Assert.AreEqual(-150f, Map(-150f), Tolerance);
        }

        // ---------------- スライド7：s(ω) ----------------

        [Test]
        public void 角速度から傾きsがスライド7の式どおりに決まる()
        {
            Assert.AreEqual(1f, HighlightMapping.SlopeFromOmega(0f, Omega0, S0), Tolerance, "ω=0 → s=1");
            Assert.AreEqual(0.775f, HighlightMapping.SlopeFromOmega(45f, Omega0, S0), Tolerance, "ω=45 → s=0.775");
            Assert.AreEqual(0.55f, HighlightMapping.SlopeFromOmega(90f, Omega0, S0), Tolerance, "ω=90 → s=s₀");
            Assert.AreEqual(0.55f, HighlightMapping.SlopeFromOmega(200f, Omega0, S0), Tolerance, "ω>ω₀ → s=s₀");
            Assert.AreEqual(0.775f, HighlightMapping.SlopeFromOmega(-45f, Omega0, S0), Tolerance, "左回り（負）でも絶対値で計算");
        }

        [Test]
        public void 制御強度cは0から1の範囲になる()
        {
            Assert.AreEqual(0f, HighlightMapping.ControlStrength(1f, S0), Tolerance, "s=1 → c=0（制御なし）");
            Assert.AreEqual(1f, HighlightMapping.ControlStrength(S0, S0), Tolerance, "s=s₀ → c=1（元研究と同じ）");
            Assert.AreEqual(0.5f, HighlightMapping.ControlStrength(0.775f, S0), Tolerance);
        }

        // ---------------- パラメータのチェック ----------------

        [Test]
        public void sの下限が正しく計算される()
        {
            // s > w / (2·min(|φ|, 90−|φ|)) = 20 / 90 = 0.222…
            Assert.AreEqual(20f / 90f, HighlightMapping.MinimumValidSlope(Phi, W), Tolerance);
            Assert.AreEqual(20f / 90f, HighlightMapping.MinimumValidSlope(-Phi, W), Tolerance, "左でも同じ");
        }

        [Test]
        public void 正しくないパラメータを見つけられる()
        {
            Assert.IsTrue(HighlightMapping.Validate(Phi, W, S0, L, out _), "元研究の値は正しい");
            Assert.IsTrue(HighlightMapping.Validate(-Phi, W, S0, L, out _), "見どころが左でもよい");
            Assert.IsTrue(HighlightMapping.Validate(Phi, W, S0, 0f, out _), "L = 0（角のある折れ線）でもよい");
            Assert.IsFalse(HighlightMapping.Validate(Phi, W, 0.2f, L, out _), "s₀ が下限より小さい");
            Assert.IsFalse(HighlightMapping.Validate(10f, W, S0, L, out _), "w/2 が φ 以上");
            Assert.IsFalse(HighlightMapping.Validate(95f, W, S0, L, out _), "φ が 90 以上");
            Assert.IsFalse(HighlightMapping.Validate(0f, W, S0, L, out _), "φ = 0 は不可");
            Assert.IsFalse(HighlightMapping.Validate(Phi, W, S0, 20f, out _), "L が大きすぎる");
            Assert.IsFalse(HighlightMapping.Validate(Phi, W, S0, -1f, out _), "L が負");
        }

        // ---------------- 既知の挙動（急停止） ----------------

        [Test]
        public void 止まったままsが戻るとvが動く_既知の挙動()
        {
            // 動的制御で r = 25° まで速く回すと s = s₀ = 0.55 なので v ≒ 32.42°。
            // そこで急に止まると s = 1 に戻り v = 25° になる（頭は止まっているのに視界が約 7.4° 戻る）。
            // 研究の焦点ではないため、実験前に被験者へ口頭で伝えて対応する挙動です。
            float vFast = HighlightMapping.MapAbsolute(25f, Phi, W, S0, L);
            float vStopped = HighlightMapping.MapAbsolute(25f, Phi, W, 1f, L);
            Assert.AreEqual(7.421f, vFast - vStopped, 0.01f);
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

        // テストを短く書くための補助関数
        // 滑らかな制御関数（元研究の最終形：φ = 45, w = 20, s = 0.55, L = 5）
        private static float Map(float r)
        {
            return HighlightMapping.MapAbsolute(r, Phi, W, S0, L);
        }

        // 角のある折れ線（L = 0）
        private static float Polyline(float r)
        {
            return HighlightMapping.MapAbsolute(r, Phi, W, S0, 0f);
        }
    }
}
