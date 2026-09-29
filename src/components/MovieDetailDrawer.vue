<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { movies as moviesApi, metatube, actors as actorsApi } from '@/api/worker'
import type { Movie, SubtitleItem } from '@/types'
import { useFavoritesStore } from '@/stores/favorites'
import { useLibraryStore } from '@/stores/libraries'
import { useBackdropClose } from '@/utils/clickOutside'
import { confirmDialog } from '@/utils/confirm'
import { toast } from '@/utils/toast'
import { t as ti } from '@/utils/i18n'

const props = defineProps<{ modelValue: boolean; movieId: number | null }>()
const emit = defineEmits<{
  'update:modelValue': [v: boolean]
  changed: []            // emitted after delete/rescrape so parent can reload
}>()

const router = useRouter()
const favs = useFavoritesStore()
const libs = useLibraryStore()
favs.ensureLoaded('movie')
const backdrop = useBackdropClose(() => close())

// Reactive favorite state for the heart (computed → dependency tracked).
const isFav = computed(() => movie.value?.id != null && favs.movieIds.includes(movie.value.id))

// Lightbox state.
const previewIdx = ref<number | null>(null)
const previewImg = computed(() =>
  previewIdx.value != null ? movie.value?.previewImages?.[previewIdx.value] ?? undefined : undefined
)
function prevPreview() {
  const imgs = movie.value?.previewImages
  if (!imgs?.length || previewIdx.value == null) return
  previewIdx.value = (previewIdx.value - 1 + imgs.length) % imgs.length
}
function nextPreview() {
  const imgs = movie.value?.previewImages
  if (!imgs?.length || previewIdx.value == null) return
  previewIdx.value = (previewIdx.value + 1) % imgs.length
}
function onKeydown(e: KeyboardEvent) {
  if (previewIdx.value == null) return
  if (e.key === 'Escape') { previewIdx.value = null }
  else if (e.key === 'ArrowLeft') { e.preventDefault(); prevPreview() }
  else if (e.key === 'ArrowRight') { e.preventDefault(); nextPreview() }
}

// Keyboard navigation for lightbox.
watch(previewIdx, (v) => {
  if (v != null) window.addEventListener('keydown', onKeydown)
  else window.removeEventListener('keydown', onKeydown)
})
onUnmounted(() => window.removeEventListener('keydown', onKeydown))

// Drag-to-scroll for the preview gallery (document-level listeners for smooth tracking).
// A click that follows real dragging must not open the lightbox, so track
// whether the pointer actually moved past a small threshold.
const galleryEl = ref<HTMLElement | null>(null)
const didDrag = ref(false)
let cleanupDrag: (() => void) | null = null
function startDrag(e: MouseEvent) {
  const el = galleryEl.value
  if (!el) return
  e.preventDefault()
  didDrag.value = false
  const startX = e.clientX, startScroll = el.scrollLeft
  const onMove = (e2: MouseEvent) => {
    if (Math.abs(e2.clientX - startX) > 5) didDrag.value = true
    el.scrollLeft = startScroll - (e2.clientX - startX)
  }
  const onUp = () => { document.removeEventListener('mousemove', onMove); document.removeEventListener('mouseup', onUp); cleanupDrag = null }
  document.addEventListener('mousemove', onMove)
  document.addEventListener('mouseup', onUp)
  cleanupDrag = onUp
}
onUnmounted(() => cleanupDrag?.())

// Open the lightbox on single click (dblclick was undiscoverable), unless the
// click was really the end of a drag-to-scroll gesture.
function openPreview(i: number) {
  if (!didDrag.value) previewIdx.value = i
}

// Escape closes the lightbox first, then the drawer (separate handlers handle
// the lightbox; we only close the drawer when it isn't open).
function onDrawerKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && previewIdx.value == null) close()
}

