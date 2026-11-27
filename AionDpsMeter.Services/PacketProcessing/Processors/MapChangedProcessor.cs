using AionDpsMeter.Services.Extensions;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Arena;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    /// <summary>
    /// 2136, sent when you enter a map: <len> 2136 <u32 counter> <u32 map id> <u32 instance> <u32 0> <3 x f32 position> ...
    /// Map ids seen: 1010 open world, 61 arena, 600063 / 500033 / 50 dungeons.
    /// </summary>
    [PacketOpcode(PacketOpcodes.MapChanged)]
    internal class MapChangedProcessor(ArenaTracker arenaTracker) : IOpcodeProcessor
    {
        public void Process(Packet packet)
        {
            var data = packet.Data;
            int offset = data.ReadVarInt().Length + 2 + 4;
            if (offset + 4 > data.Length) return;
            int mapId = data.ReadUInt32Le(offset);
            if (mapId <= 0) return;
            arenaTracker.OnMapChanged(mapId);
        }
    }
}
