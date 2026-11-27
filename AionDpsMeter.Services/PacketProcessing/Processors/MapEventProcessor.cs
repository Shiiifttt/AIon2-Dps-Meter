using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Arena;
using System.Text;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    /// <summary>
    /// 2E92 names a scripted map event, e.g. "Impetusium_Arena_MapEvent_02" or "Arkanis_G_01_BossRoom_MapEvent".
    /// Only used to tell that the current map is an arena.
    /// </summary>
    [PacketOpcode(PacketOpcodes.MapEvent)]
    internal class MapEventProcessor(ArenaTracker arenaTracker) : IOpcodeProcessor
    {
        private static readonly byte[] ArenaMarker = Encoding.ASCII.GetBytes("_Arena_");

        public void Process(Packet packet)
        {
            if (packet.Data.AsSpan().IndexOf(ArenaMarker) >= 0)
                arenaTracker.OnArenaMapEvent();
        }
    }
}
