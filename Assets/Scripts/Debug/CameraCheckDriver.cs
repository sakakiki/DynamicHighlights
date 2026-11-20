// =====================================================================
// CameraCheckDriver.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   テストを実行しなくても、Play 中にカメラの動きを目で見て確かめるための「動作確認用」スクリプトです。
//   次の3つの仕事をします（③は下の「スロー再生・一時停止・コマ送り」の説明も参照）。
//
//   ① 頭の動きの自動再生
//      「ゆっくり往復」「速く回して急停止」など、決まった頭の動き（パターン）を自動で再生します。
//      マウスで毎回同じ動きをするのは難しいので、同じ動きを何度でも再現できるようにしています。
//      → 条件や方式を変えて同じパターンを再生すれば、カメラの動きの違いを比べられます。
//      ※ HMD が接続されているときは自動再生できません（実際の頭の動きが優先されるため）。
//
//   ② 履歴の記録
//      直近数秒間の r, v, s, ω を記録しておきます。
//      「カメラ挙動チェック」ウィンドウ（Editor/CameraCheckWindow.cs）が、これを時間変化のグラフとして描きます。
//      HMD をかぶって動かしたときの記録もとれます。
//
//   ③ スロー再生・一時停止・コマ送り
//      映像はゆっくり（または止めて1コマずつ）動かしつつ、角速度 ω などの計算は本来の速さで行います。
//      「速く回したときに s と v がどう変わるか」を、目で追える速さで確かめられます。
//
//   さらに、Scene ビューに「実際の頭の向き r」と「画面の向き v」の線を描きます（OnDrawGizmos）。
//
// 【実行順】
//   [DefaultExecutionOrder(50)] で、HeadPoseSource（0）より後に Update し、
//   GazeDirectionController（-100）より後に LateUpdate するようにしています。
//     Update     … 自動再生の頭の向きを HeadPoseSource に設定（キーボード操作より後なので上書きできる）
//     LateUpdate … GazeDirectionController がこのフレームの v を計算した後に、履歴を記録
//
// 【他のファイルとの関係】
//   HeadPoseSource.SetSimulatedRotation() で頭の向きを設定し、
//   GazeDirectionController の値（RealYaw, VirtualYaw など）を読んで記録します。
//   実験の本番の処理（ExperimentFlow・ログ保存）には影響しません。
// =====================================================================

using System.Collections.Generic;
using DynamicHighlights.Core;
using UnityEngine;

