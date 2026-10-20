using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Small runtime interlude UI; narrative events can replace its placeholder.</summary>
public sealed class RunFlowUI : MonoBehaviour
{
    private GameObject root;
    private Font font;

    public void Hide()
    {
        if (root != null) Destroy(root);
        root = null;
    }

    public void Show(RunProgression run)
    {
        Hide();
        if (run == null || run.Phase == RunPhase.Battle || run.Phase == RunPhase.Reward) return;
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("RunEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        root = new GameObject("RunInterlude", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        Image background = new GameObject("Backdrop", typeof(Image)).GetComponent<Image>();
        background.transform.SetParent(root.transform, false);
        background.color = new Color(.03f, .06f, .12f, .96f);
        RectTransform bg = background.rectTransform;
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;

        string title = run.Phase == RunPhase.Menu ? "聚光灯" :
            run.Phase == RunPhase.Event ? run.EventId :
            run.Phase == RunPhase.Rest ? "休息处" :
            run.Phase == RunPhase.Victory ? "实验结束 · 通关" : "实验结束 · 失败";
        Label(title, 52, 255);
        if (run.Phase == RunPhase.Menu) Button("新游戏", 50, () => RunFlowController.Instance.StartNewGame());
        else if (run.Phase == RunPhase.Event)
        {
            Label("事件内容待接入", 28, 135);
            Button("继续", -20, () => RunFlowController.Instance.CompleteEvent());
        }
        else if (run.Phase == RunPhase.Rest)
        {
            Button("回复 50% 最大生命值", 100, () => RunFlowController.Instance.ChooseRest(RestOption.HealHalf));
            Button("生命上限 +2，回复 25% 最大生命值", -10, () => RunFlowController.Instance.ChooseRest(RestOption.HealthAndHealQuarter));
            Button("生命上限 +2，耐力和思绪上限各 +5", -120, () => RunFlowController.Instance.ChooseRest(RestOption.HealthAndResources));
        }
        else Button("返回主界面", -20, () => RunFlowController.Instance.ReturnToMenu());
    }

    private void Label(string value, int size, float y)
    {
        Text label = new GameObject("Label", typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(root.transform, false);
        label.font = font; label.text = value; label.fontSize = size;
        label.color = Color.white; label.alignment = TextAnchor.MiddleCenter;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(1200, 100);
        rect.anchoredPosition = new Vector2(0, y);
    }

    private void Button(string label, float y, Action onClick)
    {
        GameObject go = new GameObject(label, typeof(Image), typeof(Button));
        go.transform.SetParent(root.transform, false);
        Image image = go.GetComponent<Image>();
        image.color = new Color(.56f, .76f, .9f);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(720, 82);
        rect.anchoredPosition = new Vector2(0, y);
        go.GetComponent<Button>().onClick.AddListener(() => onClick());
        Text text = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(go.transform, false);
        text.font = font; text.text = label; text.fontSize = 30;
        text.color = new Color(.07f, .12f, .22f); text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        RectTransform inner = text.rectTransform;
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = inner.offsetMax = Vector2.zero;
    }

    private void OnDestroy() => Hide();
}
