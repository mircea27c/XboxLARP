using System.Runtime.InteropServices;

namespace MonitorTopology;

/// <summary>
/// The only code path in this codebase that ever calls SetDisplayConfig.
///
/// Design rules driven by researched CCD failure modes on 3+ monitor systems:
///  - Exactly ONE SetDisplayConfig call per apply attempt, built from a single full
///    path+mode array covering every known target (unwanted ones present but inactive) -
///    never a loop of per-monitor enable/disable calls.
///  - Monitors are matched by a persistent identity (EDID mfg/product + output tech +
///    connector instance), never by adapter LUID alone (LUIDs are only stable for the
///    current boot session) and never by source ID or \\.\DISPLAYn name (both are
///    enumeration-order artifacts Windows reassigns on every topology rebuild).
///  - Every apply is followed by a re-query verification pass with a small bounded retry,
///    not a silent assumption of success.
/// </summary>
public sealed class TopologyService
{
    private readonly ILogSink _log;

    public TopologyService(ILogSink log) => _log = log;

    // ---------- raw query ----------

    private static (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) QueryRaw(uint flags)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int rc = NativeMethods.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (rc != NativeMethods.ERROR_SUCCESS)
                throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: Win32 error {rc}");

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            rc = NativeMethods.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (rc == NativeMethods.ERROR_SUCCESS)
                return (paths[..(int)pathCount], modes[..(int)modeCount]);

            // Topology can change between the size query and the query itself (e.g. a monitor
            // waking up) - ERROR_INSUFFICIENT_BUFFER means retry with a fresh size.
            if (rc == NativeMethods.ERROR_INSUFFICIENT_BUFFER)
                continue;

