using UnityEngine;
using TMPro;

public class ProtractorInSphere : MonoBehaviour
{
    [Header("=== 文字設定（30度刻み） ===")]
    public float fontSize = 25f;
    public float labelRadiusScale = 0.7f;
    
    [Tooltip("文字の傾き角度（度）")]
    public float labelTiltAngle = 20f;

    [Header("=== 線設定（15度刻み） ===")]
    public float lineWidth = 0.05f;

    [Tooltip("目盛り線の始点半径（スフィア半径比）")]
    public float tickInnerRadiusScale = 0.35f;

    [Tooltip("目盛り線の終点半径（スフィア半径比）")]
    public float tickOuterRadiusScale = 0.5f;

    [Tooltip("目盛り線の終点の高さオフセット")]
    public float tickHeightOffset = 2f;

    [Header("=== 全体回転オフセット ===")]
    [Tooltip("0度基準のy軸回転調整（度）")]
    public float yawOffsetDeg = 0f;

    [Header("=== 上下オフセット ===")]
    [Tooltip("ワールドY方向への平行移動量")]
    public float yOffset = -7f;


    // 前回の値を保持
    private float lastFontSize;
    private float lastLabelRadiusScale;
    private float lastLabelTiltAngle;
    private float lastLineWidth;
    private float lastTickInnerRadiusScale;
    private float lastTickOuterRadiusScale;
    private float lastTickHeightOffset;
    private float lastYawOffsetDeg;
    private float lastYOffset;

    void Start()
    {
        RegenerateProtractor();
        SaveLastValues();
    }

    void Update()
    {
        // 値が変更されたかチェック
        if (HasValuesChanged())
        {
            RegenerateProtractor();
            SaveLastValues();
        }
    }

    bool HasValuesChanged()
    {
        return !Mathf.Approximately(fontSize, lastFontSize) ||
               !Mathf.Approximately(labelRadiusScale, lastLabelRadiusScale) ||
               !Mathf.Approximately(labelTiltAngle, lastLabelTiltAngle) ||
               !Mathf.Approximately(lineWidth, lastLineWidth) ||
               !Mathf.Approximately(tickInnerRadiusScale, lastTickInnerRadiusScale) ||
               !Mathf.Approximately(tickOuterRadiusScale, lastTickOuterRadiusScale) ||
               !Mathf.Approximately(tickHeightOffset, lastTickHeightOffset) ||
               !Mathf.Approximately(yawOffsetDeg, lastYawOffsetDeg) ||
               !Mathf.Approximately(yOffset, lastYOffset);
    }

    void SaveLastValues()
    {
        lastFontSize = fontSize;
        lastLabelRadiusScale = labelRadiusScale;
        lastLabelTiltAngle = labelTiltAngle;
        lastLineWidth = lineWidth;
        lastTickInnerRadiusScale = tickInnerRadiusScale;
        lastTickOuterRadiusScale = tickOuterRadiusScale;
        lastTickHeightOffset = tickHeightOffset;
        lastYawOffsetDeg = yawOffsetDeg;
        lastYOffset = yOffset;
    }

    void RegenerateProtractor()
    {
        // 既存の目盛りとラベルを削除
        ClearProtractor();

        float sphereRadius = GetSphereRadius();
        CreateTicks(sphereRadius);
        CreateLabels(sphereRadius);
    }

    void ClearProtractor()
    {
        // 子オブジェクトを全て削除
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

    void CreateTicks(float sphereRadius)
    {
        for (int angle = -90; angle <= 90; angle += 15)
        {
            float rad = (angle + yawOffsetDeg) * Mathf.Deg2Rad;

            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

            float innerR = sphereRadius * tickInnerRadiusScale;
            float outerR = sphereRadius * tickOuterRadiusScale;

            Vector3 inner = transform.position + dir * innerR + Vector3.up * yOffset;
            Vector3 outer = transform.position + dir * outerR + Vector3.up * (yOffset + tickHeightOffset);

            GameObject go = new GameObject($"Tick_{angle}");
            go.transform.SetParent(transform, true);

            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.SetPosition(0, inner);
            lr.SetPosition(1, outer);

            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;

            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = Color.black;
            lr.endColor = Color.black;
        }
    }

    void CreateLabels(float sphereRadius)
    {
        float labelRadius = sphereRadius * labelRadiusScale;

        for (int angle = -90; angle <= 90; angle += 30)
        {
            float rad = (angle + yawOffsetDeg) * Mathf.Deg2Rad;

            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
            Vector3 pos = transform.position + dir * labelRadius + Vector3.up * (yOffset + tickHeightOffset);

            GameObject go = new GameObject($"Label_{angle}");
            go.transform.SetParent(transform, true);
            go.transform.position = pos;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = angle.ToString();
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            // XZ平面に配置し、傾き角度を適用
            go.transform.rotation = Quaternion.Euler(90f - labelTiltAngle, angle + yawOffsetDeg, 0f);
        }
    }

    float GetSphereRadius()
    {
        var r = GetComponent<Renderer>();
        if (r != null) return r.bounds.extents.x;
        return 0.5f;
    }
}