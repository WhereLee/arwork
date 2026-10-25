# Git 与 CI 操作手册

> 给接手的 agent / 自己复习用。默认分支 `main`。

## 1. 提交（commit）约定
- 前缀：`feat:` / `fix:` / `test:` / `docs:` / `chore:`，简洁中文。
- **含中文的提交信息必须用 `-F` 文件**，且**无 BOM 写入**（PowerShell 5.1 的 `Set-Content -Encoding UTF8` 会加 BOM，导致 `git log` 中文显示成乱码 `锟?`；提交正文没坏但难读）：
```powershell
[IO.File]::WriteAllText("$PWD\.git\CM", "feat(xxx): 中文说明", (New-Object Text.UTF8Encoding $false))
git commit -q -F .git\CM ; Remove-Item .git\CM
```

## 2. 推送（push）约定：本地频繁 commit，阶段末批量 push
- 推送会触发 GitHub Actions CI（一次约 4–7 分钟）。**禁止为小改动（补一段文档、改错别字、单个小函数）单独推送**——只本地 commit，攒到一个里程碑齐备后再推。
- 推前本地自证（全绿才推）：
```powershell
cd <项目根目录>
.\.venv\Scripts\python.exe -m compileall -q app scripts
.\.venv\Scripts\python.exe -m pytest -q -m "not integration"      # 单测（秒级）
.\.venv\Scripts\python.exe -m pytest -q -m integration            # 集成（需本地 PG + bench/exp 库）
.\.venv\Scripts\python.exe -c "import app.api.main; print('IMPORT_OK')"
```

## 3. 推前必查：敏感/大文件没被 stage
```powershell
git add -A
git diff --cached --name-only | Select-String "\.env$|logs/|\.log|_wheels|\.whl|models/"
# 上面应无输出；.env/logs/_wheels/models 均已在 .gitignore
```
`.env` 含 API 密钥与 PG 口令，**绝不进 git**。任何密钥、服务器口令都不入库。

## 4. 看 CI（gh CLI）
```powershell
# 取最新一次 CI run id
$rid = gh run list -R <owner/repo> --workflow CI --limit 1 --json databaseId -q '.[0].databaseId'
# 阻塞式盯到结束（失败返回非零）
gh run watch $rid -R <owner/repo> --exit-status
# 看总结 + 各 job
gh run view $rid -R <owner/repo> --json conclusion,jobs
```
CI 共 4 个 job，都要 `success`：
- **unit-tests**：`pytest -m "not integration"`。
- **integration-tests**：用 `pgvector/pgvector:pg16` 服务容器 + `-m integration`。
- **secret-scan**：扫是否误提交密钥。
- **docker-build**：构建镜像验证（CPU torch + 挂载 models，权重不入镜像）。

## 5. CI 环境坑（改 workflow 前必读）
- **HF 端点**：仓库 `config.py` 默认 `HF_ENDPOINT=hf-mirror.com`（国内快），但 **GitHub runner 在海外连不上 hf-mirror** → integration job 必须显式覆盖 `HF_ENDPOINT=https://huggingface.co`。本地开发才用镜像。
- **PG 连接**：integration job 通过服务容器设 `PG_HOST=127.0.0.1 PG_PORT=5432 PG_USER/PG_PASSWORD/PG_DATABASE`。
- 服务器下载模型走 **ModelScope**（服务器直连 hf 也慢/不通）；这是运行环境差异，别把镜像端点写进默认配置。

## 6. 从服务器拉 GitHub 失败（CN 网络）
`GnuTLS recv error (-110)` 时用 HTTP/1.1 绕过：
```bash
git -c http.version=HTTP/1.1 fetch --depth 1 origin main
```

## 7. 常用一次性命令
```powershell
git log --oneline -8
git status -s
git push origin main 2>&1 | Select-Object -Last 1
```
