using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Hardware;

class SensorsVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) { computer.Traverse(this); }
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Accept(this);
        foreach (var sensor in hardware.Sensors) { /* no-op */ }
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

record Temps(double? cpu, double? gpu, Dictionary<string,double?> nvme);

class Program
{
    static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "read";
        var cpuMax = 85.0; var gpuMax = 90.0;
        foreach (var a in args)
        {
            if (a.StartsWith("--cpu-max=")) double.TryParse(a[10..], out cpuMax);
            else if (a.StartsWith("--gpu-max=")) double.TryParse(a[10..], out gpuMax);
        }

        var comp = new Computer()
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        comp.Open();
        comp.Accept(new SensorsVisitor());

        double? cpu = null;
        double? gpu = null;
        var nvme = new Dictionary<string,double?>();

        foreach (var hw in comp.Hardware)
        {
            try
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature)
                        {
                            // Prefer package/CPU overall temp if present
                            if (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase))
                            {
                                cpu = s.Value;
                                break;
                            }
                            cpu ??= s.Value;
                        }
                    }
                }
                else if (hw.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature)
                        {
                            if (s.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase))
                            { gpu = s.Value; break; }
                            gpu ??= s.Value;
                        }
                    }
                }
                else if (hw.HardwareType == HardwareType.Storage)
                {
                    var isNvme = hw.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || hw.Identifier.ToString().Contains("/nvme/");
                    if (isNvme)
                    {
                        double? nvmeTemp = null;
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Temperature)
                            { nvmeTemp ??= s.Value; if (s.Name.Contains("Composite", StringComparison.OrdinalIgnoreCase)) { nvmeTemp = s.Value; break; } }
                        }
                        nvme[hw.Name] = nvmeTemp;
                    }
                }
            }
            catch { /* ignore hardware read errors */ }
        }

        try
        {
            switch (cmd)
            {
            case "json":
                var json = JsonSerializer.Serialize(new
                {
                    cpu = cpu,
                    gpu = gpu,
                    nvme = nvme
                }, new JsonSerializerOptions{ DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull});
                Console.WriteLine(json);
                return 0;
            case "read":
                Console.WriteLine($"CPU: {(cpu is null ? "N/A" : $"{cpu:F1}°C")}  GPU: {(gpu is null ? "N/A" : $"{gpu:F1}°C")}");
                if (nvme.Count > 0)
                {
                    foreach (var kv in nvme) Console.WriteLine($"{kv.Key}: {(kv.Value is null ? "N/A" : $"{kv.Value:F1}°C")}");
                }
                return 0;
            case "check":
                var ok = 0;
                if (cpu is double c && c >= cpuMax) ok = 2;
                if (gpu is double g && g >= gpuMax) ok = 2;
                Console.WriteLine(ok == 0 ? "OK" : "HOT");
                return ok;
            case "metrics":
                // Prometheus textfile format to stdout
                if (cpu is double mc) Console.WriteLine($"pin_hwmon_temperature_celsius{{sensor=\"cpu\"}} {mc:F1}");
                if (gpu is double mg) Console.WriteLine($"pin_hwmon_temperature_celsius{{sensor=\"gpu\"}} {mg:F1}");
                foreach (var kv in nvme)
                    if (kv.Value is double mv) Console.WriteLine($"pin_hwmon_temperature_celsius{{sensor=\"{kv.Key}\"}} {mv:F1}");
                return 0;
            default:
                Console.WriteLine("Usage: pin-hwmon-win [read|json|check --cpu-max=N --gpu-max=N|metrics]");
                return 2;
            }
        }
        finally
        {
            try { comp.Close(); } catch { }
        }
    }
}
