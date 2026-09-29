<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">区域管理（主区域）</span>
        <div style="display:flex;gap:8px">
          <el-button size="small" @click="load">刷新</el-button>
          <el-button type="primary" size="small" @click="showCreate">新建区域</el-button>
        </div>
      </div>
    </template>

    <el-alert type="info" :closable="false" style="margin-bottom:14px"
              title="子区域（租户）数据与主区域完全隔离；子区域需使用主区域颁发的激活码激活后，其成员才能登录使用功能。" />

    <el-table :data="regions" v-loading="loading" stripe border>
      <el-table-column prop="regionId" label="区域代号" width="120" />
      <el-table-column prop="name" label="区域名称" min-width="160" />
      <el-table-column prop="ownerUsername" label="主账号" width="120" />
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <el-tag size="small" :type="row.isActive ? 'success' : row.activated ? 'danger' : 'warning'">
            {{ row.isActive ? '使用中' : row.activated ? '已到期' : '未激活' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="到期时间" width="170">
        <template #default="{ row }">
          {{ row.expireAt ? formatTime(row.expireAt) : '—' }}
          <el-tag v-if="row.isActive && row.remainingDays <= 30" size="small" type="warning"
                  style="margin-left:4px">剩 {{ row.remainingDays }} 天</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="maxDevices" label="设备上限" width="90" />
      <el-table-column label="操作" width="240" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="showIssue(row)">颁发激活码</el-button>
          <el-button link size="small" @click="showDevicePwd(row)">设备密码</el-button>
          <el-popconfirm title="删除该区域？区域内数据将不可见。" @confirm="removeRegion(row)">
            <template #reference><el-button link type="danger" size="small">删除</el-button></template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <!-- 新建区域 -->
    <el-dialog v-model="createVisible" title="新建子区域" width="440px">
      <el-form label-width="90px">
        <el-form-item label="区域名称" required>
          <el-input v-model="createForm.name" placeholder="机构/学校名称（全局唯一，不可重复）" />
        </el-form-item>
        <el-form-item label="区域代号">
          <el-input v-model="createForm.regionId" placeholder="留空自动生成（登录用：用户名@区域代号）" />
        </el-form-item>
        <el-form-item label="主账号" required>
          <el-input v-model="createForm.ownerUsername" placeholder="区域主账号用户名（不含 @）" />
        </el-form-item>
        <el-form-item label="初始密码" required>
          <el-input v-model="createForm.ownerPassword" type="password" show-password placeholder="至少 6 位" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" @click="createRegion">创建</el-button>
      </template>
    </el-dialog>

    <!-- 颁发激活码 -->
    <el-dialog v-model="issueVisible" :title="`颁发激活码 — ${issueRegion?.name || ''}`" width="480px">
      <el-form label-width="90px">
        <el-form-item label="时长（月）">
          <el-input-number v-model="issueForm.months" :min="1" :max="600" />
        </el-form-item>
        <el-form-item label="设备上限">
          <el-input-number v-model="issueForm.maxDevices" :min="1" :max="65535" />
        </el-form-item>
      </el-form>
      <el-input v-if="issuedCode" :model-value="issuedCode" readonly type="textarea" :rows="3" />
      <p v-if="issuedCode" style="color:#909399;font-size:12px">
        请把激活码交给区域主账号，由其在「区域管理 → 激活区域」处输入激活。激活码仅对该区域有效。
      </p>
      <template #footer>
        <el-button @click="issueVisible = false">关闭</el-button>
        <el-button type="primary" :disabled="!issueFormReady" @click="issue">生成激活码</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { regionApi, type RegionRow } from '@/api/client'

const loading = ref(false)
const regions = ref<RegionRow[]>([])

const createVisible = ref(false)
const createForm = ref({ name: '', regionId: '', ownerUsername: '', ownerPassword: '' })

const issueVisible = ref(false)
const issueRegion = ref<RegionRow | null>(null)
const issueForm = ref({ months: 12, maxDevices: 5 })
const issuedCode = ref('')
const issueFormReady = computed(() => issueForm.value.months >= 1 && issueForm.value.maxDevices >= 1)

onMounted(load)

async function load() {
  loading.value = true
  try { regions.value = await regionApi.list() } finally { loading.value = false }
}

function showCreate() {
  createForm.value = { name: '', regionId: '', ownerUsername: '', ownerPassword: '' }
  createVisible.value = true
}

async function createRegion() {
  if (!createForm.value.name || !createForm.value.ownerUsername || !createForm.value.ownerPassword) {
    ElMessage.warning('请填写完整信息'); return
  }
  try {
    await regionApi.create({
      name: createForm.value.name,
      regionId: createForm.value.regionId || undefined,
      ownerUsername: createForm.value.ownerUsername,
      ownerPassword: createForm.value.ownerPassword,
    })
    ElMessage.success('区域已创建（未激活）')
    createVisible.value = false
    await load()
  } catch { /* 拦截器已提示 */ }
}

function showIssue(row: RegionRow) {
  issueRegion.value = row
  issueForm.value = { months: 12, maxDevices: 5 }
  issuedCode.value = ''
  issueVisible.value = true
}

async function issue() {
  if (!issueRegion.value) return
  const res = await regionApi.issueCode(issueRegion.value.regionId, issueForm.value.months, issueForm.value.maxDevices)
  issuedCode.value = res.activationCode
  ElMessage.success('激活码已生成')
}

function showDevicePwd(row: RegionRow) {
  ElMessageBox.alert(row.devicePassword || '—', `区域「${row.name}」设备协议密码`, { confirmButtonText: '复制并关闭' })
    .then(() => { navigator.clipboard?.writeText(row.devicePassword || '') })
    .catch(() => {})
}

async function removeRegion(row: RegionRow) {
  await regionApi.remove(row.regionId)
  ElMessage.success('已删除')
  await load()
}

function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(0, 16).replace(' ', ' ') : '—' }
</script>

<style scoped>
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
</style>