            throw new InvalidOperationException($"QueryDisplayConfig failed: Win32 error {rc}");
        }

        throw new InvalidOperationException("QueryDisplayConfig: topology kept changing size across 8 attempts");
    }

    private static DISPLAYCONFIG_TARGET_PREFERRED_MODE? GetPreferredMode(LUID adapterId, uint targetId)
    {
        var request = new DISPLAYCONFIG_TARGET_PREFERRED_MODE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_PREFERRED_MODE,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_PREFERRED_MODE>(),
                adapterId = adapterId,
                id = targetId,
            },
        };
        int rc = NativeMethods.DisplayConfigGetDeviceInfoPreferredMode(ref request);
        return rc == NativeMethods.ERROR_SUCCESS ? request : null;
    }

    /// <summary>
    /// Builds a single-monitor profile for a role using the monitor's EDID-reported preferred
    /// timing, without requiring it to already be active - lets a monitor that has never been
    /// turned on be activated the first time entirely programmatically (no Windows Display
    /// Settings UI involved), so it can then be arranged/captured normally like any other.
    /// </summary>
    public DisplayProfile BuildPreferredModeProfile(MonitorConfig config, string role)
    {
        var label = config.FindByRole(role) ?? throw new InvalidOperationException($"No monitor labelled '{role}' - run --setup-monitors first");
        var live = QueryTopology().FirstOrDefault(m => m.Identity.StableKey == label.StableKey)
            ?? throw new InvalidOperationException($"Monitor for role '{role}' is not currently connected");

        var preferred = GetPreferredMode(live.AdapterId, live.TargetId)
            ?? throw new InvalidOperationException($"Could not read preferred mode for role '{role}' (target {live.TargetId})");

        var sig = preferred.targetMode.targetVideoSignalInfo;
        return new DisplayProfile
        {
            Name = $"__bootstrap_{role}",
            ActiveMonitors =
            [
                new ProfileMonitorEntry
                {
                    Role = role,
                    Rotation = (uint)DISPLAYCONFIG_ROTATION.IDENTITY,
                    Scaling = (uint)DISPLAYCONFIG_SCALING.PREFERRED,
                    RefreshNumerator = sig.vSyncFreq.Numerator,
                    RefreshDenominator = sig.vSyncFreq.Denominator,
                    ScanLineOrdering = (uint)sig.scanLineOrdering,
                    SourceMode = new CapturedSourceMode
                    {
                        Width = preferred.width,
                        Height = preferred.height,
                        PixelFormat = (uint)DISPLAYCONFIG_PIXELFORMAT.PIXELFORMAT_32BPP,
                        PositionX = 0,
                        PositionY = 0,
                    },
                    TargetMode = new CapturedTargetMode
                    {
                        PixelRate = sig.pixelRate,
                        HSyncNumerator = sig.hSyncFreq.Numerator,
                        HSyncDenominator = sig.hSyncFreq.Denominator,
                        VSyncNumerator = sig.vSyncFreq.Numerator,
                        VSyncDenominator = sig.vSyncFreq.Denominator,
                        ActiveSizeCx = sig.activeSize.cx,
                        ActiveSizeCy = sig.activeSize.cy,
                        TotalSizeCx = sig.totalSize.cx,
                        TotalSizeCy = sig.totalSize.cy,
                        VideoStandardOrCloneGroupId = sig.videoStandardOrCloneGroupId,
                        ScanLineOrdering = (uint)sig.scanLineOrdering,
                    },
                },
            ],
        };
    }

    private static DISPLAYCONFIG_TARGET_DEVICE_NAME? GetTargetName(LUID adapterId, uint targetId)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId,
            },
        };
        int rc = NativeMethods.DisplayConfigGetDeviceInfo(ref request);
        return rc == NativeMethods.ERROR_SUCCESS ? request : null;
    }

    private static DisplayIdentity IdentityFromTargetName(DISPLAYCONFIG_TARGET_DEVICE_NAME name, DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech, uint connectorInstance) =>
        new(name.EdidIdsValid, name.edidManufactureId, name.edidProductCodeId, tech, connectorInstance);

    private static DISPLAYCONFIG_SOURCE_MODE? FindSourceMode(DISPLAYCONFIG_MODE_INFO[] modes, uint idx) =>
        idx != NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID && idx < modes.Length && modes[idx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE
            ? modes[idx].sourceMode
            : null;

    // ---------- live topology (read-only, safe to call anytime) ----------

    public List<LiveMonitor> QueryTopology()
    {
        var (paths, modes) = QueryRaw(NativeMethods.QDC_ALL_PATHS);
        var result = new List<LiveMonitor>();
        var nameCache = new Dictionary<(uint low, int high, uint target), DISPLAYCONFIG_TARGET_DEVICE_NAME?>();

        for (int i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            if (!path.targetInfo.targetAvailable)
                continue; // nothing physically connected on this candidate path

            var key = (path.targetInfo.adapterId.LowPart, path.targetInfo.adapterId.HighPart, path.targetInfo.id);
            if (!nameCache.TryGetValue(key, out var name))
            {
                name = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
                nameCache[key] = name;
            }
            if (name is null)
            {
                _log.Warn($"Path {i}: DisplayConfigGetDeviceInfo(GET_TARGET_NAME) failed for target {path.targetInfo.id}, skipping");
                continue;
            }

            bool active = (path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0;
            var srcMode = active ? FindSourceMode(modes, path.sourceInfo.modeInfoIdx) : null;
            bool isPrimary = srcMode is { } sm && sm.position.x == 0 && sm.position.y == 0;

            var identity = IdentityFromTargetName(name.Value, path.targetInfo.outputTechnology, ConnectorInstanceOf(name.Value));

            result.Add(new LiveMonitor
            {
                Identity = identity,
                FriendlyName = name.Value.monitorFriendlyDeviceName,
                AdapterId = path.targetInfo.adapterId,
                TargetId = path.targetInfo.id,
                SourceId = path.sourceInfo.id,
                Active = active,
                IsPrimary = isPrimary,
                PathIndex = i,
            });
        }

        // A physical target can appear on more than one candidate path (QDC_ALL_PATHS lists
        // every possible source/target pairing) - collapse to one entry per identity,
        // preferring whichever candidate is currently active.
        return result
            .GroupBy(m => m.Identity.StableKey)
            .Select(g => g.OrderByDescending(m => m.Active).First())
            .ToList();
    }

    private static uint ConnectorInstanceOf(DISPLAYCONFIG_TARGET_DEVICE_NAME name) => name.connectorInstance;

    // ---------- capture ----------

    public DisplayProfile CaptureProfile(MonitorConfig config, string profileName)
    {
        var (paths, modes) = QueryRaw(NativeMethods.QDC_ONLY_ACTIVE_PATHS);
        var profile = new DisplayProfile { Name = profileName };

        foreach (var path in paths)
        {
            if ((path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) == 0)
                continue;

            var name = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
            if (name is null)
            {
                _log.Warn($"Capture: could not resolve target name for target {path.targetInfo.id}, skipping this monitor");
                continue;
            }

            var identity = IdentityFromTargetName(name.Value, path.targetInfo.outputTechnology, ConnectorInstanceOf(name.Value));
            var label = config.FindByIdentity(identity);
            if (label is null)
            {
                _log.Warn($"Capture: active monitor '{name.Value.monitorFriendlyDeviceName}' has no assigned role (run --setup-monitors first), skipping it from this profile");
                continue;
            }

            var srcMode = FindSourceMode(modes, path.sourceInfo.modeInfoIdx)
                ?? throw new InvalidOperationException($"Active path for role '{label.Role}' has no valid source mode");
            var tgtIdx = path.targetInfo.modeInfoIdx;
            if (tgtIdx == NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID || tgtIdx >= modes.Length || modes[tgtIdx].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.TARGET)
                throw new InvalidOperationException($"Active path for role '{label.Role}' has no valid target mode");
            var tgtMode = modes[tgtIdx].targetMode;

            profile.ActiveMonitors.Add(new ProfileMonitorEntry
            {
                Role = label.Role,
                Rotation = (uint)path.targetInfo.rotation,
                Scaling = (uint)path.targetInfo.scaling,
                RefreshNumerator = path.targetInfo.refreshRate.Numerator,
                RefreshDenominator = path.targetInfo.refreshRate.Denominator,
                ScanLineOrdering = (uint)path.targetInfo.scanLineOrdering,
                SourceMode = new CapturedSourceMode
                {
                    Width = srcMode.width,
                    Height = srcMode.height,
                    PixelFormat = (uint)srcMode.pixelFormat,
                    PositionX = srcMode.position.x,
                    PositionY = srcMode.position.y,
                },
                TargetMode = new CapturedTargetMode
                {
                    PixelRate = tgtMode.targetVideoSignalInfo.pixelRate,
                    HSyncNumerator = tgtMode.targetVideoSignalInfo.hSyncFreq.Numerator,
                    HSyncDenominator = tgtMode.targetVideoSignalInfo.hSyncFreq.Denominator,
                    VSyncNumerator = tgtMode.targetVideoSignalInfo.vSyncFreq.Numerator,
                    VSyncDenominator = tgtMode.targetVideoSignalInfo.vSyncFreq.Denominator,
                    ActiveSizeCx = tgtMode.targetVideoSignalInfo.activeSize.cx,
                    ActiveSizeCy = tgtMode.targetVideoSignalInfo.activeSize.cy,
                    TotalSizeCx = tgtMode.targetVideoSignalInfo.totalSize.cx,
                    TotalSizeCy = tgtMode.targetVideoSignalInfo.totalSize.cy,
                    VideoStandardOrCloneGroupId = tgtMode.targetVideoSignalInfo.videoStandardOrCloneGroupId,
                    ScanLineOrdering = (uint)tgtMode.targetVideoSignalInfo.scanLineOrdering,
                },
            });
        }

        if (profile.ActiveMonitors.Count == 0)
            throw new InvalidOperationException("Capture produced zero labelled active monitors - label monitors with --setup-monitors first, and make sure the monitors you want in this profile are the ones currently active in Windows.");

        return profile;
    }

    // ---------- apply ----------

    public ApplyResult ApplyProfile(MonitorConfig config, string profileName, int maxAttempts = 3, int retryDelayMs = 800, int settleDelayMs = 800)
    {
        var profile = config.FindProfile(profileName);
        if (profile is null)
            return new ApplyResult(false, $"No profile named '{profileName}' in config", 0);
        return ApplyDisplayProfile(config, profile, maxAttempts, retryDelayMs, settleDelayMs);
    }

    public ApplyResult ApplyDisplayProfile(MonitorConfig config, DisplayProfile profile, int maxAttempts = 3, int retryDelayMs = 800, int settleDelayMs = 800)
    {
        string profileName = profile.Name;
        var desiredRoles = new HashSet<string>(profile.ActiveMonitors.Select(m => m.Role), StringComparer.OrdinalIgnoreCase);
        int attempts = 0;
        string lastError = "";

        for (; attempts < maxAttempts; attempts++)
        {
            if (attempts > 0)
            {
                _log.Warn($"ApplyProfile('{profileName}'): retrying (attempt {attempts + 1}/{maxAttempts}) after: {lastError}");
                Thread.Sleep(retryDelayMs);
            }

            try
            {
                if (!TryApplyOnce(config, profile, desiredRoles, out lastError))
                    continue;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                continue;
            }

            Thread.Sleep(settleDelayMs);

            if (Verify(desiredRoles, out string verifyError))
            {
                _log.Info($"ApplyProfile('{profileName}'): success on attempt {attempts + 1}");
                return new ApplyResult(true, "OK", attempts + 1);
            }
            lastError = verifyError;
        }

        _log.Error($"ApplyProfile('{profileName}'): FAILED after {attempts} attempts: {lastError}");
        return new ApplyResult(false, lastError, attempts);
    }

    private bool TryApplyOnce(MonitorConfig config, DisplayProfile profile, HashSet<string> desiredRoles, out string error)
    {
        var (livePaths, _) = QueryRaw(NativeMethods.QDC_ALL_PATHS);

        // Resolve every candidate path's role once, and group by identity so we activate
        // exactly one path per desired physical monitor and deactivate every other candidate
        // (including duplicate candidate paths to the same target).
        var resolved = new List<(int index, DISPLAYCONFIG_PATH_INFO path, string? role)>();
        var nameCache = new Dictionary<(uint, int, uint), DISPLAYCONFIG_TARGET_DEVICE_NAME?>();
        for (int i = 0; i < livePaths.Length; i++)
        {
            var p = livePaths[i];
            if (!p.targetInfo.targetAvailable) { resolved.Add((i, p, null)); continue; }

            var key = (p.targetInfo.adapterId.LowPart, p.targetInfo.adapterId.HighPart, p.targetInfo.id);
            if (!nameCache.TryGetValue(key, out var name))
            {
                name = GetTargetName(p.targetInfo.adapterId, p.targetInfo.id);
                nameCache[key] = name;
            }
            string? role = null;
            if (name is { } n)
            {
                var identity = IdentityFromTargetName(n, p.targetInfo.outputTechnology, ConnectorInstanceOf(n));
                role = config.FindByIdentity(identity)?.Role;
            }
            resolved.Add((i, p, role));
        }

        // Pick exactly one path index to activate per desired role - prefer a candidate that's
        // already active (stable choice across repeated applies), else the first candidate.
        var chosenIndexPerRole = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in desiredRoles)
        {
            var candidates = resolved.Where(r => string.Equals(r.role, role, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0)
            {
                error = $"Role '{role}' required by profile is not present in the current live topology (monitor unplugged / renamed?)";
                return false;
            }
            bool anyActive = candidates.Any(c => (c.path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0);
            chosenIndexPerRole[role] = anyActive
                ? candidates.First(c => (c.path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0).index
                : candidates[0].index;
        }

        var newPaths = new DISPLAYCONFIG_PATH_INFO[livePaths.Length];
        var newModes = new List<DISPLAYCONFIG_MODE_INFO>();

        for (int i = 0; i < livePaths.Length; i++)
        {
            var path = livePaths[i];
            string? roleForThisIndex = resolved[i].role;
            bool isChosenActive = roleForThisIndex != null
                && chosenIndexPerRole.TryGetValue(roleForThisIndex, out int chosenIdx)
                && chosenIdx == i;

            if (isChosenActive)
            {
                var entry = profile.ActiveMonitors.First(m => string.Equals(m.Role, roleForThisIndex, StringComparison.OrdinalIgnoreCase));

                var sourceMode = new DISPLAYCONFIG_MODE_INFO
                {
                    infoType = DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE,
                    id = path.sourceInfo.id,
                    adapterId = path.sourceInfo.adapterId,
                    sourceMode = new DISPLAYCONFIG_SOURCE_MODE
                    {
                        width = entry.SourceMode.Width,
                        height = entry.SourceMode.Height,
                        pixelFormat = (DISPLAYCONFIG_PIXELFORMAT)entry.SourceMode.PixelFormat,
                        position = new POINTL { x = entry.SourceMode.PositionX, y = entry.SourceMode.PositionY },
                    },
                };
                uint sourceModeIdx = (uint)newModes.Count;
                newModes.Add(sourceMode);

                var targetMode = new DISPLAYCONFIG_MODE_INFO
                {
                    infoType = DISPLAYCONFIG_MODE_INFO_TYPE.TARGET,
                    id = path.targetInfo.id,
                    adapterId = path.targetInfo.adapterId,
                    targetMode = new DISPLAYCONFIG_TARGET_MODE
                    {
                        targetVideoSignalInfo = new DISPLAYCONFIG_VIDEO_SIGNAL_INFO
                        {
                            pixelRate = entry.TargetMode.PixelRate,
                            hSyncFreq = new DISPLAYCONFIG_RATIONAL { Numerator = entry.TargetMode.HSyncNumerator, Denominator = entry.TargetMode.HSyncDenominator },
                            vSyncFreq = new DISPLAYCONFIG_RATIONAL { Numerator = entry.TargetMode.VSyncNumerator, Denominator = entry.TargetMode.VSyncDenominator },
                            activeSize = new DISPLAYCONFIG_2DREGION { cx = entry.TargetMode.ActiveSizeCx, cy = entry.TargetMode.ActiveSizeCy },
                            totalSize = new DISPLAYCONFIG_2DREGION { cx = entry.TargetMode.TotalSizeCx, cy = entry.TargetMode.TotalSizeCy },
                            videoStandardOrCloneGroupId = entry.TargetMode.VideoStandardOrCloneGroupId,
                            scanLineOrdering = (DISPLAYCONFIG_SCANLINE_ORDERING)entry.TargetMode.ScanLineOrdering,
                        },
                    },
                };
                uint targetModeIdx = (uint)newModes.Count;
                newModes.Add(targetMode);

                path.flags |= NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
                path.sourceInfo.modeInfoIdx = sourceModeIdx;
                path.targetInfo.modeInfoIdx = targetModeIdx;
                path.targetInfo.rotation = (DISPLAYCONFIG_ROTATION)entry.Rotation;
                path.targetInfo.scaling = (DISPLAYCONFIG_SCALING)entry.Scaling;
                path.targetInfo.refreshRate = new DISPLAYCONFIG_RATIONAL { Numerator = entry.RefreshNumerator, Denominator = entry.RefreshDenominator };
                path.targetInfo.scanLineOrdering = (DISPLAYCONFIG_SCANLINE_ORDERING)entry.ScanLineOrdering;
            }
            else
            {
                path.flags &= ~NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
                path.sourceInfo.modeInfoIdx = NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                path.targetInfo.modeInfoIdx = NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }

            newPaths[i] = path;
        }

        var modesArray = newModes.ToArray();
        int rc = NativeMethods.SetDisplayConfig(
            (uint)newPaths.Length, newPaths,
            (uint)modesArray.Length, modesArray,
            NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG | NativeMethods.SDC_ALLOW_CHANGES);

        if (rc != NativeMethods.ERROR_SUCCESS)
        {
            error = $"SetDisplayConfig returned Win32 error {rc}";
            return false;
        }

        error = "";
        return true;
    }

    private bool Verify(HashSet<string> desiredRoles, out string error)
    {
        List<LiveMonitor> live;
        try { live = QueryTopology(); }
        catch (Exception ex) { error = $"Verify: re-query failed: {ex.Message}"; return false; }

        var activeRoleless = live.Where(m => m.Active).ToList();
        error = "";
        // We can't map live monitors back to roles without the config here by design (Verify
        // is identity-only); the caller already knows desiredRoles.Count is the expected count.
        if (activeRoleless.Count != desiredRoles.Count)
        {
            error = $"Verify: expected {desiredRoles.Count} active monitor(s), found {activeRoleless.Count}";
            return false;
        }
        if (!activeRoleless.Any(m => m.IsPrimary))
        {
            error = "Verify: no active monitor is at position (0,0) - none is primary";
            return false;
        }
        return true;
    }
}
