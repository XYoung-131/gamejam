using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class EventPanelChecks
{
    [MenuItem("Tools/Game/Check Event Option Callbacks")]
    public static void Run()
    {
        int chosen = -1;
        EventPanel panel = EventPanel.Show("Check", "Check", new[] { "Continue" },
            new UnityAction<int>[] { index => chosen = index });
        try
        {
            // Edit-mode checks do not automatically run MonoBehaviour.Awake.
            if (panel.GetComponent<Canvas>() == null)
                typeof(EventPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(panel, null);

            Button option = Array.Find(panel.GetComponentsInChildren<Button>(true),
                button => button.name == "OptionSlot_0");
            if (option == null) throw new Exception("Event option button was not created");
            option.onClick.Invoke();
            if (chosen != 0) throw new Exception("Single event option did not invoke its callback");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(panel.gameObject);
        }
    }
}
