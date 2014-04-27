using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;
using KSPAPIExtensions.Utils;

namespace ProceduralParts
{

    public class ProceduralSRB : PartModule
    {
        #region callbacks

        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                if (GameSceneFilter.AnyInitializing.IsLoaded())
                    LoadBells(node);
            }
            catch (Exception ex)
            {
                print("OnLoad exception: " + ex);
                throw ex;
            }
        }

        public override string GetInfo()
        {
            InitializeBells();
            return base.GetInfo();
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public override void OnStart(PartModule.StartState state)
        {
            try
            {
                InitializeBells();                    
            }
            catch (Exception ex)
            {
                print("OnStart exception: " + ex);
                throw ex;
            }
        }

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
                    AnimateHeat();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                enabled = false;
            }
        }

        [PartMessageListener(typeof(PartResourceInitialAmountChanged), scenes: GameSceneFilter.AnyEditor)]
        private void PartResourceChanged(string resource, double amount)
        {
            if (selectedBell == null)
                return;
            UpdateThrustDependentCalcs();
        }

        [PartMessageListener(typeof(ChangeAttachNodeSizeDelegate), scenes: GameSceneFilter.AnyEditor)]
        private void ChangeAttachNodeSize(string name, float minDia, float area, int size)
        {
            if (name != bottomAttachNodeName)
                return;

            UpdateMaxThrust(minDia);
        }

        #endregion

        #region Objects

        [KSPField]
        public string srbBellName;

        [KSPField]
        public string bottomAttachNodeName;
        private AttachNode bottomAttachNode;

        [KSPField]
        public string thrustVectorTransformName;
        private Transform thrustTransform;

        private EngineWrapper _engineWrapper;
        private EngineWrapper Engine
        {
            get
            {
                if (_engineWrapper == null)
                    _engineWrapper = new EngineWrapper(part);
                return _engineWrapper;
            }
        }

        #endregion

        #region Bell selection

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "SRB Type"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedBellName;

        [KSPField(isPersistant = false, guiName = "ISP", guiActive = false, guiActiveEditor = true)]
        public string srbISP;

        [KSPField(isPersistant = true)]
        public float bellScale = -1;

        [KSPField]
        public float deprecatedThrustScaleFactor = 256;

        private SRBBellConfig selectedBell;
        private Dictionary<string, SRBBellConfig> srbConfigs;
        [SerializeField]
        public ConfigNode[] srbConfigsSerialized;

        [Serializable]
        public class SRBBellConfig : IConfigNode
        {
            [Persistent]
            public string name;
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
            public float gimbalRange = -1;

            [Persistent]
            public float bellChokeDiameter = 0.5f;

            [Persistent]
            public float chokeEndRatio = 0.5f;


            public void Load(ConfigNode node)
            {
                ConfigNode.LoadObjectFromConfig(this, node);
                ConfigNode atmosCurveNode = node.GetNode("atmosphereCurve");
                if (atmosCurveNode != null)
                {
                    atmosphereCurve = new FloatCurve();
                    atmosphereCurve.Load(atmosCurveNode);
                }
                if (name == null)
                    name = node.GetValue("displayName");
            }
            public void Save(ConfigNode node)
            {
                ConfigNode.CreateConfigFromObject(this, node);
            }

        }

        private void LoadBells(ConfigNode node)
        {
            srbConfigsSerialized = node.GetNodes("SRB_BELL");
            LoadSRBConfigs();
        }

        private void LoadSRBConfigs()
        {
            srbConfigs = new Dictionary<string, SRBBellConfig>();
            foreach (ConfigNode srbNode in srbConfigsSerialized)
            {
                SRBBellConfig conf = new SRBBellConfig();
                conf.Load(srbNode);
                srbConfigs.Add(conf.name, conf);
            }
        }

        private void InitializeBells()
        {
            // Initialize the configs.
            if (srbConfigs == null)
                LoadSRBConfigs();

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

            Transform srbBell = part.FindModelTransform(srbBellName);
            thrustTransform = srbBell.Find(thrustVectorTransformName);

            foreach (SRBBellConfig conf in srbConfigs.Values)
            {
                conf.model = part.FindModelTransform(conf.modelName);
                if (conf.model == null)
                {
                    Debug.LogError("*PT* Unable to find model transform for SRB bell name: " + conf.modelName);
                    srbConfigs.Remove(conf.modelName);
                    continue;
                }
                conf.model.transform.parent = srbBell;

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

            // Select the bell
            if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
                selectedBellName = srbConfigsSerialized[0].GetValue("name");
            selectedBell = srbConfigs[selectedBellName];

            // Config for Real Fuels.
            if (part.Modules.Contains("ModuleEngineConfigs"))
            {
                var mEC = part.Modules["ModuleEngineConfigs"];
                ModuleEnginesChangeThrust = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), mEC, "ChangeThrust");
                Fields["burnTime"].guiActiveEditor = false;
                Fields["srbISP"].guiActiveEditor = false;
                Fields["heatProduction"].guiActiveEditor = false;
            }

            // Initialize the modules.
            InitModulesFromBell();

            // Break out at this stage during loading scene
            if (GameSceneFilter.AnyInitializing.IsLoaded())
            {
                UpdateThrustDependentCalcs();
                return;
            }

            // Update the thrust according to the equation when in editor mode, don't mess with ships in flight
            if (GameSceneFilter.AnyEditor.IsLoaded())
                UpdateThrustDependentCalcs();
            else
            {
                if (bellScale <= 0 || heatProduction <= 0)
                {
                    // We've reloaded from a legacy save
                    // Use the new heat production equation, but use the legacy bell scaling one.
                    UpdateThrustDependentCalcs();

                    // Legacy bell scaling equation
                    bellScale = Mathf.Sqrt(thrust / deprecatedThrustScaleFactor);
                }

                UpdateEngineAndBellScale();
            }

            // It makes no sense to have a thrust limiter for SRBs
            // Even though this is present in stock, I'm disabling it.
            BaseField thrustLimiter = ((PartModule)Engine).Fields["thrustPercentage"];
            thrustLimiter.guiActive = false;
            thrustLimiter.guiActiveEditor = false;

            ProceduralPart pPart = GetComponent<ProceduralPart>();
            if (pPart != null)
            {
                // Attach the bell. In the config file this isn't in normalized position, move it into normalized position first.
                srbBell.position = pPart.transform.TransformPoint(0, -0.5f, 0);
                pPart.AddAttachment(srbBell, true);

                // Move the bottom attach node into position.
                // This needs to be done in flight mode too for the joints to work correctly
                bottomAttachNode = part.findAttachNode(bottomAttachNodeName);
                Vector3 delta = selectedBell.srbAttach.position - selectedBell.model.position;
                bottomAttachNode.originalPosition = bottomAttachNode.position += part.transform.InverseTransformDirection(delta);
                if (HighLogic.LoadedSceneIsEditor && bottomAttachNode.attachedPart != null && bottomAttachNode.attachedPart.transform == part.transform.parent)
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

            // Move thrust transform to the end of the bell
            thrustTransform.position = selectedBell.srbAttach.position;
        }

        private void UpdateBell()
        {
            if (selectedBell != null && selectedBellName == selectedBell.name)
                return;

            SRBBellConfig oldSelectedBell = selectedBell;

            if (!srbConfigs.TryGetValue(selectedBellName, out selectedBell))
            {
                Debug.LogError("*ST* Selected bell name \"" + selectedBellName + "\" does not exist. Reverting.");
                selectedBellName = oldSelectedBell.name;
                selectedBell = oldSelectedBell;
                return;
            }

            oldSelectedBell.model.gameObject.SetActive(false);

            MoveBottomAttachmentAndNode(selectedBell.srbAttach.position - oldSelectedBell.srbAttach.position);

            InitModulesFromBell();

            UpdateThrust(true);
        }

        private void InitModulesFromBell()
        {
            // Set the bits and pieces
            selectedBell.model.gameObject.SetActive(true);
            if (selectedBell.atmosphereCurve != null)
                Engine.atmosphereCurve = selectedBell.atmosphereCurve;
            if (selectedBell.gimbalRange >= 0)
                GetComponent<ModuleGimbal>().gimbalRange = selectedBell.gimbalRange;
            srbISP = string.Format("{0:F0}s ({1:F0}s Vac)", Engine.atmosphereCurve.Evaluate(1), Engine.atmosphereCurve.Evaluate(0));
        }

        #endregion

        #region Thrust 

        private bool usingME
        {
            get { return ModuleEnginesChangeThrust != null; }
        }
        private Action<float> ModuleEnginesChangeThrust;

        [KSPField(isPersistant = true, guiName = "Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "S4+3", guiUnits = "N"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSmall = 10, incrementSlide = 1f)]
        public float thrust = 250;
        private float oldThrust;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Burn Time")]
        public string burnTime;

        [KSPField]
        public float thrust1m = 0;

        private float maxThrust = float.PositiveInfinity;

        private void UpdateMaxThrust(float attachedEndSize)
        {
            if (selectedBell == null)
                return;

            maxThrust = Mathf.Max(Mathf.Round(attachedEndSize * attachedEndSize * thrust1m * 0.1f) * 10f, 10f);

            ((UI_FloatEdit)Fields["thrust"].uiControlEditor).maxValue = maxThrust;
            if (thrust > maxThrust)
            {
                thrust = maxThrust;
                UpdateThrust();
            }
        }

        private void UpdateThrust(bool force = false)
        {
            if (!force && oldThrust == thrust)
                return;

            Vector3 oldAttach = selectedBell.srbAttach.position;
            UpdateThrustDependentCalcs();
            MoveBottomAttachmentAndNode(selectedBell.srbAttach.position - oldAttach);

            oldThrust = thrust;
        }

        private void UpdateThrustDependentCalcs()
        {
            PartResource solidFuel = part.Resources["SolidFuel"];

            double solidFuelMassG;
            if (solidFuel == null)
                solidFuelMassG = 30.75;
            else
                solidFuelMassG = solidFuel.amount * solidFuel.info.density * Engine.g;

            FloatCurve atmosphereCurve = (selectedBell.atmosphereCurve ?? Engine.atmosphereCurve);

            float burnTime0 = (float)(atmosphereCurve.Evaluate(0) * solidFuelMassG / thrust);
            float burnTime1 = (float)(atmosphereCurve.Evaluate(1) * solidFuelMassG / thrust);
            burnTime = string.Format("{0:F1}s ({1:F1}s Vac)", burnTime1, burnTime0);

            // This equation is much easier. From StretchySRBs
            if (useOldHeatEquation)
                heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((Math.Min(burnTime0, burnTime1) + 20f), 0.75f)) * 0.5f);
            // My equation.
            else
                heatProduction = heatPerThrust * Mathf.Sqrt(thrust) / (1 + part.mass);

            // Rescale the bell.
            float bellScale1m = selectedBell.chokeEndRatio / selectedBell.bellChokeDiameter;
            bellScale = bellScale1m * Mathf.Sqrt(thrust / thrust1m);

            UpdateEngineAndBellScale();
        }

        private void UpdateEngineAndBellScale()
        {
            Engine.heatProduction = heatProduction;
            Engine.maxThrust = thrust;

            selectedBell.model.transform.localScale = new Vector3(bellScale, bellScale, bellScale);

            if (usingME)
                ModuleEnginesChangeThrust(thrust);
        }

        #endregion

        #region Attachments and nodes

        private Part oldAttachedPart = null;

        private void UpdateAttachedPart()
        {
            // When we attach a new part to the bottom node, ProceduralPart sets its reference position to on the surface.
            // Since we've moved the node, we need to undo the move that ProceeduralPart does to move it back to
            // the surface when first attached.
            if (oldAttachedPart != bottomAttachNode.attachedPart)
            {
                if (bottomAttachNode.attachedPart != null)
                    MoveBottomAttachment(selectedBell.srbAttach.position - selectedBell.model.transform.position);
                oldAttachedPart = bottomAttachNode.attachedPart;
            }
        }

        private void MoveBottomAttachmentAndNode(Vector3 delta)
        {
            bottomAttachNode.originalPosition = bottomAttachNode.position += part.transform.InverseTransformDirection(delta);
            if (bottomAttachNode.attachedPart != null)
                MoveBottomAttachment(delta);
            thrustTransform.position += delta;
        }

        private Vector3 MoveBottomAttachment(Vector3 delta)
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

        [KSPField(isPersistant = true, guiName = "Heat", guiActive = false, guiActiveEditor = true, guiFormat = "S3", guiUnits = "K/s")]
        public float heatProduction;

        [KSPField]
        public float heatPerThrust = 2.0f;

        [KSPField]
        public bool useOldHeatEquation = false;

        internal const float draperPoint = 525f;


        private void AnimateHeat()
        {
            // The emmissive module is too much effort to get working, just do it the easy way.
            float num = Mathf.Clamp01((part.temperature - draperPoint) / (part.maxTemp - draperPoint));
            if (float.IsNaN(num))
                num = 0f;

            Material mat = selectedBell.model.renderer.sharedMaterial;
            mat.SetColor("_EmissiveColor", new Color(num*num, 0, 0));
        }

        #endregion
    }
}