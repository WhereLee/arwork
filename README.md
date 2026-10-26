# 现场巡检问题标注

园区巡检人员在手机上用 AR 放置标记并上报问题，管理人员在网页端查看问题、更新处理状态。

三端：Unity（AR 采集）→ Go（API + SQLite）→ React（管理页面），共用同一个后端和同一份数据。

## 版本

| 部分 | 版本 |
|---|---|
| Unity | 2022.3.61f1c1（中国版） |
| AR Foundation | 5.1.6 |
| ARCore XR Plugin | 5.1.6 |
| Go | 1.26.3（见下方说明） |
| SQLite 驱动 | modernc.org/sqlite v1.61.0（纯 Go，不需要 gcc） |
| Node.js | 18 以上 |
| React / TypeScript / Vite | 18.3 / 5.6 / 6.0 |
| Ant Design | 5.29 |

Go 版本说明：`go.mod` 声明 `go 1.26.3`，这是依赖链的上限要求，不是我自己定的。用低于 1.26 的 Go 打开时，Go 1.21 起默认的 `GOTOOLCHAIN=auto` 会自动下载对应工具链（需要联网）；也可以手动安装 1.26.3。

## 启动

### 1. Go 后端

```bash
cd server
go mod download
go run .
```

默认监听 `:8080`，数据库文件是 `server/data/issues.db`，首次启动自动建表。

可用环境变量：

- `ADDR`：监听地址，默认 `:8080`
- `DB_PATH`：数据库路径，默认 `data/issues.db`

注意：当前只自动创建 `data` 这一层目录。把 `DB_PATH` 指到别的目录时，需要先手动建好那个目录。

### 2. React 管理端

```bash
cd web
npm install
npm run dev
```

打开 `http://localhost:5173`。前端直连 `http://localhost:8080`，靠后端的 CORS 放行，没有走 Vite 代理。

### 3. Unity AR 端

用 Unity Hub 以 **2022.3.61f1c1** 打开 `unity/` 目录。

构建前确认：`Project Settings → XR Plug-in Management → Android` 里 **ARCore** 已勾选。这个配置必须由界面勾选生成，脚本重建不生效。

菜单：

- `ARWork / 1. 生成巡检场景`：重建 `Assets/Scenes/Main.unity`
- `ARWork / 2. 构建 APK`：输出到 `unity/Builds/arwork.apk`

批处理方式：

```bash
Unity.exe -batchmode -nographics -projectPath <仓库>/unity -buildTarget Android \
  -executeMethod SceneBuilder.BuildPlayground -quit -logFile build1.log

Unity.exe -batchmode -nographics -projectPath <仓库>/unity -buildTarget Android \
  -executeMethod SceneBuilder.BuildApk -quit -logFile build2.log
```

## Unity 真机如何配置后端地址

手机和电脑必须在同一个局域网。

默认地址写在 `unity/Assets/Scripts/ApiClient.cs` 的 `DefaultBaseUrl`，当前是 `http://192.168.1.5:8080`，构建时会写进表单的服务器地址输入框。

换网络后有两种改法：

1. **App 内临时改**：放好标记、表单弹出后，点右上角「设置」展开服务器地址输入框，改成电脑当前的 IP。改完对本次提交生效，不持久化。
2. **改代码重新构建**：改 `ApiClient.cs` 里的 `DefaultBaseUrl`，重跑场景生成和打包。

## 接口

| 方法 | 路径 | 说明 | 成功响应 |
|---|---|---|---|
| GET | `/health` | 服务运行状态 | `200` `{"status":"ok"}` |
| POST | `/issues` | 新建问题 | `201` 完整 Issue |
| GET | `/issues` | 问题列表（按创建时间正序，先报的在前） | `200` Issue 数组 |
| PATCH | `/issues/{id}/status` | 按 ID 修改状态 | `200` 更新后的 Issue |

错误统一返回 `{"error": "中文原因"}`：

- `400` 请求体不是合法 JSON、标题为空、优先级或状态不在枚举内
- `404` 问题 ID 不存在
- `500` 存储失败

问题结构（三端字段一致）：

```json
{
  "id": "由后端生成的 32 位十六进制串",
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "status": "open",
  "position": { "x": 0.42, "y": 0.03, "z": 1.26 },
  "createdAt": "2026-10-26T14:30:00Z",
  "updatedAt": "2026-10-26T14:30:00Z"
}
```

枚举取值：

- `priority`：`low` / `medium` / `high`，严格匹配小写字面量
- `status`：`open` / `in_progress` / `resolved`，三个状态之间允许任意互改，不做流转限制

## Unity 端操作流程

1. 启动后是引导页，选择要标记的表面：**水平面（桌面或地面）** 或 **垂直面（墙面）**
2. 摄像头打开，缓慢移动手机扫描。屏幕中心会出现一个白色方框，它贴在检测到的平面上——方框出现就代表这一下能放
3. 对准目标后点屏幕，命中位置立起一块**黄底黑边感叹号**的警示牌
4. 底部弹出表单：标题（必填）、描述（选填）、优先级（低/中/高），提交
5. 提交成功后牌面变绿；失败会显示具体原因（连不上服务器、内容不符合要求等）
6. 检测中可随时用右上角「水平 / 垂直」切换目标表面，不用退出重扫；「退出」会先确认，确认后清空本轮标记并回到引导页

## 已完成

- **Go 后端**：四个接口、SQLite 持久化（重启后数据仍在）、ID 生成、默认状态 `open`、标题为空与枚举错误的参数校验、CORS
- **React 管理端**：问题列表（标题、描述、优先级、状态、AR 坐标、创建时间）、状态下拉修改、加载中状态、后端不可用时显示错误提示和重试按钮、空列表提示
- **Unity AR 端**：真机（Redmi K80 / Android）运行、水平与垂直平面检测、屏幕中心方框指示落点、点击放置警示牌标记、表单填写与提交、成功/失败提示、点按钮和输入框不会误放标记
- **三端联调**：手机提交 → Go 存库 → 网页刷新可见 → 网页改状态，已实际跑通

## 未完成 / 已知问题

- **后端地址默认写死**为 `192.168.1.5`。换电脑或换网段后，首次提交会失败，需要按上面「配置后端地址」改一次。
- **App 重启后 AR 场景里的标记不会恢复**。题目不要求恢复真实物理位置，这里也没做锚点持久化。
- **Unity 端没有历史问题列表**，看不到别人报过的问题。
- **没有登录、权限、人员管理**，任何人都能提交和改状态。
- **CORS 设成 `*`、接口无鉴权**，只适合内网演示。
- **垂直面检测依赖墙面纹理**。白墙、玻璃、反光面大概率检测不出来，这是 ARCore 的平台限制，不是代码问题。
- **中文界面依赖设备系统字体**（Noto Sans CJK 等）。代码会从系统字体里挑一个能用的，找不到时只打日志继续运行，届时中文会显示为方块。
- **没有自动化测试**，验证靠手工联调和 adb 日志。
