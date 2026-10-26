using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

/// <summary>
/// 批处理入口：配置 Android 参数、启用 ARCore、生成最小自检场景，可单独构建 APK。
/// 用法：Unity.exe -batchmode -nographics -projectPath &lt;工程&gt; -buildTarget Android
///        -executeMethod SceneBuilder.Build -quit -logFile &lt;日志&gt;
/// </summary>
public static class SceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string ApkPath = "Builds/arwork.apk";

    [MenuItem("ARWork/1. 配置并生成场景")]
    public static void Build()
    {
        ConfigurePlayer();
        EnableARCoreLoader();
        ConfigureNativePlugins();
        CreateScene();
        AssetDatabase.SaveAssets();
        Debug.Log("SCENE_BUILT: " + ScenePath);
    }

    [MenuItem("ARWork/5. 生成巡检场景（平面检测 + 放置标记）")]
    public static void BuildPlayground()
    {
        ConfigurePlayer();
        ConfigureNetworking();

        Directory.CreateDirectory("Assets/Materials");
        Directory.CreateDirectory("Assets/Prefabs");

        Material planeMaterial = CreatePlaneMaterial();
        Material markerMaterial = CreateMaterial(
            "Assets/Materials/MarkerMaterial.mat", "Unlit/Color", new Color(1f, 0.45f, 0.1f));

        GameObject planePrefab = CreatePlanePrefab(planeMaterial);
        GameObject markerPrefab = CreateMarkerPrefab(markerMaterial);

        CreateArScene(planePrefab, markerPrefab);

        AssetDatabase.SaveAssets();
        Debug.Log("PLAYGROUND_BUILT: " + ScenePath);
    }

    /// <summary>
    /// 手机要访问电脑上的后端，Android 9 起默认禁止明文 HTTP，这里放开。
    /// </summary>
    private static void ConfigureNetworking()
    {
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        Debug.Log("NETWORK_CONFIGURED");
    }

    /// <summary>
    /// 平面可视化的透明材质。用项目内 shader，不依赖内置 shader 的打包与背面剔除行为。
    /// </summary>
    private static Material CreatePlaneMaterial()
    {
        const string materialPath = "Assets/Materials/PlaneMaterial.mat";
        const string shaderPath = "Assets/Shaders/ARPlaneOverlay.shader";

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null)
        {
            Debug.LogWarning("PLANE_SHADER_MISSING: " + shaderPath);
            shader = Shader.Find("Unlit/Transparent");
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.color = new Color(0.25f, 0.65f, 1f, 0.22f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateMaterial(string path, string shaderName, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            // 重跑时同步 shader 与颜色，避免沿用旧设置
            Shader target = Shader.Find(shaderName);
            if (target != null && existing.shader != target)
            {
                existing.shader = target;
            }

            existing.color = color;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning("SHADER_NOT_FOUND: " + shaderName);
            return null;
        }

        var material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject CreatePlanePrefab(Material material)
    {
        const string path = "Assets/Prefabs/ARPlaneVisualizer.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            return existing;
        }

        var visualizer = new GameObject("ARPlaneVisualizer");
        visualizer.AddComponent<ARPlane>();
        visualizer.AddComponent<ARPlaneMeshVisualizer>();
        visualizer.AddComponent<MeshFilter>();

        var renderer = visualizer.AddComponent<MeshRenderer>();
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(visualizer, path);
        Object.DestroyImmediate(visualizer);
        return prefab;
    }

    private static GameObject CreateMarkerPrefab(Material material)
    {
        const string path = "Assets/Prefabs/Marker.prefab";
        const float markerSize = 0.18f;

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            // 重跑时同步尺寸与材质，避免沿用旧 prefab
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            contents.transform.localScale = Vector3.one * markerSize;

            var existingRenderer = contents.GetComponent<MeshRenderer>();
            if (existingRenderer != null && material != null)
            {
                existingRenderer.sharedMaterial = material;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Marker";
        cube.transform.localScale = Vector3.one * markerSize;

        if (material != null)
        {
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(cube, path);
        Object.DestroyImmediate(cube);
        return prefab;
    }

    private static void CreateArScene(GameObject planePrefab, GameObject markerPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();

        // 提供 HandheldARInputDevice 设备，TrackedPoseDriver 靠它读取手机位姿
        sessionObject.AddComponent<ARInputManager>();

        var originObject = new GameObject("XR Origin");
        var xrOrigin = originObject.AddComponent<XROrigin>();

        var planeManager = originObject.AddComponent<ARPlaneManager>();
        planeManager.planePrefab = planePrefab;

        // 面积过小的平面不显示，避免零碎网格在屏幕上闪
        var planeFilter = originObject.AddComponent<ARPlaneFilter>();
        planeFilter.planeManager = planeManager;

        var raycastManager = originObject.AddComponent<ARRaycastManager>();

        var offsetObject = new GameObject("Camera Offset");
        offsetObject.transform.SetParent(originObject.transform, false);

        var cameraObject = new GameObject("AR Camera");
        cameraObject.transform.SetParent(offsetObject.transform, false);
        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;

        // XROrigin 需要知道哪个相机是 AR 相机，以及地面偏移对象
        xrOrigin.Camera = camera;
        xrOrigin.CameraFloorOffsetObject = offsetObject;

        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();

        // 手机 AR 的绑定是 HandheldARInputDevice，不是 VR 头显的 XRHMD
        var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
        poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        poseDriver.positionInput = new InputActionProperty(
            new InputAction("DevicePosition", InputActionType.Value, "<HandheldARInputDevice>/devicePosition"));
        poseDriver.rotationInput = new InputActionProperty(
            new InputAction("DeviceRotation", InputActionType.Value, "<HandheldARInputDevice>/deviceRotation"));

        cameraObject.AddComponent<AudioListener>();

        var placerObject = new GameObject("Marker Placer");
        PlaceMarker placer = placerObject.AddComponent<PlaceMarker>();
        placer.raycastManager = raycastManager;
        placer.markerPrefab = markerPrefab;

        var selfTestObject = new GameObject("Self Test");
        selfTestObject.AddComponent<ARSelfTest>();

        Canvas hudCanvas = CreateHud(planeManager, placer);
        placer.issueForm = CreateIssueForm(hudCanvas.transform);
        CreateGuide(hudCanvas.transform);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    /// <summary>
    /// 屏幕 HUD：底部状态提示 + 右下角清除按钮，另带必要的 EventSystem。
    /// </summary>
    private static Canvas CreateHud(ARPlaneManager planeManager, PlaceMarker placer)
    {
        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();

        var canvasObject = new GameObject("HUD Canvas");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        var hintObject = new GameObject("Hint", typeof(RectTransform));
        hintObject.transform.SetParent(canvasObject.transform, false);

        var hintRect = hintObject.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 40f);
        hintRect.sizeDelta = new Vector2(-40f, 130f);

        var hintImage = hintObject.AddComponent<Image>();
        hintImage.color = new Color(0f, 0f, 0f, 0.55f);

        Text hintText = CreateText(hintObject.transform, "Move your phone slowly to scan a flat surface", 34);

        var buttonObject = new GameObject("ClearButton", typeof(RectTransform));
        buttonObject.transform.SetParent(canvasObject.transform, false);

        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-30f, -30f);
        buttonRect.sizeDelta = new Vector2(220f, 90f);

        var buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0.14f, 0.39f, 0.92f, 0.92f);

        var clearButton = buttonObject.AddComponent<Button>();
        CreateText(buttonObject.transform, "CLEAR", 32);

        var hudObject = new GameObject("HUD");
        var hud = hudObject.AddComponent<ARHud>();
        hud.planeManager = planeManager;
        hud.placer = placer;
        hud.hintText = hintText;
        hud.clearButton = clearButton;

        return canvas;
    }

    /// <summary>
    /// 标记落下后弹出的上报表单：标题 / 描述 / 优先级 / 服务器地址 / 提交。
    /// </summary>
    private static IssueForm CreateIssueForm(Transform canvas)
    {
        var panelObject = new GameObject("IssueForm", typeof(RectTransform));
        panelObject.transform.SetParent(canvas, false);

        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 30f);
        panelRect.sizeDelta = new Vector2(-40f, 640f);

        var panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.06f, 0.08f, 0.11f, 0.94f);

        var coordObject = new GameObject("Coord", typeof(RectTransform));
        coordObject.transform.SetParent(panelObject.transform, false);
        SetTopStretch(coordObject.GetComponent<RectTransform>(), -20f, 46f);

        var coordText = coordObject.AddComponent<Text>();
        coordText.font = BuiltinFont();
        coordText.fontSize = 28;
        coordText.alignment = TextAnchor.MiddleLeft;
        coordText.color = new Color(0.7f, 0.85f, 1f);
        coordText.text = "x 0.00   y 0.00   z 0.00";

        InputField titleInput = CreateInputField(panelObject.transform, "Title (required)", -78f, 80f);
        InputField descriptionInput = CreateInputField(panelObject.transform, "Description (optional)", -172f, 80f);

        var priorityRow = new GameObject("PriorityRow", typeof(RectTransform));
        priorityRow.transform.SetParent(panelObject.transform, false);
        SetTopStretch(priorityRow.GetComponent<RectTransform>(), -266f, 80f);

        Button lowButton = CreateRowButton(priorityRow.transform, "LOW", 0f, 3f);
        Button mediumButton = CreateRowButton(priorityRow.transform, "MEDIUM", 1f, 3f);
        Button highButton = CreateRowButton(priorityRow.transform, "HIGH", 2f, 3f);

        InputField serverInput = CreateInputField(panelObject.transform, "Server", -360f, 70f);
        serverInput.text = ApiClient.DefaultBaseUrl;

        var submitObject = new GameObject("Submit", typeof(RectTransform));
        submitObject.transform.SetParent(panelObject.transform, false);
        SetTopStretch(submitObject.GetComponent<RectTransform>(), -444f, 92f);

        var submitImage = submitObject.AddComponent<Image>();
        submitImage.color = new Color(0.14f, 0.39f, 0.92f, 1f);

        var submitButton = submitObject.AddComponent<Button>();
        CreateText(submitObject.transform, "SUBMIT", 32);

        var statusObject = new GameObject("Status", typeof(RectTransform));
        statusObject.transform.SetParent(panelObject.transform, false);
        SetTopStretch(statusObject.GetComponent<RectTransform>(), -548f, 60f);

        var statusText = statusObject.AddComponent<Text>();
        statusText.font = BuiltinFont();
        statusText.fontSize = 28;
        statusText.alignment = TextAnchor.MiddleLeft;
        statusText.color = Color.white;
        statusText.text = string.Empty;

        var form = panelObject.AddComponent<IssueForm>();
        form.panel = panelObject;
        form.titleInput = titleInput;
        form.descriptionInput = descriptionInput;
        form.serverInput = serverInput;
        form.lowButton = lowButton;
        form.mediumButton = mediumButton;
        form.highButton = highButton;
        form.submitButton = submitButton;
        form.coordText = coordText;
        form.statusText = statusText;

        return form;
    }

    private static void SetTopStretch(RectTransform rect, float topOffset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, topOffset);
        rect.sizeDelta = new Vector2(-40f, height);
    }

    private static InputField CreateInputField(Transform parent, string placeholder, float topOffset, float height)
    {
        var inputObject = new GameObject("Input", typeof(RectTransform));
        inputObject.transform.SetParent(parent, false);

        SetTopStretch(inputObject.GetComponent<RectTransform>(), topOffset, height);

        var background = inputObject.AddComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.14f);

        var input = inputObject.AddComponent<InputField>();

        Text text = CreateText(inputObject.transform, string.Empty, 28);
        text.alignment = TextAnchor.MiddleLeft;

        Text hint = CreateText(inputObject.transform, placeholder, 28);
        hint.alignment = TextAnchor.MiddleLeft;
        hint.color = new Color(1f, 1f, 1f, 0.38f);

        input.textComponent = text;
        input.placeholder = hint;

        return input;
    }

    private static Button CreateRowButton(Transform parent, string label, float index, float count)
    {
        var buttonObject = new GameObject(label, typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);

        var rect = buttonObject.GetComponent<RectTransform>();
        float width = 1f / count;
        rect.anchorMin = new Vector2(index * width, 0f);
        rect.anchorMax = new Vector2((index + 1f) * width, 1f);
        rect.offsetMin = new Vector2(6f, 0f);
        rect.offsetMax = new Vector2(-6f, 0f);

        var image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.22f, 0.25f, 0.31f, 1f);

        var button = buttonObject.AddComponent<Button>();
        CreateText(buttonObject.transform, label, 30);

        return button;
    }

    /// <summary>
    /// 启动引导页：三步说明 + START 按钮，点过之后才进入 AR 取景。
    /// </summary>
    private static void CreateGuide(Transform canvas)
    {
        var panelObject = new GameObject("Guide", typeof(RectTransform));
        panelObject.transform.SetParent(canvas, false);

        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.04f, 0.06f, 0.09f, 0.98f);

        var titleObject = new GameObject("Title", typeof(RectTransform));
        titleObject.transform.SetParent(panelObject.transform, false);
        var titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -240f);
        titleRect.sizeDelta = new Vector2(-80f, 120f);

        var titleText = titleObject.AddComponent<Text>();
        titleText.font = BuiltinFont();
        titleText.fontSize = 56;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        titleText.text = "Park Inspection AR";

        var stepsObject = new GameObject("Steps", typeof(RectTransform));
        stepsObject.transform.SetParent(panelObject.transform, false);
        var stepsRect = stepsObject.GetComponent<RectTransform>();
        stepsRect.anchorMin = new Vector2(0f, 1f);
        stepsRect.anchorMax = new Vector2(1f, 1f);
        stepsRect.pivot = new Vector2(0.5f, 1f);
        stepsRect.anchoredPosition = new Vector2(0f, -400f);
        stepsRect.sizeDelta = new Vector2(-140f, 460f);

        var stepsText = stepsObject.AddComponent<Text>();
        stepsText.font = BuiltinFont();
        stepsText.fontSize = 34;
        stepsText.alignment = TextAnchor.UpperLeft;
        stepsText.color = new Color(0.78f, 0.84f, 0.92f);
        stepsText.lineSpacing = 1.6f;
        stepsText.text = "1.  Point your phone at a desk or floor\n    and move it slowly to scan\n\n2.  Tap the surface to drop a marker\n\n3.  Fill in the form and submit";

        var startObject = new GameObject("Start", typeof(RectTransform));
        startObject.transform.SetParent(panelObject.transform, false);
        var startRect = startObject.GetComponent<RectTransform>();
        startRect.anchorMin = new Vector2(0.5f, 0f);
        startRect.anchorMax = new Vector2(0.5f, 0f);
        startRect.pivot = new Vector2(0.5f, 0f);
        startRect.anchoredPosition = new Vector2(0f, 190f);
        startRect.sizeDelta = new Vector2(440f, 120f);

        var startImage = startObject.AddComponent<Image>();
        startImage.color = new Color(0.14f, 0.39f, 0.92f, 1f);

        var startButton = startObject.AddComponent<Button>();

        var startLabelObject = new GameObject("Text", typeof(RectTransform));
        startLabelObject.transform.SetParent(startObject.transform, false);
        var startLabelRect = startLabelObject.GetComponent<RectTransform>();
        startLabelRect.anchorMin = Vector2.zero;
        startLabelRect.anchorMax = Vector2.one;
        startLabelRect.offsetMin = Vector2.zero;
        startLabelRect.offsetMax = Vector2.zero;

        var startLabel = startLabelObject.AddComponent<Text>();
        startLabel.font = BuiltinFont();
        startLabel.fontSize = 40;
        startLabel.fontStyle = FontStyle.Bold;
        startLabel.alignment = TextAnchor.MiddleCenter;
        startLabel.color = Color.white;
        startLabel.text = "START";

        var guide = panelObject.AddComponent<ARGuide>();
        guide.guidePanel = panelObject;
        guide.startButton = startButton;
    }

    private static Text CreateText(Transform parent, string content, int fontSize)
    {
        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 0f);
        rect.offsetMax = new Vector2(-12f, 0f);

        var text = textObject.AddComponent<Text>();
        text.font = BuiltinFont();
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = content;

        return text;
    }

    private static Font BuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            return font;
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    [MenuItem("ARWork/2. 构建 APK")]
    public static void BuildApk()
    {
        EnsurePreloadedXR();
        Directory.CreateDirectory("Builds");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log("BUILD_RESULT: " + summary.result + " size=" + summary.totalSize);

        if (summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("ARWork/3. 导出 ARCore 插件状态")]
    public static void DumpPlugins()
    {
        string[] paths =
        {
            "Packages/com.unity.xr.arcore/Runtime/Android/UnityARCore.aar",
            "Packages/com.unity.xr.arcore/Runtime/Android/arcore_client.aar",
            "Packages/com.unity.xr.arcore/Runtime/Android/ARPresto.aar",
            "Packages/com.unity.xr.arcore/Runtime/Android/unityandroidpermissions.aar"
        };

        foreach (string path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null)
            {
                Debug.Log("PLUGIN_MISSING: " + path);
                continue;
            }

            Debug.Log(string.Format("PLUGIN: {0} android={1} cpu={2} anyPlatform={3}",
                path,
                importer.GetCompatibleWithPlatform(BuildTarget.Android),
                importer.GetPlatformData(BuildTarget.Android, "CPU"),
                importer.GetCompatibleWithAnyPlatform()));
        }
    }

    /// <summary>
    /// 项目内的 ARCore aar 默认没有 CPU 标记，导致里面的 .so 不被提取进包，
    /// 这里显式标为 ARM64 后再重新导入。
    /// </summary>
    private static void ConfigureNativePlugins()
    {
        AssetDatabase.Refresh();

        string[] aarNames = { "UnityARCore.aar", "arcore_client.aar", "ARPresto.aar" };
        foreach (string name in aarNames)
        {
            string path = "Assets/Plugins/Android/" + name;
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null)
            {
                Debug.LogWarning("NATIVE_PLUGIN_MISSING: " + path);
                continue;
            }

            importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
            importer.SaveAndReimport();
            Debug.Log("NATIVE_PLUGIN_ARM64: " + path);
        }

        // aar 里的 .so 不被 Unity 提取，这里用拆出来的裸 .so 直接提供
        string[] soNames =
        {
            "libUnityARCore.so", "libarcore_sdk_c.so",
            "libarcore_sdk_jni.so", "libarpresto_api.so"
        };
        foreach (string name in soNames)
        {
            string path = "Assets/Plugins/Android/arm64-v8a/" + name;
            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null)
            {
                Debug.LogWarning("NATIVE_SO_MISSING: " + path);
                continue;
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
            importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
            importer.SaveAndReimport();
            Debug.Log("NATIVE_SO_ARM64: " + path);
        }
    }

    /// <summary>
    /// XR 配置挂在 EditorBuildSettings 上，构建时会被当成无引用资源丢掉，
    /// 必须放进 preloaded assets 才能随包发布并在启动时加载。
    /// </summary>
    private static void EnsurePreloadedXR()
    {
        XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (settings == null)
        {
            Debug.LogError("XR_SETTINGS_ASSET_MISSING");
            return;
        }

        Debug.Log("PRELOADED_XR_SOURCE: " + AssetDatabase.GetAssetPath(settings)
            + " manager=" + (settings.Manager != null)
            + " loaders=" + (settings.Manager != null ? settings.Manager.activeLoaders.Count : -1)
            + " initOnStart=" + settings.InitManagerOnStart);

        var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
        preloaded.RemoveAll(item => item == null);

        if (!preloaded.Contains(settings))
        {
            preloaded.Add(settings);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            AssetDatabase.SaveAssets();
        }

        Debug.Log("PRELOADED_XR: " + AssetDatabase.GetAssetPath(settings) + " count=" + preloaded.Count);
    }

    [MenuItem("ARWork/4. 诊断 XR 配置")]
    public static void DiagnoseXR()
    {
        bool found = EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        Debug.Log("DIAG_TRYGET: found=" + found + " perTargetNull=" + (perTarget == null));

        if (perTarget != null)
        {
            XRGeneralSettings s = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            Debug.Log("DIAG_SETTINGS_ANDROID: " + (s == null ? "NULL" : AssetDatabase.GetAssetPath(s)));

            XRGeneralSettings s2 = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            Debug.Log("DIAG_SETTINGS_STANDALONE: " + (s2 == null ? "NULL" : AssetDatabase.GetAssetPath(s2)));
        }

        Debug.Log("DIAG_ACTIVE_TARGET: " + EditorUserBuildSettings.activeBuildTarget);

        XRGeneralSettings direct = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        Debug.Log("DIAG_DIRECT: " + (direct == null ? "NULL" : AssetDatabase.GetAssetPath(direct)));
    }

    private static void ConfigurePlayer()
    {
        NamedBuildTarget android = NamedBuildTarget.Android;

        PlayerSettings.companyName = "WhereLee";
        PlayerSettings.productName = "ARWork";
        PlayerSettings.SetApplicationIdentifier(android, "com.wherelee.arwork");

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // ARCore 配合 Vulkan 容易黑屏，只保留 OpenGLES3
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

        Debug.Log("PLAYER_CONFIGURED");
    }

    /// <summary>
    /// 在 XR Plug-in Management 的 Android 页签下勾选 ARCore。
    /// 批处理模式下属性 setter 不可靠（引用会落成 fileID: 0），
    /// 因此全部改为重建 asset + SerializedObject 直写字段。
    /// </summary>
    private static void EnableARCoreLoader()
    {
        const BuildTargetGroup group = BuildTargetGroup.Android;

        if (AssetDatabase.IsValidFolder("Assets/XR"))
        {
            AssetDatabase.DeleteAsset("Assets/XR");
        }

        Directory.CreateDirectory("Assets/XR/Loaders");

        var loader = ScriptableObject.CreateInstance<ARCoreLoader>();
        loader.name = "ARCoreLoader";
        AssetDatabase.CreateAsset(loader, "Assets/XR/Loaders/ARCoreLoader.asset");

        var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
        manager.name = "Android XR Manager";
        AssetDatabase.CreateAsset(manager, "Assets/XR/Android XR Manager.asset");

        var settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
        settings.name = "Android Settings";
        AssetDatabase.CreateAsset(settings, "Assets/XR/Android Settings.asset");

        var perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        perTarget.name = "XRGeneralSettingsPerBuildTarget";
        AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");

        AssetDatabase.SaveAssets();

        var managerSo = new SerializedObject(manager);
        SerializedProperty loadersProp = managerSo.FindProperty("m_Loaders");
        loadersProp.arraySize = 1;
        loadersProp.GetArrayElementAtIndex(0).objectReferenceValue = loader;
        managerSo.ApplyModifiedPropertiesWithoutUndo();

        var settingsSo = new SerializedObject(settings);
        settingsSo.FindProperty("m_LoaderManagerInstance").objectReferenceValue = manager;
        settingsSo.ApplyModifiedPropertiesWithoutUndo();

        perTarget.SetSettingsForBuildTarget(group, settings);
        EditorUtility.SetDirty(perTarget);
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("XR_MANAGER_PATH: " + AssetDatabase.GetAssetPath(settings.Manager));
        Debug.Log("XR_LOADER_COUNT: " + manager.activeLoaders.Count);
    }

    private static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();

        var originObject = new GameObject("XR Origin");

        var cameraObject = new GameObject("AR Camera");
        cameraObject.transform.SetParent(originObject.transform, false);
        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;

        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();
        cameraObject.AddComponent<AudioListener>();

        var selfTestObject = new GameObject("Self Test");
        selfTestObject.AddComponent<ARSelfTest>();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
