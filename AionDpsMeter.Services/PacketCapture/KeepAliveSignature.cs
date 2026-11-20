using System.Buffers.Binary;

namespace AionDpsMeter.Services.PacketCapture
{
    /// <summary>
    /// The game server's keepalive: <c>0E 00 36 &lt;u64 server unix ms&gt;</c>, about ten a second.
    /// The three header bytes alone turn up all the time in other TCP traffic (an HTTPS download on
    /// port 443 used to win detection before the game was started), so a hit also needs the eight bytes
    /// after it to be a timestamp close to now.
    /// </summary>
    internal static class KeepAliveSignature
    {
        public const int Length = 11;
        private static ReadOnlySpan<byte> Header => [0x0E, 0x00, 0x36];

        // Server time vs this PC's clock; generous so a badly set PC clock still works.
        private const long MaxClockSkewMs = 12L * 60 * 60 * 1000;

        /// <summary>How many keepalives with a believable timestamp are in this TCP payload.</summary>
        public static int Count(ReadOnlySpan<byte> payload, long nowUnixMs)
        {
            int count = 0;
            int start = 0;
            while (start <= payload.Length - Length)
            {
                int idx = payload[start..].IndexOf(Header);
                if (idx < 0) break;
                int at = start + idx;
                if (at + Length > payload.Length) break;

                long serverMs = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(at + Header.Length, 8));
                if (Math.Abs(serverMs - nowUnixMs) <= MaxClockSkewMs)
                {
                    count++;
                    start = at + Length;
                }
                else
                {
                    start = at + 1;
                }
            }
            return count;
        }
    }
}
