import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const planText = readFileSync('docs/phase4-campaign-plan.md', 'utf8');
const lines = planText.split(/\r?\n/);
const headingLine = (taskId) => {
  const index = lines.findIndex((line) => line === `### ${taskId}`);
  if (index < 0) throw new Error(`missing heading ${taskId}`);
  return index + 1;
};

const ref = (taskId) => ({
  project_id: 'OpenLogicool',
  plan_key: 'phase4-durable-lab',
  task_id: taskId,
});

const tasks = [
  ['t01-data-flow-contract', 'Data Flow Contract（§6.12）を docs へ置く', 'F',
    'journal 永続化より先に、frame/crop/OCR/path/prompt/journal/device/crash/bundle の生成元・保存先・送信先・retention・削除経路を文書化する。実装は書かない。'],
  ['t02-playbook-graph', 'Playbook graph と immutable version（PB-001/002/008）', 'A',
    '前提・状態・Semantic Action・期待結果・分岐を graph として保存する。Run 開始時に version pin。訂正は新 version。確定済み event は変更しない。'],
  ['t03-journal', 'append-only journal と projection（PB-006、OPS-008/009）', 'A',
    '観測から手動介入までを append-only event として保存する。journal と engineering log を分離し correlation ID で一遷移を追跡する。t01 の retention に従う。'],
  ['t04-attempt-sm', 'Attempt 状態機と DispatchArmed（PB-003/004/005）', 'A',
    '外部入力の前に Attempt と DispatchArmed を commit する。未解決は OutcomeUnknown。Windows 入力と SQLite を一つの transaction にしない。'],
  ['t05-run-controls', 'pause / step / skip / abandon / 手動介入（PB-007/013）', 'A',
    'Run 制御と version switch。同じ Semantic Action の物理入力は manual intervention として止め、Run 進行へ自動合流しない。'],
  ['t06-fake-observation', 'fake Observation 4状態と Confirmed 契約', 'A',
    'Unique / Ambiguous / Unknown / Unavailable。Confirmed には同じ Attempt を参照する Observation が必須。実画面は使わない。'],
  ['t07-fault-matrix', 'fault injection と未解決 DispatchArmed 禁止（NFR-012）', 'A',
    'crash / handled stop / window 喪失 / 部分 SendInput で次 dispatch を自動生成しない。保証できる中止だけ Disarmed。'],
  ['t08-gamelab-oracle', 'GameLab oracle 面（APP-010、UX-003〜005）', 'A',
    'Playbook と実行履歴の編集・閲覧。状態を常時表示。停止は AI / capture / device に依存しない。現在 state は oracle / fake Observation だけ。'],
  ['t09-recorder-replay', 'session recorder / replayer と replay 一致', 'A',
    'journal replay と projection が一致する。active Run の version が replay や crash 復元で勝手に変わらない。'],
  ['t10-resume-ux', 'UniqueMatch 以外の自動再開禁止（PB-009、UX-005）', 'A',
    '再開前に対象 app・version・現在 Observation を照合する。manual intervention 後は再観察なしに進まない。実画面 UniqueMatch は対象外。'],
  ['t11-phase4-exit', 'Phase 4 exit assessment とオーナー裁定', 'F',
    'full regression 1回、Grok read-only 監査、docs/phase4-exit-assessment.md を Exit 条件×4値で作成する。Exit 宣言はオーナー裁定。'],
];

const extraction = {
  schema: 'lattice.todo_extraction.v4',
  project_id: 'OpenLogicool',
  plan_key: 'phase4-durable-lab',
  plan_version: 'v1',
  actor: { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260819-p4' },
  recorded_at: new Date().toISOString(),
  tasks: tasks.map(([taskId, title, lane, memo]) => ({
    task_id: taskId,
    title,
    lane,
    design_memo: memo,
    narrative_ref: 'docs/phase4-campaign-plan.md',
    compile_binding: null,
    disposition: 'register_pending',
    start: null,
    completion: null,
    source: {
      origin_plan_ref: 'docs/phase4-campaign-plan.md',
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
      h_required: taskId === 't11-phase4-exit',
      condition: null,
      evidence_refs: [],
      notes: [],
    },
  })),
  hard_dependencies: [
    ['t01-data-flow-contract', 't03-journal'],
    ['t02-playbook-graph', 't04-attempt-sm'],
    ['t04-attempt-sm', 't05-run-controls'],
    ['t04-attempt-sm', 't06-fake-observation'],
    ['t04-attempt-sm', 't07-fault-matrix'],
    ['t05-run-controls', 't07-fault-matrix'],
    ['t06-fake-observation', 't07-fault-matrix'],
    ['t06-fake-observation', 't08-gamelab-oracle'],
    ['t06-fake-observation', 't10-resume-ux'],
    ['t03-journal', 't09-recorder-replay'],
    ['t07-fault-matrix', 't11-phase4-exit'],
    ['t08-gamelab-oracle', 't11-phase4-exit'],
    ['t09-recorder-replay', 't11-phase4-exit'],
    ['t10-resume-ux', 't11-phase4-exit'],
  ].map(([from, to]) => ({ from: ref(from), to: ref(to) }))
    .sort((a, b) => {
      const key = (edge) => `${edge.from.project_id}\0${edge.from.plan_key}\0${edge.from.task_id}\0${edge.to.project_id}\0${edge.to.plan_key}\0${edge.to.task_id}`;
      return key(a) < key(b) ? -1 : 1;
    }),
  joins: [],
  extraction_digest: '0'.repeat(64),
};

extraction.extraction_digest = todoSelfDigest(extraction, 'extraction_digest');

writeFileSync('.lattice/phase4-extraction.json', `${JSON.stringify(extraction)}\n`);
console.log(JSON.stringify({
  recorded_at: extraction.recorded_at,
  extraction_digest: extraction.extraction_digest,
  tasks: extraction.tasks.length,
  edges: extraction.hard_dependencies.length,
  lines: Object.fromEntries(tasks.map(([id]) => [id, headingLine(id)])),
}, null, 2));
