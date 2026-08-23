namespace OpenLogicool.Desktop;

/// <summary>公開 support matrix で使う根拠状態。Supported は実機で確認済みの行だけに限る。</summary>
public enum InputStudioSupportStatus
{
    Supported,
    StrongInference,
    Unverified,
    Unsupported,
}

/// <summary>Input Studio の公開面に出す capability 1行。</summary>
public sealed record InputStudioSupportEntry(
    string Capability,
    InputStudioSupportStatus Status,
    string Evidence,
    string Detail);

/// <summary>
/// Input Studio Public Gate の公開 matrix。
/// LGS 9.04.49 inventory に未確認／未対応の行が残るため、公開 claim は Partial LGS Replacement に固定する。
/// </summary>
public static class InputStudioSupportMatrix
{
    public const string PublicClaim = "Partial LGS Replacement";

    public static IReadOnlyList<InputStudioSupportEntry> Entries { get; } =
    [
        new(
            "Windows 11 build 26200 / x64 での G13・G600 入力と profile 適用",
            InputStudioSupportStatus.Supported,
            "reference machine 実機受入",
            "Supported はこの reference machine と同一 OS 系列に限定する。"),
        new(
            "G600 side button の legacy 無害化",
            InputStudioSupportStatus.Supported,
            "EXP-G600-02 実機往復",
            "B変種を主経路として G9〜G20 を F13〜F24 へ remap する。"),
        new(
            "G600 onboard slot の切替と退避",
            InputStudioSupportStatus.Supported,
            "EXP-G600-03 実機往復",
            "A方式を補完として使う。onboard profile は F3/F4/F5 の3 slotだけである。"),
        new(
            "Windows 11 build 26200 / x64 での Serial HID v1 出力",
            InputStudioSupportStatus.Supported,
            "G13・G600実機のSerial HID campaign受入",
            "SparkFun Pro Micro ATmega32U4 5V / 16MHz、firmware 1.0.0に限定する。"),
        new(
            "Windows 11 build 26200 / x64 での G13 native LCD 表示",
            InputStudioSupportStatus.Supported,
            "G13実機のnative LCD campaign受入",
            "160x43の画像・テキストとapp-first差替えを確認済み。LGS LCD applet互換を意味しない。"),
        new(
            "Serial HID v1の6キー超過と対象外出力",
            InputStudioSupportStatus.Unsupported,
            "protocol v1固定制約",
            SerialHidSettingsPresentation.LimitNotice),
        new(
            "G600 F6 profile の読取",
            InputStudioSupportStatus.Unsupported,
            "実機 read 不可",
            "F6 は完全 backup の対象外であり、Supported と表示しない。"),
        new(
            "LGS の script、LCD applet、power mode を含む全機能 parity",
            InputStudioSupportStatus.Unverified,
            "canonical LGS inventory の未確認行",
            "この行が残るため LGS Parity は名乗らない。"),
        new(
            "Windows 10、ARM64、別 GPU 構成での動作",
            InputStudioSupportStatus.Unverified,
            "reference machine 外",
            "実測がない環境は Supported に昇格しない。"),
    ];

    public static IReadOnlyList<InputStudioSupportEntry> SupportedEntries =>
        Entries.Where(entry => entry.Status == InputStudioSupportStatus.Supported).ToArray();
}
