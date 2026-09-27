<template>
  <div class="license">
    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">授权管理</span>
      </template>

      <el-descriptions :column="2" border>
        <el-descriptions-item label="本机指纹">
          <el-tag type="info" size="small" style="font-family:monospace">{{ state.fingerprint || '加载中…' }}</el-tag>
          <el-button link type="primary" size="small" @click="copyFingerprint" style="margin-left:8px">复制</el-button>
        </el-descriptions-item>
        <el-descriptions-item label="授权状态">
          <el-tag :type="state.activated ? 'success' : 'danger'" size="default">
            {{ statusText }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="授权类型">{{ state.type || '—' }}</el-descriptions-item>
        <el-descriptions-item label="客户名称">{{ state.customer || '—' }}</el-descriptions-item>
        <el-descriptions-item label="流水号">{{ state.serial || '—' }}</el-descriptions-item>
        <el-descriptions-item label="设备上限">{{ state.max_devices ? `${state.max_devices} 台` : '—' }}</el-descriptions-item>
        <el-descriptions-item label="激活时间">{{ state.activated_at ? new Date(state.activated_at).toLocaleString() : '—' }}</el-descriptions-item>
        <el-descriptions-item label="到期时间">{{ state.expire_at ? new Date(state.expire_at).toLocaleString() : '—' }}</el-descriptions-item>
        <el-descriptions-item label="剩余天数">
          <span :style="{ color: (state.remaining_days ?? 999) <= 30 ? '#e6a23c' : '#67c23a', fontWeight: 600 }">
            {{ state.remaining_days != null ? `${state.remaining_days} 天` : '—' }}
          </span>
        </el-descriptions-item>
      </el-descriptions>

      <el-alert v-if="state.warning" :title="state.warning" type="warning" show-icon :closable="false" style="margin-top:16px" />
    </el-card>

    <el-card shadow="never" class="page-card">
      <template #header>
        <span class="card-title">输入激活码</span>
      </template>
      <p class="hint">将激活码粘贴到下方，点击「激活」完成授权。激活码格式：<code>AGRN-xxxxx-xxxxx-…</code></p>

      <el-input v-model="activationCode" type="textarea" :rows="3" placeholder="请粘贴完整的激活码" style="max-width:600px" />

      <div style="margin-top:12px;display:flex;gap:12px;align-items:center">
        <el-button type="primary" :loading="activating" @click="onActivate">激活</el-button>
        <el-checkbox v-model="forceRebind">强制重绑（换机时使用）</el-checkbox>
      </div>

      <el-alert v-if="activateResult" :title="activateResult" :type="activateOk ? 'success' : 'error'" show-icon :closable="true" style="margin-top:12px" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { licenseApi } from '@/api/client'

interface LicenseState {
  fingerprint: string
  activated: boolean
  status: string
  type: string
  customer: string
  serial: string
  months: number
  max_devices: number
  activated_at: string
  expire_at: string
  remaining_days: number | null
  warning: string
}

const state = ref<Partial<LicenseState>>({})
const activationCode = ref('')
const forceRebind = ref(false)
const activating = ref(false)
const activateResult = ref('')
const activateOk = ref(false)

const statusText = computed(() => {
  if (!state.value.activated) return '未激活'
  if (state.value.status === 'Expired') return '已过期'
  if (state.value.status === 'ExpiringSoon') return '即将到期'
  return '已激活'
})

async function loadState() {
  try {
    state.value = await licenseApi.get()
  } catch { /* 拦截器已处理 */ }
}

function copyFingerprint() {
  if (state.value.fingerprint) {
    navigator.clipboard.writeText(state.value.fingerprint)
    ElMessage.success('指纹已复制')
  }
}

async function onActivate() {
  if (!activationCode.value.trim()) {
    ElMessage.warning('请输入激活码')
    return
  }
  activating.value = true
  activateResult.value = ''
  try {
    const result = await licenseApi.activate(activationCode.value.trim(), forceRebind.value)
    activateOk.value = true
    activateResult.value = result.message || '激活成功'
    await loadState()
  } catch (err: any) {
    activateOk.value = false
    activateResult.value = err.response?.data?.error || '激活失败'
  } finally {
    activating.value = false
  }
}

onMounted(loadState)
</script>

<style scoped>
.license {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.page-card { border-radius: 10px; }
.card-title { font-weight: 600; }
.hint { color: #606266; font-size: 13px; margin-bottom: 12px; line-height: 1.6; }
code { background: #f5f7fa; padding: 2px 6px; border-radius: 4px; font-size: 12px; }
</style>
