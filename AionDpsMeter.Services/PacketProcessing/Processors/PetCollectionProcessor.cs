using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.PacketProcessing.Shared;
using AionDpsMeter.Services.PacketProcessing.Shared.Exceptions;
using AionDpsMeter.Services.Services.Pets;
using AionDpsMeter.Services.Services.Settings;

namespace AionDpsMeter.Services.PacketProcessing.Processors
{
    // Pet collection (souls). All server -> client, usually inside compressed batches.
    //   collection (at character login):
    //     <len> 00 90 <u32 equipped pet or 0>
    //       <varint n> n x { <u32 pet> <u32 pet again> <u32 rank 1..3> }   pets unlocked
    //       <varint m> m x { <u32 pet> <u32 souls> }                       souls toward the next rank
    //   souls gained:  <len> 0D 90 <varint n> n x { <u32 pet> <u32 souls> }
    //   souls spent:   <len> 0E 90 <varint n> n x { <u32 pet> <u32 souls> }   on a rank-up
    //   rank changed:  <len> 14 90 00 00 <u32 pet> <u32 rank 1..3> ... (23 byte body)
    // Ranks cost 5 / 25 / 75 souls. Pet ids are 4 digits (1001 Black Dragon ... 1363).
    [PacketOpcode(PacketOpcodes.PetCollection)]
    [PacketOpcode(PacketOpcodes.PetSoulsGained)]
    [PacketOpcode(PacketOpcodes.PetSoulsSpent)]
    [PacketOpcode(PacketOpcodes.PetRankChanged)]
    public sealed class PetCollectionProcessor(PetCollectionTracker tracker, IAppSettingsService settings) : IOpcodeProcessor
    {
        private const int MaxPets = 2000;

        public void Process(Packet packet)
        {
            if (!settings.TrackPetCollection) return; // opt-in
            try { ProcessCore(packet); }
            catch (PacketParseException) { } // truncated or not one of ours
        }

        private void ProcessCore(Packet packet)
        {
            var reader = new PacketReader(packet.Data);
            var frameLength = (int)reader.ReadVarInt() + reader.Position - 4;
            var opcode = reader.ReadU16();
            // Only this frame's bytes, in case anything follows it.
            var end = Math.Min(frameLength, packet.Data.Length);
            if (end <= reader.Position) return;
            var body = new PacketReader(packet.Data[reader.Position..end]);
            var at = packet.ReceivedAtUtc;

            switch (opcode)
            {
                case PacketOpcodes.PetCollection:
                    if (TryReadCollection(body, out var ranks, out var souls))
                        tracker.OnCollection(ranks, souls, at);
                    break;
                case PacketOpcodes.PetSoulsGained:
                    if (TryReadBatch(body, out var gained)) tracker.OnSoulsGained(gained, at);
                    break;
                case PacketOpcodes.PetSoulsSpent:
                    if (TryReadBatch(body, out var spent)) tracker.OnSoulsSpent(spent, at);
                    break;
                case PacketOpcodes.PetRankChanged:
                    if (body.Remaining == 23 && body.ReadU16() == 0)
                    {
                        var pet = (int)body.ReadU32();
                        var rank = (int)body.ReadU32();
                        if (IsPetId(pet) && rank is >= 1 and <= 3) tracker.OnRankChanged(pet, rank, at);
                    }
                    break;
            }
        }

        private static bool TryReadBatch(PacketReader body, out List<(int PetId, int Amount)> entries)
        {
            entries = [];
            var count = (int)body.ReadVarInt();
            if (count <= 0 || count > MaxPets || body.Remaining != count * 8) return false;

            for (var i = 0; i < count; i++)
            {
                var pet = (int)body.ReadU32();
                var amount = (int)body.ReadU32();
                if (!IsPetId(pet) || amount is <= 0 or > 75) return false;
                entries.Add((pet, amount));
            }
            return true;
        }

        // Checked strictly: a misread here would replace the whole collection.
        private static bool TryReadCollection(PacketReader body, out Dictionary<int, int> ranks, out Dictionary<int, int> souls)
        {
            ranks = [];
            souls = [];
            if (body.Remaining < 6) return false;

            var equipped = (int)body.ReadU32();
            if (equipped != 0 && !IsPetId(equipped)) return false;

            var owned = (int)body.ReadVarInt();
            if (owned < 0 || owned > MaxPets || owned * 12 > body.Remaining) return false;
            for (var i = 0; i < owned; i++)
            {
                var pet = (int)body.ReadU32();
                var again = (int)body.ReadU32();
                var rank = (int)body.ReadU32();
                if (!IsPetId(pet) || again != pet || rank is < 1 or > 3 || !ranks.TryAdd(pet, rank)) return false;
            }
            if (equipped != 0 && !ranks.ContainsKey(equipped)) return false;

            var counted = (int)body.ReadVarInt();
            if (counted < 0 || counted > MaxPets || counted * 8 > body.Remaining) return false;
            for (var i = 0; i < counted; i++)
            {
                var pet = (int)body.ReadU32();
                var count = (int)body.ReadU32();
                // A rank 1-2 pet can't hold more than its next rank costs; locked and maxed pets aren't capped.
                var rank = ranks.GetValueOrDefault(pet);
                var cap = rank is 1 or 2 ? PetCollectionTracker.SoulsForNextRank(rank)!.Value : int.MaxValue;
                if (!IsPetId(pet) || count < 0 || count > cap || !souls.TryAdd(pet, count))
                    return false;
            }
            return true;
        }

        private static bool IsPetId(int id) => id is >= 1000 and <= 9999;
    }
}
