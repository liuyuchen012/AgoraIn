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
        <el-table-column prop="title" label="试卷标题" min-width="180" />
        <el-table-column prop="subject" label="科目" width="90" />
        <el-table-column label="题目数" width="70">
          <template #default="{ row }">{{ row.questionCount || 0 }}</template>
        </el-table-column>
        <el-table-column label="总分" width="60">
          <template #default="{ row }">{{ row.totalScore || 0 }}</template>
        </el-table-column>
        <el-table-column label="提交" width="60">
          <template #default="{ row }">
            <el-tag v-if="row.submissionCount" size="small" type="info">{{ row.submissionCount }}</el-tag>
            <span v-else>0</span>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="160">
          <template #default="{ row }">{{ formatTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="320" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="editPaper(row)">编辑</el-button>
            <el-button link type="success" size="small" @click="previewSheet(row)">预览答题卡</el-button>
            <el-button link type="warning" size="small" @click="downloadSheet(row)">下载</el-button>
            <el-button link type="info" size="small" @click="viewSubmissions(row)">扫卡记录</el-button>
            <el-popconfirm title="确定删除？" @confirm="deletePaper(row.id)">
              <template #reference><el-button link type="danger" size="small">删除</el-button></template>
            </el-popconfirm>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 编辑试卷（含题目 + 答题卡配置 + 预览） -->
    <template v-if="editingPaper">
      <el-card shadow="never" class="page-card">
        <template #header>
          <div class="card-header">
            <div style="display:flex;align-items:center;gap:12px">
              <el-button link @click="backToList">← 返回</el-button>
              <span class="card-title">{{ editingPaper.title }}</span>
            </div>
            <div style="display:flex;gap:8px">
              <el-button @click="previewSheet(editingPaper)">预览答题卡</el-button>
              <el-button type="primary" @click="saveAll">保存全部</el-button>
            </div>
          </div>
        </template>

        <!-- 标签页：基本信息 / 题目编辑 / 答题卡配置 -->
        <el-tabs v-model="activeTab">
          <!-- 基本信息 -->
          <el-tab-pane label="基本信息" name="info">
            <el-form :model="editingPaper" label-width="80px" style="max-width:500px">
              <el-form-item label="标题"><el-input v-model="editingPaper.title" /></el-form-item>
              <el-form-item label="科目"><el-input v-model="editingPaper.subject" placeholder="如：数学" /></el-form-item>
              <el-form-item label="班级">
                <el-select v-model="editingPaper.classId" clearable placeholder="全部班级" style="width:100%">
                  <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id" />
                </el-select>
              </el-form-item>
            </el-form>
          </el-tab-pane>

          <!-- 题目编辑 -->
          <el-tab-pane label="题目编辑" name="questions">
            <div class="section-header">
              <span style="font-weight:600">共 {{questions.length}} 题，总分 {{totalScore}}</span>
              <div>
                <el-button size="small" @click="addQ(0)">+单选</el-button>
                <el-button size="small" @click="addQ(2)">+判断</el-button>
                <el-button size="small" @click="addQ(3)">+填空</el-button>
                <el-button size="small" @click="addQ(4)">+简答</el-button>
                <el-button size="small" @click="addQ(5)">+作文</el-button>
              </div>
            </div>
            <div v-for="(q,idx) in questions" :key="idx" class="q-card">
              <div class="q-head">
                <span class="q-num">{{idx+1}}</span>
                <el-select v-model="q.type" size="small" style="width:90px">
                  <el-option label="单选" :value="0" /><el-option label="多选" :value="1" />
                  <el-option label="判断" :value="2" /><el-option label="填空" :value="3" />
                  <el-option label="简答" :value="4" /><el-option label="作文" :value="5" />
                </el-select>
                <el-input-number v-model="q.score" size="small" :min="0.5" :max="100" :step="0.5" style="width:80px" />
                <span style="font-size:11px;color:#909399">分</span>
                <el-button link type="danger" size="small" style="margin-left:auto" @click="questions.splice(idx,1)">删除</el-button>
              </div>
              <el-input v-model="q.content" size="small" :type="q.type>=3?'textarea':'text'" :rows="q.type>=3?2:1"
                        placeholder="题目内容/题干" style="margin:6px 0" />
              <template v-if="q.type<=1">
                <div v-for="(opt,oi) in getOpts(q)" :key="oi" style="display:flex;align-items:center;gap:4px;margin:2px 0">
                  <span style="font-weight:600;font-size:12px;min-width:16px">{{String.fromCharCode(65+oi)}}</span>
                  <el-input :model-value="opt.text" size="small" placeholder="选项" style="flex:1"
                            @input="(v:string) => syncOpt(q, oi, v)" />
                </div>
                <el-button size="small" text @click="addOpt(q)">+ 选项</el-button>
              </template>
              <el-input v-model="q.standardAnswer" size="small" placeholder="标准答案" style="margin-top:4px" />
              <el-input v-if="q.type>=4" v-model="q.rubric" size="small" type="textarea" :rows="1"
                        placeholder="评分要点（AI批改依据）" style="margin-top:4px" />
            </div>
          </el-tab-pane>

          <!-- 答题卡配置 -->
          <el-tab-pane label="答题卡配置" name="sheet">
            <el-form label-width="100px" style="max-width:600px">
              <el-divider content-position="left">试卷信息</el-divider>
              <el-form-item label="试卷标题"><el-input v-model="sheetConfig.paperTitle" :placeholder="editingPaper.title" /></el-form-item>
              <el-form-item label="科目"><el-input v-model="sheetConfig.subject" :placeholder="editingPaper.subject" /></el-form-item>

              <el-divider content-position="left">考号区域</el-divider>
              <el-form-item label="考号类型">
                <el-radio-group v-model="sheetConfig.idAreaType">
                  <el-radio value="bubble">考号填涂区</el-radio>
                  <el-radio value="barcode">条形码</el-radio>
                  <el-radio value="handwrite">手写考号</el-radio>
                </el-radio-group>
              </el-form-item>

              <el-divider content-position="left">辅助选项</el-divider>
              <el-form-item label="密封线"><el-switch v-model="sheetConfig.hasSealLine" /></el-form-item>
              <el-form-item label="注意事项"><el-switch v-model="sheetConfig.hasNotes" /></el-form-item>
              <el-form-item label="AB卷"><el-switch v-model="sheetConfig.hasAB" /></el-form-item>
              <el-form-item label="红色答题卡"><el-switch v-model="sheetConfig.isRed" /></el-form-item>
              <el-form-item label="客观题竖排"><el-switch v-model="sheetConfig.verticalObj" /></el-form-item>
              <el-form-item label="分区答题卡"><el-switch v-model="sheetConfig.partitioned" /></el-form-item>

              <el-divider content-position="left">布局</el-divider>
              <el-form-item label="纸张">
                <el-select v-model="sheetConfig.pageSize">
                  <el-option label="A4" value="A4" /><el-option label="A3/B4/8K" value="A3" />
                </el-select>
              </el-form-item>
              <el-form-item label="栏数">
                <el-radio-group v-model="sheetConfig.columns">
                  <el-radio :value="1">单栏</el-radio>
                  <el-radio :value="2">双栏</el-radio>
                </el-radio-group>
              </el-form-item>
            </el-form>

            <div style="margin-top:16px;display:flex;gap:12px">
              <el-button type="primary" @click="previewSheet(editingPaper)">预览答题卡</el-button>
              <el-button type="success" @click="downloadSheet(editingPaper)">下载打印</el-button>
            </div>
          </el-tab-pane>
        </el-tabs>
      </el-card>
    </template>

    <!-- 答题卡预览对话框 -->
    <el-dialog v-model="previewVisible" title="答题卡预览" width="85%" top="3vh" destroy-on-close>
      <iframe v-if="previewUrl" :src="previewUrl" style="width:100%;height:75vh;border:none;background:#f5f5f5" />
      <template #footer>
        <el-button @click="printSheet">🖨 打印</el-button>
        <el-button type="primary" @click="downloadSheet(currentPreviewPaper)">📥 下载 PDF</el-button>
        <el-button @click="previewVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 扫卡记录对话框 -->
    <el-dialog v-model="submissionsVisible" title="扫卡记录" width="70%" destroy-on-close>
      <el-table :data="submissions" v-loading="submissionsLoading" stripe>
        <el-table-column prop="studentRef" label="学生" width="120" />
        <el-table-column prop="status" label="状态" width="100">
          <template #default="{row}">
            <el-tag :type="row.status===3?'success':row.status===1?'warning':'info'" size="small">
              {{['未批','AI已批','待人工','已确认'][row.status]||'未知'}}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="totalScore" label="得分" width="80" />
        <el-table-column prop="submittedAt" label="提交时间" width="170">
          <template #default="{row}">{{formatTime(row.submittedAt)}}</template>
        </el-table-column>
        <el-table-column label="操作" width="160">
          <template #default="{row}">
            <el-button v-if="row.status<3" link type="primary" size="small" @click="aiGrade(row)">AI批改</el-button>
            <el-button v-if="row.status>=1" link type="success" size="small" @click="confirmGrade(row)">确认</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { examApi, classApi, type ClassRow } from '@/api/client'

