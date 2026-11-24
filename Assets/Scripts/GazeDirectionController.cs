// =====================================================================
// GazeDirectionController.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   このシステムの「心臓部」です。毎フレーム次の処理を行います。
//
//     ① HMD の向きを読む                     （HeadPoseSource）
//     ② 実視線方向 r を求める                 （開始時の正面を 0° とした角度）
//     ③ 角速度 ω を求める                     （AngularVelocityEstimator：直近 N フレームの単純平均。元研究と同じ）
//     ④ 傾き s を決める                       （条件によって 1 / s₀ / s(ω)）
//     ⑤ 仮想視線方向 v を求める               （HighlightMapping：スライド5の折れ線）
//     ⑥ カメラを「v の方向」に向ける
//
// 【カメラの向きの決め方（⑥の詳しい説明）】
//   HMD をかぶった人が右に 30° 向いたとき（r = 30°）、
//   画面には「右に v° 向いたときの景色」を映したい、というのが視線方向制御です。
//   そこで、カメラの回転を次のように作ります。
//
//     カメラの回転 = （ヨー方向に (v − r) だけ回す） × （HMD の実際の回転）
//
//   すると、カメラの左右の向きは r + (v − r) = v になります。
//   上下（ピッチ）と首のかしげ（ロール）は HMD のまま変わりません。
//
// 【いつ処理するか（Unity の実行順の話）】
//   Unity では1フレームの中で、だいたい次の順に関数が呼ばれます。
//     Update（全スクリプト） → LateUpdate（全スクリプト） → 描画の直前（BeforeRender） → 描画
//   ・LateUpdate：ω を計算して s を決め、v を計算します（1フレームに1回）。
//   ・BeforeRender：描画の直前に HMD の最新の向きがもう一度届くので、
//     同じ s のまま r と v だけ計算し直し、カメラに反映します。
//     こうすると、頭の動きと映像のズレ（遅延）が小さくなり、酔いにくくなります。
//     （HMD が無い模擬操作のときは BeforeRender の更新は来ないので LateUpdate だけで動きます）
//
//   [DefaultExecutionOrder(-100)] は「他のスクリプトより先に実行する」という指定です。
//   これにより、ExperimentFlow（ログ記録）より先に、このフレームの r, v, ω, s が計算されます。
//
// 【他のファイルとの関係】
//   ・ExperimentConfig     … パラメータ（φ, w, s₀, ω₀, N）を読む
//   ・HeadPoseSource       … HMD の向きを読む
//   ・HighlightMapping     … 数式（スライド4・5・7）
//   ・AngularVelocityEstimator … 角速度 ω の推定
//   ・ExperimentFlow       … 条件の切り替え（SetCondition）とリセンター（Recenter）を呼ぶ
//   ・ExperimentLogger / DebugHud … RealYaw, VirtualYaw, Omega, Slope などを読む
// =====================================================================

