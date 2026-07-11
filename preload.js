const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('padforge', {
  loadMeta: () => ipcRenderer.invoke('padforge:load-meta'),
  saveMeta: (jsonStr) => ipcRenderer.invoke('padforge:save-meta', jsonStr),
  saveAudio: (id, dataUrl) => ipcRenderer.invoke('padforge:save-audio', id, dataUrl),
  loadAudio: (id, mime) => ipcRenderer.invoke('padforge:load-audio', id, mime),
  deleteAudio: (id) => ipcRenderer.invoke('padforge:delete-audio', id),
});
