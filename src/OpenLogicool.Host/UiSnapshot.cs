using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Desktop;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Size = System.Windows.Size;

namespace OpenLogicool.Host;

/// <summary>
/// 画面を窓なしで PNG へ描く（見た目の確認用）。窓を開かないので、常駐 Host が動いている間でも使える
/// ——操作用の通信口・二重起動防止の mutex・実機・前面の窓のどれにも触れない。
/// 中身は見本データで、利用者の設定は読まない。
/// </summary>
public static class UiSnapshot
{
    public static int Run(string[] arguments)
    {
        string? outputDirectory = null;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--out" when i + 1 < arguments.Length:
                    outputDirectory = Path.GetFullPath(arguments[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"unknown ui-snapshot option: {arguments[i]}（使えるのは --out <フォルダー>）");
                    return 1;
            }
        }

        if (outputDirectory is null)
        {
            Console.Error.WriteLine("ui-snapshot には画像の出力先が要ります: ui-snapshot --out <フォルダー>");
            return 1;
        }

        Directory.CreateDirectory(outputDirectory);
        Exception? failure = null;
        var written = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                written.Add(Save(InputStudio(selectActionIndex: 4, showG600: false), outputDirectory, "input-studio-g13"));
                written.Add(Save(InputStudio(selectActionIndex: 4, showG600: true), outputDirectory, "input-studio-g600"));
                written.Add(Save(InputStudio(selectActionIndex: -1, showG600: false), outputDirectory, "input-studio-no-selection"));
                written.Add(Save(new KeyCaptureDialog("回避", "Space", overwritesExisting: true, canAssignByDevicePress: true), outputDirectory, "key-capture"));
                foreach (var panel in new[] { "bot", "macro", "recording", "research" })
                {
                    written.Add(Save(GameOperator(panel), outputDirectory, $"game-operator-{panel}"));
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }

        foreach (var path in written)
        {
            Console.WriteLine(path);
        }

