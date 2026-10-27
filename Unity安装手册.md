# Unity 2022.3 安装手册（本项目专用）

目标：在这台 i5-12500H / 16G / Iris Xe 的机器上装好 Unity，能往 K80 打出一个 ARCore APK。

---

## 0. 一句话结论

装 **Unity Hub + Unity 2022.3.x LTS + Android Build Support（含 SDK & NDK、OpenJDK）**，编辑器版本不要低于 2022.3，也不要用 Unity 6（国内已下架），不用团结引擎。

---

## 1. 下载入口

| 用途 | 地址 |
|---|---|
| Unity Hub（中国区） | https://unity.cn/download |
| 版本存档（直接下离线安装包） | https://unity.cn/releases/editor/archive |
| 团结引擎（仅备选，见第 9 节） | https://unity.cn/tuanjie/releases |

Hub 装好后，真正的编辑器是在 Hub 里下载的，不是从网页直接下。

如果 Hub 一直卡在下载进度或报 `Validation Failed`，直接去存档页找 2022.3.x，下那个 `Unity Editor (Windows)` 的离线安装程序，安装时同样能勾模块，不经过 Hub 的下载通道。

---

## 2. 装 Hub 的注意点

- 安装路径必须是**纯英文、无空格**：`F:\Unity\Hub`，不要 `F:\娱乐\unity hub`
- 首次打开要登录 Unity ID（中国区账号可直接注册）
- 登录这一步如果反复失败，先退出重开 Hub 再登，别急着重装

---

## 3. 装编辑器

Hub → 左侧 `Installs` → `Install Editor`：

1. 在版本列表里找 **2022.3.x**（有更新的 2022.3 补丁版就取最新的那个）。列表里没有就点 `Archive` / `存档` 展开历史版本
2. 版本号可能带 `c1` 后缀（中国区构建），以 Hub 里实际列出来的为准，能看到哪个就装哪个
3. 模块勾选：
   - ✅ Android Build Support
     - ✅ Android SDK & NDK Tools
     - ✅ OpenJDK
   - ✅ 简体中文（Language packs）
   - ❌ iOS Build Support —— Windows 上编不出包，白占 3~4 GB
   - ❌ Documentation —— 看在线文档就够
   - ❌ 其他平台（WebGL / Linux / UWP 等）
4. 安装位置改成 `F:\Unity`
5. 下载约 10~15 GB

装完在 Hub 的 Installs 列表里能看到 `2022.3.xx` 和它的路径，**把这个完整版本号记下来**，README 里要用。

---

## 4. 装完立刻做的三项设置

### 4.1 工程放哪
新建工程时路径同样要纯英文：`F:\arwork\unity`。C 盘只剩 27 GB，别放 C。

### 4.2 Player 设置（Android）
`Edit → Project Settings → Player → Android` 标签页：

- `Minimum API Level`：**24** 或更高（ARCore 要求）
- `Scripting Backend`：**IL2CPP**（ARM64 只支持 IL2CPP）
- `Target Architectures`：勾 **ARM64**（只勾它）
- `Other Settings → Graphics APIs`：**把 Vulkan 删掉**，只留 OpenGLES3。ARCore 和 Vulkan 组合容易出黑屏
- `Company Name` / `Product Name` 随便填，生成的包名就是 `com.公司名.产品名`

### 4.3 显卡（这台机器特有）
Iris Xe 的驱动是 2023 年的旧版。如果编辑器出现界面发黑、工具栏点不动、Scene 视图全黑，去 `Edit → Project Settings → Player → Other Settings` 确认 Graphics API，并把 `Edit → Preferences → General` 里的渲染相关选项切到 **D3D11**。改图标不影响构建结果。

---

## 5. 装 AR 相关的包

`Window → Package Manager`：

1. 左上角下拉切到 **Unity Registry**（不是 My Assets）
2. 搜 `AR Foundation`，装 **5.1.x**（不要 6.x，6.x 是给 Unity 6 的，装到 2022.3 上会报一堆依赖解析错误）
3. 搜 `ARCore XR Plugin`，装与 AR Foundation 同属 5.1 系列的版本
4. 装完打开 `Edit → Project Settings → XR Plug-in Management`，切到 **Android 标签页**，勾上 **ARCore**

XR Plug-in Management 这个包会被自动带入，看不到就去 Registry 里手动搜。

---

## 6. 手机侧准备（K80）

1. 设置 → 我的设备 → 全部参数 → 连点「MIUI 版本」7 次，开开发者选项
2. 开发者选项里打开：**USB 调试**、**USB 安装**、**USB 调试（安全设置）**
3. 数据线连电脑，手机上弹「允许 USB 调试吗」→ 允许（勾选 always）
4. 电脑上验证：

```powershell
adb devices
```

看不到设备就先查线（很多随机附带的线只有充电功能）和驱动（小米助手 / MiFlash 里带的 ADB 驱动）。

5. 确认 ARCore 服务在：

```powershell
adb shell pm list packages | findstr ar.core
```

应该输出 `package:com.google.ar.core`。没输出说明那个 AR 模块没装成。

---

## 7. 第一次构建会遇到的事

- **Gradle 依赖下载慢**：第一次 Build 时 Unity 会拉 Gradle 和 Android 依赖，国内可能卡几分钟到十几分钟，属于正常。卡死超过 20 分钟就取消重来，或用手机热点
- **SDK/JDK 路径**：`Edit → Preferences → External Tools` 里确认 `Android SDK Tools`、`JDK`、`NDK` 三项都指向 Unity 自带的目录（勾了模块就会自动填好，不要手动指到别处）
- **构建产物**在 `Builds/` 目录（已被 .gitignore 忽略，不会进仓库）
- **杀后台**：构建时关掉浏览器和大文件传输，16 GB 内存不宽裕

---

## 8. 报错对照

| 现象 | 优先怀疑 |
|---|---|
| `Gradle build failed` | 打开 Console 里的完整日志，通常是 SDK 路径或网络拉包失败 |
| `No Android device found` | adb devices 里有没有；线是否支持数据传输 |
| 应用装上后闪退 | Player 设置里 Vulkan 没删、ARM64 没勾、或 ARCore 插件没启用 |
| 黑屏但没崩 | ARCore 服务版本太旧，或设备不在支持列表 |
| 编辑器界面发黑、点不动 | 显卡驱动走 D3D12 出问题，切 D3D11（见 4.3） |
| 许可激活失败 | 退出 Unity 账号重新登录，别重装编辑器 |

---

## 9. 关于团结引擎

`unity.cn` 现在的下载页主推团结引擎（版本号 1.x，底层是 2022.3.61 的分支）。它能装 AR Foundation 5.1，理论上也能干这道题，但它加的东西（鸿蒙、微信小游戏、车机）这个项目一个都用不上，而它的工程和国际版 Unity 不保证完全互通，出问题时搜到的资料全对不上。

所以只有一种情况走团结引擎：Hub 里怎么也装不上 2022.3，离线包也拿不到。那种情况下装团结引擎 1.x，AR Foundation 照样装 5.1 系列。

---

## 10. 装完要回填的信息

装好后把这几项确认下来，README 和 AI_LOG 里要用到：

- Unity 编辑器完整版本号（如 `2022.3.62f1c1`）
- AR Foundation 版本
- ARCore XR Plugin 版本
- 打出来的 APK 在 K80 上 `ARSession` 状态是否正常（Unsupported / SessionTracking）
