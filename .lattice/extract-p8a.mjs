import { readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const { todoSelfDigest, canonicalizeTodoArtifact } = await import(pathToFileURL(
  `${process.env.USERPROFILE}\\AppData\\Roaming\\npm\\node_modules\\@quolu\\lattice\\src\\todo-contracts.mjs`,
).href);

const commit = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const actor = { agent: 'bell-grok46', host: 'kite-win11', session: 'sess-20260821-p8a' };
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
        carry_over_ref: 'docs/phase7-exit-assessment.md',
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
  planKey: 'phase8a-input-studio-dist',
  planFile: 'docs/phase8a-campaign-plan.md',
  out: '.lattice/phase8a-extraction.json',
  tasks: [
    ['t01-support-matrix-claim', 'support matrix と Partial LGS Replacement', 'A',
      '確認済みだけ Supported。未確認を Supported にしない。LGS Parity を名乗らない。G600 route と 3-slot 制約を matrix と release note へ出す。', false],
    ['t02-lgs-import-dry-run', 'LGS XML import dry-run', 'A',
      '変換可能行と未対応行を分けて表示する。元設定・device を変更しない。script と path を命令として扱わない。original=true は取り込まない。fixture XML。', false],
    ['t03-timed-macro', 'delay／repeat／toggle の明示状態', 'A',
      '停止境界を破らない。既存 Tap sequence を再実装しない。混在不正は profile 適用時に拒否。', false],
    ['t04-lgs-restore-rollback', 'migration cancel と device restore', 'A',
      'dry-run 後に apply しない経路と leftover restore。元 profile を破壊しない。write 作法を再実装しない。', false],
    ['t05-diagnostic-bundle', 'secret を含まない diagnostic bundle', 'A',
      'screen、secret、personal data を入れない。preview と削除がある。既存診断口を再実装しない。', false],
    ['t06-packaging-identity', 'package identity と配布レイアウト', 'A',
      'autostart、update manifest、unpackaged レイアウト。MSIX／Sparse／MSI の採否を実測で記録。未実測を Supported にしない。install 中に device write しない。', false],
    ['t07-sbom-notices', 'SBOM と Third-Party Notices と hash', 'A',
      '署名はしない。t06 の成果物へ同梱する口。', false],
    ['t08-install-lifecycle', 'install／update／rollback／repair／uninstall の口', 'A',
      'focused で契約を固定する。LGS 復帰は leftover restore。', false],
    ['t09-authenticode', 'Authenticode 署名（H）', 'H',
      '席は取らない。証明書が無ければ未確認のまま残す。自己署名を Supported と表示しない。', true],
    ['t10-phase8a-exit', 'Phase 8A Exit', 'F',
      'full regression 1回、docs/phase8a-exit-assessment.md。親が宣言。席は取らない。H 未確認は未確認のまま書く。Public Gate を未確認行で成立扱いにしない。', false],
  ],
  edges: [
    ['t02-lgs-import-dry-run', 't04-lgs-restore-rollback'],
    ['t06-packaging-identity', 't07-sbom-notices'],
    ['t06-packaging-identity', 't08-install-lifecycle'],
    ['t06-packaging-identity', 't09-authenticode'],
    ['t01-support-matrix-claim', 't10-phase8a-exit'],
    ['t02-lgs-import-dry-run', 't10-phase8a-exit'],
    ['t03-timed-macro', 't10-phase8a-exit'],
    ['t04-lgs-restore-rollback', 't10-phase8a-exit'],
    ['t05-diagnostic-bundle', 't10-phase8a-exit'],
    ['t06-packaging-identity', 't10-phase8a-exit'],
    ['t07-sbom-notices', 't10-phase8a-exit'],
    ['t08-install-lifecycle', 't10-phase8a-exit'],
    ['t09-authenticode', 't10-phase8a-exit'],
  ],
});

console.log(JSON.stringify(result, null, 2));
