using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
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
