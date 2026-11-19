// =====================================================================
// HeadPoseSource.cs
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   「頭（HMD）がいまどちらを向いているか」を取得するスクリプトです。
//   ・HMD（Quest）が接続されていれば、HMD のセンサーの値を読みます。
//   ・HMD が無いときは、マウスやキーボードで頭の向きを「模擬（シミュレート）」します。
//     → HMD をかぶらなくても、PC だけでプログラムの動作確認ができます。
//
// 【HMD の値の読み方】
//   Unity の Input System（新しい入力システム）では、HMD は「XRHMD」という
//   デバイスとして扱われます。その中の centerEyeRotation が「両目の中心の回転」です。
//
// 【マウス・キーボードでの模擬操作（HMD が無いとき）】
//   ・マウス右ボタンを押しながら動かす … 頭を左右・上下に回す
//   ・← / → キー                       … 一定の速さ（ゆっくり）で左右に回す
//   ・Shift ＋ ← / →                    … 速く回す（ω₀ を超える速さの確認用）
//
// 【他のファイルとの関係】
//   GazeDirectionController.cs が毎フレーム TryGetRotation() を呼び、
//   ここで得た「実際の頭の向き」から実視線方向 r を計算します。
// =====================================================================

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace DynamicHighlights
{
    /// <summary>
    /// HMD（または HMD が無いときのマウス模擬）から頭の回転を取得するコンポーネントです。
    /// </summary>
    public class HeadPoseSource : MonoBehaviour
    {
        [Header("■ HMD が無いときの模擬操作")]

        [Tooltip("ON：HMD が接続されていないとき、マウスとキーボードで頭の向きを模擬する。")]
        public bool useSimulatorWhenNoHmd = true;

        [Tooltip("マウス右ドラッグで回すときの感度 [度/ピクセル]")]
        public float mouseSensitivity = 0.15f;

        [Tooltip("← / → キーで回すときの速さ [度/秒]（ゆっくり首を回す動きの確認用）")]
        public float slowKeySpeed = 30f;

        [Tooltip("Shift ＋ ← / → キーで回すときの速さ [度/秒]（速く首を回す動きの確認用）")]
        public float fastKeySpeed = 150f;

        // 模擬操作で使う「いまの頭の向き」 [度]
        private float _simulatedYaw;
        private float _simulatedPitch;

        /// <summary>
        /// HMD が接続されていて、値を読める状態なら true です。
        /// </summary>
        public bool IsHmdActive
        {
            get
            {
                // InputSystem.GetDevice<XRHMD>() は、接続されている HMD を探して返します（無ければ null）。
                XRHMD hmd = InputSystem.GetDevice<XRHMD>();
                return hmd != null && hmd.added && hmd.enabled;
            }
        }

        /// <summary>
        /// いま模擬操作（マウス・キーボード）を使っているなら true です。
        /// </summary>
        public bool IsSimulating => !IsHmdActive && useSimulatorWhenNoHmd;

        /// <summary>
        /// 頭の回転を取得します。
        /// </summary>
        /// <param name="rotation">取得した回転（取得できなかったときは Quaternion.identity）</param>
        /// <returns>取得できたら true</returns>
        public bool TryGetRotation(out Quaternion rotation)
        {
            // ① HMD がある場合：HMD のセンサー値を読む
            XRHMD hmd = InputSystem.GetDevice<XRHMD>();
            if (hmd != null && hmd.added && hmd.enabled)
            {
                rotation = hmd.centerEyeRotation.ReadValue();

                // 起動直後など、まだ値が入っていないとき（長さ0のクォータニオン）は失敗扱いにする
                if (Mathf.Approximately(Quaternion.Dot(rotation, rotation), 0f))
                {
                    rotation = Quaternion.identity;
                    return false;
                }
                return true;
            }

            // ② HMD が無く、模擬操作を使う場合
            if (useSimulatorWhenNoHmd)
            {
                // Quaternion.Euler(上下, 左右, 傾き) で、角度から回転を作ります。
                rotation = Quaternion.Euler(-_simulatedPitch, _simulatedYaw, 0f);
                return true;
            }

            rotation = Quaternion.identity;
            return false;
        }

        /// <summary>
        /// 模擬操作での頭の向きを直接設定します（自動テストで「右に 25° 向いた」状態を作るときなどに使う）。
        /// HMD が接続されているときは効果がありません。
        /// </summary>
        /// <param name="yaw">左右の向き [度]（右がプラス）</param>
        /// <param name="pitch">上下の向き [度]（上がプラス）</param>
        public void SetSimulatedRotation(float yaw, float pitch = 0f)
        {
            _simulatedYaw = Mathf.DeltaAngle(0f, yaw);
            _simulatedPitch = Mathf.Clamp(pitch, -80f, 80f);
        }

        /// <summary>
        /// Update は毎フレーム Unity が自動で呼ぶ関数です。
        /// ここではマウス・キーボードによる模擬操作の入力を読み取ります。
        /// </summary>
        private void Update()
        {
            if (!IsSimulating) return;

            Mouse mouse = Mouse.current;       // 接続されているマウス（無ければ null）
            Keyboard keyboard = Keyboard.current;

            // ① マウス右ボタンを押しながら動かすと、頭の向きが変わる
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue(); // 前のフレームからマウスが動いた量 [ピクセル]
                _simulatedYaw += delta.x * mouseSensitivity;
                _simulatedPitch += delta.y * mouseSensitivity;
                _simulatedPitch = Mathf.Clamp(_simulatedPitch, -80f, 80f); // 真上・真下まで回りすぎないように
            }

            // ② ← / → キーで一定の速さで回す（Shift を押すと速く回る）
            if (keyboard != null)
            {
                float direction = 0f;
                if (keyboard.leftArrowKey.isPressed) direction -= 1f;
                if (keyboard.rightArrowKey.isPressed) direction += 1f;

                bool fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                float speed = fast ? fastKeySpeed : slowKeySpeed;

                // Time.deltaTime は「前のフレームから経過した時間 [秒]」。
                // 速さ [度/秒] × 時間 [秒] = 回る角度 [度]
                _simulatedYaw += direction * speed * Time.deltaTime;
            }

            // 角度を -180〜180 に収めておく
            _simulatedYaw = Mathf.DeltaAngle(0f, _simulatedYaw);
        }
    }
}
