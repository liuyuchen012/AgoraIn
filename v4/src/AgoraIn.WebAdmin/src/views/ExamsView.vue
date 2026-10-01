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
        <el-table-column label="操作" width="430" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="editPaper(row)">编辑</el-button>
            <el-button link type="success" size="small" @click="previewSheet(row)">预览答题卡</el-button>
            <el-button link type="warning" size="small" @click="downloadSheet(row)">下载</el-button>
            <el-button link type="warning" size="small" @click="showBatchSheet(row)">批量</el-button>
            <el-button link type="info" size="small" @click="viewSubmissions(row)">扫卡记录</el-button>
            <el-button link type="primary" size="small" @click="showStats(row)">成绩</el-button>
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
              <el-button @click="showReuse">从题库复用</el-button>
              <el-button @click="showImportFile">文件导题</el-button>
              <el-button @click="aiGenerateAnswers" :loading="generatingAnswers">AI 生成答案</el-button>
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
              <el-form-item label="题库模板">
                <el-switch v-model="editingPaper.isTemplate" active-text="可作为题库被复用" />
              </el-form-item>
            </el-form>
          </el-tab-pane>

          <!-- 题目编辑 -->
          <el-tab-pane label="题目编辑" name="questions">
            <div class="section-header">
              <span style="font-weight:600">共 {{questions.length}} 题，总分 {{totalScore}}</span>
              <div>
                <el-button size="small" @click="addQ(0)">+单选</el-button>
                <el-button size="small" @click="addQ(1)">+多选</el-button>
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
                <span v-if="q.knowledgeTags" style="font-size:11px;color:#909399">{{ q.knowledgeTags }}</span>
                <el-button link type="danger" size="small" style="margin-left:auto" @click="questions.splice(idx,1)">删除</el-button>
              </div>
              <el-input v-if="q.type>=3" v-model="q.content" size="small" :type="q.type>=3?'textarea':'text'" :rows="q.type>=3?2:1"
                        placeholder="题目内容/题干" style="margin:6px 0" />
              <template v-if="q.type<=1">
                <div style="display:flex;align-items:center;gap:8px;margin:6px 0">
                  <span style="font-size:12px;color:#606266">选项个数</span>
                  <el-input-number :model-value="optCount(q)" size="small" :min="2" :max="8"
                                   @change="(v:number|undefined) => setOptCount(q, v ?? 4)" />
                  <span style="font-size:11px;color:#909399">答题卡仅绘制选项气泡，无需题干</span>
                </div>
              </template>
              <el-input v-model="q.standardAnswer" size="small" placeholder="标准答案" style="margin-top:4px" />
              <el-input v-if="q.type>=3" v-model="q.knowledgeTags" size="small" placeholder="知识点标签（逗号分隔）" style="margin-top:4px" />
              <el-input v-if="q.type>=4" v-model="q.rubric" size="small" type="textarea" :rows="1"
                        placeholder="评分要点（AI批改依据，简答/作文必填）" style="margin-top:4px" />
              <div v-if="q.type>=4" style="margin-top:4px;font-size:11px">
                <span style="color:#909399">批改方式：</span>
                <el-radio-group v-model="q.aiGrading" size="small">
                  <el-radio :value="null">默认（AI）</el-radio>
                  <el-radio :value="true">AI 自动批</el-radio>
                  <el-radio :value="false">教师手判</el-radio>
                </el-radio-group>
              </div>
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
              <el-button @click="showBatchSheet(editingPaper)">按班级批量生成</el-button>
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

    <!-- 文件导题对话框：docx/pdf/txt，题目与答案可一个文件或两个文件 -->
    <el-dialog v-model="importVisible" title="上传试卷文件 AI 自动出题" width="480px">
      <el-form label-width="90px">
        <el-form-item label="试卷文件">
          <input type="file" accept=".docx,.pdf,.txt" @change="(e:any) => importQFile = e.target.files?.[0] || null" />
        </el-form-item>
        <el-form-item label="答案文件">
          <input type="file" accept=".docx,.pdf,.txt" @change="(e:any) => importAFile = e.target.files?.[0] || null" />
          <div style="font-size:11px;color:#909399">可选：答案与题目在同一个文件时留空；分开存放时上传答案文件（含评分细则）</div>
        </el-form-item>
      </el-form>
      <p style="color:#909399;font-size:12px">
        支持 docx / pdf / txt。PDF（含数学公式的试卷）会自动把每页渲染成图片交给 AI 视觉识别，
        公式不会乱码；docx/txt 走文本解析。解析后逐题生成（题型/选项/答案/分值/评分要点/知识点）。
      </p>
      <div v-if="importLog" ref="logEl" class="import-log">{{ importLog }}</div>
      <template #footer>
        <el-button @click="importVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!importQFile" :loading="importing" @click="doImportFile">上传并解析</el-button>
      </template>
    </el-dialog>

    <!-- 批量答题卡对话框（按班级一人一张带考号条码） -->
    <el-dialog v-model="batchVisible" title="按班级批量生成答题卡" width="420px">
      <el-form label-width="80px">
        <el-form-item label="班级">
          <el-select v-model="batchClassId" placeholder="选择班级" style="width:100%">
            <el-option v-for="c in classes" :key="c.id" :label="c.name" :value="c.id!" />
          </el-select>
        </el-form-item>
      </el-form>
      <p style="color:#909399;font-size:12px;margin:0 0 8px">
        生成一人一张、带学号条码的答题卡页面，打印后按人分发。
      </p>
      <template #footer>
        <el-button @click="batchVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!batchClassId" @click="openBatchSheet">生成并打开</el-button>
      </template>
    </el-dialog>

    <!-- 题库复用对话框 -->
    <el-dialog v-model="reuseVisible" title="从题库复用题目" width="480px">
      <el-table :data="templatePapers" size="small" max-height="320" @current-change="(r:any) => reusePaperId = r?.id" highlight-current-row>
        <el-table-column prop="title" label="模板试卷" min-width="180" />
        <el-table-column prop="subject" label="科目" width="90" />
        <el-table-column prop="questionCount" label="题数" width="70" />
      </el-table>
      <template #footer>
        <el-button @click="reuseVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!reusePaperId" @click="doReuse">复制题目</el-button>
      </template>
    </el-dialog>

    <!-- 扫卡记录对话框 -->
    <el-dialog v-model="submissionsVisible" title="扫卡记录（上传 → AI批改 → 人工复判 → 确认）" width="78%" destroy-on-close>
      <div style="margin-bottom:12px;display:flex;gap:8px;align-items:center">
        <el-upload :show-file-list="false" accept="image/*" :before-upload="uploadSheet">
          <el-button type="primary" size="small" :loading="uploading">📸 上传答题卡照片</el-button>
        </el-upload>
        <span style="color:#909399;font-size:12px">上传后自动识别考号与客观题；主观题点"AI批改"整卷批改</span>
      </div>
      <el-table :data="submissions" v-loading="submissionsLoading" stripe>
        <el-table-column label="学生" width="140">
          <template #default="{row}">
            <span v-if="row.studentName">{{ row.studentName }}</span>
            <span v-else style="color:#e6a23c">考号 {{ row.studentRef || '未识别' }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="100">
          <template #default="{row}">
            <el-tag :type="row.status===3?'success':row.status===2?'danger':row.status===1?'warning':'info'" size="small">
              {{['未批','AI已批','待人工','已确认'][row.status]||'未知'}}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="totalScore" label="得分" width="80" />
        <el-table-column prop="submittedAt" label="提交时间" width="170">
          <template #default="{row}">{{formatTime(row.submittedAt)}}</template>
        </el-table-column>
        <el-table-column label="操作" width="330">
          <template #default="{row}">
            <el-button link type="primary" size="small" @click="openResults(row)">逐题</el-button>
            <el-button v-if="row.status<3" link type="warning" size="small" :loading="gradingId===row.id" @click="aiGradeAll(row)">AI批改</el-button>
            <el-button v-if="row.status!==3 && row.status!==2" link type="danger" size="small" @click="markReview(row)">待人工</el-button>
            <el-button v-if="row.status>=1" link type="success" size="small" @click="confirmGrade(row)">确认</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-dialog>

    <!-- 逐题批改对话框 -->
    <el-dialog v-model="resultsVisible" :title="`逐题批改 — ${currentSubmission?.studentName || currentSubmission?.studentRef || ''}`" width="85%" destroy-on-close>
      <el-table :data="results" v-loading="resultsLoading" stripe size="small">
        <el-table-column prop="index" label="题号" width="60">
          <template #default="{row}">{{ row.index + 1 }}</template>
        </el-table-column>
        <el-table-column prop="type" label="题型" width="70">
          <template #default="{row}">{{ typeNames[row.type] || '—' }}</template>
        </el-table-column>
        <el-table-column prop="content" label="题干" min-width="200" show-overflow-tooltip />
        <el-table-column prop="standardAnswer" label="标准答案" width="110" show-overflow-tooltip />
        <el-table-column prop="recognizedAnswer" label="识别/作答" width="130" show-overflow-tooltip />
        <el-table-column label="得分" width="150">
          <template #default="{row}">
            <el-input-number v-model="row.editScore" size="small" :min="0" :max="row.fullScore" :step="0.5"
                             :disabled="row.source === 'Teacher' && false" style="width:110px" />
            <span style="color:#909399;font-size:11px"> / {{ row.fullScore }}</span>
          </template>
        </el-table-column>
        <el-table-column label="评语" min-width="160">
          <template #default="{row}">
            <el-input v-model="row.editComment" size="small" placeholder="评语" />
          </template>
        </el-table-column>
        <el-table-column label="来源" width="80">
          <template #default="{row}">
            <el-tag v-if="row.source" size="small" :type="row.source==='Teacher'?'success':'warning'">
              {{ row.source==='Teacher' ? '教师' : row.source==='Ai' ? 'AI' : '—' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="置信度" width="80">
          <template #default="{row}">{{ row.confidence != null ? Math.round(row.confidence*100)+'%' : '—' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="90" fixed="right">
          <template #default="{row}">
            <el-button link type="primary" size="small" @click="saveResult(row)">保存</el-button>
          </template>
        </el-table-column>
      </el-table>
      <template #footer>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <el-select v-model="bindStudentId" placeholder="绑定学生（考号识别结果需确认）" size="small" style="width:280px" clearable>
            <el-option v-for="s in students" :key="s.id" :label="`${s.name}${s.studentNo ? ' (' + s.studentNo + ')' : ''}`" :value="s.id!" />
          </el-select>
          <div style="display:flex;gap:8px">
            <el-button @click="bindStudent">绑定学生</el-button>
            <el-button @click="resultsVisible = false">关闭</el-button>
          </div>
        </div>
      </template>
    </el-dialog>

    <!-- 成绩统计对话框 -->
    <el-dialog v-model="statsVisible" :title="`成绩统计 — ${stats?.paperTitle || ''}`" width="80%" destroy-on-close>
      <div v-if="stats">
        <el-row :gutter="12" style="margin-bottom:14px">
          <el-col :span="4"><el-statistic title="已确认份数" :value="stats.confirmedCount" /></el-col>
          <el-col :span="4"><el-statistic title="待批/待确认" :value="stats.pendingCount" /></el-col>
          <el-col :span="4"><el-statistic title="平均分" :value="stats.avgScore" /></el-col>
          <el-col :span="4"><el-statistic title="最高分" :value="stats.maxScore" /></el-col>
          <el-col :span="4"><el-statistic title="最低分" :value="stats.minScore" /></el-col>
          <el-col :span="4"><el-statistic title="及格率(%)" :value="stats.passRate" /></el-col>
        </el-row>
        <el-tabs>
          <el-tab-pane label="按学生总分">
            <el-table :data="stats.perStudent" size="small" stripe max-height="380">
              <el-table-column type="index" label="#" width="50" />
              <el-table-column prop="studentName" label="学生" min-width="120" />
              <el-table-column prop="totalScore" label="总分" width="100" sortable />
              <el-table-column label="得分率" width="200">
                <template #default="{row}">
                  <el-progress :percentage="Math.min(100, Math.round(row.totalScore / (stats.totalPaperScore || 1) * 100))" />
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>
          <el-tab-pane label="按题得分率">
            <el-table :data="stats.perQuestion" size="small" stripe max-height="380">
              <el-table-column label="题号" width="60">
                <template #default="{row}">{{ row.index + 1 }}</template>
              </el-table-column>
              <el-table-column label="题型" width="80">
                <template #default="{row}">{{ typeNames[row.type] || '—' }}</template>
              </el-table-column>
              <el-table-column prop="fullScore" label="满分" width="70" />
              <el-table-column prop="avgScore" label="平均得分" width="90" />
              <el-table-column label="得分率" min-width="220">
                <template #default="{row}">
                  <el-progress :percentage="Math.round(row.scoreRate)"
                               :color="row.scoreRate >= 60 ? '#34a853' : row.scoreRate >= 40 ? '#e6a23c' : '#ea4335'" />
                </template>
              </el-table-column>
              <el-table-column prop="answerCount" label="作答人数" width="90" />
            </el-table>
          </el-tab-pane>
        </el-tabs>
      </div>
      <template #footer>
        <el-button @click="exportCsv">导出 Excel (CSV)</el-button>
        <el-button @click="statsVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import { examApi, classApi, studentApi, type ClassRow, type StudentRow } from '@/api/client'

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

// 批量答题卡
const batchVisible = ref(false)
const batchClassId = ref('')

// 题库复用
const reuseVisible = ref(false)
const reusePaperId = ref('')
const templatePapers = ref<any[]>([])

// 扫卡记录
const submissionsVisible = ref(false)
const submissionsLoading = ref(false)
const submissions = ref<any[]>([])
const uploading = ref(false)
const gradingId = ref('')
const currentPaperId = ref('')

// 逐题批改
const resultsVisible = ref(false)
const resultsLoading = ref(false)
const results = ref<any[]>([])
const currentSubmission = ref<any>(null)
const bindStudentId = ref('')
const students = ref<StudentRow[]>([])

// 文件导题
const importVisible = ref(false)
const importQFile = ref<File | null>(null)
const importAFile = ref<File | null>(null)
const importing = ref(false)
const generatingAnswers = ref(false)
const importLog = ref('')
const logEl = ref<HTMLElement>()

function appendLog(text: string) {
  importLog.value += (importLog.value ? '\n' : '') + text
  void nextTick(() => { if (logEl.value) logEl.value.scrollTop = logEl.value.scrollHeight })
}

// 成绩统计
const statsVisible = ref(false)
const stats = ref<any>(null)

const typeNames = ['单选', '多选', '判断', '填空', '简答', '作文']

async function loadPapers() {
  loading.value = true
  try { papers.value = await examApi.list() } finally { loading.value = false }
}

function showCreate() {
  editingPaper.value = { title: '', subject: '', classId: '', isTemplate: false }
  questions.value = []
  sheetConfig.value = { paperTitle:'', subject:'', idAreaType:'bubble', hasSealLine:false, hasNotes:true, hasAB:false, isRed:false, verticalObj:false, partitioned:false, pageSize:'A4', columns:2 }
  activeTab.value = 'info'
}

function editPaper(row: any) {
  editingPaper.value = { ...row, isTemplate: !!row.isTemplate }
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
      knowledgeTags: parseTags(q.knowledgeTagsJson), aiGrading: q.aiGradingEnabled ?? null,
    }))
  } catch { questions.value = [] }
}

