using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenLogicool.Contracts.Playbooks;
using OpenLogicool.Contracts.Profiles;
using OpenLogicool.Desktop;

namespace OpenLogicool.Host;

internal sealed record ApplicationControlServices(IWorkspaceEditorIntents Workspace, IResidentApplyIntent? Resident,
    IG600OnboardIntent Onboard, ISerialHidSettingsIntent Serial, IG13LcdSettingsIntent Lcd,
    IWebResearchIntent Research, IExplorerIntents Explorer, ILearningRouteIntents Learning,
    ISupervisedMacroIntents? Supervised, string? SupervisedUnavailable,
    IMacroAutomationIntents Macro, IDemonstrationRecordingIntents Recording, IBotScriptIntents Bot);

internal static class ApplicationControlRegistration
{
    public static void RegisterIntents(ControlOperationRegistry registry, ApplicationControlServices services)
    {
        registry.Add("workspace", services.Workspace);
        registry.Add("resident", services.Resident, "常駐入力を起動していません。");
        registry.Add("onboard", services.Onboard, background: true);
        registry.Add("serial", services.Serial, background: true);
        registry.Add("lcd", services.Lcd);
        registry.Add("research", services.Research);
        registry.Add("explorer", services.Explorer);
        registry.Add("learning", services.Learning);
        registry.Add("supervised", services.Supervised, services.SupervisedUnavailable);
        registry.Add("macro", services.Macro);
        registry.Add("recording", services.Recording);
        registry.Add("bot", services.Bot);
        registry.AddStatic("editor", typeof(WorkspaceDocumentEditor));
    }

    public static (ControlOperationRegistry Registry, ControlJobs Jobs) Create(InputStudioWindow window,
        string databasePath, ResidentInputHost? resident, ApplicationControlServices services, Func<object> profiles)
    {
        Task<object?> Dispatch(Func<object?> action) => window.Dispatcher.InvokeAsync(action).Task;
        var registry = new ControlOperationRegistry(Dispatch);
        RegisterIntents(registry, services);
        var jobs = new ControlJobs(registry);

        registry.Wrap("bot.start", async (invoke, _, context) =>
        {
            await invoke();
            try
            {
                while (services.Bot.Current().Phase == BotScriptPhase.Starting)
                    await Task.Delay(50, context.Token);
            }
            catch (OperationCanceledException) { await services.Bot.StopAsync(); throw; }
            var current = services.Bot.Current();
            if (current.Phase == BotScriptPhase.Faulted) throw new InvalidOperationException(current.Detail);
            return current;
        });
        registry.Wrap("bot.stop", async (invoke, _, _) => { await invoke(); return services.Bot.Current(); });
        registry.Wrap("workspace.save", async (invoke, arguments, _) =>
        {
            var result = await invoke();
            var document = arguments.GetProperty("document").Deserialize<WorkspaceDocument>(ControlOperationRegistry.Json)!;
            await Dispatch(() => { services.Resident?.ApplyIfResident(document); return null; });
            return result;
        });
        registry.Wrap("workspace.undo", async (invoke, _, _) =>
        {
            var result = (WorkspaceUndoOutcome)(await invoke())!;
            await Dispatch(() => { services.Resident?.ApplyIfResident(result.Document); return null; });
            return result;
        });

        object Status() => new
        {
            running = true, apiAvailable = true, processId = Environment.ProcessId, databasePath,
            resident = resident is not null, windowVisible = window.IsVisible, window.HasUnsavedChanges,
            outputRoute = resident?.OutputRoute.ToString(), residentFailure = resident?.Failure?.Message,
            processedInputs = resident?.Pump.ProcessedCount, droppedG13 = resident?.DroppedG13InputCount,
            droppedG600 = resident?.DroppedG600InputCount, lcd = resident?.G13LcdStatus,
            backlight = resident?.G13BacklightStatus,
            bot = services.Bot.Current(), recording = services.Recording.Status(), macro = services.Macro.CurrentRun()
        };
        registry.Add("app.status", async (_, _) => await Dispatch(Status));
        registry.Add("diagnostics.snapshot", async (_, _) => await Dispatch(Status));
        registry.Add("profiles.list", async (_, _) => await Dispatch(profiles));
        registry.Add("devices.list", async (_, _) => await Dispatch(() => resident?.ConnectedDevices()
            ?? throw new NotSupportedException("接続一覧は常駐モードで利用できます。")));
        registry.Add("app.open", async (arguments, _) => await Dispatch(() =>
        {
            var view = arguments.TryGetProperty("view", out var value) ? value.GetString()! : "input-studio";
            var panel = window.ControlPanel(view, true);
            return new { processId = Environment.ProcessId, view, visible = panel.IsVisible };
        }), [Parameter("view", "String", false)]);
        registry.Add("app.close", async (arguments, _) => await Dispatch(() =>
        {
            var discard = arguments.TryGetProperty("discardUnsavedChanges", out var value) && value.GetBoolean();
            if (window.HasUnsavedChanges && !discard) throw new InvalidOperationException("編集内容が未保存です。保存するか、discardUnsavedChangesを明示してください。");
            return new ControlDeferredResult(new { processId = Environment.ProcessId, closing = true },
                () => window.Dispatcher.BeginInvoke(new Action(window.Close)));
        }), [Parameter("discardUnsavedChanges", "Boolean", false)]);
        registry.Add("app.snapshot", async (arguments, _) =>
        {
            var view = arguments.TryGetProperty("view", out var value) ? value.GetString()! : "input-studio";
            var path = Path.GetFullPath(arguments.GetProperty("path").GetString()!);
            var panel = (Window)(await Dispatch(() => window.ControlPanel(view, false)))!;
            return await panel.Dispatcher.InvokeAsync<object?>(() =>
            {
                panel.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(panel);
                var width = (int)Math.Ceiling(panel.ActualWidth * dpi.DpiScaleX);
                var height = (int)Math.Ceiling(panel.ActualHeight * dpi.DpiScaleY);
                var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var output = File.Create(path); encoder.Save(output);
                return new { view, path, width, height };
            }, DispatcherPriority.Render).Task;
        }, [Parameter("path", "String", true), Parameter("view", "String", false)]);
        return (registry, jobs);
    }

    private static ControlParameter Parameter(string name, string type, bool required) =>
        new(name, type, required, JsonNode.Parse("{\"type\":\"" + type.ToLowerInvariant() + "\"}")!);
}
