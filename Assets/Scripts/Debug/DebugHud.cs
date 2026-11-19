// =====================================================================
// DebugHud.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   動作確認用に、いまの r, v, ω, s などの値と「r-v グラフ」を PC 画面の右上に表示します。
//   （HUD = Head-Up Display：画面に重ねて情報を表示するもの）
//   OnGUI で描いているので、HMD の中には映らず、PC の Game ビューにだけ表示されます。
//
// 【グラフの見方】
//   横軸 … 実視線方向 r（-90°〜90°）
//   縦軸 … 仮想視線方向 v（-90°〜90°）
//   灰色の斜め線 … v = r（制御なし）
//   水色の線     … いまの傾き s での折れ線（方式A の v = F(r; s)）
//   薄い帯       … 見どころ範囲（v₁〜v₂）
//   赤い点       … いまの (r, v)
//
//   方式A では赤い点は必ず水色の線の上にあります。
//   s が変わると線の形が変わるので、頭が止まっていても点が上下に動くことがあります（← 方式A の問題）。
//   方式B では、点は線から外れることがあります（v − r のズレが残るため）。
//   そのかわり、頭が止まっていれば点は動きません。
//
// 【操作】
//   H キー … 表示/非表示の切り替え
//
// 【他のファイルとの関係】
//   GazeDirectionController と ExperimentConfig の値を読んで表示するだけで、何も変更しません。
// =====================================================================

