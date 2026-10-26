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
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

/// <summary>
/// 批处理入口：生成巡检 AR 场景、构建 APK。
/// 注意：XR Plug-in Management 的配置必须由编辑器界面勾选一次，这里只读取、不重建。
/// </summary>
public static class SceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string ApkPath = "Builds/arwork.apk";

    /// <summary>界面元素引用，交给 ARFlowController 连线。</summary>
    private sealed class UIRefs
    {
        public GameObject guidePanel;
        public Text accountText;
        public Button openListButton;
        public Button horizontalButton;
        public Button verticalButton;

        public GameObject hudPanel;
        public Text statusText;
        public Text toastText;
        public Button exitButton;
        public Button hudHorizontalButton;
        public Button hudVerticalButton;

        public GameObject confirmPanel;
        public Button confirmYesButton;
        public Button confirmNoButton;

        public LoginForm loginForm;
        public IssueListPanel listPanel;
        public IssueDetailPanel detailPanel;
        public ReasonDialog reasonDialog;
        public IssueForm form;
    }

    [MenuItem("ARWork/1. 生成巡检场景")]
    public static void BuildPlayground()
    {
        ConfigurePlayer();
        ConfigureNetworking();

        Directory.CreateDirectory("Assets/Materials");
        Directory.CreateDirectory("Assets/Prefabs");

        RemoveLegacyAssets();

        Material planeMaterial = CreatePlaneMaterial();
        Material markerBodyMaterial = CreateMaterial(
            "Assets/Materials/MarkerBodyMaterial.mat", "Unlit/Color", new Color(0.93f, 0.95f, 0.97f));
        // 纯白方框：和淡白网格同色系，但不透不混，一眼能分开
        Material reticleMaterial = CreateMaterial(
            "Assets/Materials/ReticleMaterial.mat", "Unlit/Color", new Color(1f, 1f, 1f, 1f));

        Texture2D plateFillTexture;
        Texture2D plateInkTexture;
        CreateWarningTextures(out plateFillTexture, out plateInkTexture);

        Material plateFillMaterial = CreatePlateMaterial(
            "Assets/Materials/PlateFillMaterial.mat", plateFillTexture, new Color(1f, 0.82f, 0.08f));
        Material plateInkMaterial = CreatePlateMaterial(
            "Assets/Materials/PlateInkMaterial.mat", plateInkTexture, new Color(0.06f, 0.06f, 0.07f));

        GameObject planePrefab = CreatePlanePrefab(planeMaterial);
        GameObject markerPrefab = CreateMarkerPrefab(markerBodyMaterial, plateFillMaterial, plateInkMaterial);

        CreateArScene(planePrefab, markerPrefab, reticleMaterial);

        AssetDatabase.SaveAssets();
        Debug.Log("PLAYGROUND_BUILT: " + ScenePath);
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

    /// <summary>手机要访问电脑上的后端，Android 9 起默认禁止明文 HTTP，这里放开。</summary>
    private static void ConfigureNetworking()
    {
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        Debug.Log("NETWORK_CONFIGURED");
    }

    // ---------------------------------------------------------------- 资源

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

        material.color = new Color(1f, 1f, 1f, 0.22f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateMaterial(string path, string shaderName, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
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

    /// <summary>换成警示牌后，旧的立方体标记、针形标记和橙色材质都不再使用。</summary>
    private static void RemoveLegacyAssets()
    {
        string[] legacy =
        {
            "Assets/Prefabs/Marker.prefab",
            "Assets/Prefabs/MarkerPin.prefab",
            "Assets/Materials/MarkerMaterial.mat",
        };

        foreach (string path in legacy)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    /// <summary>
    /// 警示牌造型的标记：贴面圆盘标出落点，短杆把牌面撑起，
    /// 牌面是「黄底 + 黑边 + 感叹号」，始终正对镜头。
    /// 黄底单独一层，已上报时只换这一层的颜色，黑边不动。
    /// </summary>
    private static GameObject CreateMarkerPrefab(
        Material bodyMaterial, Material plateFillMaterial, Material plateInkMaterial)
    {
        const string path = "Assets/Prefabs/MarkerSign.prefab";

        const float baseDiameter = 0.1f;
        const float baseThickness = 0.008f;
        const float stemDiameter = 0.012f;
        const float stemHeight = 0.06f;
        const float plateSize = 0.12f;

        // 先放一个空壳占住路径，保证后续重跑时场景里的引用不丢
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            var seed = new GameObject("Marker");
            PrefabUtility.SaveAsPrefabAsset(seed, path);
            Object.DestroyImmediate(seed);
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);

        for (int i = contents.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(contents.transform.GetChild(i).gameObject);
        }

        // 根物体保持单位缩放，尺寸全交给子物体，放置时根直接落在命中点
        contents.transform.localScale = Vector3.one;

        // 牌子绕自身底部转，杆顶就是牌面下边中点；若绕中心转，牌面一倾斜就会离开杆
        float plateBottomY = baseThickness + stemHeight;

        // Cylinder 原始高度是 2，缩放取实际高度的一半
        CreatePrimitivePart(contents.transform, "Base", PrimitiveType.Cylinder,
            new Vector3(0f, baseThickness * 0.5f, 0f),
            new Vector3(baseDiameter, baseThickness * 0.5f, baseDiameter), bodyMaterial);

        CreatePrimitivePart(contents.transform, "Stem", PrimitiveType.Cylinder,
            new Vector3(0f, baseThickness + stemHeight * 0.5f, 0f),
            new Vector3(stemDiameter, stemHeight * 0.5f, stemDiameter), bodyMaterial);

        var plate = new GameObject("Plate");
        plate.transform.SetParent(contents.transform, false);
        plate.transform.localPosition = new Vector3(0f, plateBottomY, 0f);
        plate.AddComponent<MarkerBillboard>();

        // 黑边要在黄底前面，沿 Quad 正面方向错开一点，避免深度打架
        CreatePrimitivePart(plate.transform, "Fill", PrimitiveType.Quad,
            new Vector3(0f, plateSize * 0.5f, 0f), Vector3.one * plateSize, plateFillMaterial);

        CreatePrimitivePart(plate.transform, "Ink", PrimitiveType.Quad,
            new Vector3(0f, plateSize * 0.5f, -0.0006f), Vector3.one * plateSize, plateInkMaterial);

        PrefabUtility.SaveAsPrefabAsset(contents, path);
        PrefabUtility.UnloadPrefabContents(contents);

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>
    /// 程序生成牌面贴图：一张三角形黄底，一张黑边加感叹号。
    /// 两张都只存白色形状和透明度，颜色交给材质，换色不用重新生成。
    /// </summary>
    private static void CreateWarningTextures(out Texture2D fill, out Texture2D ink)
    {
        const int size = 512;
        const int samples = 4;
        // 三边等宽的边框：按到边的距离判断。若按重心等比例缩，顶角会明显变粗
        const float borderWidth = 0.055f;

        var top = new Vector2(0.5f, 0.955f);
        var left = new Vector2(0.045f, 0.08f);
        var right = new Vector2(0.955f, 0.08f);

        var fillPixels = new Color32[size * size];
        var inkPixels = new Color32[size * size];
        int total = samples * samples;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int fillHits = 0;
                int inkHits = 0;

                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        var p = new Vector2(
                            (x + (sx + 0.5f) / samples) / size,
                            (y + (sy + 0.5f) / samples) / size);

                        if (!InsideTriangle(p, top, left, right))
                        {
                            continue;
                        }

                        fillHits++;

                        if (DistanceToTriangleEdge(p, top, left, right) < borderWidth
                            || InsideExclamation(p))
                        {
                            inkHits++;
                        }
                    }
                }

                fillPixels[y * size + x] = ShapePixel(fillHits, total);
                inkPixels[y * size + x] = ShapePixel(inkHits, total);
            }
        }

        fill = SaveTexture("Assets/Textures/MarkerPlateFill.png", fillPixels, size);
        ink = SaveTexture("Assets/Textures/MarkerPlateInk.png", inkPixels, size);
    }

    private static Color32 ShapePixel(int hits, int total)
    {
        var alpha = (byte)Mathf.RoundToInt(255f * hits / total);
        return new Color32(255, 255, 255, alpha);
    }

    private static Texture2D SaveTexture(string path, Color32[] pixels, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();

        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material CreatePlateMaterial(string path, Texture2D texture, Color color)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/MarkerPlate.shader");
        if (shader == null)
        {
            Debug.LogWarning("PLATE_SHADER_MISSING");
            shader = Shader.Find("Unlit/Transparent");
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.mainTexture = texture;
        material.color = color;
        EditorUtility.SetDirty(material);

        return material;
    }

    private static float EdgeSign(Vector2 p, Vector2 a, Vector2 b)
    {
        return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }

    private static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = EdgeSign(p, a, b);
        float d2 = EdgeSign(p, b, c);
        float d3 = EdgeSign(p, c, a);

        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;

        return !(hasNegative && hasPositive);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;

        if (lengthSq < 0.000001f)
        {
            return Vector2.Distance(p, a);
        }

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
        return Vector2.Distance(p, a + ab * t);
    }

    private static float DistanceToTriangleEdge(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = DistanceToSegment(p, a, b);
        float d2 = DistanceToSegment(p, b, c);
        float d3 = DistanceToSegment(p, c, a);

        return Mathf.Min(d1, Mathf.Min(d2, d3));
    }

    private static bool InsideExclamation(Vector2 p)
    {
        // 竖条
        if (p.x >= 0.465f && p.x <= 0.535f && p.y >= 0.36f && p.y <= 0.68f)
        {
            return true;
        }

        // 下面那个点
        float dx = p.x - 0.5f;
        float dy = p.y - 0.268f;
        return dx * dx + dy * dy <= 0.042f * 0.042f;
    }

    /// <summary>
    /// 屏幕中心的方框指示器。只有一个，直接建在场景里，不走预制体。
    /// 父物体 Y 轴对齐平面法线，四条边因此落在平面本身上。
    /// </summary>
    private static ARReticle CreateReticle(
        Material material, ARRaycastManager raycastManager, ARPlaneManager planeManager)
    {
        const float size = 0.1f;
        const float thickness = 0.006f;
        const float depth = 0.004f;

        float half = size * 0.5f;

        var root = new GameObject("Reticle");

        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.SetActive(false);

        CreatePrimitivePart(visual.transform, "Top", PrimitiveType.Cube,
            new Vector3(0f, 0f, half), new Vector3(size + thickness, depth, thickness), material);
        CreatePrimitivePart(visual.transform, "Bottom", PrimitiveType.Cube,
            new Vector3(0f, 0f, -half), new Vector3(size + thickness, depth, thickness), material);
        CreatePrimitivePart(visual.transform, "Left", PrimitiveType.Cube,
            new Vector3(-half, 0f, 0f), new Vector3(thickness, depth, size + thickness), material);
        CreatePrimitivePart(visual.transform, "Right", PrimitiveType.Cube,
            new Vector3(half, 0f, 0f), new Vector3(thickness, depth, size + thickness), material);

        var reticle = root.AddComponent<ARReticle>();
        reticle.raycastManager = raycastManager;
        reticle.planeManager = planeManager;
        reticle.visual = visual;

        return reticle;
    }

    /// <summary>场景里的纯视觉零件，统一去掉碰撞体——射线走 ARRaycastManager，不用物理。</summary>
    private static void CreatePrimitivePart(
        Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;

        Object.DestroyImmediate(part.GetComponent<Collider>());

        if (material != null)
        {
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    // ---------------------------------------------------------------- 场景

    private static void CreateArScene(GameObject planePrefab, GameObject markerPrefab, Material reticleMaterial)
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
        // 水平和垂直一直检测，界面上的切换只决定哪一类画出来
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

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
        placer.planeManager = planeManager;

        // 落在平面上的方框，指示下一刀会放在哪
        ARReticle reticle = CreateReticle(reticleMaterial, raycastManager, planeManager);

        Canvas canvas = CreateCanvas();
        UIRefs ui = CreateUI(canvas.transform);

        var flowObject = new GameObject("AR Flow");
        var flow = flowObject.AddComponent<ARFlowController>();
        flow.planeManager = planeManager;
        flow.planeFilter = planeFilter;
        flow.raycastManager = raycastManager;
        flow.placer = placer;
        flow.reticle = reticle;
        flow.form = ui.form;
        flow.guidePanel = ui.guidePanel;
        flow.hudPanel = ui.hudPanel;
        flow.confirmPanel = ui.confirmPanel;
        flow.statusText = ui.statusText;
        flow.toastText = ui.toastText;
        flow.exitButton = ui.exitButton;
        flow.horizontalButton = ui.horizontalButton;
        flow.verticalButton = ui.verticalButton;
        flow.hudHorizontalButton = ui.hudHorizontalButton;
        flow.hudVerticalButton = ui.hudVerticalButton;
        flow.confirmYesButton = ui.confirmYesButton;
        flow.confirmNoButton = ui.confirmNoButton;
        flow.openListButton = ui.openListButton;
        flow.accountText = ui.accountText;
        flow.loginForm = ui.loginForm;
        flow.listPanel = ui.listPanel;
        flow.detailPanel = ui.detailPanel;

        placer.flow = flow;

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    // ---------------------------------------------------------------- 界面

    private static Canvas CreateCanvas()
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
        canvasObject.AddComponent<ChineseFontApplier>();

        return canvas;
    }

    private static UIRefs CreateUI(Transform canvas)
    {
        var refs = new UIRefs();

        // 建得越晚越靠上层；登录页打底，表单压在最上
        refs.loginForm = CreateLoginPanel(canvas);
        refs.guidePanel = CreateGuidePanel(canvas, refs);
        refs.hudPanel = CreateHudPanel(canvas, refs);
        refs.confirmPanel = CreateConfirmPanel(canvas, refs);
        refs.listPanel = CreateIssueListPanel(canvas);
        // 详情在下、输入弹窗在上，弹窗要能盖住详情
        refs.detailPanel = CreateIssueDetailPanel(canvas);
        refs.reasonDialog = CreateReasonDialog(canvas);
        refs.detailPanel.reasonDialog = refs.reasonDialog;
        refs.form = CreateFormPanel(canvas);

        return refs;
    }

    private static GameObject CreateGuidePanel(Transform canvas, UIRefs refs)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "Guide", new Color(0.04f, 0.06f, 0.09f, 0.98f), false);

        // 顶部一行：左边当前账号，右边「我的上报」
        refs.accountText = CreateText(panel.transform, string.Empty, 30, false);
        var accountRect = refs.accountText.rectTransform;
        accountRect.anchorMin = new Vector2(0f, 1f);
        accountRect.anchorMax = new Vector2(0f, 1f);
        accountRect.pivot = new Vector2(0f, 1f);
        accountRect.anchoredPosition = new Vector2(44f, -40f);
        accountRect.sizeDelta = new Vector2(620f, 60f);
        refs.accountText.alignment = TextAnchor.MiddleLeft;
        refs.accountText.color = new Color(0.68f, 0.74f, 0.82f);

        refs.openListButton = CreateFixedButton(
            panel.transform, "我的上报",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -36f),
            new Vector2(240f, 84f), new Color(0.22f, 0.25f, 0.31f, 0.95f));

        Text title = CreateText(panel.transform, "园区巡检 AR", 56, false);
        SetTopStretch(title.rectTransform, -240f, 120f);
        title.fontStyle = FontStyle.Bold;

        Text steps = CreateText(panel.transform, string.Empty, 34, false);
        var stepsRect = steps.rectTransform;
        stepsRect.anchorMin = new Vector2(0f, 1f);
        stepsRect.anchorMax = new Vector2(1f, 1f);
        stepsRect.pivot = new Vector2(0.5f, 1f);
        stepsRect.anchoredPosition = new Vector2(0f, -400f);
        stepsRect.sizeDelta = new Vector2(-160f, 420f);
        steps.alignment = TextAnchor.UpperLeft;
        steps.lineSpacing = 1.6f;
        steps.color = new Color(0.78f, 0.84f, 0.92f);
        steps.text = "1. 选择要标记的表面\n2. 缓慢移动手机扫描\n3. 点击表面，放置标记并上报";

        refs.horizontalButton = CreateFixedButton(
            panel.transform, "水平面 · 桌面或地面",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 300f),
            new Vector2(700f, 130f), new Color(0.14f, 0.39f, 0.92f, 1f));

        refs.verticalButton = CreateFixedButton(
            panel.transform, "垂直面 · 墙面",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 140f),
            new Vector2(700f, 130f), new Color(0.2f, 0.55f, 0.45f, 1f));

        return panel;
    }

    private static GameObject CreateHudPanel(Transform canvas, UIRefs refs)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "HUD", Color.clear, false);

        var hintObject = new GameObject("Hint", typeof(RectTransform));
        hintObject.transform.SetParent(panel.transform, false);

        var hintRect = hintObject.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 40f);
        hintRect.sizeDelta = new Vector2(-40f, 130f);

        var hintImage = hintObject.AddComponent<Image>();
        hintImage.color = new Color(0f, 0f, 0f, 0.55f);
        // 提示条只做展示，不能拦截 AR 场景的点击
        hintImage.raycastTarget = false;

        refs.statusText = CreateText(hintObject.transform, "正在扫描，请缓慢移动手机", 34);

        // 右上角：切换表面 + 退出
        refs.hudHorizontalButton = CreateFixedButton(
            panel.transform, "水平",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-420f, -30f),
            new Vector2(170f, 90f), new Color(0.25f, 0.28f, 0.34f, 0.95f));

        refs.hudVerticalButton = CreateFixedButton(
            panel.transform, "垂直",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-230f, -30f),
            new Vector2(170f, 90f), new Color(0.25f, 0.28f, 0.34f, 0.95f));

        refs.exitButton = CreateFixedButton(
            panel.transform, "退出",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f),
            new Vector2(170f, 90f), new Color(0.33f, 0.36f, 0.43f, 0.95f));

        // 临时提示，默认隐藏
        refs.toastText = CreateText(panel.transform, string.Empty, 32, false);
        var toastRect = refs.toastText.rectTransform;
        toastRect.anchorMin = new Vector2(0.5f, 0.5f);
        toastRect.anchorMax = new Vector2(0.5f, 0.5f);
        toastRect.pivot = new Vector2(0.5f, 0.5f);
        toastRect.anchoredPosition = new Vector2(0f, -170f);
        toastRect.sizeDelta = new Vector2(860f, 110f);
        refs.toastText.color = new Color(1f, 0.9f, 0.6f);
        refs.toastText.gameObject.SetActive(false);

        return panel;
    }

    /// <summary>
    /// 退出确认框：退出会清空本轮标记，先问一句再动。
    /// </summary>
    private static GameObject CreateConfirmPanel(Transform canvas, UIRefs refs)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "Confirm", new Color(0f, 0f, 0f, 0.6f), true);

        var boxObject = new GameObject("Box", typeof(RectTransform));
        boxObject.transform.SetParent(panel.transform, false);

        var boxRect = boxObject.GetComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0.5f, 0.5f);
        boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = new Vector2(820f, 440f);

        var boxImage = boxObject.AddComponent<Image>();
        ApplyRoundedBackground(boxImage, new Color(0.1f, 0.12f, 0.16f, 1f));

        Text message = CreateText(boxObject.transform, "退出会清空本轮所有标记\n确定要退出吗？", 34, false);
        SetTopStretch(message.rectTransform, -70f, 190f);
        message.alignment = TextAnchor.MiddleCenter;
        message.lineSpacing = 1.4f;

        var buttonRow = new GameObject("Buttons", typeof(RectTransform));
        buttonRow.transform.SetParent(boxObject.transform, false);

        var rowRect = buttonRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.anchoredPosition = new Vector2(0f, 50f);
        rowRect.sizeDelta = new Vector2(-60f, 110f);

        refs.confirmNoButton = CreateRowButton(buttonRow.transform, "留下", 0f, 2f);
        refs.confirmYesButton = CreateRowButton(buttonRow.transform, "退出", 1f, 2f);
        refs.confirmYesButton.image.color = new Color(0.68f, 0.26f, 0.26f, 1f);

        return panel;
    }

    /// <summary>
    /// 登录页：居中卡片 + 账号 / 密码 / 服务器地址。
    /// 服务器地址放在登录页，因为换网络后第一个要失败的就是登录请求。
    /// </summary>
    private static LoginForm CreateLoginPanel(Transform canvas)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "Login", new Color(0.04f, 0.06f, 0.09f, 1f), true);

        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(panel.transform, false);

        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(840f, 830f);

        var cardImage = card.AddComponent<Image>();
        ApplyRoundedBackground(cardImage, new Color(0.09f, 0.11f, 0.15f, 1f));

        Text title = CreateText(card.transform, "园区巡检 AR", 54, false);
        SetTopStretch(title.rectTransform, -56f, 76f);
        title.fontStyle = FontStyle.Bold;

        Text subtitle = CreateText(card.transform, "登录后开始上报问题", 30, false);
        SetTopStretch(subtitle.rectTransform, -136f, 50f);
        subtitle.color = new Color(0.68f, 0.74f, 0.82f);

        InputField usernameInput = CreateInputField(card.transform, "账号", -220f, 92f);

        InputField passwordInput = CreateInputField(card.transform, "密码", -330f, 92f);
        passwordInput.contentType = InputField.ContentType.Password;

        Button loginButton = CreateWideButton(card.transform, "登录", -450f, 100f,
            new Color(0.14f, 0.39f, 0.92f, 1f));

        Text statusText = CreateText(card.transform, string.Empty, 28, false);
        SetTopStretch(statusText.rectTransform, -572f, 56f);
        statusText.color = new Color(1f, 0.55f, 0.5f);

        InputField serverInput = CreateInputField(card.transform, "服务器地址", -648f, 80f);
        serverInput.text = ApiClient.DefaultBaseUrl;

        var form = panel.AddComponent<LoginForm>();
        form.panel = panel;
        form.usernameInput = usernameInput;
        form.passwordInput = passwordInput;
        form.serverInput = serverInput;
        form.loginButton = loginButton;
        form.statusText = statusText;

        return form;
    }

    /// <summary>撑满父级宽度、按上边距定位的按钮。</summary>
    private static Button CreateWideButton(Transform parent, string label, float topOffset, float height, Color color)
    {
        var buttonObject = new GameObject(label, typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);

        SetTopStretch(buttonObject.GetComponent<RectTransform>(), topOffset, height);

        var image = buttonObject.AddComponent<Image>();
        ApplyRoundedBackground(image, color);

        var button = buttonObject.AddComponent<Button>();
        CreateText(buttonObject.transform, label, 36);

        return button;
    }

    /// <summary>
    /// 问题列表：顶部标题 + 关闭，中间可滚动列表，底部刷新与状态文字。
    /// 巡检员看到的是自己报的，管理员看到全部，区分在后端做。
    /// </summary>
    private static IssueListPanel CreateIssueListPanel(Transform canvas)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "IssueList", new Color(0.04f, 0.06f, 0.09f, 0.99f), true);

        Text header = CreateText(panel.transform, "我的上报", 44, false);
        var headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.anchoredPosition = new Vector2(44f, -44f);
        headerRect.sizeDelta = new Vector2(600f, 76f);
        header.alignment = TextAnchor.MiddleLeft;
        header.fontStyle = FontStyle.Bold;

        Button closeButton = CreateFixedButton(
            panel.transform, "关闭",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -36f),
            new Vector2(170f, 84f), new Color(0.33f, 0.36f, 0.43f, 0.95f));

        var scrollObject = new GameObject("Scroll", typeof(RectTransform));
        scrollObject.transform.SetParent(panel.transform, false);

        var scrollArea = scrollObject.GetComponent<RectTransform>();
        scrollArea.anchorMin = Vector2.zero;
        scrollArea.anchorMax = Vector2.one;
        scrollArea.offsetMin = new Vector2(40f, 150f);
        scrollArea.offsetMax = new Vector2(-40f, -150f);

        var scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        scrollObject.AddComponent<RectMask2D>();

        var contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(scrollObject.transform, false);

        var contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        var layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;

        var fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRect;
        scroll.viewport = scrollArea;

        GameObject rowTemplate = CreateIssueRow(contentObject.transform);

        Button refreshButton = CreateFixedButton(
            panel.transform, "刷新",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f),
            new Vector2(170f, 84f), new Color(0.22f, 0.25f, 0.31f, 1f));

        Text statusText = CreateText(panel.transform, string.Empty, 28, false);
        var statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = new Vector2(110f, 36f);
        statusRect.sizeDelta = new Vector2(-320f, 84f);
        statusText.alignment = TextAnchor.MiddleLeft;

        var listPanel = panel.AddComponent<IssueListPanel>();
        listPanel.panel = panel;
        listPanel.content = contentRect;
        listPanel.rowTemplate = rowTemplate;
        listPanel.headerText = header;
        listPanel.statusText = statusText;
        listPanel.closeButton = closeButton;
        listPanel.refreshButton = refreshButton;

        return listPanel;
    }

    /// <summary>列表条目：左侧状态色条 + 标题 + 状态与时间。</summary>
    private static GameObject CreateIssueRow(Transform parent)
    {
        var row = new GameObject("RowTemplate", typeof(RectTransform));
        row.transform.SetParent(parent, false);

        // childControlHeight 是关的，行高只能由这里直接定，LayoutElement 不生效
        var rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0f, 140f);

        var rowImage = row.AddComponent<Image>();
        ApplyRoundedBackground(rowImage, new Color(0.12f, 0.15f, 0.2f, 1f));

        // 整行可点，点开详情
        row.AddComponent<Button>().targetGraphic = rowImage;

        var badge = new GameObject("Badge", typeof(RectTransform));
        badge.transform.SetParent(row.transform, false);

        var badgeRect = badge.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(28f, 0f);
        badgeRect.sizeDelta = new Vector2(10f, 76f);

        var badgeImage = badge.AddComponent<Image>();
        ApplyRoundedBackground(badgeImage, new Color(0.6f, 0.65f, 0.72f));

        Text title = CreateText(row.transform, string.Empty, 34, false);
        title.name = "Title";
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(72f, -66f);
        titleRect.offsetMax = new Vector2(-32f, -20f);
        title.alignment = TextAnchor.MiddleLeft;

        Text meta = CreateText(row.transform, string.Empty, 28, false);
        meta.name = "Meta";
        var metaRect = meta.rectTransform;
        metaRect.anchorMin = new Vector2(0f, 0f);
        metaRect.anchorMax = new Vector2(1f, 0f);
        metaRect.pivot = new Vector2(0.5f, 0f);
        metaRect.offsetMin = new Vector2(72f, 20f);
        metaRect.offsetMax = new Vector2(-32f, 62f);
        meta.alignment = TextAnchor.MiddleLeft;
        meta.color = new Color(0.68f, 0.74f, 0.82f);

        row.SetActive(false);
        return row;
    }

    /// <summary>
    /// 工单详情：标题、状态、元信息、描述、处理记录，底部按状态给完成 / 退回。
    /// 正文放在可滚动区域里，描述再长也能看全。
    /// </summary>
    private static IssueDetailPanel CreateIssueDetailPanel(Transform canvas)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "IssueDetail", new Color(0.04f, 0.06f, 0.09f, 0.99f), true);

        Text title = CreateText(panel.transform, string.Empty, 40, false);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(44f, -150f);
        titleRect.offsetMax = new Vector2(-230f, -40f);
        title.alignment = TextAnchor.MiddleLeft;
        title.fontStyle = FontStyle.Bold;

        Button closeButton = CreateFixedButton(
            panel.transform, "关闭",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -36f),
            new Vector2(170f, 84f), new Color(0.33f, 0.36f, 0.43f, 0.95f));

        var scrollObject = new GameObject("Scroll", typeof(RectTransform));
        scrollObject.transform.SetParent(panel.transform, false);

        var scrollArea = scrollObject.GetComponent<RectTransform>();
        scrollArea.anchorMin = Vector2.zero;
        scrollArea.anchorMax = Vector2.one;
        scrollArea.offsetMin = new Vector2(40f, 230f);
        scrollArea.offsetMax = new Vector2(-40f, -160f);

        var scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        scrollObject.AddComponent<RectMask2D>();

        var contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(scrollObject.transform, false);

        var contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        var layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 18f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperLeft;

        contentObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRect;
        scroll.viewport = scrollArea;

        Text status = CreateBodyText(contentObject.transform, string.Empty, 32);
        status.fontStyle = FontStyle.Bold;

        Text meta = CreateBodyText(contentObject.transform, string.Empty, 28);
        meta.color = new Color(0.68f, 0.74f, 0.82f);

        Text description = CreateBodyText(contentObject.transform, string.Empty, 30);

        var repairSection = new GameObject("RepairSection", typeof(RectTransform));
        repairSection.transform.SetParent(contentObject.transform, false);

        var repairLayout = repairSection.AddComponent<VerticalLayoutGroup>();
        repairLayout.spacing = 8f;
        repairLayout.padding = new RectOffset(20, 20, 16, 16);
        repairLayout.childControlWidth = true;
        repairLayout.childControlHeight = true;
        repairLayout.childForceExpandWidth = true;
        repairLayout.childForceExpandHeight = false;
        repairLayout.childAlignment = TextAnchor.UpperLeft;

        repairSection.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ApplyRoundedBackground(repairSection.AddComponent<Image>(), new Color(0.12f, 0.15f, 0.2f, 1f));

        Text repairLabel = CreateBodyText(repairSection.transform, "处理记录", 26);
        repairLabel.color = new Color(0.68f, 0.74f, 0.82f);

        Text repairText = CreateBodyText(repairSection.transform, string.Empty, 30);

        Text timelineLabel = CreateBodyText(contentObject.transform, "流转记录", 26);
        timelineLabel.color = new Color(0.68f, 0.74f, 0.82f);

        Text timelineText = CreateBodyText(contentObject.transform, string.Empty, 28);
        timelineText.color = new Color(0.78f, 0.84f, 0.92f);

        Button completeButton = CreateFixedButton(
            panel.transform, "完成",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f),
            new Vector2(300f, 100f), new Color(0.14f, 0.39f, 0.92f, 1f));

        Button returnButton = CreateFixedButton(
            panel.transform, "退回",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(360f, 40f),
            new Vector2(300f, 100f), new Color(0.5f, 0.28f, 0.28f, 1f));

        Button withdrawButton = CreateFixedButton(
            panel.transform, "撤回",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(680f, 40f),
            new Vector2(300f, 100f), new Color(0.3f, 0.32f, 0.38f, 1f));

        Text statusMessage = CreateText(panel.transform, string.Empty, 28, false);
        var msgRect = statusMessage.rectTransform;
        msgRect.anchorMin = new Vector2(0f, 0f);
        msgRect.anchorMax = new Vector2(1f, 0f);
        msgRect.pivot = new Vector2(0.5f, 0f);
        msgRect.anchoredPosition = new Vector2(0f, 155f);
        msgRect.sizeDelta = new Vector2(-80f, 60f);
        statusMessage.alignment = TextAnchor.MiddleLeft;

        var detail = panel.AddComponent<IssueDetailPanel>();
        detail.panel = panel;
        detail.titleText = title;
        detail.statusText = status;
        detail.metaText = meta;
        detail.descriptionText = description;
        detail.repairSection = repairSection;
        detail.repairText = repairText;
        detail.timelineText = timelineText;
        detail.completeButton = completeButton;
        detail.returnButton = returnButton;
        detail.withdrawButton = withdrawButton;
        detail.closeButton = closeButton;
        detail.statusMessage = statusMessage;

        return detail;
    }

    /// <summary>滚动区里的一段文字，高度随内容自适应。</summary>
    private static Text CreateBodyText(Transform parent, string content, int fontSize)
    {
        Text text = CreateText(parent, content, fontSize, false);
        text.alignment = TextAnchor.UpperLeft;

        text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return text;
    }

    /// <summary>
    /// 写处理记录 / 退回原因的弹窗。两个场景共用一个，标题和占位文字在打开时改。
    /// </summary>
    private static ReasonDialog CreateReasonDialog(Transform canvas)
    {
        GameObject panel = CreateFullScreenPanel(canvas, "ReasonDialog", new Color(0f, 0f, 0f, 0.75f), true);

        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(panel.transform, false);

        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(880f, 640f);

        ApplyRoundedBackground(card.AddComponent<Image>(), new Color(0.09f, 0.11f, 0.15f, 1f));

        Text title = CreateText(card.transform, string.Empty, 36, false);
        SetTopStretch(title.rectTransform, -48f, 60f);
        title.fontStyle = FontStyle.Bold;

        InputField input = CreateInputField(card.transform, string.Empty, -140f, 300f);
        input.lineType = InputField.LineType.MultiLineNewline;
        input.textComponent.alignment = TextAnchor.UpperLeft;

        Text hint = CreateText(card.transform, string.Empty, 26, false);
        SetTopStretch(hint.rectTransform, -462f, 50f);
        hint.color = new Color(1f, 0.55f, 0.5f);

        Button cancelButton = CreateFixedButton(
            card.transform, "取消",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f),
            new Vector2(380f, 96f), new Color(0.22f, 0.25f, 0.31f, 1f));

        Button confirmButton = CreateFixedButton(
            card.transform, "确定",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(460f, 36f),
            new Vector2(380f, 96f), new Color(0.14f, 0.39f, 0.92f, 1f));

        var dialog = panel.AddComponent<ReasonDialog>();
        dialog.panel = panel;
        dialog.titleText = title;
        dialog.input = input;
        dialog.hintText = hint;
        dialog.confirmButton = confirmButton;
        dialog.cancelButton = cancelButton;

        return dialog;
    }

    private static IssueForm CreateFormPanel(Transform canvas)
    {
        var panelObject = new GameObject("IssueForm", typeof(RectTransform));
        panelObject.transform.SetParent(canvas, false);

        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 24f);
        // 只占屏幕下方约三成，保证准星和刚放的标记露在表单上方
        panelRect.sizeDelta = new Vector2(-40f, 560f);

        var panelImage = panelObject.AddComponent<Image>();
        ApplyRoundedBackground(panelImage, new Color(0.06f, 0.08f, 0.11f, 0.95f));

        // 第一行左侧给出“位置已记录”的确认，右侧留给「设置」
        var coordText = CreateText(panelObject.transform, "已记录标记位置", 28, false);
        var coordRect = coordText.rectTransform;
        coordRect.anchorMin = new Vector2(0f, 1f);
        coordRect.anchorMax = new Vector2(1f, 1f);
        coordRect.pivot = new Vector2(0.5f, 1f);
        coordRect.offsetMin = new Vector2(20f, -54f);
        coordRect.offsetMax = new Vector2(-180f, -14f);
        coordText.alignment = TextAnchor.MiddleLeft;
        coordText.color = new Color(0.7f, 0.85f, 1f);

        Button settingsButton = CreateFixedButton(
            panelObject.transform, "设置",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -14f),
            new Vector2(140f, 56f), new Color(0.22f, 0.25f, 0.31f, 1f));

        InputField titleInput = CreateInputField(panelObject.transform, "问题标题（必填）", -66f, 76f);
        InputField descriptionInput = CreateInputField(panelObject.transform, "问题描述（选填）", -150f, 76f);

        // 优先级：左侧标题 + 三档按钮，避免“低 中 高”三个字没有来历
        var priorityRow = new GameObject("PriorityRow", typeof(RectTransform));
        priorityRow.transform.SetParent(panelObject.transform, false);
        SetTopStretch(priorityRow.GetComponent<RectTransform>(), -234f, 76f);

        Text priorityLabel = CreateText(priorityRow.transform, "优先级", 28, false);
        var labelRect = priorityLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0.25f, 1f);
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = Vector2.zero;
        priorityLabel.alignment = TextAnchor.MiddleLeft;
        priorityLabel.color = new Color(0.78f, 0.84f, 0.92f);

        Button lowButton = CreateRowButton(priorityRow.transform, "低", 1f, 4f);
        Button mediumButton = CreateRowButton(priorityRow.transform, "中", 2f, 4f);
        Button highButton = CreateRowButton(priorityRow.transform, "高", 3f, 4f);

        // 服务器地址默认收起，只在排障时由「设置」展开
        var serverRow = new GameObject("ServerRow", typeof(RectTransform));
        serverRow.transform.SetParent(panelObject.transform, false);
        SetTopStretch(serverRow.GetComponent<RectTransform>(), -318f, 66f);
        serverRow.SetActive(false);

        InputField serverInput = CreateInputField(serverRow.transform, "服务器地址", 0f, 66f);
        ((RectTransform)serverInput.transform).sizeDelta = new Vector2(0f, 66f);
        serverInput.text = ApiClient.DefaultBaseUrl;

        var actionRow = new GameObject("ActionRow", typeof(RectTransform));
        actionRow.transform.SetParent(panelObject.transform, false);
        SetTopStretch(actionRow.GetComponent<RectTransform>(), -394f, 88f);

        Button cancelButton = CreateRowButton(actionRow.transform, "取消", 0f, 2f);
        Button submitButton = CreateRowButton(actionRow.transform, "提交", 1f, 2f);
        submitButton.image.color = new Color(0.14f, 0.39f, 0.92f, 1f);

        var statusText = CreateText(panelObject.transform, string.Empty, 28, false);
        SetTopStretch(statusText.rectTransform, -490f, 54f);
        statusText.alignment = TextAnchor.MiddleLeft;

        var form = panelObject.AddComponent<IssueForm>();
        form.panel = panelObject;
        form.titleInput = titleInput;
        form.descriptionInput = descriptionInput;
        form.serverInput = serverInput;
        form.settingsButton = settingsButton;
        form.serverRow = serverRow;
        form.lowButton = lowButton;
        form.mediumButton = mediumButton;
        form.highButton = highButton;
        form.submitButton = submitButton;
        form.cancelButton = cancelButton;
        form.coordText = coordText;
        form.statusText = statusText;

        return form;
    }

    // ---------------------------------------------------------------- 界面辅助

    /// <summary>
    /// 圆角矩形贴图，用作 9 宫格背景。UGUI 默认只有直角方块，
    /// 换成圆角是观感提升最直接的一步，且不需要引入第三方 UI 包。
    /// </summary>
    private static Sprite CreateRoundedSprite()
    {
        const string path = "Assets/Textures/UIRounded.png";
        const int size = 64;
        const int radius = 20;

        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            return existing;
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 只算到圆角矩形四个角距离，中间区域直接不透明
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                bool inside = dx * dx + dy * dy <= radius * radius;

                pixels[y * size + x] = inside
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteBorder = new Vector4(radius, radius, radius, radius);
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ApplyRoundedBackground(Image image, Color color)
    {
        Sprite sprite = CreateRoundedSprite();
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        image.color = color;
    }

    private static GameObject CreateFullScreenPanel(Transform canvas, string name, Color color, bool raycastTarget)
    {
        var panelObject = new GameObject(name, typeof(RectTransform));
        panelObject.transform.SetParent(canvas, false);

        var rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = panelObject.AddComponent<Image>();
        image.color = color;
        // 全屏面板本身不接收射线，否则会挡住 AR 场景的点击
        image.raycastTarget = raycastTarget;

        return panelObject;
    }

    private static Button CreateFixedButton(
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        Color color)
    {
        var buttonObject = new GameObject(label, typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);

        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = buttonObject.AddComponent<Image>();
        ApplyRoundedBackground(image, color);

        var button = buttonObject.AddComponent<Button>();
        CreateText(buttonObject.transform, label, 34);

        return button;
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
        ApplyRoundedBackground(image, new Color(0.22f, 0.25f, 0.31f, 1f));

        var button = buttonObject.AddComponent<Button>();
        CreateText(buttonObject.transform, label, 30);

        return button;
    }

    private static InputField CreateInputField(Transform parent, string placeholder, float topOffset, float height)
    {
        var inputObject = new GameObject("Input", typeof(RectTransform));
        inputObject.transform.SetParent(parent, false);

        SetTopStretch(inputObject.GetComponent<RectTransform>(), topOffset, height);

        var background = inputObject.AddComponent<Image>();
        ApplyRoundedBackground(background, new Color(1f, 1f, 1f, 0.2f));

        var input = inputObject.AddComponent<InputField>();

        Text text = CreateText(inputObject.transform, string.Empty, 28);
        text.alignment = TextAnchor.MiddleLeft;

        Text hint = CreateText(inputObject.transform, placeholder, 28);
        hint.alignment = TextAnchor.MiddleLeft;
        hint.color = new Color(1f, 1f, 1f, 0.55f);

        input.textComponent = text;
        input.placeholder = hint;

        return input;
    }

    private static Text CreateText(Transform parent, string content, int fontSize)
    {
        return CreateText(parent, content, fontSize, true);
    }

    private static Text CreateText(Transform parent, string content, int fontSize, bool stretch)
    {
        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        var rect = textObject.GetComponent<RectTransform>();

        if (stretch)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 0f);
            rect.offsetMax = new Vector2(-12f, 0f);
        }

        var text = textObject.AddComponent<Text>();
        text.font = ChineseFont();
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = content;
        // 文字不参与点击，按钮靠背景 Image 接收
        text.raycastTarget = false;

        return text;
    }

    private static void SetTopStretch(RectTransform rect, float topOffset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, topOffset);
        rect.sizeDelta = new Vector2(-40f, height);
    }

    /// <summary>
    /// 构建时用的占位字体。真正的中文字形由 ChineseFontApplier 在运行时替换。
    /// </summary>
    private static Font ChineseFont()
    {
        Font font = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" }, 32);

        if (font != null)
        {
            return font;
        }

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // ---------------------------------------------------------------- 配置与诊断

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

    [MenuItem("ARWork/3. 诊断 XR 配置")]
    public static void DiagnoseXR()
    {
        bool found = EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        Debug.Log("DIAG_TRYGET: found=" + found + " perTargetNull=" + (perTarget == null));

        if (perTarget != null)
        {
            XRGeneralSettings s = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            Debug.Log("DIAG_SETTINGS_ANDROID: " + (s == null ? "NULL" : AssetDatabase.GetAssetPath(s)));
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
}
