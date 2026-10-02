#!/bin/bash
# AgoraIn v4 服务端部署脚本
# 用法: bash deploy-server.sh <host> <user> [password]
# 密码也可用环境变量 AGORAIN_SSH_PASS 提供：
#   AGORAIN_SSH_PASS=xxx bash deploy-server.sh 49.232.203.254 ubuntu
# 密码不得写进本文件（仓库为公开仓库）。

set -e

HOST=${1:-"49.232.203.254"}
USER=${2:-"ubuntu"}
PASS=${3:-"$AGORAIN_SSH_PASS"}
if [ -z "$PASS" ]; then
  echo "错误：未提供 SSH 密码。请用第三个参数或环境变量 AGORAIN_SSH_PASS 提供。" >&2
  exit 1
fi
REMOTE_DIR="/home/$USER/agorain-server"

echo "=== [1/5] 构建 WebAdmin ==="
cd src/AgoraIn.WebAdmin && npm ci && npm run build && cd ../..

echo "=== [2/5] 本地发布（含 WebAdmin，自包含：生产服务器未安装 dotnet 运行时） ==="
dotnet publish src/AgoraIn.Server/AgoraIn.Server.csproj -c Release -o ./publish/server --self-contained true -r linux-x64
mkdir -p publish/server/wwwroot
cp -r src/AgoraIn.WebAdmin/dist/* publish/server/wwwroot/

echo "=== [3/5] 上传到服务器 ==="
sshpass -p "$PASS" ssh -o StrictHostKeyChecking=no "$USER@$HOST" "mkdir -p $REMOTE_DIR/data"
sshpass -p "$PASS" scp -r ./publish/server/* "$USER@$HOST:$REMOTE_DIR/"

echo "=== [4/5] 重启服务 ==="
sshpass -p "$PASS" ssh -o StrictHostKeyChecking=no "$USER@$HOST" "cd $REMOTE_DIR && sudo systemctl restart agorain 2>/dev/null || (sudo pkill -9 AgoraIn.Server 2>/dev/null; nohup ./AgoraIn.Server > /dev/null 2>&1 &)"

echo "=== [5/5] 验证 ==="
sleep 3
curl -s "http://$HOST:5250/api/v4/setup/status" || echo "服务启动中..."
echo ""
echo "=== 部署完成 ==="