        return 0;
    }

    private static Window InputStudio(int selectActionIndex, bool showG600)
    {
        var intents = new FakeWorkspaceEditorIntents();
        var document = intents.LoadDocument("*").Document;
        (string Id, string Name, string[] Outputs, string? G13, string? G600)[] actions =
        [
            ("move-up", "前進", ["Key:W"], "STICK_UP", null),
            ("move-down", "後退", ["Key:S"], "STICK_DOWN", null),
            ("move-left", "左移動", ["Key:A"], "STICK_LEFT", null),
            ("move-right", "右移動", ["Key:D"], "STICK_RIGHT", null),
            ("dodge", "回避", ["Key:Space"], "G4", "G9"),
            ("menu", "メニュー", ["Key:Esc"], "G22", "G12"),
            ("skill", "スキル1", ["Key:Q"], "STICK_PRESS", "G13"),
            ("map", "地図", ["Key:M"], "LCD2", "G2"),
            ("copy", "コピー", ["Key:LCtrl", "Key:C"], null, null),
        ];
        foreach (var (id, name, outputs, g13, g600) in actions)
        {
            document = WorkspaceDocumentEditor.AddAction(document, id, name, outputs);
            if (g13 is not null)
            {
                document = WorkspaceDocumentEditor.SetBinding(document, id, "G13", g13, "base");
            }

            if (g600 is not null)
            {
                document = WorkspaceDocumentEditor.SetBinding(document, id, "G600", g600, "base");
            }
        }

        document = WorkspaceDocumentEditor.SetBinding(document, "skill", "G13", "G11", "m2");
        var saved = intents.Save(document, "*");
        var window = new InputStudioWindow(
            new WorkspaceScreenSnapshot(
                "共通設定を適用中",
                "見本のゲーム",
                saved.RevisionNumber,
                saved.Stages,
                G13ConnectedCount: 1,
                G600ConnectedCount: 1,
                [
                    new ApplicationRailEntryInput("*", "共通設定（どのアプリでもない時）", false, true),
                    new ApplicationRailEntryInput(@"C:\Games\Sample\sample.exe", "見本のゲーム", true, false),
                ]),
            InputStudioReportBuilder.Build(new DeviceDisplayInput("G13", 1, null, null), new DeviceDisplayInput("G600", 1, null, null)),
            "*",
            intents);

        if (selectActionIndex >= 0)
        {
            Descendants(window).OfType<ListBox>().First(list => AutomationProperties.GetName(list) == "操作一覧").SelectedIndex = selectActionIndex;
        }

        if (showG600)
        {
            Descendants(window).OfType<Button>()
                .First(button => AutomationProperties.GetName(button).StartsWith("G600 マウス", StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        return window;
    }

    private static Window GameOperator(string panel)
    {
        var window = new GameOperatorWindow(
            SampleIntents.Create<IWebResearchIntent>(),
            macroAutomationIntents: SampleIntents.Create<IMacroAutomationIntents>(method => method.Name switch
            {
                nameof(IMacroAutomationIntents.ListTargets) => new MacroTargetOption[] { new("game", "見本のゲーム") },
                nameof(IMacroAutomationIntents.CurrentTarget) or nameof(IMacroAutomationIntents.SelectTarget) => new MacroTargetOption("game", "見本のゲーム"),
                _ => null,
            }),
            demonstrationRecordingIntents: SampleIntents.Create<IDemonstrationRecordingIntents>(method => method.Name switch
            {
                nameof(IDemonstrationRecordingIntents.Status) => new DemonstrationRecordingStatus(DemonstrationRecorderStatus.Idle, null, 0, 0, 0, 0, 0),
                _ => null,
            }),
            botScriptIntents: SampleIntents.Create<IBotScriptIntents>(method => method.Name switch
            {
                nameof(IBotScriptIntents.ListScripts) => new BotScriptItem[] { new("sample", "見本のBot", "見本の説明") },
                nameof(IBotScriptIntents.Current) => new BotScriptSnapshot(BotScriptPhase.Stopped, "停止しています。"),
                _ => null,
            }));
        window.SelectControlPanel(panel);
        return window;
    }

    /// <summary>窓の中身を、窓と同じ見た目の設定を持つ入れ物へ移して描く（窓そのものは表示しない）。</summary>
    private static string Save(Window window, string outputDirectory, string name)
    {
        var width = double.IsNaN(window.Width) ? 470 : window.Width;
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content, Width = width, UseLayoutRounding = window.UseLayoutRounding };
        if (!double.IsNaN(window.Height))
        {
            host.Height = window.Height;
        }

        foreach (var dictionary in window.Resources.MergedDictionaries)
        {
            host.Resources.MergedDictionaries.Add(dictionary);
        }

        foreach (var key in window.Resources.Keys)
        {
            host.Resources[key] = window.Resources[key];
        }

        TextElement.SetForeground(host, window.Foreground);
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);
        TextOptions.SetTextFormattingMode(host, TextOptions.GetTextFormattingMode(window));
        host.Measure(new Size(width, double.IsNaN(window.Height) ? double.PositiveInfinity : window.Height));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(outputDirectory, $"{name}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var item in Descendants(child))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// 見本の画面用の intent。一覧は空、状態は指定した見本値を返す。画面を描くのに要らない呼び出しは既定値を返すだけで、
    /// 何も実行しない。
    /// </summary>
    public class SampleIntents : DispatchProxy
    {
        private Func<MethodInfo, object?>? sample;

        public static T Create<T>(Func<MethodInfo, object?>? sample = null)
            where T : class
        {
            var proxy = Create<T, SampleIntents>();
            ((SampleIntents)(object)proxy).sample = sample;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!;
            if (sample?.Invoke(method) is { } value)
            {
                return value;
            }

            var type = method.ReturnType;
            if (type == typeof(void))
            {
                return null;
            }

            if (type == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
