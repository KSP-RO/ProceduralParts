using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using KSPAPIExtensions;


/// <summary>
/// Module that allows switching the contents of a pPart between different resources.
/// The possible contents are set up in the config file, and the user may switch beween
/// the different possiblities.
/// 
/// This is a bit of a light-weight version of RealFuels.
/// 
/// One can set this module up on any existing fuel pPart (Maybe with module manager if you like) 
/// if you set the volume property in the config file.
/// 
/// The class also accepts the message ChangeVolume(float volume) if attached to a dynamic resizing pPart
/// such as ProceeduralTanks.
/// </summary>
public class TankContentSwitcher : PartModule
{
    #region Callbacks

    public override void OnLoad(ConfigNode node)
    {
        if (HighLogic.LoadedScene != GameScenes.LOADING)
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
            //part.mass = dryMass;
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

    [KSPField]
    public float volMultiplier = 1.0f; // for MFS

    /// <summary>
    /// Volume of pPart in kilolitres. 
    /// </summary>
    [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Volume", guiFormat = "F3", guiUnits = "kL")]
    public float tankVolume = 0.0f;

    [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Dry Mass", guiFormat = "F3", guiUnits = "t")]
    public float dryMass = 0.0f;

    [KSPField]
    public bool useVolume = false;

    /// <summary>
    /// Message sent from ProceduralAbstractShape when it updates.
    /// </summary>
    public void ChangeVolume(float volume)
    {
        // Never update resources once flying
        if (HighLogic.LoadedSceneIsFlight)
            return;

        if (!useVolume)
        {
            Debug.LogError("Updating pPart volume when this is not expected. Set useVolume = true in config file");
            return;
        }

        if (volume <= 0f)
            throw new ArgumentOutOfRangeException("volume");

        this.tankVolume = volume;
        UpdateMassAndResources(false);
    }
    
    #endregion

    #region Tank Type

    [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Tank Type"), UI_ChooseOption(scene=UI_Scene.Editor)]
    public string tankType;

    private TankTypeOption selectedTankType;

    //[SerializeField]  <-- this doesn't work. Don't know why. Have given up trying to figure it out.
    private List<TankTypeOption> tankTypeOptions;

    // Serialize to bytes instead.
    [SerializeField]
    private byte[] tankTypeOptionsSerialized;

    [Serializable]
    public class TankTypeOption : IConfigNode
    {
        [Persistent]
        public string optionName;
        [Persistent]
        public float dryDensity;
        [Persistent]
        public bool isStructural = false;
        [Persistent]
        public List<TankResource> resources;

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            if (resources == null)
                resources = new List<TankResource>();
        }
        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }
    }

    [Serializable]
    public class TankResource : IConfigNode
    {
        [Persistent]
        public string resourceName;
        [Persistent]
        public float unitsPerKL;
        [Persistent]
        public float unitsPerT;
        [Persistent]
        public bool isTweakable = true;

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
        }
        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }
    }

    private void InitializeTankType()
    {
        // Have to DIY to get the pPart options deserialized
        if (tankTypeOptionsSerialized != null)
            ObjectSerializer.Deserialize(tankTypeOptionsSerialized, out tankTypeOptions);

        Fields["tankVolume"].guiActiveEditor = useVolume;
        Fields["dryMass"].guiActiveEditor = useVolume;

        if (tankTypeOptions == null || tankTypeOptions.Count == 0)
        {
            Debug.LogError("*TCS* No pPart type options available");
            return;
        }

        BaseField field = Fields["tankType"];
        UI_ChooseOption options = (UI_ChooseOption)field.uiControlEditor;

        options.options = tankTypeOptions.ConvertAll<string>(opt => opt.optionName).ToArray();

        field.guiActiveEditor = (tankTypeOptions.Count > 1);

        if (string.IsNullOrEmpty(tankType))
            tankType = tankTypeOptions[0].optionName;
    }

    private void UpdateTankType()
    {
        if (tankTypeOptions == null || ( selectedTankType != null && selectedTankType.optionName == tankType))
            return;

        TankTypeOption oldTankType = selectedTankType;

        selectedTankType = tankTypeOptions.Find(opt => opt.optionName == tankType);

        if (selectedTankType == null)
        {
            if (oldTankType == null)
            {
                Debug.LogWarning("*TCS* Initially selected pPart type '" + tankType + "' does not exist, reverting to default");
                selectedTankType = tankTypeOptions[0];
                tankType = selectedTankType.optionName;
            }
            else 
            {
                Debug.LogWarning("*TCS* Selected pPart type '" + tankType + "' does not exist, reverting to previous");
                selectedTankType = oldTankType;
                tankType = selectedTankType.optionName;
                return;
            }
        }

        if (selectedTankType.isStructural)
        {
            Fields["dryMass"].guiName = "Mass";
            Fields["tankVolume"].guiActiveEditor = false;
        }
        else
        {
            Fields["dryMass"].guiName = "Dry Mass";
            Fields["tankVolume"].guiActiveEditor = useVolume;
        }

        UpdateMassAndResources(true);
    }

