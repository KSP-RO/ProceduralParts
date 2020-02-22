namespace ProceduralParts
{
    public class TechLimit : IConfigNode
    {
        [Persistent]
        public string name;
        [Persistent]
        public float diameterMin = float.NaN;
        [Persistent]
        public float diameterMax = float.NaN;
        [Persistent]
        public float lengthMin = float.NaN;
        [Persistent]
        public float lengthMax = float.NaN;
        [Persistent]
        public float volumeMax = float.NaN;
        [Persistent]
        public bool allowCurveTweaking = true;

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            if (name == null)
            {
                name = node.GetValue("TechRequired");
            }
        }

        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }

        internal void Validate()
        {
            if (diameterMax == 0)
                diameterMax = float.PositiveInfinity;
            if (float.IsInfinity(diameterMin))
                diameterMin = 0.01f;
            if (lengthMax == 0)
                lengthMax = float.PositiveInfinity;
            if (float.IsInfinity(lengthMin))
                lengthMin = 0.01f;
            if (volumeMax == 0)
                volumeMax = float.PositiveInfinity;
        }

        internal void ApplyLimit(TechLimit limit)
        {
            if (limit.diameterMin < diameterMin)
                diameterMin = limit.diameterMin;
            if (limit.diameterMax > diameterMax)
                diameterMax = limit.diameterMax;
            if (limit.lengthMin < lengthMin)
                lengthMin = limit.lengthMin;
            if (limit.lengthMax > lengthMax)
                lengthMax = limit.lengthMax;
            if (limit.volumeMax > volumeMax)
                volumeMax = limit.volumeMax;
            if (limit.allowCurveTweaking)
                allowCurveTweaking = true;
        }

        public override string ToString() =>
            $"TechLimits(TechRequired={name} diameter=({diameterMin:G3}, {diameterMax:G3}) length=({lengthMin:G3}, {lengthMax:G3}) volumeMax={volumeMax:G3}";
    }
}
