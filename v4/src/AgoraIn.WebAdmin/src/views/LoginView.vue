<template>
  <div class="login-page">
    <el-card class="login-card">
      <div class="logo">
        <div class="logo-circle">A</div>
        <h1>AgoraIn 管理面板</h1>
        <p class="subtitle">课堂签到打卡与班级教学管理平台</p>
      </div>

      <!-- 首次初始化 -->
      <el-alert
        v-if="needsSetup"
        title="首次使用：请创建管理员账户"
        type="warning"
        :closable="false"
        show-icon
        class="setup-alert"
      />

      <el-form :model="form" @submit.prevent="onSubmit">
        <el-form-item>
          <el-input
            v-model="form.username"
            placeholder="用户名（子区域：用户名@区域代号，主区域：用户名@manager）"
            size="large"
            :prefix-icon="User"
          />
        </el-form-item>
        <el-form-item>
          <el-input
            v-model="form.password"
            type="password"
            placeholder="密码"
            size="large"
            show-password
            :prefix-icon="Lock"
            @keyup.enter="onSubmit"
          />
        </el-form-item>

        <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon class="error-alert" />

        <el-button
          type="primary"
          size="large"
          class="submit-btn"
          :loading="loading"
          @click="onSubmit"
        >
          {{ needsSetup ? '创建管理员并登录' : '登录' }}
        </el-button>
      </el-form>

      <div class="register-hint">
        没有账户？<router-link to="/register" class="link">注册区域 / 家长注册</router-link>
      </div>

      <div class="footer">AgoraIn v4.0 · 闭源商业软件</div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { User, Lock } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { authApi } from '@/api/client'

const router = useRouter()
const auth = useAuthStore()

const form = reactive({ username: '', password: '' })
const loading = ref(false)
const error = ref('')
const needsSetup = ref(false)

onMounted(async () => {
  try {
    const res: { needsSetup: boolean } = await authApi.setupStatus()
    needsSetup.value = !!res.needsSetup
  } catch {
    // 服务端未就绪时忽略
  }
})

async function onSubmit() {
  error.value = ''
  if (!form.username || !form.password) {
    error.value = '请输入用户名和密码'
    return
  }

  loading.value = true
  try {
    if (needsSetup.value) {
      await authApi.setup(form.username, form.password)
      ElMessage.success('管理员创建成功')
    }
    await auth.login(form.username, form.password)
    router.push('/dashboard')
  } catch (err) {
    error.value = (err as Error).message || '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #4285f4 0%, #3367d6 100%);
}

.login-card {
  width: 420px;
  padding: 20px;
  border-radius: 12px;
}

.logo {
  text-align: center;
  margin-bottom: 28px;
}

.logo-circle {
  width: 56px;
  height: 56px;
  margin: 0 auto 16px;
  border-radius: 50%;
  background: #4285f4;
  color: #fff;
  font-size: 28px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}

h1 {
  font-size: 20px;
  margin-bottom: 6px;
}

.subtitle {
  font-size: 13px;
  color: #909399;
}

.setup-alert,
.error-alert {
  margin-bottom: 16px;
}

.submit-btn {
  width: 100%;
}

.footer {
  text-align: center;
  font-size: 12px;
  color: #c0c4cc;
  margin-top: 24px;
}

.register-hint {
  text-align: center;
  font-size: 13px;
  color: #909399;
  margin-top: 14px;
}

.register-hint .link {
  color: #4285f4;
  text-decoration: none;
}
</style>
