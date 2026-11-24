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
// 【対応するスライド】
//   スライド4 … 見どころ範囲の端点 v₁, v₂, r₁, r₂ の計算
//   スライド5 … 実視線方向 r から仮想視線方向 v を求める折れ線の式（方式A）
//   スライド7 … 角速度 ω から傾き s を求める式
//   （追加）  … 方式B（増分ゲイン方式）：頭の動き Δr にゲイン g をかける
//
// 【記号と変数名の対応】
//   r   … 実視線方向（HMD の実際の向き）      → 変数名 r / realYaw   （単位：度）
//   v   … 仮想視線方向（画面に映る向き）      → 変数名 v / virtualYaw（単位：度）
//   φ   … 見どころ方向                        → phi                  （単位：度）
//   w   … 見どころ範囲（仮想視線方向上の幅）  → width                （単位：度）
//   s   … 見どころ範囲内の線分の傾き          → s                    （単位なし）
//   s₀  … s の最小値                          → s0
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
    /// 折れ線の「折れ曲がる点」と「各区間の傾き」をまとめた入れ物（構造体）です。
    /// <para>
    /// 構造体（struct）は、いくつかの値をひとまとめにして持ち運ぶための仕組みです。
    /// 関数の戻り値として複数の値を一度に返したいときに便利です。
    /// </para>
    /// </summary>
    public readonly struct MappingEndpoints
    {
        /// <summary>仮想視線方向における見どころ範囲の左端 v₁ = φ − w/2 [度]</summary>
        public readonly float V1;

        /// <summary>仮想視線方向における見どころ範囲の右端 v₂ = φ + w/2 [度]</summary>
        public readonly float V2;

        /// <summary>実視線方向における見どころ範囲の左端 r₁ = φ − w/(2s) [度]</summary>
        public readonly float R1;

        /// <summary>実視線方向における見どころ範囲の右端 r₂ = φ + w/(2s) [度]</summary>
        public readonly float R2;

        /// <summary>傾き s（見どころ範囲内の線分の傾き）</summary>
        public readonly float S;

        /// <summary>
        /// 区間①（0 ≦ r &lt; r₁）の傾き = v₁ / r₁。
        /// 見どころより手前の区間で、ここでは視界が頭より「速く」回ります（傾き &gt; 1）。
        /// </summary>
        public readonly float GainBefore;

        /// <summary>
        /// 区間③（r₂ &lt; r ≦ 90）の傾き = (90 − v₂) / (90 − r₂)。
        /// 見どころを過ぎた区間で、ここでも視界が頭より速く回ります（傾き &gt; 1）。
        /// </summary>
        public readonly float GainAfter;

        public MappingEndpoints(float v1, float v2, float r1, float r2, float s)
        {
            V1 = v1;
            V2 = v2;
            R1 = r1;
            R2 = r2;
            S = s;
            GainBefore = v1 / r1;
            GainAfter = (HighlightMapping.MaxAngle - v2) / (HighlightMapping.MaxAngle - r2);
        }
    }

    /// <summary>
    /// 視線方向制御の数式をまとめた static クラスです。
    /// <para>
    /// static クラスは「インスタンス（new で作る実体）を作らずに使う関数の集まり」です。
    /// 例：<c>HighlightMapping.MapAbsolute(30f, 45f, 20f, 0.5f, true)</c> のように直接呼べます。
    /// </para>
    /// </summary>
    public static class HighlightMapping
    {
        /// <summary>
        /// 制御をかける範囲の端の角度（90°）。
        /// スライド5の式に出てくる「90」です。0°〜90°の間で折れ線を作り、
        /// それより外側（|r| &gt; 90°）は制御なし（v = r）にします。
        /// </summary>
        public const float MaxAngle = 90f;

        /// <summary>
        /// 0 で割ってしまうのを防ぐための、ごく小さな余裕の値です。
        /// </summary>
        private const float Epsilon = 1e-4f;

        // -----------------------------------------------------------------
        // ■ スライド4：端点の計算
        // -----------------------------------------------------------------

        /// <summary>
        /// 見どころ範囲の端点 v₁, v₂, r₁, r₂ を計算します（スライド4）。
        /// </summary>
        /// <param name="phi">見どころ方向 φ [度]（0〜90の範囲）</param>
        /// <param name="width">見どころ範囲 w [度]</param>
        /// <param name="s">見どころ範囲内の線分の傾き s（0 &lt; s ≦ 1）</param>
        /// <returns>端点と各区間の傾きをまとめた構造体</returns>
        public static MappingEndpoints ComputeEndpoints(float phi, float width, float s)
        {
            // s が小さすぎると r₁ が 0 以下になり、折れ線が作れなくなります。
            // （実験設定の段階で警告を出していますが、念のためここでも下限を守ります）
            s = Mathf.Clamp(s, MinimumValidSlope(phi, width) + Epsilon, 1f);

            // v₁ = φ − w/2,  v₂ = φ + w/2
            float v1 = phi - width / 2f;
            float v2 = phi + width / 2f;

            // r₁ = φ − w/(2s),  r₂ = φ + w/(2s)
            float r1 = phi - width / (2f * s);
            float r2 = phi + width / (2f * s);

            return new MappingEndpoints(v1, v2, r1, r2, s);
        }

        // -----------------------------------------------------------------
        // ■ スライド5：折れ線の式（方式A：絶対写像）
        // -----------------------------------------------------------------

        /// <summary>
        /// 【方式A】実視線方向 r から仮想視線方向 v を直接計算します（スライド5の式）。
        /// <para>
        /// 「r がいくつなら v はいくつ」という対応表（写像）を使う方法なので「絶対写像」と呼びます。
        /// 元にした研究（制御一定の条件）もこの方法です。
        /// </para>
        /// <para>
        /// 注意：動的制御でこの方式を使うと、頭が止まって s が 1 に戻るときに、
        /// r が変わっていなくても v が変わる（視界が勝手に動く）ことがあります。
        /// </para>
        /// </summary>
        /// <param name="r">実視線方向 r [度]（-180〜180）</param>
        /// <param name="phi">見どころ方向 φ [度]</param>
        /// <param name="width">見どころ範囲 w [度]</param>
        /// <param name="s">傾き s</param>
        /// <param name="symmetric">
        /// true なら左右対称に制御します（r が負の側にも −φ の位置に見どころ範囲を作る）。
        /// false なら 0°〜90° の範囲だけ制御し、負の側は制御なしです。
        /// </param>
        /// <returns>仮想視線方向 v [度]</returns>
        public static float MapAbsolute(float r, float phi, float width, float s, bool symmetric)
        {
            if (symmetric)
            {
                // 左右対称の場合：
                //   |r| ≦ 90° なら、r の絶対値で折れ線を計算し、もとの符号（プラス/マイナス）を付け直す。
                //   例：r = −30° → |r| = 30° で計算して v = 37.5° → 符号を戻して v = −37.5°
                if (Mathf.Abs(r) <= MaxAngle)
                {
                    float sign = r < 0f ? -1f : 1f;
                    return sign * MapHalf(Mathf.Abs(r), phi, width, s);
                }
                return r; // 90°より外側は制御なし
            }

            // 左右対称にしない場合：0°〜90°だけ制御する
            if (r >= 0f && r <= MaxAngle)
            {
                return MapHalf(r, phi, width, s);
            }
            return r;
        }

        /// <summary>
        /// 0°〜90° の範囲だけで折れ線の式を計算します（スライド5の3つの式そのもの）。
        /// </summary>
        /// <param name="r">実視線方向 r [度]（0〜90）</param>
        private static float MapHalf(float r, float phi, float width, float s)
        {
            MappingEndpoints e = ComputeEndpoints(phi, width, s);

            if (r < e.R1)
            {
                // 区間①：r < r₁ のとき   v = r · v₁ / r₁
                return r * e.V1 / e.R1;
            }

            if (r <= e.R2)
            {
                // 区間②：r₁ ≦ r ≦ r₂ のとき   v = s(r − φ) + φ
                return e.S * (r - phi) + phi;
            }

            // 区間③：r₂ < r のとき
            //   v = r · (90 − v₂)/(90 − r₂) − 90(r₂ − v₂)/(90 − r₂)
            float denominator = MaxAngle - e.R2;
            return r * (MaxAngle - e.V2) / denominator
                   - MaxAngle * (e.R2 - e.V2) / denominator;
        }

        // -----------------------------------------------------------------
        // ■ 方式B：増分ゲイン方式
        // -----------------------------------------------------------------

        /// <summary>
        /// 【方式B】頭の回転量 Δr にゲイン（倍率）g をかけて、仮想視線方向 v を少しずつ更新します。
        /// <para>
        /// 考え方：v_new = v_old + g(v_old) × Δr
        /// g は「いま v が折れ線のどの区間にいるか」で決まる、その区間の傾きです。
        /// 　区間①（0〜v₁）   … g = v₁ / r₁
        /// 　区間②（v₁〜v₂）  … g = s
        /// 　区間③（v₂〜90） … g = (90 − v₂) / (90 − r₂)
        /// 　90°より外側       … g = 1（制御なし）
        /// </para>
        /// <para>
        /// この方式の良いところは、頭が止まっていれば Δr = 0 なので、
        /// s がどう変わっても v は絶対に動かないことです（方式Aの「視界が勝手に動く」問題が起きない）。
        /// s が一定のまま 0°→90° と回せば、方式Aとまったく同じ v になります（テストで確認しています）。
        /// </para>
        /// <para>
        /// 1回の Δr で区間の境目をまたぐ場合は、境目までと境目から先に分けて計算します。
        /// （例：区間①の傾き 1.4 で境目まで進み、残りの Δr を区間②の傾き 0.5 で進める）
        /// </para>
        /// </summary>
        /// <param name="v">いまの仮想視線方向 v [度]</param>
        /// <param name="deltaR">このフレームでの頭の回転量 Δr [度]（右回りがプラス）</param>
        /// <param name="phi">見どころ方向 φ [度]</param>
        /// <param name="width">見どころ範囲 w [度]</param>
        /// <param name="s">傾き s</param>
        /// <param name="symmetric">左右対称にするかどうか（MapAbsolute と同じ意味）</param>
        /// <returns>更新後の仮想視線方向 v [度]（-180〜180 に収めた値）</returns>
        public static float StepIncremental(float v, float deltaR, float phi, float width, float s, bool symmetric)
        {
            MappingEndpoints e = ComputeEndpoints(phi, width, s);

            // ① 区間の境目（仮想視線方向 v の値）と、各区間の傾きを表にします。
            //    breakpoints[i] 〜 breakpoints[i+1] の区間の傾きが gains[i] です。
            float[] breakpoints;
            float[] gains;
            if (symmetric)
            {
                // -180 … -90 … -v₂ … -v₁ … v₁ … v₂ … 90 … 180
                breakpoints = new[] { -180f, -MaxAngle, -e.V2, -e.V1, e.V1, e.V2, MaxAngle, 180f };
                gains = new[] { 1f, e.GainAfter, e.S, e.GainBefore, e.S, e.GainAfter, 1f };
            }
            else
            {
                // -180 … 0 … v₁ … v₂ … 90 … 180（負の側は制御なし）
                breakpoints = new[] { -180f, 0f, e.V1, e.V2, MaxAngle, 180f };
                gains = new[] { 1f, e.GainBefore, e.S, e.GainAfter, 1f };
            }

            // ② 残りの回転量がなくなるまで、区間ごとに少しずつ進めます。
            float remaining = deltaR;   // まだ v に反映していない頭の回転量 [度]
            v = WrapAngle(v);

            // 無限ループを防ぐため、最大の繰り返し回数を決めておきます（区間数より十分多い値）
            for (int guard = 0; guard < 32 && Mathf.Abs(remaining) > 1e-6f; guard++)
            {
                int direction = remaining > 0f ? 1 : -1; // 右回り(+1) か 左回り(-1) か

                // ±180° に到達したら反対側（∓180°）につなげる（角度は一周するため）
                if (direction > 0 && v >= 180f) v -= 360f;
                if (direction < 0 && v <= -180f) v += 360f;

                // ③ いまの v がどの区間にいるかを探します。
                //    ちょうど境目にいるときは「これから進む向きの区間」を選びます。
                int index = FindSegment(breakpoints, v, direction);
                float gain = gains[index];
                float boundary = direction > 0 ? breakpoints[index + 1] : breakpoints[index];

                // ④ この区間の傾きで全部進めたら、どこまで行くかを計算します。
                float wantedDeltaV = gain * remaining;
                bool staysInSegment = direction > 0
                    ? v + wantedDeltaV <= boundary
                    : v + wantedDeltaV >= boundary;

                if (staysInSegment)
                {
                    // 区間の中に収まる → そのまま進めて終わり
                    v += wantedDeltaV;
                    remaining = 0f;
                }
                else
                {
                    // 境目をまたぐ → 境目まで進め、使った分の Δr を差し引いて次の区間へ
                    float deltaVToBoundary = boundary - v;
                    remaining -= deltaVToBoundary / gain;
                    v = boundary;
                }
            }

            return WrapAngle(v);
        }

        /// <summary>
        /// v がどの区間に入っているか（breakpoints の何番目の区間か）を返します。
        /// </summary>
        private static int FindSegment(float[] breakpoints, float v, int direction)
        {
            int last = breakpoints.Length - 2; // 区間の数 − 1
            for (int i = 0; i <= last; i++)
            {
                float lo = breakpoints[i];
                float hi = breakpoints[i + 1];
                // 右回りなら [lo, hi)、左回りなら (lo, hi] に入っているかを調べる
                bool inside = direction > 0 ? (v >= lo && v < hi) : (v > lo && v <= hi);
                if (inside) return i;
            }
            return direction > 0 ? last : 0;
        }

        /// <summary>
        /// 方式Bで、仮想視線方向 v の位置での傾き（ゲイン）g を返します。
        /// デバッグ表示やログ用です。境目では右回り側の区間の値を返します。
        /// </summary>
        public static float GainAt(float v, float phi, float width, float s, bool symmetric)
        {
            MappingEndpoints e = ComputeEndpoints(phi, width, s);
            float a = Mathf.Abs(v);
            if (!symmetric && v < 0f) return 1f;
            if (a >= MaxAngle) return 1f;
            if (a < e.V1) return e.GainBefore;
            if (a < e.V2) return e.S;
            return e.GainAfter;
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
        /// r₁ = φ − w/(2s) ＞ 0  かつ  r₂ = φ + w/(2s) ＜ 90  が必要なので、
        /// s ＞ w / (2·min(φ, 90 − φ)) でなければなりません。
        /// 例：φ = 45, w = 20 なら s ＞ 0.222…
        /// </para>
        /// </summary>
        public static float MinimumValidSlope(float phi, float width)
        {
            float margin = Mathf.Min(phi, MaxAngle - phi); // 見どころ方向から 0° または 90° までの近い方の距離
            if (margin <= 0f) return 1f;
            return width / (2f * margin);
        }

        /// <summary>
        /// パラメータの組み合わせが正しいかを調べます。
        /// </summary>
        /// <param name="message">問題がある場合、その理由（日本語）。問題なければ空文字。</param>
        /// <returns>正しければ true</returns>
        public static bool Validate(float phi, float width, float s0, out string message)
        {
            if (phi <= 0f || phi >= MaxAngle)
            {
                message = $"見どころ方向 φ={phi} は 0〜90 の間にしてください。";
                return false;
            }
            if (width <= 0f || width / 2f >= Mathf.Min(phi, MaxAngle - phi))
            {
                message = $"見どころ範囲 w={width} が大きすぎます。w/2 が φ と (90−φ) の両方より小さくなるようにしてください。";
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
                message = $"s₀={s0} が小さすぎます。φ={phi}, w={width} のとき s₀ は {minS:F3} より大きくする必要があります（r₁ が 0° 以下になってしまうため）。";
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
