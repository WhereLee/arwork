using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 输入一段文字的弹窗：修复者交活时写处理记录，退回时写原因，两者共用一个面板。
/// </summary>
public class ReasonDialog : MonoBehaviour
{
    [Tooltip("整个弹窗面板")]
    public GameObject panel;

    public Text titleText;
    public InputField input;
    public Text hintText;
    public Button confirmButton;
    public Button cancelButton;

    /// <summary>点确定时把输入内容交出去。</summary>
    public event Action<string> Confirmed;

    /// <summary>点取消或关闭。</summary>
    public event Action Cancelled;

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(Close);
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    public void Show(string title, string placeholder)
    {
        if (titleText != null)
        {
            titleText.text = title;
        }

        if (input != null)
        {
            input.text = string.Empty;
            if (input.placeholder != null)
            {
                input.placeholder.GetComponent<Text>().text = placeholder;
            }
        }

        if (hintText != null)
        {
            hintText.text = string.Empty;
        }

        if (panel != null)
        {
            panel.SetActive(true);
        }
    }

    public void Close()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (Cancelled != null)
        {
            Cancelled();
        }
    }

    private void OnConfirm()
    {
        string note = input != null && input.text != null ? input.text.Trim() : string.Empty;

        if (note.Length == 0)
        {
            if (hintText != null)
            {
                hintText.text = "这一项必须填";
                hintText.color = new Color(1f, 0.55f, 0.5f);
            }

            return;
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (Confirmed != null)
        {
            Confirmed(note);
        }
    }
}
