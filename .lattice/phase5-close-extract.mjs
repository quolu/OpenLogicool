import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest, canonicalizeTodoArtifact } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const planText = readFileSync('docs/phase5-perception-close-campaign-plan.md', 'utf8');
const lines = planText.split(/\r?\n/);
const headingLine = (taskId) => {
  const index = lines.findIndex((line) => line === `### ${taskId}`);
  if (index < 0) throw new Error(`missing heading ${taskId}`);
  return index + 1;
};

const planKey = 'phase5-perception-close';
const ref = (taskId) => ({
  project_id: 'OpenLogicool',
  plan_key: planKey,
  task_id: taskId,
});

const tasks = [
  ['t01-recorded-live-conformance', 'recorded／live を同一 Observe へ（条件1）', 'A',
    'recorded 画素を CapturedFrame にし、live WGC 自前 window も同じ型にする。両方を LiveObservationSource.Observe へ渡す。FakeObservationSource の queue 差し替えを recorded 証明に使わない。t03 の recognizer を使う。'],
  ['t02-frozen-metrics', '事前固定 metric 評価（条件2）', 'A',
    'acceptance だけを読む runner。training に acceptance を載せない。Known 誤判定／Unknown→Known／success FP を事前固定基準（いずれも 0）で判定する。acceptance を見て閾値や recognizer を動かさない。'],
  ['t03-fixture-recognizer', 'fixture 用製品 recognizer', 'A',
    '製品 IFrameRecognizer。fixture／自前 window 状態だけ。未校正・候補なしは Unknown、複数は Ambiguous、契約外は明示エラー。Known へ丸めない。実 game 一般対応を claim しない。'],
  ['t04-continuity-dispatch', 'ContinuityGate を製品 dispatch へ', 'A',
    'CaptureContinuityGate を製品 dispatch が読む。backend change／resize／stale では dispatch しない。静止無 frame では止めない。FastPathPump には載せない。'],
  ['t05-unique-resume-loop', 'LiveResumeGate を製品 dispatch へ', 'A',
    '同じ dispatch 経路が LiveResumeGate を読む。UniqueMatch 以外と window／capture／input 不一致では送らない。自前 window の Windows native。実 NIKKE は H のまま未確認でよい。'],
  ['t06-phase5-exit-reassess', 'Phase 5 Exit の取り直し', 'F',
    'full regression 1回、Grok read-only 監査、docs/phase5-exit-assessment.md を4値で取り直す。親が宣言して閉じる。席は取らない。'],
];

const recordedAt = new Date(Date.now() - 60_000).toISOString();
const extraction = {
  schema: 'lattice.todo_extraction.v4',
  project_id: 'OpenLogicool',
  plan_key: planKey,
  plan_version: 'v1',
  actor: { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260820-p5b' },
  recorded_at: recordedAt.includes('.') ? recordedAt : recordedAt.replace('Z', '.000Z'),
  tasks: tasks.map(([taskId, title, lane, memo]) => ({
    task_id: taskId,
    title,
    lane,
    design_memo: memo,
    narrative_ref: 'docs/phase5-perception-close-campaign-plan.md',
    compile_binding: null,
    disposition: 'register_pending',
    start: null,
    completion: null,
    source: {
      origin_plan_ref: 'docs/phase5-perception-close-campaign-plan.md',
      origin_line: headingLine(taskId),
      source_commit: commit,
      heading_path: ['Lattice task 仕様（正本は store。以下は起票時の作業指定）', taskId],
      markdown_depth: 3,
      parent_task_id: null,
      checkbox_state: 'absent',
    },
    migration_context: {
      external_canonical_ref: 'docs/development-plan.md',
      carry_over_ref: 'docs/phase5-exit-assessment.md',
      h_required: taskId === 't05-unique-resume-loop' || taskId === 't06-phase5-exit-reassess' ? false : false,
      condition: null,
      evidence_refs: [],
      notes: [],
    },
  })),
  hard_dependencies: [
    ['t03-fixture-recognizer', 't01-recorded-live-conformance'],
    ['t03-fixture-recognizer', 't02-frozen-metrics'],
    ['t03-fixture-recognizer', 't05-unique-resume-loop'],
    ['t01-recorded-live-conformance', 't05-unique-resume-loop'],
    ['t04-continuity-dispatch', 't05-unique-resume-loop'],
    ['t01-recorded-live-conformance', 't06-phase5-exit-reassess'],
    ['t02-frozen-metrics', 't06-phase5-exit-reassess'],
    ['t04-continuity-dispatch', 't06-phase5-exit-reassess'],
    ['t05-unique-resume-loop', 't06-phase5-exit-reassess'],
  ].map(([from, to]) => ({ from: ref(from), to: ref(to) }))
    .sort((a, b) => {
      const key = (edge) => `${edge.from.project_id}\0${edge.from.plan_key}\0${edge.from.task_id}\0${edge.to.project_id}\0${edge.to.plan_key}\0${edge.to.task_id}`;
      return key(a) < key(b) ? -1 : 1;
    }),
  joins: [],
  extraction_digest: '0'.repeat(64),
};

extraction.extraction_digest = todoSelfDigest(extraction, 'extraction_digest');
writeFileSync('.lattice/phase5-close-extraction.json', `${canonicalizeTodoArtifact(extraction)}\n`);
console.log(JSON.stringify({
  recorded_at: extraction.recorded_at,
  extraction_digest: extraction.extraction_digest,
  tasks: extraction.tasks.length,
  edges: extraction.hard_dependencies.length,
}, null, 2));