function parseTags(json?: string | null): string {
  if (!json) return ''
  try { const a = JSON.parse(json); return Array.isArray(a) ? a.join(',') : '' } catch { return '' }
}

function backToList() { editingPaper.value = null; questions.value = []; loadPapers() }

function addQ(type: number) {
  questions.value.push({ type, score:type===5?40:type>=3?5:1, content:'', standardAnswer:'', optionsJson:'[]', rubric:'', knowledgeTags:'', aiGrading: null })
}

function getOpts(q: any): {key:string;text:string}[] {
  try { const a=JSON.parse(q.optionsJson||'[]'); return Array.isArray(a)?a:[] } catch {} return [{key:'A',text:''},{key:'B',text:''}]
}
function optCount(q: any): number {
  const o = getOpts(q)
  return o.length || 4
}

function setOptCount(q: any, n: number) {
  const opts = getOpts(q)
  while (opts.length < n) opts.push({ key: String.fromCharCode(65 + opts.length), text: '' })
  while (opts.length > n) opts.pop()
  q.optionsJson = JSON.stringify(opts)
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
        standardAnswer:q.standardAnswer, optionsJson:q.optionsJson, rubric:q.rubric,
        knowledgeTagsJson: tagsJson(q.knowledgeTags), aiGradingEnabled: q.aiGrading }
      if (q.id) await examApi.updateQuestion(editingPaper.value.id, q.id, payload)
      else { const r = await examApi.createQuestion(editingPaper.value.id, payload); q.id = r.id }
    }
    ElMessage.success('已保存')
  } catch(e:any) { ElMessage.error(e.response?.data?.error||'保存失败') }
}

