using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public struct Vec3
{
    public float x;
    public float y;
    public float z;

    public Vec3(Vector3 source)
    {
        x = source.x;
        y = source.y;
        z = source.z;
    }
}

[Serializable]
public class CreateIssuePayload
{
    public string title;
    public string description;
    public string priority;
    public Vec3 position;
}

[Serializable]
public class IssueResponse
{
    public string id;
    public string title;
    public string status;
}

[Serializable]
public class ApiErrorBody
{
    public string error;
}

[Serializable]
public class LoginPayload
{
    public string username;
    public string password;
}

[Serializable]
public class LoginResponse
{
    public string token;
    public string username;
    public string role;
}

/// <summary>列表里每条问题只取展示需要的字段。</summary>
[Serializable]
public class IssueSummary
{
    public string id;
    public string title;
    public string description;
    public string priority;
    public string status;
    public string reporter;
    public string createdAt;
}

/// <summary>JsonUtility 不能解析顶层数组，用它包一层。</summary>
[Serializable]
public class IssueCollection
{
    public IssueSummary[] items;
}

/// <summary>
/// 与 Go 后端的 HTTP 交互。字段名与作业任务书 §4 的结构保持一致。
/// 所有错误都会翻译成使用者能看懂的中文。
/// </summary>
public static class ApiClient
{
    /// <summary>电脑在局域网里的地址，手机与电脑需处于同一网络。</summary>
    public const string DefaultBaseUrl = "http://192.168.1.5:8080";

    /// <summary>
    /// 最近一次使用的地址。在登录页或表单里改过服务器地址后，
    /// 列表和后续提交都会跟着用新地址，不用再改第二遍。
    /// </summary>
    public static string LastBaseUrl = DefaultBaseUrl;

    /// <summary>登录态失效（401）时触发，由流程控制负责回到登录页。</summary>
    public static event Action Unauthorized;

    /// <summary>登录：成功时返回 token、用户名和角色。</summary>
    public static IEnumerator Login(
        string baseUrl,
        string username,
        string password,
        Action<LoginResponse> onSuccess,
        Action<string> onError)
    {
        var payload = new LoginPayload { username = username, password = password };
        string url = baseUrl.TrimEnd('/') + "/login";
        LastBaseUrl = baseUrl.TrimEnd('/');

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                onError("连不上服务器，请确认手机与电脑在同一 WiFi");
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError(ReadServerMessage(request));
                yield break;
            }

            LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
            if (response == null || string.IsNullOrEmpty(response.token))
            {
                onError("服务器返回的登录信息不完整");
                yield break;
            }

            onSuccess(response);
        }
    }

    /// <summary>取问题列表。巡检员拿到的是自己报的，管理员拿到全部。</summary>
    public static IEnumerator ListIssues(
        string baseUrl,
        string token,
        Action<IssueSummary[]> onSuccess,
        Action<string> onError)
    {
        string url = baseUrl.TrimEnd('/') + "/issues";
        LastBaseUrl = baseUrl.TrimEnd('/');

        using (var request = UnityWebRequest.Get(url))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                onError("连不上服务器，请确认手机与电脑在同一 WiFi");
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                {
                    NotifyUnauthorized();
                }

                onError(ReadServerMessage(request));
                yield break;
            }

            // 后端返回的是裸数组，包一层对象再交给 JsonUtility
            string json = "{\"items\":" + request.downloadHandler.text + "}";
            IssueCollection parsed = JsonUtility.FromJson<IssueCollection>(json);

            onSuccess(parsed != null && parsed.items != null ? parsed.items : new IssueSummary[0]);
        }
    }

    /// <summary>提交一个问题；成功时回调返回后端生成的 id，失败时回调返回中文原因。</summary>
    public static IEnumerator CreateIssue(
        string baseUrl,
        string token,
        string title,
        string description,
        string priority,
        Vector3 position,
        Action<string> onSuccess,
        Action<string> onError)
    {
        var payload = new CreateIssuePayload
        {
            title = title,
            description = description,
            priority = priority,
            position = new Vec3(position),
        };

        string body = JsonUtility.ToJson(payload);
        string url = baseUrl.TrimEnd('/') + "/issues";
        LastBaseUrl = baseUrl.TrimEnd('/');

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                onError("连不上服务器，请确认手机与电脑在同一 WiFi，且后端已启动");
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                {
                    NotifyUnauthorized();
                }

                onError(ReadServerMessage(request));
                yield break;
            }

            IssueResponse issue = JsonUtility.FromJson<IssueResponse>(request.downloadHandler.text);
            onSuccess(issue != null ? issue.id : string.Empty);
        }
    }

    private static void NotifyUnauthorized()
    {
        if (Unauthorized != null)
        {
            Unauthorized();
        }
    }

    /// <summary>优先用后端返回的中文错误，没有就按状态码翻译。</summary>
    private static string ReadServerMessage(UnityWebRequest request)
    {
        try
        {
            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(text))
            {
                ApiErrorBody parsed = JsonUtility.FromJson<ApiErrorBody>(text);
                if (parsed != null && !string.IsNullOrEmpty(parsed.error))
                {
                    return parsed.error;
                }
            }
        }
        catch (Exception)
        {
            // 响应不是约定的 JSON，走下面的状态码兜底
        }

        switch (request.responseCode)
        {
            case 0:
                return "连不上服务器，请确认手机与电脑在同一 WiFi";
            case 400:
                return "提交的内容不符合要求，请检查标题和优先级";
            case 401:
                return "登录已失效，请重新登录";
            case 403:
                return "当前账号没有这个操作权限";
            case 404:
                return "服务器找不到这条记录";
            default:
                return "服务器出错，请稍后重试";
        }
    }
}
