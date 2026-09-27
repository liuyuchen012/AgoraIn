<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span>班级管理</span>
        <el-button type="primary" size="small" @click="openDialog()">新增班级</el-button>
      </div>
    </template>

    <el-table :data="classes" v-loading="loading" empty-text="暂无班级">
      <el-table-column prop="name" label="班级名称" min-width="180" />
      <el-table-column prop="grade" label="年级" width="140" />
      <el-table-column prop="remark" label="备注" min-width="160" />
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="openDialog(row)">编辑</el-button>
          <el-button link type="danger" size="small" @click="onRemove(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </el-card>

  <el-dialog v-model="dialogVisible" :title="editing ? '编辑班级' : '新增班级'" width="420px">
    <el-form :model="form" label-width="80px">
      <el-form-item label="班级名称" required>
        <el-input v-model="form.name" placeholder="例如 三年级2班" />
      </el-form-item>
      <el-form-item label="年级">
        <el-input v-model="form.grade" placeholder="例如 三年级" />
      </el-form-item>
      <el-form-item label="备注">
        <el-input v-model="form.remark" type="textarea" :rows="2" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="dialogVisible = false">取消</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">保存</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { classApi, type ClassRow } from '@/api/client'

const loading = ref(false)
const saving = ref(false)
const classes = ref<ClassRow[]>([])
const dialogVisible = ref(false)
const editing = ref(false)

const form = reactive<ClassRow>({ name: '', grade: '', remark: '' })

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res = await classApi.list()
    classes.value = Array.isArray(res) ? res : []
  } finally {
    loading.value = false
  }
}

function openDialog(row?: ClassRow) {
  editing.value = !!row
  Object.assign(form, row || { id: undefined, name: '', grade: '', remark: '' })
  dialogVisible.value = true
}

async function onSave() {
  if (!form.name) {
    ElMessage.warning('请输入班级名称')
    return
  }
  saving.value = true
  try {
    if (editing.value && form.id) {
      await classApi.update(form.id, { ...form })
    } else {
      await classApi.create({ ...form })
    }
    ElMessage.success('保存成功')
    dialogVisible.value = false
    await loadData()
  } finally {
    saving.value = false
  }
}

async function onRemove(row: ClassRow) {
  await ElMessageBox.confirm(`确定删除班级「${row.name}」吗？`, '提示', { type: 'warning' })
  await classApi.remove(row.id!)
  ElMessage.success('已删除')
  await loadData()
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
