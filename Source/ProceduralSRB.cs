using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.Utils;

namespace ProceduralParts
{

    public class ProceduralSRB : PartModule, IPartCostModifier
    {
        #region callbacks

        public override void OnAwake()
        {
            base.OnAwake();
            //PartMessageService.Register(this);
            //this.RegisterOnUpdateEditor(OnUpdateEditor);
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                OnUpdateEditor();
        }

        public override void OnLoad(ConfigNode node)
        {
            Debug.Log("OnLoad");
            try
            {
                if (GameSceneFilter.AnyInitializing.IsLoaded())
                    LoadBells(node);
            }
            catch (Exception ex)
            {
                print("OnLoad exception: " + ex);
                throw;
            }
            Debug.Log("OnLoad end");
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

        public override void OnStart(StartState state)
        {
            Debug.Log("OnStart");
            try
            {
                InitializeBells();
                UpdateMaxThrust();
            }
            catch (Exception ex)
            {
                print("OnStart exception: " + ex);
                throw;
            }
            Debug.Log("OnStartEnd");
        }

        public override void OnUpdate()
        {
            AnimateHeat();
            UpdateBurnTime();
        }

        public void OnUpdateEditor()
        {
            try
            {
                UpdateBell();
                UpdateThrust();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                enabled = false;
            }
        }

        //[PartMessageListener(typeof(PartResourceInitialAmountChanged), scenes: GameSceneFilter.AnyEditor)]
        //public void PartResourceChanged(PartResource resource, double amount)
        [KSPEvent(guiActive = false, active = true)]
        public void OnPartResourceInitialAmountChanged(BaseEventData data)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            if (selectedBell == null)
                return;

            if (UsingME)
                UpdateMaxThrust();
            else
                UpdateThrustDependentCalcs();
        }

        //[PartMessageListener(typeof(PartAttachNodeSizeChanged), scenes: GameSceneFilter.AnyEditor)]
        //public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
        [KSPEvent(guiActive = false, active = true)]
        public void ChangeAttachNodeSize(BaseEventData data)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            AttachNode node = data.Get<AttachNode>("node");
            float minDia = data.Get<float>("minDia");
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (node.id != bottomAttachNodeName || minDia == attachedEndSize)
                return;

            attachedEndSize = minDia;
            UpdateMaxThrust();
        }

        [KSPField]
        public float costMultiplier = 1.0f;

        //public float GetModuleCost(float stdCost)
        //{
        //    return thrust * 0.5f * costMultiplier;
        //}

        #region IPartCostModifier implementation

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            return thrust * 0.5f * costMultiplier;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen()
        {
            return ModifierChangeWhen.CONSTANTLY;
        }

        #endregion

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
        private Transform bellTransform;

        private EngineWrapper _engineWrapper;
        private EngineWrapper Engine
        {
            get { return _engineWrapper ?? (_engineWrapper = new EngineWrapper(part)); }
        }

        #endregion

        #region Bell selection

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "SRB Type"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedBellName;

        // ReSharper disable once InconsistentNaming
        [KSPField(isPersistant = false, guiName = "ISP", guiActive = false, guiActiveEditor = true)]
        public string srbISP;

        [KSPField(isPersistant = true)]
        public float bellScale = -1;

        [KSPField]
        public float deprecatedThrustScaleFactor = 256;

        private SRBBellConfig selectedBell;
        private Dictionary<string, SRBBellConfig> srbConfigs;

        private static ConfigNode[] srbConfigsSerialized;

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

            [Persistent]
            public string realFuelsEngineType;

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
            print("*PP* InitializeBells");
            // Initialize the configs.
            if (srbConfigs == null)
                LoadSRBConfigs();

            BaseField field = Fields["selectedBellName"];
            // ReSharper disable once PossibleNullReferenceException
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

            bellTransform = part.FindModelTransform(srbBellName);
            thrustTransform = bellTransform.Find(thrustVectorTransformName);
            //startDirection = thrustTransform.localEulerAngles.z;
            startDirection = bellTransform.localEulerAngles.z;


            foreach (SRBBellConfig conf in srbConfigs.Values)
            {
                conf.model = part.FindModelTransform(conf.modelName);
                if (conf.model == null)
                {
                    Debug.LogError("*PT* Unable to find model transform for SRB bell name: " + conf.modelName);
                    srbConfigs.Remove(conf.modelName);
                    continue;
                }
                conf.model.transform.parent = bellTransform;

                conf.srbAttach = conf.model.Find(conf.srbAttachName);
                if (conf.srbAttach == null)
                {
                    Debug.LogError("*PT* Unable to find srbAttach for SRB bell name: " + conf.modelName);
                    srbConfigs.Remove(conf.modelName);
                    continue;
                }

                // Only enable the colider for flight mode. This prevents any surface attachments.
                if (HighLogic.LoadedSceneIsEditor && conf.model.GetComponent<Collider>() != null)
                    Destroy(conf.model.GetComponent<Collider>());

                conf.model.gameObject.SetActive(false);
            }

