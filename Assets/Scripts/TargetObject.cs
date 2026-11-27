// =====================================================================
// TargetObject.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   鑑賞の対象になるオブジェクトに付けるスクリプトです。シーンには2つあります。
//     ・見どころ（黄色い塔。ID = Highlight）
//     ・鑑賞対象（青い箱。ID = Object。「手前の鑑賞」「奥の鑑賞」のパターンで使う）
//   試行ごとに、ExperimentFlow が PlaceAtYaw() で置く方向を変え、SetVisible() で表示を切り替えます。
//
//   このスクリプトは次のことをします。
//     ① オブジェクトに表示する文字（ランダムな3文字）を試行ごとに変える
//        → 「その文字を読み上げてもらう」ことで、ちゃんと見えたかを客観的に確かめられる
//     ② 「視野の中心付近にあるか（＝見ているか）」を判定し、
//        ・初めて見た時刻（到達時間）
//        ・見ていた合計時間（注視時間）
//        を記録する
//
// 【「見ている」の判定方法】
//   カメラの正面方向と、カメラからオブジェクトへの方向の「角度」を計算し、
//   その角度が設定値（ExperimentConfig の gazeAngleThreshold、初期値 10°）以内なら
//   「見ている」とみなします。
//   ※ アイトラッキング（目の動き）ではなく、頭の向き（画面の中心）で判定しています。
//
// 【他のファイルとの関係】
//   ExperimentFlow.cs が毎フレーム、表示中のオブジェクトの角度を調べて
//   一番中心に近いものを「注視中」として Measure() を呼びます。
// =====================================================================

using DynamicHighlights.Util;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicHighlights
{
    /// <summary>
    /// 鑑賞対象のオブジェクト。表示文字の変更と、到達時間・注視時間の計測を行います。
    /// </summary>
    public class TargetObject : MonoBehaviour
    {
        [Header("■ 識別情報")]

        [Tooltip("ログに書き出すときの名前（半角英数字）。例：Highlight（見どころ）/ Object（鑑賞対象）")]
        public string targetId = "Highlight";

        [Tooltip("実験者向けの表示名。例：見どころ")]
        public string displayName = "見どころ";

        [Header("■ 表示")]

        [Tooltip("オブジェクトの上に表示する文字（UI の Text）")]
        public Text labelText;

        [Tooltip("注視中に色を変える対象（ExperimentConfig の highlightGazedTarget が ON のときだけ）")]
        public Renderer bodyRenderer;

        // ---- 計測結果 ----

        /// <summary>試行開始から初めて見た（視野の中心に入った）時刻 [秒]。まだ見ていなければ -1。</summary>
        public float FirstReachTime { get; private set; } = -1f;

        /// <summary>見ていた時間の合計 [秒]</summary>
        public float DwellTime { get; private set; }

        /// <summary>いま表示している文字</summary>
        public string CurrentLabel { get; private set; } = "";

        // 色を戻すために、元の色を覚えておく
        private Color _originalColor;
        private bool _hasOriginalColor;

        // 読み間違えやすい文字（0 と O、1 と I など）を除いた文字の一覧
        private const string LabelCharacters = "ACEFHKLMNPRTUVXYZ2345789";

        /// <summary>
        /// Awake はオブジェクトが作られたときに最初に1回呼ばれます。
        /// </summary>
        private void Awake()
        {
            JapaneseFont.ApplyTo(labelText);
            if (bodyRenderer != null)
            {
                _originalColor = bodyRenderer.material.color;
                _hasOriginalColor = true;
            }
        }

        /// <summary>
        /// 計測結果をリセットします（試行の開始時に呼ぶ）。
        /// </summary>
        public void ResetMeasurement()
        {
            FirstReachTime = -1f;
            DwellTime = 0f;
            SetGazedVisual(false, false);
        }

        /// <summary>
        /// 表示する文字をランダムな3文字に変えます。
        /// </summary>
        /// <param name="random">乱数を作る道具（System.Random）</param>
        public void RandomizeLabel(System.Random random)
        {
            char[] chars = new char[3];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = LabelCharacters[random.Next(LabelCharacters.Length)];
            }
            CurrentLabel = new string(chars);
            if (labelText != null) labelText.text = CurrentLabel;
        }

        /// <summary>
        /// 文字を隠します（試行と試行の間に、先に答えを覚えられないようにするため）。
        /// </summary>
        public void HideLabel()
        {
            CurrentLabel = "";
            if (labelText != null) labelText.text = "";
        }

        /// <summary>いま表示されている（試行で使っている）か</summary>
        public bool IsVisible => gameObject.activeSelf;

        /// <summary>
        /// オブジェクトを表示する / 隠します（SetActive で GameObject ごと有効・無効を切り替える）。
        /// 試行と試行の間は隠しておき、置き直したところを見られないようにします。
        /// </summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        /// <summary>
        /// オブジェクトを、指定した方向（ヨー角）に置き直します。
        /// 原点（被験者の立つ位置）からの水平距離と高さは今のまま変えません。
        /// </summary>
        /// <param name="yaw">置く方向 [度]（正面 0°、右回りがプラス）</param>
        public void PlaceAtYaw(float yaw)
        {
            Vector3 position = transform.position;
            float distance = new Vector2(position.x, position.z).magnitude; // 原点からの水平距離

            float radian = yaw * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(radian), 0f, Mathf.Cos(radian)); // 0° → +Z、90° → +X
            transform.position = direction * distance + new Vector3(0f, position.y, 0f);

            // 「被験者から見て奥の方向」を向かせると、文字が正しい向きで読めます
            transform.rotation = Quaternion.LookRotation(direction);
        }

        /// <summary>
        /// カメラの正面方向から見て、このオブジェクトが何度ずれた位置にあるかを返します [度]。
        /// 0° なら画面のど真ん中にあります。
        /// </summary>
        public float AngleFromView(Transform cameraTransform)
        {
            Vector3 toTarget = transform.position - cameraTransform.position; // カメラ → オブジェクト のベクトル
            return Vector3.Angle(cameraTransform.forward, toTarget);           // 2つのベクトルのなす角
        }

        /// <summary>
        /// 1フレーム分の計測を行います。
        /// </summary>
        /// <param name="isGazed">このフレームで「見ている」と判定されたか</param>
        /// <param name="deltaTime">このフレームの長さ [秒]</param>
        /// <param name="trialTime">試行開始からの時間 [秒]</param>
        /// <param name="showFeedback">注視中に色を変えるか</param>
        public void Measure(bool isGazed, float deltaTime, float trialTime, bool showFeedback)
        {
            if (isGazed)
            {
                DwellTime += deltaTime;
                if (FirstReachTime < 0f) FirstReachTime = trialTime; // 初めて見た時刻を記録
            }
            SetGazedVisual(isGazed, showFeedback);
        }

        /// <summary>
        /// 注視中の色の切り替え（動作確認用）。
        /// </summary>
        private void SetGazedVisual(bool isGazed, bool showFeedback)
        {
            if (bodyRenderer == null || !_hasOriginalColor) return;
            bodyRenderer.material.color = (isGazed && showFeedback) ? Color.Lerp(_originalColor, Color.white, 0.5f) : _originalColor;
        }
    }
}
