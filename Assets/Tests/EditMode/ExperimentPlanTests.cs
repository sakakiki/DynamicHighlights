// =====================================================================
// ExperimentPlanTests.cs（自動テスト）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   ExperimentPlan.cs（試行の並び）と、ExperimentConfig のパターンの設定チェックが
//   正しく動くかを確かめます。
//
// 【確認していること】
//   ・練習3試行 ＋ 本番 9セット × 6試行 = 57試行ができる
//   ・セットの中では右左交互で、右と左が同じ回数
//   ・パターンごとに3セット続き、その中に3条件が1回ずつ入る
//   ・パターンの角度が設定どおりで、向き（右/左）と符号がそろっている
//   ・被験者を6人集めると、パターンと条件の並べ方が偏らない（カウンターバランス）
//   ・同じ被験者ID なら毎回同じ並びになる（再現性）
//   ・鑑賞対象が見どころ範囲に入っているなど、おかしな設定を見つけられる
//
// 【ScriptableObject.CreateInstance について】
//   設定ファイル（ExperimentConfig）は ScriptableObject なので、new ではなく
//   ScriptableObject.CreateInstance で作ります。値はスクリプトに書いた初期値になります。
// =====================================================================

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace DynamicHighlights.Tests
{
    public class ExperimentPlanTests
    {
        private ExperimentConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<ExperimentConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void 練習3試行と本番9セット6試行ずつができる()
        {
            List<PlannedTrial> trials = ExperimentPlan.Build("P01", _config);
            Assert.AreEqual(3 + 9 * 6, trials.Count);

            // 練習：通常のVR視聴で、3パターンを1回ずつ
            List<PlannedTrial> practice = trials.Where(t => t.IsPractice).ToList();
            Assert.AreEqual(3, practice.Count);
            Assert.IsTrue(practice.All(t => t.Condition == ViewingCondition.NoControl));
            CollectionAssert.AreEquivalent(
                new[] { ViewingPattern.Highlight, ViewingPattern.BeforeHighlight, ViewingPattern.BeyondHighlight },
                practice.Select(t => t.Pattern).ToArray());

            // 通し番号は 1 から順に並ぶ
            for (int i = 0; i < trials.Count; i++) Assert.AreEqual(i + 1, trials[i].Number);
        }

        [Test]
        public void セットの中では右左交互で同じ回数になる()
        {
            List<PlannedTrial> trials = ExperimentPlan.Build("P03", _config);
            foreach (IGrouping<int, PlannedTrial> set in trials.Where(t => !t.IsPractice).GroupBy(t => t.SetNumber))
            {
                List<PlannedTrial> list = set.OrderBy(t => t.TrialInSet).ToList();
                Assert.AreEqual(6, list.Count, $"セット {set.Key}");
                for (int i = 1; i < list.Count; i++)
                {
                    Assert.AreEqual(-list[i - 1].Side, list[i].Side, $"セット {set.Key} の {i + 1} 試行目は前と逆向き");
                }
                Assert.AreEqual(3, list.Count(t => t.Side > 0), "右が3回");
                Assert.AreEqual(1, list.Select(t => t.Condition).Distinct().Count(), "セットの中は同じ条件");
                Assert.AreEqual(1, list.Select(t => t.Pattern).Distinct().Count(), "セットの中は同じパターン");
            }
        }

        [Test]
        public void パターンごとに3条件が1回ずつ続けて行われる()
        {
            List<PlannedTrial> trials = ExperimentPlan.Build("P02", _config);
            List<PlannedTrial> firstOfSets = trials.Where(t => !t.IsPractice && t.TrialInSet == 1).ToList();
            Assert.AreEqual(9, firstOfSets.Count);

            for (int p = 0; p < 3; p++)
            {
                List<PlannedTrial> block = firstOfSets.Skip(p * 3).Take(3).ToList();
                Assert.AreEqual(1, block.Select(t => t.Pattern).Distinct().Count(), "3セット続けて同じパターン");
                CollectionAssert.AreEquivalent(
                    new[] { ViewingCondition.NoControl, ViewingCondition.FixedControl, ViewingCondition.DynamicControl },
                    block.Select(t => t.Condition).ToArray(), "3条件が1回ずつ");
            }
        }

        [Test]
        public void パターンの角度が設定どおりで向きと符号がそろう()
        {
            foreach (PlannedTrial t in ExperimentPlan.Build("P05", _config))
            {
                Assert.AreEqual(t.Side, (int)Mathf.Sign(t.HighlightYaw), "見どころは試行の向きの側にある");
                switch (t.Pattern)
                {
                    case ViewingPattern.Highlight:
                        Assert.AreEqual(45f, Mathf.Abs(t.HighlightYaw), 1e-4f);
                        Assert.IsFalse(t.HasObject, "見どころの鑑賞では青い箱を置かない");
                        Assert.AreEqual(ExperimentPlan.HighlightTargetId, t.IntendedTargetId);
                        break;
                    case ViewingPattern.BeforeHighlight:
                        Assert.AreEqual(60f, Mathf.Abs(t.HighlightYaw), 1e-4f);
                        Assert.AreEqual(t.Side * 30f, t.ObjectYaw, 1e-4f);
                        Assert.AreEqual(ExperimentPlan.ObjectTargetId, t.IntendedTargetId);
                        break;
                    case ViewingPattern.BeyondHighlight:
                        Assert.AreEqual(30f, Mathf.Abs(t.HighlightYaw), 1e-4f);
                        Assert.AreEqual(t.Side * 60f, t.ObjectYaw, 1e-4f);
                        Assert.AreEqual(ExperimentPlan.ObjectTargetId, t.IntendedTargetId);
                        break;
                }
            }
        }

        [Test]
        public void 被験者6人で並べ方が偏らない()
        {
            // パターンの順番：6人でちょうど6通りの並べ方になる
            var patternOrders = new HashSet<string>();
            // 各パターンの位置（1番目・2番目・3番目）で、各条件が何番目に来たかを数える
            var count = new Dictionary<(int, ViewingCondition, int), int>();

            for (int n = 1; n <= 6; n++)
            {
                string id = $"P{n:00}";
                patternOrders.Add(string.Join(",", ExperimentPlan.GetPatternOrder(id)));
                for (int p = 0; p < 3; p++)
                {
                    ViewingCondition[] order = ExperimentPlan.GetConditionOrder(id, p);
                    for (int k = 0; k < 3; k++)
                    {
                        var key = (p, order[k], k);
                        count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
            }

            Assert.AreEqual(6, patternOrders.Count, "パターンの並べ方6通りが1回ずつ");
            foreach (int value in count.Values)
            {
                Assert.AreEqual(2, value, "どのパターンの位置でも、各条件が各順番に2回ずつ来る");
            }
        }

        [Test]
        public void 同じ被験者IDなら同じ並びになる()
        {
            List<PlannedTrial> a = ExperimentPlan.Build("P07", _config);
            List<PlannedTrial> b = ExperimentPlan.Build("P07", _config);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Condition, b[i].Condition);
                Assert.AreEqual(a[i].Pattern, b[i].Pattern);
                Assert.AreEqual(a[i].Side, b[i].Side);
            }
        }

        [Test]
        public void 試行回数と練習の有無を設定で変えられる()
        {
            _config.trialsPerSide = 1;
            _config.includePracticeTrial = false;
            List<PlannedTrial> trials = ExperimentPlan.Build("P01", _config);
            Assert.AreEqual(9 * 2, trials.Count, "練習なし・1セット2試行");
            Assert.IsFalse(trials.Any(t => t.IsPractice));
        }

        [Test]
        public void 初期値の設定は正しい()
        {
            Assert.IsTrue(_config.IsValid(out string message), message);
        }

        [Test]
        public void パターンのおかしな設定を見つけられる()
        {
            _config.beforePatternObjectAngle = 55f; // 見どころ 60° の見どころ範囲（50〜70°）の中
            Assert.IsFalse(_config.IsValid(out _), "鑑賞対象が見どころ範囲の中");

            _config.beforePatternObjectAngle = 75f; // 見どころより奥にある
            Assert.IsFalse(_config.IsValid(out _), "「手前の鑑賞」なのに鑑賞対象が奥");

            _config.beforePatternObjectAngle = 30f;
            _config.beyondPatternHighlightAngle = 10f; // w/2 = 10 なので見どころ範囲が 0° を越える
            Assert.IsFalse(_config.IsValid(out _), "見どころの角度が正面に近すぎる");
        }
    }
}
