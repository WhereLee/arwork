# 一键部署：本地交叉编译 -> 打包 -> 上传 -> 服务器重启容器
#
# 用法（在仓库根目录）：
#     powershell -ExecutionPolicy Bypass -File .\deploy\deploy.ps1
#
# 前置条件：
#   - 本地已配置到服务器的免密 SSH（手册 §1）
#   - 服务器已装 docker + compose，且能免密 sudo
#   - 服务器安全组放行 9400
#
# 第一次部署后，需要去服务器上编辑 /opt/arwork/.env 填 DEEPSEEK_API_KEY，
# 然后重新执行本脚本或 sudo docker compose restart api。

$ErrorActionPreference = "Stop"

$server    = "ubuntu@124.223.36.154"
$remoteDir = "/opt/arwork"

$repo   = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$deploy = Join-Path $repo "deploy"
$tar    = Join-Path $repo "deploy.tar.gz"

Write-Host "[1/6] 交叉编译 Go (linux/amd64)" -ForegroundColor Cyan
Push-Location (Join-Path $repo "server")
$prevEap = $ErrorActionPreference
# go 和 vite 都会往 stderr 写警告，Stop 模式会把警告当致命错误，这里临时放宽
$ErrorActionPreference = "Continue"
$env:GOOS = "linux"
$env:GOARCH = "amd64"
$env:CGO_ENABLED = "0"
go build -trimpath -ldflags "-s -w" -o (Join-Path $deploy "arwork-server") .
$code = $LASTEXITCODE
Remove-Item Env:GOOS, Env:GOARCH, Env:CGO_ENABLED -ErrorAction SilentlyContinue
$ErrorActionPreference = $prevEap
Pop-Location
if ($code -ne 0) { throw "Go 编译失败，先修编译错误" }

Write-Host "[2/6] 构建前端（API 基址 /api）" -ForegroundColor Cyan
Push-Location (Join-Path $repo "web")
$ErrorActionPreference = "Continue"
$env:VITE_API_BASE = "/api"
npm run build 2>&1 | Select-Object -Last 3
$code = $LASTEXITCODE
Remove-Item Env:VITE_API_BASE -ErrorAction SilentlyContinue
$ErrorActionPreference = $prevEap
Pop-Location
if ($code -ne 0) { throw "前端构建失败" }

Write-Host "[3/6] 收集产物" -ForegroundColor Cyan
$distTarget = Join-Path $deploy "dist"
if (Test-Path $distTarget) { Remove-Item $distTarget -Recurse -Force }
Copy-Item (Join-Path $repo "web\dist") $distTarget -Recurse

Write-Host "[4/6] 打包" -ForegroundColor Cyan
if (Test-Path $tar) { Remove-Item $tar -Force }
tar -czf $tar -C $deploy arwork-server dist Dockerfile.api Dockerfile.web nginx.conf docker-compose.yml
if ($LASTEXITCODE -ne 0) { throw "打包失败" }
Write-Host ("      包大小 " + [Math]::Round((Get-Item $tar).Length / 1MB, 1) + " MB")

Write-Host "[5/6] 上传" -ForegroundColor Cyan
# /opt 属于 root，先建目录再交给 ubuntu，后面 scp 才不用 sudo
ssh -o ConnectTimeout=15 $server "sudo mkdir -p $remoteDir && sudo chown ubuntu:ubuntu $remoteDir"
if ($LASTEXITCODE -ne 0) { throw "创建远程目录失败" }
scp $tar "${server}:${remoteDir}/"
scp (Join-Path $repo "server\.env.example") "${server}:${remoteDir}/.env.example"

Write-Host "[6/6] 服务器上解压并重启" -ForegroundColor Cyan
$ErrorActionPreference = "Continue"
ssh $server "cd $remoteDir && tar -xzf deploy.tar.gz && rm -f deploy.tar.gz && (test -f .env || cp .env.example .env) && sudo docker compose up -d --build" 2>&1 | Select-Object -Last 12
$code = $LASTEXITCODE
$ErrorActionPreference = $prevEap
if ($code -ne 0) { throw "容器启动失败，登录服务器看 sudo docker compose logs" }

Write-Host "等待服务就绪..." -ForegroundColor Cyan
Start-Sleep -Seconds 8

$check = ssh $server "curl -s -o /dev/null -w '%{http_code}' http://localhost:9400/api/health; echo ''; curl -s -o /dev/null -w '%{http_code}' http://localhost:9400/"
Write-Host "健康检查（api / web）：" -NoNewline
ssh $server "printf 'api='; curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9400/api/health; printf 'web='; curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9400/"

Remove-Item $tar -Force
Write-Host ""
Write-Host "完成。对外地址 http://124.223.36.154:9400" -ForegroundColor Green
Write-Host "若 api 报 AI 未启用，登录服务器编辑 /opt/arwork/.env 填入 DEEPSEEK_API_KEY，" -ForegroundColor Yellow
Write-Host "然后执行：ssh $server 'cd /opt/arwork && sudo docker compose restart api'" -ForegroundColor Yellow
