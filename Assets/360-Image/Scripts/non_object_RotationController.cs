using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;


public class non_object_RotationController : MonoBehaviour
{
    [SerializeField] Transform centerEyeAnchor;
    float rotateY;
    [SerializeField, ReadOnly] float r;
    [SerializeField, ReadOnly] float v;
    [SerializeField, ReadOnly] float additionalRotate;

    [Header("回転補正関数選択（同時に複数オンにしないこと）")]
    [SerializeField] bool liner;  // 原点、両端通る線形
    [SerializeField] bool bezier;  // 3次ベジェ曲線

    [Header("折れ線モデルパラメータ")]
    [SerializeField] float t = 45;
    public float T => t;
    [SerializeField] float w = 20.0f;
    [SerializeField] float s = 0.55f;
    [SerializeField] float L = 5f; // 丸め長さ

    public float CalcAdditionalRotate(float r)
    {
        float result = 0f;

        if (liner)
        {
            // --- 折れ線型（修正版）誇張関数 ---
            float x1 = -90f, y1 = -90f;
            float x4 = 90f, y4 = 90f;

            // 折れ線の節点計算
            float x2 = t - (w / 2f) / s;
            float x3 = t + (w / 2f) / s;
            float y2 = s * (x2 - t) + t;
            float y3 = s * (x3 - t) + t;

            float y;

            if (x2 > 0)
            {
                // L2がr>0にある場合（左を2分割）
                if (r < 0)
                {
                    // (-90, -90) → (0, 0)
                    y = y1 + (0 - y1) * (r - x1) / (0 - x1);
                }
                else if (r < x2)
                {
                    // (0, 0) → (x2, y2)
                    y = 0 + (y2 - 0) * (r - 0) / (x2 - 0);
                }
                else if (r < x3)
                {
                    // (x2, y2) → (x3, y3)
                    y = y2 + (y3 - y2) * (r - x2) / (x3 - x2);
                }
                else
                {
                    // (x3, y3) → (90, 90)
                    y = y3 + (y4 - y3) * (r - x3) / (x4 - x3);
                }
            }
            else if (x3 < 0)
            {
                // L2がr<0にある場合            
                if (r < x2)
                {
                    // (-90, -90) → (x2, y2)
                    y = y1 + (y2 - y1) * (r - x1) / (x2 - x1);
                }
                else if (r < x3)
                {
                    // (x2, y2) → (x3, y3)
                    y = y2 + (y3 - y2) * (r - x2) / (x3 - x2);
                }
                else if (r < 0)
                {
                    // (x3, y3) → (0, 0)
                    y = y3 + (0 - y3) * (r - x3) / (0 - x3);
                }
                else
                {
                    // (0, 0) → (90, 90)
                    y = 0 + (y4 - 0) * (r - 0) / (x4 - 0);
                }
            }
            else
            {
                // x2 <= 0 <= x3 の場合
                float b = s * (0 - t) + t;
                y2 -= b;
                y3 -= b;

                if (r < x2)
                {
                    // (-90, -90) → (x2, y2)
                    y = y1 + (y2 - y1) * (r - x1) / (x2 - x1);
                }
                else if (r < x3)
                {
                    // (x2, y2) → (x3, y3)
                    y = y2 + (y3 - y2) * (r - x2) / (x3 - x2);
                }
                else
                {
                    // (x3, y3) → (90, 90)
                    y = y3 + (y4 - y3) * (r - x3) / (x4 - x3);
                }
            }

            // rとの差（誇張量）を追加回転に
            result = y - r;
        }

        else if (bezier)
        {
            // --- 固定端点 ---
            float x1 = -90f, y1 = -90f;
            float x4 = 90f, y4 = 90f;

            // --- 折れ線節点 ---
            float x2 = t - (w / 2f) / s;
            float x3 = t + (w / 2f) / s;
            float y2 = s * (x2 - t) + t;
            float y3 = s * (x3 - t) + t;

            // ===== Python: calc_segments =====
            List<Vector2> basePoints = new List<Vector2>();

            if (x2 > 0f)
            {
                basePoints.Add(new Vector2(x1, y1));
                basePoints.Add(Vector2.zero);
                basePoints.Add(new Vector2(x2, y2));
                basePoints.Add(new Vector2(x3, y3));
                basePoints.Add(new Vector2(x4, y4));
            }
            else if (x3 < 0f)
            {
                basePoints.Add(new Vector2(x1, y1));
                basePoints.Add(new Vector2(x2, y2));
                basePoints.Add(new Vector2(x3, y3));
                basePoints.Add(Vector2.zero);
                basePoints.Add(new Vector2(x4, y4));
            }
            else
            {
                float b = s * (0f - t) + t;
                y2 -= b;
                y3 -= b;

                basePoints.Add(new Vector2(x1, y1));
                basePoints.Add(new Vector2(x2, y2));
                basePoints.Add(new Vector2(x3, y3));
                basePoints.Add(new Vector2(x4, y4));
            }

            // ===== cubic_bezier =====
            Vector2[] CubicBezier(Vector2 P0, Vector2 P1, Vector2 P2, Vector2 P3, int n = 50)
            {
                Vector2[] pts = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float tt = i / (float)(n - 1);
                    float u = 1f - tt;
                    pts[i] =
                        u * u * u * P0 +
                        3f * u * u * tt * P1 +
                        3f * u * tt * tt * P2 +
                        tt * tt * tt * P3;
                }
                return pts;
            }

            // ===== rounded_segment =====
            Vector2[] RoundedSegment(Vector2 prev, Vector2 center, Vector2 next, float len)
            {
                Vector2 vPrev = center - prev;
                Vector2 vNext = next - center;

                float lp = vPrev.magnitude;
                float ln = vNext.magnitude;
                if (lp == 0f || ln == 0f)
                    return new Vector2[] { center };

                Vector2 uPrev = vPrev / lp;
                Vector2 uNext = vNext / ln;

                Vector2 P0 = center - uPrev * len;
                Vector2 P3 = center + uNext * len;
                Vector2 P1 = P0 + uPrev * (len * 0.5f);
                Vector2 P2 = P3 - uNext * (len * 0.5f);

                return CubicBezier(P0, P1, P2, P3);
            }

            // ===== 折れ線 + 丸め結合 =====
            List<Vector2> smoothPoints = new List<Vector2>();

            for (int i = 0; i < basePoints.Count; i++)
            {
                if (i > 0 && i < basePoints.Count - 1)
                {
                    smoothPoints.AddRange(
                        RoundedSegment(
                            basePoints[i - 1],
                            basePoints[i],
                            basePoints[i + 1],
                            L
                        )
                    );
                }
                else
                {
                    smoothPoints.Add(basePoints[i]);
                }
            }

            // ===== r → v 評価 =====
            float v = r;
            bool found = false;

            for (int i = 0; i < smoothPoints.Count - 1; i++)
            {
                if (r >= smoothPoints[i].x && r <= smoothPoints[i + 1].x)
                {
                    float tl =
                        (r - smoothPoints[i].x) /
                        (smoothPoints[i + 1].x - smoothPoints[i].x);

                    v = Mathf.Lerp(smoothPoints[i].y, smoothPoints[i + 1].y, tl);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                if (r < smoothPoints[0].x)
                {
                    v = y1 +
                        (smoothPoints[0].y - y1) *
                        (r - x1) / (smoothPoints[0].x - x1);
                }
                else
                {
                    int last = smoothPoints.Count - 1;
                    v = smoothPoints[last].y +
                        (y4 - smoothPoints[last].y) *
                        (r - smoothPoints[last].x) /
                        (x4 - smoothPoints[last].x);
                }
            }

            result = v - r;
        }

        return result;
    }

    void Update()
    {
        // 実視線方向
        rotateY = centerEyeAnchor.localEulerAngles.y;
        r = rotateY > 180 ? rotateY - 360 : rotateY;

        additionalRotate = CalcAdditionalRotate(r);


        this.transform.localRotation = Quaternion.Euler(0, additionalRotate, 0);
        //追加の回転量を決める関数、y=rなら2倍、45なら45°方向から始まる

        // --- 仮想視線（映像方向） ---
        v = r + additionalRotate;
    }
}
