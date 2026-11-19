// =====================================================================
// OpenXRProjectSetup.cs（エディタ拡張）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   メニュー「DynamicHighlights → XR（OpenXR）を設定」を押すと、
//   Quest を PC につないで動かすための XR 設定を自動で行います。
//
//   手作業で行う場合は、次の操作と同じです。
//     ① Edit → Project Settings → XR Plug-in Management を開く
//     ② Windows（PC）タブで「OpenXR」にチェックを入れる
//     ③ XR Plug-in Management → OpenXR を開き、
//        ・Render Mode を「Single Pass Instanced」にする
//        ・Enabled Interaction Profiles に「Oculus Touch Controller Profile」を追加する
//
// 【OpenXR とは】
//   VR 機器を動かすための共通の規格です。Unity は OpenXR を通して HMD とやり取りするので、
//   Quest 以外の HMD（Vive, Index など）でも同じプログラムで動かせます。
//   Quest Link で使う場合、PC の Meta Quest Link アプリが OpenXR の「ランタイム」として働きます。
//
// 【このファイルだけ別の asmdef にしている理由】
//   XR パッケージの内部の仕組み（API）は、パッケージのバージョンが変わると変わることがあります。
//   もしここでエラーが出ても、他のスクリプト（実験本体）に影響しないように分けています。
// =====================================================================

using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace DynamicHighlights.EditorTools
{
    /// <summary>
    /// PC（Windows）向けに OpenXR を有効にするエディタ拡張です。
    /// </summary>
    public static class OpenXRProjectSetup
    {
        private const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string SettingsFolder = "Assets/XR";

        [MenuItem("DynamicHighlights/XR（OpenXR）を設定", priority = 2)]
        public static void ConfigureMenu()
        {
            bool ok = Configure(out string report);
            EditorUtility.DisplayDialog("XR（OpenXR）の設定", report, "OK");
            if (!ok) Debug.LogWarning("[OpenXRProjectSetup] " + report);
        }

        /// <summary>
        /// コマンドライン（-executeMethod）から呼ぶための関数です。
        /// </summary>
        public static void ConfigureFromCommandLine()
        {
            Configure(out string report);
            Debug.Log("[OpenXRProjectSetup] " + report);
        }

        /// <summary>
        /// 設定の本体です。
        /// </summary>
        /// <param name="report">結果の説明（日本語）</param>
        /// <returns>すべて成功したら true</returns>
        public static bool Configure(out string report)
        {
            const BuildTargetGroup group = BuildTargetGroup.Standalone; // PC（Windows）向け

            // ① XR の全体設定のファイル（XRGeneralSettingsPerBuildTarget）を探す。無ければ作る。
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (guids.Length > 0)
                {
                    perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
                else
                {
                    Directory.CreateDirectory(SettingsFolder);
                    perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perTarget, $"{SettingsFolder}/XRGeneralSettingsPerBuildTarget.asset");
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            // ② PC 向けの設定と「どの XR プラグインを使うか」の設定を用意する
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
            {
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            }
            XRGeneralSettings general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true; // Play したら自動で XR を開始する

            // ③ OpenXR を有効にする（XR Plug-in Management の「OpenXR」にチェックを入れるのと同じ）
            bool loaderAssigned = XRPackageMetadataStore.AssignLoader(general.Manager, OpenXRLoaderTypeName, group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);

            // ④ OpenXR の細かい設定（描画方式とコントローラーのプロファイル）
            FeatureHelpers.RefreshFeatures(group); // 使える機能の一覧を最新にする
            OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            bool openXRConfigured = false;
            if (openXR != null)
            {
                // Single Pass Instanced：左右の目を1回の描画でまとめて描く方式（処理が軽い）
                openXR.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;

                // Quest のコントローラーのプロファイル（この実験ではコントローラーを使わないが、
                // 何もプロファイルが無いと警告が出るため有効にしておく）
                var touch = openXR.GetFeature<OculusTouchControllerProfile>();
                if (touch != null) touch.enabled = true;

                EditorUtility.SetDirty(openXR);
                openXRConfigured = true;
            }

            AssetDatabase.SaveAssets();

            report = "XR の設定結果\n" +
                     $"・OpenXR の有効化：{(loaderAssigned || IsLoaderActive(general) ? "OK" : "失敗")}\n" +
                     $"・Render Mode / Interaction Profile：{(openXRConfigured ? "OK" : "未設定（Project Settings で手動で設定してください）")}\n\n" +
                     "Edit → Project Settings → XR Plug-in Management → Project Validation に\n" +
                     "警告が出ていないかも確認してください。";
            return (loaderAssigned || IsLoaderActive(general)) && openXRConfigured;
        }

        /// <summary>
        /// すでに OpenXR が有効になっているかを調べます。
        /// </summary>
        private static bool IsLoaderActive(XRGeneralSettings general)
        {
            if (general == null || general.Manager == null) return false;
            foreach (XRLoader loader in general.Manager.activeLoaders)
            {
                if (loader != null && loader.GetType().FullName == OpenXRLoaderTypeName) return true;
            }
            return false;
        }
    }
}