            // Select the bell
            if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
                selectedBellName = srbConfigsSerialized[0].GetValue("name");
            selectedBell = srbConfigs[selectedBellName];

            // Config for Real Fuels.
            if (part.Modules.Contains("ModuleEngineConfigs"))
            {
                // ReSharper disable once InconsistentNaming
                var mEC = part.Modules["ModuleEngineConfigs"];
                ModularEnginesChangeThrust = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), mEC, "ChangeThrust");
                try
                {
                    ModularEnginesChangeEngineType = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), mEC, "ChangeEngineType");
                }
                catch
                {
                    ModularEnginesChangeEngineType = null;
                }

                //Fields["burnTime"].guiActiveEditor = false;
                //Fields["srbISP"].guiActiveEditor = false;
                //Fields["heatProduction"].guiActiveEditor = false;
            }
            Fields["thrust"].guiActiveEditor = !UsingME;
            Fields["burnTime"].guiActiveEditor = !UsingME;
            Fields["burnTimeME"].guiActiveEditor = UsingME;
            Fields["thrustME"].guiActiveEditor = UsingME;

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
                print("*PP* Setting bell position: " + pPart.transform.TransformPoint(0, -0.5f, 0));
                bellTransform.position = pPart.transform.TransformPoint(0, -0.5f, 0);

                var bellRotation = bellTransform.eulerAngles;
                bellRotation.z = startDirection - bellDirection;
                bellTransform.localEulerAngles = bellRotation;

                pPart.AddAttachment(bellTransform, true);

                // Move the bottom attach node into position.
                // This needs to be done in flight mode too for the joints to work correctly
                bottomAttachNode = part.findAttachNode(bottomAttachNodeName);
                Vector3 delta = selectedBell.srbAttach.position - selectedBell.model.position;
                bottomAttachNode.originalPosition = bottomAttachNode.position += part.transform.InverseTransformDirection(delta);

                pPart.AddNodeOffset(bottomAttachNodeName, GetOffset);
            }

            // Move thrust transform to the end of the bell
            thrustTransform.position = selectedBell.srbAttach.position;
        }

        private Vector3 GetOffset()
        {
            return selectedBell.srbAttach.position - selectedBell.model.position;
        }

        private void UpdateBell()
        {
            if (selectedBell == null || (selectedBellName == selectedBell.name && oldBellDirection == bellDirection))
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

            var bellRotation = bellTransform.localEulerAngles;
            bellRotation.z = startDirection - bellDirection;
            bellTransform.localEulerAngles = bellRotation;


            MoveBottomAttachmentAndNode(selectedBell.srbAttach.position - oldSelectedBell.srbAttach.position);

            InitModulesFromBell();

            UpdateMaxThrust();

            oldBellDirection = bellDirection;
        }

        private void InitModulesFromBell()
        {
            // Set the bits and pieces
            selectedBell.model.gameObject.SetActive(true);
            if (selectedBell.atmosphereCurve != null)
                Engine.atmosphereCurve = selectedBell.atmosphereCurve;
            if (selectedBell.gimbalRange >= 0)
                GetComponent<ModuleGimbal>().gimbalRange = selectedBell.gimbalRange;
            if (ModularEnginesChangeEngineType != null && selectedBell.realFuelsEngineType != null)
                ModularEnginesChangeEngineType(selectedBell.realFuelsEngineType);
            srbISP = string.Format("{0:F0}s ({1:F0}s Vac)", Engine.atmosphereCurve.Evaluate(1), Engine.atmosphereCurve.Evaluate(0));
        }

        #endregion

        #region Thrust

        // ReSharper disable once InconsistentNaming
        private bool UsingME
        {
            get { return ModularEnginesChangeThrust != null; }
        }

        // ReSharper disable once InconsistentNaming
        private Action<float> ModularEnginesChangeThrust;
        // ReSharper disable once InconsistentNaming
        private Action<string> ModularEnginesChangeEngineType;

        [KSPField(isPersistant = true, guiName = "Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "F3", guiUnits = "N"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSmall = 10, incrementSlide = 1f, sigFigs = 3, unit = "kN", useSI = true)]
        public float thrust = 250;
        private float oldThrust;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Thrust ASL")]
        public string thrustASL;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Burn Time")]
        public string burnTime;

        [KSPField(isPersistant = true, guiName = "Burn Time", guiActive = false, guiActiveEditor = false, guiFormat = "F0", guiUnits = "s"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = 600f, incrementLarge = 60f, incrementSmall = 0, incrementSlide = 2e-4f)]
        public float burnTimeME = 60;
        private float oldBurnTimeME;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Thrust")]
        public string thrustME;

        [KSPField(isPersistant = true, guiName = "Direction", guiActive = false, guiActiveEditor = true, guiFormat = "F3", guiUnits = "°"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = -25f, maxValue = 25f, incrementLarge = 5f, incrementSmall = 1f, incrementSlide = 0.1f, sigFigs = 2, unit = "°")]
        public float bellDirection = 0;
        private float oldBellDirection;

        private float startDirection = 0f;

        // ReSharper disable once InconsistentNaming
        [KSPField]
        public float thrust1m = 0;

        private float maxThrust = float.PositiveInfinity;
        private float attachedEndSize = float.PositiveInfinity;

        // Real fuels integration
        //[PartMessageListener(typeof(PartEngineConfigChanged))]
        [KSPEvent(guiActive = false, active = true)]
        public void OnPartEngineConfigsChanged()
        {
            UpdateMaxThrust();
        }

        private void UpdateMaxThrust()
        {
            if (selectedBell == null)
                return;

            maxThrust = (float)Math.Max(Math.Round(attachedEndSize * attachedEndSize * thrust1m, 1), 10.0);

            if (!UsingME)
            {
                ((UI_FloatEdit)Fields["thrust"].uiControlEditor).maxValue = maxThrust;
                if (thrust > maxThrust)
                    thrust = maxThrust;
            }
            else
            {
                // Work out the min burn time.
                PartResource solidFuel = part.Resources["SolidFuel"];
                if (solidFuel != null)
                {
                    float isp0 = Engine.atmosphereCurve.Evaluate(0);
                    float minBurnTime = (float)Math.Ceiling(isp0 * solidFuel.maxAmount * solidFuel.info.density * Engine.g / maxThrust);

                    ((UI_FloatEdit)Fields["burnTimeME"].uiControlEditor).minValue = minBurnTime;

                    // Keep the thrust constant, change the current burn time to match
                    // Don't round the value, this stops it jumping around and won't matter that much
                    burnTimeME = (float)(isp0 * solidFuel.maxAmount * solidFuel.info.density * Engine.g / thrust);
                }
            }

            UpdateThrust(true);
        }

        private void UpdateThrust(bool force = false)
        {
            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (!force && oldThrust == thrust && burnTimeME == oldBurnTimeME && oldBellDirection == bellDirection)
                return;
            // ReSharper restore CompareOfFloatsByEqualityOperator

            Vector3 oldAttach = selectedBell.srbAttach.position;
            UpdateThrustDependentCalcs();
            MoveBottomAttachmentAndNode(selectedBell.srbAttach.position - oldAttach);

            oldThrust = thrust;
            oldBurnTimeME = burnTimeME;

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        [KSPField]
        public float fuelRate = 0.0f;

        private void UpdateBurnTime()
        {
            PartResource solidFuel = part.Resources["SolidFuel"];

            if (solidFuel != null)
            {
                float _burnTime = (float)(solidFuel.amount / (fuelRate / solidFuel.info.density));
                burnTime = string.Format("{0:F1}s", _burnTime);
            }
        }

        private void UpdateThrustDependentCalcs()
        {
            PartResource solidFuel = part.Resources["SolidFuel"];

            double solidFuelMassG;
            if (solidFuel == null)
                solidFuelMassG = UsingME ? 7.454 : 30.75;
            else
                solidFuelMassG = solidFuel.amount * solidFuel.info.density * Engine.g;

            FloatCurve atmosphereCurve = Engine.atmosphereCurve;

            if (!UsingME)
            {
                //float burnTime0 = burnTimeME = (float)(atmosphereCurve.Evaluate(0) * solidFuelMassG / thrust);
                //float burnTime1 = (float)(atmosphereCurve.Evaluate(1) * solidFuelMassG / thrust);

                fuelRate = thrust / (atmosphereCurve.Evaluate(0f) * Engine.g);
                if (solidFuel != null)
                {
                    UpdateBurnTime();
                    thrustASL = string.Format("{0:F1}", fuelRate * atmosphereCurve.Evaluate(1f) * Engine.g);
                }
                //burnTime = string.Format("{0:F1}s ({1:F1}s Vac)", burnTime1, burnTime0);
                //fuelRate = solidFuelMassG / burnTime0;
            }
            else
            {
                thrust = (float)(atmosphereCurve.Evaluate(0) * solidFuelMassG / burnTimeME);
                if (thrust > maxThrust)
                {
                    burnTimeME = (float)Math.Ceiling(atmosphereCurve.Evaluate(0) * solidFuelMassG / maxThrust);
                    thrust = (float)(atmosphereCurve.Evaluate(0) * solidFuelMassG / burnTimeME);
                }

                float thrust0 = Engine.maxThrust = thrust;
                float thrust1 = (float)(atmosphereCurve.Evaluate(1) * solidFuelMassG / burnTimeME);

                thrustME = thrust0.ToStringSI(unit: "N", exponent: 3) + " Vac / " + thrust1.ToStringSI(unit: "N", exponent: 3) + " ASL";
                srbISP = string.Format("{1:F0}s Vac / {0:F0}s ASL", atmosphereCurve.Evaluate(1), atmosphereCurve.Evaluate(0));
                fuelRate = (float)solidFuelMassG / (burnTimeME * Engine.g);
            }

            // This equation is much easier. From StretchySRBs
            if (useOldHeatEquation)
                heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((burnTimeME + 20f), 0.75f)) * 0.5f);
            // My equation.
            else
                heatProduction = heatPerThrust * Mathf.Sqrt(thrust) / (1 + part.mass);

            // ReSharper disable once InconsistentNaming
            // Rescale the bell.
            float bellScale1m = selectedBell.chokeEndRatio / selectedBell.bellChokeDiameter;
            bellScale = bellScale1m * Mathf.Sqrt(thrust / thrust1m);

            UpdateEngineAndBellScale();
        }

        private void UpdateEngineAndBellScale()
        {
            Engine.heatProduction = heatProduction;
            Engine.maxThrust = thrust;
            //part.GetComponent<ModuleEngines>().maxFuelFlow = (float)(0.1*fuelRate);
            part.GetComponent<ModuleEngines>().maxFuelFlow = fuelRate;


            selectedBell.model.transform.localScale = new Vector3(bellScale, bellScale, bellScale);

            //if (UsingME)
            //    ModularEnginesChangeThrust(thrust);
            UpdateFAR();
        }

        public void UpdateFAR()
        {
            /*if (HighLogic.LoadedSceneIsEditor)
            {
                if (part.Modules.Contains("FARBasicDragModel"))
                {
                    PartModule pModule = part.Modules["FARBasicDragModel"];
                    pModule.GetType().GetMethod("UpdatePropertiesWithShapeChange").Invoke(pModule, null);
                }
            }*/
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                part.SendMessage("GeometryPartModuleRebuildMeshData");
            }
        }

        #endregion

        #region Attachments and nodes

        private void MoveBottomAttachmentAndNode(Vector3 delta)
        {
            bottomAttachNode.originalPosition = bottomAttachNode.position += part.transform.InverseTransformDirection(delta);
            thrustTransform.position += delta;

            if (bottomAttachNode.attachedPart == null)
                return;

            if (bottomAttachNode.attachedPart.transform == part.transform.parent)
            {
                part.transform.Translate(-delta, Space.World);
                Part root = KSPAPIExtensions.GameSceneFilter.AnyEditor.IsLoaded() ? EditorLogic.RootPart : part.vessel.rootPart;
                int siblings = part.symmetryCounterparts == null ? 1 : (part.symmetryCounterparts.Count + 1);

                root.transform.Translate(delta / siblings, Space.World);
            }
            else
            {
                bottomAttachNode.attachedPart.transform.Translate(delta, Space.World);
            }
        }

        #endregion

        #region Heat

        [KSPField(isPersistant = true, guiName = "Heat", guiActive = false, guiActiveEditor = true, guiFormat = "F3", guiUnits = "K/s")]
        public float heatProduction;

        [KSPField]
        public float heatPerThrust = 2.0f;

        [KSPField]
        public bool useOldHeatEquation = false;

        internal const float DraperPoint = 525f;


        private void AnimateHeat()
        {
            // The emmissive module is too much effort to get working, just do it the easy way.
            float num = Mathf.Clamp01(((float)part.temperature - DraperPoint) / ((float)part.maxTemp - DraperPoint));
            if (float.IsNaN(num))
                num = 0f;

            Material mat = selectedBell.model.GetComponent<Renderer>().sharedMaterial;
            mat.SetColor("_EmissiveColor", new Color(num * num, 0, 0));
        }

        #endregion
    }
}
