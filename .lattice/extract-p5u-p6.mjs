import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest, canonicalizeTodoArtifact } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const actor = { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260820-p6' };
const recordedAt = new Date(Date.now() - 60_000).toISOString();

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
    recorded_at: recordedAt.includes('.') ? recordedAt : recordedAt.replace('Z', '.000Z'),
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
        carry_over_ref: planKey === 'phase5-unverified' ? 'docs/phase5-exit-assessment.md' : 'docs/phase5-exit-assessment.md',
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

const r1 = writeExtraction({
  planKey: 'phase5-unverified',
  planFile: 'docs/phase5-unverified-campaign-plan.md',
  out: '.lattice/phase5-unverified-extraction.json',
  tasks: [
    ['t01-resident-dispatch-loop', 'Host resident／CLI が ContinuityDispatch を駆動', 'A',
      'Host の resident または CLI が CaptureContinuityDispatch を駆動する。test 専用呼び出しを製品経路と数えない。FastPathPump には載せない。', false],
    ['t02-png-corpus-metrics', 'tracked PNG を metric に通す', 'A',
      'FrozenMetricRunner に fixtures/frames の acceptance PNG を通す。合成 BGRA8 の数値を PNG 数値と偽らない。3指標を証跡へ残す。', false],
    ['t03-catalog-live-match', '事前登録カタログと live frame を照合', 'A',
      '事前登録 state と自前 window の live WGC を照合する。live frame 自身の SHA から rule を作る自己照合は禁止。', false],
    ['t04-nikke-live-unique', '実 NIKKE UniqueMatch（H）', 'H',
      '実 NIKKE 窓で UniqueMatch 再開を実測する。席は取らない。窓が無ければ未確認のまま残す。', true],
    ['t05-support-matrix-live', 'matrix live 行の実測（H）', 'H',
      'borderless／fullscreen／DPI／HDR／multi-monitor／遮蔽のうち用意できる条件だけ実測。席は取らない。', true],
    ['t06-unverified-assessment', '未確認の4値 assessment', 'F',
      'docs/phase5-unverified-assessment.md。親が閉じる。席は取らない。', false],
  ],
  edges: [
    ['t01-resident-dispatch-loop', 't06-unverified-assessment'],
    ['t02-png-corpus-metrics', 't06-unverified-assessment'],
    ['t03-catalog-live-match', 't06-unverified-assessment'],
    ['t04-nikke-live-unique', 't06-unverified-assessment'],
    ['t05-support-matrix-live', 't06-unverified-assessment'],
  ],
});

const r2 = writeExtraction({
  planKey: 'phase6-ai-teach',
  planFile: 'docs/phase6-campaign-plan.md',
  out: '.lattice/phase6-extraction.json',
  tasks: [
    ['t01-planner-proposal-schema', 'PlannerContext／Proposal schema', 'A',
      'PlannerContext と NextActionProposal の契約。未知 schema version 拒否。focused conformance。', false],
    ['t02-ai-isolation', 'AI が input／DB／device に届かない', 'A',
      'AI プロジェクトが Input／Devices／Persistence／Capture を参照しない。architecture test。AI-002。', false],
    ['t03-proposal-reject', '不正 proposal を dispatch 前拒否', 'A',
      'schema 外、catalog 外、state 不一致、risk 不一致を拒否。InputEmitter を呼ばない。', false],
    ['t04-exp-ai-01-harness', 'EXP-AI-01 harness（未選定）', 'A',
      'frozen corpus で精度、unknown、latency、cost、cancel を測る口。provider を選定しない。acceptance を prompt 調整へ渡せない。', false],
    ['t05-observe-only', 'Observe Only', 'A',
      'proposal を出しても dispatch しない。Playbook を書き換えない。', false],
    ['t06-teach-supervised', 'Teach／Supervised 口', 'A',
      '一手承認前に SendInput しない。本番 provider を埋め込まない。fake で口を閉じる。', false],
    ['t07-verified-env-scope', 'GameLab Verified は実 game へ継承しない', 'A',
      'environment scope。実 game へ継承しない focused test。', false],
    ['t08-phase6-exit', 'Phase 6 Exit', 'F',
      'full regression 1回、docs/phase6-exit-assessment.md。親が宣言。provider 未選定を維持。席は取らない。', false],
  ],
  edges: [
    ['t01-planner-proposal-schema', 't03-proposal-reject'],
    ['t01-planner-proposal-schema', 't04-exp-ai-01-harness'],
    ['t01-planner-proposal-schema', 't05-observe-only'],
    ['t02-ai-isolation', 't05-observe-only'],
    ['t03-proposal-reject', 't06-teach-supervised'],
    ['t04-exp-ai-01-harness', 't06-teach-supervised'],
    ['t05-observe-only', 't06-teach-supervised'],
    ['t06-teach-supervised', 't07-verified-env-scope'],
    ['t01-planner-proposal-schema', 't08-phase6-exit'],
    ['t02-ai-isolation', 't08-phase6-exit'],
    ['t03-proposal-reject', 't08-phase6-exit'],
    ['t04-exp-ai-01-harness', 't08-phase6-exit'],
    ['t05-observe-only', 't08-phase6-exit'],
    ['t06-teach-supervised', 't08-phase6-exit'],
    ['t07-verified-env-scope', 't08-phase6-exit'],
  ],
});

console.log(JSON.stringify({ r1, r2 }, null, 2));
