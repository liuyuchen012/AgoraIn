<template>
  <el-card shadow="never" class="page-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">家长消息会话</span>
        <el-button size="small" @click="loadConversations">刷新</el-button>
      </div>
    </template>

    <el-empty v-if="!conversations.length && !loading" description="暂无家长留言" />

    <div v-else class="chat-layout">
      <!-- 会话列表 -->
      <div class="conv-list">
        <div v-for="c in conversations" :key="c.parentUserId" class="conv-item"
             :class="{ active: c.parentUserId === activeConv?.parentUserId }"
             @click="openConversation(c)">
          <div class="conv-title">
            <span>{{ c.studentId ? studentName(c.studentId) : c.parentUserId }}</span>
            <el-tag v-if="c.hasFlagged" size="small" type="danger">敏感词</el-tag>
          </div>
          <div class="conv-last">{{ c.lastMessage }}</div>
          <div class="conv-time">{{ formatTime(c.lastTime) }} · {{ c.messageCount }} 条</div>
        </div>
      </div>

      <!-- 会话内容 -->
      <div class="chat-panel">
        <el-empty v-if="!activeConv" description="选择左侧会话开始查看" />
        <template v-else>
          <div class="messages" ref="messagesEl">
            <div v-for="m in messages" :key="m.id" class="msg" :class="{ mine: m.senderRole === 0 }">
              <div class="msg-meta">
                {{ m.senderRole === 0 ? m.senderName : (m.senderName || '家长') }}
                · {{ formatTime(m.createdAt) }}
                <el-tag v-if="m.flagged" size="small" type="danger">待审核</el-tag>
              </div>
              <div class="msg-bubble">
                <img v-if="m.isImage" :src="fileUrl(m.content)" style="max-width:220px;border-radius:6px" />
                <span v-else>{{ m.content }}</span>
              </div>
            </div>
          </div>
          <div class="reply-bar">
            <el-input v-model="replyText" placeholder="回复家长…" type="textarea" :rows="2" @keydown.enter.ctrl="send" />
            <el-button type="primary" :disabled="!replyText.trim()" @click="send">发送</el-button>
          </div>
        </template>
      </div>
    </div>
  </el-card>
</template>

<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue'
import { messageApi, studentApi, type MessageConversationRow, type MessageRow, type StudentRow } from '@/api/client'

const loading = ref(false)
const conversations = ref<MessageConversationRow[]>([])
const activeConv = ref<MessageConversationRow | null>(null)
const messages = ref<MessageRow[]>([])
const replyText = ref('')
const students = ref<StudentRow[]>([])
const messagesEl = ref<HTMLElement>()

onMounted(async () => {
  await loadConversations()
  try { students.value = await studentApi.list() } catch {}
})

async function loadConversations() {
  loading.value = true
  try { conversations.value = await messageApi.conversations() } finally { loading.value = false }
}

async function openConversation(c: MessageConversationRow) {
  activeConv.value = c
  messages.value = await messageApi.conversation(c.parentUserId, c.studentId || undefined)
  await nextTick()
  messagesEl.value?.scrollTo({ top: messagesEl.value.scrollHeight })
}

async function send() {
  if (!activeConv.value || !replyText.value.trim()) return
  await messageApi.reply({
    parentUserId: activeConv.value.parentUserId,
    studentId: activeConv.value.studentId || undefined,
    classId: activeConv.value.className || undefined,
    content: replyText.value.trim(),
  })
  replyText.value = ''
  await openConversation(activeConv.value)
}

function studentName(studentId: string) {
  return students.value.find(s => s.id === studentId)?.name || '学生'
}

function fileUrl(content: string) {
  return content.startsWith('http') ? content : `/api/v4/resources/file-by-key/${encodeURIComponent(content)}`
}

function formatTime(t: string) { return t ? String(t).replace('T', ' ').substring(5, 16) : '—' }
</script>

<style scoped>
.page-card { border-radius: 10px; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
.card-title { font-weight: 600; }
.chat-layout { display: flex; gap: 12px; min-height: 480px; }
.conv-list { width: 280px; border-right: 1px solid #e4e7ed; overflow-y: auto; max-height: 560px; }
.conv-item { padding: 10px 12px; cursor: pointer; border-radius: 8px; }
.conv-item:hover { background: #f5f7fa; }
.conv-item.active { background: #ecf5ff; }
.conv-title { display: flex; justify-content: space-between; align-items: center; font-weight: 600; font-size: 13px; }
.conv-last { font-size: 12px; color: #606266; margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.conv-time { font-size: 11px; color: #909399; margin-top: 2px; }
.chat-panel { flex: 1; display: flex; flex-direction: column; }
.messages { flex: 1; overflow-y: auto; padding: 8px; background: #f5f7fa; border-radius: 8px; max-height: 440px; }
.msg { margin-bottom: 10px; }
.msg-meta { font-size: 11px; color: #909399; margin-bottom: 2px; }
.msg-bubble { display: inline-block; background: #fff; border-radius: 8px; padding: 8px 12px; font-size: 13px; max-width: 70%; box-shadow: 0 1px 2px rgba(0,0,0,.06); }
.msg.mine .msg-bubble { background: #e8f0fe; }
.reply-bar { display: flex; gap: 8px; margin-top: 10px; align-items: flex-end; }
</style>
