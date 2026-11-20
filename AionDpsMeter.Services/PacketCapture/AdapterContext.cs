using SharpPcap;
using System;
using System.Collections.Generic;
using System.Text;

namespace AionDpsMeter.Services.PacketCapture
{
    internal enum AdapterKind { Loopback, Physical, Skip }

    internal sealed class AdapterContext
    {
        public ICaptureDevice Device { get; }
        public AdapterKind Kind { get; }
        public bool IsOpen { get; set; }

        /// <summary>This adapter's packet handler, kept so it can be detached before re-attaching on reopen.</summary>
        public SharpPcap.PacketArrivalEventHandler? Handler { get; set; }

        public AdapterContext(ICaptureDevice device, AdapterKind kind)
        {
            Device = device;
            Kind = kind;
        }
    }
}
