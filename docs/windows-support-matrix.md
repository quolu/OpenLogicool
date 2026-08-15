# Windows support matrix と reference machine（Phase 0 deliverable / 2026-08-15）

計画 Phase 0「Windows support matrix と reference machine の確定」。support tier は**実測できる環境だけを Supported と表示する**原則（AGENTS.md 裁定1）に従い、現時点で実機がある環境だけを Tier 1 とする。

## 1. Reference machine（実測値）

| 項目 | 値 |
|---|---|
| 機体名 | FOX |
| OS | Windows 11 Pro 10.0.26200（build 26200）、64bit |
| M/B | ASRock X870 Steel Legend WiFi |
| CPU | AMD Ryzen 9 9950X3D（16 core） |
| RAM | 61.6 GB |
| GPU | NVIDIA GeForce RTX 5090（driver 32.0.16.1062）＋ AMD Radeon 内蔵（32.0.21036.18） |
| ディスプレイ | DISPLAY1: 5120×1440 @ (0,0) primary（Acer） / DISPLAY2: 2560×1440 @ (1307,−1440)（MSI）。**multi-monitor・非整列配置・負座標を含む** |
| .NET SDK | 10.0.400 |
| Logitech 常駐 | LCore（LGS 9.04.49）、logi_lamparray_service、LogiRegistryService が稼働中 |
| device | G13（VID_046D PID_C21C）、G600（VID_046D PID_C24A、firmware 7702） |

この機体の性質として記録しておくべき点:

- **primary が超ワイド（5120×1440）で、secondary が負の Y 座標**。仮想スクリーンは 5120×2880 になり、原点が (0,−1440)。座標変換（§6.9）の実装で原点非ゼロを踏むので、この機体は良いテストベッドになる。
- **NVIDIA と AMD の GPU が同居**。Desktop Duplication / WGC がどのアダプタに紐づくかで挙動が変わり得る。capture probe の実測はこの構成で取られている（[probes/capture-backend-matrix-2026-08-15.md](probes/capture-backend-matrix-2026-08-15.md)）。
- **LGS 稼働下での実測**。device 実測はすべて LGS が動いたまま取れている（[probes/g600-input-map-2026-08-15.md](probes/g600-input-map-2026-08-15.md)）。LGS 非稼働時の挙動は未実測（同文書の残ギャップ）。

## 2. Support tier

| Tier | 定義 | 現在の該当 |
|---|---|---|
| **Tier 1（Supported）** | reference machine と同一 OS 系列で、全 release gate の受入を実機で通した環境 | Windows 11 build 26200 / x64 のみ |
| **Tier 2（強い推定・未実測）** | API 契約上は同等だが本製品での実測がない環境 | Windows 11 の他 build、Windows 10 22H2（WGC は利用可能だが未実測）、Intel GPU 環境、単一モニタ環境 |
| **Tier 3（非対応）** | 技術・方針で対応しないと裁定した環境 | Windows 10 21H2 以前、x86（32bit）、ARM64（未評価につき当面）、Windows Server、仮想化された GPU なし環境 |

Tier 2 を Supported として表示しない。Tier 2 環境からの不具合報告は受け付けるが、対応可否は都度判断する。

## 3. 環境軸ごとの現状（CAP-005 support matrix の骨格）

| 軸 | 実測済み | 未実測（Phase 4 の release gate 材料） |
|---|---|---|
| display mode | windowed（メモ帳）| borderless、fullscreen（排他） |
| multi-monitor | 2枚・非整列・負座標で capture 成立 | モニタ跨ぎ window、capture 中の display 追加/削除 |
| DPI | 96（システム既定）| 高 DPI、混在 DPI、実行中の DPI 変更 |
| HDR | 無効 | 有効時の pixel format と色空間 |
| 最小化／遮蔽 | 最小化の失敗系を実測（frame 供給停止＋サイズ急変）| 他 window による遮蔽、仮想デスクトップ切替 |
| GPU | NVIDIA + AMD 同居 | Intel、GPU 単一、driver 更新中の device lost |

## 4. 追加 reference machine の必要性

Tier 1 が1台では、**この機体固有の性質と製品の一般的挙動を区別できない**。特に区別が付いていないもの:

- 超ワイド＋負座標という座標系が、実装の暗黙の前提になっていないか
- 2 GPU 同居が capture backend 選択に効いているか

Phase 4（capture の release gate）までに、**単一モニタ・単一 GPU・標準解像度**の2台目を用意することを推奨する。用意できない場合は、その旨を support matrix に明記して Tier 1 を1構成に限定する（実測なき一般化をしない）。
