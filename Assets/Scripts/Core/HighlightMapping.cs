// =====================================================================
// HighlightMapping.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   視線方向制御の「数式」だけを集めたクラスです。
//   Unity の GameObject やシーンには一切さわらず、
//   「数値を入れると数値が返ってくる」だけの純粋な計算をまとめています。
//   こうしておくと、HMD が無くても EditMode テスト（Assets/Tests）で
//   数式が正しいかを自動チェックできます。
//
// 【対応する資料】
//   元研究の論文（新宮 2025）
//     2.2節 … 折れ線による視線方向制御関数（式 2.1〜2.5。スライド4・5と同じ）
//     2.3節 … ベジェ曲線による平滑化（式 2.6）← 実際に使うのはこちら
//   スライド7 … 角速度 ω から傾き s を求める式（本研究の拡張）
//   ※ 以前あった「方式B（増分ゲイン方式）」は廃止しました（Archive/方式B_増分ゲイン/ を参照）。
//
// 【元研究から引き継いだこと】
//   ・見どころは 1 か所だけ。見どころ側（φ が正なら右、負なら左）の 0°〜90° だけを制御し、
//     反対側は制御なし（v = r）。関数は (−90, −90)・(0, 0)・(90, 90) を通る。
//   ・2つの折れ点 P₁, P₂ の近くを三次ベジェ曲線で滑らかにする（L = 5）。
//
// 【元研究の実装から直したこと】
//   ・元研究のコードは ±90° の外で計算結果が NaN（数ではない値）になっていた
//     → 本研究では ±90° の外は制御なし（v = r）にした。
//   ・元研究のコードは原点 (0, 0) の角も丸めていて、正面を向いても v ≠ 0 になっていた
//     → 論文 2.3節のとおり、丸めるのは P₁ と P₂ の2か所だけにした。
//   ・元研究のコードはベジェ曲線を50個の点の折れ線で近似していた
//     → 本研究ではベジェ曲線の式 (2.6) から直接 v を求める（誤差がほぼ 0）。
//
// 【記号と変数名の対応】
//   r   … 実視線方向（HMD の実際の向き）      → 変数名 r / realYaw   （単位：度）
//   v   … 仮想視線方向（画面に映る向き）      → 変数名 v / virtualYaw（単位：度）
//   φ   … 見どころ方向（−90 < φ < 90）       → phi                  （単位：度）
//   w   … 見どころ範囲（仮想視線方向上の幅）  → width                （単位：度）
//   s   … 見どころ範囲内の線分の傾き          → s                    （単位なし）
//   s₀  … s の最小値                          → s0
//   L   … ベジェ曲線で置き換える長さ          → bezierLength         （単位：度。r-v グラフ上の長さ）
//   ω   … HMD の角速度                        → omega                （単位：度/秒）
//   ω₀  … s が変化する |ω| の上限             → omega0               （単位：度/秒）
//
// 【角度の向き】
//   Unity では「上から見て時計回り（右を向く方向）」がヨー角のプラスです。
//   開始時の正面方向を 0° とし、右に 45° 向くと r = +45° になります。
//
// 【他のファイルとの関係】
//   GazeDirectionController.cs が毎フレームこのクラスの関数を呼び出し、
//   計算結果の v をカメラの向きに反映します。
// =====================================================================

using UnityEngine;

