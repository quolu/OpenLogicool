using OpenLogicool.Contracts.Playbooks;

namespace OpenLogicool.Host;

/// <summary>実行体が待つ一手の確認をUIへ渡す。過去のOKを次の手順へ流用しない。</summary>
public sealed class MacroStepConfirmationChannel(Action<MacroStepConfirmationRequest> publish) : IMacroStepConfirmation
{
    private readonly object gate = new();
    private (string Id, TaskCompletionSource<MacroStepDecision> Reply)? pending;

    public async ValueTask<MacroStepDecision> RequestAsync(
        MacroStepConfirmationRequest request, CancellationToken cancellationToken)
    {
        var reply = new TaskCompletionSource<MacroStepDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (pending is not null) throw new InvalidOperationException("前の手順の確認が終わっていません。");
            pending = (request.ConfirmationId, reply);
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            publish(request);
            return await reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
                if (pending?.Reply == reply) pending = null;
        }
    }

    public void Confirm(string confirmationId, MacroStepDecision decision)
    {
        if (!Enum.IsDefined(decision)) throw new ArgumentOutOfRangeException(nameof(decision));
        lock (gate)
        {
            if (pending is not { } current || current.Id != confirmationId)
                throw new InvalidOperationException("この確認は現在待っている手順と一致しません。");
            pending = null;
            current.Reply.SetResult(decision);
        }
    }
}
