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
using System.Linq;
using Microsoft.Extensions.Logging;

namespace AOBuddy20;

public sealed class ControlArbiter
{
    private int _activePriority = (int)ControlPriority.None;
    private TaskCompletionSource? _resume = null;

    private Dictionary<ControlPriority, int> _priorityLevels = new Dictionary<ControlPriority, int>();

    private readonly ILogger<ControlArbiter> _logger;
    
    public ControlArbiter(ILogger<ControlArbiter> logger)
    {
        _logger = logger;
        // Priority levels init
        foreach (var value in Enum.GetValues(typeof(ControlPriority)))
        {
            _priorityLevels.Add((ControlPriority)value, 0);
        }

        _priorityLevels[ControlPriority.None]++;
        _logger.LogInformation("ControlArbiter initialized.");
    }
        
    
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
            if (_resume == null)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _resume = tcs;
            }

            _priorityLevels[priority]++;
            
            // re-check after registering (avoid missed signal)
            if (Volatile.Read(ref _activePriority) < (int)priority)
            {
                return;
            }

            await _resume.Task.WaitAsync(ct);
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
        _resume = null;
    }

    public bool HasControl(ControlPriority priority)
    {
        return Volatile.Read(ref _activePriority) < (int)priority;
    }
}