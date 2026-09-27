<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span>课时账户</span>
        <el-button size="small" @click="loadData">刷新</el-button>
      </div>
    </template>

    <el-table :data="accounts" v-loading="loading" empty-text="暂无课时数据">
      <el-table-column prop="studentId" label="学生 ID" min-width="160" show-overflow-tooltip />
      <el-table-column prop="totalHours" label="总课时" width="120" />
      <el-table-column prop="usedHours" label="已用课时" width="120" />
      <el-table-column label="剩余课时" width="120">
        <template #default="{ row }">
          <el-tag :type="remaining(row) > 0 ? 'success' : 'danger'" size="small">
            {{ remaining(row) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="remark" label="备注" min-width="140" />
    </el-table>
  </el-card>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { classhourApi } from '@/api/client'

const loading = ref(false)
const accounts = ref<any[]>([])

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res = await classhourApi.accounts()
    accounts.value = Array.isArray(res) ? res : []
  } finally {
    loading.value = false
  }
}

function remaining(row: any) {
  return Math.round(((row.totalHours || 0) - (row.usedHours || 0)) * 10) / 10
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