namespace DynamicHighlights.Core
{
    /// <summary>
    /// 折れ線の「折れ曲がる点」と見どころ範囲内の傾き s をまとめた入れ物（構造体）です。
    /// <para>
    /// 構造体（struct）は、いくつかの値をひとまとめにして持ち運ぶための仕組みです。
    /// 関数の戻り値として複数の値を一度に返したいときに便利です。
    /// </para>
    /// <para>
    /// φ が負（見どころが左）のときも同じ式で計算できます。
    /// 例：φ = −45, w = 20, s = 0.5 → v₁ = −55, v₂ = −35, r₁ = −65, r₂ = −25
    /// （v₁ ＜ v₂、r₁ ＜ r₂ の順番は変わりません）
    /// </para>
    /// </summary>
    public readonly struct MappingEndpoints
    {
        /// <summary>仮想視線方向における見どころ範囲の左端 v₁ = φ − w/2 [度]（論文の式 2.1）</summary>
        public readonly float V1;

        /// <summary>仮想視線方向における見どころ範囲の右端 v₂ = φ + w/2 [度]（論文の式 2.2）</summary>
        public readonly float V2;

        /// <summary>実視線方向における見どころ範囲の左端 r₁ = φ − w/(2s) [度]（論文の式 2.4）</summary>
        public readonly float R1;

        /// <summary>実視線方向における見どころ範囲の右端 r₂ = φ + w/(2s) [度]（論文の式 2.5）</summary>
        public readonly float R2;

        /// <summary>傾き s（見どころ範囲内の線分の傾き）</summary>
        public readonly float S;

        public MappingEndpoints(float v1, float v2, float r1, float r2, float s)
        {
            V1 = v1;
            V2 = v2;
            R1 = r1;
            R2 = r2;
            S = s;
        }
    }

    /// <summary>
    /// 視線方向制御の数式をまとめた static クラスです。
    /// <para>
    /// static クラスは「インスタンス（new で作る実体）を作らずに使う関数の集まり」です。
    /// 例：<c>HighlightMapping.MapAbsolute(30f, 45f, 20f, 0.55f, 5f)</c> のように直接呼べます。
    /// </para>
    /// </summary>
    public static class HighlightMapping
    {
        /// <summary>
        /// 制御をかける範囲の端の角度（90°）。
        /// 論文の式に出てくる「90」です。0°〜90°（φ が負なら −90°〜0°）の間で関数を作り、
        /// それより外側（|r| &gt; 90°）は制御なし（v = r）にします。
        /// </summary>
        public const float MaxAngle = 90f;

        /// <summary>
        /// ベジェ曲線で置き換える長さ L の初期値（元研究の論文 3.1節で L = 5 に固定）。
        /// </summary>
        public const float DefaultBezierLength = 5f;

        /// <summary>
        /// 0 で割ってしまうのを防ぐための、ごく小さな余裕の値です。
        /// </summary>
        private const float Epsilon = 1e-4f;

        // -----------------------------------------------------------------
        // ■ 論文の式 2.1〜2.5（スライド4）：端点の計算
        // -----------------------------------------------------------------

        /// <summary>
        /// 見どころ範囲の端点 v₁, v₂, r₁, r₂ を計算します（論文の式 2.1〜2.5、スライド4）。
        /// </summary>
        /// <param name="phi">見どころ方向 φ [度]（−90〜90。0 は不可）</param>
        /// <param name="width">見どころ範囲 w [度]</param>
        /// <param name="s">見どころ範囲内の線分の傾き s（0 &lt; s ≦ 1）</param>
        /// <returns>端点と傾き s をまとめた構造体</returns>
        public static MappingEndpoints ComputeEndpoints(float phi, float width, float s)
        {
            // s が小さすぎると r₁ が 0 を越えてしまい、関数が作れなくなります。
            // （実験設定の段階で警告を出していますが、念のためここでも下限を守ります）
            s = Mathf.Clamp(s, MinimumValidSlope(phi, width) + Epsilon, 1f);

            // v₁ = φ − w/2,  v₂ = φ + w/2      （式 2.1, 2.2）
            float v1 = phi - width / 2f;
            float v2 = phi + width / 2f;

            // r₁ = φ − w/(2s),  r₂ = φ + w/(2s) （式 2.4, 2.5）
            float r1 = phi - width / (2f * s);
            float r2 = phi + width / (2f * s);

            return new MappingEndpoints(v1, v2, r1, r2, s);
        }

        // -----------------------------------------------------------------
        // ■ 視線方向制御関数（論文 2.2節の折れ線 ＋ 2.3節のベジェ曲線）
        // -----------------------------------------------------------------

