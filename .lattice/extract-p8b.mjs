import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest, canonicalizeTodoArtifact } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const actor = { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260821-p8b' };
const recordedAt = new Date().toISOString();

function writeExtraction({ planKey, planFile, tasks, edges, out }) {
  const lines = readFileSync(planFile, 'utf8').split(/\r?\n/);
  const headingLine = (taskId) => {
    const index = lines.findIndex((line) => line === `### ${taskId}`);
    if (index < 0) throw new Error(`missing heading ${taskId} in ${planFile}`);
    return index + 1;
  };
  const ref = (taskId) => ({ project_id: 'OpenLogicool', plan_key: planKey, task_id: taskId });
  const extraction = {
    schema: 'lattice.todo_extraction.v4',
    project_id: 'OpenLogicool',
    plan_key: planKey,
    plan_version: 'v1',
    actor,
    recorded_at: recordedAt,
    tasks: tasks.map(([taskId, title, lane, memo, hRequired]) => ({
      task_id: taskId,
      title,
      lane,
      design_memo: memo,
      narrative_ref: planFile,
      compile_binding: null,
      disposition: 'register_pending',
      start: null,
      completion: null,
      source: {
        origin_plan_ref: planFile,
        origin_line: headingLine(taskId),
        source_commit: commit,
        heading_path: ['Lattice task 仕様（正本は store）', taskId],
        markdown_depth: 3,
        parent_task_id: null,
        checkbox_state: 'absent',
      },
      migration_context: {
        external_canonical_ref: 'docs/development-plan.md',
        carry_over_ref: 'docs/phase8a-exit-assessment.md',
        h_required: Boolean(hRequired),
        condition: null,
        evidence_refs: [],
        notes: [],
      },
    })),
    hard_dependencies: edges.map(([from, to]) => ({ from: ref(from), to: ref(to) }))
      .sort((a, b) => {
        const key = (edge) => `${edge.from.task_id}\0${edge.to.task_id}`;
        return key(a) < key(b) ? -1 : 1;
      }),
    joins: [],
    extraction_digest: '0'.repeat(64),
  };
  extraction.extraction_digest = todoSelfDigest(extraction, 'extraction_digest');
  writeFileSync(out, `${canonicalizeTodoArtifact(extraction)}\n`);
  return { planKey, digest: extraction.extraction_digest, tasks: extraction.tasks.length, edges: extraction.hard_dependencies.length };
}

const result = writeExtraction({
  planKey: 'phase8b-game-operator-dist',
  planFile: 'docs/phase8b-campaign-plan.md',
  out: '.lattice/phase8b-extraction.json',
  tasks: [
    ['t01-go-support-matrix', 'GO support matrix と公開情報', 'A',
      '確認済みだけ Supported。provider は未選定。GamePolicyGate を再実装しない。', false],
    ['t02-schema-rollback', 'Playbook／journal／KP の schema update と rollback', 'A',
      '未知 version は fail。既存 store／validator／journal fold を再実装しない。', false],
    ['t03-active-run-update-hold', 'active Run 中の update 抑止と resume', 'A',
      'InstallLifecycle と Run pin を再実装しない。', false],
    ['t04-capability-release-gates', 'Observe／Teach／Supervised／Verified の release 設定', 'A',
      '各 mode は自分の gate を迂回できない。既存 mode 実装を再実装しない。', false],
    ['t05-restart-ownership-reconcile', '再起動後の ownership reconcile まで dispatch 禁止', 'A',
      'watchdog と AttemptDispatchGate を再実装しない。', false],
    ['t06-input-studio-isolation', 'AI／network 障害でも Input Studio は動く', 'A',
      'fast path に AI を待たせない。既存 architecture 禁止を再実装しない。', false],
    ['t07-data-flow-controls', 'image／cloud／削除／provider／cost の制御口', 'A',
      'provider 未選定の間 cloud は開始しない。diagnostic bundle を再実装しない。', false],
    ['t08-eval-threshold-record', 'eval threshold と dataset／model／prompt／parameter 記録', 'A',
      'EvalHarness を再実装しない。provider を選定しない。', false],
    ['t09-live-verified-session', '実 game Verified の独立 live（H）', 'H',
      '席は取らない。窓が無ければ未確認のまま残す。GameLab Verified を写さない。', true],
    ['t10-phase8b-exit', 'Phase 8B Exit', 'F',
      'full regression 1回、docs/phase8b-exit-assessment.md。親が宣言。H 未確認は未確認のまま。', false],
  ],
  edges: [
    ['t01-go-support-matrix', 't10-phase8b-exit'],
    ['t02-schema-rollback', 't10-phase8b-exit'],
    ['t03-active-run-update-hold', 't10-phase8b-exit'],
    ['t04-capability-release-gates', 't10-phase8b-exit'],
    ['t05-restart-ownership-reconcile', 't10-phase8b-exit'],
    ['t06-input-studio-isolation', 't10-phase8b-exit'],
    ['t07-data-flow-controls', 't10-phase8b-exit'],
    ['t08-eval-threshold-record', 't10-phase8b-exit'],
    ['t09-live-verified-session', 't10-phase8b-exit'],
  ],
});

console.log(JSON.stringify(result, null, 2));
