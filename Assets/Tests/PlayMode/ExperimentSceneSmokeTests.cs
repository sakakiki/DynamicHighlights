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
//       通常のVR視聴 → 25°、制御一定 → 約 32.42°、動的制御（止まっている） → 25°
//   ・見どころと反対側（左）を向いたときは、どの条件でも制御されない
//   ・1試行を開始して終了すると、CSV ファイルが保存される
//   ・カメラ挙動チェックの自動再生で頭が動く
//   ・スロー再生でも、角速度 ω は本来の速さで計算される
//   ・一時停止中は ω・s・v が保たれる（視界が勝手に動かない）
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

            _flow = Object.FindAnyObjectByType<ExperimentFlow>();
            _controller = Object.FindAnyObjectByType<GazeDirectionController>();
            _headPose = Object.FindAnyObjectByType<HeadPoseSource>();
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

            // 元研究の最終的な値で確認する（設定ファイルの値は変えずに、テストの中だけで上書き）
            ExperimentConfig config = Object.Instantiate(_controller.config);
            config.highlightDirection = 45f;
            config.highlightWidth = 20f;
            config.minSlope = 0.55f;
            config.bezierLength = 5f;
            _controller.config = config;

            // ① 通常のVR視聴：v = r = 25°
            _controller.SetCondition(ViewingCondition.NoControl);
            yield return TurnTo(25f);
            Assert.AreEqual(25f, _controller.VirtualYaw, 0.1f);
            Assert.AreEqual(25f, CameraYaw(), 0.1f, "カメラの向き");

            // ② 制御一定（元研究、s = 0.55、L = 5）：r = 25° → v ≒ 32.42°
            //    （r = 25° は折れ点 P₁ の近くなので、ベジェ曲線の上の値になる）
            _controller.SetCondition(ViewingCondition.FixedControl);
            yield return Wait(3);
            Assert.AreEqual(32.42f, _controller.VirtualYaw, 0.1f);
            Assert.AreEqual(32.42f, CameraYaw(), 0.1f, "カメラの向き");

            // ③ 動的制御：止まっていれば ω = 0 → s = 1 → v = r = 25°
            _controller.SetCondition(ViewingCondition.DynamicControl);
            yield return Wait(10);
            Assert.AreEqual(1f, _controller.Slope, 0.01f);
            Assert.AreEqual(25f, _controller.VirtualYaw, 0.1f);

            // ④ 見どころと反対側（左 30°）は、制御一定でも制御されない（見どころは右の1か所だけ）
            _controller.SetCondition(ViewingCondition.FixedControl);
            yield return TurnTo(-30f);
            Assert.AreEqual(-30f, _controller.VirtualYaw, 0.1f);

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

        [UnityTest]
        public IEnumerator 自動再生のパターンで頭が動く()
        {
            CameraCheckDriver driver = GetOrAddDriver();

            // 「速く回して急停止」：φ−20°（初期値なら 25°）まで 150°/秒で回して 2 秒止まる
            float phi = _controller.config.Phi;
            CameraCheckDriver.MotionPattern pattern = CameraCheckDriver.CreatePatterns(phi)[2];
            _controller.SetCondition(ViewingCondition.FixedControl);
            Assert.IsTrue(driver.Play(pattern), "HMD が無いので再生できるはず");

            yield return new WaitForSeconds(0.8f); // 回し終わって止まっている途中
            Assert.IsTrue(driver.IsPlaying);
            Assert.AreEqual(phi - 20f, _controller.RealYaw, 0.5f, "頭の向き r がパターンの角度になっている");
            float expected = Core.HighlightMapping.MapAbsolute(phi - 20f, phi, _controller.config.W,
                _controller.config.S0, _controller.config.L);
            Assert.AreEqual(expected, _controller.VirtualYaw, 0.5f, "制御一定なので v は制御関数どおり");

            var history = new System.Collections.Generic.List<CameraCheckDriver.Sample>();
            driver.CopyHistory(history);
            Assert.Greater(history.Count, 1, "履歴が記録されている");

            driver.Stop();
            Assert.IsFalse(driver.IsPlaying);
        }

        [UnityTest]
        public IEnumerator スロー再生でも角速度は本来の速さで計算される()
        {
            CameraCheckDriver driver = GetOrAddDriver();
            ExperimentConfig config = _controller.config;

            // 「速く往復」（150°/秒）を 0.25 倍のスローで再生する
            CameraCheckDriver.MotionPattern pattern = CameraCheckDriver.CreatePatterns(config.Phi)[1];
            _controller.SetCondition(ViewingCondition.DynamicControl);
            Assert.IsTrue(driver.Play(pattern));
            Assert.IsTrue(driver.SetPlaybackSpeed(0.25f));
            Assert.AreEqual(0.25f, Time.timeScale, 1e-4f);

            float realStart = Time.realtimeSinceStartup;      // 現実の時間
            yield return new WaitForSeconds(0.3f);            // ゲーム内時間で 0.3 秒（回している途中）
            float realElapsed = Time.realtimeSinceStartup - realStart;

            Assert.Greater(realElapsed, 0.9f, "現実の時間では約 4 倍（1.2 秒）かかる＝映像はスロー");
            Assert.AreEqual(150f, _controller.Omega, 10f, "ω は本来の速さ（150°/秒）で計算される");
            Assert.AreEqual(config.S0, _controller.Slope, 0.02f, "ω > ω₀ なので s = s₀");

            driver.ResetTime();
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "元に戻すと等速");
            driver.Stop();
        }

        [UnityTest]
        public IEnumerator 一時停止中はωとsとvが保たれる()
        {
            CameraCheckDriver driver = GetOrAddDriver();
            ExperimentConfig config = _controller.config;

            // 動的制御で「速く往復」（150°/秒）を再生し、回している途中で一時停止する
            _controller.SetCondition(ViewingCondition.DynamicControl);
            Assert.IsTrue(driver.Play(CameraCheckDriver.CreatePatterns(config.Phi)[1]));
            yield return new WaitForSeconds(0.2f); // r ≈ 30°（見どころの手前を速く回している途中）
            Assert.IsTrue(driver.SetPaused(true));
            yield return null; // 一時停止が反映されるのを待つ

            float omega = _controller.Omega;
            float slope = _controller.Slope;
            float v = _controller.VirtualYaw;
            Assert.Greater(omega, 100f, "止める前は速く回している");

            // 一時停止のまま 20 フレーム待っても、値が変わらない
            yield return Wait(20);
            Assert.AreEqual(omega, _controller.Omega, 1e-3f, "ω は止めた瞬間の値のまま（0 にならない）");
            Assert.AreEqual(slope, _controller.Slope, 1e-4f, "s も止めた瞬間の値のまま");
            Assert.AreEqual(v, _controller.VirtualYaw, 1e-3f, "一時停止中は視界も動かない");

            // 一時停止中に条件を切り替えても、ω は保たれる
            _controller.SetCondition(ViewingCondition.NoControl);
            yield return null;
            Assert.AreEqual(omega, _controller.Omega, 1e-3f, "条件を変えても ω は保たれる");
            Assert.AreEqual(1f, _controller.Slope, 1e-4f, "通常の視聴なので s = 1");

            driver.ResetTime();
            driver.Stop();
        }

        /// <summary>テストの後は、必ず時間の進み方を等速に戻す（他のテストに影響しないように）。</summary>
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        // ---- 補助関数 ----

        /// <summary>
        /// シーンの CameraCheckDriver を返します。無ければこのテストの間だけ追加します
        /// （カメラ挙動チェックのウィンドウと同じ動き）。
        /// </summary>
        private CameraCheckDriver GetOrAddDriver()
        {
            var driver = Object.FindAnyObjectByType<CameraCheckDriver>();
            if (driver == null)
            {
                driver = _controller.gameObject.AddComponent<CameraCheckDriver>();
                driver.controller = _controller;
                driver.headPose = _headPose;
                driver.config = _controller.config;
            }
            return driver;
        }

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
