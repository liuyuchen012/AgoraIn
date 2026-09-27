# AgoraIn v4 部署手册

## 1. 环境要求

| 项 | 要求 |
| --- | --- |
| 操作系统 | 服务端：Linux（推荐）/ Windows；桌面端：Windows 10 及以上 |
| .NET | **.NET 10 SDK**（构建）/ ASP.NET Core 10 Runtime（仅运行） |
| Node.js | ≥ 20（仅构建 Web 管理面板） |
| 数据库 | SQLite（默认，零配置）/ 可选 PostgreSQL |
| 端口 | 服务端默认 **5250**；Web 开发服务器 5173 |
| 域名 | **`agorain.615mc.cn`**（客户端硬编码，必须部署在此域名下） |
| WebView2 | 桌面端控制/教师模式内嵌网页依赖；Win10/11 通常已随 Edge 预装（缺失时自动降级为浏览器打开） |

> ⚠️ **服务器地址已锁定**：桌面端、小程序、移动端全部硬编码连接 `https://agorain.615mc.cn`，
> 客户端不提供服务器地址配置入口。服务端必须部署在该域名（HTTPS）下，否则客户端无法使用。
> 本地联调时在 hosts 中把该域名指向内网服务器即可，无需改动客户端。

## 2. 服务端部署

### 2.1 一键部署（推荐）

```bash
cd v4
bash deploy-server.sh <host> <user> <password>
# 示例：bash deploy-server.sh 192.168.31.3 liuyuchen <your-password>
```

脚本流程：
1. `dotnet publish src/AgoraIn.Server -c Release -o ./publish/server`
2. 上传到服务器 `/home/<user>/agorain-server/`
3. 安装并启动 systemd 服务 `agorain`
4. 验证 `GET /api/v4/setup/status`

> 依赖：本机需 `sshpass` 与 `ssh`（Windows 下可用 WSL/Git Bash 执行）。

### 2.2 手动部署

```bash
# 本地发布
dotnet publish src/AgoraIn.Server/AgoraIn.Server.csproj -c Release -o ./publish/server

# 上传
scp -r ./publish/server/* <user>@<host>:/home/<user>/agorain-server/

# 服务器上以 systemd 运行
sudo tee /etc/systemd/system/agorain.service > /dev/null <<'EOF'
[Unit]
Description=AgoraIn v4 Server
After=network.target

[Service]
Type=simple
User=<user>
WorkingDirectory=/home/<user>/agorain-server
ExecStart=/home/<user>/agorain-server/AgoraIn.Server
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5250

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload && sudo systemctl enable --now agorain
```

### 2.3 首次初始化

1. 浏览器打开 `http://<host>:5250/`，进入 Web 管理面板登录页
2. 页面检测到 `needsSetup` 时提示创建管理员 → 输入账号密码 → 自动登录
   （或直接调用 `POST /api/v4/auth/setup`）

### 2.4 配置项（`appsettings.json`）

```json
{
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://0.0.0.0:5250" } } },
  "Jwt": { "Key": "<生产环境请替换为随机密钥>" },
  "Data": { "Directory": "" },
  "Server": { "Password": "<集控连接密码，留空则 ClassIsland 接口免密>" },
  "DeepSeek": {
    "ApiKey": "<DeepSeek API Key>",
    "BaseUrl": "https://api.deepseek.com",
    "Model": "deepseek-chat"
  }
}
```

| 配置项 | 说明 |
| --- | --- |
| `Kestrel:Endpoints:Http:Url` | 监听地址与端口（默认 5250） |
| `Jwt:Key` | JWT 签名密钥，**生产必须替换** |
| `Data:Directory` | 数据目录（数据库与上传资源）。**留空 = 程序目录下的 `data/`**；宝塔等场景若程序目录不可写，可指向 `/www/wwwroot/xxx/data` 之类 |
| `Server:Password` | ClassIsland 插件连接密码（留空则该组接口免密，仅建议内网） |
| `DeepSeek:*` | AI 阅卷配置 |

> **数据目录会自动创建**：服务端启动时若 `data/` 不存在会自动建立（含 `resources/` 子目录），
> 无需手动 mkdir。若仍报 `SQLite Error 14: unable to open database file`，说明
> **运行用户对该路径没有写权限**，请执行 `chown -R <运行用户> <数据目录>`（宝塔通常为 `www`）。

> **安全提示**：`Jwt:Key` 与 `DeepSeek:ApiKey` 属敏感信息，生产环境建议用环境变量覆盖
> （`Jwt__Key`、`DeepSeek__ApiKey`、`Data__Directory`），不要提交到仓库。

### 2.5 域名与 HTTPS 反向代理

客户端硬编码访问 `https://agorain.615mc.cn`，因此需在该域名上配置反向代理到本机 5250：

