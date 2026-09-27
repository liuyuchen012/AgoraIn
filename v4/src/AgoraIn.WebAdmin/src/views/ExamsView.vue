<template>
  <div class="exams">
    <!-- 试卷列表 -->
    <el-card v-if="!editingPaper" shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span class="card-title">试卷管理</span>
          <el-button type="primary" @click="showCreate">新建试卷</el-button>
        </div>
      </template>
      <el-table :data="papers" v-loading="loading" stripe border>
        <el-table-column prop="title" label="试卷标题" min-width="200" />
        <el-table-column prop="subject" label="科目" width="100" />
        <el-table-column label="题目数" width="80">
          <template #default="{ row }">{{ row.questionCount || 0 }}</template>
        </el-table-column>
        <el-table-column label="总分" width="80">
          <template #default="{ row }">{{ row.totalScore || 0 }}</template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="170">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="280" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="editPaper(row)">编辑题目</el-button>
            <el-button link type="success" size="small" @click="previewSheet(row)">预览答题卡</el-button>
            <el-button link type="warning" size="small" @click="batchGenerate(row)">批量生成</el-button>
            <el-popconfirm title="确定删除该试卷？" @confirm="deletePaper(row.id)">
              <template #reference>
                <el-button link type="danger" size="small">删除</el-button>
              </template>
            </el-popconfirm>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 编辑试卷 + 题目 -->
    <template v-if="editingPaper">
      <el-card shadow="never" class="page-card">
        <template #header>
          <div class="card-header">
            <div style="display:flex;align-items:center;gap:12px">
              <el-button link @click="backToList">← 返回</el-button>
              <span class="card-title">{{ editingPaper.title }}</span>
              <el-tag size="small">{{ editingPaper.subject || '未分类' }}</el-tag>
            </div>
            <el-button type="primary" @click="savePaper">保存试卷</el-button>
          </div>
        </template>

        <!-- 试卷基本信息 -->
        <el-form :model="editingPaper" label-width="80px" style="max-width:600px;margin-bottom:20px">
          <el-form-item label="标题">
            <el-input v-model="editingPaper.title" />
          </el-form-item>
          <el-form-item label="科目">
            <el-input v-model="editingPaper.subject" placeholder="如：数学" />
          </el-form-item>
          <el-form-item label="班级">
            <el-select v-model="editingPaper.classId" clearable placeholder="全部班级" style="width:100%">
              <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
            </el-select>
          </el-form-item>
        </el-form>

        <!-- 题目列表 -->
        <div class="question-section">
          <div class="section-header">
            <span style="font-weight:600">题目列表（共 {{ questions.length }} 题，总分 {{ totalScore }}）</span>
            <el-button type="primary" size="small" @click="addQuestion">+ 添加题目</el-button>
          </div>

          <div v-for="(q, idx) in questions" :key="q.id || idx" class="question-card">
            <div class="question-header">
              <span class="question-num">第 {{ idx + 1 }} 题</span>
              <el-select v-model="q.type" size="small" style="width:120px">
                <el-option label="单选" :value="0" />
                <el-option label="多选" :value="1" />
                <el-option label="判断" :value="2" />
                <el-option label="填空" :value="3" />
                <el-option label="简答" :value="4" />
                <el-option label="作文" :value="5" />
              </el-select>
              <el-input-number v-model="q.score" size="small" :min="0.5" :max="100" :step="0.5"
                               style="width:100px;margin-left:8px" />
              <span style="font-size:12px;color:#909399;margin-left:4px">分</span>
              <el-button link type="danger" size="small" style="margin-left:auto" @click="removeQuestion(idx)">删除</el-button>
            </div>

            <el-input v-model="q.content" type="textarea" :rows="2" placeholder="题目内容"
                      style="margin:8px 0" />

            <!-- 选择题选项 -->
            <template v-if="q.type === 0 || q.type === 1">
              <div class="options-area">
                <div v-for="(opt, oi) in getOptions(q)" :key="oi" class="option-row">
                  <span class="option-key">{{ String.fromCharCode(65 + oi) }}</span>
                  <el-input v-model="opt.text" size="small" placeholder="选项内容" style="flex:1" />
                  <el-button link type="danger" size="small" @click="removeOption(q, oi)"
                             v-if="getOptions(q).length > 2">✕</el-button>
                </div>
                <el-button size="small" @click="addOption(q)" style="margin-top:4px">+ 添加选项</el-button>
              </div>
            </template>

            <!-- 标准答案 -->
            <div style="margin-top:8px">
              <el-input v-model="q.standardAnswer" size="small" placeholder="标准答案（选择题填 A/B/C/D，填空题填文本，判断题填 对/错）"
                        style="max-width:400px" />
            </div>

            <!-- 简答/作文评分要点 -->
            <div v-if="q.type === 4 || q.type === 5" style="margin-top:8px">
              <el-input v-model="q.rubric" type="textarea" :rows="2"
                        placeholder="评分要点/rubric（分步给分点，AI 批改依据）" />
            </div>
          </div>
        </div>
      </el-card>
    </template>

    <!-- 答题卡预览对话框 -->
    <el-dialog v-model="previewVisible" title="答题卡预览" width="80%" top="5vh" destroy-on-close>
      <iframe v-if="previewUrl" :src="previewUrl" style="width:100%;height:70vh;border:none" />
      <template #footer>
        <el-button @click="printSheet">打印答题卡</el-button>
        <el-button type="primary" @click="previewVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 批量生成对话框 -->
    <el-dialog v-model="batchVisible" title="批量生成答题卡" width="440px" destroy-on-close>
      <p style="margin-bottom:12px;color:#606266">选择班级后将为该班每位学生生成含姓名+学号的专属答题卡。</p>
      <el-select v-model="batchClassId" placeholder="选择班级" style="width:100%">
        <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
      </el-select>
      <template #footer>
        <el-button @click="batchVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!batchClassId" @click="doBatchGenerate">生成并预览</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { examApi, classApi, type ClassRow } from '@/api/client'

