using System;
using System.Threading;
using System.Threading.Tasks;
using SoftwareFix.Core.Models;

namespace SoftwareFix.Device;

public enum DeviceMode
{
    None,
    Adb,
    Fastboot
}

public class DeviceMonitor : IDisposable
{
    private readonly FastbootRunner _fastboot = new();
    private readonly AdbRunner _adb = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;

    public event Action<DeviceRescueInfo, DeviceMode>? DeviceConnected;
    public event Action? DeviceDisconnected;

    public string? CurrentSerial { get; private set; }
    public DeviceMode CurrentMode { get; private set; } = DeviceMode.None;
    public DeviceRescueInfo? CurrentDeviceInfo { get; private set; }

    public void StartMonitoring(int pollIntervalMs = 2000)
    {
        _monitorTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Check Fastboot devices first
                    var fbDevices = await _fastboot.GetConnectedDevicesAsync();
                    if (fbDevices.Count > 0)
                    {
                        string serial = fbDevices[0];
                        if (CurrentSerial != serial || CurrentMode != DeviceMode.Fastboot)
                        {
                            var info = await _fastboot.InspectDeviceAsync(serial);
                            if (info != null)
                            {
                                CurrentSerial = serial;
                                CurrentMode = DeviceMode.Fastboot;
                                CurrentDeviceInfo = info;
                                DeviceConnected?.Invoke(info, DeviceMode.Fastboot);
                            }
                        }
                    }
                    else
                    {
                        // Check ADB devices
                        var adbDevices = await _adb.GetConnectedDevicesAsync();
                        if (adbDevices.Count > 0)
                        {
                            string serial = adbDevices[0];
                            if (CurrentSerial != serial || CurrentMode != DeviceMode.Adb)
                            {
                                string sku = await _adb.GetPropertyAsync(serial, "ro.boot.hardware.sku");
                                if (string.IsNullOrEmpty(sku))
                                {
                                    sku = await _adb.GetPropertyAsync(serial, "ro.product.model");
                                }
                                var info = new DeviceRescueInfo
                                {
                                    SerialNumber = serial,
                                    Sku = sku,
                                    ModelName = sku,
                                    RoCarrier = await _adb.GetPropertyAsync(serial, "ro.carrier"),
                                    Fingerprint = await _adb.GetPropertyAsync(serial, "ro.build.fingerprint"),
                                    BlurVersion = await _adb.GetPropertyAsync(serial, "ro.build.version.full")
                                };
                                CurrentSerial = serial;
                                CurrentMode = DeviceMode.Adb;
                                CurrentDeviceInfo = info;
                                DeviceConnected?.Invoke(info, DeviceMode.Adb);
                            }
                        }
                        else
                        {
                            if (CurrentMode != DeviceMode.None)
                            {
                                CurrentSerial = null;
                                CurrentMode = DeviceMode.None;
                                CurrentDeviceInfo = null;
                                DeviceDisconnected?.Invoke();
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore device polling errors
                }

                await Task.Delay(pollIntervalMs, _cts.Token);
            }
        }, _cts.Token);
    }

    public void StopMonitoring()
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        StopMonitoring();
        _cts.Dispose();
    }
}
