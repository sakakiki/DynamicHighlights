// =====================================================================
// ExperimentFlow.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   実験全体の「進行役」です。被験者ID の入力から、各条件の試行、終了までを管理します。
//
// 【実験の流れ（状態の移り変わり）】
//
//   [Setup 準備]  … PC 画面で被験者ID を入力して「実験を開始」
//        ↓
//   [Ready 待機] … VR 内に「正面を向いてください」と表示。制御はかけない（通常の視聴）。
//        ↓ 実験者が Space キー
//   [Running 試行中] … リセンターして、その試行の条件で視線方向制御をかける。
//                      オブジェクトに文字を表示し、ログを記録する。
//        ↓ 実験者が Space キー（または制限時間）
//   （次の試行があれば Ready に戻る。全部終わったら↓）
//   [Finished 終了]
//
//   このように「いまどの状態か」によって処理を変える作り方を「状態機械（ステートマシン）」と呼びます。
//
// 【条件の順番（カウンターバランス）】
//   3つの条件を全員同じ順番で行うと、「慣れ」や「疲れ」の影響が特定の条件に偏ってしまいます。
//   そこで、3条件の並べ方6通りを被験者番号で順番に割り当てます。
//   例：P01 → 1番目の並べ方、P02 → 2番目の並べ方、…、P07 → 1番目に戻る
//   （6人の倍数の被験者を集めると、すべての並べ方が同じ回数になります）
//
// 【実験者のキー操作】
//   Space … 試行の開始 / 終了
//   R     … リセンター（いま向いている方向を正面にする）
//   1/2/3 … 待機中に、次の試行の条件を強制的に変える（動作確認用）
//            1 = 通常のVR視聴、2 = 制御一定、3 = 動的制御
//
// 【他のファイルとの関係】
//   GazeDirectionController（条件の切り替え・リセンター）、
//   ExperimentLogger（ログの保存）、TargetObject（計測）、InstructionPanel（説明表示）を操作します。
// =====================================================================