interface QuestionItem {
  id?: string
  type: number
  score: number
  content: string
  standardAnswer: string
  optionsJson: string
  rubric: string
}

const loading = ref(false)
const papers = ref<any[]>([])
const classes = ref<ClassRow[]>([])

// 编辑状态
const editingPaper = ref<any>(null)
const questions = ref<QuestionItem[]>([])
const totalScore = computed(() => questions.value.reduce((s, q) => s + (q.score || 0), 0))

// 答题卡预览
const previewVisible = ref(false)
const previewUrl = ref('')
const previewPaperId = ref('')

// 批量生成
const batchVisible = ref(false)
const batchClassId = ref('')
const batchPaperId = ref('')

async function loadPapers() {
  loading.value = true
  try { papers.value = await examApi.list() } finally { loading.value = false }
}

function showCreate() {
  editingPaper.value = { title: '', subject: '', classId: '' }
  questions.value = []
}

function editPaper(row: any) {
  editingPaper.value = { ...row }
  // 加载题目
  loadQuestions(row.id)
}

async function loadQuestions(paperId: string) {
  try {
    const data = await examApi.getQuestions(paperId)
    questions.value = (data || []).map((q: any) => ({
      id: q.id,
      type: q.type,
      score: q.score || 1,
      content: q.content || q.stem || '',
      standardAnswer: q.standardAnswer || '',
      optionsJson: q.optionsJson || '[]',
      rubric: q.rubric || '',
    }))
  } catch {
    questions.value = []
  }
}

function backToList() {
  editingPaper.value = null
  questions.value = []
  loadPapers()
}

function addQuestion() {
  questions.value.push({
    type: 0, score: 1, content: '', standardAnswer: '', optionsJson: '[]', rubric: '',
  })
}

function removeQuestion(idx: number) {
  questions.value.splice(idx, 1)
}

function getOptions(q: QuestionItem): { key: string; text: string }[] {
  try {
    const arr = JSON.parse(q.optionsJson || '[]')
    if (Array.isArray(arr)) return arr
  } catch {}
  return [{ key: 'A', text: '' }, { key: 'B', text: '' }]
}

function addOption(q: QuestionItem) {
  const opts = getOptions(q)
  const nextKey = String.fromCharCode(65 + opts.length)
  opts.push({ key: nextKey, text: '' })
  q.optionsJson = JSON.stringify(opts)
}

function removeOption(q: QuestionItem, idx: number) {
  const opts = getOptions(q)
  opts.splice(idx, 1)
  q.optionsJson = JSON.stringify(opts)
}

async function savePaper() {
  if (!editingPaper.value?.title) { ElMessage.warning('请填写试卷标题'); return }
  try {
    if (editingPaper.value.id) {
      await examApi.update(editingPaper.value.id, editingPaper.value)
    } else {
      const result = await examApi.create(editingPaper.value)
      editingPaper.value.id = result.id
    }

    // 保存题目
    for (let i = 0; i < questions.value.length; i++) {
      const q = questions.value[i]
      const payload = {
        paperId: editingPaper.value.id,
        index: i,
        type: q.type,
        score: q.score,
        content: q.content,
        standardAnswer: q.standardAnswer,
        optionsJson: q.optionsJson,
        rubric: q.rubric,
      }
      if (q.id) {
        await examApi.updateQuestion(editingPaper.value.id, q.id, payload)
      } else {
        const result = await examApi.createQuestion(editingPaper.value.id, payload)
        q.id = result.id
      }
    }
    ElMessage.success('试卷已保存')
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error || '保存失败')
  }
}

async function deletePaper(id: string) {
  try {
    await examApi.remove(id)
    ElMessage.success('已删除')
    await loadPapers()
  } catch {}
}

function previewSheet(row: any) {
  previewPaperId.value = row.id
  previewUrl.value = `/api/v4/exams/papers/${row.id}/sheet`
  previewVisible.value = true
}

function printSheet() {
  const iframe = document.querySelector('iframe') as HTMLIFrameElement
  iframe?.contentWindow?.print()
}

function batchGenerate(row: any) {
  batchPaperId.value = row.id
  batchClassId.value = ''
  batchVisible.value = true
}

function doBatchGenerate() {
  if (!batchClassId.value || !batchPaperId.value) return
  const url = `/api/v4/exams/papers/${batchPaperId.value}/sheets/batch?classId=${batchClassId.value}`
  window.open(url, '_blank')
  batchVisible.value = false
}

function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(0, 19) : '—' }

onMounted(async () => {
  loadPapers()
  try { classes.value = await classApi.list() } catch {}
})
</script>

<style scoped>
.exams { display: flex; flex-direction: column; gap: 16px; }
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.question-section { margin-top: 16px; }
.section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
.question-card { border: 1px solid #e4e7ed; border-radius: 8px; padding: 12px 16px; margin-bottom: 12px; background: #fafafa; }
.question-header { display: flex; align-items: center; gap: 8px; }
.question-num { font-weight: 600; font-size: 13px; color: #409eff; min-width: 50px; }
.options-area { margin: 8px 0; }
.option-row { display: flex; align-items: center; gap: 8px; margin-bottom: 4px; }
.option-key { font-weight: 600; font-size: 13px; color: #606266; min-width: 20px; }
</style>
