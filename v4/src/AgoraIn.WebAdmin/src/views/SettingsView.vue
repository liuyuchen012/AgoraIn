<template>
  <div class="settings">
    <!-- 修改密码 -->
    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">修改密码</span>
      </template>
      <el-form :model="pwdForm" label-width="100px" style="max-width: 420px">
        <el-form-item label="原密码" required>
          <el-input v-model="pwdForm.oldPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="新密码" required>
          <el-input v-model="pwdForm.newPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="确认新密码" required>
          <el-input v-model="pwdForm.confirm" type="password" show-password />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="pwdSaving" @click="onPwdSubmit">保存</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- SMTP 邮件服务 -->
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span class="card-title">SMTP 邮件服务</span>
          <el-tag v-if="smtp.configured" type="success" size="small">已配置</el-tag>
          <el-tag v-else type="info" size="small">未配置</el-tag>
        </div>
      </template>
      <p class="hint">配置 SMTP 后可启用注册验证码、密码重置邮件等功能。</p>
      <el-form :model="smtp" label-width="120px" style="max-width: 520px">
        <el-form-item label="服务器地址" required>
          <el-input v-model="smtp.host" placeholder="smtp.qq.com" />
        </el-form-item>
        <el-form-item label="端口" required>
          <el-input-number v-model="smtp.port" :min="1" :max="65535" />
        </el-form-item>
        <el-form-item label="发件人邮箱" required>
          <el-input v-model="smtp.from" placeholder="noreply@example.com" />
        </el-form-item>
        <el-form-item label="显示名称">
          <el-input v-model="smtp.displayName" placeholder="AgoraIn" />
        </el-form-item>
        <el-form-item label="用户名">
          <el-input v-model="smtp.user" placeholder="邮箱账号（通常与发件人相同）" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="smtp.password" type="password" show-password
            :placeholder="smtp.configured ? '留空则不修改已有密码' : '邮箱密码或授权码'" />
        </el-form-item>
        <el-form-item label="启用 SSL">
          <el-switch v-model="smtp.enableSsl" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="smtpSaving" @click="onSmtpSave">保存配置</el-button>
          <el-button @click="showTestDialog = true">发送测试邮件</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 家长端隐私设置 -->
    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">家长端隐私设置</span>
      </template>
      <el-form label-width="160px">
        <el-form-item label="家长端显示成绩">
          <el-switch v-model="showScores" />
          <span class="hint-inline">开启后，家长可在小程序查看孩子的成绩概览（默认关闭）</span>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 关于 -->
    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">关于</span>
      </template>
      <el-descriptions :column="1" border>
        <el-descriptions-item label="产品名称">AgoraIn 课堂签到打卡与班级教学管理平台</el-descriptions-item>
        <el-descriptions-item label="版本">v4.0</el-descriptions-item>
        <el-descriptions-item label="在线文档">
          <a href="https://doc.615mc.cn" target="_blank" rel="noreferrer">https://doc.615mc.cn</a>
        </el-descriptions-item>
        <el-descriptions-item label="授权">闭源商业软件</el-descriptions-item>
      </el-descriptions>
    </el-card>

    <!-- 测试邮件对话框 -->
    <el-dialog v-model="showTestDialog" title="发送测试邮件" width="380px" destroy-on-close>
      <el-form label-width="80px">
        <el-form-item label="收件地址">
          <el-input v-model="testEmail" placeholder="test@example.com" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showTestDialog = false">取消</el-button>
        <el-button type="primary" :loading="testSending" @click="onTestSend">发送</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { authApi, smtpApi } from '@/api/client'

// ── 修改密码 ──
const pwdSaving = ref(false)
const pwdForm = reactive({ oldPassword: '', newPassword: '', confirm: '' })

async function onPwdSubmit() {
  if (!pwdForm.oldPassword || !pwdForm.newPassword) {
    ElMessage.warning('请填写原密码和新密码')
    return
  }
  if (pwdForm.newPassword !== pwdForm.confirm) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }
  pwdSaving.value = true
  try {
    await authApi.changePassword(pwdForm.oldPassword, pwdForm.newPassword)
    ElMessage.success('密码修改成功')
    pwdForm.oldPassword = ''
    pwdForm.newPassword = ''
    pwdForm.confirm = ''
  } finally {
    pwdSaving.value = false
  }
}

// ── SMTP ──
const smtpSaving = ref(false)
const smtp = reactive({
  configured: false,
  host: '',
  port: 465,
  user: '',
  password: '',
  from: '',
  enableSsl: true,
  displayName: 'AgoraIn',
})

const showTestDialog = ref(false)
const testEmail = ref('')
const testSending = ref(false)

async function loadSmtp() {
  try {
    const data = await smtpApi.get()
    Object.assign(smtp, data)
  } catch { /* 静默 */ }
}

async function onSmtpSave() {
  if (!smtp.host || !smtp.from) {
    ElMessage.warning('请填写服务器地址和发件人邮箱')
    return
  }
  smtpSaving.value = true
  try {
    const result = await smtpApi.save(smtp)
    smtp.configured = result.configured
    ElMessage.success(result.message || '已保存')
  } finally {
    smtpSaving.value = false
  }
}

async function onTestSend() {
  if (!testEmail.value || !testEmail.value.includes('@')) {
    ElMessage.warning('请输入有效的邮箱地址')
    return
  }
  testSending.value = true
  try {
    await smtpApi.test(testEmail.value)
    ElMessage.success('测试邮件已发送，请查收')
    showTestDialog.value = false
  } catch {
    ElMessage.error('发送失败，请检查 SMTP 配置')
  } finally {
    testSending.value = false
  }
}

// ── 家长端隐私 ──
const showScores = ref(false)

onMounted(loadSmtp)
</script>

<style scoped>
.settings {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.page-card { border-radius: 10px; }
.card-header { display: flex; align-items: center; gap: 8px; }
.card-title { font-weight: 600; }
.hint { color: #909399; font-size: 12px; margin-bottom: 12px; }
.hint-inline { margin-left: 12px; font-size: 12px; color: #909399; }
</style>
