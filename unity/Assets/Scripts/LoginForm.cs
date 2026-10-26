using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录页：账号、密码、登录按钮、错误提示。
/// 登录成功后把 token 交给 Session，再由流程控制进入引导页。
/// </summary>
public class LoginForm : MonoBehaviour
{
    [Tooltip("整个登录面板")]
    public GameObject panel;

    public InputField usernameInput;
    public InputField passwordInput;
    public InputField serverInput;
    public Button loginButton;
    public Text statusText;

    /// <summary>登录成功后触发。</summary>
    public event System.Action LoggedIn;

    private static readonly Color ErrorColor = new Color(1f, 0.55f, 0.5f);
    private static readonly Color BusyColor = new Color(0.75f, 0.8f, 0.88f);
    private static readonly Color SuccessColor = new Color(0.55f, 0.95f, 0.62f);

    private bool _busy;

    private void Awake()
    {
        if (loginButton != null)
        {
            loginButton.onClick.AddListener(delegate { StartCoroutine(Submit()); });
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    /// <summary>显示登录页，清掉上一轮的密码。</summary>
    public void Show()
    {
        _busy = false;

        if (passwordInput != null)
        {
            passwordInput.text = string.Empty;
        }

        if (loginButton != null)
        {
            loginButton.interactable = true;
        }

        SetStatus(string.Empty, BusyColor);

        if (panel != null)
        {
            panel.SetActive(true);
        }
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private IEnumerator Submit()
    {
        if (_busy)
        {
            yield break;
        }

        string username = usernameInput != null && usernameInput.text != null
            ? usernameInput.text.Trim()
            : string.Empty;

        string password = passwordInput != null && passwordInput.text != null
            ? passwordInput.text
            : string.Empty;

        if (username.Length == 0 || password.Length == 0)
        {
            SetStatus("账号和密码都要填", ErrorColor);
            yield break;
        }

        _busy = true;
        if (loginButton != null) loginButton.interactable = false;
        SetStatus("正在登录…", BusyColor);

        string baseUrl = serverInput != null && !string.IsNullOrEmpty(serverInput.text)
            ? serverInput.text.Trim()
            : ApiClient.DefaultBaseUrl;

        LoginResponse response = null;
        string error = null;

        yield return ApiClient.Login(baseUrl, username, password,
            delegate (LoginResponse ok) { response = ok; },
            delegate (string message) { error = message; });

        _busy = false;
        if (loginButton != null) loginButton.interactable = true;

        if (error != null)
        {
            SetStatus("登录失败：" + error, ErrorColor);
            yield break;
        }

        Session.Save(response);
        SetStatus("登录成功", SuccessColor);

        Debug.Log("[AR] 登录成功：" + response.username + "（" + response.role + "）");

        if (LoggedIn != null)
        {
            LoggedIn();
        }
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = message;
        statusText.color = color;
    }
}
