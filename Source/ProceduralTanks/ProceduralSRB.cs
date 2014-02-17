using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

public class ProceduralSRB : PartModule
{
    #region callbacks

    public override void OnInitialize()
    {
        try
        {
            if (part.Modules.Contains("ModuleEngineConfigs"))
                ChangeThrust();
        }
        catch (Exception ex)
        {
            print("OnInitialize exception: " + ex);
            throw ex;
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        LoadBells(node);
        try
        {
            if (part.Modules.Contains("ModuleEngineConfigs"))
                ChangeThrust();
        }
        catch (Exception ex)
        {
            print("OnLoad exception: " + ex);
            throw ex;
        }
    }


    public override void OnStart(PartModule.StartState state)
    {
        InitializeBells();

        if (HighLogic.LoadedSceneIsFlight)
        {
            // Do editor type updates once.
            UpdateBell();
            UpdateThrust();
        }
    }

    public void Update()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            UpdateBell();
            UpdateThrust();
        }
        else
        {
            UpdateHeat();
        }
    }

    public void UpdateTankResources()
    {
        ChangeThrust();
    }

    #endregion

    #region Objects

    [KSPField]
    public string srbBellName;

    [KSPField]
    public string bottomAttachNodeName;

    [KSPField]
    public string thrustTransform = "thrustTransform";

    #endregion

    #region Bell selection

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "SRB Type"), UI_ChooseOption()]
    public string selectedBellName;

    private SRBBellConfig selectedBell;
    private Dictionary<string, SRBBellConfig> srbConfigs;

    [SerializeField]
    private ConfigNode[] srbConfigsSerialized;

    [Serializable]
    public class SRBBellConfig : IConfigNode
    {
        [Persistent]
        public string displayName;
        [Persistent]
        public string modelName;
        [NonSerialized]
        public Transform model;

        [Persistent]
        public string srbAttachName = "srbAttach";
        [NonSerialized]
        public Transform srbAttach;

        [Persistent]
        public FloatCurve atmosphereCurve;
        [Persistent]
        public float gimbalRange;

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
            ConfigNode atmosCurveNode = node.GetNode("atmosphereCurve");
            if (atmosCurveNode != null)
            {
                atmosphereCurve = new FloatCurve();
                atmosphereCurve.Load(atmosCurveNode);
            }
        }
        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }

    }

    private void LoadBells(ConfigNode node)
    {
        if (HighLogic.LoadedScene != GameScenes.LOADING)
            return;

        srbConfigs = new Dictionary<string, SRBBellConfig>();
        srbConfigsSerialized = node.GetNodes("SRB_BELL");
        foreach (ConfigNode srbNode in srbConfigsSerialized)
        {
            SRBBellConfig conf = new SRBBellConfig();
            conf.Load(srbNode);
            srbConfigs.Add(conf.displayName, conf);
        }

    }

    private void InitializeBells()
    {
        // Attach the bell
        ProceduralPart tank = GetComponent<ProceduralPart>();
        Transform srbBell = part.FindModelTransform(srbBellName);

        // Initialize the configs.
        if (srbConfigs == null)
        {
            srbConfigs = new Dictionary<string, SRBBellConfig>();
            foreach (ConfigNode srbNode in srbConfigsSerialized)
            {
                SRBBellConfig conf = new SRBBellConfig();
                conf.Load(srbNode);
                srbConfigs.Add(conf.displayName, conf);
            }
        }

        Transform thrustTransform = srbBell.Find(this.thrustTransform);

        foreach (SRBBellConfig conf in srbConfigs.Values)
        {
            conf.model = part.FindModelTransform(conf.modelName);
            if (conf.model == null)
            {
                Debug.LogError("*PT* Unable to find model transform for SRB bell name: " + conf.modelName);
                srbConfigs.Remove(conf.modelName);
                continue;
            }
            conf.model.transform.parent = thrustTransform;

            conf.srbAttach = conf.model.Find(conf.srbAttachName);
            if (conf.srbAttach == null)
            {
                Debug.LogError("*PT* Unable to find srbAttach for SRB bell name: " + conf.modelName);
                srbConfigs.Remove(conf.modelName);
                continue;
            }

            // Only enable the colider for flight mode. This prevents any surface attachments.
            if (conf.model.collider != null)
                conf.model.collider.enabled = HighLogic.LoadedSceneIsFlight;
            conf.model.gameObject.SetActive(false);
        }

        srbBell.position = tank.transform.TransformPoint(0, -0.5f, 0);
        tank.AddAttachment(srbBell, true);

        BaseField field = Fields["selectedBellName"];
        switch (srbConfigs.Count)
        {
            case 0:
                Debug.LogError("*PT*  No SRB bells configured");
                return;
            case 1:
                field.guiActiveEditor = false;
                break;
            default:
                field.guiActiveEditor = true;
                UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;
                range.options = srbConfigs.Keys.ToArray();
                break;
        }

        if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
            selectedBellName = srbConfigs.Keys.First();

        // It makes no sense to have a thrust limiter for SRBs
        BaseField thrustLimiter = GetComponent<ModuleEngines>().Fields["thrustPercentage"];
        thrustLimiter.guiActive = false;
        thrustLimiter.guiActiveEditor = false;
    }

    private void UpdateBell()
    {
        if (selectedBell != null && selectedBellName == selectedBell.displayName)
            return;

        SRBBellConfig oldSelectedBell = selectedBell;

        if (!srbConfigs.TryGetValue(selectedBellName, out selectedBell))
        {
            if (oldSelectedBell != null)
            {
                Debug.LogWarning("*ST* Selected bell name \"" + selectedBellName + "\" does not exist. Reverting.");
                selectedBellName = oldSelectedBell.displayName;
                selectedBell = oldSelectedBell;
                return;
            }
            selectedBell = srbConfigs.Values.First();
            selectedBellName = selectedBell.displayName;
        }

        if(oldSelectedBell != null)
            oldSelectedBell.model.gameObject.SetActive(false);

        // Set the bits and pieces
        selectedBell.model.gameObject.SetActive(true);
        GetComponent<ModuleEngines>().atmosphereCurve = selectedBell.atmosphereCurve;
        GetComponent<ModuleGimbal>().gimbalRange = selectedBell.gimbalRange;

        UpdateNodePosition();
        ChangeThrust();
    }

    #endregion

    #region Thrust and heat production

    [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "SRB Burn Time", guiFormat = "F2"), UI_FloatRange(minValue = 0.25f, maxValue = 360f, stepIncrement = 0.25f)]
    public float srbBurnTime = 60f;
    private float oldSRBBurnTime; // NK for SRBs

    [KSPField(isPersistant = false, guiName = "SRB Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "F2")]
    public float srbThrust;

    [KSPField(isPersistant = false, guiName = "SRB Heat", guiActive = false, guiActiveEditor = true, guiFormat = "F2")]
    public float srbHeatProduction;


    private void UpdateThrust()
    {
        if (oldSRBBurnTime == srbBurnTime)
            return;
        ChangeThrust();
    }


    private void ChangeThrust()
    {
        ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
        PartResource solidFuel = part.Resources["SolidFuel"];
        
        srbThrust = mE.maxThrust = (float)Math.Round(mE.atmosphereCurve.Evaluate(0) * solidFuel.maxAmount * solidFuel.info.density * 9.81f / srbBurnTime, 2);
        srbHeatProduction = mE.heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((srbBurnTime + 20f), 0.75f)) * 0.5f);

        if (part.Modules.Contains("ModuleEngineConfigs"))
        {

            var mEC = part.Modules["ModuleEngineConfigs"];
            if (mEC != null)
            {
                Type engineType = mEC.GetType();
                engineType.GetMethod("ChangeThrust").Invoke(mEC, new object[] { srbThrust });
            }
        }

        oldSRBBurnTime = srbBurnTime;
    }

    private void UpdateNodePosition()
    {
        AttachNode bottom = part.findAttachNode(bottomAttachNodeName);

        bottom.position = part.transform.InverseTransformPoint(selectedBell.srbAttach.position);
    }
    #endregion

    #region Heat 

    public const float draperPoint = 525f;

    private void UpdateHeat()
    {
        // The emmissive module is too much effort to get working, just do it the easy way.
        float num = Mathf.Clamp01((part.temperature - draperPoint) / (part.maxTemp - draperPoint));
        if (float.IsNaN(num))
            num = 0f;

        Material mat = selectedBell.model.renderer.sharedMaterial;
        mat.SetColor("_EmissiveColor", new Color(num, 0, 0));
    }

    #endregion
}
