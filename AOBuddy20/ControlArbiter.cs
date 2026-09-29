// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: ControlArbiter.cs
// 
// Last modified: 2026-09-29 13:47
// Created:       2026-09-28 17:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using AOBuddy20.Enums;

namespace AOBuddy20;

public sealed class ControlArbiter
{
    private int _activePriority = (int)ControlPriority.None;
    private TaskCompletionSource? _resume = null;

    /// <summary>
    ///     Runs a step. If a higher-priority system is active (or becomes active),
    ///     the step suspends until control is released back down.
    /// </summary>
    public async Task RunStepAsync(ControlPriority priority, Func<Task> step, CancellationToken ct)
    {
        await YieldUntilAvailableAsync(priority, ct);
        await step();
    }

    /// <summary>
    ///     For long-running steps that tick (e.g. "clear mission" over many heartbeats).
    ///     Each tick checks if control is still available.
    /// </summary>
    public async Task RunTicksAsync(ControlPriority priority, Func<Task> tick,
        Func<bool> shouldContinue, CancellationToken ct)
    {
        while (shouldContinue() && !ct.IsCancellationRequested)
        {
            await YieldUntilAvailableAsync(priority, ct);
            await tick();
        }
    }

    private async Task YieldUntilAvailableAsync(ControlPriority priority, CancellationToken ct)
    {
        while (Volatile.Read(ref _activePriority) >= (int)priority)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _resume = tcs;
            // re-check after registering (avoid missed signal)
            if (Volatile.Read(ref _activePriority) < (int)priority)
            {
                return;
            }

            await tcs.Task.WaitAsync(ct);
        }
    }

    // ── called by interrupting systems ──
    public void TakeControl(ControlPriority priority)
    {
        Volatile.Write(ref _activePriority, (int)priority);
    }

    public void ReleaseControl()
    {
        Volatile.Write(ref _activePriority, (int)ControlPriority.None);
        _resume?.TrySetResult();
    }

    public bool HasControl(ControlPriority priority)
    {
        return Volatile.Read(ref _activePriority) < (int)priority;
    }
}