function tagsJson(tags?: string): string | null {
  const arr = (tags || '').split(/[,，]/).map(t => t.trim()).filter(Boolean)
  return arr.length ? JSON.stringify(arr) : null
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

// ── 批量答题卡（按班级一人一张带条码） ──
function showBatchSheet(row: any) {
  batchClassId.value = row.classId || ''
  currentPreviewPaper.value = row
  batchVisible.value = true
}
function openBatchSheet() {
  if (!currentPreviewPaper.value?.id || !batchClassId.value) return
  window.open(examApi.batchSheetUrl(currentPreviewPaper.value.id, batchClassId.value), '_blank')
  batchVisible.value = false
  ElMessage.info('批量答题卡已生成（一人一张带学号条码），Ctrl+P 即可打印')
}

// ── 题库复用 ──
async function showReuse() {
  templatePapers.value = papers.value.filter(p => p.isTemplate && p.id !== editingPaper.value?.id)
  if (templatePapers.value.length === 0) {
    ElMessage.info('暂无题库模板：在试卷基本信息里打开"题库模板"开关后即可被复用')
    return
  }
  reusePaperId.value = ''
  reuseVisible.value = true
}
async function doReuse() {
  if (!editingPaper.value?.id || !reusePaperId.value) return
  // 未保存的先落库，避免复制题无归属
  if (!editingPaper.value.id) { ElMessage.warning('请先保存试卷再复用题库'); return }
  try {
    const r = await examApi.reuse(editingPaper.value.id, reusePaperId.value)
    ElMessage.success(`已复制 ${r.copied} 题`)
    reuseVisible.value = false
    loadQuestions(editingPaper.value.id)
  } catch {}
}

// ── 文件导题与 AI 生成答案 ──
function showImportFile() {
  importQFile.value = null
  importAFile.value = null
  importVisible.value = true
}

/** pdf.js 把 PDF 每页渲染成 JPEG（数学公式文字层乱码，视觉识别读渲染像素） */
async function renderPdfToImages(file: File, maxPages = 12): Promise<Blob[]> {
  const pdfjs = await import('pdfjs-dist')
  // Vite 下用打包的 worker；中文 Word/PDF 渲染必须带 cMaps 与标准字体，否则整页空白
  const workerUrl = (await import('pdfjs-dist/build/pdf.worker.min.mjs?url')).default
  pdfjs.GlobalWorkerOptions.workerSrc = workerUrl

  const buf = new Uint8Array(await file.arrayBuffer())
  const pdf = await pdfjs.getDocument({
    data: buf,
    cMapUrl: '/assets/cmaps/',
    cMapPacked: true,
    standardFontDataUrl: '/assets/standard_fonts/',
  }).promise
  if (pdf.numPages > maxPages) {
    ElMessage.warning(`试卷共 ${pdf.numPages} 页，仅导入前 ${maxPages} 页`)
  }
  const blobs: Blob[] = []
  const pages = Math.min(pdf.numPages, maxPages)
  let blankPages = 0
  for (let i = 1; i <= pages; i++) {
    const page = await pdf.getPage(i)
    // 目标宽度 ~1400px：AI 视觉识别足够清晰，体积仅为 scale2 的 1/4（移动网络下大幅缩短上传时间）
    const base = page.getViewport({ scale: 1 })
    const scale = Math.min(2, Math.max(1, 1400 / base.width))
    const viewport = page.getViewport({ scale })
    const canvas = document.createElement('canvas')
    canvas.width = viewport.width
    canvas.height = viewport.height
    const ctx = canvas.getContext('2d')!
    await page.render({ canvasContext: ctx, viewport }).promise
    // 空白页检测：字符表缺失时中文页会渲染成全白，AI 只能返回 0 题
    if (isMostlyBlank(canvas)) blankPages++
    const blob = await new Promise<Blob | null>(r => canvas.toBlob(r, 'image/jpeg', 0.78))
    if (blob) blobs.push(blob)
    canvas.width = 0
  }
  if (!blobs.length) throw new Error('PDF 渲染结果为空，请确认文件未损坏')
  if (blankPages === pages) {
    throw new Error(`全部 ${pages} 页渲染为空白（中文字符表未加载）。请按 Ctrl+F5 强制刷新页面后重试`)
  }
  if (blankPages > 0) {
    ElMessage.warning(`${blankPages}/${pages} 页渲染疑似空白，请检查 PDF 是否为扫描件`)
  }
  return blobs
}

/** 采样画布判断是否近乎全白（<1% 非白像素视为空白页）：先缩到 64x64 再取样 */
function isMostlyBlank(canvas: HTMLCanvasElement): boolean {
  const sw = 64, sh = 64
  const tiny = document.createElement('canvas')
  tiny.width = sw
  tiny.height = sh
  const tctx = tiny.getContext('2d')!
  tctx.drawImage(canvas, 0, 0, sw, sh)
  const data = tctx.getImageData(0, 0, sw, sh).data
  let nonWhite = 0
  for (let i = 0; i < data.length; i += 4) {
    if (data[i] < 240 || data[i + 1] < 240 || data[i + 2] < 240) nonWhite++
  }
  return nonWhite < data.length / 400
}

async function doImportFile() {
  if (!editingPaper.value?.id) { ElMessage.warning('请先保存试卷再导入题目'); return }
  const qf = importQFile.value
  if (!qf) return
  importing.value = true
  try {
    const isPdf = qf.name.toLowerCase().endsWith('.pdf')
    if (isPdf) {
      // ── 逐批实时流程：每批 3 页 → 上传 → AI 识别 → 实时展示回复 → 自动入库 ──
      ElMessage.info('正在把 PDF 渲染为页面图片…')
      const qImgs = await renderPdfToImages(qf)
      const aImgs = importAFile.value && importAFile.value.name.toLowerCase().endsWith('.pdf')
        ? await renderPdfToImages(importAFile.value)
        : []

      importLog.value = `已渲染试卷 ${qImgs.length} 页${aImgs.length ? ` + 答案 ${aImgs.length} 页` : ''}\n`
      let importedTotal = 0
      let startNumber = questions.value.length + 1

      for (let i = 0; i < qImgs.length; i += 3) {
        const batch = qImgs.slice(i, i + 3)
        appendLog(`── 第 ${i + 1}-${i + batch.length} 页识别中… ──`)
        const r: any = await examApi.extractBatch(editingPaper.value.id, batch, startNumber)
        appendLog((r.raw || '(AI 无文本返回)').trim())
        appendLog(`✔ 本批导入 ${r.imported} 题`)
        importedTotal += r.imported || 0
        startNumber += r.imported || 0
      }

      if (aImgs.length) {
        appendLog('── 答案页回填中… ──')
        for (let i = 0; i < aImgs.length; i += 5) {
          const r = await examApi.fillBatch(editingPaper.value.id, aImgs.slice(i, i + 5))
          appendLog(`✔ 答案回填 ${r.updated} 题`)
        }
      }

      appendLog(`── 完成：共导入 ${importedTotal} 题 ──`)
      ElMessage.success(`AI 识别并导入 ${importedTotal} 题`)
      await loadQuestions(editingPaper.value.id)
      return
    }
    const r = await examApi.importFile(editingPaper.value.id, qf, importAFile.value || undefined)
    ElMessage.success(`AI 已识别并导入 ${r.imported} 题`)
    importVisible.value = false
    await loadQuestions(editingPaper.value.id)
  } catch (e: any) {
    // 错误必须可见：HTTP 业务错误取 error 字段，其余（渲染失败/网络/413 等）取 message
    const detail = e?.response?.data?.error || e?.message || '导入失败，请重试'
    ElMessage.error(String(detail).slice(0, 200))
  } finally {
    importing.value = false
  }
}

/** 后台识别轮询：题目出现即刷新；每分钟播报；10 分钟超时提示查日志 */


async function aiGenerateAnswers() {
  if (!editingPaper.value?.id) { ElMessage.warning('请先保存试卷'); return }
  generatingAnswers.value = true
  try {
    const r = await examApi.aiGenerateAnswers(editingPaper.value.id)
    ElMessage.success(r.updated > 0
      ? `AI 已为 ${r.updated}/${r.total} 题生成标准答案/评分要点`
      : '所有题目均已有答案与评分要点，无需生成')
    await loadQuestions(editingPaper.value.id)
  } catch {} finally { generatingAnswers.value = false }
}

// ── 扫卡记录与批改闭环 ──
async function viewSubmissions(row: any) {
  currentPaperId.value = row.id
  submissionsVisible.value = true
  submissionsLoading.value = true
  try {
    submissions.value = await examApi.getSubmissions(row.id)
    if (!students.value.length) {
      try { students.value = await studentApi.list() } catch {}
    }
  } catch { submissions.value = [] }
  finally { submissionsLoading.value = false }
}

async function uploadSheet(file: File) {
  uploading.value = true
  try {
    const r: any = await examApi.uploadSubmission(currentPaperId.value, file)
    ElMessage.success(`已上传${r.autoScored ? `，客观题自动判分 ${r.autoScored} 题` : ''}${r.matchedStudentId ? '，考号已匹配' : ''}`)
    submissions.value = await examApi.getSubmissions(currentPaperId.value)
  } catch {} finally { uploading.value = false }
  return false // 阻止 el-upload 默认上传
}

async function aiGradeAll(row: any) {
  gradingId.value = row.id
  try {
    const r = await examApi.gradeAll(row.id)
    ElMessage.success(r.graded > 0 ? `AI 已批改 ${r.graded} 题，状态：${statusName(r.status)}` : '没有可 AI 批改的主观题（客观题在上传时已自动判分）')
    row.status = statusIndex(r.status)
    submissions.value = await examApi.getSubmissions(currentPaperId.value)
  } catch {} finally { gradingId.value = '' }
}

async function markReview(row: any) {
  try {
    await examApi.review(row.id)
    ElMessage.success('已标记待人工复判')
    row.status = 2
  } catch {}
}

async function confirmGrade(row: any) {
  try { await examApi.confirm(row.id); ElMessage.success('已确认，成绩计入统计'); row.status = 3 } catch {}
}

function statusName(s: string) { return { NotGraded: '未批', AiGraded: 'AI已批', NeedsHuman: '待人工', Confirmed: '已确认' }[s] || s }
function statusIndex(s: string) { return ['NotGraded','AiGraded','NeedsHuman','Confirmed'].indexOf(s) }

// ── 逐题批改 ──
async function openResults(row: any) {
  currentSubmission.value = row
  bindStudentId.value = row.studentId || ''
  resultsVisible.value = true
  resultsLoading.value = true
  try {
    const data = await examApi.getResults(row.id)
    results.value = (data || []).map(r => ({ ...r, editScore: r.score ?? undefined, editComment: r.comment ?? '' }))
  } catch { results.value = [] } finally { resultsLoading.value = false }
}

async function saveResult(row: any) {
  if (row.editScore == null) { ElMessage.warning('请填写得分'); return }
  try {
    await examApi.overrideResult(currentSubmission.value.id, row.questionId, {
      score: row.editScore, comment: row.editComment,
    })
    ElMessage.success(`第 ${row.index + 1} 题已保存（教师批改留痕）`)
    row.source = 'Teacher'
  } catch {}
}

async function bindStudent() {
  if (!bindStudentId.value) { ElMessage.warning('请选择学生'); return }
  try {
    await examApi.bindStudent(currentSubmission.value.id, bindStudentId.value)
    ElMessage.success('已绑定学生')
    currentSubmission.value.studentId = bindStudentId.value
    const stu = students.value.find(s => s.id === bindStudentId.value)
    currentSubmission.value.studentName = stu?.name
    submissions.value = await examApi.getSubmissions(currentPaperId.value)
  } catch {}
}

// ── 成绩统计 ──
async function showStats(row: any) {
  try {
    stats.value = await examApi.statistics(row.id)
    statsVisible.value = true
  } catch {}
}
function exportCsv() {
  if (stats.value?.paperId) window.open(examApi.exportCsvUrl(stats.value.paperId), '_blank')
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
.import-log {
  background: #1e1e2e; color: #cdd6f4; border-radius: 8px;
  padding: 10px 12px; max-height: 220px; overflow-y: auto;
  font-family: Consolas, Menlo, monospace; font-size: 12px;
  white-space: pre-wrap; word-break: break-all; line-height: 1.6;
  margin-bottom: 10px;
}
.q-head { display: flex; align-items: center; gap: 8px; }
.q-num { font-weight: 700; font-size: 13px; color: #409eff; min-width: 20px; }
</style>