using System.Collections.Generic;
using System.Text;
using DynamicHighlights.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DynamicHighlights
{
    /// <summary>
    /// 実験の進行を管理するコンポーネントです。
    /// </summary>
    public class ExperimentFlow : MonoBehaviour
    {
        /// <summary>実験の状態</summary>
        public enum State
        {
            Setup,    // 被験者ID の入力待ち
            Ready,    // 試行の開始待ち
            Running,  // 試行中
            Finished, // 全試行が終了
        }

        /// <summary>1回の試行の情報</summary>
        private struct Trial
        {
            public int Number;                 // 試行番号（練習 0、本番 1〜3）
            public ViewingCondition Condition; // 視聴条件
            public bool IsPractice;            // 練習かどうか
        }

        // =================================================================
        // Inspector で設定する項目
        // =================================================================

        [Header("■ 参照（シーン作成メニューで自動設定されます）")]
        public ExperimentConfig config;
        public GazeDirectionController controller;
        public ExperimentLogger logger;
        public InstructionPanel instructionPanel;

        [Tooltip("鑑賞対象のオブジェクトの一覧")]
        public List<TargetObject> targets = new List<TargetObject>();

        [Header("■ 被験者")]
        [Tooltip("被験者ID（例：P01）。数字の部分で条件の順番が決まります。")]
        public string participantId = "P01";

        // =================================================================
        // 3条件の並べ方6通り（カウンターバランス用）
        // =================================================================
        private static readonly ViewingCondition[][] ConditionOrders =
        {
            new[] { ViewingCondition.NoControl, ViewingCondition.FixedControl, ViewingCondition.DynamicControl },
            new[] { ViewingCondition.FixedControl, ViewingCondition.DynamicControl, ViewingCondition.NoControl },
            new[] { ViewingCondition.DynamicControl, ViewingCondition.NoControl, ViewingCondition.FixedControl },
            new[] { ViewingCondition.NoControl, ViewingCondition.DynamicControl, ViewingCondition.FixedControl },
            new[] { ViewingCondition.FixedControl, ViewingCondition.NoControl, ViewingCondition.DynamicControl },
            new[] { ViewingCondition.DynamicControl, ViewingCondition.FixedControl, ViewingCondition.NoControl },
        };

        // =================================================================
        // 内部の状態
        // =================================================================

        /// <summary>いまの状態</summary>
        public State CurrentState { get; private set; } = State.Setup;

        private readonly List<Trial> _trials = new List<Trial>(); // この被験者の試行の一覧
        private int _trialIndex;      // いま何番目の試行か（0 から数える）
        private float _trialTime;     // 試行開始からの経過時間 [秒]
        private string _gazedTargetId = ""; // いま見ているオブジェクト
        private System.Random _random = new System.Random();
        private string _lastSavedMessage = "";

        // =================================================================
        // Unity が自動で呼ぶ関数
        // =================================================================

        private void Start()
        {
            // 最初は制御なし。文字は隠しておく。
            controller.SetCondition(ViewingCondition.NoControl);
            HideAllLabels();
            instructionPanel.Show("実験の準備中です。\nしばらくお待ちください。");

            if (config != null && !config.IsValid(out string message))
            {
                Debug.LogError($"[ExperimentFlow] 設定ファイルに問題があります：{message}");
            }
        }

        /// <summary>
        /// 毎フレーム、実験者のキー操作を調べます。
        /// </summary>
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // R キー：リセンター（どの状態でも使える）
            if (keyboard.rKey.wasPressedThisFrame)
            {
                controller.Recenter();
            }

            switch (CurrentState)
            {
                case State.Ready:
                    if (keyboard.spaceKey.wasPressedThisFrame) StartTrial();
                    // 1/2/3 キー：次の試行の条件を強制的に変える（動作確認用）
                    if (keyboard.digit1Key.wasPressedThisFrame) OverrideCondition(ViewingCondition.NoControl);
                    if (keyboard.digit2Key.wasPressedThisFrame) OverrideCondition(ViewingCondition.FixedControl);
                    if (keyboard.digit3Key.wasPressedThisFrame) OverrideCondition(ViewingCondition.DynamicControl);
                    break;

                case State.Running:
                    if (keyboard.spaceKey.wasPressedThisFrame) EndTrial();
                    break;
            }
        }

        /// <summary>
        /// LateUpdate：GazeDirectionController の計算（実行順 -100）が終わった後に呼ばれるので、
        /// このフレームの最新の値で計測とログ記録ができます。
        /// </summary>
        private void LateUpdate()
        {
            if (CurrentState != State.Running) return;

            _trialTime += Time.deltaTime;

            // ① どのオブジェクトを見ているかを調べる
            _gazedTargetId = UpdateGaze();

            // ② ログに1行書く
            logger.LogFrame(_trialTime, _gazedTargetId);

            // ③ 制限時間があれば、時間が来たら自動で終了
            if (config.trialDurationSeconds > 0f && _trialTime >= config.trialDurationSeconds)
            {
                EndTrial();
            }
        }

        // =================================================================
        // 実験の進行
        // =================================================================

        /// <summary>
        /// 実験を開始します（被験者ID を確定し、試行の一覧を作る）。
        /// </summary>
        public void BeginExperiment()
        {
            _trials.Clear();

            // 練習試行（通常のVR視聴）
            if (config.includePracticeTrial)
            {
                _trials.Add(new Trial { Number = 0, Condition = ViewingCondition.NoControl, IsPractice = true });
            }

            // 本番の3試行（被験者番号で並べ方を決める）
            ViewingCondition[] order = GetConditionOrder(participantId);
            for (int i = 0; i < order.Length; i++)
            {
                _trials.Add(new Trial { Number = i + 1, Condition = order[i], IsPractice = false });
            }

            // 被験者ID から乱数の種（シード）を作る → 同じ ID なら同じ文字の並びになる（再現性のため）
            _random = new System.Random(StableHash(participantId));

            _trialIndex = 0;
            EnterReady(afterTrial: false);
        }

        /// <summary>
        /// 文字列から、毎回同じになる整数（ハッシュ値）を作ります。
        /// （string.GetHashCode() は環境によって実行のたびに値が変わることがあるため、自分で計算します）
        /// </summary>
        private static int StableHash(string text)
        {
            unchecked // 計算の途中で int の範囲を超えてもエラーにしない（あふれた分は捨てる）
            {
                int hash = 17;
                foreach (char c in text ?? "") hash = hash * 31 + c;
                return hash;
            }
        }

        /// <summary>
        /// 被験者ID の数字部分から、条件の並べ方を決めます。
        /// 例："P07" → 7 → (7 − 1) % 6 = 0 → 1番目の並べ方
        /// </summary>
        public static ViewingCondition[] GetConditionOrder(string id)
        {
            int number = ExtractNumber(id);
            int index = ((number - 1) % ConditionOrders.Length + ConditionOrders.Length) % ConditionOrders.Length;
            return (ViewingCondition[])ConditionOrders[index].Clone();
        }

        /// <summary>
        /// 文字列の中の数字だけを取り出して整数にします。数字が無ければ 1 を返します。
        /// </summary>
        private static int ExtractNumber(string id)
        {
            var digits = new StringBuilder();
            foreach (char c in id ?? "")
            {
                if (char.IsDigit(c)) digits.Append(c);
            }
            return int.TryParse(digits.ToString(), out int number) ? number : 1;
        }

        /// <summary>
        /// 待機状態に入ります。VR 内に指示を表示します。
        /// </summary>
        /// <param name="afterTrial">試行が終わった直後なら true（アンケートの案内を追加する）</param>
        private void EnterReady(bool afterTrial)
        {
            CurrentState = State.Ready;
            controller.SetCondition(ViewingCondition.NoControl); // 待機中は制御しない
            HideAllLabels();

            Trial trial = _trials[_trialIndex];
            string title = trial.IsPractice ? "練習" : $"第{trial.Number}試行";
            string header = afterTrial
                ? "前の試行は終了です。お疲れさまでした。\n実験者の指示に従ってアンケートに回答してください。\n\n"
                : "";
            instructionPanel.Show(
                header +
                $"【次は{title}です】\n" +
                "この表示が正面に見えるように\n体と顔を向けてください。\n" +
                "準備ができたら実験者に合図してください。");
        }

        /// <summary>
        /// 試行を開始します（待機中に Space キーで呼ばれます。テストからも呼べるよう public にしています）。
        /// </summary>
        public void StartTrial()
        {
            if (CurrentState != State.Ready) return; // 待機中以外は何もしない

            Trial trial = _trials[_trialIndex];

            // ① いま向いている方向を正面にし、この試行の条件を設定
            //    （動作確認で方式を一時的に上書きしていても、本番は必ず設定ファイルの方式に戻す）
            controller.Recenter();
            controller.MethodOverride = null;
            Time.timeScale = 1f; // カメラ挙動チェックのスロー再生が残っていても、本番は必ず等速にする
            controller.SetCondition(trial.Condition);

            // ② オブジェクトの準備（文字をランダムに変え、計測をリセット）
            foreach (TargetObject target in targets)
            {
                target.ResetMeasurement();
                target.RandomizeLabel(_random);
            }

            // ③ ログの記録を開始
            logger.BeginTrial(participantId, trial.Number, trial.Condition, trial.IsPractice);

            instructionPanel.Hide();
            _trialTime = 0f;
            CurrentState = State.Running;
        }

        /// <summary>
        /// 試行を終了します（試行中に Space キーまたは制限時間で呼ばれます）。
        /// </summary>
        public void EndTrial()
        {
            if (CurrentState != State.Running) return; // 試行中以外は何もしない

            logger.EndTrial(targets, _trialTime, config);
            _lastSavedMessage = $"保存しました：{logger.LastFramesFilePath}";

            _trialIndex++;
            if (_trialIndex < _trials.Count)
            {
                // 試行と試行の間でアンケートに答えてもらう場合は、ここで HMD を外してもらいます。
                EnterReady(afterTrial: true);
            }
            else
            {
                CurrentState = State.Finished;
                controller.SetCondition(ViewingCondition.NoControl);
                HideAllLabels();
                instructionPanel.Show("すべての試行が終了しました。\nご協力ありがとうございました。\n\nHMD を外してください。");
            }
        }

        /// <summary>
        /// 待機中に、次の試行の条件を強制的に変えます（動作確認用）。
        /// </summary>
        private void OverrideCondition(ViewingCondition condition)
        {
            Trial trial = _trials[_trialIndex];
            trial.Condition = condition;
            _trials[_trialIndex] = trial; // struct なので、変更したものを入れ直す必要がある
            Debug.Log($"[ExperimentFlow] 次の試行の条件を {condition} に変更しました。");
        }

        /// <summary>
        /// どのオブジェクトを見ているかを調べ、各オブジェクトの計測を進めます。
        /// </summary>
        /// <returns>見ているオブジェクトの ID（見ていなければ空文字）</returns>
        private string UpdateGaze()
        {
            Transform cameraTransform = controller.viewCamera;

            // 視野の中心に一番近いオブジェクトを探す
            TargetObject closest = null;
            float closestAngle = float.MaxValue;
            foreach (TargetObject target in targets)
            {
                float angle = target.AngleFromView(cameraTransform);
                if (angle < closestAngle)
                {
                    closestAngle = angle;
                    closest = target;
                }
            }

            // しきい値以内なら「見ている」
            TargetObject gazed = closestAngle <= config.gazeAngleThreshold ? closest : null;

            foreach (TargetObject target in targets)
            {
                target.Measure(target == gazed, Time.deltaTime, _trialTime, config.highlightGazedTarget);
            }

            return gazed != null ? gazed.targetId : "";
        }

        private void HideAllLabels()
        {
            foreach (TargetObject target in targets) target.HideLabel();
        }

        // =================================================================
        // 実験者用の操作パネル（PC 画面にだけ表示。HMD の中には映らない）
        // =================================================================

        // OnGUI は Unity の古い UI の仕組み（IMGUI）で、毎フレーム画面に直接ボタンや文字を描きます。
        // 見た目は簡素ですが、少ないコードで操作パネルが作れるので実験者用に使っています。
        private void OnGUI()
        {
            GUI.skin.font = JapaneseFont.Get();
            GUI.skin.label.fontSize = 16;
            GUI.skin.button.fontSize = 16;
            GUI.skin.textField.fontSize = 16;

            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("<b>実験者パネル</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18 });

            switch (CurrentState)
            {
                case State.Setup:
                    GUILayout.Label("被験者ID を入力して「実験を開始」を押してください。");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("被験者ID", GUILayout.Width(90));
                    participantId = GUILayout.TextField(participantId, GUILayout.Width(150));
                    GUILayout.EndHorizontal();

                    ViewingCondition[] order = GetConditionOrder(participantId);
                    GUILayout.Label($"条件の順番：{string.Join(" → ", order)}");
                    GUILayout.Label($"動的制御の方式：{DescribeMethod(config.dynamicMethod)}");
                    if (GUILayout.Button("実験を開始", GUILayout.Height(36))) BeginExperiment();
                    break;

                case State.Ready:
                case State.Running:
                    Trial trial = _trials[_trialIndex];
                    GUILayout.Label($"被験者：{participantId}　試行 {_trialIndex + 1}/{_trials.Count}" +
                                    (trial.IsPractice ? "（練習）" : ""));
                    GUILayout.Label($"条件：{DescribeCondition(trial.Condition)}");
                    if (trial.Condition == ViewingCondition.DynamicControl)
                    {
                        GUILayout.Label($"方式：{DescribeMethod(config.dynamicMethod)}");
                    }

                    if (CurrentState == State.Ready)
                    {
                        GUILayout.Label("状態：待機中（Space で開始）");
                    }
                    else
                    {
                        GUILayout.Label($"状態：試行中 {_trialTime:F1} 秒（Space で終了）");
                        GUILayout.Label($"注視中：{(_gazedTargetId == "" ? "なし" : _gazedTargetId)}");
                        GUILayout.Label("表示中の文字（被験者の読み上げと照合）：");
                        foreach (TargetObject target in targets)
                        {
                            string reached = target.FirstReachTime >= 0f ? $"{target.FirstReachTime:F1}秒で到達" : "未到達";
                            GUILayout.Label($"　{target.displayName}：{target.CurrentLabel}（{reached}）");
                        }
                    }
                    break;

                case State.Finished:
                    GUILayout.Label("全試行が終了しました。");
                    GUILayout.Label($"データの保存先：\n{logger.OutputDirectory}");
                    if (GUILayout.Button("次の被験者へ（Setup に戻る）", GUILayout.Height(32)))
                    {
                        CurrentState = State.Setup;
                        instructionPanel.Show("実験の準備中です。\nしばらくお待ちください。");
                    }
                    break;
            }

            if (_lastSavedMessage != "")
            {
                GUILayout.Space(8);
                GUILayout.Label(_lastSavedMessage, new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true });
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Space：開始/終了　R：リセンター\n1/2/3：次の条件を変更（待機中）\nH：デバッグ表示の切り替え",
                new GUIStyle(GUI.skin.label) { fontSize = 13 });
            GUILayout.EndArea();
        }

        /// <summary>条件の日本語名を返します。</summary>
        public static string DescribeCondition(ViewingCondition condition)
        {
            switch (condition)
            {
                case ViewingCondition.NoControl: return "通常のVR視聴（制御なし）";
                case ViewingCondition.FixedControl: return "制御一定（元研究）";
                case ViewingCondition.DynamicControl: return "動的制御（本研究）";
                default: return condition.ToString();
            }
        }

        /// <summary>方式の日本語名を返します。</summary>
        public static string DescribeMethod(DynamicMethod method)
        {
            return method == DynamicMethod.Absolute ? "方式A（絶対写像）" : "方式B（増分ゲイン）";
        }
    }
}
