using ADLXWrapper;
using FanControl.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FanControl.ADLX
{
    public class ADLXPlugin : IPlugin2
    {
        private readonly IPluginLogger _pluginLogger;
        private readonly IPluginDialog _pluginDialog;
        private ADLXWrapper.ADLXWrapper _wrapper;
        private SystemServices _system;
        private IReadOnlyList<GPU> _gpus;
        private GPUTuningService _tuning;
        private PerformanceMonitor _perf;
        private Dictionary<int, ManualFanTuning> _fans;
        private IDisposable _tracking;
        private bool _initialized;
        private GPUMetricsProvider[] _metricsProviders;

        public ADLXPlugin(IPluginLogger pluginLogger, IPluginDialog pluginDialog)
        {
            _pluginLogger = pluginLogger;
            _pluginDialog = pluginDialog;
        }

        public string Name => "ADLX";

        public void Initialize()
        {
            try
            {
                Log("Initializing ADLX");
                _wrapper = new ADLXWrapper.ADLXWrapper();
                _wrapper.Initialize();
                
                // Even though _wrapper.Initialize() returns it may take some time for it to actually be initialized
                var retryCount = 0;
                const int retryMax = 5, retryInterval = 1000; // ms
                while (retryCount < retryMax)
                {
                    Thread.Sleep(retryInterval);
                    try
                    {
                        _system = _wrapper.GetSystemServices(); // Throws an error if ADLX isn't initialized
                        break; // Only called if GetSystemServices() threw no exception
                    }
                    catch (ADLXEception e)
                    {
                        Log($"Failed to get system services due to error: {e.Message}");
                        
                    }
                    retryCount++;
                }

                Log($"ADLX initialized after {retryCount} retries");
                _gpus = _system.GetGPUs();
                
                _tuning = _system.GetGPUTuningService();
                _perf = _system.GetPerformanceMonitor();

                _fans = _gpus.Where(_tuning.IsManualFanTuningSupported)
                    .ToDictionary(x => x.UniqueId, x => _tuning.GetManualFanTuning(x));

                //_tracking = _perf.StartTracking(1000);
                _metricsProviders = _gpus.Select(x => new GPUMetricsProvider(_perf, x)).ToArray();
                _initialized = true;
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                DisposeAll();
                _initialized = false;
            }
        }

        public void Load(IPluginSensorsContainer _container)
        {
            if (!_initialized)
            {
                return;
            }
            Log("Getting GPUs from ADLX");
            ADLXControl[] controls = _gpus.Where(x => _fans.ContainsKey(x.UniqueId)).Select(x => new ADLXControl(x, _fans[x.UniqueId])).ToArray();
            ADLXFanSensor[] fanSensors = _gpus.Zip(_metricsProviders, (gpu, m) => new ADLXFanSensor(gpu, m)).ToArray();
            ADLXTemperatureSensor[] gpuHotspots = _gpus.Zip(_metricsProviders, (gpu, m) => new ADLXTemperatureSensor("Hotspot", gpu, () => m.Current.GPUHotspotTemperature)).ToArray();
            ADLXTemperatureSensor[] gpuTemps = _gpus.Zip(_metricsProviders, (gpu, m) => new ADLXTemperatureSensor("Core", gpu, () => m.Current.GPUTemperature)).ToArray();
            ADLXTemperatureSensor[] gpuMemoryTemps = _gpus.Zip(_metricsProviders, (gpu, m) => new ADLXTemperatureSensor("Memory", gpu, () => m.Current.GPUMemoryTemperature)).ToArray();

            foreach (var control in controls)
            {
                _container.ControlSensors.Add(control);
            }

            foreach (var fan in fanSensors)
            {
                _container.FanSensors.Add(fan);
            }

            Log("Combining temps");
            var combinedTemps = gpuHotspots.Concat(gpuTemps).Concat(gpuMemoryTemps);
            foreach (var temp in combinedTemps)
            {
                _container.TempSensors.Add(temp);
            }
        }

        public void Update()
        {
            if (!_initialized) return;

            foreach (var provider in _metricsProviders)
                provider.UpdateMetrics();
        }

        public void Close()
        {
            if (!_initialized)
                return;

            DisposeAll();

            _initialized = false;
        }

        private void Log(string message)
        {
            _pluginLogger.Log($"ADLX plugin: {message}");
        }

        private void DisposeAll()
        {
            _tracking?.Dispose();
            _fans?.Values.ToList().ForEach(x => x.Dispose());
            _fans.Clear();
            _perf?.Dispose();
            _tuning?.Dispose();
            _gpus?.ToList().ForEach(x => x.Dispose());
            _gpus = null;
            _system?.Dispose();
            _wrapper?.Dispose();
        }
    }
}
