<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue'
import { useRoute } from 'vue-router'
import { useLibraryStore } from '@/stores/libraries'
import { useSettingsStore } from '@/stores/settings'
import { t } from '@/utils/i18n'

const route = useRoute()
const libs = useLibraryStore()
const settings = useSettingsStore()

// Collapsible sidebar — persisted so it survives reloads (整体 #2).
const collapsed = ref(localStorage.getItem('javideo.sidebar.collapsed') === '1')
watch(collapsed, (v) => localStorage.setItem('javideo.sidebar.collapsed', v ? '1' : '0'))

// Mobile (<md): the sidebar turns into an overlay drawer behind a top bar.
// matchMedia (not a one-shot innerWidth) so rotation/resize stays reactive.
const mq = window.matchMedia('(min-width: 768px)')
const isMobile = ref(!mq.matches)
const onMq = () => { isMobile.value = !mq.matches }
mq.addEventListener('change', onMq)
onUnmounted(() => mq.removeEventListener('change', onMq))

const drawerOpen = ref(false)
// The drawer always shows full labels, even when the desktop sidebar is collapsed.
const showFull = computed(() => isMobile.value || !collapsed.value)

function onDrawerKeydown(e: KeyboardEvent) { if (e.key === 'Escape') drawerOpen.value = false }
watch(drawerOpen, (open) => {
  document.body.style.overflow = open ? 'hidden' : ''
  window.removeEventListener('keydown', onDrawerKeydown)
  if (open) window.addEventListener('keydown', onDrawerKeydown)
})
// Navigating from the drawer closes it (the watcher above releases the lock).
watch(() => route.fullPath, () => { drawerOpen.value = false })
onUnmounted(() => {
  document.body.style.overflow = ''
  window.removeEventListener('keydown', onDrawerKeydown)
})

onMounted(async () => {
  await Promise.allSettled([libs.load(), settings.load()])
})

const nav = computed(() => [
  { to: '/search', label: t('search'), icon: 'i-carbon-search' },
  { to: '/favorites', label: t('favorites'), icon: 'i-carbon-favorite-filled' },
  { to: '/actors', label: t('actors'), icon: 'i-carbon-user-multiple' },
  { to: '/tags', label: t('tags'), icon: 'i-carbon-tag' },
  { to: '/settings', label: t('settings'), icon: 'i-carbon-settings' },
])

// Total ingested movies across all libraries — badge for the "全部" entry.
const totalMovies = computed(() => libs.items.reduce((n: number, l: any) => n + (l.movieCount ?? 0), 0))
</script>