namespace DynamicHighlights
{
    /// <summary>
    /// 頭の動きのパターンの自動再生と、r・v などの履歴の記録を行う動作確認用コンポーネントです。
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CameraCheckDriver : MonoBehaviour
    {
        // =================================================================
        // パターンの定義
        // =================================================================

        /// <summary>
        /// パターンの1つの動き（「〇〇度まで、〇〇度/秒で回して、〇〇秒止まる」）です。
        /// </summary>
        public struct MotionStep
        {
            public float TargetYaw;   // 回す先の角度 [度]（正面 0°、右がプラス）
            public float Speed;       // 回す速さ [度/秒]
            public float HoldSeconds; // 着いてから止まっている時間 [秒]
            public string Note;       // いま何をしているかの説明（ウィンドウに表示）

            public MotionStep(float targetYaw, float speed, float holdSeconds, string note)
            {
                TargetYaw = targetYaw;
                Speed = speed;
                HoldSeconds = holdSeconds;
                Note = note;
            }
        }

        /// <summary>
        /// 頭の動きのパターン（名前・説明・動きの並び）です。
        /// </summary>
        public class MotionPattern
        {
            public string Name;         // ボタンに表示する名前
            public string Description;  // 何を確かめるためのパターンか
            public MotionStep[] Steps;  // 動きの並び
        }

        /// <summary>
        /// 用意しているパターンの一覧を作ります。見どころ方向 φ に合わせて角度を決めます。
        /// </summary>
        /// <param name="phi">見どころ方向 φ [度]</param>
        public static MotionPattern[] CreatePatterns(float phi)
        {
            return new[]
            {
                new MotionPattern
                {
                    Name = "ゆっくり往復",
                    Description = "30°/秒 で 0°→90°→0° を往復します。動的制御では s がほぼ 1 のまま（制御が弱い）になることを確認します。",
                    Steps = new[]
                    {
                        new MotionStep(90f, 30f, 0.5f, "右へゆっくり回す（30°/秒）"),
                        new MotionStep(0f, 30f, 0.5f, "正面へゆっくり戻す（30°/秒）"),
                    },
                },
                new MotionPattern
                {
                    Name = "速く往復",
                    Description = "150°/秒 で 0°→90°→0° を往復します。動的制御でも s = s₀ になり、制御一定と同じ動きになることを確認します。",
                    Steps = new[]
                    {
                        new MotionStep(90f, 150f, 1f, "右へ速く回す（150°/秒）"),
                        new MotionStep(0f, 150f, 1f, "正面へ速く戻す（150°/秒）"),
                    },
                },
                new MotionPattern
                {
                    Name = "速く回して急停止",
                    Description = $"見どころの手前（{phi - 20f:F0}°）まで速く回して止まります。" +
                                  "方式A では、止まった後に画面の向き v が勝手に動く（視界が戻る）ことを確認します。方式B では止まります。",
                    Steps = new[]
                    {
                        new MotionStep(phi - 20f, 150f, 2f, $"{phi - 20f:F0}° まで速く回して止まる"),
                        new MotionStep(0f, 150f, 1f, "正面へ速く戻す"),
                    },
                },
                new MotionPattern
                {
                    Name = "速く通り過ぎてゆっくり戻る",
                    Description = "見どころを速く通り過ぎ（80°まで）、ゆっくり正面に戻ります。" +
                                  "方式B では、正面に戻っても v − r のズレが残ることを確認します。",
                    Steps = new[]
                    {
                        new MotionStep(80f, 150f, 1f, "80° まで速く回す"),
                        new MotionStep(0f, 20f, 2f, "正面へゆっくり戻す（20°/秒）"),
                    },
                },
                new MotionPattern
                {
                    Name = "見どころ付近を見回す",
                    Description = "見どころまで速く回した後、その周り（±15°）をゆっくり見回します。" +
                                  "動的制御では、ゆっくり見回すときに制御が弱まる（見どころ以外も見やすい）ことを確認します。",
                    Steps = new[]
                    {
                        new MotionStep(phi, 150f, 1f, $"見どころ（{phi:F0}°）まで速く回す"),
                        new MotionStep(phi + 15f, 15f, 0.5f, "見どころの右側をゆっくり見る"),
                        new MotionStep(phi - 15f, 15f, 0.5f, "見どころの左側をゆっくり見る"),
                        new MotionStep(phi, 15f, 1f, "見どころに戻る"),
                        new MotionStep(0f, 150f, 1f, "正面へ速く戻す"),
                    },
                },
            };
        }

        // =================================================================
        // 履歴の記録
        // =================================================================

        /// <summary>
        /// 履歴の1つ分のデータです。
        /// </summary>
        public struct Sample
        {
            public float Time;  // 記録した時刻 [秒]
            public float R;     // 実視線方向 r [度]
            public float V;     // 仮想視線方向 v [度]
            public float S;     // 傾き s
            public float Omega; // 角速度 ω [度/秒]
        }

        // =================================================================
        // Inspector で設定する項目
        // =================================================================

        public GazeDirectionController controller;
        public HeadPoseSource headPose;
        public ExperimentConfig config;

        [Tooltip("ON：パターンの最後まで行ったら最初から繰り返す")]
        public bool loop = true;

        [Tooltip("履歴を残す時間 [秒]（グラフの横幅）")]
        [Range(1f, 30f)]
        public float historySeconds = 6f;

        [Tooltip("Scene ビューに r と v の向きの線を描く")]
        public bool drawGizmos = true;

        // =================================================================
        // 状態
        // =================================================================

        /// <summary>パターンを再生中なら true</summary>
        public bool IsPlaying => _pattern != null;

        /// <summary>再生中のパターンの名前（再生していなければ空）</summary>
        public string CurrentPatternName => _pattern != null ? _pattern.Name : "";

        /// <summary>いま行っている動きの説明（再生していなければ空）</summary>
        public string CurrentStepNote =>
            _pattern != null ? _pattern.Steps[_stepIndex].Note + (_holding ? "（停止中）" : "") : "";

        /// <summary>
        /// 再生速度（1 = 等速、0.25 = 4分の1の速さのスロー再生）。
        /// </summary>
        public float PlaybackSpeed { get; private set; } = 1f;

        /// <summary>一時停止中なら true</summary>
        public bool IsPaused { get; private set; }

        /// <summary>スロー再生または一時停止をしているなら true</summary>
        public bool IsTimeChanged => IsPaused || !Mathf.Approximately(PlaybackSpeed, 1f);

        private int _framesUntilPause; // 「1コマ進む」で、あと何フレーム進めたら止めるか

        private MotionPattern _pattern; // 再生中のパターン（再生していなければ null）
        private int _stepIndex;         // いま何番目の動きか
        private float _yaw;             // 自動再生でのいまの頭の向き [度]
        private bool _holding;          // 目標の角度に着いて止まっている途中か
        private float _holdTimer;       // 止まっている時間の残り [秒]

        // 履歴（古いものから順に並ぶ）。List の先頭から古いものを消していく。
        private readonly List<Sample> _history = new List<Sample>();

        // =================================================================
        // 外から呼ぶ関数
        // =================================================================

        /// <summary>
        /// パターンを再生します。正面（0°）を向いてリセンターしてから始めます。
        /// </summary>
        /// <returns>再生を始められたら true（HMD 接続中は false）</returns>
        public bool Play(MotionPattern pattern)
        {
            if (pattern == null || headPose == null || !headPose.IsSimulating) return false;

            _pattern = pattern;
            _stepIndex = 0;
            _yaw = 0f;
            _holding = false;
            headPose.SetSimulatedRotation(0f);
            if (controller != null) controller.Recenter();
            ClearHistory();
            return true;
        }

        /// <summary>再生を止めます（頭の向きはその場で止まります）。</summary>
        public void Stop()
        {
            _pattern = null;
        }

        // -----------------------------------------------------------------
        // スロー再生・一時停止・コマ送り
        // -----------------------------------------------------------------
        //
        // 【仕組み】
        //   Unity の Time.timeScale は「ゲーム内の時間が、現実の時間の何倍の速さで進むか」です。
        //   Time.timeScale = 0.25 にすると、現実の1秒の間にゲーム内では 0.25 秒しか進みません。
        //
        //   ・自動再生の頭の動き（Time.deltaTime を使う）→ 現実の時間ではゆっくり動く ＝ 映像はスロー
        //   ・角速度 ω の計算（ゲーム内時間 Time.timeAsDouble を使う）→ 本来の速さのまま
        //       例：150°/秒 のパターンを 0.25 倍で再生しても、ω は 150°/秒 と計算される
        //
        //   つまり「頭は速く回している」ものとして s と v が計算され、その様子をゆっくり見られます。
        //   グラフの横軸もゲーム内時間なので、グラフの形は等速のときと同じになります。
        //
        // 【注意】
        //   ・Time.timeScale はシーン全体に効くので、試行中（実験の本番）は使えないようにしています。
        //   ・スロー再生中にマウスで回すと、現実の速さで動かしてもゲーム内では速く回したことになります。
        //   ・HMD 接続中は使えません（実際の頭の動きと時間の進み方がずれて、ω がおかしくなるため）。

        /// <summary>
        /// 再生速度を設定します。
        /// </summary>
        /// <param name="speed">1 = 等速、0.5 = 半分の速さ、0.25 = 4分の1 …</param>
        /// <returns>設定できたら true（HMD 接続中は false）</returns>
        public bool SetPlaybackSpeed(float speed)
        {
            if (!CanChangeTime()) return false;
            PlaybackSpeed = Mathf.Clamp(speed, 0.01f, 1f);
            ApplyTimeScale();
            return true;
        }

        /// <summary>一時停止 / 再開を切り替えます。</summary>
        public bool SetPaused(bool paused)
        {
            if (!CanChangeTime()) return false;
            IsPaused = paused;
            _framesUntilPause = 0;
            ApplyTimeScale();
            return true;
        }

        /// <summary>
        /// 一時停止中に、1フレームだけ進めます（コマ送り）。
        /// 進む時間は「現実の1フレーム分 × 再生速度」です。
        /// </summary>
        public void StepOneFrame()
        {
            if (!IsPaused || !CanChangeTime()) return;
            _framesUntilPause = 1;
            Time.timeScale = PlaybackSpeed; // 次のフレームだけ時間を進める（LateUpdate でまた 0 に戻す）
        }

        /// <summary>等速・一時停止なしに戻します。</summary>
        public void ResetTime()
        {
            PlaybackSpeed = 1f;
            IsPaused = false;
            _framesUntilPause = 0;
            Time.timeScale = 1f;
        }

        /// <summary>時間の進み方を変えてよい状態かどうか（HMD 接続中はだめ）。</summary>
        private bool CanChangeTime()
        {
            return headPose == null || !headPose.IsHmdActive;
        }

        /// <summary>いまの設定を Time.timeScale に反映します。</summary>
        private void ApplyTimeScale()
        {
            Time.timeScale = IsPaused ? 0f : PlaybackSpeed;
        }

        /// <summary>
        /// コンポーネントが無効になったとき（Play を止めたときなど）は、必ず時間の進み方を元に戻します。
        /// </summary>
        private void OnDisable()
        {
            if (IsTimeChanged) ResetTime();
        }

        /// <summary>履歴を消します。</summary>
        public void ClearHistory()
        {
            _history.Clear();
        }

        /// <summary>
        /// 履歴をコピーして渡します（グラフを描く側が使う）。
        /// </summary>
        /// <param name="output">コピー先のリスト（中身は上書きされる）</param>
        public void CopyHistory(List<Sample> output)
        {
            output.Clear();
            output.AddRange(_history);
        }

        // =================================================================
        // Unity が自動で呼ぶ関数
        // =================================================================

        private void Update()
        {
            // HMD がつながったら、スロー再生・一時停止は解除する
            if (IsTimeChanged && !CanChangeTime()) ResetTime();

            if (_pattern == null) return;

            // HMD がつながったら自動再生はやめる
            if (headPose == null || !headPose.IsSimulating)
            {
                Stop();
                return;
            }

            AdvancePattern(Time.deltaTime);
            headPose.SetSimulatedRotation(_yaw);
        }

        /// <summary>
        /// パターンを deltaTime 秒だけ進めます。
        /// </summary>
        private void AdvancePattern(float deltaTime)
        {
            MotionStep step = _pattern.Steps[_stepIndex];

            if (_holding)
            {
                // 止まっている途中：時間が来たら次の動きへ
                _holdTimer -= deltaTime;
                if (_holdTimer <= 0f) NextStep();
                return;
            }

            // 目標の角度に向かって、速さ × 時間 だけ進める
            // Mathf.MoveTowards(今, 目標, 最大の変化量) は「目標を通り越さないように近づける」関数
            _yaw = Mathf.MoveTowards(_yaw, step.TargetYaw, step.Speed * deltaTime);

            if (Mathf.Approximately(_yaw, step.TargetYaw))
            {
                _holding = true;
                _holdTimer = step.HoldSeconds;
            }
        }

        /// <summary>次の動きに進みます。最後まで行ったら、繰り返すか止めます。</summary>
        private void NextStep()
        {
            _holding = false;
            _stepIndex++;
            if (_stepIndex >= _pattern.Steps.Length)
            {
                if (loop) _stepIndex = 0;
                else Stop();
            }
        }

        /// <summary>
        /// LateUpdate：GazeDirectionController がこのフレームの値を計算した後に、履歴に記録します。
        /// </summary>
        private void LateUpdate()
        {
            // コマ送り：1フレーム進めたら、また一時停止（Time.timeScale = 0）に戻す
            if (_framesUntilPause > 0)
            {
                _framesUntilPause--;
                if (_framesUntilPause == 0 && IsPaused) Time.timeScale = 0f;
            }

            if (controller == null) return;

            // Time.time はゲーム内時間 [秒]（スロー再生中はゆっくり進み、一時停止中は止まる）
            float now = Time.time;
            var sample = new Sample
            {
                Time = now,
                R = controller.RealYaw,
                V = controller.VirtualYaw,
                S = controller.condition == ViewingCondition.NoControl ? 1f : controller.Slope,
                Omega = controller.Omega,
            };

            // 一時停止中は時間が進まないので、同じ時刻のデータを増やさずに最後の1つを置き換える
            if (_history.Count > 0 && now <= _history[_history.Count - 1].Time)
            {
                _history[_history.Count - 1] = sample;
            }
            else
            {
                _history.Add(sample);
            }

            // 古い履歴を消す（historySeconds より前のもの）
            int removeCount = 0;
            while (removeCount < _history.Count && _history[removeCount].Time < now - historySeconds)
            {
                removeCount++;
            }
            if (removeCount > 0) _history.RemoveRange(0, removeCount);
        }

        /// <summary>
        /// Scene ビューに線を描きます（Gizmos はエディタの Scene ビューにだけ表示される補助線）。
        ///   赤い線 … 画面の向き v（カメラが実際に向いている方向）
        ///   灰色の線 … 実際の頭の向き r
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!drawGizmos || controller == null || controller.viewCamera == null) return;

            Vector3 origin = controller.viewCamera.position;
            Vector3 virtualDirection = DirectionFromYaw(controller.VirtualYaw);
            Vector3 realDirection = DirectionFromYaw(controller.RealYaw);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(origin, virtualDirection * 4f);
            Gizmos.color = Color.gray;
            Gizmos.DrawRay(origin, realDirection * 3f);

            // 見どころ範囲の端（v₁, v₂）を黄色の線で描く
            if (config != null)
            {
                MappingEndpoints e = HighlightMapping.ComputeEndpoints(config.Phi, config.W, config.S0);
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(origin, DirectionFromYaw(e.V1) * 6f);
                Gizmos.DrawRay(origin, DirectionFromYaw(e.V2) * 6f);
                if (config.symmetric)
                {
                    Gizmos.DrawRay(origin, DirectionFromYaw(-e.V1) * 6f);
                    Gizmos.DrawRay(origin, DirectionFromYaw(-e.V2) * 6f);
                }
            }
        }

        /// <summary>ヨー角 [度] から水平方向のベクトルを作ります（0° → +Z、90° → +X）。</summary>
        private static Vector3 DirectionFromYaw(float yaw)
        {
            float radian = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radian), 0f, Mathf.Cos(radian));
        }
    }
}
