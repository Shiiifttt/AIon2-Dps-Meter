namespace AionDpsMeter.Core.GameData.Contracts
{
    public sealed class ClassData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
    }

    public sealed class ClassesFile
    {
        public List<ClassData> Classes { get; set; } = [];
    }

    public sealed class MobData
    {
        public string Name { get; set; } = string.Empty;
        public bool IsBoss { get; set; }
        public bool isDummy { get; set; }
    }

    public sealed class PetData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Fera, Natura, Cogni, Varian or Special.</summary>
        public string Genus { get; set; } = string.Empty;
        /// <summary>Souls drop from mobs (the rest come from shops, events and the like).</summary>
        public bool Farmable { get; set; }
    }

    public sealed class PetsFile
    {
        public List<PetData> Pets { get; set; } = [];
    }

}
