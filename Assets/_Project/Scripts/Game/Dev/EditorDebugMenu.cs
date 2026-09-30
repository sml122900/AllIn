#if UNITY_EDITOR
using AllIn.Game.LoopScene;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AllIn.Game.Dev
{
    /// <summary>Editor-only debug menu (F9). Exists to check the companion rendering path (DESIGN.md E9); it is compiled out of builds.</summary>
    public sealed class EditorDebugMenu : MonoBehaviour
    {
        bool _open;
        LoopApp _app;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) _open = !_open;
        }

        void OnGUI()
        {
            if (!_open) return;
            if (_app == null) _app = FindAnyObjectByType<LoopApp>();
            if (_app == null || _app.Loop == null) return;
            GUILayout.BeginArea(new Rect(20, 140, 260, 220), GUI.skin.box);
            GUILayout.Label("DEBUG (editor only) — F9");
            if (GUILayout.Button("companions: spouse")) _app.DebugSetCompanions(true, false);
            if (GUILayout.Button("companions: spouse + child")) _app.DebugSetCompanions(true, true);
            if (GUILayout.Button("companions: none")) _app.DebugSetCompanions(false, false);
            GUILayout.EndArea();
        }
    }
}
#endif