```nginx
server {
    listen 443 ssl http2;
    server_name agorain.615mc.cn;

    ssl_certificate     /etc/letsencrypt/live/agorain.615mc.cn/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/agorain.615mc.cn/privkey.pem;

    # Web 管理面板 + REST API
    location / {
        proxy_pass http://127.0.0.1:5250;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SignalR 实时推送（必须开启 WebSocket 升级）
    location /hub/ {
        proxy_pass http://127.0.0.1:5250;
        proxy_http_version 1.1;
        proxy_set_header Upgrade    $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host       $host;
        proxy_read_timeout 3600s;
    }

    # 答题卡照片/资源上传体积上限
    client_max_body_size 200m;
}
```

> 若用 Caddy，等价配置为 `agorain.615mc.cn { reverse_proxy 127.0.0.1:5250 }`（WebSocket 自动处理）。

**验证**：`curl -I https://agorain.615mc.cn/api/v4/setup/status` 应返回 200。

**本地联调**：不改客户端，在 hosts 中加入 `192.168.31.3 agorain.615mc.cn` 即可让客户端连到内网服务器。

## 3. Web 管理面板部署

Web 端产物由服务端以静态文件托管：

```bash
cd src/AgoraIn.WebAdmin
npm install
npm run build            # 产出 dist/
# 把 dist/ 内容复制到服务端的 wwwroot/
cp -r dist/* ../AgoraIn.Server/wwwroot/
```

开发模式（热更新 + 自动代理到 5250）：

```bash
npm run dev              # 打开 http://localhost:5173
```

## 4. 桌面端构建

```bash
dotnet build src/AgoraIn.App/AgoraIn.App.csproj -c Release
# 产出：src/AgoraIn.App/bin/Release/net10.0/AgoraIn.exe
```

Windows 10 兼容：`v4/Directory.Build.props` 已统一设置 `CETCompat=false`
（.NET 10 在 ntdll < 19041.5007 的 Win10 上默认 CET 检查会致命报错，沿用 v3.2 commit `32509aa` 方案）。

## 5. 移动端构建

> **共同注意**：`AgoraIn.Mobile` 是多目标项目（android / ios / windows）。
> 直接 `publish`/`build` 时隐式还原**不会**包含需要的 RID 目标，会报
> `NETSDK1047: assets 文件没有 xx/yy 的目标` 或要求安装其它平台的工作负载。
> 正确做法：**先按「单一 TFM + 单一 RID」显式还原，再带 `--no-restore` 构建**。

### Android

```bash
dotnet workload install maui-android

# 先按 TFM + RID 还原，再发布（-r android-arm64 对应绝大多数现代手机）
dotnet restore src/AgoraIn.Mobile/AgoraIn.Mobile.csproj \
  -p:TargetFrameworks=net10.0-android -p:RuntimeIdentifier=android-arm64
dotnet publish src/AgoraIn.Mobile/AgoraIn.Mobile.csproj \
  -f net10.0-android -c Release -r android-arm64 --no-restore \
  -p:AndroidPackageFormat=apk -o publish/android
# 产出 APK：publish/android/*.apk（默认未签名，装机需自行签名）
```

### iOS（需 macOS + Xcode）

```bash
dotnet workload install maui-ios

# macOS 上 TargetFrameworks 同时含 android + ios，还原必须限定为 ios，
# 否则报「the following workloads must be installed: android」
dotnet restore src/AgoraIn.Mobile/AgoraIn.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet build src/AgoraIn.Mobile/AgoraIn.Mobile.csproj \
  -f net10.0-ios -c Release -p:RuntimeIdentifier=iossimulator-arm64 --no-restore \
  -o publish/ios
# 产出 .app（模拟器用，免签名）；真机分发需 Apple 证书 + Provisioning Profile
```

## 6. 家长端小程序

小程序为**独立仓库**（`miniprogram-parent/`，已由 `.gitignore` 排除）：

1. 用微信开发者工具打开 `v4/miniprogram-parent/`
2. AppID：`wxa2003db2b4693a66`
3. 首次使用：家长在「绑定孩子」页输入老师提供的 6 位邀请码
   （老师端在 Web 面板「学生管理 → 生成家长邀请码」或桌面端教师模式生成）

## 7. 质量门禁

```powershell
powershell -ExecutionPolicy Bypass -File v4/scripts/gate.ps1
```

- 门禁覆盖 `AgoraIn.sln`（Core / Data / App / Server / tests）
- `--selftest` 报告落在 `v4/out/selftest-report.txt`（GUI 子系统无控制台，必须用 `--out` 落盘）
- MAUI 项目不在门禁内，按上文单独构建

## 7.1 自动构建（GitHub Actions）

工作流：`.github/workflows/v4-build.yml`（清理：`.github/workflows/cleanup-artifacts.yml`）

