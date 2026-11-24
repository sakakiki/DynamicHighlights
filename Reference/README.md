# Reference（参考資料置き場）

このフォルダは `Assets/` の外にあるので、Unity は中身を読み込みません（コンパイルもされません）。

## 元研究_SDKなど/（Git には入れていません）

元研究のプロジェクト（360-Image）をインポートしたときに一緒に入ってきたファイルのうち、
**本研究に悪影響があるため `Assets/` から退避したもの** です（2026-11-24）。
容量が大きい（約 750MB）ので `.gitignore` で Git の管理から外しています。必要なら元研究のプロジェクトから取り直せます。

| 退避したもの | 退避した理由 |
|---|---|
| `Oculus/`（Oculus Integration SDK） | Unity 6 では廃止された API を使っていて **コンパイルエラー**（`AudioImporter.preloadAudioData` など）。エラーがあると Play できない |
| `SteamVR/`、`SteamVR_Resources/`、`StreamingAssets/SteamVR/` | 同じく **コンパイルエラー**（`PhysicMaterial` → `PhysicsMaterial` に名前が変わった）。本研究は OpenXR を使うので不要 |
| `XR/Loaders/`（Oculus Loader、Open VR Loader、OpenXRLoaderNoPreInit） | 本研究のパッケージには無いローダー。XR の設定に混ざると、OpenXR 以外のランタイムが使われるおそれがある |
| `XR/Loaders/OpenXRLoader.asset`（元研究の版） | 本研究の同名ファイルを **別の GUID で上書きしていた**。そのままだと本研究の XR 設定が OpenXR ローダーを見失う（HMD に映らない）。本研究の版は Git から元に戻した |
| `XR/Settings/`（Oculus Settings、Open VR Settings、OpenXR Editor Settings、OpenXRPackageSettings） | 本研究の `OpenXR Package Settings.asset` と重複する、元研究の設定 |
| `XR/XRGeneralSettings.asset` | 元研究の XR 設定（Oculus ローダーが先頭）。本研究は `XRGeneralSettingsPerBuildTarget.asset` を使う |
| `Scenes/SampleScene.unity`（元研究の版） | 本研究のテンプレートの SampleScene を上書きしていた。本研究の版は Git から元に戻した |

## Assets に残したもの

| 場所 | 内容 |
|---|---|
| `Assets/360-Image/` | 元研究の 360 度画像、マテリアル、シーン、スクリプト（`RotationController.cs` など） |
| `Assets/360-Image/Editor/` | 元研究の `RotationGraphWindow.cs`、`ReadOnlyDrawer.cs`。元は `Assets/Editer/`（綴り違いでエディタ専用フォルダとして扱われず、ビルド時にエラーになる）だったので移動した |
| `Assets/SimpleLowPolyNature/`、`Assets/Sphere100.fbx` | 元研究の 3D シーン用の素材と、360 度画像を貼る球 |

**注意**：元研究のシーン（`360-Image/Scenes/image.unity`、`3D.unity`）は、退避した `OVRCameraRig`（Oculus）と `VelocityEstimator`（SteamVR）を使っています。
そのため、このプロジェクトで開くとカメラが「Missing Prefab」になり、そのままでは再生できません。スクリプトや画像を読むための参考として残しています。