        /// <summary>
        /// 実視線方向 r から仮想視線方向 v を計算します（滑らかな視線方向制御関数）。
        /// <para>
        /// 「r がいくつなら v はいくつ」という対応（写像）で決めるので「絶対写像」と呼んでいます。
        /// 制御一定（元研究）の条件も、本研究の動的制御（s を ω で変える）も、この関数で v を計算します。
        /// </para>
        /// <para>
        /// 関数の形（φ &gt; 0 の場合）：
        ///   ・r ≦ 0（見どころと反対側）       … v = r（制御なし）
        ///   ・0 ≦ r ≦ 90                       … (0,0) → P₁(r₁,v₁) → P₂(r₂,v₂) → (90,90) を結ぶ折れ線で、
        ///                                        P₁ と P₂ の近くだけを三次ベジェ曲線で滑らかにしたもの
        ///   ・|r| ＞ 90                         … v = r（制御なし）
        /// φ &lt; 0（見どころが左）のときは、左右を反転して同じ計算をします。
        /// </para>
        /// <para>
        /// 注意（既知の挙動）：動的制御では、頭を急に止めて s が 1 に戻るときに、
        /// r が変わっていなくても v が変わる（視界が少し戻る）ことがあります。
        /// 研究の焦点は「見どころ制御の動的変化」なので、この挙動は実験前に被験者へ口頭で伝えて対応します。
        /// </para>
        /// </summary>
        /// <param name="r">実視線方向 r [度]（−180〜180）</param>
        /// <param name="phi">見どころ方向 φ [度]（正なら右、負なら左）</param>
        /// <param name="width">見どころ範囲 w [度]</param>
        /// <param name="s">傾き s</param>
        /// <param name="bezierLength">ベジェ曲線で置き換える長さ L [度]。0 なら角のある折れ線（論文 2.2節）</param>
        /// <returns>仮想視線方向 v [度]</returns>
        public static float MapAbsolute(float r, float phi, float width, float s, float bezierLength)
        {
            // ① 90°より外側は制御なし（元研究のコードではここで NaN になっていた）
            if (Mathf.Abs(r) > MaxAngle) return r;

            // ② 見どころが左（φ < 0）なら、左右を反転して「見どころが右」の計算に置き換える。
            //    例：φ = −45 で r = −30 のときは、φ = 45・r = 30 で計算して、符号を戻す（v = −36.75 など）
            if (phi < 0f) return -MapAbsolute(-r, -phi, width, s, bezierLength);

            // ③ 見どころと反対側（r ≦ 0）は制御なし。(−90, −90) から (0, 0) までの v = r の直線。
            if (r <= 0f) return r;

            // ④ s = 1 は制御なし（折れ線がまっすぐな v = r になる）
            if (s >= 1f) return r;

            MappingEndpoints e = ComputeEndpoints(phi, width, s);

            // ⑤ 折れ点 P₁, P₂ の近くなら、ベジェ曲線の上の値を使う（論文 2.3節）
            if (bezierLength > 0f)
            {
                var origin = new Vector2(0f, 0f);
                var p1 = new Vector2(e.R1, e.V1);
                var p2 = new Vector2(e.R2, e.V2);
                var end = new Vector2(MaxAngle, MaxAngle);

                if (TryEvaluateRoundedCorner(r, origin, p1, p2, bezierLength, out float v1Curve)) return v1Curve;
                if (TryEvaluateRoundedCorner(r, p1, p2, end, bezierLength, out float v2Curve)) return v2Curve;
            }

            // ⑥ それ以外は、角のある折れ線の式（論文 2.2節、スライド5の3つの式）
            return MapPolyline(r, phi, e);
        }

