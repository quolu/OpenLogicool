// 遠隔表示の受信側の測定。窓を出さない Chrome で視聴ページを開き、届いた映像の「絵が前のコマから変わったか」を数える。
// コマの数と間隔だけでは、同じ絵の繰り返し（送り出し側が映像を止めて埋めた状態）を見逃す。
// 使い方: node scripts/remote-view-receive-check.mjs <視聴用のURL> <IDとパスワードのJSON> [秒数]
//   JSON は {"viewUser": "...", "viewPass": "..."}。パスワードは表示しない。
import { spawn } from 'node:child_process';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [, , viewerUrl, credsPath, secondsArg] = process.argv;
if (!viewerUrl || !credsPath) {
  console.error('使い方: node scripts/remote-view-receive-check.mjs <視聴用のURL> <IDとパスワードのJSON> [秒数]');
  process.exit(2);
}
const seconds = Number(secondsArg ?? 30);
const creds = JSON.parse(readFileSync(credsPath, 'utf8'));
const auth = 'Basic ' + Buffer.from(`${creds.viewUser}:${creds.viewPass}`).toString('base64');
const chromePath = process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const port = 9333;
const profileDir = mkdtempSync(join(tmpdir(), 'remote-view-check-'));
const chrome = spawn(chromePath, [
  '--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profileDir}`,
  '--autoplay-policy=no-user-gesture-required', '--mute-audio', '--no-first-run', '--no-default-browser-check', 'about:blank',
], { stdio: 'ignore' });

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
async function pageSocketUrl() {
  for (let attempt = 0; attempt < 50; attempt++) {
    try {
      const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      const page = targets.find((target) => target.type === 'page');
      if (page) return page.webSocketDebuggerUrl;
    } catch { /* まだ起動中 */ }
    await sleep(200);
  }
  throw new Error('Chrome の debugging port へ接続できません');
}

// 視聴ページの中で動かす測定。コマが表示されるたびに縮小した絵を読み、前のコマとの差を取る。
const measure = `(async () => {
  const video = document.querySelector('video');
  if (!video) return JSON.stringify({ error: '視聴ページに video がありません', body: document.body.innerText.slice(0, 200) });
  const canvas = document.createElement('canvas'); canvas.width = 96; canvas.height = 54;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  const presented = []; const differences = []; let previous = null; let running = true;
  const onFrame = (now) => {
    presented.push(now);
    context.drawImage(video, 0, 0, 96, 54);
    const pixels = context.getImageData(0, 0, 96, 54).data;
    if (previous) {
      let sum = 0;
      for (let i = 0; i < pixels.length; i += 4) sum += Math.abs(pixels[i] - previous[i]) + Math.abs(pixels[i + 1] - previous[i + 1]) + Math.abs(pixels[i + 2] - previous[i + 2]);
      differences.push(sum / (96 * 54 * 3));
    }
    previous = pixels.slice();
    if (running) video.requestVideoFrameCallback(onFrame);
  };
  video.requestVideoFrameCallback(onFrame);
  await new Promise((resolve) => setTimeout(resolve, ${seconds} * 1000));
  running = false;
  const intervals = presented.slice(1).map((value, index) => value - presented[index]).sort((a, b) => a - b);
  const at = (p) => intervals.length ? +intervals[Math.min(intervals.length - 1, Math.floor(p * intervals.length))].toFixed(1) : null;
  let identical = 0, run = 0, longestRun = 0, runsOf3OrMore = 0;
  for (const difference of differences) {
    if (difference === 0) { identical++; run++; longestRun = Math.max(longestRun, run); }
    else { if (run >= 3) runsOf3OrMore++; run = 0; }
  }
  if (run >= 3) runsOf3OrMore++;
  return JSON.stringify({
    seconds: ${seconds},
    presentedFrames: presented.length,
    presentIntervalMs: { median: at(0.5), p95: at(0.95), max: at(0.9999), over100: intervals.filter((v) => v > 100).length, over300: intervals.filter((v) => v > 300).length },
    identicalToPrevious: { frames: identical, of: differences.length, longestRun, runsOf3OrMore },
  });
})()`;

let exitCode = 0;
try {
  const socket = new WebSocket(await pageSocketUrl());
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let nextId = 0; const pending = new Map();
  socket.onmessage = (event) => { const message = JSON.parse(event.data); if (message.id && pending.has(message.id)) { pending.get(message.id)(message); pending.delete(message.id); } };
  const send = (method, params = {}) => new Promise((resolve) => { const id = ++nextId; pending.set(id, resolve); socket.send(JSON.stringify({ id, method, params })); });

  await send('Page.enable'); await send('Network.enable');
  await send('Network.setExtraHTTPHeaders', { headers: { Authorization: auth } });
  await send('Page.navigate', { url: viewerUrl });
  await sleep(6000);
  const result = await send('Runtime.evaluate', { expression: measure, awaitPromise: true, returnByValue: true });
  console.log(result.result?.result?.value ?? JSON.stringify(result).slice(0, 600));
  socket.close();
} catch (error) {
  console.log(JSON.stringify({ error: String(error) }));
  exitCode = 1;
} finally {
  chrome.kill();
  await sleep(500);
  try { rmSync(profileDir, { recursive: true, force: true }); } catch { /* Chrome がまだ掴んでいる。一時フォルダーなので残っても害は無い */ }
}
process.exit(exitCode);