const movie = ref<Movie | null>(null)
const loading = ref(false)
const busy = ref(false)
const trailerSrc = ref<string>('')
const actionMsg = ref('')
const actionErr = ref(false)

// Inline player (trailer + main video streaming). Browsers decode
// mp4/webm/most mkv natively; exotic codecs raise the <video> error event →
// playerError shows a fallback hint.
const showPlayer = ref(false)
const playerSrc = ref('')
const playerError = ref(false)

function close() { emit('update:modelValue', false) }

async function load() {
  if (props.movieId == null) { movie.value = null; return }
  loading.value = true
  actionMsg.value = ''
  try {
    movie.value = await moviesApi.get(props.movieId)
    // Resolve the trailer URL once (only if a trailer file exists).
    trailerSrc.value = movie.value?.hasTrailer ? await moviesApi.trailerUrl(props.movieId) : ''
  } catch (e: any) {
    actionMsg.value = ti('loadFailed') + ': ' + e.message; actionErr.value = true
  } finally { loading.value = false }
}
watch(() => [props.modelValue, props.movieId], async ([open]) => {
  // Escape-to-close + body scroll lock while the drawer is up.
  window.removeEventListener('keydown', onDrawerKeydown)
  document.body.style.overflow = open ? 'hidden' : ''
  if (open) {
    window.addEventListener('keydown', onDrawerKeydown)
    await load()
  }
}, { immediate: true })
onUnmounted(() => {
  document.body.style.overflow = ''
  window.removeEventListener('keydown', onDrawerKeydown)
})

function playTrailer() {
  if (!trailerSrc.value) return
  playerError.value = false
  playerSrc.value = trailerSrc.value
  showPlayer.value = true
}

// Stream the main video through the worker (HTTP Range). The stream endpoint
// 404s for unstreamable files (.strm / missing) — surface that as the same
// unsupported-format hint.
async function play() {
  if (!movie.value?.id) return
  playerError.value = false
  const url = await moviesApi.videoUrl(movie.value.id)
  try {
    const head = await fetch(url, { method: 'HEAD' })
    if (!head.ok) { playerError.value = true; playerSrc.value = ''; showPlayer.value = true; return }
  } catch { /* probe failed — let the video element decide */ }
  playerSrc.value = url
  showPlayer.value = true
}

async function remove() {
  if (!movie.value?.id) return
  if (!await confirmDialog(ti('deleteConfirm'), ti('deleteMovie'))) return
  busy.value = true
  try {
    await moviesApi.remove(movie.value.id, true)
    // Keep the favorites store in sync so no stale heart remains for a
    // now-deleted movie.
    favs.movieIds = favs.movieIds.filter((id) => id !== movie.value!.id)
    // Refresh sidebar library counts — they include this movie until now.
    libs.load().catch(() => { /* sidebar refresh is best-effort */ })
    emit('changed')
    close()
  } catch (e: any) { actionMsg.value = ti('deleteFailed') + ': ' + e.message; actionErr.value = true }
  finally { busy.value = false }
}

const rescapeCandidates = ref<{ provider: string; id: string; title?: string | null; coverUrl?: string | null }[]>([])
const showRescrapePicker = ref(false)

async function rescrape() {
  if (!movie.value?.id || !movie.value.number) return
  busy.value = true; actionMsg.value = ti('scraping'); actionErr.value = false
  try {
    // First fetch candidates — if multiple, show picker.
    const cands = await metatube.candidates(movie.value.number)
    if (cands.length > 1) {
      rescapeCandidates.value = cands
      showRescrapePicker.value = true
      busy.value = false
      actionMsg.value = ''
      return
    }
    // Single result — scrape directly.
    await doRescrape()
  } catch { await doRescrape() }
  finally { busy.value = false }
}

