// =====================================================================
// InstructionPanel.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   VR 空間の正面に浮かぶ「説明パネル」に文字を表示するスクリプトです。
//   被験者への指示（「正面を向いてください」など）を HMD の中に表示します。
//
// 【仕組み】
//   World Space（ワールド空間）の Canvas に Text を置いています。
//   Canvas は通常は画面に貼り付く UI ですが、World Space にすると
//   3D 空間の中に看板のように置くことができます。
//
// 【他のファイルとの関係】
//   ExperimentFlow.cs が、実験の進み具合に合わせて Show() / Hide() を呼びます。
// =====================================================================

using DynamicHighlights.Util;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicHighlights
{
    /// <summary>
    /// VR 空間内に説明文を表示するパネルです。
    /// </summary>
    public class InstructionPanel : MonoBehaviour
    {
        [Tooltip("説明文を表示する Text")]
        public Text messageText;

        [Tooltip("表示・非表示を切り替える対象（パネル全体）")]
        public GameObject panelRoot;

        private void Awake()
        {
            JapaneseFont.ApplyTo(messageText);
        }

        /// <summary>
        /// 説明文を表示します。
        /// </summary>
        public void Show(string message)
        {
            if (messageText != null) messageText.text = message;
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        /// <summary>
        /// パネルを隠します（試行中は景色の邪魔にならないように隠す）。
        /// </summary>
        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
