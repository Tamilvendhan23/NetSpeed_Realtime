using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetSpeedMonitor;

/// <summary>
/// Model representing a selectable network interface.
/// </summary>
public sealed class NetworkAdapterInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public NetworkInterfaceType InterfaceType { get; }
    public OperationalStatus Status { get; }
    public bool IsAutomatic { get; }

    public NetworkAdapterInfo(string id, string name, string description, NetworkInterfaceType type, OperationalStatus status, bool isAutomatic = false)
    {
        Id = id;
        Name = name;
        Description = description;
        InterfaceType = type;
        Status = status;
        IsAutomatic = isAutomatic;
    }

    public override string ToString()
    {
        if (IsAutomatic)
        {
            return "Automatic (Active Network)";
        }

        string typeLabel = InterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "Ethernet",
            _ => "Network"
        };

        return $"{typeLabel}: {Name}";
    }
}

/// <summary>
/// Result of a single speed sample calculation.
/// </summary>
public readonly struct SpeedSample
{
    public bool IsMeasuring { get; }
    public double DownloadBytesPerSec { get; }
    public double UploadBytesPerSec { get; }
    public string ActiveAdapterName { get; }
    public bool HasConnection { get; }

    public SpeedSample(bool isMeasuring, double downloadBytesPerSec, double uploadBytesPerSec, string activeAdapterName, bool hasConnection)
    {
        IsMeasuring = isMeasuring;
        DownloadBytesPerSec = downloadBytesPerSec;
        UploadBytesPerSec = uploadBytesPerSec;
        ActiveAdapterName = activeAdapterName;
        HasConnection = hasConnection;
    }

    public static SpeedSample Measuring(string adapterName) =>
        new(isMeasuring: true, 0, 0, adapterName, hasConnection: true);

    public static SpeedSample Disconnected(string adapterName) =>
        new(isMeasuring: false, 0, 0, adapterName, hasConnection: false);
}

/// <summary>
/// Measures real network traffic rates by sampling Windows NetworkInterface statistics.
/// Accurately computes download & upload speeds while avoiding multi-adapter double-counting.
/// </summary>
public sealed class NetworkSpeedTracker
{
    private long _prevRxBytes = -1;
    private long _prevTxBytes = -1;
    private readonly Stopwatch _stopwatch = new();
    private string _currentTrackedAdapterId = string.Empty;
    private string _configuredAdapterId = string.Empty;

    public NetworkSpeedTracker()
    {
        _stopwatch.Start();
    }

    public void SetSelectedAdapter(string adapterId)
    {
        _configuredAdapterId = adapterId ?? string.Empty;
        ResetBaseline();
    }

    public void ResetBaseline()
    {
        _prevRxBytes = -1;
        _prevTxBytes = -1;
        _stopwatch.Restart();
    }

