# Primary-source manifest

- 取得日: 2026-08-14
- 確度: 高（各リンク先の公式文書または公開リポジトリを直接確認）
- 保存方針: 著作権・ライセンス境界を明確にするため、外部ページ全文の複製ではなく取得URLと調査用途を台帳化した。実装時はリンク先の固定commitを再取得し、採用コードに対応するライセンス表示を同梱する。

## デバイス固有資産

- [dmicsa/logitech-g13-ahk-bind](https://github.com/dmicsa/logitech-g13-ahk-bind) — Windows Raw InputによるG13入力の実例。MIT。
- [G13Bind.ahk](https://raw.githubusercontent.com/dmicsa/logitech-g13-ahk-bind/master/G13Bind.ahk) — Usage Page `0xFF00`、8バイト入力レポート、キー／スティックの配置。
- [khampf/g13](https://github.com/khampf/g13) — G13の入力、バックライト、M LED、LCD通信の参考実装。リポジトリに明示ライセンスなし。コードは再利用しない。
- [khampf/g13 g13_device.cpp](https://raw.githubusercontent.com/khampf/g13/master/g13_device.cpp) — 入力エンドポイント、M LED、RGB制御の通信例。
- [khampf/g13 g13_lcd.cpp](https://raw.githubusercontent.com/khampf/g13/master/g13_lcd.cpp) — 160×43 LCDの960バイトフレームと転送例。
- [libratbag G600 driver](https://raw.githubusercontent.com/libratbag/libratbag/master/src/driver-logitech-g600.c) — G600オンボードプロファイルの完全なレポート構造。ファイル内MIT。
- [libratbag G600 device entry](https://raw.githubusercontent.com/libratbag/libratbag/master/data/devices/logitech-g600.device) — `046d:c24a`と専用driverの対応。
- [ecerulm/python-hidapi-logitech-g600](https://github.com/ecerulm/python-hidapi-logitech-g600) — hidapiによるG600 Feature Report読み書き例。明示ライセンスなし。コードは再利用しない。
- [OpenRGB G600 support request](https://gitlab.com/CalcProgrammer1/OpenRGB/-/issues/920) — G600はサポート実装ではなく追加要望の状態。
- [Solaar G600 support request](https://github.com/pwr-Solaar/Solaar/issues/1721) — USB descriptor情報はあるが完成した対応ではない。

## Windows公式資料

- [Raw Input overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input)
- [RegisterRawInputDevices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices)
- [RAWINPUTDEVICE flags](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)
- [Using Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-raw-input)
- [Obtaining HID reports](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
- [HidD_GetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getfeature)
- [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [KBDLLHOOKSTRUCT injected-input flag](https://learn.microsoft.com/ja-jp/windows/win32/api/winuser/ns-winuser-kbdllhookstruct)
- [Virtual HID Framework](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)
- [Keyboard filter sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/keyboard-input-wdf-filter-driver-kbfiltr/)
- [Driver signing options](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings)
- [Dynamic Lighting / LampArray sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-universal-samples/lamparray/)
- [Dynamic Lighting devices](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/dynamic-lighting-devices)

## 計画改訂時に追加確認した公式資料

- 取得日: 2026-08-15
- [.NET 10 overview](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview) — .NET 10がLTSであり、WPFを含むWindows Desktopを提供することの確認。
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) — runtime support期間をsupport matrixへ反映するための正本。
- [Windows screen capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture) — Windows.Graphics.Captureのwindow／display capture、support判定、picker、frame処理。
- [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api) — monitor capture backend、rotation、cursor、desktop更新処理の公式契約。
- [Dynamic Lighting](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/lighting-dynamic-lamparray) — LampArrayのbackground control、利用者priority、package identity、AppExtension要件。
- [Handling passwords](https://learn.microsoft.com/en-us/windows/win32/secbp/handling-passwords) — Windowsで秘密を扱う際の公式guidance。
- [CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata) — CurrentUser scopeを含むDPAPI候補の確認。
- [MSIX deployment planning](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-targetdevices) — install、update、app data、deploymentを計画するための公式資料。
- [Authenticode timestamping](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures) — 公開artifactの署名とtimestamp gate。
- [Windows lifecycle FAQ](https://learn.microsoft.com/en-us/lifecycle/faq/windows) — Windows 10を新規製品のSupported対象へ含めるか裁定するための公式life-cycle資料。

## メーカー資料・法務資料

- [Logitech Gaming Software 9.04.49](https://support.logi.com/hc/en-ca/articles/6330888992023-Logitech-Gaming-Software) — 最終更新2022-05-25、Windows 10/11 HVCI対応。
- [G600 on-board memory / application detection](https://support.logi.com/hc/en-ca/articles/360023411353) — 3プロファイルとアプリ検出の公式説明。
- [LGS guide](https://www.logitech.com/assets/51813/26/lgs-guide.pdf) — G13/G600の機能定義とG-Shift。
- [Japan Patent Office: trademark system overview](https://www.jpo.go.jp/system/trademark/gaiyo/seidogaiyo/chizai08.html)
- [U.S. Copyright Office: 17 USC Chapter 12, §1201(f)](https://www.copyright.gov/title17/92chap12.html) — 相互運用目的のリバースエンジニアリング規定。適用判断は法務確認が必要。