using DynamicHighlights.Core;
using DynamicHighlights.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DynamicHighlights
{
    /// <summary>
    /// 動作確認用の情報とグラフを PC 画面に表示するコンポーネントです。
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        public GazeDirectionController controller;
        public ExperimentConfig config;
        public HeadPoseSource headPose;

        [Tooltip("起動時に表示するか")]
        public bool visible = true;

        private const int GraphSize = 240;   // グラフの大きさ [ピクセル]
        private const float GraphRange = 90f; // グラフの範囲（-90〜90°）

        private Texture2D _graph;            // グラフを描いた画像
        private Color[] _pixels;             // 画像の各ピクセルの色
        private float _drawnSlope = -1f;     // 前回グラフを描いたときの s（変わったときだけ描き直す）
        private string _drawnSettings = "";  // 前回グラフを描いたときの設定（φ, w など）

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) visible = !visible;
        }

        private void OnDestroy()
        {
            if (_graph != null) Destroy(_graph); // 作った画像はメモリから消しておく
        }

        private void OnGUI()
        {
            if (!visible || controller == null || config == null) return;

            GUI.skin.font = JapaneseFont.Get();
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            float width = GraphSize + 20;
            var area = new Rect(Screen.width - width - 10, 10, width, GraphSize + 300);
            GUILayout.BeginArea(area, GUI.skin.box);

            // ---- ① 数値の表示 ----
            string device = headPose != null && headPose.IsHmdActive ? "HMD" : "マウス模擬（右ドラッグ / ←→ / Shift）";
            float gain = HighlightMapping.GainAt(controller.VirtualYaw, config.Phi, config.W, controller.Slope, config.symmetric);

            GUILayout.Label("<b>デバッグ表示（H で切替）</b>", style);
            GUILayout.Label($"入力：{device}", style);
            GUILayout.Label($"条件：{ExperimentFlow.DescribeCondition(controller.condition)}", style);
            if (controller.condition == ViewingCondition.DynamicControl)
            {
                GUILayout.Label($"方式：{ExperimentFlow.DescribeMethod(controller.CurrentMethod)}", style);
            }
            GUILayout.Label($"r = {controller.RealYaw,7:F1}°   v = {controller.VirtualYaw,7:F1}°", style);
            GUILayout.Label($"v − r = {controller.Offset,6:F1}°   ω = {controller.Omega,7:F1}°/s", style);
            GUILayout.Label($"s = {controller.Slope:F3}   c = {controller.ControlStrength:F2}   g = {gain:F2}", style);

            if (!config.IsValid(out string message))
            {
                GUILayout.Label($"<color=red>設定エラー：{message}</color>", new GUIStyle(style) { wordWrap = true });
            }

            // ---- ② グラフの表示 ----
            UpdateGraphTexture(controller.condition == ViewingCondition.NoControl ? 1f : controller.Slope);
            Rect graphRect = GUILayoutUtility.GetRect(GraphSize, GraphSize, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(graphRect, _graph);

            // 赤い点：いまの (r, v)
            float pointX = graphRect.x + ToPixel(controller.RealYaw);
            float pointY = graphRect.yMax - ToPixel(controller.VirtualYaw); // GUI は下に行くほど y が大きいので反転
            Color previousColor = GUI.color;
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(pointX - 3, pointY - 3, 6, 6), Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUILayout.Label("横軸 r / 縦軸 v（-90°〜90°）", new GUIStyle(style) { fontSize = 12 });
            GUILayout.EndArea();
        }

        /// <summary>
        /// 角度 [度] をグラフ上のピクセル位置に変換します。-90° → 0、90° → GraphSize−1
        /// </summary>
        private static float ToPixel(float angle)
        {
            float t = (Mathf.Clamp(angle, -GraphRange, GraphRange) + GraphRange) / (2f * GraphRange);
            return t * (GraphSize - 1);
        }

        /// <summary>
        /// s や設定が変わったときだけ、グラフの画像を描き直します。
        /// （毎フレーム描き直すと重くなるため）
        /// </summary>
        private void UpdateGraphTexture(float s)
        {
            string settings = $"{config.Phi}/{config.W}/{config.symmetric}";
            if (_graph != null && Mathf.Abs(s - _drawnSlope) < 0.005f && settings == _drawnSettings) return;
            _drawnSlope = s;
            _drawnSettings = settings;

            if (_graph == null)
            {
                _graph = new Texture2D(GraphSize, GraphSize, TextureFormat.RGBA32, false);
                _pixels = new Color[GraphSize * GraphSize];
            }

            // ① 背景と見どころ範囲の帯
            var background = new Color(0.12f, 0.12f, 0.14f, 0.9f);
            var band = new Color(0.25f, 0.22f, 0.10f, 0.9f);
            MappingEndpoints e = HighlightMapping.ComputeEndpoints(config.Phi, config.W, s);
            for (int y = 0; y < GraphSize; y++)
            {
                float v = Mathf.Lerp(-GraphRange, GraphRange, y / (float)(GraphSize - 1));
                bool inBand = (v >= e.V1 && v <= e.V2) || (config.symmetric && v >= -e.V2 && v <= -e.V1);
                for (int x = 0; x < GraphSize; x++)
                {
                    _pixels[y * GraphSize + x] = inBand ? band : background;
                }
            }

            // ② 軸（r = 0, v = 0）と、v = r の斜め線
            int center = Mathf.RoundToInt(ToPixel(0f));
            var axis = new Color(0.4f, 0.4f, 0.4f, 1f);
            for (int i = 0; i < GraphSize; i++)
            {
                SetPixel(center, i, axis);
                SetPixel(i, center, axis);
                SetPixel(i, i, new Color(0.6f, 0.6f, 0.6f, 1f));
            }

            // ③ 折れ線 v = F(r; s)（横に1ピクセルずつ進みながら、前の点との間を縦に塗りつぶす）
            var curve = new Color(0.3f, 0.8f, 1f, 1f);
            int previousY = -1;
            for (int x = 0; x < GraphSize; x++)
            {
                float r = Mathf.Lerp(-GraphRange, GraphRange, x / (float)(GraphSize - 1));
                float v = HighlightMapping.MapAbsolute(r, config.Phi, config.W, s, config.symmetric);
                int y = Mathf.RoundToInt(ToPixel(v));
                int from = previousY < 0 ? y : Mathf.Min(previousY, y);
                int to = previousY < 0 ? y : Mathf.Max(previousY, y);
                for (int yy = from; yy <= to; yy++) SetPixel(x, yy, curve);
                previousY = y;
            }

            _graph.SetPixels(_pixels);
            _graph.Apply(); // 変更した色を GPU に送る（これを呼ばないと表示が変わらない）
        }

        private void SetPixel(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= GraphSize || y >= GraphSize) return;
            _pixels[y * GraphSize + x] = color;
        }
    }
}
