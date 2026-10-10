# 一つのアプリの音だけを拾う（Windows の process loopback）

- 取得日: 2026-10-10
- 対象: Windows 11 build 26200、C# / .NET 10（COM interop）
- 確度: 高（Microsoft 公式の API リファレンス＋Windows 実機での取り込みの実測）

## 結論

`ActivateAudioInterfaceAsync` に仮想デバイス `VAD\Process_Loopback` と `AUDIOCLIENT_ACTIVATION_PARAMS`（process loopback）を渡すと、指定した process と子 process が鳴らしている音だけを `IAudioClient` で受け取れる。対象の process の handle は開かず、process ID だけを渡す。ほかのアプリの音は混ざらない。

製品の実装は `src/OpenLogicool.Capture/ProcessLoopbackAudioSource.cs`。

## 公式仕様（Microsoft Learn）

- `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS`: `TargetProcessId`（対象と子 process の render stream を含める／除く）と `ProcessLoopbackMode`。最小対応は Windows 10 Build 20348。
  <https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params>
- 公式の見本: Application Loopback API Capture Sample（windows-classic-samples）。

## 実機で成立した呼び方

1. `AUDIOCLIENT_ACTIVATION_PARAMS`（12 bytes: `ActivationType = 1`（process loopback）、`TargetProcessId`、`ProcessLoopbackMode = 0`（対象と子を含める））を、`VT_BLOB`（65）の `PROPVARIANT` に入れて渡す。
2. 完了通知を受ける側は `IActivateAudioInterfaceCompletionHandler` に加えて `IAgileObject` を実装する。通知は別 thread へ届くので、event で待ってから `GetActivateResult` を呼ぶ。
3. `IAudioClient.Initialize` へ、こちらが決めた形式（PCM 16-bit・2ch・48000 Hz、`WAVEFORMATEX` は 18 bytes・pack 1）を渡す。flag は `AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK`、buffer は 200 ms。
4. `SetEventHandle` の後に `Start`。読み出しは `IAudioCaptureClient` の `GetNextPacketSize`／`GetBuffer`／`ReleaseBuffer`。

## 実測（マビノギモバイルの process、2026-10-10）

- 30 ms おきにまとめて読む形で、20 秒間に 924,480 sample（毎秒約 46,160。開始直後の立ち上がりを除くとほぼ 48,000）。読み出し 643 回のうち空だったのは 1 回。
- 対象の process が鳴らしていない間は、何も届かない（無音の sample は来ない）。読む側は経過時間ぶんを無音として扱う。
- 取り込みの間、対象のゲームと常駐 Host の動作に異常はなかった。

## 未確認

- anti-cheat で保護された process（identity が取れない process）の音を拾えるか。
- UWP の窓（`ApplicationFrameHost` が前面になるアプリ）で、音を鳴らす process を前面の process ID から辿れるか。
- Windows 10 での動作。
