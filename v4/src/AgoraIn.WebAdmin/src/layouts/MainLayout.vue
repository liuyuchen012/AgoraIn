<template>
  <el-container class="layout">
    <!-- 侧边栏 -->
    <el-aside width="220px" class="sidebar">
      <div class="brand">
        <span class="brand-dot"></span>
        <span class="brand-name">AgoraIn</span>
        <span class="brand-ver">v4.0</span>
      </div>
      <el-menu :default-active="route.path" router class="menu">
        <el-menu-item index="/dashboard">
          <el-icon><DataLine /></el-icon>
          <span>仪表盘</span>
        </el-menu-item>
        <el-menu-item index="/classes">
          <el-icon><School /></el-icon>
          <span>班级管理</span>
        </el-menu-item>
        <el-menu-item index="/students">
          <el-icon><User /></el-icon>
          <span>学生管理</span>
        </el-menu-item>
        <el-menu-item index="/devices">
          <el-icon><Monitor /></el-icon>
          <span>设备管理</span>
        </el-menu-item>
        <el-menu-item index="/classhours">
          <el-icon><Clock /></el-icon>
          <span>课时管理</span>
        </el-menu-item>
        <el-menu-item index="/notices">
          <el-icon><Bell /></el-icon>
          <span>通知公告</span>
        </el-menu-item>
        <el-menu-item index="/resources">
          <el-icon><FolderOpened /></el-icon>
          <span>资源库</span>
        </el-menu-item>
        <el-menu-item index="/exams">
          <el-icon><Document /></el-icon>
          <span>试卷管理</span>
        </el-menu-item>
        <el-menu-item index="/users">
          <el-icon><UserFilled /></el-icon>
          <span>用户管理</span>
        </el-menu-item>
        <el-menu-item index="/license">
          <el-icon><Key /></el-icon>
          <span>授权管理</span>
        </el-menu-item>
        <el-menu-item index="/settings">
          <el-icon><Setting /></el-icon>
          <span>系统设置</span>
        </el-menu-item>
      </el-menu>
    </el-aside>

    <el-container>
      <!-- 顶栏 -->
      <el-header class="header">
        <div class="header-title">{{ route.meta.title || '管理面板' }}</div>
        <div class="header-right">
          <el-tag v-if="auth.user" size="small" type="info">{{ roleText }}</el-tag>
          <span class="username">{{ auth.user?.username }}</span>
          <el-button link type="danger" @click="onLogout">退出</el-button>
        </div>
      </el-header>

      <!-- 内容区 -->
      <el-main class="main">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { DataLine, School, User, Monitor, Clock, Setting, UserFilled, Key, Bell, FolderOpened, Document } from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const roleText = computed(() => {
  const map: Record<string, string> = {
    admin: '管理员',
    teacher: '教师',
    viewer: '只读',
  }
  return map[auth.user?.role || ''] || auth.user?.role || ''
})

async function onLogout() {
  await ElMessageBox.confirm('确定要退出登录吗？', '提示', { type: 'warning' })
  auth.logout()
  router.push('/login')
}
</script>

<style scoped>
.layout {
  height: 100vh;
}

.sidebar {
  background: #fff;
  border-right: 1px solid #e4e7ed;
  display: flex;
  flex-direction: column;
}

.brand {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 60px;
  padding: 0 20px;
  border-bottom: 1px solid #f0f0f0;
}

.brand-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #4285f4;
}

.brand-name {
  font-size: 16px;
  font-weight: 600;
}

.brand-ver {
  font-size: 12px;
  color: #909399;
  margin-top: 2px;
}

.menu {
  border-right: none;
  flex: 1;
}

.header {
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 60px;
}

.header-title {
  font-size: 17px;
  font-weight: 600;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 12px;
}

.username {
  font-size: 14px;
  color: #606266;
}

.main {
  background: #f5f7fa;
  padding: 20px;
}
</style>
