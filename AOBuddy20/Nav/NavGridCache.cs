// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: NavGridCache.cs
//
// Last modified: 2026-10-01
// Created:       2026-10-01 (ported from AOBuddy10 NavGridCache.cs, R1.9)
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

#nullable disable

namespace AOBuddy20.Nav;

/// <summary>
///     A playfield's nav data and walkable grid, built OFF every loop thread (Lush Fields' grid took
///     6.3 s and froze the whole bot while it built, log 2026-09-24 01:36). Poll it every tick:
///     Request starts the build for a playfield (or keeps waiting on one already running) and returns
///     false while it loads; the first true carries Nav/Grid for that playfield (null on failure -
///     the caller falls back to straight lines, exactly as before).
///     THREADING (owner, 2026-10-01: "make sure they're threadsafe"): all state sits under one lock,
///     so any thread may Request and any thread may read Nav/Grid/LoadedPf. The build runs on a
///     thread-pool task; the published AOBuddyNav and grid objects are immutable once loaded
///     (NavGround builds its fallback water eagerly at Read), so queries against them need no lock.
/// </summary>
public sealed class NavGridCache
{
    private readonly object _sync = new();
    private Task<(AOBuddyNav Nav, IWalkGrid Grid)> _task;
    private int _taskPf = -1;
    private int _loadedPf = -1;

    public int LoadedPf
    {
        get
        {
            lock (_sync)
            {
                return _loadedPf;
            }
        }
    }

    public AOBuddyNav Nav
    {
        get
        {
            lock (_sync)
            {
                return _nav;
            }
        }
        private set => _nav = value;
    }

    public IWalkGrid Grid
    {
        get
        {
            lock (_sync)
            {
                return _grid;
            }
        }
        private set => _grid = value;
    }

    private AOBuddyNav _nav;
    private IWalkGrid _grid;

    /// <summary>tag prefixes the failure log line so it reads as the consumer that asked.</summary>
    public bool Request(int pf, string pluginDir, Action<string> log, string tag)
    {
        lock (_sync)
        {
            if (pf == _loadedPf)
            {
                return true;
            }

            if (_task == null || _taskPf != pf)
            {
                var dir = pluginDir;
                var logger = log;
                _taskPf = pf;
                _task = Task.Run(() =>
                {
                    var nav = AOBuddyNav.Load(dir, pf);
                    // The finished grid first from the disk cache (GridCache); only a miss pays the
                    // 0.6-3.8 s stamping, and a fresh build is saved back for next time.
                    IWalkGrid grid = GridCache.TryLoad(dir, pf, nav, logger);
                    if (grid == null)
                    {
                        grid = (IWalkGrid)OverlandGrid.Build(dir, pf, nav, logger) ?? FloorGrid.Build(dir, pf, nav, logger);
                        if (grid != null)
                        {
                            GridCache.Save(dir, pf, grid, logger);
                        }
                    }

                    return (nav, grid);
                });
                return false;
            }

            if (!_task.IsCompleted)
            {
                return false;
            }

            var done = _task;
            _task = null;
            _loadedPf = pf;
            Nav = null;
            Grid = null;
            if (done.IsFaulted)
            {
                log?.Invoke($"{tag}: no nav data for pf {pf}: {done.Exception?.GetBaseException().Message}");
            }
            else
            {
                (Nav, Grid) = done.Result;
            }

            return true;
        }
    }
}