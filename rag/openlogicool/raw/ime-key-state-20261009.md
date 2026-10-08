# IMEモード用仮想キーの一次資料

出典: Microsoft Windows SDK 10.0.26100.0 `um/Ime.h` 70〜81行、`um/WinUser.h` 759行。取得日: 2026-10-09。確度: SDK原文確認済み。

```c
#define VK_DBE_ALPHANUMERIC              0x0f0
#define VK_DBE_SBCSCHAR                  0x0f3
#define VK_DBE_NOCODEINPUT               0x0fb
#define VK_OEM_AUTO       0xF3
```

公開一次資料: [MicrosoftのVK_DBE_SBCSCHAR定義](https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/UI/Input/KeyboardAndMouse/constant.VK_DBE_SBCSCHAR.html)、[IMEモード用キーの説明](https://learn.microsoft.com/en-us/previous-versions/windows/embedded/ms927178(v=msdn.10))。後者はWindows CEの旧資料なので、現行の値は上記の導入済みSDKで照合した。[MapVirtualKeyW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-mapvirtualkeyw)は仮想キーとスキャンコードの変換を定義する。
