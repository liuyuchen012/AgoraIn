<template>
  <div class="dashboard">
    <!-- 统计卡片 -->
    <el-row :gutter="16">
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">班级总数</div>
          <div class="stat-value">{{ stats.classes }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">学生总数</div>
          <div class="stat-value">{{ stats.students }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">设备总数</div>
          <div class="stat-value">{{ stats.devices }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">在线设备</div>
          <div class="stat-value online">{{ stats.online }}</div>
        </el-card>
      </el-col>
    </el-row>

    <!-- 设备列表 -->
    <el-card class="table-card" shadow="never">
      <template #header>
        <div class="card-header">
          <span>已注册设备</span>
          <el-button size="small" @click="loadData">刷新</el-button>
        </div>
      </template>
      <el-table :data="devices" v-loading="loading" empty-text="暂无设备">
        <el-table-column prop="deviceName" label="设备名称" min-width="160" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.isOnline ? 'success' : 'info'" size="small">
              {{ row.isOnline ? '在线' : '离线' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="lastSeen" label="最后在线" min-width="180">
          <template #default="{ row }">{{ formatTime(row.lastSeen) }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { classApi, studentApi, deviceApi } from '@/api/client'

const loading = ref(false)
const devices = ref<any[]>([])
const stats = reactive({ classes: 0, students: 0, devices: 0, online: 0 })

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const [classes, students, devs] = await Promise.all([
      classApi.list(),
      studentApi.list(),
      deviceApi.list(),
    ])
    const classList = Array.isArray(classes) ? classes : []
    const studentList = Array.isArray(students) ? students : []
    const deviceList = Array.isArray(devs) ? devs : []

    devices.value = deviceList
    stats.classes = classList.length
    stats.students = studentList.length
    stats.devices = deviceList.length
    stats.online = deviceList.filter((d: any) => d.isOnline).length
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
.stat-card {
  border-radius: 10px;
}

.stat-label {
  font-size: 13px;
  color: #909399;
  margin-bottom: 8px;
}

.stat-value {
  font-size: 32px;
  font-weight: 700;
  color: #4285f4;
}

.stat-value.online {
  color: #34a853;
}

.table-card {
  margin-top: 16px;
  border-radius: 10px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
}
</style>
