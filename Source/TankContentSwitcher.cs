using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralParts
{
    /// <summary>
    /// Module that allows switching the contents of a part between different resources.
    /// The possible contents are set up in the config file, and the user may switch beween
    /// the different possiblities.
    /// 
    /// This is a bit of a light-weight version of RealFuels.
    /// 
    /// One can set this module up on any existing fuel part (Maybe with module manager if you like) 
    /// if you set the volume property in the config file.
    /// 
    /// The class also accepts the message ChangeVolume(float volume) if attached to a dynamic resizing part
    /// such as ProceeduralTanks.
    /// </summary>
    public class TankContentSwitcher : PartModule, IPartMassModifier, ICostMultiplier
    {
        private static readonly string ModTag = "[ProceduralParts.TankContentSwitcher]";

        #region IPartMassModifier implementation

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => mass - defaultMass;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion

        #region Unity Callbacks

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                foreach (ConfigNode optNode in node.GetNodes("TANK_TYPE_OPTION"))
                {
                    TankTypeOption option = new TankTypeOption();
                    option.Load(optNode);
                    tankTypeOptions.Add(option.name, option);
                }
                if (string.IsNullOrEmpty(tankType) || !tankTypeOptions.Keys.Contains(tankType))
                    tankType = tankTypeOptions.Keys.First();
            }
            bool useV = false;
            if (node.TryGetValue("useVolume", ref useV) && useV)
                tankVolumeName = PartVolumes.Tankage.ToString();
        }

        public override void OnStart(StartState state)
        {
            InitializeTankType();

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(volumeDisplay)].guiActiveEditor = !string.IsNullOrEmpty(tankVolumeName);
                Fields[nameof(massDisplay)].guiActiveEditor = !string.IsNullOrEmpty(tankVolumeName);
                Fields[nameof(tankType)].guiActiveEditor = tankTypeOptions.Count > 1;
                // Don't listen for OnPartVolumeChanged until we have started.
                Events[nameof(OnPartVolumeChanged)].active = true;

                UI_ChooseOption options = Fields[nameof(tankType)].uiControlEditor as UI_ChooseOption;
                options.options = tankTypeOptions.Keys.ToArray();
                options.onFieldChanged += OnTankTypeChangedWithSymmetry;
            }
            tankVolume = PPart?.CurrentShape ? PPart.CurrentShape.Volume : 0;
            volumeDisplay = tankVolume.ToStringSI(4, 3, "L");
        }

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);
            if (HighLogic.LoadedSceneIsEditor)
                this.OnTankTypeChanged(Fields[nameof(tankType)], null);
            else
            {
                UpdateTankMass();
                UpdateMassDisplay();
            }

            // Done here and done in OnStart in PP to get ordering right
            // since these get fired in reverse-add order
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Add(OnShipModified);
        }

        protected void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnShipModified);
        }

        #endregion

        #region Fields Definitions

        /// <summary>
        /// Volume of part in kilolitres. 
        /// </summary>
        [KSPField(isPersistant = true)]
        public float tankVolume;

        [KSPField(guiActiveEditor = true, guiName = "Mass", groupName = "TCS", groupDisplayName = "TankContentSwitcher")]
        public string massDisplay;

        [KSPField(guiActiveEditor = true, guiName = "Volume", groupName = "TCS")]
        public string volumeDisplay;

        [KSPField(isPersistant = true)]
        public float mass;

        [KSPField]
        public string tankVolumeName;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tank Type", groupName = "TCS"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string tankType;
        private TankTypeOption SelectedTankType => tankTypeOptions.ContainsKey(tankType) ? tankTypeOptions[tankType] : null;

        public float GetCurrentCostMult() => SelectedTankType is TankTypeOption ? SelectedTankType.costMultiplier : 0;
        private readonly Dictionary<string, TankTypeOption> tankTypeOptions = new Dictionary<string, TankTypeOption>();

        #endregion

        #region Tank Type

        [Serializable]
        public class TankTypeOption : IConfigNode
        {
            [Persistent]
            public string name;
            [Persistent]
            public float dryDensity;
            [Persistent]
            public float massConstant;
            [Persistent]
            public bool isStructural = false;
            [Persistent]
            public float costMultiplier = 1.0f;
            [Persistent]
            public string techRequired;
            
            public List<TankResource> resources;

            public void Load(ConfigNode node)
            {
                ConfigNode.LoadObjectFromConfig(this, node);
                resources = new List<TankResource>();
                foreach (ConfigNode resNode in node.GetNodes("RESOURCE"))
                {
                    TankResource resource = new TankResource();
                    resource.Load(resNode);
                    resources.Add(resource);
                }
                resources.Sort();
            }
            public void Save(ConfigNode node)
            {
                ConfigNode.CreateConfigFromObject(this, node);
                foreach (TankResource resource in resources)
                {
                    ConfigNode resNode = new ConfigNode("RESOURCE");
                    resource.Save(resNode);
                    node.AddNode(resNode);
                }
            }
            public bool TechAvailable =>
                !(HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX) ||
                 string.IsNullOrEmpty(techRequired) ||
                 ResearchAndDevelopment.GetTechnologyState(techRequired) == RDTech.State.Available;

        }

        [Serializable]
        public class TankResource : IConfigNode, IComparable<TankResource>
        {
            [Persistent]
            public string name;
            [Persistent]
            public float unitsPerKL;
            [Persistent]
            public float unitsPerT;
            [Persistent]
            public float unitsConst;
            [Persistent]
            public bool isTweakable = true;
            [Persistent]
            public bool forceEmpty = false;
            [Persistent]
            public string flowMode = "Both";

            public void Load(ConfigNode node)
            {
                ConfigNode.LoadObjectFromConfig(this, node);
            }
            public void Save(ConfigNode node)
            {
                ConfigNode.CreateConfigFromObject(this, node);
            }

            int IComparable<TankResource>.CompareTo(TankResource other)
            {
                return other == null ? 1 : 
                    string.Compare(name, other.name, StringComparison.Ordinal);
            }

        }

        private void InitializeTankType()
        {
            tankTypeOptions.Clear();
            foreach (var kvp in part.partInfo.partPrefab.FindModuleImplementing<TankContentSwitcher>().tankTypeOptions)
            {
                if (kvp.Value.TechAvailable)
                    tankTypeOptions.Add(kvp.Key, kvp.Value);
            }

            if (tankTypeOptions == null || tankTypeOptions.Count == 0)
            {
                Debug.LogError("*TCS* No part type options available");
                return;
            }
            if (string.IsNullOrEmpty(tankType) || !tankTypeOptions.Keys.Contains(tankType))
                tankType = tankTypeOptions.Keys.First();
            Fields[nameof(volumeDisplay)].guiActiveEditor = SelectedTankType.isStructural ? false : tankVolumeName != null;
        }

        #endregion

        #region Updaters / Callbacks

        /// <summary>
        /// Message sent from ProceduralAbstractShape when it updates.
        /// </summary>
        [KSPEvent(guiActive = false, active = false)]
        public void OnPartVolumeChanged(BaseEventDetails data)
        {
            string volumeName = data.Get<string>("volName");
            double volume = data.Get<double>("newTotalVolume");

            if (volumeName != tankVolumeName)
            {
                Debug.LogWarning($"{ModTag} OnPartVolumeChanged for {this}, volumeName {volumeName} vs tankVolumeName {tankVolumeName}");
                return;
            }

            if (volume <= 0f)
                throw new ArgumentOutOfRangeException("volume");

            tankVolume = Convert.ToSingle(volume);
            volumeDisplay = tankVolume.ToStringSI(4, 3, "L");
            UpdateTankMass();
            UpdateResourceAmounts();
            UpdateMassDisplay();

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        private void OnTankTypeChangedWithSymmetry(BaseField f, object obj)
        {
            OnTankTypeChanged(f, obj);
            foreach (Part p in part.symmetryCounterparts)
                p.FindModuleImplementing<TankContentSwitcher>()?.OnTankTypeChanged(f, obj);
            MonoUtilities.RefreshPartContextWindow(part);
        }

        private void OnTankTypeChanged(BaseField f, object obj)
        {
            UpdateTankMass();
            string s = obj as string;
            if (!string.IsNullOrEmpty(s) && tankTypeOptions.ContainsKey(s) && tankTypeOptions[s] is TankTypeOption oldTTO)
            {
                foreach (TankResource tr in oldTTO.resources)
                {
                    if (part.Resources.Get(tr.name) is PartResource pr)
                    {
                        part.RemoveResource(pr);
                    }
                }
            }
            foreach (TankResource tr in SelectedTankType.resources)
            {
                double maxAmount = CalculateMaxResourceAmount(tr);
                // On initialization, old TankTypeOptions is null but there is still a list of PartResources, so apply their value.
                double amount = (part.Resources.Get(tr.name) is PartResource pr) ? pr.amount : maxAmount;

                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("name", tr.name);
                node.AddValue("maxAmount", maxAmount);
                node.AddValue("amount", amount);
                node.AddValue("flowMode", tr.flowMode);
                node.AddValue("isTweakable", tr.isTweakable);
                part.SetResource(node);
            }
            Fields[nameof(volumeDisplay)].guiActiveEditor = SelectedTankType.isStructural ? false : tankVolumeName != null;
            UpdateMassDisplay();
        }

        private void UpdateTankMass()
        {
            if (!string.IsNullOrEmpty(tankVolumeName))
            {
                mass = SelectedTankType.dryDensity * tankVolume + SelectedTankType.massConstant;
                if (PPart != null)
                {
                    mass *= PPart.CurrentShape.massMultiplier;
                    if (PPart.density > 0f)
                        mass *= PPart.density * (1f / ProceduralPart.NominalDensity);

                }
            }
        }

        private void UpdateMassDisplay() 
        {
            if (!string.IsNullOrEmpty(tankVolumeName))
            {
                double resourceMass = part.Resources.Cast<PartResource>().Sum(r => r.maxAmount * r.info.density);
                float totalMass = mass + Convert.ToSingle(resourceMass);
                if (PPart != null)
                    totalMass += PPart.moduleMass;
                massDisplay = (SelectedTankType.isStructural) ?
                                MathUtils.FormatMass(totalMass) :
                                $"Dry: {MathUtils.FormatMass(mass)} / Wet: {MathUtils.FormatMass(totalMass)}";
            }
        }

        private void UpdateResourceAmounts()
        {
            foreach (TankResource tr in SelectedTankType.resources)
            {
                if (part.Resources.Get(tr.name) is PartResource pr)
                {
                    double maxAmount = CalculateMaxResourceAmount(tr);
                    pr.amount = Math.Round(pr.amount * maxAmount / pr.maxAmount, 4);
                    pr.maxAmount = maxAmount;
                    pr.amount = Math.Min(pr.amount, maxAmount);
                    if (part.PartActionWindow?.ListItems.FirstOrDefault(x => (x as UIPartActionResourceEditor)?.Resource == pr) is UIPartActionResourceEditor pare)
                    {
                        pare.resourceMax.text = KSPUtil.LocalizeNumber(pr.maxAmount, "F1");
                        pare.UpdateItem();
                        pare.slider.onValueChanged.Invoke(pare.slider.value);
                    }
                }
                else
                {
                    Debug.LogError($"{ModTag} UpdateResourceAmounts for {tr.name} found no matching PartResource!");
                }
            }
        }

        private double CalculateMaxResourceAmount(TankResource res)
        {
            double shapeMultiplier = (PPart is ProceduralPart && PPart.CurrentShape is ProceduralAbstractShape) ?
                                    PPart.CurrentShape.resourceMultiplier : 0;
            return Math.Round((res.unitsConst + (tankVolume * res.unitsPerKL) + (mass * res.unitsPerT)) * shapeMultiplier, 2);
        }

        #endregion

        public ProceduralPart PPart => _pPart != null ? _pPart : (_pPart = GetComponent<ProceduralPart>());
        private ProceduralPart _pPart;

        private void OnShipModified(ShipConstruct _)
        {
            UpdateTankMass();
            UpdateMassDisplay();
        }
    }
}
