# Input Studio — Partial LGS Replacement

OpenLogicool Input Studio は、確認済みの G13/G600 入力、application workspace、profile 適用を提供します。公開 claim は **Partial LGS Replacement** です。LGS Parity は名乗りません。

## 確認済み

- reference machine（Windows 11 build 26200 / x64）での G13・G600 入力と profile 適用
- G600 B変種: G9〜G20 の legacy key 配送を F13〜F24 へ remap する無害化
- G600 A方式: Input Studio 管理 slot と素の slot を切り替える補完経路

## 制約と未確認

- G600 onboard profile は F3/F4/F5 の **3 slot** です。F6 は read できず、完全 backup の対象外です。
- G600 の G1〜G5 の mouse TLC 物理配送を完全に抑止する driver route は採用していません。
- LGS script、LCD applet、power mode と、Windows 10 / ARM64 / reference machine 外の GPU 構成は未確認です。これらを Supported と表示しません。
