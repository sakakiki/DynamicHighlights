// =====================================================================
// JapaneseFont.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   画面に日本語を表示するためのフォントを用意する、小さな便利クラスです。
//
// 【なぜ必要か】
//   Unity に最初から入っているフォントには日本語の文字が入っていないことがあり、
//   そのままだと日本語が「□□□」のように表示されてしまう場合があります。
//   そこで、Windows に入っている日本語フォント（游ゴシック・メイリオなど）を
//   実行時に読み込んで使います（Font.CreateDynamicFontFromOSFont）。
//
// 【他のファイルとの関係】
//   InstructionPanel（VR内の説明文）、TargetObject（オブジェクトの文字）、
//   ExperimentFlow / DebugHud（PC画面の操作パネル）から使われます。
// =====================================================================

using UnityEngine;
using UnityEngine.UI;

namespace DynamicHighlights.Util
{
    /// <summary>
    /// OS（Windows）に入っている日本語フォントを読み込んで使い回すクラスです。
    /// </summary>
    public static class JapaneseFont
    {
        /// <summary>使いたいフォント名の候補（上から順に、PCに入っているものが使われる）</summary>
        private static readonly string[] CandidateFontNames =
        {
            "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic", "Noto Sans CJK JP",
        };

        /// <summary>一度作ったフォントを覚えておく変数（毎回作り直さないため）</summary>
        private static Font _cached;

        /// <summary>
        /// 日本語フォントを返します。初めて呼ばれたときだけフォントを作ります。
        /// </summary>
        public static Font Get()
        {
            if (_cached == null)
            {
                _cached = Font.CreateDynamicFontFromOSFont(CandidateFontNames, 32);
            }
            return _cached;
        }

        /// <summary>
        /// UI の Text コンポーネントに日本語フォントを設定します。
        /// </summary>
        public static void ApplyTo(Text text)
        {
            if (text != null)
            {
                text.font = Get();
            }
        }
    }
}
