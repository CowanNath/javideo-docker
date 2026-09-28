<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import { useLibraryStore } from '@/stores/libraries'
import { useSettingsStore } from '@/stores/settings'
import { t } from '@/utils/i18n'

const libs = useLibraryStore()
const settings = useSettingsStore()

// Collapsible sidebar — persisted so it survives reloads (整体 #2).
const collapsed = ref(localStorage.getItem('javideo.sidebar.collapsed') === '1')
watch(collapsed, (v) => localStorage.setItem('javideo.sidebar.collapsed', v ? '1' : '0'))

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
    <!-- Sidebar (整体 #1: narrower) -->
    <aside
      class="shrink-0 flex flex-col border-r border-border transition-all duration-200"
      :class="collapsed ? 'w-[60px]' : 'w-[160px]'"
      style="background: var(--sidebar)"
    >
      <!-- Brand -->
      <div class="flex items-center gap-2.5 px-4 h-16 border-b border-border" :class="collapsed && 'justify-center px-0'">
        <img src="@/assets/logo-small.png" alt="Javideo" class="w-9 h-9 rounded-lg shrink-0 object-cover" />
        <span v-if="!collapsed" class="text-[15px] font-bold tracking-tight">Javideo</span>
      </div>

      <!-- Scrollable middle: media libraries first (app opens on 全部), then primary nav -->
      <div class="flex-1 overflow-y-auto">
        <!-- Media libraries -->
        <div class="p-2">
          <!-- Section label — same visual style as the nav items below -->
          <div
            class="flex items-center justify-center gap-2.5 px-3 py-2 rounded-md text-[13px] font-medium text-text-soft leading-none"
            :class="collapsed && '!px-0'"
            :title="collapsed ? t('libraries') : ''"
          >
            <span class="i-carbon-media-library text-base shrink-0" />
            <span v-if="!collapsed">{{ t('libraries') }}</span>
          </div>
          <!-- Virtual "all" library — always present, spans every library -->
          <RouterLink
            to="/library/all"
            class="nav-item w-full min-h-[36px] group"
            :class="collapsed && '!justify-center !px-0'"
            :title="collapsed ? t('allLibraries') : ''"
            active-class="!bg-primary-soft !text-primary"
          >
            <span class="i-carbon-apps shrink-0 text-base" />
            <span v-if="!collapsed" class="truncate flex-1 ml-2 leading-snug py-1">{{ t('allLibraries') }}</span>
            <span v-if="!collapsed" class="text-[11px] px-1.5 py-0.5 rounded-full bg-surface2 text-muted group-hover:bg-surface3 shrink-0">{{ totalMovies }}</span>
          </RouterLink>
          <RouterLink
            v-for="lib in libs.items"
            :key="lib.id"
            :to="`/library/${lib.id}`"
            class="nav-item w-full min-h-[36px] group"
            :class="collapsed && '!justify-center !px-0'"
            :title="collapsed ? lib.name : ''"
            active-class="!bg-primary-soft !text-primary"
          >
            <span class="i-carbon-folder shrink-0 text-base" />
            <span v-if="!collapsed" class="truncate flex-1 ml-2 leading-snug py-1">{{ lib.name }}</span>
            <span v-if="!collapsed" class="text-[11px] px-1.5 py-0.5 rounded-full bg-surface2 text-muted group-hover:bg-surface3 shrink-0">{{ lib.movieCount ?? 0 }}</span>
          </RouterLink>
          <div v-if="!libs.items.length" class="px-3 py-2 text-xs text-muted leading-relaxed"
               :class="collapsed && 'text-center px-0'">{{ t('noLibraries') }}</div>
        </div>

        <!-- Primary nav -->
        <nav class="flex flex-col gap-0.5 p-2 border-t border-border">
          <RouterLink
            v-for="n in nav"
            :key="n.to"
            :to="n.to"
            class="nav-item w-full justify-center"
            :class="collapsed && '!px-0'"
            :title="collapsed ? n.label : ''"
            active-class="!bg-primary-soft !text-primary"
          >
            <span :class="n.icon" class="text-base shrink-0" />
            <span v-if="!collapsed">{{ n.label }}</span>
          </RouterLink>
        </nav>
      </div>

      <!-- Collapse toggle (整体 #2) -->
      <div class="px-2 py-2 border-t border-border">
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

    <!-- Main content -->
    <main class="flex-1 overflow-y-auto">
      <slot />
    </main>
  </div>
</template>
