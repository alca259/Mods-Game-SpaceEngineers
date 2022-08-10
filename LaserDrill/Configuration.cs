namespace Phoenix.LaserDrill
{
    public class Configuration
    {
        public PowerSettings Compsumption { get; set; }
        public OtherSettings Others { get; set; }
    }

    public class PowerSettings
    {
        public float PowerFactor { get; set; } = 0.635f;
        public float MinPowerCompsumption { get; set; } = 0.2f;
    }

    public class OtherSettings
    {
        public float LaserDrillRange { get; set; } = 800f;
        public float TurretRange { get; set; } = 800f;
        public bool CollectStone { get; set; } = true;
        public bool LogDebugEnabled { get; set; } = false;
    }
}
