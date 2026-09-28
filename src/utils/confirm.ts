// Web build: the browser's native confirm is always available. The Tauri
// dialog-plugin variant lives in the desktop-only source tree.
export async function confirmDialog(message: string, _title = '确认'): Promise<boolean> {
  return window.confirm(message)
}
