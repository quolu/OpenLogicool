# G600 方式A／B／C read-only 判定（Deliverable 0B / 2026-08-15）

G0-Device-RO の受入「read-only で棄却できるものを棄却し、残候補と必要な write 実験を列挙」に対する判定。根拠はすべて 2026-08-15 の実測（[probes/g600-input-map-2026-08-15.md](probes/g600-input-map-2026-08-15.md)、[probes/g600-profile-decode-2026-08-15.md](probes/g600-profile-decode-2026-08-15.md)）。

## 1. 実測が変えた前提

計画冒頭の未成立事項「ユーザーモードだけで G600 の全ボタンを無制限のアプリプロファイルへ割り当て、元入力を重複させない」は、実測により2つの独立した問題へ分解された。

1. **入力識別（確認済み・方式不要）**: raw report 0x80 が全20 control＋G-Shift 層を割当非依存・user-mode・LGS 稼働下で配送する。無制限のアプリプロファイルは「raw route で読む → Mapping Runtime → Input Emitter で出す」の user-mode 経路だけで成立し、**onboard profile 数（3）はアプリプロファイル数の上限にならない**。方式A/B/C のどれも、入力識別のためには不要になった。
2. **元入力の抑止（未解決・方式選定の唯一の争点）**: 同じ押下が legacy 経路でも届く（mouse TLC のクリック/チルト/ホイール、LGS 仮想キーボードの割当キー）。foreground アプリへこの重複が漏れることの抑止だけが、方式A/B/C が担う責務として残る。

## 2. 方式別判定

| 方式 | read-only 判定 | 根拠 |
|---|---|---|
| A: onboard 3 profile 直接利用 | **残候補**（棄却せず） | F0〜F5 read と layout 解読は成立。active slot 切替（F0 write）の成立可否は write を要し read-only では判定不能。役割は「識別」から「legacy 配送の切替・無害化」へ縮小 |
| B: 中間 usage の user-mode 変換 | **変種を分割**: 「識別のための中間 usage」は**棄却**。「legacy 無害化のための中間 usage」は残候補 | 識別は raw route が既に確認済みで担うため、識別目的の onboard 書換えは目的が消滅した。一方、side ボタンの割当を無衝突 usage（F13〜F24 等）へ書き換えて legacy 配送を無害化する用途は残る。成立判定には 154-byte write（EXP-G600-02 write 拡張）が必要 |
| C: 署名済み driver での物理入力抑止 | **残候補**（最後の手段のまま） | read-only では判定材料が増えない。ただし必要性は「クリック等の mouse TLC 配送を完全抑止したい場合」だけへ狭まった。user-mode release と driver release の scope 分離（計画 No-Go）を維持 |

read-only で完全棄却できた方式は無い。棄却できたのは方式Bの識別変種だけである。これは「判定不能」ではなく、**争点が legacy 抑止だけへ縮小したという判定**である。

## 3. 残 write 実験（G0-Device-W で実施、この順を推奨）

1. **EXP-MIG-01**（backup・無変更 write-back・最小変更 apply・restore）: 手順は [migration-safety-gate.md](migration-safety-gate.md)。全 write 実験の前提。
2. **EXP-G600-03**（F0 active slot 切替）: 方式Aの成立判定。切替が即時か・入力欠落が出るか・LGS 稼働下で巻き戻されるかを観測する。
3. **EXP-G600-02 write 拡張**（side 割当を中間 usage へ書換え）: 方式B残存変種の成立判定。書換え後に (a) legacy keyboard 配送が中間 usage へ変わるか (b) raw route が影響を受けないか (c) LGS が割当を巻き戻すか、を観測する。

## 4. read-only で残った観測ギャップ（write 不要・機会があれば埋める）

- **LGS 非稼働時の legacy 配送**: 今日の実測はすべて LGS 稼働下。LGS 停止時に side ボタンが G600 自身の keyboard TLC (MI_01/COL01) から onboard 割当キーとして届くか（強い推定）は未実測。方式選定の前提には影響しないが、Input Studio の「LGS 無し環境」受入で必要になる。
- F0 report の byte 意味（active slot の読み出し表現）。0xF0=2B の解釈は未確定で、EXP-G600-03 の設計時に二度読みと切替前後比較で確定する。
