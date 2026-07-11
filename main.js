const { app, BrowserWindow, ipcMain, Menu } = require('electron');
const path = require('path');
const fs = require('fs');

// Where user data lives. When run as a portable exe, we keep it next to the
// executable (in a "deltapad-data" folder) so the app is truly self-contained
// and portable (no install, no AppData dependency, works from a USB stick).
function getDataDir() {
  const base = process.env.PORTABLE_EXECUTABLE_DIR || path.dirname(app.getPath('exe'));
  const dir = path.join(base, 'deltapad-data');
  const audioDir = path.join(dir, 'audio');
  if (!fs.existsSync(audioDir)) fs.mkdirSync(audioDir, { recursive: true });
  return { dir, audioDir };
}

const { dir: DATA_DIR, audioDir: AUDIO_DIR } = getDataDir();
const META_PATH = path.join(DATA_DIR, 'pads.json');

function createWindow() {
  const win = new BrowserWindow({
    width: 1100,
    height: 820,
    minWidth: 640,
    minHeight: 560,
    backgroundColor: '#100e14',
    title: 'DeltaPad',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  Menu.setApplicationMenu(null); // clean window, no browser-like menu bar
  win.loadFile('index.html');

  // Temporary: auto-open DevTools so we can see console errors without relying
  // on a keyboard shortcut. Remove this line once debugging is done.
  win.webContents.openDevTools();

  win.webContents.on('before-input-event', (event, input) => {
    if (input.control && input.shift && input.key.toLowerCase() === 'i') {
      win.webContents.toggleDevTools();
    }
  });
}

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// ---- IPC: metadata ----
ipcMain.handle('padforge:load-meta', () => {
  try {
    if (!fs.existsSync(META_PATH)) return null;
    return fs.readFileSync(META_PATH, 'utf-8');
  } catch (e) {
    console.error('load-meta failed', e);
    return null;
  }
});

ipcMain.handle('padforge:save-meta', (event, jsonStr) => {
  try {
    fs.writeFileSync(META_PATH, jsonStr, 'utf-8');
    return true;
  } catch (e) {
    console.error('save-meta failed', e);
    return false;
  }
});

// ---- IPC: audio files ----
ipcMain.handle('padforge:save-audio', (event, id, dataUrl) => {
  try {
    const match = /^data:(.+);base64,(.*)$/.exec(dataUrl);
    if (!match) return false;
    const buffer = Buffer.from(match[2], 'base64');
    fs.writeFileSync(path.join(AUDIO_DIR, id), buffer);
    return true;
  } catch (e) {
    console.error('save-audio failed', e);
    return false;
  }
});

ipcMain.handle('padforge:load-audio', (event, id, mime) => {
  try {
    const filePath = path.join(AUDIO_DIR, id);
    if (!fs.existsSync(filePath)) return null;
    const buffer = fs.readFileSync(filePath);
    return `data:${mime};base64,${buffer.toString('base64')}`;
  } catch (e) {
    console.error('load-audio failed', e);
    return null;
  }
});

ipcMain.handle('padforge:delete-audio', (event, id) => {
  try {
    const filePath = path.join(AUDIO_DIR, id);
    if (fs.existsSync(filePath)) fs.unlinkSync(filePath);
    return true;
  } catch (e) {
    console.error('delete-audio failed', e);
    return false;
  }
});