using DynamicHighlights.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DynamicHighlights
{
    /// <summary>
    /// HMD の向きから仮想視線方向 v を計算し、カメラに反映するコンポーネントです。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GazeDirectionController : MonoBehaviour
    {
        // =================================================================
        // Inspector で設定する項目
        // =================================================================

        [Header("■ 参照（シーン作成メニューで自動設定されます）")]

        [Tooltip("実験パラメータの設定ファイル")]
        public ExperimentConfig config;

        [Tooltip("頭の向きを取得するコンポーネント")]
        public HeadPoseSource headPose;

        [Tooltip("向きを制御するカメラの Transform")]
        public Transform viewCamera;

        [Header("■ 現在の条件")]

        [Tooltip("視聴条件。実験中は ExperimentFlow が自動で切り替えます。動作確認のときは手動で変えてもOK。")]
        public ViewingCondition condition = ViewingCondition.NoControl;

        // =================================================================
        // 外から読める「いまの状態」（プロパティ）
        // { get; private set; } は「外からは読むだけ、書き換えはこのクラスの中だけ」という意味です。
        // =================================================================

        /// <summary>実視線方向 r [度]（開始時の正面が 0°、右回りがプラス、-180〜180）</summary>
        public float RealYaw { get; private set; }

        /// <summary>仮想視線方向 v [度]（画面に映っている向き）</summary>
        public float VirtualYaw { get; private set; }

        /// <summary>角速度 ω [度/秒]</summary>
        public float Omega { get; private set; }

        /// <summary>見どころ範囲内の傾き s（1 = 制御なし）</summary>
        public float Slope { get; private set; } = 1f;

        /// <summary>制御強度 c = (1 − s)/(1 − s₀)（0 = 制御なし、1 = 元研究と同じ強さ）</summary>
        public float ControlStrength => condition == ViewingCondition.NoControl
            ? 0f
            : HighlightMapping.ControlStrength(Slope, config != null ? config.S0 : 1f);

        /// <summary>上下の向き（ピッチ角）[度]。上を向くとプラス。</summary>
        public float Pitch { get; private set; }

        /// <summary>v − r：実際の向きと画面の向きのズレ [度]</summary>
        public float Offset => HighlightMapping.WrapAngle(VirtualYaw - RealYaw);

        // =================================================================
        // 内部で使う変数
        // =================================================================

        private AngularVelocityEstimator _estimator; // 角速度 ω を推定する計算機
        private float _startYawRaw;   // リセンターしたときの HMD のヨー角（これを r = 0° とする）
        private float _lastRawYaw;    // 前回の HMD のヨー角（真上を向いたときの予備）
        private bool _needsRecenter = true; // 最初のフレームで自動的にリセンターする
        private double _lastSampleTime = double.NegativeInfinity; // 最後に角速度の計算機に記録したゲーム内時刻 [秒]

        // =================================================================
        // Unity が自動で呼ぶ関数
        // =================================================================

        /// <summary>
        /// OnEnable はコンポーネントが有効になったときに呼ばれます。
        /// ここで「入力システムの更新が終わったら OnInputAfterUpdate を呼んでね」と登録します。
        /// （+= はイベントに関数を登録する書き方です）
        /// </summary>
        private void OnEnable()
        {
            CreateEstimator();
            InputSystem.onAfterUpdate += OnInputAfterUpdate;
        }

        /// <summary>
        /// OnDisable はコンポーネントが無効になったときに呼ばれます。登録を解除します（-=）。
        /// 解除しないと、オブジェクトが消えた後も呼ばれ続けてエラーになります。
        /// </summary>
        private void OnDisable()
        {
            InputSystem.onAfterUpdate -= OnInputAfterUpdate;
        }

        /// <summary>
        /// LateUpdate は、全スクリプトの Update が終わった後に毎フレーム呼ばれます。
        /// ここで ω と s を更新し、v を計算します。
        /// </summary>
        private void LateUpdate()
        {
            Step(updateVelocity: true);
        }

        /// <summary>
        /// 入力システムの更新が終わるたびに呼ばれます。
        /// 更新の種類が「BeforeRender（描画直前）」のときだけ、最新の HMD の向きで v を計算し直します。
        /// </summary>
        private void OnInputAfterUpdate()
        {
            if (InputState.currentUpdateType == InputUpdateType.BeforeRender && headPose != null && headPose.IsHmdActive)
            {
                // ω と s は LateUpdate で計算した値のまま。r と v だけ最新にする。
                Step(updateVelocity: false);
            }
        }

        // =================================================================
        // 外から呼ぶ関数
        // =================================================================

        /// <summary>
        /// リセンター：いま向いている方向を「正面（r = 0°）」にし直します。
        /// 各試行の開始前に、被験者に正面を向いてもらってから呼びます。
        /// </summary>
        public void Recenter()
        {
            _needsRecenter = true;
        }

        /// <summary>
        /// 視聴条件を切り替えます。
        /// </summary>
        public void SetCondition(ViewingCondition newCondition)
        {
            condition = newCondition;
            // 角速度 ω は「頭がどう動いているか」で決まり、条件とは関係ないので、履歴はそのまま使い続けます。
            // （消してしまうと、カメラ挙動チェックで一時停止中に条件を切り替えたとき ω が 0 になってしまう）
        }

        /// <summary>
        /// 設定（N）を変えたときに、角速度の計算機を作り直します。
        /// </summary>
        public void CreateEstimator()
        {
            int frames = config != null ? config.velocityFrameCount : 5;
            _estimator = new AngularVelocityEstimator(frames);
        }

        // =================================================================
        // メインの処理
        // =================================================================

        /// <summary>
        /// 1回分の計算を行い、カメラの向きを更新します。
        /// </summary>
        /// <param name="updateVelocity">true なら ω と s も更新する（LateUpdate のとき）</param>
        private void Step(bool updateVelocity)
        {
            if (config == null || headPose == null || viewCamera == null) return;

            // 設定ファイルの N が変わっていたら、角速度の計算機を作り直す
            if (_estimator == null || _estimator.FrameCount != Mathf.Max(1, config.velocityFrameCount))
            {
                CreateEstimator();
            }

            // ---- ① HMD の向きを読む ----
            if (!headPose.TryGetRotation(out Quaternion rawRotation)) return;

            float rawYaw = HighlightMapping.YawOf(rawRotation, _lastRawYaw); // HMD の生のヨー角
            _lastRawYaw = rawYaw;
            Pitch = HighlightMapping.PitchOf(rawRotation);

            // リセンターが必要なら、いまの向きを正面（r = 0, v = 0）にする
            if (_needsRecenter)
            {
                _startYawRaw = rawYaw;
                VirtualYaw = 0f;
                _estimator.Reset();
                Omega = 0f;
                _lastSampleTime = double.NegativeInfinity; // 次のフレームは必ず角速度の計算機に記録する
                _needsRecenter = false;
            }

            // ---- ② 実視線方向 r を求める ----
            // 「開始時の向き」から「いまの向き」への角度差。DeltaAngle で -180〜180 に収まる。
            float r = Mathf.DeltaAngle(_startYawRaw, rawYaw);
            RealYaw = r;

            // ---- ③ 角速度 ω を求める（1フレームに1回）----
            if (updateVelocity)
            {
                // Time.timeAsDouble：ゲーム開始からの「ゲーム内時間」[秒]。
                // ふだん（Time.timeScale = 1）は現実の時間と同じです。
                // カメラ挙動チェックのスロー再生（Time.timeScale を 0.25 などにする）では、
                // ゲーム内時間がゆっくり進むので、映像はゆっくりでも ω は「本来の速さ」で計算されます。
                // （現実の時間 Time.unscaledTimeAsDouble を使うと、スロー再生中は ω も遅くなってしまう）
                double now = Time.timeAsDouble;

                // ゲーム内時間が進んでいないフレーム（カメラ挙動チェックの一時停止中など）では、
                // 角速度の計算機に記録せず、ω は前の値のまま保ちます。
                // もし記録してしまうと「同じ時刻のデータ」ばかりになって速さが計算できなくなり、
                // ω = 0 → s = 1 となって、止めているはずなのに視界が動いてしまいます。
                if (now > _lastSampleTime)
                {
                    _estimator.AddSample(now, r);
                    Omega = _estimator.Omega;
                    _lastSampleTime = now;
                }

                // ---- ④ 傾き s を決める ----
                // ω が同じでも、一時停止中に条件を切り替えたときに s が変わるように、毎フレーム計算し直す
                Slope = ComputeSlope(Omega);
            }

            // ---- ⑤ 仮想視線方向 v を求める ----
            VirtualYaw = ComputeVirtualYaw(r);

            // ---- ⑥ カメラを v の方向に向ける ----
            // ヨー方向に (v − r − 開始時の向き) だけ回してから、HMD の回転をかける。
            //   → カメラの左右の向き ＝ 開始時の向き + r + (v − r − 開始時の向き) ＝ v
            // Quaternion.AngleAxis(角度, 軸) は「軸のまわりに角度だけ回す回転」を作る関数。
            // クォータニオン同士の掛け算 A * B は「B の回転をしてから A の回転をする」という意味。
            float yawCorrection = VirtualYaw - r - _startYawRaw;
            viewCamera.localRotation = Quaternion.AngleAxis(yawCorrection, Vector3.up) * rawRotation;
        }

        /// <summary>
        /// 条件に応じて傾き s を決めます。
        /// </summary>
        private float ComputeSlope(float omega)
        {
            switch (condition)
            {
                case ViewingCondition.NoControl:
                    return 1f; // 制御なし
                case ViewingCondition.FixedControl:
                    return config.S0; // 元にした研究：常に s₀
                case ViewingCondition.DynamicControl:
                    return HighlightMapping.SlopeFromOmega(omega, config.Omega0, config.S0); // スライド7
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// 条件に応じて仮想視線方向 v を計算します。
        /// </summary>
        /// <param name="r">実視線方向 r [度]</param>
        private float ComputeVirtualYaw(float r)
        {
            switch (condition)
            {
                case ViewingCondition.NoControl:
                    // 通常のVR視聴：v = r
                    return r;

                case ViewingCondition.FixedControl:
                    // 元にした研究：s = s₀ 固定の絶対写像
                    return HighlightMapping.MapAbsolute(r, config.Phi, config.W, config.S0, config.L);

                case ViewingCondition.DynamicControl:
                    // 本研究：s = s(ω) の絶対写像   v = F(r; s(ω))
                    // ※ 頭を急に止めると s が 1 に戻り、r が同じでも v が少し戻る（既知の挙動。実験前に口頭で注意する）
                    return HighlightMapping.MapAbsolute(r, config.Phi, config.W, Slope, config.L);

                default:
                    return r;
            }
        }
    }
}
