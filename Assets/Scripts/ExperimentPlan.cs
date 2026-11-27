// =====================================================================
// ExperimentPlan.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   被験者1人分の「試行の並び（実験計画）」を作るクラスです。
//   どの条件・どのパターンを、どの順番で、左右どちら向きで行うかを決めます。
//   Unity のシーンには触らない純粋な計算なので、EditMode テストで並びを確かめられます。
//
// 【用語】
//   条件（ViewingCondition）… 通常のVR視聴 / 制御一定（元研究）/ 動的制御（本研究）
//   パターン（ViewingPattern）… 何を見てもらうか（参加者の「意図」）
//       見どころの鑑賞      … 見どころ（黄色い塔）を見る        （初期値：見どころ 45°）
//       見どころより手前の鑑賞 … 見どころより手前の青い箱を見る   （初期値：見どころ 60°、箱 30°）
//       見どころより奥の鑑賞   … 見どころより奥の青い箱を見る     （初期値：見どころ 30°、箱 60°）
//   セット … 同じ条件・同じパターンの試行のまとまり。セットが終わるたびにアンケートに答える。
//            1セット = 左右交互に（trialsPerSide × 2）試行。初期値は 3 × 2 = 6 試行。
//   試行   … 「正面 → 対象を見る（文字を読み上げる）→ 正面に戻る」の1回分。
//
// 【並べ方】
//   元研究の評価1（見どころの大きさ w ごとにまとめ、その中で傾き s の順番を入れ替え）にならい、
//   「パターンごとにまとめ、その中で3条件を続けて行う」並べ方にしています。
//     例：見どころの鑑賞（通常 → 制御一定 → 動的）→ 奥の鑑賞（…）→ 手前の鑑賞（…）
//   ・パターンの順番 … 被験者番号で、3パターンの並べ方6通りを順番に割り当てる
//   ・条件の順番     … 被験者番号とパターンの位置で、3条件の並べ方6通りから選ぶ
//                       （パターンごとに違う並べ方になるようにずらす）
//   被験者を6の倍数の人数集めると、どの並べ方も同じ回数ずつ使われます（カウンターバランス）。
//   ・左右 … セットの中では右左交互。最初が右か左かは、被験者IDとセット番号から決めた乱数で選ぶ。
//
// 【他のファイルとの関係】
//   ExperimentFlow.cs が実験の開始時に Build() を呼び、できた一覧の順に試行を進めます。
//   ExperimentLogger.cs は、各試行の情報（PlannedTrial）を CSV に書き出します。
// =====================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DynamicHighlights
{
    /// <summary>
    /// 試行のパターン（参加者に何を見てもらうか）です。
    /// </summary>
    public enum ViewingPattern
    {
        /// <summary>見どころの鑑賞：見どころ（黄色い塔）を見る</summary>
        Highlight = 0,

        /// <summary>見どころより手前の鑑賞：見どころより正面寄りにある鑑賞対象（青い箱）を見る</summary>
        BeforeHighlight = 1,

        /// <summary>見どころより奥の鑑賞：見どころより外側にある鑑賞対象（青い箱）を見る</summary>
        BeyondHighlight = 2,
    }

    /// <summary>
    /// 1回の試行の計画です。
    /// <para>
    /// class（クラス）にしているのは、ExperimentFlow で一部（条件）を書き換えるときに、
    /// 取り出して入れ直す手間を省くためです（struct は値のコピーになるため）。
    /// </para>
    /// </summary>
    public class PlannedTrial
    {
        /// <summary>通し番号（1 から。練習も含めて数える）</summary>
        public int Number;

        /// <summary>セット番号（0 = 練習、本番は 1 から）</summary>
        public int SetNumber;

        /// <summary>セットの中で何回目の試行か（1 から）</summary>
        public int TrialInSet;

        /// <summary>そのセットの試行数</summary>
        public int TrialsInSet;

        /// <summary>視聴条件</summary>
        public ViewingCondition Condition;

        /// <summary>パターン</summary>
        public ViewingPattern Pattern;

        /// <summary>向き：右なら +1、左なら −1</summary>
        public int Side;

        /// <summary>見どころの方向 φ [度]（符号つき。左なら負）</summary>
        public float HighlightYaw;

        /// <summary>鑑賞対象（青い箱）の方向 [度]（符号つき）。見どころの鑑賞パターンでは使わない</summary>
        public float ObjectYaw;

        /// <summary>練習かどうか</summary>
        public bool IsPractice => SetNumber == 0;

        /// <summary>鑑賞対象（青い箱）を置くか（手前・奥のパターンなら true）</summary>
        public bool HasObject => Pattern != ViewingPattern.Highlight;

        /// <summary>この試行で見てもらうオブジェクトの ID（TargetObject.targetId と同じ）</summary>
        public string IntendedTargetId => HasObject ? ExperimentPlan.ObjectTargetId : ExperimentPlan.HighlightTargetId;
    }

    /// <summary>
    /// 試行の並びを作る static クラスです。
    /// </summary>
    public static class ExperimentPlan
    {
        /// <summary>見どころのオブジェクトの ID</summary>
        public const string HighlightTargetId = "Highlight";

        /// <summary>鑑賞対象（青い箱）の ID</summary>
        public const string ObjectTargetId = "Object";

        // 3つのものの並べ方6通り（0, 1, 2 の並び）。条件にもパターンにも使います。
        private static readonly int[][] Orders =
        {
            new[] { 0, 1, 2 },
            new[] { 1, 2, 0 },
            new[] { 2, 0, 1 },
            new[] { 0, 2, 1 },
            new[] { 1, 0, 2 },
            new[] { 2, 1, 0 },
        };

        private static readonly ViewingCondition[] Conditions =
        {
            ViewingCondition.NoControl, ViewingCondition.FixedControl, ViewingCondition.DynamicControl,
        };

        private static readonly ViewingPattern[] Patterns =
        {
            ViewingPattern.Highlight, ViewingPattern.BeforeHighlight, ViewingPattern.BeyondHighlight,
        };

        /// <summary>
        /// 被験者1人分の試行の一覧を作ります。
        /// </summary>
        /// <param name="participantId">被験者ID（例：P01）。数字の部分で順番が決まる</param>
        /// <param name="config">設定ファイル（パターンの角度、1セットの試行数、練習の有無）</param>
        public static List<PlannedTrial> Build(string participantId, ExperimentConfig config)
        {
            var trials = new List<PlannedTrial>();
            int number = 1;

            // ① 練習セット：通常のVR視聴で、3パターンを1回ずつ（右・左・右）
            if (config.includePracticeTrial)
            {
                for (int i = 0; i < Patterns.Length; i++)
                {
                    int side = i % 2 == 0 ? 1 : -1;
                    trials.Add(CreateTrial(config, number++, 0, i + 1, Patterns.Length,
                        ViewingCondition.NoControl, Patterns[i], side));
                }
            }

            // ② 本番：パターンごとにまとめ、その中で3条件を1セットずつ行う
            ViewingPattern[] patternOrder = GetPatternOrder(participantId);
            int setNumber = 1;
            int trialsInSet = Mathf.Max(1, config.trialsPerSide) * 2;

            for (int p = 0; p < patternOrder.Length; p++)
            {
                ViewingCondition[] conditionOrder = GetConditionOrder(participantId, p);
                foreach (ViewingCondition condition in conditionOrder)
                {
                    // 最初の向き（右か左か）を、被験者とセットごとに決まった乱数で選ぶ
                    var random = new System.Random(StableHash(participantId) * 31 + setNumber);
                    int firstSide = random.Next(2) == 0 ? 1 : -1;

                    for (int t = 0; t < trialsInSet; t++)
                    {
                        int side = t % 2 == 0 ? firstSide : -firstSide; // 右左交互
                        trials.Add(CreateTrial(config, number++, setNumber, t + 1, trialsInSet,
                            condition, patternOrder[p], side));
                    }
                    setNumber++;
                }
            }

            return trials;
        }

        /// <summary>
        /// 1回の試行の計画を作ります。パターンに応じて見どころと鑑賞対象の角度を決めます。
        /// </summary>
        private static PlannedTrial CreateTrial(ExperimentConfig config, int number, int setNumber, int trialInSet,
            int trialsInSet, ViewingCondition condition, ViewingPattern pattern, int side)
        {
            float highlightAngle;
            float objectAngle;
            switch (pattern)
            {
                case ViewingPattern.BeforeHighlight:
                    highlightAngle = config.beforePatternHighlightAngle;
                    objectAngle = config.beforePatternObjectAngle;
                    break;
                case ViewingPattern.BeyondHighlight:
                    highlightAngle = config.beyondPatternHighlightAngle;
                    objectAngle = config.beyondPatternObjectAngle;
                    break;
                default:
                    highlightAngle = Mathf.Abs(config.highlightDirection);
                    objectAngle = 0f;
                    break;
            }

            return new PlannedTrial
            {
                Number = number,
                SetNumber = setNumber,
                TrialInSet = trialInSet,
                TrialsInSet = trialsInSet,
                Condition = condition,
                Pattern = pattern,
                Side = side,
                HighlightYaw = side * highlightAngle,
                ObjectYaw = side * objectAngle,
            };
        }

        /// <summary>
        /// 被験者ID から、パターンの順番を決めます。
        /// 例："P01" → 1番目の並べ方（見どころ → 手前 → 奥）
        /// </summary>
        public static ViewingPattern[] GetPatternOrder(string participantId)
        {
            int[] order = Orders[IndexFor(ExtractNumber(participantId) - 1)];
            return new[] { Patterns[order[0]], Patterns[order[1]], Patterns[order[2]] };
        }

        /// <summary>
        /// 被験者ID と「何番目のパターンか」から、そのパターンの中での条件の順番を決めます。
        /// パターンの位置ごとに並べ方をずらすので、同じ被験者でもパターンごとに順番が変わります。
        /// </summary>
        /// <param name="participantId">被験者ID</param>
        /// <param name="patternPosition">何番目のパターンか（0, 1, 2）</param>
        public static ViewingCondition[] GetConditionOrder(string participantId, int patternPosition)
        {
            int[] order = Orders[IndexFor(ExtractNumber(participantId) - 1 + patternPosition * 2)];
            return new[] { Conditions[order[0]], Conditions[order[1]], Conditions[order[2]] };
        }

        /// <summary>並べ方6通りのどれを使うか（0〜5）。負の数でも正しく割った余りにする。</summary>
        private static int IndexFor(int value)
        {
            return ((value % Orders.Length) + Orders.Length) % Orders.Length;
        }

        /// <summary>
        /// 文字列の中の数字だけを取り出して整数にします。数字が無ければ 1 を返します。
        /// 例："P07" → 7
        /// </summary>
        public static int ExtractNumber(string id)
        {
            var digits = new StringBuilder();
            foreach (char c in id ?? "")
            {
                if (char.IsDigit(c)) digits.Append(c);
            }
            return int.TryParse(digits.ToString(), out int value) ? value : 1;
        }

        /// <summary>
        /// 文字列から、毎回同じになる整数（ハッシュ値）を作ります。
        /// （string.GetHashCode() は環境によって実行のたびに値が変わることがあるため、自分で計算します）
        /// </summary>
        public static int StableHash(string text)
        {
            unchecked // 計算の途中で int の範囲を超えてもエラーにしない（あふれた分は捨てる）
            {
                int hash = 17;
                foreach (char c in text ?? "") hash = hash * 31 + c;
                return hash;
            }
        }

        /// <summary>パターンの日本語名を返します。</summary>
        public static string DescribePattern(ViewingPattern pattern)
        {
            switch (pattern)
            {
                case ViewingPattern.Highlight: return "見どころの鑑賞";
                case ViewingPattern.BeforeHighlight: return "見どころより手前の鑑賞";
                case ViewingPattern.BeyondHighlight: return "見どころより奥の鑑賞";
                default: return pattern.ToString();
            }
        }

        /// <summary>向きの日本語名を返します（+1 → 右、−1 → 左）。</summary>
        public static string DescribeSide(int side)
        {
            return side >= 0 ? "右" : "左";
        }

        /// <summary>向きを CSV 用の1文字で返します（+1 → R、−1 → L）。</summary>
        public static string SideCode(int side)
        {
            return side >= 0 ? "R" : "L";
        }
    }
}
