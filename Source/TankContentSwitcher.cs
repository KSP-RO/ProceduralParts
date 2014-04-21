using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
    public class TankContentSwitcher : PartModule
    {
        #region Callbacks
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
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
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public override void OnStart(PartModule.StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if(useVolume)
                    part.mass = mass;
                isEnabled = enabled = false;
                return;
            }

            InitializeTankType();
            if (tankVolume != 0)
                UpdateTankType();
            isEnabled = enabled = HighLogic.LoadedSceneIsEditor;
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                UpdateTankType();
        }

        #endregion

        #region Tank Volume

        /// <summary>
        /// Volume of part in kilolitres. 
        /// </summary>
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Volume", guiFormat="S4+3", guiUnits="L")]
        public float tankVolume = 0.0f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Mass")]
        public string massDisplay;

        [KSPField(isPersistant=true)]
        public float mass;

        [KSPField]
        public bool useVolume = false;

        /// <summary>
        /// Message sent from ProceduralAbstractShape when it updates.
        /// </summary>
        [PartMessageListener(typeof(ChangePartVolumeDelegate), scenes:~GameSceneFilter.Flight)]
        private void ChangeVolume(float volume)
        {
            if (!useVolume)
            {
                Debug.LogError("Updating part volume when this is not expected. Set useVolume = true in config file");
                return;
            }

            if (volume <= 0f)
                throw new ArgumentOutOfRangeException("volume");

            tankVolume = volume;

            UpdateMassAndResources(false);
        }
    
        #endregion

        #region Tank Type

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type"), UI_ChooseOption(scene=UI_Scene.Editor)]
        public string tankType;

        private TankTypeOption selectedTankType;
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
            public bool isStructural = false;
            
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
                if (other == null)
                    return 1;
                return name.CompareTo(other.name);
            }
        }

        private void InitializeTankType()
        {
            // Have to DIY to get the part options deserialized
            if (tankTypeOptionsSerialized != null)
                ObjectSerializer.Deserialize(tankTypeOptionsSerialized, out tankTypeOptions);

            Fields["tankVolume"].guiActiveEditor = useVolume; 
            Fields["massDisplay"].guiActiveEditor = useVolume;

            if (tankTypeOptions == null || tankTypeOptions.Count == 0)
            {
                Debug.LogError("*TCS* No part type options available");
                return;
            }

            BaseField field = Fields["tankType"];
            UI_ChooseOption options = (UI_ChooseOption)field.uiControlEditor;

            options.options = tankTypeOptions.ConvertAll<string>(opt => opt.name).ToArray();

            field.guiActiveEditor = (tankTypeOptions.Count > 1);

            if (string.IsNullOrEmpty(tankType))
                tankType = tankTypeOptions[0].name;
        }

        private void UpdateTankType()
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
                    return;
                }
            }

            if (selectedTankType.isStructural)
                Fields["tankVolume"].guiActiveEditor = false;
            else
                Fields["tankVolume"].guiActiveEditor = useVolume;

            UpdateMassAndResources(true);
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

        private void UpdateMassAndResources(bool typeChanged)
        {
            // Wait for the first update...
            if (selectedTankType == null)
                return;

            if (useVolume)
            {
                part.mass = mass = selectedTankType.dryDensity * tankVolume;
                MassChanged(mass);
            }

            // Update the resources list.
            if (typeChanged || !UpdateResources())
                RebuildResources();

            if (useVolume)
            {
                if (selectedTankType.isStructural)
                    massDisplay = FormatMass(part.mass);
                else
                {
                    double resourceMass = 0;
                    foreach (PartResource r in part.Resources)
                        resourceMass += r.maxAmount * r.info.density;

                    float totalMass = part.mass + (float)resourceMass;
                    massDisplay = "Dry: " + FormatMass(part.mass) + " / Wet: " + FormatMass(totalMass);
                }
            }
        }

        private static string FormatMass(float mass)
        {
            if (mass < 1.0f)
                return mass.ToStringSI(4, 6, "g");
            else
                return mass.ToStringSI(4, unit:"t");
        }

        [PartMessageListener(typeof(PartResourceInitialAmountChanged), scenes: GameSceneFilter.AnyEditor)]
        private void ResourceChanged(string name, double amount)
        {
            if(selectedTankType == null)
                return;

            TankResource tankResource = selectedTankType.resources.Find(r => r.name == name);
            if (tankResource == null || !tankResource.forceEmpty)
                return;

            PartResource resource = part.Resources[name];
            if(resource != null && resource.amount > 0)
            {
                resource.amount = 0;
                InitialAmountChanged(resource.name, resource.amount);
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

                double maxAmount = (float)Math.Round(tankRes.unitsConst + tankVolume * tankRes.unitsPerKL + mass * tankRes.unitsPerT, 2);

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

                MaxAmountChanged(partRes.name, partRes.maxAmount);
                InitialAmountChanged(partRes.name, partRes.amount);
            }

            return true;
        }

        private void RebuildResources()
        {
            // Purge the old resources
            foreach (PartResource res in part.Resources)
                Destroy(res);
            part.Resources.list.Clear();

            // Build them afresh. This way we don't need to do all the messing around with reflection
            // The downside is the UIPartActionWindow gets maked dirty and rebuit, so you can't use 
            // the sliders that affect part contents properly cos they get recreated underneith you and the drag dies.
            foreach (TankResource res in selectedTankType.resources)
            {
                double maxAmount = (double)Math.Round(res.unitsConst + tankVolume * res.unitsPerKL + part.mass * res.unitsPerT, 2);

                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("name", res.name);
                node.AddValue("maxAmount", maxAmount);
                node.AddValue("amount", res.forceEmpty ? 0 : maxAmount);
                node.AddValue("isTweakable", res.isTweakable);
                part.AddResource(node);
            }

            UIPartActionWindow window = part.FindActionWindow();
            if (window != null)
                window.displayDirty = true;

            ResourceListChanged();
        }

        #endregion
    }


    public class TankContentSwitcherRealFuels : PartModule
    {
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
        }

    #if false
        [KSPField(guiName = "Tank Type", guiActive = false, guiActiveEditor = true, isPersistant = true), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string type;
        private string oldType;

        [KSPField]
        public string[] typesAvailable;
    #endif

        /// <summary>
        /// Volume of part in kilolitres. 
        /// </summary>
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Volume", guiFormat = "S3+3", guiUnits = "L")]
        public float tankVolume = 0.0f;

        [KSPField(guiActive = false, guiActiveEditor = true, guiName = "Mass")]
        public string massDisplay;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Real Fuels"),
         UI_Toggle(enabledText="GUI", disabledText="GUI")]
        public bool showRFGUI = false;

        /// <summary>
        /// Real Fuels uses larger volumes than is really there. This factor is applied to the volume prior to it
        /// arriving in real fuels.
        /// </summary>
        [KSPField]
        public float volumeScale = 867.7219117f;

        /// <summary>
        /// Utilized fraction of the tank.
        /// </summary>
        [KSPField]
        public float utilization = 1.0f;

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        private object moduleFuelTanks;
        private MethodInfo fuelManagerGUIMethod;
        private MethodInfo changeVolume;

        public override void OnStart(PartModule.StartState state)
        {
            if (!part.Modules.Contains("ModuleFuelTanks")
                || HighLogic.LoadedSceneIsFlight)
            {
                isEnabled = enabled = false;
                return;
            }

            moduleFuelTanks = part.Modules["ModuleFuelTanks"];
            fuelManagerGUIMethod = moduleFuelTanks.GetType().GetMethod("fuelManagerGUI", BindingFlags.Public | BindingFlags.Instance);
            changeVolume = moduleFuelTanks.GetType().GetMethod("ChangeVolume", BindingFlags.Public | BindingFlags.Instance);

            GameEvents.onPartActionUIDismiss.Add(p => { if (p == part) showRFGUI = false; });

    #if false
            BaseField typeField = Fields["type"];
            if (typesAvailable != null && typesAvailable.Length > 0)
            {
                UI_ChooseOption typeEditor = (UI_ChooseOption)typeField.uiControlEditor;
                typeEditor.options = typesAvailable;

                if (type == null || !typesAvailable.Contains(type))
                    type = typesAvailable[0];
            }
            else
            {
                typeField.guiActiveEditor = false;
            }
            UpdateTankType();
    #endif
            if (tankVolume > 0)
                ChangeVolume(tankVolume);

            isEnabled = enabled = HighLogic.LoadedSceneIsEditor;
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            Fields["showRFGUI"].guiActiveEditor = EditorLogic.fetch.editorScreen == EditorLogic.EditorScreen.Parts;

            UpdateTankType();
        }

        public void OnGUI()
        {
            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !showRFGUI || !editor || editor.editorScreen != EditorLogic.EditorScreen.Parts)
            {
                return;
            }

            //Rect screenRect = new Rect(0, 365, 430, (Screen.height - 365));
            Rect screenRect = new Rect((Screen.width-438), 365, 438, (Screen.height - 365));
            //Color reset = GUI.backgroundColor;
            //GUI.backgroundColor = Color.clear;
            GUILayout.Window(part.name.GetHashCode(), screenRect, windowID => fuelManagerGUIMethod.Invoke(moduleFuelTanks, new object[] { windowID }), "Fuel Tanks for " + part.partInfo.title);
            //GUI.backgroundColor = reset;
        }

        /// <summary>
        /// Message sent from ProceduralAbstractShape when it updates.
        /// </summary>
        [PartMessageListener(typeof(ChangePartVolumeDelegate), scenes:GameSceneFilter.AnyEditor)]
        private void ChangeVolume(float volume)
        {
            // Need to call ChangeVolume in Modular Fuel Tanks
            tankVolume = volume;
            if (tankVolume > 0 && moduleFuelTanks != null)
                changeVolume.Invoke(moduleFuelTanks, new object[] { Math.Round(tankVolume * volumeScale * utilization) });
        }

        [KSPEvent (guiActive=false, active = true)]
        public void OnMassModified(BaseEventData data)
        {
            if (HighLogic.LoadedSceneIsFlight || moduleFuelTanks == null)
                return;

            UpdateMassDisplay();
        }

        [KSPEvent(guiActive=false, active=true)]
        public void OnResourcesModified(BaseEventData data)
        {
            if (HighLogic.LoadedSceneIsFlight || moduleFuelTanks == null)
                return;

            UIPartActionWindow window = part.FindActionWindow();
            if (window != null)
                window.displayDirty = true;
        }

        private void UpdateTankType()
        {
    #if false
            if (type == null || oldType == type)
                return;

            //part.SendPartMessage(PartMessageScope.Default | PartMessageScope.IgnoreAttribute, "SwitchTankType", type);
            //UpdateMassDisplay();

            oldType = type;
    #endif
        }

        private void UpdateMassDisplay()
        {
            if (part == null)
                return;

            if (part.Resources.Count == 0)
                massDisplay = FormatMass(part.mass);
            else
            {
                double resourceMass = 0;
                foreach (PartResource r in part.Resources)
                    resourceMass += r.maxAmount * r.info.density;

                float totalMass = part.mass + (float)resourceMass;
                massDisplay = "Dry: " + FormatMass(part.mass) + " / Wet: " + FormatMass(totalMass);
            }
        }

        private static string FormatMass(float mass)
        {
            if (mass < 1.0f)
                return mass.ToStringSI(4, 6, "g");
            else
                return mass.ToStringSI(4, unit:"t");
        }

    }
}