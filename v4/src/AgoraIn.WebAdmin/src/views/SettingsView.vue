<template>
  <div class="settings">
    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">修改密码</span>
      </template>
      <el-form :model="form" label-width="100px" style="max-width: 420px">
        <el-form-item label="原密码" required>
          <el-input v-model="form.oldPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="新密码" required>
          <el-input v-model="form.newPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="确认新密码" required>
          <el-input v-model="form.confirm" type="password" show-password />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="saving" @click="onSubmit">保存</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">家长端隐私设置</span>
      </template>
      <el-form label-width="160px">
        <el-form-item label="家长端显示成绩">
          <el-switch v-model="showScores" />
          <span class="hint">开启后，家长可在小程序查看孩子的成绩概览（默认关闭）</span>
        </el-form-item>
      </el-form>
    </el-card>

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
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { ElMessage } from 'element-plus'
import { authApi } from '@/api/client'

const saving = ref(false)
const showScores = ref(false)
const form = reactive({ oldPassword: '', newPassword: '', confirm: '' })

async function onSubmit() {
  if (!form.oldPassword || !form.newPassword) {
    ElMessage.warning('请填写原密码和新密码')
    return
  }
  if (form.newPassword !== form.confirm) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }
  saving.value = true
  try {
    await authApi.changePassword(form.oldPassword, form.newPassword)
    ElMessage.success('密码修改成功')
    form.oldPassword = ''
    form.newPassword = ''
    form.confirm = ''
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.settings {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-card {
  border-radius: 10px;
}

.card-title {
  font-weight: 600;
}

.hint {
  margin-left: 12px;
  font-size: 12px;
  color: #909399;
}
</style>
