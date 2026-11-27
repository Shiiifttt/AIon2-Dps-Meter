using AionDpsMeter.Services.Extensions;
using System.Text;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    internal class BasePlayerInfoProcessor
    {
        private const int MaxNameLength = 72;
        private const ulong MinCombatPower = 10_000;
        private const ulong MaxCombatPower = 2_000_000;
        private const int CombatPowerScanWindow = 256;
        protected readonly record struct PlayerInfoResult(int EntityId, string Name, int ServerId, int JobCode, int CombatPower);
        protected readonly record struct NameReadResult(string Name, int EndOffset);

        protected static bool TryParseCombatPower(byte[] data, int endOffset, out int combatPower)
        {
            combatPower = 0;
            const int pairSize = sizeof(ulong) * 2;
            if (endOffset < pairSize) return false;

            int startOffset = endOffset - pairSize;
            int minOffset = Math.Max(0, startOffset - (CombatPowerScanWindow - 1));

            for (int offset = startOffset; offset >= minOffset; offset--)
            {
                ulong currentCombatPower = data.ReadUInt64Le(offset);
                ulong highestCombatPower = data.ReadUInt64Le(offset + sizeof(ulong));

                bool isCurrentInRange = currentCombatPower >= MinCombatPower && currentCombatPower <= MaxCombatPower;
                bool isHighestInRange = highestCombatPower >= MinCombatPower && highestCombatPower <= MaxCombatPower;

                if (!isCurrentInRange || !isHighestInRange) continue;
                if (currentCombatPower > highestCombatPower) continue;

                combatPower = (int)currentCombatPower;

                return true;
            }

            return false;
        }

        // In 3336 and 4536 the HP block is: <varint hp> <varint max hp> <u32 mp> <u32 max mp> <8 bytes>
        // <u32> <u32> <u32> <u32> <u32 1|2> <u32 100000> <u32 100000>. The bytes before it vary
        // (position, flags), so it's found from the 100000 pair and read backwards.
        private static readonly byte[] HpBlockAnchor = [0xA0, 0x86, 0x01, 0x00, 0xA0, 0x86, 0x01, 0x00];
        private const int HpBlockAnchorToMp = 36;
        private const int HpBlockSearchWindow = 400; // 3336 has ~220 bytes between the name and the HP block

        /// <summary>Current and max HP from a player info packet (3336 / 4536), searched for after the name.</summary>
        protected static bool TryReadHp(byte[] data, int searchStart, out long hpCurrent, out long hpMax)
        {
            hpCurrent = hpMax = 0;
            if (searchStart < 0 || searchStart >= data.Length) return false;

            int windowLen = Math.Min(HpBlockSearchWindow, data.Length - searchStart);
            int found = data.AsSpan(searchStart, windowLen).IndexOf(HpBlockAnchor);
            if (found < 0) return false;

            int mpOffset = searchStart + found - HpBlockAnchorToMp;
            if (mpOffset < searchStart + 2) return false;

            if (!TryReadVarIntBackwards(data, mpOffset, searchStart, out long max, out int maxStart)) return false;
            if (!TryReadVarIntBackwards(data, maxStart, searchStart, out long cur, out _)) return false;
            if (max <= 0 || max > 50_000_000 || cur < 0) return false;

            hpCurrent = Math.Min(cur, max);
            hpMax = max;
            return true;
        }

        /// <summary>Reads the varint that ends right before <paramref name="end"/>.</summary>
        private static bool TryReadVarIntBackwards(byte[] data, int end, int floor, out long value, out int start)
        {
            value = 0;
            start = end - 1;
            if (start < floor || (data[start] & 0x80) != 0) return false;
            while (start - 1 >= floor && (data[start - 1] & 0x80) != 0 && end - start < 5) start--;

            int shift = 0;
            for (int i = start; i < end; i++, shift += 7)
                value |= (long)(data[i] & 0x7F) << shift;
            return true;
        }

        /// <summary>
        /// 4536 ends with <u32 small> <u64 combat power> and one or two more bytes.
        /// </summary>
        protected static int ReadTrailingCombatPower(byte[] data)
        {
            for (int back = 9; back <= 12; back++)
            {
                int offset = data.Length - back;
                if (offset < 4) break;
                ulong cp = data.ReadUInt64Le(offset);
                uint before = (uint)data.ReadUInt32Le(offset - 4);
                if (cp >= MinCombatPower && cp <= MaxCombatPower && before < 256) return (int)cp;
            }
            return 0;
        }

        protected NameReadResult? ReadPlayerName(byte[] data, int start, int endOffset)
        {

            start += 4;
            if ((data[start] & 0x01) != 0)
            {
                int pos = start + 1;
                var nameLengthVarInt = data.ReadVarInt(pos);
                int nameByteLength = nameLengthVarInt.Value;
                if (nameByteLength < 1 || nameByteLength > MaxNameLength) return null;
                pos += nameLengthVarInt.Length;
                if (pos + nameByteLength > endOffset) return null;
                string name = DecodeGameString(data, pos, nameByteLength);
                if (string.IsNullOrEmpty(name) || IsAllDigits(name)) return null;

                return new NameReadResult(name, pos + nameByteLength);
            }
            return null;
        }

        protected static int FindServerId(byte[] data, int searchStart, int endOffset, HashSet<int>? validServerIds)
        {
            for (int i = searchStart; i < endOffset - 1; i++)
            {
                int candidateId = data[i] | (data[i + 1] << 8);
                if (validServerIds != null && validServerIds.Contains(candidateId))
                    return candidateId;
            }
            return -1;
        }

        protected static string DecodeGameString(byte[] data, int offset, int maxLen)
        {
            byte[] outputBuffer = new byte[maxLen * 4];
            int writePos = 0;
            int readEnd = offset + maxLen;

            for (int i = offset; i < readEnd; i++)
            {
                byte b = data[i];
                if (b == 0) break;
                if (b < 32)
                {
                    int repeatCount = Math.Min(b, writePos);
                    for (int j = 0; j < repeatCount && writePos < outputBuffer.Length; j++)
                        outputBuffer[writePos++] = outputBuffer[j];
                }
                else if (writePos < outputBuffer.Length)
                {
                    outputBuffer[writePos++] = b;
                }
            }

            string rawString = Encoding.UTF8.GetString(outputBuffer, 0, writePos);
            var cleanName = new StringBuilder(rawString.Length);
            foreach (char c in rawString)
                if (char.IsLetterOrDigit(c) || (c >= '?' && c <= '?'))
                    cleanName.Append(c);

            return cleanName.ToString();
        }

        private static bool IsAllDigits(string value)
        {
            foreach (char c in value)
                if (!char.IsDigit(c)) return false;
            return true;
        }
    }
}