    public static List<NetworkAdapterInfo> GetAvailableAdapters()
    {
        var list = new List<NetworkAdapterInfo>
        {
            new NetworkAdapterInfo("AUTO", "Automatic", "Automatically tracks active connection", NetworkInterfaceType.Unknown, OperationalStatus.Up, isAutomatic: true)
        };

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    list.Add(new NetworkAdapterInfo(
                        ni.Id,
                        ni.Name,
                        ni.Description,
                        ni.NetworkInterfaceType,
                        ni.OperationalStatus
                    ));
                }
            }
        }
        catch { }

        return list;
    }

    public SpeedSample Sample()
    {
        NetworkInterface? target = ResolveTargetAdapter();

        if (target == null)
        {
            ResetBaseline();
            return SpeedSample.Disconnected("No active network");
        }

        if (_currentTrackedAdapterId != target.Id)
        {
            _currentTrackedAdapterId = target.Id;
            ResetBaseline();
        }

        long currentRx;
        long currentTx;

        try
        {
            var stats = target.GetIPStatistics();
            currentRx = stats.BytesReceived;
            currentTx = stats.BytesSent;
        }
        catch
        {
            ResetBaseline();
            return SpeedSample.Disconnected(target.Name);
        }

        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();

        if (_prevRxBytes < 0 || _prevTxBytes < 0 || elapsedSeconds <= 0.05)
        {
            _prevRxBytes = currentRx;
            _prevTxBytes = currentTx;
            return SpeedSample.Measuring(target.Name);
        }

        long diffRx = currentRx - _prevRxBytes;
        long diffTx = currentTx - _prevTxBytes;

        _prevRxBytes = currentRx;
        _prevTxBytes = currentTx;

        if (diffRx < 0 || diffTx < 0)
        {
            ResetBaseline();
            return SpeedSample.Measuring(target.Name);
        }

        double rxRate = diffRx / elapsedSeconds;
        double txRate = diffTx / elapsedSeconds;

        return new SpeedSample(
            isMeasuring: false,
            downloadBytesPerSec: rxRate,
            uploadBytesPerSec: txRate,
            activeAdapterName: target.Name,
            hasConnection: true
        );
    }

    private NetworkInterface? ResolveTargetAdapter()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            if (!string.IsNullOrEmpty(_configuredAdapterId) && _configuredAdapterId != "AUTO")
            {
                var specific = interfaces.FirstOrDefault(i => i.Id == _configuredAdapterId && i.OperationalStatus == OperationalStatus.Up);
                if (specific != null) return specific;
            }

            NetworkInterface? bestCandidate = null;
            int bestScore = -1;
            long bestTraffic = -1;

            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                bool hasGateway = false;
                try
                {
                    hasGateway = ni.GetIPProperties().GatewayAddresses
                        .Any(g => g.Address != null && g.Address.ToString() != "0.0.0.0");
                }
                catch { }

                bool isPreferredType = ni.NetworkInterfaceType is NetworkInterfaceType.Wireless80211
                    or NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.GigabitEthernet;

                long traffic = 0;
                try
                {
                    var st = ni.GetIPStatistics();
                    traffic = st.BytesReceived + st.BytesSent;
                }
                catch { }

                // A real internet-facing adapter (has a gateway) always beats one without, regardless of
                // traffic totals, so idle-but-virtual adapters (Hyper-V, VMware...) can't hijack "Automatic".
                int score = (hasGateway ? 2 : 0) + (isPreferredType ? 1 : 0);

                if (score > bestScore || (score == bestScore && traffic > bestTraffic))
                {
                    bestCandidate = ni;
                    bestScore = score;
                    bestTraffic = traffic;
                }
            }

            return bestCandidate;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Formats network speed for any chosen unit mode:
    ///   - "Mbps": bytes/s * 8 / 1,000,000
    ///   - "MB/s": bytes/s / 1,000,000
    ///   - "Kbps": bytes/s * 8 / 1,000
    ///   - "KB/s": bytes/s / 1,000
    ///   - "bps": bytes/s * 8
    ///   - "B/s": bytes/s
    ///   - "Automatic": selects optimal unit separately per direction
    /// </summary>
    public static string FormatSpeedForUnit(double bytesPerSec, string unitMode)
    {
        if (bytesPerSec <= 0.0001)
        {
            return unitMode switch
            {
                "MB/s" => "0 MB/s",
                "Kbps" => "0 Kbps",
                "KB/s" => "0 KB/s",
                "bps" => "0 bps",
                "B/s" => "0 B/s",
                _ => "0 Mbps"
            };
        }

        double bps = bytesPerSec * 8.0;

        switch (unitMode)
        {
            case "Mbps":
            {
                double val = bps / 1_000_000.0;
                return $"{FormatNumber(val)} Mbps";
            }
            case "MB/s":
            {
                double val = bytesPerSec / 1_000_000.0;
                return $"{FormatNumber(val)} MB/s";
            }
            case "Kbps":
            {
                double val = bps / 1_000.0;
                return $"{FormatNumber(val)} Kbps";
            }
            case "KB/s":
            {
                double val = bytesPerSec / 1_000.0;
                return $"{FormatNumber(val)} KB/s";
            }
            case "bps":
            {
                return $"{FormatNumber(bps)} bps";
            }
            case "B/s":
            {
                return $"{FormatNumber(bytesPerSec)} B/s";
            }
            case "Automatic":
            default:
            {
                if (bps >= 1_000_000.0)
                {
                    double val = bps / 1_000_000.0;
                    return $"{FormatNumber(val)} Mbps";
                }
                if (bps >= 1_000.0)
                {
                    double val = bps / 1_000.0;
                    return $"{FormatNumber(val)} Kbps";
                }
                return $"{FormatNumber(bps)} bps";
            }
        }
    }

    /// <summary>
    /// Helper to format numeric values cleanly: omits redundant decimals for integers (e.g. 5250, 40, 5, 0),
    /// while retaining up to 2 decimal places for fractional rates (e.g. 5.25, 0.04, 0.66, 656.25).
    /// </summary>
    public static string FormatNumber(double value)
    {
        if (value <= 0.0001) return "0";
        double rounded = Math.Round(value, 2);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.001)
        {
            return Math.Round(rounded).ToString("0");
        }
        return rounded.ToString("0.##");
    }
}