const loading = ref(false)
const papers = ref<any[]>([])
const classes = ref<ClassRow[]>([])
const activeTab = ref('info')

// 编辑状态
const editingPaper = ref<any>(null)
const questions = ref<any[]>([])
const totalScore = computed(() => questions.value.reduce((s,q)=>s+(q.score||0),0))

// 答题卡配置
const sheetConfig = ref({
  paperTitle: '', subject: '', idAreaType: 'bubble',
  hasSealLine: false, hasNotes: true, hasAB: false, isRed: false,
  verticalObj: false, partitioned: false, pageSize: 'A4', columns: 2,
})

// 预览
const previewVisible = ref(false)
const previewUrl = ref('')
const currentPreviewPaper = ref<any>(null)

// 扫卡记录
const submissionsVisible = ref(false)
const submissionsLoading = ref(false)
const submissions = ref<any[]>([])

async function loadPapers() {
  loading.value = true
  try { papers.value = await examApi.list() } finally { loading.value = false }
}

function showCreate() {
  editingPaper.value = { title: '', subject: '', classId: '' }
  questions.value = []
  sheetConfig.value = { paperTitle:'', subject:'', idAreaType:'bubble', hasSealLine:false, hasNotes:true, hasAB:false, isRed:false, verticalObj:false, partitioned:false, pageSize:'A4', columns:2 }
  activeTab.value = 'info'
}

