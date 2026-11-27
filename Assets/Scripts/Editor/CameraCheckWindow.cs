// =====================================================================
// CameraCheckWindow.cs（エディタ拡張）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   メニュー「DynamicHighlights → カメラ挙動チェック」で開くウィンドウです。
//   テストを実行しなくても、Play 中にカメラの動きを目で見て確かめられます。
//
//   ウィンドウでできること：
//     ① 条件の切り替え     … 通常 / 制御一定 / 動的制御 をボタン1つで切り替え
//     ② 頭の動きの自動再生 … 「速く回して急停止」などのパターンを再生（CameraCheckDriver が動かす）
//     ③ 上から見た図       … 実際の頭の向き r と、画面の向き v を矢印で表示
//     ④ 時間変化のグラフ   … 直近数秒の r, v, s の変化
//     ⑤ スロー再生         … 映像はゆっくり（×0.5〜×0.1）、一時停止・コマ送りもできる。
//                            角速度 ω などは本来の速さで計算されるので、速く回したときの動きをじっくり見られる
//
//   Game ビューには実際にカメラに映っている景色が出るので、
//   このウィンドウを Game ビューの横に並べて使うと分かりやすいです。
//
// 【EditorWindow とは】
//   Unity エディタに独自のウィンドウを追加する仕組みです。Inspector や Console と同じように、
//   タブをドラッグして好きな場所に置けます。ゲームのビルドには含まれません。
//
// 【描画の仕組み】
//   OnGUI() が呼ばれるたびに、ボタンや図を描き直します。
//   線や円は Handles（ハンドル）という、エディタ用の描画機能で描いています。
//   Play 中は Update() で毎回 Repaint()（描き直し）を呼ぶので、図がなめらかに動きます。
// =====================================================================

