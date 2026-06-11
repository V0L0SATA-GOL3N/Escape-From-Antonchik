using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DoorController))]
public class DoorControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        DoorController door = (DoorController)target;

        if (GUILayout.Button("Toggle Door"))
        {
            door.ToggleDoor();

            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(door);
            }
        }
    }
}