        /// <summary>
        /// 0°〜90° の範囲で、角のある折れ線の式を計算します（論文 2.2節、スライド5の3つの式そのもの）。
        /// </summary>
        /// <param name="r">実視線方向 r [度]（0〜90）</param>
        private static float MapPolyline(float r, float phi, MappingEndpoints e)
        {
            if (r < e.R1)
            {
                // 区間①：0 ≦ r < r₁ のとき   v = r · v₁ / r₁   （(0,0) と P₁ を結ぶ直線。傾き > 1 で視線移動を促進）
                return r * e.V1 / e.R1;
            }

            if (r <= e.R2)
            {
                // 区間②：r₁ ≦ r ≦ r₂ のとき   v = s(r − φ) + φ   （論文の式 2.3。傾き s < 1 で視線移動を抑制）
                return e.S * (r - phi) + phi;
            }

            // 区間③：r₂ < r ≦ 90 のとき（P₂ と (90,90) を結ぶ直線。傾き > 1 で促進）
            //   v = r · (90 − v₂)/(90 − r₂) − 90(r₂ − v₂)/(90 − r₂)
            float denominator = MaxAngle - e.R2;
            return r * (MaxAngle - e.V2) / denominator
                   - MaxAngle * (e.R2 - e.V2) / denominator;
        }

        /// <summary>
        /// 折れ点 corner の角を三次ベジェ曲線で丸めた部分で、r に対応する v を求めます（論文 2.3節）。
        /// <para>
        /// 制御点の置き方（論文の図 2.5）：
        ///   Q₀ … 折れ点から「手前の線分」に沿って L だけ戻った点（曲線の始点）
        ///   Q₃ … 折れ点から「先の線分」に沿って L だけ進んだ点（曲線の終点）
        ///   Q₁ … Q₀ と折れ点の中点
        ///   Q₂ … Q₃ と折れ点の中点
        /// 曲線は Q₀ で手前の線分に、Q₃ で先の線分に、傾きまでなめらかにつながります。
        /// </para>
        /// </summary>
        /// <param name="r">実視線方向 r [度]</param>
        /// <param name="previous">手前の線分の始まりの点</param>
        /// <param name="corner">丸める折れ点（P₁ または P₂）</param>
        /// <param name="next">先の線分の終わりの点</param>
        /// <param name="length">ベジェ曲線で置き換える長さ L</param>
        /// <param name="v">求めた仮想視線方向 v（曲線の範囲外なら 0）</param>
        /// <returns>r が曲線の範囲（Q₀〜Q₃）に入っていれば true</returns>
        private static bool TryEvaluateRoundedCorner(float r, Vector2 previous, Vector2 corner, Vector2 next,
            float length, out float v)
        {
            v = 0f;
            Vector2 toCorner = corner - previous; // 手前の線分（の向きと長さ）
            Vector2 fromCorner = next - corner;   // 先の線分
            float lengthBefore = toCorner.magnitude;
            float lengthAfter = fromCorner.magnitude;
            if (lengthBefore < Epsilon || lengthAfter < Epsilon) return false;

            // L が線分より長いと、隣の角の曲線と重なったり線分からはみ出したりするので、
            // 線分の長さの半分までに抑えます（ふつうの設定ではここで短くなることはありません）。
            float l = Mathf.Min(length, lengthBefore * 0.5f, lengthAfter * 0.5f);

            // 制御点 Q₀〜Q₃（Vector2 の x が r、y が v）
            Vector2 q0 = corner - toCorner / lengthBefore * l;
            Vector2 q3 = corner + fromCorner / lengthAfter * l;
            Vector2 q1 = (q0 + corner) / 2f;
            Vector2 q2 = (q3 + corner) / 2f;

            if (r < q0.x || r > q3.x) return false; // 曲線の範囲外

            // 式 (2.6)：r(t) = (1−t)³r′₀ + 3(1−t)²t r′₁ + 3(1−t)t² r′₂ + t³ r′₃ （v(t) も同じ形）
            // r(t) は t が増えると必ず増える（Q₀〜Q₃ の r がこの順に並んでいるため）ので、
            // 「r(t) = r となる t」を二分法で探します。
            //   二分法：答えがある範囲 [lo, hi] を毎回半分にしていく方法。40 回で範囲は 1兆分の1 になる。
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2f;
                if (Bezier(q0.x, q1.x, q2.x, q3.x, mid) < r) lo = mid;
                else hi = mid;
            }
            float t = (lo + hi) / 2f;

