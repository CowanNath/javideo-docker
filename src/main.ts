import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { initTheme } from './utils/theme'
import { initLang } from './utils/i18n'

// Apply theme + language before mount.
initTheme()
initLang()
import './styles.css'
import 'virtual:uno.css'

// Surface any error that happens during/after mount onto the page, so a blank
// screen always shows *something* diagnostic instead of nothing.
function showError(msg: string) {
  const box = document.createElement('pre')
  box.style.cssText =
    'position:fixed;top:10px;left:10px;right:10px;z-index:99999;' +
    'background:#2a1518;color:#ff9b9b;padding:12px;border-radius:6px;' +
    'font-size:13px;white-space:pre-wrap;word-break:break-all;'
  box.textContent = '[启动错误] ' + msg
  document.body.appendChild(box)
  // eslint-disable-next-line no-console
  console.error('[javideo]', msg)
}

// Browser extensions can inject userscripts into the page. Their exceptions
// arrive at these global listeners but should not appear as Javideo errors.
const extensionSource = /(?:chrome|moz|safari-web)-extension:\/\//i
window.addEventListener('error', (e) => {
  if (extensionSource.test(`${e.filename}\n${e.error?.stack ?? ''}`)) return
  showError(e.message + (e.error ? '\n' + e.error.stack : ''))
})
window.addEventListener('unhandledrejection', (e) => {
  if (extensionSource.test(String(e.reason?.stack ?? e.reason))) return
  showError('Promise 未捕获: ' + e.reason)
})

try {
  const app = createApp(App)
  app.use(createPinia())
  app.use(router)
  app.mount('#app')
} catch (e: any) {
  showError('挂载失败: ' + (e?.message || String(e)) + '\n' + (e?.stack || ''))
}