async function doRescrape(provider?: string, id?: string) {
  if (!movie.value?.id) return
  busy.value = true; actionMsg.value = ti('scraping'); actionErr.value = false
  showRescrapePicker.value = false
  try {
    const res = provider && id
      ? await moviesApi.rescrapePick(movie.value.id, provider, id)
      : await moviesApi.rescrape(movie.value.id)
    actionMsg.value = res.detail || ti('scraped'); actionErr.value = false
    await load()
    emit('changed')
  } catch (e: any) { actionMsg.value = ti('rescrapeFail') + ': ' + e.message; actionErr.value = true }
  finally { busy.value = false }
}

async function toggleFav() {
  if (movie.value?.id) await favs.toggle('movie', movie.value.id)
}

const addingTag = ref(false)
const newTagName = ref('')
const tagInput = ref<HTMLInputElement | null>(null)
const committingTag = ref(false)

function startAddTag() {
  newTagName.value = ''
  addingTag.value = true
  nextTick(() => tagInput.value?.focus())
}
// Blur cancels the inline add-tag input (per UX decision) — committing
// happens only via Enter or the explicit confirm. If a commit is already in
// flight, ignore the blur that follows the input unmounting.
function cancelTag() {
  if (committingTag.value) return
  addingTag.value = false
}
async function commitTag() {
  const name = newTagName.value.trim()
  // Enter and blur can both fire — guard against double-submit.
  if (!name || !movie.value?.id || committingTag.value) return
  committingTag.value = true
  try {
    await moviesApi.addTag(movie.value.id, name)
    toast(ti('tagAdded'), 'success')
    addingTag.value = false
    await load()
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    committingTag.value = false
  }
}

// --- Actor add / rename / remove (for scrapes that returned no cast) ---
const addingActor = ref(false)
const newActorName = ref('')
const editingActorId = ref<number | null>(null)
const actorName = ref('')
const actorInput = ref<HTMLInputElement | null>(null)
const committingActor = ref(false)

function focusActorInput() {
  nextTick(() => actorInput.value?.focus())
}
function startAddActor() {
  editingActorId.value = null
  newActorName.value = ''
  addingActor.value = true
  focusActorInput()
}
function startActorRename(a: { id?: number; name?: string | null }) {
  if (a.id == null) return
  addingActor.value = false
  actorName.value = a.name ?? ''
  editingActorId.value = a.id
  focusActorInput()
}
function cancelActorEdit() {
  if (committingActor.value) return
  addingActor.value = false
  editingActorId.value = null
}
async function commitActorAdd() {
  const name = newActorName.value.trim()
  if (!name || !movie.value?.id || committingActor.value) return
  committingActor.value = true
  try {
    await moviesApi.addActor(movie.value.id, name)
    toast(ti('actorAdded'), 'success')
    addingActor.value = false
    await load()
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    committingActor.value = false
  }
}
async function commitActorRename(a: { id?: number }) {
  const name = actorName.value.trim()
  if (a.id == null || !name || committingActor.value) { cancelActorEdit(); return }
  committingActor.value = true
  try {
    await actorsApi.rename(a.id, name)
    toast(ti('actorUpdated'), 'success')
    editingActorId.value = null
    await load()
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    committingActor.value = false
  }
}
async function removeActor(a: { id?: number; name?: string | null }) {
  if (a.id == null || !movie.value?.id) return
  try {
    await moviesApi.removeActor(movie.value.id, a.id)
    toast(ti('actorRemoved'), 'success')
    await load()
  } catch (e: any) {
    toast(e.message, 'error')
  }
}

// --- Subtitles (Thunder API, matched by the video file's GCID) ---
const showSubtitles = ref(false)
const subsLoading = ref(false)
const subtitles = ref<SubtitleItem[]>([])
const subDetail = ref('')
const downloadedUrls = ref<Set<string>>(new Set())

