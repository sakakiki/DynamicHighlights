using UnityEngine;
using UnityEditor;

public class RotationGraphWindow : EditorWindow
{
    [MenuItem("Window/Rotation Graph")]
    public static void ShowWindow()
    {
        GetWindow<RotationGraphWindow>("Rotation Graph");
    }

    public RotationController rotationController;
    private int resolution = 200;
    private float rMin = -90f;
    private float rMax = 90f;

    private GUIStyle labelStyle;

    private void OnEnable()
    {
        labelStyle = new GUIStyle(EditorStyles.label);
        labelStyle.normal.textColor = Color.white;
        labelStyle.fontSize = 10;
    }

    private void OnGUI()
    {
        rotationController =
            (RotationController)EditorGUILayout.ObjectField(
                "Rotation Controller",
                rotationController,
                typeof(RotationController),
                true
            );

        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 10, 500);

        if (rotationController == null)
        {
            EditorGUILayout.HelpBox("RotationController ÇÉAÉ^ÉbÉ`ÇµÇƒÇ≠ÇæÇ≥Ç¢", MessageType.Warning);
            return;
        }

        Rect graphRect = GUILayoutUtility.GetRect(500, 350);
        DrawGraph(graphRect);
    }

    private void DrawGraph(Rect rect)
    {
        Handles.BeginGUI();

        // --- îwåi ---
        EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f));

        // --- ògê¸ ---
        Handles.color = Color.gray;
        Handles.DrawLine(rect.min, new Vector2(rect.xMax, rect.yMin));
        Handles.DrawLine(rect.min, new Vector2(rect.xMin, rect.yMax));
        Handles.DrawLine(rect.max, new Vector2(rect.xMin, rect.yMax));
        Handles.DrawLine(rect.max, new Vector2(rect.xMax, rect.yMin));

        // --- íÜâõé≤Åi0Åj ---
        float zeroT = (-rMin) / (rMax - rMin);
        float zeroX = Mathf.Lerp(rect.x, rect.xMax, zeroT);
        float zeroY = Mathf.Lerp(rect.yMax, rect.y, zeroT);

        Handles.color = new Color(1f, 1f, 1f, 0.25f);
        Handles.DrawLine(new Vector2(rect.x, zeroY), new Vector2(rect.xMax, zeroY));
        Handles.DrawLine(new Vector2(zeroX, rect.y), new Vector2(zeroX, rect.yMax));

        // --- ñ⁄ê∑ ---
        float[] ticks = { -90f, -45f, 0f, 45f, 90f };
        foreach (float tick in ticks)
        {
            float ty = Mathf.Lerp(rect.yMax, rect.y, (tick - rMin) / (rMax - rMin));
            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawLine(new Vector2(rect.x, ty), new Vector2(rect.xMax, ty));
            GUI.Label(new Rect(rect.x - 40, ty - 8, 35, 16), tick.ToString(), labelStyle);
        }

        // --- ÉOÉâÉtï`âÊ ---
        Vector2 prevR = Vector2.zero;
        Vector2 prevV = Vector2.zero;

        for (int i = 0; i <= resolution; i++)
        {
            float t = (float)i / resolution;
            float r = Mathf.Lerp(rMin, rMax, t);

            float additional = rotationController.CalcAdditionalRotate(r);
            float v = r + additional;

            Vector2 rPos = new Vector2(
                Mathf.Lerp(rect.x, rect.xMax, t),
                Mathf.Lerp(rect.yMax, rect.y, (r - rMin) / (rMax - rMin))
            );

            Vector2 vPos = new Vector2(
                Mathf.Lerp(rect.x, rect.xMax, t),
                Mathf.Lerp(rect.yMax, rect.y, (v - rMin) / (rMax - rMin))
            );

            if (i > 0)
            {
                Handles.color = Color.red;
                Handles.DrawLine(prevR, rPos);

                Handles.color = Color.green;
                Handles.DrawLine(prevV, vPos);
            }

            prevR = rPos;
            prevV = vPos;
        }

        // --- ñ}ó· ---
        float lx = rect.x + 10;
        float ly = rect.y + 10;

        EditorGUI.DrawRect(new Rect(lx, ly, 12, 12), Color.red);
        GUI.Label(new Rect(lx + 18, ly - 2, 80, 16), "r (input)", labelStyle);

        EditorGUI.DrawRect(new Rect(lx, ly + 16, 12, 12), Color.green);
        GUI.Label(new Rect(lx + 18, ly + 14, 80, 16), "v (output)", labelStyle);

        Handles.EndGUI();
    }
}
