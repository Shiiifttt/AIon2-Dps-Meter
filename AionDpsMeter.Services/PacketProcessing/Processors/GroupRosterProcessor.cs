using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Arena;
using System.Text;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    /// <summary>
    /// 0092, your group (party in dungeons, your team in the arena):
    /// <len> 0092 <u8> <u16 counter> <u16> <u32 group id> <u64 leader dbid> <u32 size> ... then per member
    /// <u8 mask> <u8 slot> <u32> <u16 born server> <u16 current server> <u8 36> <36 char guid>
    /// <u64 dbid> <u8 len> <name> <u32> <varint hp> <varint max hp> ...
    /// A packet can carry only some of the members. Members are found by their guid.
    /// </summary>
    [PacketOpcode(PacketOpcodes.GroupRoster)]
    internal class GroupRosterProcessor(ArenaTracker arenaTracker) : IOpcodeProcessor
    {
        private const int GuidLength = 36;
        private const int MaxNameLength = 72;

        public void Process(Packet packet)
        {
            var data = packet.Data;
            int lenBytes = 0;
            while (lenBytes < data.Length && (data[lenBytes] & 0x80) != 0) lenBytes++;
            lenBytes++;

            int groupIdOffset = lenBytes + 2 + 1 + 2 + 2;
            if (groupIdOffset + 4 > data.Length) return;
            uint groupId = BitConverter.ToUInt32(data, groupIdOffset);

            var names = new List<string>();
            for (int i = groupIdOffset + 4; i + 1 + GuidLength + 8 + 1 < data.Length; i++)
            {
                if (data[i] != GuidLength || !IsGuid(data, i + 1)) continue;

                int nameLenOffset = i + 1 + GuidLength + 8;
                int nameLen = data[nameLenOffset];
                if (nameLen < 1 || nameLen > MaxNameLength || nameLenOffset + 1 + nameLen > data.Length) continue;

                names.Add(Encoding.UTF8.GetString(data, nameLenOffset + 1, nameLen));
                i = nameLenOffset + nameLen;
            }

            arenaTracker.OnRoster(groupId, names);
        }

        // 8-4-4-4-12 upper-case hex
        private static bool IsGuid(byte[] data, int offset)
        {
            for (int k = 0; k < GuidLength; k++)
            {
                byte b = data[offset + k];
                bool dash = k is 8 or 13 or 18 or 23;
                if (dash ? b != '-' : !Uri.IsHexDigit((char)b)) return false;
            }
            return true;
        }
    }
}