async function openSubtitles() {
  if (!movie.value?.id) return
  showSubtitles.value = true
  subsLoading.value = true
  subtitles.value = []
  subDetail.value = ''
  downloadedUrls.value = new Set()
  try {
    const r = await moviesApi.subtitles.list(movie.value.id)
    subtitles.value = r.subtitles ?? []
    subDetail.value = r.detail ?? ''
  } catch (e: any) {
    subDetail.value = e.message
  } finally {
    subsLoading.value = false
  }
}
async function downloadSub(s: SubtitleItem) {
  if (!movie.value?.id || downloadedUrls.value.has(s.url)) return
  try {
    const r = await moviesApi.subtitles.download(movie.value.id, s.url)
    if (r.ok) {
      downloadedUrls.value = new Set([...downloadedUrls.value, s.url])
      toast(r.detail, 'success')
    } else {
      toast(r.detail, 'error')
    }
  } catch (e: any) {
    toast(e.message, 'error')
  }
}
// API duration is milliseconds → h:mm:ss for display.
function formatDuration(ms?: number): string {
  if (!ms || ms <= 0) return ''
  const total = Math.round(ms / 1000)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const mm = String(m).padStart(2, '0')
  const ss = String(s).padStart(2, '0')
  return h > 0 ? `${h}:${mm}:${ss}` : `${m}:${ss}`
}
// |subtitle duration − video duration|, shown in minutes.
function formatDiff(sec: number): string {
  const min = Math.max(1, Math.round(sec / 60))
  return `${min} ${ti('minutes')}`
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="modelValue"
      class="fixed inset-0 z-50 flex justify-end"
      style="background: rgba(0,0,0,0.5); backdrop-filter: blur(2px);"
      @mousedown="backdrop.onMouseDown"
      @mouseup="backdrop.onMouseUp"
    >
      <div class="bg-surface border-l border-border w-full sm:w-[420px] sm:max-w-[90vw] overflow-y-auto shadow-lg">
        <template v-if="loading">
          <div class="p-12 text-center text-muted text-sm">{{ ti('loading') }}</div>
        </template>
        <template v-else-if="movie">
          <!-- Cover banner with favorite heart + close -->
          <div class="relative">
            <img
              v-if="movie.thumbUrl || movie.coverUrl"
              :src="(movie.thumbUrl || movie.coverUrl) as string"
              class="w-full h-56 object-cover block"
              referrerpolicy="no-referrer"
            />
            <div v-else class="w-full h-56 bg-surface2 flex items-center justify-center text-muted">
              <span class="i-carbon-image text-4xl" />
            </div>
            <!-- favorite heart (top-left) -->
            <button
              v-if="movie.id"
              class="absolute top-3 left-3 w-9 h-9 rounded-full flex items-center justify-center text-white transition-all hover:scale-110"
              :class="isFav ? '!text-red-500' : ''"
              style="background: rgba(0,0,0,0.55); backdrop-filter: blur(4px);"
              :title="ti('favorites')"
              :aria-label="ti('favorites')"
              @click="toggleFav"
            >
              <span :class="isFav ? 'i-carbon-favorite-filled' : 'i-carbon-favorite'" />
            </button>
            <button
              class="absolute top-3 right-3 w-8 h-8 rounded-full flex items-center justify-center text-white"
              style="background: rgba(0,0,0,0.5); backdrop-filter: blur(4px);"
              :aria-label="ti('cancel')"
              @click="close"
            >
              <span class="i-carbon-close" />
            </button>
          </div>

          <!-- Preview images gallery (drag-to-scroll + double-click to lightbox) -->
          <div
            ref="galleryEl"
            v-if="trailerSrc || movie.previewImages?.length"
            class="flex gap-2 overflow-x-auto px-5 py-3 bg-surface2/50 cursor-grab select-none"
            style="-webkit-overflow-scrolling:touch; scrollbar-width:thin"
            @mousedown="startDrag"
          >
            <!-- Trailer (first) -->
            <div
              v-if="trailerSrc"
              class="relative h-28 w-44 shrink-0 rounded-md overflow-hidden border border-primary pointer-events-auto cursor-pointer group"
              @click.stop="playTrailer"
            >
              <video :src="trailerSrc" class="h-full w-full object-cover" muted preload="metadata" />
              <div class="absolute inset-0 flex items-center justify-center bg-black/40 group-hover:bg-black/20 transition-colors">
                <span class="i-carbon-play-filled-alt text-white text-3xl drop-shadow" />
              </div>
            </div>
            <img
              v-for="(img, i) in movie.previewImages"
              :key="i"
              :src="img"
              class="h-28 w-auto rounded-md shrink-0 object-cover border border-border cursor-zoom-in hover:brightness-90 transition-all pointer-events-auto"
              referrerpolicy="no-referrer"
              loading="lazy"
              @click="openPreview(i)"
            />
          </div>

          <div class="p-5">
            <!-- order per search: number+score, title, actors, other, summary -->
            <div class="flex items-center gap-2 flex-wrap mb-1.5">
              <span class="text-primary font-bold text-base">{{ movie.number }}</span>
              <span v-if="movie.score" class="chip !text-amber-400 !bg-amber-500/10 !border-amber-500/20">★ {{ movie.score }}</span>
            </div>
            <h2 class="text-lg font-bold leading-tight">{{ movie.title || movie.number }}</h2>

            <!-- actors (clickable; hover to rename/remove; add at the end) -->
            <div class="mt-3 flex flex-wrap items-center gap-x-2 gap-y-1.5">
              <template v-for="a in movie.actors" :key="a.id ?? a.name">
                <!-- renaming this actor → inline input -->
                <input
                  v-if="a.id != null && editingActorId === a.id"
                  ref="actorInput"
                  v-model="actorName"
                  class="chip !outline-none !border-primary !px-2 !py-0 !w-32 !text-[12px]"
                  :placeholder="ti('actorNamePlaceholder')"
                  @keydown.enter.prevent="commitActorRename(a)"
                  @keydown.escape.prevent="cancelActorEdit"
                  @blur="cancelActorEdit"
                />
                <span v-else class="inline-flex items-center gap-1 group">
                  <button class="inline-flex items-center gap-1.5 hover:text-primary transition-colors" @click="a.id && router.push(`/actors/${a.id}`)">
                    <span class="relative w-5 h-5 rounded-full bg-surface2 overflow-hidden inline-flex items-center justify-center text-muted shrink-0">
                      <span class="i-carbon-user text-[10px]" />
                      <img
                        v-if="a.avatarUrl"
                        :src="a.avatarUrl"
                        class="absolute inset-0 w-full h-full object-cover"
                        referrerpolicy="no-referrer"
                        @error="($event.target as HTMLImageElement).style.display = 'none'"
                      />
                    </span>
                    <span class="text-[12px]">{{ a.name }}</span>
                  </button>
                  <!-- edit / remove, visible on chip hover (always on touch) -->
                  <span class="hidden group-hover:inline-flex touch-show items-center gap-0.5">
                    <button class="w-4 h-4 rounded flex items-center justify-center text-muted hover:text-primary" :title="ti('renameActor')" :aria-label="ti('renameActor')" @click.stop="startActorRename(a)">
                      <span class="i-carbon-edit text-[10px]" />
                    </button>
                    <button class="w-4 h-4 rounded flex items-center justify-center text-muted hover:text-red-400" :title="ti('removeActor')" :aria-label="ti('removeActor')" @click.stop="removeActor(a)">
                      <span class="i-carbon-close text-[10px]" />
                    </button>
                  </span>
                </span>
              </template>

              <!-- add actor -->
              <input
                v-if="addingActor"
                ref="actorInput"
                v-model="newActorName"
                class="chip !outline-none !border-primary !px-2 !py-0 !w-32 !text-[12px]"
                :placeholder="ti('actorNamePlaceholder')"
                @keydown.enter.prevent="commitActorAdd"
                @keydown.escape.prevent="cancelActorEdit"
                @blur="cancelActorEdit"
              />
              <button v-else class="chip !px-2 !text-muted hover:!text-text hover:!border-primary" :title="ti('addActor')" @click="startAddActor">
                <span class="i-carbon-add text-[14px]" />{{ ti('addActor') }}
              </button>
            </div>

            <!-- other info -->
            <div class="flex flex-wrap gap-x-5 gap-y-1.5 text-[12px] text-text-soft mt-3">
              <span v-if="movie.maker" class="flex items-center gap-1.5"><span class="i-carbon-building text-muted" />{{ movie.maker }}</span>
              <span v-if="movie.releaseDate" class="flex items-center gap-1.5"><span class="i-carbon-calendar text-muted" />{{ movie.releaseDate.slice(0,10) }}</span>
              <span v-if="movie.runtimeMinutes" class="flex items-center gap-1.5"><span class="i-carbon-time text-muted" />{{ movie.runtimeMinutes }} {{ ti('minutes') }}</span>
            </div>

            <!-- tags (clickable) + add tag -->
            <div class="flex flex-wrap gap-1.5 mt-3">
              <span v-for="t in movie.tags" :key="t.id ?? t.name" class="chip" @click="t.id && router.push(`/tags/${t.id}`)">{{ t.name }}</span>
              <button v-if="!addingTag" class="chip !px-2 !text-muted hover:!text-text hover:!border-primary" :title="ti('addCustomTag')" @click="startAddTag">
                <span class="i-carbon-add text-[14px]" />
              </button>
              <input v-else ref="tagInput" v-model="newTagName"
                class="chip !outline-none !border-primary !px-2 !py-0 !w-28 !text-[12px]"
                :placeholder="ti('tagName')" @keydown.enter.prevent="commitTag"
                @keydown.escape.prevent="addingTag = false" @blur="cancelTag" />
            </div>

            <p v-if="movie.summary" class="text-[13px] text-text-soft leading-relaxed mt-3">{{ movie.summary }}</p>

            <div v-if="movie.folderPath" class="flex items-start gap-2 mt-3 text-[11px]">
              <span class="i-carbon-folder mt-0.5 text-muted" />
              <span class="break-all text-muted flex-1">{{ movie.folderPath }}</span>
            </div>

            <!-- actions -->
            <div class="flex items-center gap-2 mt-5 pt-4 border-t border-border">
              <button class="btn-primary flex-1" :disabled="busy" @click="play">
                <span class="i-carbon-play-filled" /> {{ ti('play') }}
              </button>
              <button class="btn" :title="ti('subtitles')" :aria-label="ti('subtitles')" @click="openSubtitles">
                <span class="i-carbon-closed-caption" />
              </button>
              <button class="btn" :disabled="busy" :title="busy ? ti('scraping') : ti('rescrape')" @click="rescrape">
                <span :class="busy ? 'i-carbon-renew animate-spin' : 'i-carbon-renew'" />
              </button>
              <button class="btn hover:!text-red-400" :disabled="busy" :title="ti('delete')" @click="remove">
                <span class="i-carbon-trash-can" />
              </button>
            </div>
            <p
              v-if="actionMsg"
              class="text-[12px] mt-2"
              :class="actionErr ? 'text-red-400' : 'text-status-green'"
            >{{ actionMsg }}</p>
          </div>
        </template>
      </div>
    </div>

    <!-- Image lightbox (double-click preview to open, prev/next, keyboard) -->
    <Teleport to="body">
      <div
        v-if="previewIdx != null"
        class="fixed inset-0 z-[70] flex items-center justify-center p-4 sm:p-8"
        style="background: rgba(0,0,0,0.88); backdrop-filter: blur(6px);"
        @click="previewIdx = null"
      >
        <img
          :src="previewImg"
          class="max-w-[85vw] max-h-[90vh] object-contain rounded-lg shadow-2xl pointer-events-none select-none"
          referrerpolicy="no-referrer"
        />
        <!-- Prev -->
        <button
          class="absolute left-4 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full flex items-center justify-center text-white hover:scale-110 transition-all pointer-events-auto"
          style="background: rgba(0,0,0,0.6); backdrop-filter: blur(4px);"
          aria-label="←"
          @click.stop="prevPreview"
        >
          <span class="i-carbon-chevron-left text-xl" />
        </button>
        <!-- Next -->
        <button
          class="absolute right-4 top-1/2 -translate-y-1/2 w-10 h-10 rounded-full flex items-center justify-center text-white hover:scale-110 transition-all pointer-events-auto"
          style="background: rgba(0,0,0,0.6); backdrop-filter: blur(4px);"
          aria-label="→"
          @click.stop="nextPreview"
        >
          <span class="i-carbon-chevron-right text-xl" />
        </button>
        <!-- Counter -->
        <span
          class="absolute bottom-6 left-1/2 -translate-x-1/2 px-3 py-1 rounded-full text-xs text-white/80"
          style="background: rgba(0,0,0,0.6);"
        >
          {{ (previewIdx ?? 0) + 1 }} / {{ movie?.previewImages?.length ?? 0 }}
        </span>
        <!-- Close -->
        <button
          class="absolute top-4 right-4 w-9 h-9 rounded-full flex items-center justify-center text-white/80 hover:text-white hover:scale-110 transition-all"
          style="background: rgba(0,0,0,0.6); backdrop-filter: blur(4px);"
          :aria-label="ti('cancel')"
          @click.stop="previewIdx = null"
        >
          <span class="i-carbon-close text-lg" />
        </button>
      </div>
    </Teleport>
  </Teleport>

  <!-- Video player overlay (main video stream / trailer) -->
  <Teleport to="body">
    <div
      v-if="showPlayer"
      class="fixed inset-0 z-[80] flex items-center justify-center p-3 sm:p-8"
      style="background: rgba(0,0,0,0.9); backdrop-filter: blur(6px);"
      @click.self="showPlayer = false"
    >
      <div class="relative max-w-[90vw] max-h-[90vh]">
        <video
          v-if="playerSrc"
          :src="playerSrc"
          class="max-w-[88vw] max-h-[86vh] rounded-lg shadow-2xl bg-black"
          controls
          autoplay
          @error="playerError = true"
        />
        <button
          class="absolute -top-3 -right-3 w-9 h-9 rounded-full flex items-center justify-center text-white shadow-lg"
          style="background: rgba(0,0,0,0.7);"
          @click="showPlayer = false"
        >
          <span class="i-carbon-close text-lg" />
        </button>
        <div
          v-if="playerError"
          class="absolute inset-0 flex flex-col items-center justify-center gap-3 rounded-lg bg-black/85 text-center p-6"
        >
          <span class="i-carbon-warning-alt text-3xl text-amber-400" />
          <p class="text-sm text-white/90 max-w-[60vw] leading-relaxed">{{ ti('videoNotSupported') }}</p>
        </div>
      </div>
    </div>
  </Teleport>

  <!-- Subtitle picker (Thunder match by GCID) -->
  <Teleport to="body">
    <div
      v-if="showSubtitles"
      class="fixed inset-0 z-[75] flex items-center justify-center p-4 sm:p-8"
      style="background: rgba(0,0,0,0.7); backdrop-filter: blur(3px);"
      @click.self="showSubtitles = false"
    >
      <div class="card !rounded-lg w-[560px] max-w-[90vw] max-h-[80vh] overflow-y-auto p-5">
        <h3 class="text-[15px] font-semibold mb-3">{{ ti('subtitles') }}</h3>
        <div v-if="subsLoading" class="text-muted text-sm py-8 text-center">{{ ti('loading') }}</div>
        <template v-else>
          <div v-if="!subtitles.length" class="text-muted text-sm py-6 text-center">
            {{ subDetail || ti('subNone') }}
          </div>
          <div v-else class="flex flex-col gap-1.5">
            <div v-for="(s, i) in subtitles" :key="i" class="flex items-center gap-3 p-2.5 rounded-md border border-border">
              <span class="chip !cursor-default shrink-0">{{ s.ext.toUpperCase() }}</span>
              <div class="flex-1 min-w-0">
                <div class="text-[12px] leading-snug line-clamp-2 break-all" :title="s.name">{{ s.name || s.url }}</div>
                <div class="text-[11px] text-muted mt-0.5 flex items-center gap-2 flex-wrap">
                  <span
                    v-if="s.gcidHit"
                    class="inline-flex items-center gap-0.5 px-1.5 rounded text-status-green bg-green-500/10 font-medium"
                    :title="ti('subGcidHit')"
                  >GCID</span>
                  <span v-if="s.durationMs" class="inline-flex items-center gap-0.5">
                    <span class="i-carbon-time text-[11px]" />{{ formatDuration(s.durationMs) }}
                  </span>
                  <span v-if="s.languages">{{ s.languages }}</span>
                  <span v-if="s.score > 0">{{ ti('subScore') }} {{ s.score }}</span>
                  <span
                    v-if="s.durationDiffSec != null"
                    class="inline-flex items-center gap-0.5 px-1.5 rounded"
                    :class="s.durationDiffSec <= 120 ? 'text-status-green bg-green-500/10' : ''"
                    :title="ti('subDurDiff')"
                  >
                    <span class="i-carbon-timer text-[11px]" />{{ ti('subDurDiff') }} {{ formatDiff(s.durationDiffSec) }}
                  </span>
                </div>
              </div>
              <button
                class="btn-ghost !text-primary shrink-0"
                :disabled="downloadedUrls.has(s.url)"
                @click="downloadSub(s)"
              >
                <span :class="downloadedUrls.has(s.url) ? 'i-carbon-checkmark-filled' : 'i-carbon-download'" />
                {{ downloadedUrls.has(s.url) ? ti('subDownloaded') : ti('download') }}
              </button>
            </div>
          </div>
        </template>
        <div class="flex justify-end mt-3">
          <button class="btn" @click="showSubtitles = false">{{ ti('cancel') }}</button>
        </div>
      </div>
    </div>
  </Teleport>

  <!-- Rescrape candidate picker -->
  <Teleport to="body">
    <div
      v-if="showRescrapePicker"
      class="fixed inset-0 z-[75] flex items-center justify-center p-4 sm:p-8"
      style="background: rgba(0,0,0,0.7); backdrop-filter: blur(3px);"
      @click.self="showRescrapePicker = false"
    >
      <div class="card !rounded-lg p-5 w-[600px] max-w-[90vw] max-h-[80vh] overflow-y-auto">
        <h3 class="text-[15px] font-semibold mb-3">{{ ti('multipleResults') }}</h3>
        <div class="flex gap-2 overflow-x-auto pb-2">
          <button
            v-for="(c, i) in rescapeCandidates"
            :key="i"
            class="card !rounded-md p-2 w-36 shrink-0 text-left"
            @click="doRescrape(c.provider, c.id)"
          >
            <div class="aspect-[2/3] bg-surface2 rounded overflow-hidden mb-1.5">
              <img v-if="c.coverUrl" :src="c.coverUrl" class="w-full h-full object-cover" referrerpolicy="no-referrer" loading="lazy" />
              <div v-else class="w-full h-full flex items-center justify-center text-muted"><span class="i-carbon-image text-2xl" /></div>
            </div>
            <div class="text-[10px] text-muted truncate">[{{ c.provider }}]</div>
            <div class="text-[11px] truncate leading-tight">{{ c.title || c.id }}</div>
          </button>
        </div>
        <div class="flex justify-end mt-3">
          <button class="btn" @click="showRescrapePicker = false">{{ ti('cancel') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
