using UnityEngine;

/// <summary>
/// 登录态。token 存 PlayerPrefs，重启 App 不用重新登录。
/// token 本身 12 小时过期，过期后请求返回 401，由流程控制清掉并回登录页。
/// </summary>
public static class Session
{
    private const string KeyToken = "arwork.token";
    private const string KeyUser = "arwork.username";
    private const string KeyRole = "arwork.role";

    public static string Token { get; private set; }
    public static string Username { get; private set; }
    public static string Role { get; private set; }

    public static bool IsLoggedIn
    {
        get { return !string.IsNullOrEmpty(Token); }
    }

    public static bool IsAdmin
    {
        get { return Role == "admin"; }
    }

    /// <summary>启动时读一次。</summary>
    public static void Load()
    {
        Token = PlayerPrefs.GetString(KeyToken, string.Empty);
        Username = PlayerPrefs.GetString(KeyUser, string.Empty);
        Role = PlayerPrefs.GetString(KeyRole, string.Empty);
    }

    public static void Save(LoginResponse response)
    {
        Token = response.token;
        Username = response.username;
        Role = response.role;

        PlayerPrefs.SetString(KeyToken, Token);
        PlayerPrefs.SetString(KeyUser, Username);
        PlayerPrefs.SetString(KeyRole, Role);
        PlayerPrefs.Save();
    }

    public static void Clear()
    {
        Token = string.Empty;
        Username = string.Empty;
        Role = string.Empty;

        PlayerPrefs.DeleteKey(KeyToken);
        PlayerPrefs.DeleteKey(KeyUser);
        PlayerPrefs.DeleteKey(KeyRole);
        PlayerPrefs.Save();
    }
}