    #endregion

    #region Resources

    private void UpdateMassAndResources(bool typeChanged)
    {
        // Wait for the first update...
        if (selectedTankType == null)
            return;

        if (useVolume)
            dryMass = part.mass = Mathf.Round(selectedTankType.dryDensity * tankVolume * volMultiplier * 1000f) / 1000f;

        // Update the resources list.
        UIPartActionWindow window = FindWindow();
        if (window == null)
            goto reinitialize;

        if (!typeChanged) 
        {
            // Hopefully we can just fiddle with the existing part resources.
            if (part.Resources.Count != selectedTankType.resources.Count)
            {
                Debug.LogWarning("*TCS* Selected and existing resource counts differ");
                goto reinitialize;
            }

            for (int i = 0; i < part.Resources.Count; ++i)
            {
                PartResource partRes = part.Resources[i];
                TankResource tankRes = selectedTankType.resources[i];

                if (partRes.resourceName != tankRes.resourceName)
                {
                    Debug.LogWarning("*TCS* Selected and existing resource names differ");
                    goto reinitialize;
                }

                double amount = (float)Math.Round(tankVolume * volMultiplier * tankRes.unitsPerKL + dryMass * tankRes.unitsPerT , 2);
                double oldFillFraction = partRes.amount / partRes.maxAmount;

                partRes.maxAmount = amount;
                partRes.amount = double.IsNaN(oldFillFraction)?amount:Math.Round(amount * oldFillFraction, 2);

                if (partRes.isTweakable && !UpdateWindow(window, partRes))
                    goto reinitialize;
            }
            part.SendPartMessage("ResourcesChanged");
            return;
        }

        reinitialize:
        
        // Purge the old resources
        foreach (PartResource res in part.Resources)
            Destroy(res);
        part.Resources.list.Clear();

        // Build them afresh. This way we don't need to do all the messing around with reflection
        // The downside is the UIPartActionWindow gets maked dirty and rebuit, so you can't use 
        // the sliders that affect pPart contents properly cos they get recreated underneith you and the drag dies.
        foreach (TankResource res in selectedTankType.resources)
        {
            double amount = (double)Math.Round(tankVolume * volMultiplier * res.unitsPerKL + dryMass * res.unitsPerT, 2);

            ConfigNode node = new ConfigNode("RESOURCE");
            node.AddValue("name", res.resourceName);
            node.AddValue("amount", amount);
            node.AddValue("maxAmount", amount);
            node.AddValue("isTweakable", res.isTweakable);
            part.AddResource(node);
        }

        if (window != null)
            window.displayDirty = true;

        part.SendPartMessage("ResourcesChanged");
    }

    #endregion

    #region Nasty Reflection Code

    private FieldInfo windowListField;
    private UIPartActionWindow FindWindow()
    {
        // We need to do quite a bit of piss-farting about with reflection to 
        // dig the thing out.
        UIPartActionController controller = UIPartActionController.Instance;

        if(windowListField == null) 
        {
            Type cntrType = typeof(UIPartActionController);
            foreach (FieldInfo info in cntrType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (info.FieldType == typeof(List<UIPartActionWindow>))
                {
                    windowListField = info;
                    goto foundField;
                }
            }
            Debug.LogWarning("*TCS* Unable to find UIPartActionWindow list");
            return null;
        }
    foundField:

        foreach (UIPartActionWindow window in (List<UIPartActionWindow>)windowListField.GetValue(controller))
            if (window.part == part)
                return window;

        return null;
    }

    private FieldInfo actionItemListField;
    private bool UpdateWindow(UIPartActionWindow window, PartResource res)
    {
        if (actionItemListField == null)
        {
            Type cntrType = typeof(UIPartActionWindow);
            foreach (FieldInfo info in cntrType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (info.FieldType == typeof(List<UIPartActionItem>))
                {
                    actionItemListField = info;
                    goto foundField;
                }
            }
            Debug.LogWarning("*TCS* Unable to find UIPartActionWindow list");
            return false;
        }
    foundField:

        UIPartActionResourceEditor editor;
        foreach (UIPartActionItem item in (List<UIPartActionItem>)actionItemListField.GetValue(window))
        {
            UIPartActionResourceEditor ed = item as UIPartActionResourceEditor;
            if (ed == null)
                continue;
            if (ed.resourceName.Text == res.resourceName)
            {
                editor = ed;
                goto foundEditor;
            }
        }
        Debug.LogWarning("*TCS* Unable to find resource editor in window");
        return false;

    foundEditor:

        // Fortunatly as we're keeping proportions, we don't need to update the slider.
        editor.resourceMax.Text = res.maxAmount.ToString("F1");
        editor.resourceAmnt.Text = res.amount.ToString("F1");

        return true;
    }


    #endregion
}
