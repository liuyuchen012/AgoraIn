import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/views/RegisterView.vue'),
      meta: { public: true },
    },
    {
      // 手机 App 内嵌的动态扫卡页（WebView 直载；?token= 注入登录态，?paper= 预选试卷，?app=1 表示 App 内嵌）
      path: '/scan',
      name: 'scan-web',
      component: () => import('@/views/ScanWebView.vue'),
      meta: { public: true },
    },
    {
      // 手机 App 内嵌的逐题批改页（WebView 直载；?token= 注入登录态）
      path: '/grading/:submissionId',
      name: 'grading-mobile',
      component: () => import('@/views/GradingMobileView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      component: () => import('@/layouts/MainLayout.vue'),
      redirect: '/dashboard',
      children: [
        {
          path: 'dashboard',
          name: 'dashboard',
          component: () => import('@/views/DashboardView.vue'),
          meta: { title: '仪表盘' },
        },
        {
          path: 'classes',
          name: 'classes',
          component: () => import('@/views/ClassesView.vue'),
          meta: { title: '班级管理' },
        },
        {
          path: 'students',
          name: 'students',
          component: () => import('@/views/StudentsView.vue'),
          meta: { title: '学生管理' },
        },
        {
          path: 'devices',
          name: 'devices',
          component: () => import('@/views/DevicesView.vue'),
          meta: { title: '设备管理' },
        },
        {
          path: 'classhours',
          name: 'classhours',
          component: () => import('@/views/ClassHoursView.vue'),
          meta: { title: '课时管理' },
        },
        {
          path: 'schedule',
          name: 'schedule',
          component: () => import('@/views/ScheduleView.vue'),
          meta: { title: '课程表' },
        },
        {
          path: 'notices',
          name: 'notices',
          component: () => import('@/views/NoticesView.vue'),
          meta: { title: '通知公告' },
        },
        {
          path: 'resources',
          name: 'resources',
          component: () => import('@/views/ResourcesView.vue'),
          meta: { title: '资源库' },
        },
        {
          path: 'exams',
          name: 'exams',
          component: () => import('@/views/ExamsView.vue'),
          meta: { title: '试卷管理' },
        },
        {
          path: 'scores',
          name: 'scores',
          component: () => import('@/views/ScoresView.vue'),
          meta: { title: '成绩统计' },
        },
        {
          path: 'messages',
          name: 'messages',
          component: () => import('@/views/MessagesView.vue'),
          meta: { title: '家长消息' },
        },
        {
          path: 'users',
          name: 'users',
          component: () => import('@/views/UsersView.vue'),
          meta: { title: '用户管理' },
        },
        {
          path: 'regions',
          name: 'regions',
          component: () => import('@/views/RegionsView.vue'),
          meta: { title: '区域管理' },
        },
        {
          path: 'license',
          name: 'license',
          component: () => import('@/views/LicenseView.vue'),
          meta: { title: '授权管理' },
        },
        {
          path: 'settings',
          name: 'settings',
          component: () => import('@/views/SettingsView.vue'),
          meta: { title: '系统设置' },
        },
        {
          path: 'ai-settings',
          name: 'ai-settings',
          component: () => import('@/views/AiSettingsView.vue'),
          meta: { title: 'AI 批改设置' },
        },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isLoggedIn) {
    return { name: 'login' }
  }
  if (to.name === 'login' && auth.isLoggedIn) {
    return { name: 'dashboard' }
  }
})

export default router
