using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

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
            if (!GameSceneFilter.AnyInitializing.IsLoaded())
                return;

            try
            {
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

        [PartMessageListener(typeof(PartResourceMaxAmountChanged))]
        private void ResourcesChanged(string resource, double maxAmount)
        {
            if (selectedBell == null)
                return;
            UpdateThrust(true);
        }

        [PartMessageListener(typeof(ChangeAttachNodeSizeDelegate))]
        private void ChangeAttachNodeSize(string name, float minDia, float area, int size)
        {
            if (name != "bottom")
                return;

            // Max choke diameter is equal to the tank bottom size.
            maxBellChokeDiameter = minDia;

            UpdateMaxThrust();
        }

        #endregion

        #region Objects

        [KSPField]
        public string srbBellName;

        [KSPField]
        public string bottomAttachNodeName;

        [KSPField]
        public string thrustVectorTransformName;

        #endregion

        #region Bell selection

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "SRB Type"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedBellName;

        [KSPField(isPersistant = false, guiName = "ISP", guiActive = false, guiActiveEditor = true)]
        public string srbISP;

        private SRBBellConfig selectedBell;
        private Dictionary<string, SRBBellConfig> srbConfigs;

        // Needs to be public because of a bug in KSP.
        [SerializeField]
        public ConfigNode[] srbConfigsSerialized;

        private float maxBellChokeDiameter = float.PositiveInfinity;
        private Transform thrustTransform;

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
            public float thrustScaleFactor = 256f;

            [Persistent]
            public float bellChokeDiameter = 0.5f;

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
            {
                LoadSRBConfigs();
            }

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

            ProceduralPart pPart = GetComponent<ProceduralPart>();

            Transform srbBell = part.FindModelTransform(srbBellName);
            thrustTransform = srbBell.Find(this.thrustVectorTransformName);

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

            if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
                selectedBellName = srbConfigsSerialized[0].GetValue("displayName");

            selectedBell = srbConfigs[selectedBellName];

            InitModulesFromBell();

            // Config for Real Fuels.
            if( part.Modules.Contains("ModuleEngineConfigs") ) 
            {
                var mEC = part.Modules["ModuleEngineConfigs"];
                ChangeThrust = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), mEC, "ChangeThrust");
                Fields["burnTime"].guiActiveEditor = false;
                Fields["srbISP"].guiActiveEditor = false;
                Fields["heatProduction"].guiActiveEditor = false;
            }

            // Call change thrust to initialize the bell scale.
            UpdateThrustNoMoving();

            // Break out at this stage during loading scene
            if (GameSceneFilter.AnyInitializing.IsLoaded())
                return;

            // Attach the bell. In the config file this isn't in normalized position, move it into normalized position first.
            srbBell.position = pPart.transform.TransformPoint(0, -0.5f, 0);
            pPart.AddAttachment(srbBell, true);

            // Move thrust transform to the end of the bell
            thrustTransform.position = selectedBell.srbAttach.position;

            // Move the bottom attach node into position.
            bottomAttachNode = part.findAttachNode(bottomAttachNodeName);
            if (HighLogic.LoadedSceneIsEditor)
            {
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

            // It makes no sense to have a thrust limiter for SRBs
            // Even though this is present in stock, I'm disabling it.
            BaseField thrustLimiter = GetComponent<ModuleEngines>().Fields["thrustPercentage"];
            thrustLimiter.guiActive = false;
            thrustLimiter.guiActiveEditor = false;
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

            UpdateMaxThrust();
        }

        private void InitModulesFromBell()
        {
            // Set the bits and pieces
            selectedBell.model.gameObject.SetActive(true);
            ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
            if (selectedBell.atmosphereCurve != null)
                GetComponent<ModuleEngines>().atmosphereCurve = selectedBell.atmosphereCurve;
            if (selectedBell.gimbalRange >= 0)
                GetComponent<ModuleGimbal>().gimbalRange = selectedBell.gimbalRange;
            srbISP = string.Format("{0:F0}s ({1:F0}s Vac)", mE.atmosphereCurve.Evaluate(1), mE.atmosphereCurve.Evaluate(0));
        }

        #endregion

        #region Thrust and heat production

        private bool usingME
        {
            get { return ChangeThrust != null; }
        }
        private Action<float> ChangeThrust;

        [KSPField(isPersistant = true, guiName = "Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "S3+3", guiUnits = "N"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = 2000f, incrementLarge = 100f, incrementSmall = 10, incrementSlide = 1f)]
        public float thrust = 250;
        private float oldThrust;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Burn Time")]
        public string burnTime;

        [KSPField(isPersistant = false, guiName = "Heat", guiActive = false, guiActiveEditor = true, guiFormat = "S3", guiUnits = "K/s")]
        public float heatProduction;

        [KSPField]
        public float heatPerThrust = 2.0f;

        [KSPField]
        public bool useOldHeatEquation = false;

        private void UpdateMaxThrust()
        {
            if (selectedBell == null)
                return;

            float maxThrustSqrt = maxBellChokeDiameter * Mathf.Sqrt(selectedBell.thrustScaleFactor) / selectedBell.bellChokeDiameter;
            float maxThrust = maxThrustSqrt * maxThrustSqrt;

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
            UpdateThrustNoMoving();
            MoveBottomAttachmentAndNode(selectedBell.srbAttach.position - oldAttach);
        }

        private void UpdateThrustNoMoving()
        {
            ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
            PartResource solidFuel = part.Resources["SolidFuel"];

            double solidFuelMassG;
            if (solidFuel == null)
                solidFuelMassG = 30.75;
            else
                solidFuelMassG = solidFuel.maxAmount * solidFuel.info.density * mE.g;

            mE.maxThrust = thrust;

            if (!usingME)
            {
                FloatCurve atmosphereCurve = (selectedBell.atmosphereCurve ?? mE.atmosphereCurve);

                float burnTime0 = (float)(atmosphereCurve.Evaluate(0) * solidFuelMassG / thrust);
                float burnTime1 = (float)(atmosphereCurve.Evaluate(1) * solidFuelMassG / thrust);

                burnTime = string.Format("{0:F1}s ({1:F1}s Vac)", burnTime1, burnTime0);

                // This equation is much easier
                if (useOldHeatEquation)
                    mE.heatProduction = heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((Math.Min(burnTime0, burnTime1) + 20f), 0.75f)) * 0.5f);
                // The heat production is directly proportional to the thrust on part mass.
                else
                    mE.heatProduction = heatProduction = thrust * heatPerThrust / (part.mass + part.GetResourceMass());
            }
            else
            {
                ChangeThrust(thrust);
            }

            // Rescale the bell.
            float bellScale = Mathf.Sqrt(thrust) / Mathf.Sqrt(selectedBell.thrustScaleFactor);
            selectedBell.model.transform.localScale = new Vector3(bellScale, bellScale, bellScale);

            oldThrust = thrust;
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

        internal const float draperPoint = 525f;

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
}