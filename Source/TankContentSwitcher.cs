using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        #region Callbacks
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
            //this.RegisterOnUpdateEditor(OnUpdateEditor);
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                OnUpdateEditor();
        }

        public override void OnLoad(ConfigNode node)
        {
            if (!GameSceneFilter.AnyInitializing.IsLoaded())
                return;
        
            tankTypeOptions = new List<TankTypeOption>();
            foreach (ConfigNode optNode in node.GetNodes("TANK_TYPE_OPTION"))
            {
                TankTypeOption option = new TankTypeOption();
                option.Load(optNode);
                tankTypeOptions.Add(option);
            }

            // Unity for some daft reason, and not as according to it's own documentation, won't clone 
            // serializable member fields. Lets DIY.
            tankTypeOptionsSerialized = ObjectSerializer.Serialize(tankTypeOptions);


            try
            {
                if (bool.Parse(node.GetValue("useVolume")))
                {
                    tankVolumeName = PartVolumes.Tankage.ToString();
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch { }
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (tankVolumeName != null)
                {
                    part.mass = mass;
                    MassChanged(mass);
                }
                isEnabled = enabled = false;
                return;
            }

            InitializeTankType();
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (tankVolume != 0)
                UpdateTankType(true);
            isEnabled = enabled = HighLogic.LoadedSceneIsEditor;
        }

        public void OnUpdateEditor()
        {
            UpdateTankType();
        }

        #endregion

        #region Tank Volume

        /// <summary>
        /// Volume of part in kilolitres. 
        /// </summary>
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Volume", guiFormat="S4+3", guiUnits="L")]
        public float tankVolume;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass")]
        public string massDisplay;

        [KSPField(isPersistant=true)]
        public float mass;

        [KSPField]
        public string tankVolumeName;

        /// <summary>
        /// Message sent from ProceduralAbstractShape when it updates.
        /// </summary>
        [PartMessageListener(typeof(PartVolumeChanged), scenes:~GameSceneFilter.Flight)]
        public void ChangeVolume(string volumeName, float volume)
        {
            if (volumeName != tankVolumeName)
                return;

            if (volume <= 0f)
                throw new ArgumentOutOfRangeException("volume");

            tankVolume = volume;

            UpdateMassAndResources(false);
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }
    
        #endregion

        #region Tank Type

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type"), UI_ChooseOption(scene=UI_Scene.Editor)]
        public string tankType;

        private TankTypeOption selectedTankType;
        
        public float GetCurrentCostMult()
        {
            if (null != selectedTankType)
                return selectedTankType.costMultiplier;
            else
                return 0; // tank type has not been initialized yet
        }
        private List<TankTypeOption> tankTypeOptions;

        // This should be private, but there's a bug in KSP.
        [SerializeField]
        public byte[] tankTypeOptionsSerialized;

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
            // Have to DIY to get the part options deserialized
            if (tankTypeOptionsSerialized != null)
                ObjectSerializer.Deserialize(tankTypeOptionsSerialized, out tankTypeOptions);

            if(HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX)
            {
                tankTypeOptions = tankTypeOptions.Where(to => string.IsNullOrEmpty(to.techRequired) || ResearchAndDevelopment.GetTechnologyState(to.techRequired) == RDTech.State.Available).ToList();
            }

            Fields["tankVolume"].guiActiveEditor = tankVolumeName != null; 
            Fields["massDisplay"].guiActiveEditor = tankVolumeName != null;

            if (tankTypeOptions == null || tankTypeOptions.Count == 0)
            {
                Debug.LogError("*TCS* No part type options available");
                return;
            }

            BaseField field = Fields["tankType"];
            UI_ChooseOption options = (UI_ChooseOption)field.uiControlEditor;

            options.options = tankTypeOptions.ConvertAll(opt => opt.name).ToArray();

            field.guiActiveEditor = (tankTypeOptions.Count > 1);

            if (string.IsNullOrEmpty(tankType))
                tankType = tankTypeOptions[0].name;
        }

        private void UpdateTankType(bool init = false)
        {
            if (tankTypeOptions == null || ( selectedTankType != null && selectedTankType.name == tankType))
                return;

            TankTypeOption oldTankType = selectedTankType;

            selectedTankType = tankTypeOptions.Find(opt => opt.name == tankType);

            if (selectedTankType == null)
            {
                if (oldTankType == null)
                {
                    Debug.LogWarning("*TCS* Initially selected part type '" + tankType + "' does not exist, reverting to default");
                    selectedTankType = tankTypeOptions[0];
                    tankType = selectedTankType.name;
                }
                else 
                {
                    Debug.LogWarning("*TCS* Selected part type '" + tankType + "' does not exist, reverting to previous");
                    selectedTankType = oldTankType;
                    tankType = selectedTankType.name;
                    if (HighLogic.LoadedSceneIsEditor)
                        GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                    return;
                }
            }

            if (selectedTankType.isStructural)
                Fields["tankVolume"].guiActiveEditor = false;
            else
                Fields["tankVolume"].guiActiveEditor = tankVolumeName != null;

            UpdateMassAndResources(true, init);
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        #endregion

        #region Resources

        [PartMessageEvent]
        public event PartMassChanged MassChanged;
        [PartMessageEvent]
        public event PartResourceListChanged ResourceListChanged;
        [PartMessageEvent]
        public event PartResourceMaxAmountChanged MaxAmountChanged;
        [PartMessageEvent]
        public event PartResourceInitialAmountChanged InitialAmountChanged;

        private void UpdateMassAndResources(bool typeChanged, bool keepAmount = false) // keep amount when rebuild (for saved part loading)
        {
            // Wait for the first update...
            if (selectedTankType == null)
                return;

            if (tankVolumeName != null)
            {
                mass = selectedTankType.dryDensity * tankVolume + selectedTankType.massConstant;

                if (PPart != null)
                    mass *= PPart.CurrentShape.massMultiplier;

                part.mass = mass;
                MassChanged(mass);
            }

            // Update the resources list.
            if (typeChanged || !UpdateResources())
                RebuildResources(keepAmount);

            if (tankVolumeName != null)
            {
                double resourceMass = part.Resources.Cast<PartResource>().Sum(r => r.maxAmount*r.info.density);

                float totalMass = part.mass + (float)resourceMass;
                if (selectedTankType.isStructural)
                    massDisplay = MathUtils.FormatMass(totalMass);
                else
                    massDisplay = "Dry: " + MathUtils.FormatMass(part.mass) + " / Wet: " + MathUtils.FormatMass(totalMass);
            }
        }

        [PartMessageListener(typeof(PartResourceInitialAmountChanged), scenes: GameSceneFilter.AnyEditor)]
        public void ResourceChanged(PartResource resource, double amount)
        {
            if(selectedTankType == null)
                return;

            TankResource tankResource = selectedTankType.resources.Find(r => r.name == name);
            if (tankResource == null || !tankResource.forceEmpty)
                return;

            if(resource != null && resource.amount > 0)
            {
                resource.amount = 0;
                InitialAmountChanged(resource, resource.amount);
            }
        }

        private bool UpdateResources()
        {
            // Hopefully we can just fiddle with the existing part resources.
            // This saves having to make the window dirty, which breaks dragging on sliders.
            if (part.Resources.Count != selectedTankType.resources.Count)
            {
                Debug.LogWarning("*TCS* Selected and existing resource counts differ");
                return false;
            }

            for (int i = 0; i < part.Resources.Count; ++i)
            {
                PartResource partRes = part.Resources[i];
                TankResource tankRes = selectedTankType.resources[i];

                if (partRes.resourceName != tankRes.name)
                {
                    Debug.LogWarning("*TCS* Selected and existing resource names differ");
                    return false;
                }

                //double maxAmount = (float)Math.Round(tankRes.unitsConst + tankVolume * tankRes.unitsPerKL + mass * tankRes.unitsPerT, 2);
                double maxAmount = CalculateMaxResourceAmount(tankRes);

                // ReSharper disable CompareOfFloatsByEqualityOperator
                if (partRes.maxAmount == maxAmount)
                    continue;

                if (tankRes.forceEmpty)
                    partRes.amount = 0;
                else if (partRes.maxAmount == 0)
                    partRes.amount = maxAmount;
                else
                {
                    SIPrefix pfx = maxAmount.GetSIPrefix();
                    partRes.amount = pfx.Round(partRes.amount * maxAmount / partRes.maxAmount, 4);
                }
                partRes.maxAmount = maxAmount;
                // ReSharper restore CompareOfFloatsByEqualityOperator

                MaxAmountChanged(partRes, partRes.maxAmount);
                InitialAmountChanged(partRes, partRes.amount);
            }

            return true;
        }

        private double CalculateMaxResourceAmount(TankResource res)
        {
            double shapeMultiplier = 0;

            if (PPart != null)
                if (PPart.CurrentShape != null)
                    shapeMultiplier = PPart.CurrentShape.resourceMultiplier;

            return Math.Round((res.unitsConst + tankVolume * res.unitsPerKL + part.mass * res.unitsPerT) * shapeMultiplier, 2);
        }

        private void RebuildResources(bool keepAmount = false)
        {
            List<PartResource> partResources = new List<PartResource>();

            // Purge the old resources
            foreach (PartResource res in part.Resources)
            {
                if (keepAmount && selectedTankType.resources.Any(tr => tr.name == res.resourceName))
                    partResources.Add(res);
                else
                    Destroy(res);
            }

            part.Resources.list.Clear();

            // Build them afresh. This way we don't need to do all the messing around with reflection
            // The downside is the UIPartActionWindow gets maked dirty and rebuit, so you can't use 
            // the sliders that affect part contents properly cos they get recreated underneith you and the drag dies.
            foreach (TankResource res in selectedTankType.resources)
            {
                //double maxAmount = Math.Round(res.unitsConst + tankVolume * res.unitsPerKL + part.mass * res.unitsPerT, 2);
                double maxAmount = CalculateMaxResourceAmount(res);

                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("name", res.name);
                node.AddValue("maxAmount", maxAmount);

                PartResource partResource = partResources.FirstOrDefault(r => r.resourceName == res.name);
                if (!res.forceEmpty && null != partResource)
                { 
                    node.AddValue("amount", Math.Min(partResource.amount, maxAmount));
                }
                else
                    node.AddValue("amount", res.forceEmpty ? 0 : maxAmount);

                node.AddValue("isTweakable", res.isTweakable);
                part.AddResource(node);
            }

            foreach (PartResource res in partResources)
                Destroy(res);

            UIPartActionWindow window = part.FindActionWindow();
            if (window != null)
                window.displayDirty = true;

            ResourceListChanged();
        }

        #endregion

        #region Message passing for EPL

        // Extraplanetary launchpads needs these messages sent.
        // From the next update of EPL, this won't be required.

        [PartMessageListener(typeof(PartResourcesChanged))]
        public void ResourcesModified()
        {
            BaseEventData data = new BaseEventData(BaseEventData.Sender.USER);
            data.Set("part", part);
            part.SendEvent("OnResourcesModified", data, 0);
        }

        private float oldmass;

        [PartMessageListener(typeof(PartMassChanged))]
        public void MassModified(float paramMass)
        {
            BaseEventData data = new BaseEventData(BaseEventData.Sender.USER);
            data.Set("part", part);
            data.Set<float>("oldmass", oldmass);
            part.SendEvent("OnMassModified", data, 0);

            oldmass = paramMass;
        }

        #endregion
        
        #region Mass
		public float GetModuleMass(float defaultMass)
		{
			return part.mass - defaultMass;
		}
		#endregion

        public ProceduralPart PPart
        {
            get { return _pPart ?? (_pPart = GetComponent<ProceduralPart>()); }
        }
        private ProceduralPart _pPart;
    }
}
