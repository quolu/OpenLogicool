import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest, canonicalizeTodoArtifact } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const planText = readFileSync('docs/phase5-campaign-plan.md', 'utf8');
const lines = planText.split(/\r?\n/);
const headingLine = (taskId) => {
  const index = lines.findIndex((line) => line === `### ${taskId}`);
  if (index < 0) throw new Error(`missing heading ${taskId}`);
  return index + 1;
};

const ref = (taskId) => ({
  project_id: 'OpenLogicool',
  plan_key: 'phase5-capture-perception',
  task_id: taskId,
});

const tasks = [
  ['t01-wgc-frame', 'WGC 第一 backend の製品 Frame（CAP-001）', 'A',
    'WGC window から sequence・時刻・size・pixel format・color space・DPI・rotation・crop 付き Frame を供給する。Phase 0 probe の確認済み経路。fallback しない。'],
  ['t02-capability-matrix', 'capture support matrix（CAP-004/005）', 'A',
    'windowed／borderless／fullscreen、DPI、HDR、multi-monitor、遮蔽、最小化。失敗理由を記録。未確認を Supported と書かない。'],
  ['t03-alt-backends', 'Duplication／可視領域の明示選択（CAP-004）', 'A',
    '別 backend として明示切替。黙った fallback 禁止。t02 の採否に従い、不採用なら非対応表示で実装しない。'],
  ['t04-frame-transform', 'resize／DPI／HDR の transform revision（§6.9）', 'A',
    'source→content→normalized→client→input。変更で revision 更新し古い locator を無効化。'],
  ['t05-capture-faults', 'stale／最小化／backend change で入力停止（CAP-002/003）', 'A',
    '状態を別分類。静止の無 frame は失敗にしない。backend／resize／stale では自動入力を止める。'],
  ['t06-live-observation', 'recorded／live の Observation 4状態（PER-001〜004）', 'A',
    'Known／Ambiguous／Unknown／Unavailable。Perception は Attempt を知らない。Known 以外を自動実行条件にしない。'],
  ['t07-knowledge-pack', 'Knowledge Pack schema（KP-001〜004）', 'A',
    'schema・検証状態・出典。実行 code／script／秘密を含めない。import は Untrusted／Candidate。Screen Graph は独立 version。'],
  ['t08-corpus-split', 'development／calibration／acceptance corpus 分離', 'A',
    'NIKKE 等の探索 frame を experiment artifact 化。acceptance を過学習に使わない口。'],
  ['t09-unique-resume', '実画面 UniqueMatch だけの resume（PER-005）', 'A',
    'Phase 4 ResumeGate へ実画面 Observation を供給。不一致 window／source／target は dispatch 前停止。'],
  ['t10-failure-ux', 'capture／認識失敗 UX（PER-006）', 'A',
    '失敗を明示。一つの実 game 成功を一般対応と表示しない。絶対座標 step は fragile。'],
  ['t11-phase5-exit', 'Phase 5 exit assessment', 'F',
    'full regression 1回、Grok read-only 監査、docs/phase5-exit-assessment.md を Exit 条件×4値。親が宣言して閉じる。オーナー承認待ちで止めない。'],
];

const extraction = {
  schema: 'lattice.todo_extraction.v4',
  project_id: 'OpenLogicool',
  plan_key: 'phase5-capture-perception',
  plan_version: 'v1',
  actor: { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260820-p5' },
  recorded_at: new Date(Date.now() - 60_000).toISOString().replace(/Z$/, '.000Z').replace(/\.\d{3}\.000Z$/, '.000Z'),
  tasks: tasks.map(([taskId, title, lane, memo]) => ({
    task_id: taskId,
    title,
    lane,
    design_memo: memo,
    narrative_ref: 'docs/phase5-campaign-plan.md',
    compile_binding: null,
    disposition: 'register_pending',
    start: null,
    completion: null,
    source: {
      origin_plan_ref: 'docs/phase5-campaign-plan.md',
      origin_line: headingLine(taskId),
      source_commit: commit,
      heading_path: ['Lattice task 仕様（正本は store。以下は起票時の作業指定）', taskId],
      markdown_depth: 3,
      parent_task_id: null,
      checkbox_state: 'absent',
    },
    migration_context: {
      external_canonical_ref: 'docs/development-plan.md',
      carry_over_ref: null,
      h_required: false,
      condition: null,
      evidence_refs: [],
      notes: [],
    },
  })),
  hard_dependencies: [
    ['t01-wgc-frame', 't02-capability-matrix'],
    ['t01-wgc-frame', 't04-frame-transform'],
    ['t01-wgc-frame', 't05-capture-faults'],
    ['t01-wgc-frame', 't06-live-observation'],
    ['t02-capability-matrix', 't03-alt-backends'],
    ['t05-capture-faults', 't10-failure-ux'],
    ['t06-live-observation', 't08-corpus-split'],
    ['t06-live-observation', 't09-unique-resume'],
    ['t06-live-observation', 't10-failure-ux'],
    ['t03-alt-backends', 't11-phase5-exit'],
    ['t04-frame-transform', 't11-phase5-exit'],
    ['t07-knowledge-pack', 't11-phase5-exit'],
    ['t08-corpus-split', 't11-phase5-exit'],
    ['t09-unique-resume', 't11-phase5-exit'],
    ['t10-failure-ux', 't11-phase5-exit'],
  ].map(([from, to]) => ({ from: ref(from), to: ref(to) }))
    .sort((a, b) => {
      const key = (edge) => `${edge.from.project_id}\0${edge.from.plan_key}\0${edge.from.task_id}\0${edge.to.project_id}\0${edge.to.plan_key}\0${edge.to.task_id}`;
      return key(a) < key(b) ? -1 : 1;
    }),
  joins: [],
  extraction_digest: '0'.repeat(64),
};

const ts = extraction.recorded_at;
extraction.recorded_at = ts.includes('.') ? ts : ts.replace('Z', '.000Z');
if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(extraction.recorded_at)) {
  extraction.recorded_at = new Date(Date.now() - 60_000).toISOString();
  if (!extraction.recorded_at.includes('.')) extraction.recorded_at = extraction.recorded_at.replace('Z', '.000Z');
}

extraction.extraction_digest = todoSelfDigest(extraction, 'extraction_digest');
writeFileSync('.lattice/phase5-extraction.json', `${canonicalizeTodoArtifact(extraction)}\n`);
console.log(JSON.stringify({
  recorded_at: extraction.recorded_at,
  extraction_digest: extraction.extraction_digest,
  tasks: extraction.tasks.length,
  edges: extraction.hard_dependencies.length,
}, null, 2));