            v = Bezier(q0.y, q1.y, q2.y, q3.y, t);
            return true;
        }

        /// <summary>
        /// 三次ベジェ曲線の式 (2.6) の1成分を計算します。
        /// B(t) = (1−t)³p₀ + 3(1−t)²t p₁ + 3(1−t)t² p₂ + t³ p₃
        /// </summary>
        private static float Bezier(float p0, float p1, float p2, float p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        // -----------------------------------------------------------------
        // ■ スライド7：角速度 ω から傾き s を求める
        // -----------------------------------------------------------------

        /// <summary>
        /// 角速度 ω から見どころ範囲内の傾き s を計算します（スライド7）。
        /// <para>
        ///   |ω| ≦ ω₀ のとき： s = −|ω|(1 − s₀)/ω₀ + 1  （ω=0 で s=1、ω=ω₀ で s=s₀ になる直線）
        ///   |ω| ＞ ω₀ のとき： s = s₀                  （それ以上は変化しない）
        /// </para>
        /// <para>s = 1 は「制御なし」と同じ、s = s₀ は「元にした研究と同じ強さの制御」です。</para>
        /// </summary>
        /// <param name="omega">角速度 ω [度/秒]（符号はどちらでもよい。内部で絶対値を使う）</param>
        /// <param name="omega0">ω₀ [度/秒]</param>
        /// <param name="s0">s の最小値 s₀</param>
        public static float SlopeFromOmega(float omega, float omega0, float s0)
        {
            float absOmega = Mathf.Abs(omega);
            if (omega0 <= 0f || absOmega >= omega0)
            {
                return s0;
            }
            return -absOmega * (1f - s0) / omega0 + 1f;
        }

        /// <summary>
        /// 「制御強度」c を計算します。c = (1 − s) / (1 − s₀)
        /// <para>
        ///   c = 0 … 制御なし（s = 1）
        ///   c = 1 … 元にした研究と同じ強さ（s = s₀）
        /// スライドの「視線方向制御の影響の強さ」を一言で表すための用語として使えます。
        /// </para>
        /// </summary>
        public static float ControlStrength(float s, float s0)
        {
            if (s0 >= 1f) return 0f; // s₀ = 1 のときは制御そのものが無い
            return Mathf.Clamp01((1f - s) / (1f - s0));
        }

        // -----------------------------------------------------------------
        // ■ パラメータの妥当性チェック
        // -----------------------------------------------------------------

        /// <summary>
        /// 折れ線が成り立つための s の下限を返します。
        /// <para>
        /// （φ &gt; 0 のとき）r₁ = φ − w/(2s) ＞ 0  かつ  r₂ = φ + w/(2s) ＜ 90  が必要なので、
        /// s ＞ w / (2·min(|φ|, 90 − |φ|)) でなければなりません。φ &lt; 0 のときも左右反転して同じです。
        /// 例：φ = 45, w = 20 なら s ＞ 0.222…
        /// </para>
        /// </summary>
        public static float MinimumValidSlope(float phi, float width)
        {
            float absPhi = Mathf.Abs(phi);
            float margin = Mathf.Min(absPhi, MaxAngle - absPhi); // 見どころ方向から 0° または 90° までの近い方の距離
            if (margin <= 0f) return 1f;
            return width / (2f * margin);
        }

        /// <summary>
        /// パラメータの組み合わせが正しいかを調べます。
        /// </summary>
        /// <param name="bezierLength">ベジェ曲線で置き換える長さ L</param>
        /// <param name="message">問題がある場合、その理由（日本語）。問題なければ空文字。</param>
        /// <returns>正しければ true</returns>
        public static bool Validate(float phi, float width, float s0, float bezierLength, out string message)
        {
            float absPhi = Mathf.Abs(phi);
            if (absPhi <= 0f || absPhi >= MaxAngle)
            {
                message = $"見どころ方向 φ={phi} は −90〜90 の間（0 以外）にしてください。正なら右、負なら左です。";
                return false;
            }
            if (width <= 0f || width / 2f >= Mathf.Min(absPhi, MaxAngle - absPhi))
            {
                message = $"見どころ範囲 w={width} が大きすぎます。w/2 が |φ| と (90−|φ|) の両方より小さくなるようにしてください。";
                return false;
            }
            if (s0 <= 0f || s0 > 1f)
            {
                message = $"s₀={s0} は 0 より大きく 1 以下にしてください。";
                return false;
            }
            float minS = MinimumValidSlope(phi, width);
            if (s0 <= minS)
            {
                message = $"s₀={s0} が小さすぎます。φ={phi}, w={width} のとき s₀ は {minS:F3} より大きくする必要があります（r₁ が 0° を越えてしまうため）。";
                return false;
            }
            if (bezierLength < 0f)
            {
                message = $"L={bezierLength} は 0 以上にしてください（0 なら角のある折れ線）。";
                return false;
            }

            // L は、丸める角につながる線分の長さの半分より短い必要がある（長いと内部で短くされる）。
            // 線分が一番短くなるのは、(0,0)–P₁ と P₂–(90,90) は s = s₀ のとき、P₁–P₂ は s = 1 のとき。
            MappingEndpoints e = ComputeEndpoints(absPhi, width, s0);
            float shortest = Mathf.Min(
                new Vector2(e.R1, e.V1).magnitude,                              // (0,0)–P₁
                new Vector2(MaxAngle - e.R2, MaxAngle - e.V2).magnitude,        // P₂–(90,90)
                width * Mathf.Sqrt(2f));                                        // P₁–P₂（s = 1 のとき）
            if (bezierLength > shortest / 2f)
            {
                message = $"L={bezierLength} が大きすぎます。この φ, w, s₀ では {shortest / 2f:F1} 以下にしてください（曲線が線分からはみ出すため）。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        // -----------------------------------------------------------------
        // ■ 角度の便利関数
        // -----------------------------------------------------------------

        /// <summary>
        /// 角度を -180°〜180° の範囲に収めます。
        /// 例：190° → -170°、-200° → 160°
        /// <para>
        /// Mathf.DeltaAngle(a, b) は「a から b へ回るときの最短の角度差」を返す Unity の関数です。
        /// 359° と 1° の差を「-358°」ではなく正しく「+2°」と計算してくれます。
        /// </para>
        /// </summary>
        public static float WrapAngle(float angle)
        {
            return Mathf.DeltaAngle(0f, angle);
        }

        /// <summary>
        /// 回転（Quaternion）から「水平面での向き（ヨー角）」だけを取り出します [度]。
        /// <para>
        /// Quaternion（クォータニオン）は Unity で3次元の回転を表す型です。
        /// ここでは、回転させた「正面ベクトル」を真上から見て、何度の方向を向いているかを計算しています。
        /// 真上や真下を向いているときは水平方向が決まらないので、fallback（前回の値）を返します。
        /// </para>
        /// </summary>
        /// <param name="rotation">頭の回転</param>
        /// <param name="fallback">ヨー角が決められないときに返す値</param>
        public static float YawOf(Quaternion rotation, float fallback)
        {
            Vector3 forward = rotation * Vector3.forward;      // 頭の正面方向のベクトル
            float horizontal = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
            if (horizontal < 1e-3f) return fallback;          // ほぼ真上/真下を向いている
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg; // +Z 方向を 0°、右回りをプラスとする角度
        }

        /// <summary>
        /// 回転から「上下の向き（ピッチ角）」を取り出します [度]。上を向くとプラス。
        /// </summary>
        public static float PitchOf(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