function editPaper(row: any) {
  editingPaper.value = { ...row }
  sheetConfig.value.paperTitle = row.title
  sheetConfig.value.subject = row.subject || ''
  activeTab.value = 'info'
  loadQuestions(row.id)
}

async function loadQuestions(paperId: string) {
  try {
    const data = await examApi.getQuestions(paperId)
    questions.value = (data||[]).map((q:any)=>({
      id:q.id, type:q.type, score:q.score||1, content:q.content||'',
      standardAnswer:q.standardAnswer||'', optionsJson:q.optionsJson||'[]', rubric:q.rubric||'',
    }))
  } catch { questions.value = [] }
}

function backToList() { editingPaper.value = null; questions.value = []; loadPapers() }

function addQ(type: number) {
  questions.value.push({ type, score:type===5?40:type>=3?5:1, content:'', standardAnswer:'', optionsJson:'[]', rubric:'' })
}

function getOpts(q: any): {key:string;text:string}[] {
  try { const a=JSON.parse(q.optionsJson||'[]'); return Array.isArray(a)?a:[] } catch {} return [{key:'A',text:''},{key:'B',text:''}]
}
function syncOpt(q: any, idx: number, val: string) {
  const o=getOpts(q); if(o[idx]) o[idx].text=val; q.optionsJson=JSON.stringify(o)
}
function addOpt(q: any) {
  const o=getOpts(q); o.push({key:String.fromCharCode(65+o.length),text:''}); q.optionsJson=JSON.stringify(o)
}

async function saveAll() {
  if (!editingPaper.value?.title) { ElMessage.warning('请填写标题'); return }
  try {
    if (editingPaper.value.id) {
      await examApi.update(editingPaper.value.id, editingPaper.value)
    } else {
      const r = await examApi.create(editingPaper.value)
      editingPaper.value.id = r.id
    }
    for (let i=0; i<questions.value.length; i++) {
      const q = questions.value[i]
      const payload = { paperId:editingPaper.value.id, index:i, type:q.type, score:q.score, content:q.content,
        standardAnswer:q.standardAnswer, optionsJson:q.optionsJson, rubric:q.rubric }
      if (q.id) await examApi.updateQuestion(editingPaper.value.id, q.id, payload)
      else { const r = await examApi.createQuestion(editingPaper.value.id, payload); q.id = r.id }
    }
    ElMessage.success('已保存')
  } catch(e:any) { ElMessage.error(e.response?.data?.error||'保存失败') }
}

async function deletePaper(id: string) {
  try { await examApi.remove(id); ElMessage.success('已删除'); loadPapers() } catch {}
}

function previewSheet(row: any) {
  currentPreviewPaper.value = row
  previewUrl.value = examApi.sheetUrl(row.id)
  previewVisible.value = true
}

function downloadSheet(row: any) {
  window.open(examApi.sheetUrl(row.id), '_blank')
  ElMessage.info('已在新窗口打开答题卡，使用 Ctrl+P 打印或另存为 PDF')
}

function printSheet() {
  const iframe = document.querySelector('.el-dialog iframe') as HTMLIFrameElement
  iframe?.contentWindow?.print()
}

async function viewSubmissions(row: any) {
  submissionsVisible.value = true
  submissionsLoading.value = true
  try { submissions.value = await examApi.getSubmissions(row.id) } catch { submissions.value = [] }
  finally { submissionsLoading.value = false }
}

async function aiGrade(_row: any) {
  ElMessage.info('AI 批改功能需要配置 DeepSeek API Key')
}

async function confirmGrade(row: any) {
  try { await examApi.confirm(row.id); ElMessage.success('已确认'); row.status = 3 } catch {}
}

function formatTime(t: string) { return t ? String(t).replace('T',' ').substring(0,19) : '—' }

onMounted(async () => { loadPapers(); try { classes.value = await classApi.list() } catch {} })
</script>

<style scoped>
.exams { display: flex; flex-direction: column; gap: 16px; }
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
.q-card { border: 1px solid #e4e7ed; border-radius: 8px; padding: 10px 14px; margin-bottom: 10px; background: #fafafa; }
.q-head { display: flex; align-items: center; gap: 8px; }
.q-num { font-weight: 700; font-size: 13px; color: #409eff; min-width: 20px; }
</style>
