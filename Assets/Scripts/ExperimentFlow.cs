// =====================================================================
// ExperimentFlow.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   実験全体の「進行役」です。被験者ID の入力から、各セット・各試行の進行、終了までを管理します。
//   試行の並び（どの条件・どのパターンを、どの順番で、左右どちらで行うか）は ExperimentPlan.cs が作ります。
//
// 【実験の組み立て】
//   ・パターン … 参加者に何を見てもらうか（見どころの鑑賞 / 見どころより手前の鑑賞 / 見どころより奥の鑑賞）
//   ・セット   … 同じ条件・同じパターンの試行のまとまり（初期値：右左交互に 6 試行）
//                セットが終わるたびに、HMD を外してアンケートに答えてもらう（3パターン × 3条件 = 9セット）
//   ・試行     … 「正面を向く → 合図で開始 → 対象を見て文字を読み上げる → 正面に戻る」の1回分
//
// 【試行の流れ（状態の移り変わり）】
//
//   [Setup 準備]  … PC 画面で被験者ID を入力して「実験を開始」
//        ↓
//   [Ready 待機] … VR 内に「次は右の見どころを見ます」などの指示を表示。オブジェクトは隠し、制御はかけない。
//        ↓ 実験者が Space キー
//   [Running 試行中] … リセンターして、その試行の条件・見どころの方向で視線方向制御をかける。
//                      オブジェクトを置いて表示し、見てもらう対象にだけ文字を出す。ログを記録する。
//        ↓ 実験者が Space キー（被験者が文字を読み上げて正面に戻ったら。または制限時間）
//   （次の試行があれば Ready に戻る。セットの最後ならアンケートの案内も出す。全部終わったら↓）
//   [Finished 終了]
//
//   このように「いまどの状態か」によって処理を変える作り方を「状態機械（ステートマシン）」と呼びます。
//
// 【実験者のキー操作】
//   Space … 試行の開始 / 終了
//   R     … リセンター（いま向いている方向を正面にする）
//   1/2/3 … 待機中に、いまのセットの残りの試行の条件を強制的に変える（動作確認用）
//            1 = 通常のVR視聴、2 = 制御一定、3 = 動的制御
//
// 【他のファイルとの関係】
//   ExperimentPlan（試行の並び）、GazeDirectionController（条件・見どころの方向の切り替え、リセンター）、
//   ExperimentLogger（ログの保存）、TargetObject（配置・計測）、InstructionPanel（説明表示）を使います。
// =====================================================================

