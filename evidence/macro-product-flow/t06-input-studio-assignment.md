# t06 Input Studioマクロ割当

- 既存Input Studioの上部、左操作一覧、中央G13/G600図、右Inspector、保存欄は維持した。
- 右Inspectorの既存「録って追加／更新」の直下へ「マクロを選ぶ」入口だけを追加した。
- 選択したmacroは既存Semantic Actionの`Outputs`へ単独tokenとして保存し、既存のG13/G600 bindingでbuttonへ割り当てる。
- tokenはroute最新版を追従し、AI修復で新revisionができてもbutton設定の作り直しを不要にした。AI監視あり／なしは割当ごとに保持する。
- focused test: Desktopのmacro assignment/workspace/projection 17件、Hostのworkspace保存＋catalog 2件 green（2026-08-26）。
