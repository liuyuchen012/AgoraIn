<template>
  <div class="notices">
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span class="card-title">通知公告</span>
          <el-button type="primary" @click="showCreate">发布公告</el-button>
        </div>
      </template>
      <el-table :data="list" v-loading="loading" stripe border>
        <el-table-column prop="title" label="标题" min-width="200" />
        <el-table-column prop="classId" label="班级" width="120">
          <template #default="{ row }">{{ row.classId || '全部' }}</template>
        </el-table-column>
        <el-table-column prop="publishedBy" label="发布者" width="100" />
        <el-table-column prop="createdAt" label="发布时间" width="170">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column prop="publishAt" label="定时发布" width="170">
          <template #default="{ row }">
            <el-tag v-if="row.publishAt" type="warning" size="small">{{ formatTime(row.publishAt) }}</el-tag>
            <span v-else>立即</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" title="发布公告" width="520px" destroy-on-close>
      <el-form :model="form" label-width="80px">
        <el-form-item label="标题" required>
          <el-input v-model="form.title" placeholder="通知标题" />
        </el-form-item>
        <el-form-item label="班级">
          <el-select v-model="form.classId" clearable placeholder="全部班级" style="width:100%">
            <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="内容" required>
          <el-input v-model="form.content" type="textarea" :rows="5" placeholder="通知内容" />
        </el-form-item>
        <el-form-item label="定时发布">
          <el-date-picker v-model="form.publishAt" type="datetime" placeholder="留空则立即发布" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="onSave">发布</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { noticeApi, classApi, type ClassRow } from '@/api/client'

const loading = ref(false)
const saving = ref(false)
const list = ref<any[]>([])
const classes = ref<ClassRow[]>([])
const dialogVisible = ref(false)
const form = reactive({ title: '', content: '', classId: '', publishAt: '' })

async function loadList() {
  loading.value = true
  try { list.value = await noticeApi.list() } finally { loading.value = false }
}

function showCreate() {
  form.title = ''; form.content = ''; form.classId = ''; form.publishAt = ''
  dialogVisible.value = true
}

async function onSave() {
  if (!form.title || !form.content) { ElMessage.warning('请填写标题和内容'); return }
  saving.value = true
  try {
    await noticeApi.create(form)
    ElMessage.success('已发布')
    dialogVisible.value = false
    await loadList()
  } finally { saving.value = false }
}

function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(0, 19) : '—' }

onMounted(async () => {
  loadList()
  try { classes.value = await classApi.list() } catch {}
})
</script>

<style scoped>
.notices { display: flex; flex-direction: column; gap: 16px; }
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
</style>
