using ADLXWrapper;
using System;

namespace FanControl.ADLX
{
    public class GPUMetricsProvider
    {
        private readonly PerformanceMonitor _performanceMonitor;
        private readonly GPU _gpu;
        private GPUMetricsStruct1 _metrics1;

        public GPUMetricsProvider(PerformanceMonitor performanceMonitor, GPU gpu)
        {
            _performanceMonitor = performanceMonitor;
            _gpu = gpu;
            UpdateMetrics();
        }

        public void UpdateMetrics()
        {
            _metrics1 = _performanceMonitor.GetGPUMetricsStruct1(_gpu);
        }


        public GPUMetricsStruct1 Current => _metrics1;
    }
}