using System.Collections.Generic;
using DynamicHighlights.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DynamicHighlights.EditorTools
{
    /// <summary>
    /// Play 中のカメラの動きを確かめるためのエディタウィンドウです。
    /// </summary>
    public class CameraCheckWindow : EditorWindow
    {
        private const string ScenePath = "Assets/Scenes/Experiment.unity";

        // 色（図とグラフで共通）
        private static readonly Color RealColor = new Color(0.75f, 0.75f, 0.75f);  // r：灰色
        private static readonly Color VirtualColor = new Color(0.3f, 0.85f, 1f);   // v：水色
        private static readonly Color SlopeColor = new Color(1f, 0.8f, 0.25f);     // s：黄色
        private static readonly Color BandColor = new Color(1f, 0.75f, 0.2f, 0.18f); // 見どころ範囲
        private static readonly Color BackgroundColor = new Color(0.13f, 0.13f, 0.15f);

        // Play 中のシーンから見つけたコンポーネント
        private GazeDirectionController _controller;
        private HeadPoseSource _headPose;
        private CameraCheckDriver _driver;
        private ExperimentFlow _flow;
        private List<TargetObject> _targets = new List<TargetObject>();

        private readonly List<CameraCheckDriver.Sample> _samples = new List<CameraCheckDriver.Sample>();

        /// <summary>メニューからウィンドウを開きます。</summary>
        [MenuItem("DynamicHighlights/カメラ挙動チェック", priority = 10)]
        public static void Open()
        {
            var window = GetWindow<CameraCheckWindow>();
            window.titleContent = new GUIContent("カメラ挙動チェック");
            window.minSize = new Vector2(520f, 640f);
            window.Show();
        }

        /// <summary>
        /// エディタが更新されるたびに呼ばれます。Play 中はウィンドウを描き直して、図を動かします。
        /// </summary>
        private void Update()
        {
            if (EditorApplication.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            // ---- Play していないとき ----
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "このウィンドウは Play 中に使います。\n" +
                    "下のボタンで Experiment シーンを開いて再生すると、条件の切り替え・頭の動きの自動再生・" +
                    "上から見た図・グラフでカメラの動きを確かめられます。", MessageType.Info);
                if (GUILayout.Button("▶ Experiment シーンを開いて再生", GUILayout.Height(32)))
                {
                    OpenSceneAndPlay();
                }
                return;
            }

            // ---- Play 中：シーンからコンポーネントを探す ----
            FindComponents();
            if (_controller == null || _controller.config == null)
            {
                EditorGUILayout.HelpBox("GazeDirectionController が見つかりません。Experiment シーンを再生してください。", MessageType.Warning);
                return;
            }
            ExperimentConfig config = _controller.config;

            DrawStatus(config);
            EditorGUILayout.Space(4);
            DrawConditionButtons(config);
            EditorGUILayout.Space(4);
            DrawPatternButtons(config);
            EditorGUILayout.Space(4);
            DrawSpeedControls();
            EditorGUILayout.Space(6);
            DrawValues();
            EditorGUILayout.Space(4);
            DrawFigures(config);
        }

        // =================================================================
        // 上部：状態・ボタン
        // =================================================================

        private void DrawStatus(ExperimentConfig config)
        {
            string device = _headPose != null && _headPose.IsHmdActive ? "HMD" : "マウス模擬";
            string state = _flow != null ? _flow.CurrentState.ToString() : "-";
            EditorGUILayout.LabelField($"入力：{device}　　実験の状態：{state}　　φ={_controller.CurrentPhi}°  w={config.W}°  s₀={config.S0}  ω₀={config.Omega0}°/s");

            if (!config.IsValid(out string message))
            {
                EditorGUILayout.HelpBox($"設定エラー：{message}", MessageType.Error);
            }
        }

        /// <summary>
        /// 条件を切り替えるボタンを描きます。いまの条件のボタンは色が付きます。
        /// </summary>
        private void DrawConditionButtons(ExperimentConfig config)
        {
            EditorGUILayout.LabelField("① 条件", EditorStyles.boldLabel);

            bool running = _flow != null && _flow.CurrentState == ExperimentFlow.State.Running;
            if (running)
            {
                EditorGUILayout.HelpBox("試行中は条件を変更できません（実験の記録が正しくなくなるため）。", MessageType.None);
            }

            // EditorGUI.DisabledScope の中のボタンは、条件が true のとき押せなくなります
            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUILayout.BeginHorizontal();
                ConditionButton("通常（制御なし）", ViewingCondition.NoControl);
                ConditionButton("制御一定（元研究）", ViewingCondition.FixedControl);
                ConditionButton("動的制御（本研究）", ViewingCondition.DynamicControl);
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 条件のボタンを1つ描きます。いまの条件のボタンは水色になります。
        /// </summary>
        private void ConditionButton(string label, ViewingCondition condition)
        {
            bool selected = _controller.condition == condition;

            Color previous = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button(label, GUILayout.Height(28)))
            {
                _controller.SetCondition(condition);
            }
            GUI.backgroundColor = previous;
        }

        /// <summary>
        /// 頭の動きのパターンのボタンを描きます。
        /// </summary>
        private void DrawPatternButtons(ExperimentConfig config)
        {
            EditorGUILayout.LabelField("② 頭の動きの自動再生", EditorStyles.boldLabel);

            if (_driver == null)
            {
                EditorGUILayout.HelpBox("CameraCheckDriver がシーンにありません。メニュー「実験シーンを作成」でシーンを作り直してください。", MessageType.Warning);
                return;
            }
            if (_headPose != null && _headPose.IsHmdActive)
            {
                EditorGUILayout.HelpBox("HMD 接続中は自動再生できません。実際に頭を動かすと、下の図とグラフに表示されます。", MessageType.None);
                return;
            }

            CameraCheckDriver.MotionPattern[] patterns = CameraCheckDriver.CreatePatterns(_controller.CurrentPhi);

            // ボタンを2列に並べる
            for (int i = 0; i < patterns.Length; i += 2)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, patterns.Length); j++)
                {
                    bool playing = _driver.IsPlaying && _driver.CurrentPatternName == patterns[j].Name;
                    Color previous = GUI.backgroundColor;
                    if (playing) GUI.backgroundColor = new Color(0.5f, 1f, 0.6f);
                    var content = new GUIContent(patterns[j].Name, patterns[j].Description); // 2つ目はマウスを乗せたときの説明
                    if (GUILayout.Button(content, GUILayout.Height(26))) _driver.Play(patterns[j]);
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("停止", GUILayout.Width(80))) _driver.Stop();
            _driver.loop = GUILayout.Toggle(_driver.loop, "繰り返す", GUILayout.Width(80));
            if (GUILayout.Button("リセンター", GUILayout.Width(90)))
            {
                _driver.Stop();
                _headPose.SetSimulatedRotation(0f);
                _controller.Recenter();
            }
            EditorGUILayout.EndHorizontal();

            // 再生中のパターンの説明
            if (_driver.IsPlaying)
            {
                foreach (CameraCheckDriver.MotionPattern pattern in patterns)
                {
                    if (pattern.Name != _driver.CurrentPatternName) continue;
                    EditorGUILayout.HelpBox($"再生中：{pattern.Name} … {_driver.CurrentStepNote}\n{pattern.Description}", MessageType.None);
                }
            }
            else
            {
                EditorGUILayout.LabelField("（マウス右ドラッグ・←→キーでも Game ビューで手動で動かせます）", EditorStyles.miniLabel);
            }
        }

        /// <summary>再生速度の選択肢（1 = 等速）</summary>
        private static readonly float[] SpeedOptions = { 1f, 0.5f, 0.25f, 0.1f };

        /// <summary>
        /// スロー再生・一時停止・コマ送りのボタンを描きます。
        /// 映像はゆっくりになりますが、ω などの計算は本来の速さ（ゲーム内時間）で行われます。
        /// </summary>
        private void DrawSpeedControls()
        {
            EditorGUILayout.LabelField("③ スロー再生（映像はゆっくり、角速度 ω などは本来の速さで計算）", EditorStyles.boldLabel);
            if (_driver == null) return;

            if (_headPose != null && _headPose.IsHmdActive)
            {
                EditorGUILayout.HelpBox("HMD 接続中はスロー再生できません（実際の頭の動きと時間の進み方がずれるため）。", MessageType.None);
                return;
            }

            bool running = _flow != null && _flow.CurrentState == ExperimentFlow.State.Running;
            if (running && _driver.IsTimeChanged) _driver.ResetTime(); // 試行中は必ず等速に戻す

            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("再生速度", GUILayout.Width(60));
                foreach (float speed in SpeedOptions)
                {
                    bool selected = Mathf.Approximately(_driver.PlaybackSpeed, speed);
                    Color previous = GUI.backgroundColor;
                    if (selected) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                    string label = speed >= 1f ? "等速" : $"×{speed:0.##}";
                    if (GUILayout.Button(label, GUILayout.Height(24))) _driver.SetPlaybackSpeed(speed);
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                string pauseLabel = _driver.IsPaused ? "▶ 再開" : "⏸ 一時停止";
                if (GUILayout.Button(pauseLabel, GUILayout.Height(24), GUILayout.Width(110))) _driver.SetPaused(!_driver.IsPaused);
                using (new EditorGUI.DisabledScope(!_driver.IsPaused))
                {
                    if (GUILayout.Button("1コマ進む", GUILayout.Height(24), GUILayout.Width(90))) _driver.StepOneFrame();
                }
                if (GUILayout.Button("元に戻す", GUILayout.Height(24), GUILayout.Width(80))) _driver.ResetTime();
                EditorGUILayout.EndHorizontal();
            }

            if (running)
            {
                EditorGUILayout.LabelField("（試行中はスロー再生できません）", EditorStyles.miniLabel);
            }
            else if (_driver.IsTimeChanged)
            {
                string state = _driver.IsPaused ? "一時停止中" : $"映像 ×{_driver.PlaybackSpeed:0.##}";
                EditorGUILayout.HelpBox(
                    $"{state}　ゲーム内時間 {Time.time:F2} 秒（Time.timeScale = {Time.timeScale:0.##}）\n" +
                    "ω・s・v は本来の速さで回したときと同じ値になります。グラフの横軸もゲーム内時間です。\n" +
                    "※ スロー再生中にマウスで回すと、ゲーム内では速く回したことになります。", MessageType.None);
            }
        }

        private void DrawValues()
        {
            var style = new GUIStyle(EditorStyles.label) { fontSize = 13 };
            EditorGUILayout.LabelField(
                $"r = {_controller.RealYaw,6:F1}°    v = {_controller.VirtualYaw,6:F1}°    v − r = {_controller.Offset,6:F1}°    " +
                $"ω = {_controller.Omega,6:F1}°/s    s = {_controller.Slope:F2}    c = {_controller.ControlStrength:F2}", style);
        }

        // =================================================================
        // 下部：上から見た図とグラフ
        // =================================================================

        private void DrawFigures(ExperimentConfig config)
        {
            // 残りの高さを使って図を描く場所を確保する
            Rect area = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float topSize = Mathf.Min(area.height, area.width * 0.42f);
            var topRect = new Rect(area.x, area.y, topSize, topSize);
            var graphRect = new Rect(topRect.xMax + 8f, area.y, area.width - topSize - 8f, area.height);

            _driver?.CopyHistory(_samples);

            // 描画は「Repaint」のイベントのときだけ行う（OnGUI はクリックなどでも呼ばれるため）
            if (Event.current.type != EventType.Repaint) return;

            DrawTopView(topRect, config);
            DrawTimeGraph(graphRect, config);
        }

        /// <summary>
        /// 上から見た図を描きます。上が正面（0°）、右が +90°。
        /// </summary>
        private void DrawTopView(Rect rect, ExperimentConfig config)
        {
            EditorGUI.DrawRect(rect, BackgroundColor);
            Vector2 center = rect.center;
            float radius = rect.width * 0.40f;

            // 見どころ範囲（外側：画面の向きでの範囲 v₁〜v₂）
            float s = _controller.condition == ViewingCondition.NoControl ? 1f : _controller.Slope;
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(_controller.CurrentPhi, config.W, s);
            DrawSector(center, radius, e.V1, e.V2, BandColor);

            // 内側の弧：いまの s のときの「実際の頭の向き」での範囲 r₁〜r₂
            Handles.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Handles.DrawAAPolyLine(3f, ArcPoints(center, radius * 0.55f, e.R1, e.R2));

            // 外周の円と、15° ごとの目盛り
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            Handles.DrawAAPolyLine(1.5f, ArcPoints(center, radius, -180f, 180f));
            for (int angle = -165; angle <= 180; angle += 15)
            {
                float length = angle % 90 == 0 ? 10f : 5f;
                Handles.DrawAAPolyLine(1.5f, PointOnCircle(center, radius, angle), PointOnCircle(center, radius - length, angle));
            }

            // 鑑賞対象のオブジェクト（点）
            foreach (TargetObject target in _targets)
            {
                if (target == null) continue;
                Vector3 p = target.transform.position;
                float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                Handles.color = target.targetId.StartsWith("Highlight") ? new Color(1f, 0.8f, 0.2f) : new Color(0.6f, 0.9f, 0.6f);
                Handles.DrawSolidDisc(PointOnCircle(center, radius + 8f, yaw), Vector3.forward, 4f);
            }

            // 矢印：実際の頭の向き r（灰色・細い）と、画面の向き v（水色・太い）
            Handles.color = RealColor;
            Handles.DrawAAPolyLine(3f, center, PointOnCircle(center, radius * 0.8f, _controller.RealYaw));
            Handles.color = VirtualColor;
            Handles.DrawAAPolyLine(5f, center, PointOnCircle(center, radius * 0.95f, _controller.VirtualYaw));
            Handles.DrawSolidDisc(center, Vector3.forward, 4f);

            // 凡例
            var legend = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white }, wordWrap = true };
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, 30),
                "上から見た図（上が正面）\n灰：頭の向き r　水色：画面の向き v", legend);
            GUI.Label(new Rect(rect.x + 4, rect.yMax - 30, rect.width - 8, 30),
                "帯：見どころ範囲 v₁〜v₂\n橙の弧：頭の向きでの範囲 r₁〜r₂", legend);
        }

        /// <summary>
        /// 時間変化のグラフを描きます（上：r と v、下：s）。
        /// </summary>
        private void DrawTimeGraph(Rect rect, ExperimentConfig config)
        {
            EditorGUI.DrawRect(rect, BackgroundColor);
            var legend = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };

            // 上 70%：角度のグラフ、下 30%：s のグラフ
            var angleRect = new Rect(rect.x + 34f, rect.y + 18f, rect.width - 40f, rect.height * 0.68f - 22f);
            var slopeRect = new Rect(angleRect.x, angleRect.yMax + 22f, angleRect.width, rect.yMax - angleRect.yMax - 40f);

            float historySeconds = _driver != null ? _driver.historySeconds : 6f;
            float now = Time.time;
            const float angleRange = 100f; // 縦軸 -100°〜100°

            // ---- 角度のグラフの目盛り ----
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(_controller.CurrentPhi, config.W, config.S0);
            EditorGUI.DrawRect(Rect.MinMaxRect(angleRect.x, AngleToY(angleRect, e.V2, angleRange), angleRect.xMax,
                AngleToY(angleRect, e.V1, angleRange)), BandColor);
            foreach (float angle in new[] { -90f, -45f, 0f, 45f, 90f })
            {
                float y = AngleToY(angleRect, angle, angleRange);
                Handles.color = new Color(1f, 1f, 1f, angle == 0f ? 0.35f : 0.12f);
                Handles.DrawAAPolyLine(1f, new Vector3(angleRect.x, y), new Vector3(angleRect.xMax, y));
                GUI.Label(new Rect(rect.x + 2f, y - 8f, 32f, 16f), $"{angle:F0}°", legend);
            }

            // ---- s のグラフの目盛り（0〜1）----
            foreach (float value in new[] { 0f, config.S0, 1f })
            {
                float y = Mathf.Lerp(slopeRect.yMax, slopeRect.y, value);
                Handles.color = new Color(1f, 1f, 1f, 0.15f);
                Handles.DrawAAPolyLine(1f, new Vector3(slopeRect.x, y), new Vector3(slopeRect.xMax, y));
                GUI.Label(new Rect(rect.x + 2f, y - 8f, 32f, 16f), value.ToString("0.##"), legend);
            }

            // ---- 線 ----
            if (_samples.Count >= 2)
            {
                var rPoints = new Vector3[_samples.Count];
                var vPoints = new Vector3[_samples.Count];
                var sPoints = new Vector3[_samples.Count];
                for (int i = 0; i < _samples.Count; i++)
                {
                    CameraCheckDriver.Sample sample = _samples[i];
                    // 横軸：右端が「いま」、左端が historySeconds 秒前
                    float x = Mathf.Lerp(angleRect.x, angleRect.xMax, 1f - (now - sample.Time) / historySeconds);
                    rPoints[i] = new Vector3(x, AngleToY(angleRect, sample.R, angleRange));
                    vPoints[i] = new Vector3(x, AngleToY(angleRect, sample.V, angleRange));
                    sPoints[i] = new Vector3(x, Mathf.Lerp(slopeRect.yMax, slopeRect.y, Mathf.Clamp01(sample.S)));
                }
                Handles.color = RealColor;
                Handles.DrawAAPolyLine(2f, rPoints);
                Handles.color = VirtualColor;
                Handles.DrawAAPolyLine(3f, vPoints);
                Handles.color = SlopeColor;
                Handles.DrawAAPolyLine(2f, sPoints);
            }

            GUI.Label(new Rect(angleRect.x, rect.y + 2f, angleRect.width, 16f),
                $"時間変化（ゲーム内時間で直近 {historySeconds:F0} 秒、右端がいま）　灰：r　水色：v　帯：見どころ範囲", legend);
            GUI.Label(new Rect(slopeRect.x, slopeRect.y - 18f, slopeRect.width, 16f), "黄：傾き s（1 = 制御なし、s₀ = 最も強い制御）", legend);
        }

        // =================================================================
        // 小さな便利関数
        // =================================================================

        /// <summary>角度をグラフの縦の位置に変換します（上がプラス）。</summary>
        private static float AngleToY(Rect rect, float angle, float range)
        {
            float t = (Mathf.Clamp(angle, -range, range) + range) / (2f * range);
            return Mathf.Lerp(rect.yMax, rect.y, t);
        }

        /// <summary>
        /// 上から見た図で、ヨー角 yaw の方向・半径 radius の点を返します。
        /// 画面の上が 0°、右が +90°（GUI の座標は下に行くほど y が大きいので、y はマイナスにする）。
        /// </summary>
        private static Vector3 PointOnCircle(Vector2 center, float radius, float yaw)
        {
            float radian = yaw * Mathf.Deg2Rad;
            return new Vector3(center.x + Mathf.Sin(radian) * radius, center.y - Mathf.Cos(radian) * radius, 0f);
        }

        /// <summary>fromYaw から toYaw までの弧の点の並びを作ります。</summary>
        private static Vector3[] ArcPoints(Vector2 center, float radius, float fromYaw, float toYaw)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(toYaw - fromYaw) / 3f) + 1);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = PointOnCircle(center, radius, Mathf.Lerp(fromYaw, toYaw, i / (float)(count - 1)));
            }
            return points;
        }

        /// <summary>扇形（中心から fromYaw〜toYaw の範囲）を塗りつぶします。</summary>
        private static void DrawSector(Vector2 center, float radius, float fromYaw, float toYaw, Color color)
        {
            Vector3[] arc = ArcPoints(center, radius, fromYaw, toYaw);
            var polygon = new Vector3[arc.Length + 1];
            polygon[0] = center;
            arc.CopyTo(polygon, 1);
            Handles.color = color;
            Handles.DrawAAConvexPolygon(polygon); // 中心と弧の点を結んだ多角形（扇形）を塗る
        }

        /// <summary>
        /// Play 中のシーンから、必要なコンポーネントを探します（見つかったものは覚えておく）。
        /// </summary>
        private void FindComponents()
        {
            if (_controller == null) _controller = FindAnyObjectByType<GazeDirectionController>();
            if (_headPose == null) _headPose = FindAnyObjectByType<HeadPoseSource>();
            if (_driver == null) _driver = FindAnyObjectByType<CameraCheckDriver>();
            if (_driver == null && _controller != null)
            {
                // シーンに CameraCheckDriver が無いときは、この Play の間だけ一時的に追加する
                // （Play を止めると消えます。シーンに保存したいときはメニュー「開いているシーンにカメラ挙動チェックを追加」）
                _driver = _controller.gameObject.AddComponent<CameraCheckDriver>();
                _driver.controller = _controller;
                _driver.headPose = _controller.headPose;
                _driver.config = _controller.config;
            }
            if (_flow == null) _flow = FindAnyObjectByType<ExperimentFlow>();
            if (_targets.Count == 0 || _targets[0] == null)
            {
                _targets = new List<TargetObject>(FindObjectsByType<TargetObject>());
            }
        }

        /// <summary>
        /// Experiment シーンを開いて Play を始めます。
        /// </summary>
        private static void OpenSceneAndPlay()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (!System.IO.File.Exists(ScenePath))
                {
                    EditorUtility.DisplayDialog("カメラ挙動チェック", "Experiment シーンがありません。先にメニュー「実験シーンを作成」を実行してください。", "OK");
                    return;
                }
                EditorSceneManager.OpenScene(ScenePath);
            }
            EditorApplication.isPlaying = true;
        }
    }
}
