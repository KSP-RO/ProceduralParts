using UnityEngine;

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
        public float volumeMin = float.NaN;
        [Persistent]
        public float volumeMax = float.NaN;
        [Persistent]
        public bool allowCurveTweaking = true;

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            name ??= node.GetValue("TechRequired");
        }

        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }

        internal void Validate()
        {
            if (diameterMax == 0)
                diameterMax = float.PositiveInfinity;
            if (float.IsInfinity(diameterMin) || diameterMin > diameterMax)
                diameterMin = 0.01f;
            if (lengthMax == 0)
                lengthMax = float.PositiveInfinity;
            if (float.IsInfinity(lengthMin) || lengthMin > lengthMax)
                lengthMin = 0.01f;
            if (volumeMax == 0)
                volumeMax = float.PositiveInfinity;
            if (float.IsInfinity(volumeMin) || float.IsNaN(volumeMin) || volumeMin > volumeMax || volumeMin < 0)
                volumeMin = 0;
        }

        internal void ApplyLimit(TechLimit limit)
        {
            diameterMin = Mathf.Min(diameterMin, limit.diameterMin);
            lengthMin = Mathf.Min(lengthMin, limit.lengthMin);
            volumeMin = Mathf.Min(volumeMin, limit.volumeMin);
            diameterMax = Mathf.Max(diameterMax, limit.diameterMax);
            lengthMax = Mathf.Max(lengthMax, limit.lengthMax);
            volumeMax = Mathf.Max(volumeMax, limit.volumeMax);
            allowCurveTweaking |= limit.allowCurveTweaking;
        }

        public override string ToString() =>
            $"TechLimits (TechRequired={name}) diameter=({diameterMin:G3}, {diameterMax:G3}) length=({lengthMin:G3}, {lengthMax:G3}) volume=({volumeMin:G3}, {volumeMax:G3})";
    }
}
