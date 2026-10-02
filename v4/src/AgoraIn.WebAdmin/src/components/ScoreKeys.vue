<template>
  <!-- 记分快捷键：0 → 本题满分，间隔 0.5；窗口只显示 2-4 个，
       其余用上下箭头 + 滚轮/滑动到达（屏幕过小时自动减到 2 个） -->
  <div ref="rootEl" class="score-keys" @wheel.prevent="onWheel">
    <button class="arrow" :disabled="centerIndex <= 0" @click="step(-1)">▲</button>
    <button v-for="v in visibleValues" :key="v" class="key"
            :class="{ active: isActive(v) }" @click="$emit('update:modelValue', v)">
      {{ fmt(v) }}
    </button>
    <button class="arrow" :disabled="centerIndex >= steps - 1" @click="step(1)">▼</button>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

const props = withDefaults(defineProps<{
  modelValue: number | null
  max: number
  step?: number
  /** 可见按钮数（容器高度不足时父组件可给 2） */
  visibleCount?: number
}>(), { step: 0.5, visibleCount: 4 })



const steps = computed(() => Math.max(1, Math.round(props.max / props.step) + 1))
/** 窗口中心（未选分时停在满分附近，判卷人多数给高分） */
const centerIndex = ref(Math.min(steps.value - 1, Math.round(steps.value / 2)))

const visibleValues = computed(() => {
  const half = Math.floor(props.visibleCount / 2)
  const lo = Math.min(Math.max(0, centerIndex.value - half), Math.max(0, steps.value - props.visibleCount))
  const out: number[] = []
  for (let i = 0; i < Math.min(props.visibleCount, steps.value); i++)
    out.push(Math.min((lo + i) * props.step, props.max))
  return out
})

function isActive(v: number) {
  return props.modelValue != null && Math.abs(props.modelValue - v) < 1e-9
}

function step(dir: number) {
  centerIndex.value = Math.max(0, Math.min(steps.value - 1, centerIndex.value + dir))
}

function onWheel(e: WheelEvent) {
  step(e.deltaY > 0 ? 1 : -1)
}

// 触屏滑动
let touchY = 0
function onTouchStart(e: TouchEvent) { touchY = e.touches[0]?.clientY ?? 0 }
function onTouchEnd(e: TouchEvent) {
  const dy = (e.changedTouches[0]?.clientY ?? 0) - touchY
  if (Math.abs(dy) > 24) step(dy < 0 ? 1 : -1)   // 上滑 = 更高分
}

function fmt(v: number) {
  return (Math.round(v * 2) / 2).toString()
}

const rootEl = ref<HTMLElement>()
onMounted(() => {
  rootEl.value?.addEventListener('touchstart', onTouchStart, { passive: true })
  rootEl.value?.addEventListener('touchend', onTouchEnd, { passive: true })
})
onBeforeUnmount(() => {
  rootEl.value?.removeEventListener('touchstart', onTouchStart)
  rootEl.value?.removeEventListener('touchend', onTouchEnd)
})
</script>

<style scoped>
.score-keys {
  display: flex;
  flex-direction: column;
  gap: 6px;
  align-items: stretch;
  width: 76px;
  user-select: none;
  touch-action: none;
}
.arrow {
  border: 1px solid #dcdfe6;
  background: #f5f7fa;
  border-radius: 6px;
  height: 30px;
  cursor: pointer;
  color: #606266;
  font-size: 12px;
}
.arrow:disabled { opacity: .35; cursor: default; }
.key {
  border: 1px solid #dcdfe6;
  background: #fff;
  border-radius: 6px;
  height: 40px;
  font-size: 15px;
  font-weight: 600;
  cursor: pointer;
  color: #303133;
}
.key.active {
  background: #4285f4;
  border-color: #4285f4;
  color: #fff;
}
.key:active { transform: scale(.96); }
</style>
