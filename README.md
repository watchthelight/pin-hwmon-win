# pin-hwmon-win

Windows temperature reader using LibreHardwareMonitor.

Usage:
- Read:    pin-hwmon-win read
- JSON:    pin-hwmon-win json
- Check:   pin-hwmon-win check --cpu-max=85 --gpu-max=90
- Metrics: pin-hwmon-win metrics

Notes:
- Requires .NET 8 runtime.
- Uses LibreHardwareMonitorLib to read CPU/GPU/NVMe temps.