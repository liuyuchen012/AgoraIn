<template>
  <div class="resources">
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span class="card-title">资源库</span>
          <el-button type="primary" @click="showUpload">上传资源</el-button>
        </div>
      </template>
      <el-table :data="list" v-loading="loading" stripe border>
        <el-table-column prop="title" label="标题" min-width="200" />
        <el-table-column prop="kind" label="类型" width="80">
          <template #default="{ row }">
            <el-tag size="small" :type="kindType(row.kind)">{{ kindText(row.kind) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="subject" label="科目" width="100" />
        <el-table-column prop="classId" label="班级" width="100">
          <template #default="{ row }">{{ row.classId || '全部' }}</template>
        </el-table-column>
        <el-table-column prop="uploadedBy" label="上传者" width="100" />
        <el-table-column prop="createdAt" label="上传时间" width="170">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="onDownload(row)">下载</el-button>
            <el-button link type="success" size="small" @click="onPublish(row)">下发</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="uploadVisible" title="上传资源" width="440px" destroy-on-close>
      <el-form label-width="80px">
        <el-form-item label="文件">
          <el-upload ref="uploadRef" :auto-upload="false" :limit="1" :on-change="onFileChange"
                     drag action="" accept="*">
            <el-icon :size="40"><UploadFilled /></el-icon>
            <div>拖拽文件到此处或 <em>点击上传</em></div>
          </el-upload>
        </el-form-item>
        <el-form-item label="标题">
          <el-input v-model="uploadForm.title" placeholder="可选，默认用文件名" />
        </el-form-item>
        <el-form-item label="科目">
          <el-input v-model="uploadForm.subject" placeholder="可选" />
        </el-form-item>
        <el-form-item label="班级">
          <el-select v-model="uploadForm.classId" clearable placeholder="全部班级" style="width:100%">
            <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="uploadVisible = false">取消</el-button>
        <el-button type="primary" :loading="uploading" @click="onUpload">上传</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { UploadFilled } from '@element-plus/icons-vue'
import { resourceApi, classApi, type ClassRow } from '@/api/client'

const loading = ref(false)
const uploading = ref(false)
const list = ref<any[]>([])
const classes = ref<ClassRow[]>([])
const uploadVisible = ref(false)
const uploadForm = reactive({ title: '', subject: '', classId: '' })
const uploadFile = ref<File | null>(null)

async function loadList() {
  loading.value = true
  try { list.value = await resourceApi.list() } finally { loading.value = false }
}

function showUpload() {
  uploadForm.title = ''; uploadForm.subject = ''; uploadForm.classId = ''; uploadFile.value = null
  uploadVisible.value = true
}

function onFileChange(file: any) { uploadFile.value = file.raw }

async function onUpload() {
  if (!uploadFile.value) { ElMessage.warning('请选择文件'); return }
  uploading.value = true
  try {
    await resourceApi.upload(uploadFile.value, uploadForm)
    ElMessage.success('上传成功')
    uploadVisible.value = false
    await loadList()
  } finally { uploading.value = false }
}

function onDownload(row: any) {
  if (row.location) window.open(`/api/v4/resources/${row.id}/file`, '_blank')
}

async function onPublish(row: any) {
  try {
    await resourceApi.publish(row.id)
    ElMessage.success('已下发给家长端')
  } catch {}
}

function kindType(k: string) { return k === 'Image' ? 'success' : k === 'Video' ? 'warning' : '' }
function kindText(k: string) { return k === 'Image' ? '图片' : k === 'Video' ? '视频' : k === 'Link' ? '链接' : '文件' }
function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(0, 19) : '—' }

onMounted(async () => {
  loadList()
  try { classes.value = await classApi.list() } catch {}
})
</script>

<style scoped>
.resources { display: flex; flex-direction: column; gap: 16px; }
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
</style>
