using System.Xml;
using System.Xml.Linq;

namespace OpenLogicool.Profiles;

/// <summary>dry-run の各行が変換候補か、明示的な未対応かを表す。</summary>
public enum LgsXmlDryRunLineKind
{
    Convertible,
    Unsupported,
}

/// <summary>
/// LGS profile XML を読んだ 1 行の表示用結果。
/// SourceValue は保持しないため、XML 内の script 本文や path を命令・実行対象として渡さない。
/// </summary>
public sealed record LgsXmlDryRunLine(
    LgsXmlDryRunLineKind Kind,
    string ProfileName,
    string Source,
    string Summary);

/// <summary>LGS profile XML の読み取り専用 import dry-run 結果。</summary>
public sealed record LgsXmlDryRunReport(
    IReadOnlyList<LgsXmlDryRunLine> Convertible,
    IReadOnlyList<LgsXmlDryRunLine> Unsupported);

/// <summary>
/// LGS 9.04.49 profile XML の import 候補を分類する pure parser（APP-009）。
/// ここでは設定保存・device API・script 実行を一切行わず、利用者変更の単純 keystroke 割当だけを
/// 変換候補として表示する。path と script は外部入力のまま未対応として表示する。
/// </summary>
public static class LgsXmlDryRun
{
    public static LgsXmlDryRunReport Analyze(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var document = XDocument.Load(reader, LoadOptions.None);

        var convertible = new List<LgsXmlDryRunLine>();
        var unsupported = new List<LgsXmlDryRunLine>();
        foreach (var profile in document.Descendants().Where(element => element.Name.LocalName == "profile"))
        {
            var profileName = Attribute(profile, "name") ?? "(名前なし profile)";
            foreach (var target in profile.Elements().Where(element => element.Name.LocalName == "target"))
            {
                if (Attribute(target, "path") is not null)
                {
                    unsupported.Add(new(
                        LgsXmlDryRunLineKind.Unsupported,
                        profileName,
                        "target",
                        "アプリ関連付け path は import dry-run では取り込まない。"));
                }
            }

            var macros = profile.Descendants()
                .Where(element => element.Name.LocalName == "macro")
                .Select(macro => new { Guid = Attribute(macro, "guid"), Element = macro })
                .Where(entry => entry.Guid is not null)
                .ToDictionary(entry => entry.Guid!, entry => entry.Element, StringComparer.Ordinal);

            foreach (var assignment in profile.Descendants().Where(element => element.Name.LocalName == "assignment"))
            {
                var contextId = Attribute(assignment, "contextid") ?? "(contextid なし)";
                if (IsTrue(Attribute(assignment, "original")))
                {
                    unsupported.Add(new(
                        LgsXmlDryRunLineKind.Unsupported,
                        profileName,
                        contextId,
                        "LGS 既定割当（original=true）は取り込まない。"));
                    continue;
                }

                var macroGuid = Attribute(assignment, "macroguid");
                if (macroGuid is null || !macros.TryGetValue(macroGuid, out var macro))
                {
                    unsupported.Add(new(
                        LgsXmlDryRunLineKind.Unsupported,
                        profileName,
                        contextId,
                        "参照 macro がないため変換できない。"));
                    continue;
                }

                if (macro.Descendants().Any(element => element.Name.LocalName == "script"))
                {
                    unsupported.Add(new(
                        LgsXmlDryRunLineKind.Unsupported,
                        profileName,
                        contextId,
                        "script を含む macro は実行せず、取り込まない。"));
                    continue;
                }

                var keys = macro.Descendants()
                    .Where(element => element.Name.LocalName == "key")
                    .Select(element => Attribute(element, "value"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (macro.Descendants().Count(element => element.Name.LocalName == "keystroke") == 1 && keys.Count == 1)
                {
                    convertible.Add(new(
                        LgsXmlDryRunLineKind.Convertible,
                        profileName,
                        contextId,
                        "単一 keystroke 割当は変換候補。"));
                    continue;
                }

                unsupported.Add(new(
                    LgsXmlDryRunLineKind.Unsupported,
                    profileName,
                    contextId,
                    "単一 keystroke 以外の macro は import dry-run では変換しない。"));
            }
        }

        return new(convertible, unsupported);
    }

    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;

    private static bool IsTrue(string? value) => bool.TryParse(value, out var parsed) && parsed;
}
