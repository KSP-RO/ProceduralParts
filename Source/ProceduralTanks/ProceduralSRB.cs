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

    public override void OnSave(ConfigNode node)
    {
        // Force saved value for enabled to be true.
        node.SetValue("isEnabled", "True");
    }

    public override void OnStart(PartModule.StartState state)
    {
        InitializeBells();

        if (HighLogic.LoadedSceneIsFlight)
        {            
            // Need to update the thrust at least once, then disable
            UpdateThrust();
            isEnabled = enabled = false;
        }
    }

    private AttachNode bottomAttachNode;

    public void Update()
    {
        try
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateBell();
                UpdateThrust();

                UpdateAttachedPart();
            }
            else
            {
                UpdateHeat();
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            enabled = false;
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

    [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "SRB Type"), UI_ChooseOption(scene = UI_Scene.Editor)]
    public string selectedBellName;

    [KSPField(isPersistant = false, guiName = "ISP", guiActive = false, guiActiveEditor = true)]
    public string srbISP;

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

        ProceduralPart tank = GetComponent<ProceduralPart>();

        // Attach the bell.
        Transform srbBell = part.FindModelTransform(srbBellName);
        srbBell.position = tank.transform.TransformPoint(0, -0.5f, 0);
        tank.AddAttachment(srbBell, true);

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
            if (HighLogic.LoadedSceneIsEditor && conf.model.collider != null)
                Destroy(conf.model.collider);

            conf.model.gameObject.SetActive(false);
        }

        if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
            selectedBellName = srbConfigsSerialized[0].GetValue("displayName");

        selectedBell = srbConfigs[selectedBellName];

        // Move the bottom attach node into position.
        if (HighLogic.LoadedSceneIsEditor)
        {
            bottomAttachNode = part.findAttachNode(bottomAttachNodeName);
            Vector3 delta = selectedBell.srbAttach.position - selectedBell.model.position;
            bottomAttachNode.originalPosition = bottomAttachNode.position += part.transform.InverseTransformDirection(delta);
            if (bottomAttachNode.attachedPart != null && bottomAttachNode.attachedPart.transform == part.transform.parent)
            {
                // Because the parent is attached using our node, we need to move it out by the
                // node offset as it will get moved back again by the same amount when it gets attached
                // Children are attached using their own nodes, so their original offset ends up correct.
                bottomAttachNode.attachedPart.transform.position += delta;
                part.transform.position -= delta;
                oldAttachedPart = bottomAttachNode.attachedPart;
                //Debug.LogWarning("Moving bottom attach " + delta);
            }
        }

        // Set the bell active and do the bits and pieces.
        selectedBell.model.gameObject.SetActive(true);
        GetComponent<ModuleEngines>().atmosphereCurve = selectedBell.atmosphereCurve;
        GetComponent<ModuleGimbal>().gimbalRange = selectedBell.gimbalRange;
        srbISP = string.Format("{0:F0}s ({1:F0}s Vac)", selectedBell.atmosphereCurve.Evaluate(1), selectedBell.atmosphereCurve.Evaluate(0));

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

        // It makes no sense to have a thrust limiter for SRBs
        // Even though this is present in stock, I'm disabling it.
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
            Debug.LogError("*ST* Selected bell name \"" + selectedBellName + "\" does not exist. Reverting.");
            selectedBellName = oldSelectedBell.displayName;
            selectedBell = oldSelectedBell;
            return;
        }

        oldSelectedBell.model.gameObject.SetActive(false);

        UpdateBottomPosition(selectedBell.srbAttach.position - oldSelectedBell.srbAttach.position);

        // Set the bits and pieces
        selectedBell.model.gameObject.SetActive(true);
        GetComponent<ModuleEngines>().atmosphereCurve = selectedBell.atmosphereCurve;
        GetComponent<ModuleGimbal>().gimbalRange = selectedBell.gimbalRange;
        srbISP = string.Format("{0:F0}s ({1:F0}s Vac)", selectedBell.atmosphereCurve.Evaluate(1), selectedBell.atmosphereCurve.Evaluate(0));

        ChangeThrust();
    }

    #endregion

    #region Thrust and heat production

    [KSPField(isPersistant = true, guiName = "Thrust", guiActive = false, guiActiveEditor = true, guiFormat="F0", guiUnits="kN"),
     UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = 2000f, incrementLarge = 100f, incrementSmall = 0, incrementSlide = 1f) ]
    public float thrust = 250;
    private float oldThrust;

    [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Burn Time")]
    public string srbBurnTime;

    [KSPField(isPersistant = false, guiName = "Heat", guiActive = false, guiActiveEditor = true, guiFormat="F2", guiUnits="K/s")]
    public float heatProduction;

    [KSPField]
    public float heatPerThrust = 2.0f;

    private void UpdateThrust()
    {
        if (oldThrust == thrust)
            return;
        ChangeThrust();
    }
    
    
    private void ChangeThrust()
    {
        ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
        PartResource solidFuel = part.Resources["SolidFuel"];

        float srbBurn0 = (float)(mE.atmosphereCurve.Evaluate(0) * solidFuel.maxAmount * solidFuel.info.density * mE.g / thrust);
        float srbBurn1 = (float)(mE.atmosphereCurve.Evaluate(1) * solidFuel.maxAmount * solidFuel.info.density * mE.g / thrust);

        mE.maxThrust = thrust;
        srbBurnTime = string.Format("{0:F1}s ({1:F1}s Vac)", srbBurn1, srbBurn0);
        
        // The heat production is directly proportional to the thrust. This is what stock KSP uses.
        heatProduction = mE.heatProduction = thrust * heatPerThrust;

        // Old equation. Not sure where this came from. Doesn't make a lot of physical sense. 
        //heatProduction = mE.heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((srbBurnTime + 20f), 0.75f)) * 0.5f);

        // Can't use SendPartMessage. Somewhere there's a method ChangeThrust that stuffs things up.
        //part.SendPartMessage("ChangeThrust", thrust);

        if (part.Modules.Contains("ModuleEngineConfigs"))
        {
            var mEC = part.Modules["ModuleEngineConfigs"];
            if (mEC != null)
            {
                Type engineType = mEC.GetType();
                engineType.GetMethod("ChangeThrust").Invoke(mEC, new object[] { thrust });
            }
        }

        oldThrust = thrust;
    }

    #endregion

    #region Attachments and nodes

    private void UpdateBottomPosition(Vector3 delta)
    {
        bottomAttachNode.originalPosition = bottomAttachNode.position += part.transform.InverseTransformDirection(delta);
        if (bottomAttachNode.attachedPart != null)
            UpdateAttachedPart(delta);
    }

    private Part oldAttachedPart = null;

    private void UpdateAttachedPart()
    {
        // When we attach a new part to the bottom node, ProceduralPart sets its reference position to on the surface.
        // Since we've moved the node, we need to undo the move that ProceeduralPart does to move it back to
        // the surface when first attached.
        if (oldAttachedPart != bottomAttachNode.attachedPart)
        {
            if (bottomAttachNode.attachedPart != null)
                UpdateAttachedPart(selectedBell.srbAttach.position - selectedBell.model.transform.position);
            oldAttachedPart = bottomAttachNode.attachedPart;
        }
    }

    private Vector3 UpdateAttachedPart(Vector3 delta)
    {
        //Debug.LogWarning("UpdateAttachedPart delta=" + delta);
        if (bottomAttachNode.attachedPart.transform == part.transform.parent)
        {
            part.transform.Translate(-delta, Space.World);
            Part root = EditorLogic.SortedShipList[0];
            int siblings = part.symmetryCounterparts == null ? 1 : (part.symmetryCounterparts.Count + 1);

            root.transform.Translate(delta / siblings, Space.World);
        }
        else
        {
            bottomAttachNode.attachedPart.transform.Translate(delta, Space.World);
        }
        return delta;
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
