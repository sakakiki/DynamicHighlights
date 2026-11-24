// =====================================================================
// ExperimentSceneBuilder.cs（エディタ拡張）
// ---------------------------------------------------------------------
// 【このファイルの役割】
//   メニュー「DynamicHighlights → 実験シーンを作成」を押すと、
//   実験用のシーン（Assets/Scenes/Experiment.unity）を自動で組み立てるスクリプトです。
//
// 【なぜ自動で作るのか】
//   シーンを手作業で作ると、「カメラの参照を設定し忘れた」「オブジェクトの角度がずれた」などの
//   ミスが起きやすく、他の人が同じ環境を再現するのも大変です。
//   スクリプトで作れば、何度でも同じシーンを作り直せます。
//   （作った後に Unity 上で手で配置を調整しても大丈夫です）
//
// 【作られるもの】
//   ・ExperimentConfig（設定ファイル）… Assets/Settings/ExperimentConfig.asset（既にあればそれを使う）
//   ・マテリアル（色）              … Assets/Materials/
//   ・シーンの中身：
//       Directional Light   … 太陽の光
//       Floor               … 床
//       DirectionPosts      … 15° ごとの小さな柱（回転している感覚をつかみやすくする目印）
//       ExperimentRig
//         └ Main Camera     … HMD の視点になるカメラ（目の高さ 1.6m）
//       ExperimentSystem    … 実験のスクリプト一式（HeadPoseSource, GazeDirectionController,
//                              ExperimentLogger, ExperimentFlow, DebugHud）
//       InstructionCanvas   … 正面に浮かぶ説明パネル
//       Targets             … 鑑賞対象のオブジェクト（見どころ側に、手前・見どころ・過ぎた方向の3つ。見どころは1か所だけ）
//
// 【エディタ拡張とは】
//   Unity エディタ自体に機能（メニューやボタン）を追加するスクリプトです。
//   Editor フォルダ（またはエディタ専用の asmdef）に置いたスクリプトは、
//   ゲームのビルドには含まれず、Unity エディタの中だけで動きます。
//   [MenuItem("メニューの場所")] を付けた static 関数がメニューに追加されます。
// =====================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DynamicHighlights.EditorTools
{
    /// <summary>
    /// 実験シーンを自動で組み立てるエディタ拡張です。
    /// </summary>
    public static class ExperimentSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Experiment.unity";
        private const string ConfigPath = "Assets/Settings/ExperimentConfig.asset";
        private const string MaterialFolder = "Assets/Materials";

        private const float EyeHeight = 1.6f;      // 目の高さ [m]
        private const float TargetDistance = 6f;   // 鑑賞対象までの距離 [m]
        private const float PostDistance = 9f;     // 目印の柱までの距離 [m]
        private const float NeighborOffset = 25f;  // 見どころから手前/過ぎた方向のオブジェクトまでの角度 [度]

        /// <summary>
        /// メニューから呼ばれる関数です。
        /// </summary>
        [MenuItem("DynamicHighlights/実験シーンを作成", priority = 1)]
        public static void BuildSceneMenu()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("実験シーンを作成",
                    $"{ScenePath} はすでにあります。作り直しますか？\n（シーン上で手作業で変更した内容は消えます。設定ファイルはそのまま使います）",
                    "作り直す", "キャンセル"))
            {
                return;
            }

            // いま開いているシーンに保存していない変更があれば、保存するか聞く
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Build();
        }

        /// <summary>
        /// コマンドライン（-executeMethod）から呼ぶための関数です。確認ダイアログを出しません。
        /// </summary>
        public static void BuildFromCommandLine()
        {
            Build();
        }

        /// <summary>
        /// シーンを組み立てる本体です。
        /// </summary>
        private static void Build()
        {
            // ① 設定ファイルを用意する
            ExperimentConfig config = LoadOrCreateConfig();

            // ② 空のシーンを新しく作る
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");

            // ③ 環境（光・床・目印）
            CreateLight();
            CreateFloor();
            CreateDirectionPosts();

            // ④ カメラ
            var rig = new GameObject("ExperimentRig");
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            var cameraComponent = cameraObject.AddComponent<Camera>();
            cameraComponent.nearClipPlane = 0.05f;
            cameraComponent.farClipPlane = 200f;
            cameraObject.AddComponent<AudioListener>();

            // ⑤ 説明パネルと鑑賞対象
            InstructionPanel panel = CreateInstructionPanel();
            List<TargetObject> targets = CreateTargets(config);

            // ⑥ 実験のスクリプト一式を1つの GameObject にまとめて付け、参照をつなぐ
            var system = new GameObject("ExperimentSystem");
            var headPose = system.AddComponent<HeadPoseSource>();

            var controller = system.AddComponent<GazeDirectionController>();
            controller.config = config;
            controller.headPose = headPose;
            controller.viewCamera = cameraObject.transform;

            var logger = system.AddComponent<ExperimentLogger>();
            logger.controller = controller;

            var flow = system.AddComponent<ExperimentFlow>();
            flow.config = config;
            flow.controller = controller;
            flow.logger = logger;
            flow.instructionPanel = panel;
            flow.targets = targets;

            var hud = system.AddComponent<DebugHud>();
            hud.controller = controller;
            hud.config = config;
            hud.headPose = headPose;

            AttachCameraCheckDriver(controller); // カメラ挙動チェック用（下の関数を参照）

            // ⑦ シーンを保存し、ビルド設定（File → Build Profiles のシーン一覧）に登録する
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ExperimentSceneBuilder] 実験シーンを作成しました：{ScenePath}");
        }

        // -----------------------------------------------------------------
        // カメラ挙動チェック用のコンポーネント
        // -----------------------------------------------------------------

        /// <summary>
        /// いま開いているシーンに CameraCheckDriver（カメラ挙動チェック用）を追加します。
        /// シーンを作り直さずに追加できるので、手作業で変更したシーンでも使えます。
        /// </summary>
        [MenuItem("DynamicHighlights/開いているシーンにカメラ挙動チェックを追加", priority = 11)]
        public static void AddCameraCheckDriverMenu()
        {
            var controller = Object.FindAnyObjectByType<GazeDirectionController>();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("カメラ挙動チェックを追加", "GazeDirectionController が見つかりません。Experiment シーンを開いてください。", "OK");
                return;
            }
            bool added = AttachCameraCheckDriver(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene); // 「変更あり」にする（保存が必要になる）
            EditorUtility.DisplayDialog("カメラ挙動チェックを追加",
                added ? "追加しました。シーンを保存してください（Ctrl+S）。" : "すでに追加されています（参照を設定し直しました）。", "OK");
        }

        /// <summary>
        /// コマンドライン（-executeMethod）から呼ぶための関数です。Experiment シーンを開いて追加し、保存します。
        /// </summary>
        public static void AddCameraCheckDriverFromCommandLine()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = Object.FindAnyObjectByType<GazeDirectionController>();
            if (controller == null)
            {
                Debug.LogError("[ExperimentSceneBuilder] GazeDirectionController が見つかりません。");
                return;
            }
            AttachCameraCheckDriver(controller);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ExperimentSceneBuilder] CameraCheckDriver を追加しました。");
        }

        /// <summary>
        /// GazeDirectionController と同じ GameObject に CameraCheckDriver を付け、参照をつなぎます。
        /// </summary>
        /// <returns>新しく追加したら true、すでにあったら false</returns>
        private static bool AttachCameraCheckDriver(GazeDirectionController controller)
        {
            var driver = controller.GetComponent<CameraCheckDriver>();
            bool added = driver == null;
            if (added) driver = controller.gameObject.AddComponent<CameraCheckDriver>();

            driver.controller = controller;
            driver.headPose = controller.headPose;
            driver.config = controller.config;
            EditorUtility.SetDirty(driver);
            return added;
        }

        // -----------------------------------------------------------------
        // 設定ファイル・マテリアル
        // -----------------------------------------------------------------

        /// <summary>
        /// 設定ファイルを読み込みます。無ければ初期値で新しく作ります。
        /// </summary>
        private static ExperimentConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ExperimentConfig>(ConfigPath);
            if (config != null) return config;

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? "Assets/Settings");
            config = ScriptableObject.CreateInstance<ExperimentConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>
        /// 指定した色のマテリアル（URP の Lit シェーダー）を作ります。既にあれば色だけ更新します。
        /// </summary>
        private static Material GetOrCreateMaterial(string name, Color color)
        {
            Directory.CreateDirectory(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color); // URP の Lit シェーダーでは色の名前が _BaseColor
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        // -----------------------------------------------------------------
        // 環境
        // -----------------------------------------------------------------

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void CreateFloor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(10f, 1f, 10f); // Plane は 10m 四方なので 100m 四方になる
            floor.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Floor", new Color(0.45f, 0.47f, 0.45f));
        }

        /// <summary>
        /// 15° ごとに小さな柱を立てます（回転の目印）。
        /// </summary>
        private static void CreateDirectionPosts()
        {
            var parent = new GameObject("DirectionPosts");
            Material material = GetOrCreateMaterial("Post", new Color(0.3f, 0.3f, 0.3f));
            for (int angle = -180; angle < 180; angle += 15)
            {
                GameObject post = CreatePrimitiveWithoutCollider(PrimitiveType.Cylinder, $"Post_{angle}", parent.transform);
                post.transform.position = DirectionFromYaw(angle) * PostDistance + new Vector3(0f, 0.3f, 0f);
                post.transform.localScale = new Vector3(0.1f, 0.3f, 0.1f);
                post.GetComponent<Renderer>().sharedMaterial = material;
            }

            // 正面（0°）の床に目印を置く
            GameObject front = CreatePrimitiveWithoutCollider(PrimitiveType.Cube, "FrontMarker", parent.transform);
            front.transform.position = new Vector3(0f, 0.01f, 3f);
            front.transform.localScale = new Vector3(0.4f, 0.02f, 1.2f);
            front.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("FrontMarker", new Color(0.9f, 0.9f, 0.9f));
        }

        // -----------------------------------------------------------------
        // 説明パネル
        // -----------------------------------------------------------------

        private static InstructionPanel CreateInstructionPanel()
        {
            // World Space の Canvas を正面 2.5m の位置に置く
            var canvasObject = new GameObject("InstructionCanvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(800f, 450f);
            rect.position = new Vector3(0f, EyeHeight, 2.5f);
            rect.localScale = Vector3.one * 0.0015f; // 800 × 0.0015 = 1.2m の幅

            // 背景（半透明の黒）
            var panelObject = new GameObject("Panel", typeof(RectTransform));
            panelObject.transform.SetParent(canvasObject.transform, false);
            StretchToParent(panelObject.GetComponent<RectTransform>());
            panelObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            // 文字
            var textObject = new GameObject("Message", typeof(RectTransform));
            textObject.transform.SetParent(panelObject.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            StretchToParent(textRect);
            textRect.offsetMin = new Vector2(30f, 30f);
            textRect.offsetMax = new Vector2(-30f, -30f);
            Text text = CreateText(textObject, 34, Color.white);
            text.text = "説明文";

            var panel = canvasObject.AddComponent<InstructionPanel>();
            panel.messageText = text;
            panel.panelRoot = panelObject;
            return panel;
        }

        // -----------------------------------------------------------------
        // 鑑賞対象のオブジェクト
        // -----------------------------------------------------------------

        /// <summary>
        /// 鑑賞対象のオブジェクトを作ります（スライド9）。
        /// 見どころ側（φ が正なら右、負なら左）に、手前（φ∓25°）・見どころ（φ）・過ぎた方向（φ±25°）を置きます。
        /// <para>
        /// 見どころは元研究と同じく1か所だけです。手前と過ぎた方向のオブジェクトは見どころではなく、
        /// 「見どころ以外を見たいとき」の見やすさを測るための目標です（本研究で追加）。
        /// 見どころの横幅は、元研究と同じく見どころ範囲 w の角度に合わせます。
        /// </para>
        /// </summary>
        private static List<TargetObject> CreateTargets(ExperimentConfig config)
        {
            var parent = new GameObject("Targets");
            var targets = new List<TargetObject>();

            float side = config.Phi < 0f ? -1f : 1f; // 見どころが右なら +1、左なら −1
            float phi = Mathf.Abs(config.Phi);
            float before = Mathf.Clamp(phi - NeighborOffset, 5f, 85f);
            float after = Mathf.Clamp(phi + NeighborOffset, 5f, 85f);

            // 見どころの横幅 [m]：距離 D の所で角度 w に見える幅 = 2·D·tan(w/2)
            float highlightDiameter = 2f * TargetDistance * Mathf.Tan(config.W / 2f * Mathf.Deg2Rad);

            targets.Add(CreateTarget(parent.transform, "Before", "手前", side * before,
                PrimitiveType.Cube, new Color(0.25f, 0.45f, 0.85f), 0.8f));
            targets.Add(CreateTarget(parent.transform, "Highlight", "見どころ", side * phi,
                PrimitiveType.Cylinder, new Color(0.9f, 0.7f, 0.2f), highlightDiameter));
            targets.Add(CreateTarget(parent.transform, "After", "過ぎた方向", side * after,
                PrimitiveType.Sphere, new Color(0.3f, 0.7f, 0.4f), 0.8f));
            return targets;
        }

        /// <summary>
        /// 鑑賞対象のオブジェクトを1つ作ります。
        /// </summary>
        /// <param name="yaw">置く方向 [度]（正面 0°、右回りがプラス）</param>
        /// <param name="size">本体の横幅 [m]</param>
        private static TargetObject CreateTarget(Transform parent, string id, string displayName, float yaw,
            PrimitiveType shape, Color color, float size)
        {
            // 根元の GameObject：目の高さに置く（注視の判定はこの位置で行う）
            // LookRotation で「カメラから見て奥の方向」を向かせると、文字が正しい向きで読めます。
            Vector3 direction = DirectionFromYaw(yaw);
            var root = new GameObject($"Target_{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = direction * TargetDistance + new Vector3(0f, EyeHeight, 0f);
            root.transform.rotation = Quaternion.LookRotation(direction);

            // 本体の形
            GameObject body = CreatePrimitiveWithoutCollider(shape, "Body", root.transform);
            body.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial($"Target_{id.Split('_')[0]}", color);
            if (shape == PrimitiveType.Cylinder)
            {
                // 見どころは床から伸びる塔の形（高さ 3.2m、横幅は見どころ範囲 w に合わせる）
                body.transform.localScale = new Vector3(size, EyeHeight, size);
            }
            else
            {
                body.transform.localScale = Vector3.one * size;

                // 台座（床から本体の下まで）
                GameObject stand = CreatePrimitiveWithoutCollider(PrimitiveType.Cylinder, "Stand", root.transform);
                stand.transform.localPosition = new Vector3(0f, -(EyeHeight + 0.4f) / 2f, 0f);
                stand.transform.localScale = new Vector3(0.12f, (EyeHeight - 0.4f) / 2f, 0.12f);
                stand.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Post", new Color(0.3f, 0.3f, 0.3f));
            }

            // 文字の看板（白地に黒文字）
            var canvasObject = new GameObject("LabelCanvas");
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(160f, 60f);
            rect.localPosition = new Vector3(0f, 0.75f, -(size / 2f + 0.2f)); // 本体の少し上・手前（本体の表面より 0.2m 手前）
            rect.localScale = Vector3.one * 0.004f;             // 160 × 0.004 = 0.64m の幅

            var backgroundObject = new GameObject("Background", typeof(RectTransform));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            StretchToParent(backgroundObject.GetComponent<RectTransform>());
            backgroundObject.AddComponent<Image>().color = Color.white;

            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            StretchToParent(textObject.GetComponent<RectTransform>());
            Text text = CreateText(textObject, 40, Color.black);
            text.text = "ABC";

            var target = root.AddComponent<TargetObject>();
            target.targetId = id;
            target.displayName = displayName;
            target.labelText = text;
            target.bodyRenderer = body.GetComponent<Renderer>();
            return target;
        }

        // -----------------------------------------------------------------
        // 小さな便利関数
        // -----------------------------------------------------------------

        /// <summary>
        /// ヨー角 [度] から水平方向のベクトルを作ります。0° → +Z（正面）、90° → +X（右）
        /// </summary>
        private static Vector3 DirectionFromYaw(float yaw)
        {
            float radian = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radian), 0f, Mathf.Cos(radian));
        }

        /// <summary>
        /// 当たり判定（Collider）の無いプリミティブ（立方体・球など）を作ります。
        /// この実験では物理演算を使わないので、当たり判定は消しておきます。
        /// </summary>
        private static GameObject CreatePrimitiveWithoutCollider(PrimitiveType type, string name, Transform parent)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            return obj;
        }

        /// <summary>UI の大きさを親いっぱいに広げます。</summary>
        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>中央揃えの Text を作ります。（日本語フォントは実行時に JapaneseFont で設定されます）</summary>
        private static Text CreateText(GameObject target, int fontSize, Color color)
        {
            Text text = target.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// シーンをビルド設定の一覧の先頭に登録します（既に登録されていれば先頭に移動）。
        /// </summary>
        private static void AddSceneToBuildSettings(string path)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
                .Where(s => s.path != path)
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
