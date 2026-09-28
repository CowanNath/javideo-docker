<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Movie } from '@/types'
import { useFavoritesStore } from '@/stores/favorites'
import { t } from '@/utils/i18n'

const props = defineProps<{ movie: Movie; size?: 'sm' | 'md' | 'lg'; eager?: boolean }>()
const emit = defineEmits<{ click: [movie: Movie] }>()

const favs = useFavoritesStore()
// Make sure movie favorites are loaded once so the heart shows real state.
favs.ensureLoaded('movie')

// Wrap the store check in a computed so the reactive dependency on
// favs.movieIds is tracked explicitly — calling a method inside :class can
// fail to re-render in some setups.
const isFav = computed(() =>
  props.movie.id != null && favs.movieIds.includes(props.movie.id)
)

// Cards are portrait-shaped, so use the full poster. Thumbnails are commonly
// landscape stills and get cropped incorrectly in this layout.
const imageUrl = computed(() => {
  const cover = props.movie.coverUrl || ''
  // The poster endpoint previously cached thumbnail fallbacks for an hour.
  // Version its browser URL so corrected posters appear immediately.
  return cover.startsWith('/api/movies/') && cover.includes('/image/poster')
    ? `${cover}?v=portrait-2`
    : cover
})
const imageReady = ref(false)
const imageFailed = ref(false)
watch(imageUrl, () => {
  imageReady.value = false
  imageFailed.value = false
})

async function toggleFav(e: Event) {
  e.stopPropagation()
  if (props.movie.id != null) favs.toggle('movie', props.movie.id)
}
</script>

<template>
  <!-- w-full: fill the parent grid column (minmax tracks) instead of a fixed
       width, so cards stretch uniformly in every grid. -->
  <div class="card group cursor-pointer w-full" @click="emit('click', movie)">
    <div class="aspect-[2/3] bg-surface2 overflow-hidden relative">
      <div v-if="!imageReady" class="absolute inset-0 flex items-center justify-center text-muted">
        <span class="i-carbon-image text-3xl opacity-40" />
      </div>
      <img
        v-if="imageUrl && !imageFailed"
        :src="imageUrl"
        :alt="movie.number"
        class="absolute inset-0 w-full h-full object-cover group-hover:scale-105 transition-[opacity,transform] duration-300"
        :class="imageReady ? 'opacity-100' : 'opacity-0'"
        :loading="eager ? 'eager' : 'lazy'"
        decoding="async"
        referrerpolicy="no-referrer"
        @load="imageReady = true"
        @error="imageFailed = true"
      />

      <!-- 番号 badge -->
      <span
        class="absolute bottom-2 left-2 text-[11px] font-semibold px-2 py-0.5 rounded-md text-white"
        style="background: rgba(0,0,0,0.65); backdrop-filter: blur(4px);"
      >{{ movie.number }}</span>

      <!-- video / trailer badges (top-left, streaming-style pills) -->
      <div v-if="movie.hasVideo || movie.hasTrailer" class="absolute top-2 left-2 flex gap-1">
        <span
          v-if="movie.hasVideo"
          class="h-[18px] px-1.5 rounded-[5px] flex items-center gap-1 text-[10px] font-medium leading-none"
          style="background: rgba(0,0,0,0.55); backdrop-filter: blur(6px); color: var(--status-green);"
          :title="t('hasVideo')"
        >
          <span class="i-carbon-video text-[11px]" />{{ t('hasVideo') }}
        </span>
        <span
          v-if="movie.hasTrailer"
          class="h-[18px] px-1.5 rounded-[5px] flex items-center gap-1 text-[10px] font-medium leading-none"
          style="background: rgba(0,0,0,0.55); backdrop-filter: blur(6px); color: var(--primary);"
          :title="t('hasTrailer')"
        >
          <span class="i-carbon-play-filled-alt text-[11px]" />{{ t('hasTrailer') }}
        </span>
      </div>

      <!-- favorite heart (top-right, reactive state) -->
      <button
        v-if="movie.id != null"
        class="absolute top-2 right-2 w-7 h-7 rounded-full flex items-center justify-center text-white transition-all duration-150 hover:scale-110"
        :class="isFav ? '!text-red-500 opacity-100' : 'opacity-0 group-hover:opacity-100'"
        style="background: rgba(0,0,0,0.55); backdrop-filter: blur(4px);"
        :title="t('favorites')"
        :aria-label="t('favorites')"
        @click="toggleFav"
      >
        <span :class="isFav ? 'i-carbon-favorite-filled' : 'i-carbon-favorite'" />
      </button>

      <!-- hover overlay (decorative only — must not intercept clicks meant
           for the favorite heart underneath) -->
      <div
        class="absolute inset-0 opacity-0 group-hover:opacity-100 transition-opacity duration-200 flex items-end p-2.5 pointer-events-none"
        style="background: linear-gradient(to top, rgba(0,0,0,0.85), transparent 55%);"
      >
        <p class="text-white text-xs leading-snug line-clamp-3 drop-shadow">{{ movie.title || movie.number }}</p>
      </div>
    </div>
  </div>
</template>
