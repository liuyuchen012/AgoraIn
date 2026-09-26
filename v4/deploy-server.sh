#!/bin/bash
# AgoraIn v4 服务端部署脚本
# 用法: bash deploy-server.sh <host> <user> <password>
# 示例: bash deploy-server.sh 192.168.31.3 liuyuchen Nj96khu3

set -e

HOST=${1:-"192.168.31.3"}
USER=${2:-"liuyuchen"}
PASS=${3:-"Nj96khu3"}
REMOTE_DIR="/home/$USER/agorain-server"

echo "=== [1/4] 本地发布 ==="
dotnet publish src/AgoraIn.Server/AgoraIn.Server.csproj -c Release -o ./publish/server --self-contained false

echo "=== [2/4] 上传到服务器 ==="
sshpass -p "$PASS" ssh -o StrictHostKeyChecking=no "$USER@$HOST" "mkdir -p $REMOTE_DIR/data"
sshpass -p "$PASS" scp -r ./publish/server/* "$USER@$HOST:$REMOTE_DIR/"

echo "=== [3/4] 安装 systemd 服务 ==="
sshpass -p "$PASS" ssh -o StrictHostKeyChecking=no "$USER@$HOST" "sudo tee /etc/systemd/system/agorain.service > /dev/null << 'EOF'
[Unit]
Description=AgoraIn v4 Server
After=network.target

[Service]
Type=simple
User=$USER
WorkingDirectory=$REMOTE_DIR
ExecStart=$REMOTE_DIR/AgoraIn.Server
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5250

[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload
sudo systemctl enable agorain
sudo systemctl restart agorain"

echo "=== [4/4] 验证服务 ==="
sleep 2
curl -s "http://$HOST:5250/api/v4/setup/status" || echo "服务启动中..."
echo ""
echo "=== 部署完成 ==="
echo "Swagger: http://$HOST:5250/swagger"
echo "初始化: POST http://$HOST:5250/api/v4/auth/setup"
