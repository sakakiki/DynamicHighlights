// =====================================================================
// ExperimentSceneSmokeTests.cs（自動テスト・PlayMode）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   実際に Experiment シーンを再生（Play）して、システム全体がつながって動くかを確かめるテストです。
//   「スモークテスト」とは「電源を入れて煙が出ないか（すぐ壊れないか）」を見る、という意味の
//   簡単な動作確認のことです。
//
// 【確認していること】
//   ・シーンを開いてエラーなく動く（参照の設定し忘れなどが無い）
//   ・頭を右に 25° 向けたとき、カメラが条件どおりの向き（v）になる
//       通常のVR視聴 → 25°、制御一定 → 35°、動的制御（止まっている） → 25°
//   ・1試行を開始して終了すると、CSV ファイルが保存される
//
// 【実行方法】
//   Window → General → Test Runner → PlayMode タブ → Run All
//   （先にメニュー「DynamicHighlights → 実験シーンを作成」でシーンを作っておく必要があります）
//
// 【[UnityTest] と IEnumerator について】
//   PlayMode のテストは、何フレームか進めながら確認したいので、
//   「yield return null;」で1フレーム待つことができる IEnumerator（コルーチン）で書きます。
// =====================================================================

using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DynamicHighlights.Tests
{
    public class ExperimentSceneSmokeTests
    {
        private const string TestFolder = "ExperimentData/_smoke_test";

        private ExperimentFlow _flow;
        private GazeDirectionController _controller;
        private HeadPoseSource _headPose;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // HMD が無い環境では XR の初期化でエラーのログが出ることがあるので、テストの失敗扱いにしない
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene("Experiment");
            yield return null; // シーンの読み込みを待つ
            yield return null;

            _flow = Object.FindFirstObjectByType<ExperimentFlow>();
            _controller = Object.FindFirstObjectByType<GazeDirectionController>();
            _headPose = Object.FindFirstObjectByType<HeadPoseSource>();
            Assert.IsNotNull(_flow, "ExperimentFlow がシーンにありません");
            Assert.IsNotNull(_controller, "GazeDirectionController がシーンにありません");
            Assert.IsNotNull(_headPose, "HeadPoseSource がシーンにありません");
            Assert.IsNotNull(_controller.config, "設定ファイルが設定されていません");
        }

        [UnityTest]
        public IEnumerator 条件ごとにカメラが正しい向きになる()
        {
            // 正面を向いた状態でリセンター
            _headPose.SetSimulatedRotation(0f);
            _controller.Recenter();
            yield return null;

            // スライド8と同じ値で確認する（設定ファイルの値は変えずに、テストの中だけで上書き）
            ExperimentConfig config = Object.Instantiate(_controller.config);
            config.highlightDirection = 45f;
            config.highlightWidth = 20f;
            config.minSlope = 0.5f;
            config.symmetric = true;
            _controller.config = config;

            // ① 通常のVR視聴：v = r = 25°
            _controller.SetCondition(ViewingCondition.NoControl);
            yield return TurnTo(25f);
            Assert.AreEqual(25f, _controller.VirtualYaw, 0.1f);
            Assert.AreEqual(25f, CameraYaw(), 0.1f, "カメラの向き");

            // ② 制御一定（元研究、s = 0.5）：r = 25° → v = 35°
            _controller.SetCondition(ViewingCondition.FixedControl);
            yield return Wait(3);
            Assert.AreEqual(35f, _controller.VirtualYaw, 0.1f);
            Assert.AreEqual(35f, CameraYaw(), 0.1f, "カメラの向き");

            // ③ 動的制御・方式A：止まっていれば ω = 0 → s = 1 → v = r = 25°
            config.dynamicMethod = DynamicMethod.Absolute;
            _controller.SetCondition(ViewingCondition.DynamicControl);
            yield return Wait(10);
            Assert.AreEqual(1f, _controller.Slope, 0.01f);
            Assert.AreEqual(25f, _controller.VirtualYaw, 0.1f);

            // ④ 動的制御・方式B：止まっていれば v は動かない（③の 25° のまま）
            config.dynamicMethod = DynamicMethod.Incremental;
            yield return Wait(10);
            Assert.AreEqual(25f, _controller.VirtualYaw, 0.1f);

            Object.Destroy(config);
        }

        [UnityTest]
        public IEnumerator 一試行を実行するとCSVが保存される()
        {
            // テスト用のフォルダに保存し、終わったら消す
            _flow.logger.outputFolderName = TestFolder;
            string folder = _flow.logger.OutputDirectory;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);

            _flow.participantId = "P99";
            _flow.BeginExperiment();
            Assert.AreEqual(ExperimentFlow.State.Ready, _flow.CurrentState);

            _flow.StartTrial();
            Assert.AreEqual(ExperimentFlow.State.Running, _flow.CurrentState);
            yield return Wait(10);

            string framesFile = _flow.logger.LastFramesFilePath;
            _flow.EndTrial();
            Assert.AreEqual(ExperimentFlow.State.Ready, _flow.CurrentState, "次の試行の待機に戻る");

            Assert.IsTrue(File.Exists(framesFile), "フレームログが保存されている");
            Assert.Greater(File.ReadAllLines(framesFile).Length, 5, "フレームログに行がある");

            string summary = Path.Combine(folder, "P99_summary.csv");
            Assert.IsTrue(File.Exists(summary), "まとめファイルが保存されている");
            int targetCount = _flow.targets.Count;
            Assert.AreEqual(1 + targetCount, File.ReadAllLines(summary).Length, "ヘッダー + オブジェクトの数の行");

            Directory.Delete(folder, true);
        }

        // ---- 補助関数 ----

        /// <summary>模擬操作の頭の向きを、少しずつ目的の角度まで回します。</summary>
        private IEnumerator TurnTo(float yaw)
        {
            for (int i = 1; i <= 10; i++)
            {
                _headPose.SetSimulatedRotation(yaw * i / 10f);
                yield return null;
            }
            yield return Wait(3);
        }

        private static IEnumerator Wait(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>カメラのヨー角（左右の向き）[度]</summary>
        private float CameraYaw()
        {
            Vector3 forward = _controller.viewCamera.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }
    }
}
