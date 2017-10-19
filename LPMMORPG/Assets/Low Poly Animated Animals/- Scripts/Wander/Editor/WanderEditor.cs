using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Wander))]
public class WanderEditor : Editor
{
    private Wander wander;

    private float minValue = 0f;
    private float maxValue = 10f;

    public void OnEnable()
    {
        wander = (Wander)target;
    }

    public override void OnInspectorGUI()
    {
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Walk Speed", GUILayout.Width(100));
        wander.WalkSpeed = EditorGUILayout.Slider(wander.WalkSpeed, 0, 15);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Turn Speed", GUILayout.Width(100));
        wander.TurnSpeed = EditorGUILayout.Slider(wander.TurnSpeed, 0, 15);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Min Idle Time", GUILayout.Width(100));
        wander.MinIdleTime = EditorGUILayout.Slider(wander.MinIdleTime, 0, 20);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Max Idle Time", GUILayout.Width(100));
        wander.MaxIdleTime = EditorGUILayout.Slider(wander.MaxIdleTime, 0, 20);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Wander Range", GUILayout.Width(100));
        wander.WanderRange = EditorGUILayout.Slider(wander.WanderRange, 0, 25);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (wander.showGizmos)
        {
            if (GUILayout.Button("Dsiable Scene Gizmos"))
            {
                wander.showGizmos = false;
                SceneView.RepaintAll();
                Repaint();
            }
        }
        else
        {
            if (GUILayout.Button("Enable Scene Gizmos"))
            {
                wander.showGizmos = true;
                SceneView.RepaintAll();
                Repaint();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
    }
}
