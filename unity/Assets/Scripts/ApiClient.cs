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

/// <summary>
/// 与 Go 后端的 HTTP 交互。字段名与作业任务书 §4 的结构保持一致。
/// 所有错误都会翻译成使用者能看懂的中文。
/// </summary>
public static class ApiClient
{
    /// <summary>电脑在局域网里的地址，手机与电脑需处于同一网络。</summary>
    public const string DefaultBaseUrl = "http://192.168.1.5:8080";

    /// <summary>提交一个问题；成功时回调返回后端生成的 id，失败时回调返回中文原因。</summary>
    public static IEnumerator CreateIssue(
        string baseUrl,
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

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                onError("连不上服务器，请确认手机与电脑在同一 WiFi，且后端已启动");
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError(ReadServerMessage(request));
                yield break;
            }

            IssueResponse issue = JsonUtility.FromJson<IssueResponse>(request.downloadHandler.text);
            onSuccess(issue != null ? issue.id : string.Empty);
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
            case 404:
                return "服务器找不到这条记录";
            default:
                return "服务器出错，请稍后重试";
        }
    }
}
