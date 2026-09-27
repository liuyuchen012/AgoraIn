<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span>设备管理</span>
        <el-button size="small" @click="loadData">刷新</el-button>
      </div>
    </template>

    <el-table :data="devices" v-loading="loading" empty-text="暂无设备">
      <el-table-column prop="deviceName" label="设备名称" min-width="180" />
      <el-table-column prop="deviceUuid" label="设备标识" min-width="200" show-overflow-tooltip />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.isOnline ? 'success' : 'info'" size="small">
            {{ row.isOnline ? '在线' : '离线' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="最后在线" min-width="180">
        <template #default="{ row }">{{ formatTime(row.lastSeen) }}</template>
      </el-table-column>
    </el-table>
  </el-card>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { deviceApi } from '@/api/client'

const loading = ref(false)
const devices = ref<any[]>([])

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res = await deviceApi.list()
    devices.value = Array.isArray(res) ? res : []
  } finally {
    loading.value = false
  }
}

function formatTime(t: string) {
  if (!t) return '—'
  return String(t).replace('T', ' ').substring(0, 19)
}
</script>

<style scoped>
.page-card {
  border-radius: 10px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
}
</style>