<template>
  <div class="flex h-full">
    <!-- Mobile top bar: hamburger opens the drawer sidebar -->
    <header
      class="md:hidden fixed top-0 inset-x-0 z-40 flex items-center gap-2.5 h-14 px-4 border-b border-border"
      style="background: var(--sidebar)"
    >
      <button
        class="w-9 h-9 -ml-1.5 rounded-md flex items-center justify-center text-text-soft hover:bg-surface2 transition-colors"
        :aria-label="t('menu')"
        @click="drawerOpen = true"
      >
        <span class="i-carbon-menu text-xl" />
      </button>
      <img src="@/assets/logo-small.png" alt="Javideo" class="w-8 h-8 rounded-lg object-cover" />
      <span class="text-[15px] font-bold tracking-tight">Javideo</span>
    </header>

    <!-- Drawer backdrop (mobile only) -->
    <div
      v-if="drawerOpen"
      class="md:hidden fixed inset-0 z-[45] transition-opacity"
      style="background: rgba(0,0,0,0.5); backdrop-filter: blur(2px);"
      @click="drawerOpen = false"
    />

    <!-- Sidebar: static column on desktop, slide-in drawer on mobile -->
    <aside
      class="shrink-0 flex flex-col border-r border-border transition-all duration-200
             fixed inset-y-0 left-0 z-50 w-[260px] max-w-[85vw] shadow-lg
             md:static md:z-auto md:w-[160px] md:max-w-none md:shadow-none md:translate-x-0"
      :class="[
        drawerOpen ? 'translate-x-0' : '-translate-x-full',
        collapsed && 'md:!w-[60px]',
      ]"
      style="background: var(--sidebar)"
    >
      <!-- Brand -->
      <div class="flex items-center gap-2.5 px-4 h-16 border-b border-border shrink-0" :class="collapsed && 'md:justify-center md:px-0'">
        <img src="@/assets/logo-small.png" alt="Javideo" class="w-9 h-9 rounded-lg shrink-0 object-cover" />
        <span v-if="showFull" class="text-[15px] font-bold tracking-tight">Javideo</span>
      </div>

      <!-- Scrollable middle: media libraries first (app opens on 全部), then primary nav -->
      <div class="flex-1 overflow-y-auto">
        <!-- Media libraries -->
        <div class="p-2">
          <!-- Section label — same visual style as the nav items below -->
          <div
            class="flex items-center justify-center gap-2.5 px-3 py-2 rounded-md text-[13px] font-medium text-text-soft leading-none"
            :class="collapsed && 'md:!px-0'"
            :title="collapsed && !isMobile ? t('libraries') : ''"
          >
            <span class="i-carbon-media-library text-base shrink-0" />
            <span v-if="showFull">{{ t('libraries') }}</span>
          </div>
          <!-- Virtual "all" library — always present, spans every library -->
          <RouterLink
            to="/library/all"
            class="nav-item w-full min-h-[36px] group"
            :class="collapsed && 'md:!justify-center md:!px-0'"
            :title="collapsed && !isMobile ? t('allLibraries') : ''"
            active-class="!bg-primary-soft !text-primary"
          >
            <span class="i-carbon-apps shrink-0 text-base" />
            <span v-if="showFull" class="truncate flex-1 ml-2 leading-snug py-1">{{ t('allLibraries') }}</span>
            <span v-if="showFull" class="text-[11px] px-1.5 py-0.5 rounded-full bg-surface2 text-muted group-hover:bg-surface3 shrink-0">{{ totalMovies }}</span>
          </RouterLink>
          <RouterLink
            v-for="lib in libs.items"
            :key="lib.id"
            :to="`/library/${lib.id}`"
            class="nav-item w-full min-h-[36px] group"
            :class="collapsed && 'md:!justify-center md:!px-0'"
            :title="collapsed && !isMobile ? lib.name : ''"
            active-class="!bg-primary-soft !text-primary"
          >
            <span class="i-carbon-folder shrink-0 text-base" />
            <span v-if="showFull" class="truncate flex-1 ml-2 leading-snug py-1">{{ lib.name }}</span>
            <span v-if="showFull" class="text-[11px] px-1.5 py-0.5 rounded-full bg-surface2 text-muted group-hover:bg-surface3 shrink-0">{{ lib.movieCount ?? 0 }}</span>
          </RouterLink>
          <div v-if="!libs.items.length" class="px-3 py-2 text-xs text-muted leading-relaxed"
               :class="collapsed && 'md:text-center md:px-0'">{{ t('noLibraries') }}</div>
        </div>

        <!-- Primary nav -->
        <nav class="flex flex-col gap-0.5 p-2 border-t border-border">
          <RouterLink
            v-for="n in nav"
            :key="n.to"
            :to="n.to"
            class="nav-item w-full justify-center"
            :class="collapsed && 'md:!px-0'"
            :title="collapsed && !isMobile ? n.label : ''"
            active-class="!bg-primary-soft !text-primary"
          >
            <span :class="n.icon" class="text-base shrink-0" />
            <span v-if="showFull">{{ n.label }}</span>
          </RouterLink>
        </nav>
      </div>

      <!-- Collapse toggle (整体 #2) — desktop only, the mobile drawer is always full -->
      <div class="px-2 py-2 border-t border-border hidden md:block">
        <button
          class="nav-item w-full"
          :class="collapsed && '!justify-center !px-0'"
          :title="collapsed ? t('expandSidebar') : t('collapseSidebar')"
          @click="collapsed = !collapsed"
        >
          <span :class="collapsed ? 'i-carbon-chevron-right' : 'i-carbon-chevron-left'" class="text-base shrink-0" />
          <span v-if="!collapsed">{{ t('collapse') }}</span>
        </button>
      </div>
    </aside>

    <!-- Main content (pt-14 clears the mobile top bar) -->
    <main class="flex-1 overflow-y-auto max-md:pt-14">
      <slot />
    </main>
  </div>
</template>
