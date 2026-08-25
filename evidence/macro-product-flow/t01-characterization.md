# t01 既存UIと通常bindingの不変契約

判定日: 2026-08-26

## 固定する現行構造

- Input Studioは既存の上部bar、左の操作一覧、中央のG13／G600 tab＋device模式図、右の割当Inspector、下部保存領域を維持する。
- 通常actionは`WorkspaceActionEntry.Outputs`から`WorkspaceCompiler`を通り、device別`MappingProfile`へcompileされる。
- 物理inputは`DeviceMappingRuntime`で通常output edgeへ変換され、`FastPathPump`から既存emitterへ送られる。
- Game Operatorは既存`GameOperatorWindow`内のTabControlを維持し、新機能は同じWindowへtab追加する。

## baseline

- Desktop 92件green。
- Profiles 25件green。
- Input 151件green。
- Host 212件green。
- Playbooks 154件green。
- 合計634件、失敗0、skip 0。

## 受入

macro追加後も通常actionのWorkspaceDocument、MappingProfile、mapped output edge、emitter入力を変えない回帰testを保持する。Input Studio全体の再設計、新しいWindow、ViewModel／DI framework移行は行わない。

## 統括記録

多段受入のためorchestrate Control initを試行したが、既存の未追跡probe資産を含むworkspace fingerprintが64MiB上限を超えてfail closedした。資産を削除・ignore化せず、既存Lattice plan `macro-product-flow`と単一親直列を工程正本とする。writer委譲は行わず、終端の契約反証だけ円卓を使う。
