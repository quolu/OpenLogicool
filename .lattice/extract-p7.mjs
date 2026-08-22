import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest, canonicalizeTodoArtifact } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const actor = { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260821-p7' };
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
        carry_over_ref: 'docs/phase6-exit-assessment.md',
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
  planKey: 'phase7-daily-pilot',
  planFile: 'docs/phase7-campaign-plan.md',
  out: '.lattice/phase7-extraction.json',
  tasks: [
    ['t01-two-cycle-not-verified', '2 cycle。初日成功は Verified にしない', 'A',
      'GameLab で virtual day を2回回す。day1 の成功は Verified にしない。day2 相当の別 session で known path を replay する。既存 daily reset を再実装しない。', false],
    ['t02-unknown-branch-append', '未知 branch を verified path を壊さず追記', 'A',
      '未知 branch を追記する。旧 verified Version は書き換えない。PlaybookCorrection の新 Version だけが未知を持つ。', false],
    ['t03-game-policy-gate', 'Game Policy Record の mode gate', 'A',
      '未確認は automation disabled。Observe／Assist／Auto を mode 別に許可。SendInput 受理を規約許可の証拠にしない。import は迂回できない。実 ToS 解釈はしない。', false],
    ['t04-shadow-compare', '操作と proposal の shadow 比較', 'A',
      'dispatch しない。SendInput しない。fake planner で閉じる。本番 provider を埋め込まない。', false],
    ['t05-daily-recovery', 'daily cycle からの復帰', 'A',
      '途中停止、manual intervention、foreground 喪失、capture loss、OutcomeUnknown から復帰する。既存 fault／resume 口を再実装しない。', false],
    ['t06-real-observe', '実 game Observe Only（H）', 'H',
      '実 game の Observe Only。席は取らない。窓が無ければ未確認のまま残す。一般対応と書かない。', true],
    ['t07-phase7-exit', 'Phase 7 Exit', 'F',
      'full regression 1回、docs/phase7-exit-assessment.md。親が宣言。席は取らない。H 未確認は未確認のまま書く。', false],
  ],
  edges: [
    ['t01-two-cycle-not-verified', 't04-shadow-compare'],
    ['t01-two-cycle-not-verified', 't05-daily-recovery'],
    ['t01-two-cycle-not-verified', 't07-phase7-exit'],
    ['t02-unknown-branch-append', 't07-phase7-exit'],
    ['t03-game-policy-gate', 't07-phase7-exit'],
    ['t04-shadow-compare', 't07-phase7-exit'],
    ['t05-daily-recovery', 't07-phase7-exit'],
    ['t06-real-observe', 't07-phase7-exit'],
  ],
});

console.log(JSON.stringify(result, null, 2));