using System.Collections.Generic;
using System.Linq;
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

        // =================================================================
        // Inspector で設定する項目
        // =================================================================

        [Header("■ 参照（シーン作成メニューで自動設定されます）")]
        public ExperimentConfig config;
        public GazeDirectionController controller;
        public ExperimentLogger logger;
        public InstructionPanel instructionPanel;

        [Tooltip("鑑賞対象のオブジェクトの一覧（見どころ Highlight と、鑑賞対象 Object の2つ）")]
        public List<TargetObject> targets = new List<TargetObject>();

        [Header("■ 被験者")]
        [Tooltip("被験者ID（例：P01）。数字の部分でパターンと条件の順番が決まります。")]
        public string participantId = "P01";

        // =================================================================
        // 内部の状態
        // =================================================================

        /// <summary>いまの状態</summary>
        public State CurrentState { get; private set; } = State.Setup;

        /// <summary>いまの試行（または次に行う試行）。Setup 中は null</summary>
        public PlannedTrial CurrentTrial =>
            _trialIndex >= 0 && _trialIndex < _trials.Count ? _trials[_trialIndex] : null;

        /// <summary>この被験者の試行の数（練習を含む）</summary>
        public int TrialCount => _trials.Count;

        private List<PlannedTrial> _trials = new List<PlannedTrial>(); // この被験者の試行の一覧
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
            // 最初は制御なし。オブジェクトは隠しておく。
            controller.SetCondition(ViewingCondition.NoControl);
            controller.PhiOverride = null;
            HideAllTargets();
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
                    // 1/2/3 キー：いまのセットの残りの試行の条件を強制的に変える（動作確認用）
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
            _trials = ExperimentPlan.Build(participantId, config);

            // 被験者ID から乱数の種（シード）を作る → 同じ ID なら同じ文字の並びになる（再現性のため）
            _random = new System.Random(ExperimentPlan.StableHash(participantId));

            _trialIndex = 0;
            _lastSavedMessage = "";
            EnterReady(previous: null);
        }

        /// <summary>
        /// 待機状態に入ります。VR 内に、次の試行で何を見るかを表示します。
        /// </summary>
        /// <param name="previous">直前に終わった試行（実験の最初なら null）</param>
        private void EnterReady(PlannedTrial previous)
        {
            CurrentState = State.Ready;
            controller.SetCondition(ViewingCondition.NoControl); // 待機中は制御しない
            HideAllTargets();

            PlannedTrial trial = CurrentTrial;

            // セットが変わるときの案内（前のセットが終わったら、アンケートに答えてもらう）
            string header = "";
            if (previous != null && previous.SetNumber != trial.SetNumber)
            {
                header = previous.IsPractice
                    ? "練習は終わりです。\n次から本番が始まります。\n\n"
                    : "このセットは終わりです。お疲れさまでした。\nHMD を外して、アンケートに答えてください。\n\n";
            }

            string setTitle = trial.IsPractice
                ? $"練習　{trial.TrialInSet} / {trial.TrialsInSet}"
                : $"セット {trial.SetNumber} / {SetCount}　試行 {trial.TrialInSet} / {trial.TrialsInSet}";

            instructionPanel.Show(
                header +
                $"【{setTitle}】\n" +
                $"次は {ExperimentPlan.DescribeSide(trial.Side)}側 の {DescribeIntendedTarget(trial)} を見ます。\n" +
                "この表示が正面に見えるように体と顔を向けて、\n準備ができたら実験者に合図してください。");
        }

        /// <summary>本番のセットの数</summary>
        private int SetCount => _trials.Count == 0 ? 0 : _trials.Max(t => t.SetNumber);

        /// <summary>
        /// 試行を開始します（待機中に Space キーで呼ばれます。テストからも呼べるよう public にしています）。
        /// </summary>
        public void StartTrial()
        {
            if (CurrentState != State.Ready) return; // 待機中以外は何もしない

            PlannedTrial trial = CurrentTrial;

            // ① いま向いている方向を正面にし、この試行の見どころの方向と条件を設定
            controller.Recenter();
            Time.timeScale = 1f; // カメラ挙動チェックのスロー再生が残っていても、本番は必ず等速にする
            controller.PhiOverride = trial.HighlightYaw;
            controller.SetCondition(trial.Condition);

            // ② オブジェクトを置いて表示する。文字は「見てもらう対象」にだけ出す。
            foreach (TargetObject target in targets)
            {
                bool isHighlight = target.targetId == ExperimentPlan.HighlightTargetId;
                bool used = isHighlight || trial.HasObject;
                target.SetVisible(used);
                if (!used) continue;

                target.PlaceAtYaw(isHighlight ? trial.HighlightYaw : trial.ObjectYaw);
                target.ResetMeasurement();
                if (target.targetId == trial.IntendedTargetId) target.RandomizeLabel(_random);
                else target.HideLabel();
            }

            // ③ ログの記録を開始
            logger.BeginTrial(participantId, trial);

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

            PlannedTrial finished = CurrentTrial;
            logger.EndTrial(finished, targets.Where(t => t.IsVisible), _trialTime, config);
            _lastSavedMessage = $"保存しました：{logger.LastFramesFilePath}";

            _trialIndex++;
            if (_trialIndex < _trials.Count)
            {
                EnterReady(previous: finished);
            }
            else
            {
                CurrentState = State.Finished;
                controller.SetCondition(ViewingCondition.NoControl);
                controller.PhiOverride = null;
                HideAllTargets();
                instructionPanel.Show(
                    "すべての試行が終了しました。\nご協力ありがとうございました。\n\n" +
                    "HMD を外して、最後のアンケートに答えてください。");
            }
        }

        /// <summary>
        /// 待機中に、いまのセットの残りの試行の条件を強制的に変えます（動作確認用）。
        /// </summary>
        private void OverrideCondition(ViewingCondition condition)
        {
            int setNumber = CurrentTrial.SetNumber;
            for (int i = _trialIndex; i < _trials.Count && _trials[i].SetNumber == setNumber; i++)
            {
                _trials[i].Condition = condition; // PlannedTrial は class なので、直接書き換えられる
            }
            Debug.Log($"[ExperimentFlow] セット {setNumber} の残りの試行の条件を {condition} に変更しました。");
        }

        /// <summary>
        /// どのオブジェクトを見ているかを調べ、各オブジェクトの計測を進めます。
        /// </summary>
        /// <returns>見ているオブジェクトの ID（見ていなければ空文字）</returns>
        private string UpdateGaze()
        {
            Transform cameraTransform = controller.viewCamera;

            // 表示中のオブジェクトの中で、視野の中心に一番近いものを探す
            TargetObject closest = null;
            float closestAngle = float.MaxValue;
            foreach (TargetObject target in targets)
            {
                if (!target.IsVisible) continue;
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
                if (!target.IsVisible) continue;
                target.Measure(target == gazed, Time.deltaTime, _trialTime, config.highlightGazedTarget);
            }

            return gazed != null ? gazed.targetId : "";
        }

        private void HideAllTargets()
        {
            foreach (TargetObject target in targets)
            {
                target.HideLabel();
                target.SetVisible(false);
            }
        }

        /// <summary>見てもらう対象の名前（被験者向けの表示）</summary>
        private static string DescribeIntendedTarget(PlannedTrial trial)
        {
            return trial.HasObject ? "「青い箱」" : "「見どころ（黄色い塔）」";
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

            GUILayout.BeginArea(new Rect(10, 10, 460, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("<b>実験者パネル</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18 });

            switch (CurrentState)
            {
                case State.Setup:
                    GUILayout.Label("被験者ID を入力して「実験を開始」を押してください。");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("被験者ID", GUILayout.Width(90));
                    participantId = GUILayout.TextField(participantId, GUILayout.Width(150));
                    GUILayout.EndHorizontal();

                    // この被験者の順番（パターンごとに、その中の条件の順番）
                    ViewingPattern[] patterns = ExperimentPlan.GetPatternOrder(participantId);
                    GUILayout.Label("順番：");
                    for (int p = 0; p < patterns.Length; p++)
                    {
                        ViewingCondition[] conditions = ExperimentPlan.GetConditionOrder(participantId, p);
                        GUILayout.Label($"　{p + 1}. {ExperimentPlan.DescribePattern(patterns[p])}：" +
                                        string.Join(" → ", conditions.Select(ShortCondition)),
                            new GUIStyle(GUI.skin.label) { fontSize = 14 });
                    }
                    if (GUILayout.Button("実験を開始", GUILayout.Height(36))) BeginExperiment();
                    break;

                case State.Ready:
                case State.Running:
                    PlannedTrial trial = CurrentTrial;
                    GUILayout.Label($"被験者：{participantId}　通し {trial.Number}/{_trials.Count}");
                    GUILayout.Label(trial.IsPractice
                        ? $"練習　{trial.TrialInSet}/{trial.TrialsInSet}"
                        : $"セット {trial.SetNumber}/{SetCount}　試行 {trial.TrialInSet}/{trial.TrialsInSet}");
                    GUILayout.Label($"パターン：{ExperimentPlan.DescribePattern(trial.Pattern)}");
                    GUILayout.Label($"条件：{DescribeCondition(trial.Condition)}");
                    GUILayout.Label($"向き：{ExperimentPlan.DescribeSide(trial.Side)}　見どころ {trial.HighlightYaw:F0}°" +
                                    (trial.HasObject ? $"　青い箱 {trial.ObjectYaw:F0}°" : ""));

                    if (CurrentState == State.Ready)
                    {
                        GUILayout.Label("状態：待機中（被験者が正面を向いたら Space で開始）");
                        if (trial.TrialInSet == 1 && !trial.IsPractice && trial.SetNumber > 1)
                        {
                            GUILayout.Label("※ 前のセットのアンケートが終わってから始めてください",
                                new GUIStyle(GUI.skin.label) { fontSize = 14 });
                        }
                    }
                    else
                    {
                        GUILayout.Label($"状態：試行中 {_trialTime:F1} 秒（読み上げて正面に戻ったら Space で終了）");
                        GUILayout.Label($"注視中：{(_gazedTargetId == "" ? "なし" : _gazedTargetId)}");
                        foreach (TargetObject target in targets)
                        {
                            if (!target.IsVisible) continue;
                            string reached = target.FirstReachTime >= 0f ? $"{target.FirstReachTime:F1}秒で到達" : "未到達";
                            string label = target.CurrentLabel == "" ? "（文字なし）" : $"文字「{target.CurrentLabel}」";
                            GUILayout.Label($"　{target.displayName}：{label}　{reached}");
                        }
                    }
                    break;

                case State.Finished:
                    GUILayout.Label("全試行が終了しました。最後のアンケート（順位づけ）をお願いします。");
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
            GUILayout.Label("Space：開始/終了　R：リセンター\n1/2/3：このセットの条件を変更（待機中・動作確認用）\nH：デバッグ表示の切り替え",
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

        /// <summary>条件の短い名前（実験者パネルの一覧用）</summary>
        private static string ShortCondition(ViewingCondition condition)
        {
            switch (condition)
            {
                case ViewingCondition.NoControl: return "通常";
                case ViewingCondition.FixedControl: return "一定";
                case ViewingCondition.DynamicControl: return "動的";
                default: return condition.ToString();
            }
        }
    }
}
