<template>
  <div class="license">
    <!-- 租户模式：区域状态与激活 -->
    <el-card v-if="!auth.isManager" shadow="never" class="page-card">
      <template #header>
        <span class="card-title">区域授权状态</span>
      </template>
      <el-descriptions :column="2" border v-loading="regionLoading">
        <el-descriptions-item label="区域代号">{{ regionMe.regionId || '—' }}</el-descriptions-item>
        <el-descriptions-item label="区域名称">{{ regionMe.name || '—' }}</el-descriptions-item>
        <el-descriptions-item label="授权状态">
          <el-tag :type="regionMe.isActive ? 'success' : regionMe.activated ? 'danger' : 'warning'">
            {{ regionMe.isActive ? '使用中' : regionMe.activated ? '已到期' : '未激活' }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="到期时间">{{ regionMe.expireAt ? new Date(regionMe.expireAt).toLocaleString() : '—' }}</el-descriptions-item>
        <el-descriptions-item label="设备上限">{{ regionMe.maxDevices || '—' }}</el-descriptions-item>
        <el-descriptions-item label="剩余天数">
          <span :style="{ color: (regionMe.remainingDays ?? 999) <= 30 ? '#e6a23c' : '#67c23a', fontWeight: 600 }">
            {{ regionMe.remainingDays != null ? `${regionMe.remainingDays} 天` : '—' }}
          </span>
        </el-descriptions-item>
      </el-descriptions>
      <el-alert v-if="regionMe.isActive" type="success" :closable="false" show-icon style="margin-top:14px"
                title="区域授权有效，区域成员可正常登录使用。" />
    </el-card>

    <!-- 租户模式：输入主区域颁发的激活码 -->
    <el-card v-if="!auth.isManager" shadow="never" class="page-card">
      <template #header>
        <span class="card-title">激活区域</span>
      </template>
      <p class="hint">请向平台运营方（主区域）索取本区域的激活码（AGRR- 开头），粘贴到下方完成激活或续期。激活后区域成员方可登录使用。</p>
      <el-input v-model="regionCode" type="textarea" :rows="3" placeholder="请粘贴完整的区域激活码（AGRR-…）" style="max-width:600px" />
      <div style="margin-top:12px">
        <el-button type="primary" :loading="regionActivating" @click="onRegionActivate">激活区域</el-button>
      </div>
      <el-alert v-if="regionResult" :title="regionResult" :type="regionOk ? 'success' : 'error'" show-icon :closable="true" style="margin-top:12px" />
    </el-card>

    <!-- 管理员模式：服务器整体授权 -->
    <el-card v-if="auth.isManager" shadow="never" class="page-card">
      <template #header>
        <span class="card-title">服务器授权（主区域）</span>
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

    <el-card v-if="auth.isManager" shadow="never" class="page-card">
      <template #header>
        <span class="card-title">输入激活码</span>
      </template>
      <p class="hint">将服务器激活码粘贴到下方，点击「激活」完成服务器整体授权。激活码格式：<code>AGRN-xxxxx-xxxxx-…</code>（区域租户请勿使用此栏——租户激活码在上方"激活区域"处输入）</p>

      <el-input v-model="activationCode" type="textarea" :rows="3" placeholder="请粘贴完整的激活码" style="max-width:600px" />

      <div style="margin-top:12px;display:flex;gap:12px;align-items:center">
        <el-button type="primary" :loading="activating" @click="onActivate">激活</el-button>
        <el-checkbox v-model="forceRebind">强制重绑（换机时使用）</el-checkbox>
      </div>

      <el-alert v-if="activateResult" :title="activateResult" :type="activateOk ? 'success' : 'error'" show-icon :closable="true" style="margin-top:12px" />
    </el-card>

    <!-- 租户激活码签发（仅主区域） -->
    <el-card v-if="auth.isManager" shadow="never" class="page-card">
      <template #header>
        <span class="card-title">租户激活码签发</span>
      </template>
      <p class="hint">为子区域（租户）生成区域激活码（AGRR- 开头，仅对该区域有效）。区域主账号在上方"区域授权状态"页输入激活码完成激活；未激活区域的成员将无法登录（区域主账号除外）。</p>
      <el-form label-width="120px" style="max-width:520px">
        <el-form-item label="目标区域" required>
          <el-select v-model="issueRegionId" placeholder="选择子区域" style="width:100%" filterable>
            <el-option v-for="r in regions" :key="r.regionId" :label="`${r.name}（${r.regionId}）`" :value="r.regionId" />
          </el-select>
        </el-form-item>
        <el-form-item label="时长（月）" required>
          <el-input-number v-model="issueMonths" :min="1" :max="600" />
        </el-form-item>
        <el-form-item label="设备上限" required>
          <el-input-number v-model="issueDevices" :min="1" :max="65535" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="issuing" @click="issueTenantCode">生成激活码</el-button>
        </el-form-item>
      </el-form>
      <el-input v-if="issuedTenantCode" :model-value="issuedTenantCode" readonly type="textarea" :rows="3" />
      <p v-if="issuedTenantCode" style="color:#909399;font-size:12px">
        请把激活码交给该区域主账号；激活码仅对所选区域有效，激活即开始计时（到期可再次签发续期叠加）。
      </p>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { licenseApi, regionApi } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()

// ── 租户模式：区域状态与激活 ──
const regionLoading = ref(false)
const regionMe = ref<any>({})
const regionCode = ref('')
const regionActivating = ref(false)
const regionResult = ref('')
const regionOk = ref(false)

async function loadRegionMe() {
  regionLoading.value = true
  try {
    regionMe.value = await regionApi.me()
  } catch { /* 拦截器已处理 */ }
  finally { regionLoading.value = false }
}

async function onRegionActivate() {
  if (!regionCode.value.trim()) {
    ElMessage.warning('请输入激活码')
    return
  }
  regionActivating.value = true
  regionResult.value = ''
  try {
    const r = await regionApi.activateRegion(regionCode.value.trim())
    regionOk.value = true
    regionResult.value = `激活成功：设备上限 ${r.maxDevices}，到期 ${r.expireAt ? new Date(r.expireAt).toLocaleString() : '—'}`
    await loadRegionMe()
  } catch (err: any) {
    regionOk.value = false
    regionResult.value = err.response?.data?.error || '激活失败'
  } finally {
    regionActivating.value = false
  }
}

// ── 管理员模式：租户激活码签发 ──
const regions = ref<any[]>([])
const issueRegionId = ref('')
const issueMonths = ref(12)
const issueDevices = ref(5)
const issuing = ref(false)
const issuedTenantCode = ref('')

async function loadRegions() {
  try { regions.value = await regionApi.list() } catch { /* 无权限时静默 */ }
}

async function issueTenantCode() {
  if (!issueRegionId.value) { ElMessage.warning('请选择子区域'); return }
  issuing.value = true
  try {
    const r = await regionApi.issueCode(issueRegionId.value, issueMonths.value, issueDevices.value)
    issuedTenantCode.value = r.activationCode
    ElMessage.success('激活码已生成，请复制交给该区域主账号')
  } catch { /* 拦截器已处理 */ }
  finally { issuing.value = false }
}

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

onMounted(async () => {
  if (auth.isManager) {
    await loadState()
    await loadRegions()
  } else {
    await loadRegionMe()
  }
})
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