| 作业 | 运行环境 | 产物 |
| --- | --- | --- |
| 质量门禁 | ubuntu | build + `dotnet test`（失败时留存 trx，1 天） |
| Web 管理面板 | ubuntu | `dist/`（约 1MB，供服务端打包用） |
| 桌面端 ×5 | windows / ubuntu / macos | `AgoraIn-{win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64}.zip`（自包含，解压即用） |
| 服务端 ×3 | windows / ubuntu / macos | `AgoraIn-Server-{win-x64, linux-x64, osx-arm64}.zip`（已内置 Web 管理面板到 wwwroot） |
| 移动端 Android | ubuntu | `AgoraIn-Android.apk` / `.aab` |
| 移动端 iOS | macos | `AgoraIn-iOS-simulator.zip`（模拟器构建，免签名） |

### 触发策略与产物发布

| 触发 | 执行内容 | 发布 |
| --- | --- | --- |
| `push` / PR（改动 `v4/**`） | 门禁 + Web 面板 + 桌面端 ×5 + 服务端 ×3 + 移动端 ×2（全平台） | push 到分支时**自动发布预发布版本** |
| 手动打 tag（如 `v4.0.1`） | 同上，使用该 tag | **正式版本**（非预发布） |
| 手动 `workflow_dispatch` | 同上 | 打 tag 逻辑同上（分支触发则预发布） |

**自动发布规则**（`release` 作业，需桌面端与服务端构建成功；移动端失败不阻塞）：

- 分支 push → 自动打 tag `v4.0.0-build.{运行号}`，发布为**预发布**（prerelease）
- 手动打 tag → 用该 tag 发布**正式版本**
- Release 说明自动生成变更日志 + 平台文件对照表；附件包含全部 zip / apk / aab

> 自动生成的 `v4.0.0-build.*` tag 仍在 `tags: ['v4*']` 触发范围内，但
> **用默认 `GITHUB_TOKEN` 创建 tag 不会触发新的工作流运行**，因此不会形成循环；
> 工作流另加了 `!startsWith(github.ref_name, 'v4.0.0-build.')` 守卫，换成 PAT 时同样安全。

产物保留 7 天（Actions 产物），过期清理见 `cleanup-artifacts.yml`；
**Release 附件长期保存**，是推荐的分发渠道。

### 产物签名

iOS 与 macOS 产物**默认不签名**，仅供内部测试与验证编译；正式分发需在仓库
Secrets 中配置 Apple 证书 / 签名密钥后扩展对应步骤。Android Release 包同样需要
配置 keystore 才会产出已签名 APK。

## 8. 备份与数据

| 数据 | 位置 | 备份建议 |
| --- | --- | --- |
| 服务端数据库 | `<server>/data/server.db` | 每日快照 |
| 服务端上传资源 | `<server>/data/resources/` | 随数据库一起备份 |
| 桌面端本地数据 | 客户端目录 `data/` + `workspace.json` | 客户端「文件 → 导出打卡数据」CSV |

## 9. 常见问题

**Q：桌面端提示 `Failed to load ntdll` 或启动即崩溃？**
A：确认 `CETCompat=false` 已生效（`Directory.Build.props`），且运行在 Windows 10 19041+ 或 Windows 11。

**Q：`dotnet build` 报 MSB3027「文件被占用」？**
A：先结束在跑的进程：`Stop-Process -Name AgoraIn -Force`。

**Q：MAUI 构建报 `NETSDK1047: 资产文件没有 xx/win-x64 的目标`？**
A：需按 RID 特化还原（见第 5 节 Windows 部分），不能直接 `dotnet build`。

**Q：Web 面板登录后立刻跳回登录页？**
A：检查 `Jwt:Key` 是否为空或过短；令牌签发失败会导致 401。

**Q：Web 面板登录后立刻提示「登录已过期」并跳回登录页？**
A：这是服务端签发的 JWT 格式问题（历史版本用 `JwtSecurityToken.ToString()` 返回了调试用 JSON
而非紧凑序列化），升级到最新服务端即可。若确认版本已最新，请用浏览器开发者工具查看
`/api/v4/*` 请求的响应头 `WWW-Authenticate`，其中的 `IDX14xxx` 错误码会指出具体原因。

**Q：访问 `http://<host>:5250/login` 返回 404？**
A：Web 管理面板是单页应用（history 路由），需要服务端把未知路径回退到 `index.html`
（`app.MapFallbackToFile("index.html")`，已内置）。同时确认 `wwwroot/` 下确实有 Web 面板产物
（`index.html` + `assets/`）；没有的话按第 3 节把 `dist/` 的内容复制进去。

**Q：ClassIsland 插件提示「无法连接集控平台」？**
A：检查插件「服务器地址」配置、服务端 `Server:Password` 是否与插件一致；契约见
[api-contract-classisland.md](./api-contract-classisland.md)。
