namespace AionDpsMeter.Services.PacketProcessing.Routing
{
    public struct Packet
    {
        public byte[] Data;
        public long ReceivedAt;

        public readonly DateTime ReceivedAtUtc
            => ReceivedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ReceivedAt).UtcDateTime : DateTime.UtcNow;
    }
}
