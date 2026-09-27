<template>
  <div class="dashboard">
    <el-row :gutter="16">
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">班级总数</div>
          <div class="stat-value">{{ stats.classCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">学生总数</div>
          <div class="stat-value">{{ stats.studentCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">设备总数</div>
          <div class="stat-value">{{ stats.deviceCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card">
          <div class="stat-label">在线设备</div>
          <div class="stat-value online">{{ stats.onlineDevices }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="16" style="margin-top:16px">
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card mini">
          <div class="stat-label">今日打卡</div>
          <div class="stat-value accent">{{ stats.todayCheckins }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card mini">
          <div class="stat-label">用户数</div>
          <div class="stat-value">{{ stats.userCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card mini">
          <div class="stat-label">通知公告</div>
          <div class="stat-value">{{ stats.noticeCount }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="hover" class="stat-card mini">
          <div class="stat-label">资源文件</div>
          <div class="stat-value">{{ stats.resourceCount }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="16" style="margin-top:16px">
      <el-col :span="12">
        <el-card shadow="never" class="page-card">
          <template #header>
            <div class="card-header">
              <span>最近打卡</span>
              <el-button size="small" @click="loadData">刷新</el-button>
            </div>
          </template>
          <el-table :data="recentCheckins" size="small" stripe empty-text="暂无打卡记录">
            <el-table-column prop="studentId" label="学生" width="120" />
            <el-table-column prop="taskId" label="任务" width="120" />
            <el-table-column prop="checkedAt" label="时间">
              <template #default="{ row }">{{ formatTime(row.checkedAt) }}</template>
            </el-table-column>
            <el-table-column prop="source" label="来源" width="80" />
          </el-table>
        </el-card>
      </el-col>
      <el-col :span="12">
        <el-card shadow="never" class="page-card">
          <template #header><span>设备状态</span></template>
          <el-table :data="devices" size="small" stripe empty-text="暂无设备">
            <el-table-column prop="deviceName" label="设备名" width="140" />
            <el-table-column label="状态" width="80">
              <template #default="{ row }">
                <el-tag :type="row.isOnline ? 'success' : 'info'" size="small">
                  {{ row.isOnline ? '在线' : '离线' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="lastSeen" label="最后在线">
              <template #default="{ row }">{{ formatTime(row.lastSeen) }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { dashboardApi } from '@/api/client'

const loading = ref(false)
const devices = ref<any[]>([])
const recentCheckins = ref<any[]>([])
const stats = reactive({
  classCount: 0, studentCount: 0, deviceCount: 0, onlineDevices: 0,
  todayCheckins: 0, userCount: 0, noticeCount: 0, resourceCount: 0,
})

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const data = await dashboardApi.overview()
    Object.assign(stats, data.stats)
    recentCheckins.value = data.recentCheckins || []
    devices.value = data.devices || []
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
.stat-card { border-radius: 10px; }
.stat-card :deep(.el-card__body) { padding: 20px; }
.stat-label { font-size: 13px; color: #909399; margin-bottom: 8px; }
.stat-value { font-size: 32px; font-weight: 700; color: #4285f4; }
.stat-value.online { color: #34a853; }
.stat-value.accent { color: #ea4335; }
.stat-card.mini :deep(.el-card__body) { padding: 14px 20px; }
.stat-card.mini .stat-value { font-size: 24px; }
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; font-weight: 600; }
</style>
