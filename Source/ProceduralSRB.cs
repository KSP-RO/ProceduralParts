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
        private const string ModTag = "[ProceduralSRB]";
        // ReSharper disable once InconsistentNaming
        public const string PAWGroupName = "ProcSRB";
        // ReSharper disable once InconsistentNaming
        public const string PAWGroupDisplayName = "ProceduralSRB";

        private readonly Dictionary<String, GameObject> LRs = new Dictionary<string, GameObject>();
        private readonly Dictionary<String, Vector3> VECs = new Dictionary<string, Vector3>();

        public ProceduralPart PPart => _pPart ??= GetComponent<ProceduralPart>();
        private ProceduralPart _pPart;

        [KSPField] 
        public bool debugMarkers = false;

        #region callbacks

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                LoadBells(node);
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
            if (PPart is null)
            {
                Debug.LogError($"{ModTag} {part}.{this} Procedural Part not found");
                return;
            }
            try
            {
                // isMirrored flag is required for correct bell deflection - real deflection angles is multiplied by
                // -1 on mirrored part.
                // Because of this, we need to check if we've been created off of the part that already was "mirrored"
                // and set out flag accordingly.
                isMirrored =
                    part.symMethod == SymmetryMethod.Mirror && part.symmetryCounterparts.Count > 0 &&
                    !part.symmetryCounterparts[0].GetComponent<ProceduralSRB>().isMirrored;

                bottomAttachNode = part.FindAttachNode(bottomAttachNodeName);

                InitializeBells();
                UpdateMaxThrust();
            }
            catch (Exception ex)
            {
                Debug.Log($"{ModTag}: OnStart exception: {ex}");
                throw;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(selectedBellName)].uiControlEditor.onFieldChanged += HandleBellTypeChange;
                Fields[nameof(selectedBellName)].uiControlEditor.onSymmetryFieldChanged += HandleBellTypeChange;

                Fields[nameof(thrustDeflection)].uiControlEditor.onFieldChanged += HandleBellDeflectionChange;
                // onSymmetryFieldChanged is buggy here, will call handler for symmetry counterparts ourselves

                Fields[nameof(burnTimeME)].uiControlEditor.onFieldChanged += HandleThrustChange;
                Fields[nameof(burnTimeME)].uiControlEditor.onSymmetryFieldChanged += HandleThrustChange;

                Fields[nameof(thrust)].uiControlEditor.onFieldChanged += HandleThrustChange;
                Fields[nameof(thrust)].uiControlEditor.onSymmetryFieldChanged += HandleThrustChange;

                Fields[nameof(thrust)].guiActiveEditor = !UsingME;
                if (!UsingME)
                    Fields[nameof(burnTimeME)].uiControlEditor = Fields[nameof(burnTimeME)].uiControlFlight;
            }
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
                AnimateHeat();
            if (!UsingME)
                burnTimeME = BurnTimeFromFuelRate;
            if (HighLogic.LoadedSceneIsEditor && debugMarkers)
            {
                LR("srbNozzle", part.FindModelTransform("srbNozzle").position);
                LR("bellModel ", selectedBell.model.position);
                LR("srbAttach  ", selectedBell.srbAttach.position);
                LR("bellTransform", bellTransform.position);
                LR("Transform", part.transform.position);
            }
        }

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartResourceInitialAmountChanged(BaseEventDetails data)
        {
            if (HighLogic.LoadedSceneIsEditor && selectedBell != null)
            {
                if (UsingME)
                    UpdateMaxThrust();
                else
                    UpdateThrustDependentCalcs();
            }
        }

        //[PartMessageListener(typeof(PartAttachNodeSizeChanged), scenes: GameSceneFilter.AnyEditor)]
        //public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
        public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
        {
            var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
            data.Set("node", node);
            data.Set("minDia", minDia);
            data.Set("area", area);
            part.SendEvent("OnPartAttachNodeSizeChanged", data, 0);
        }

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartAttachNodeSizeChanged(BaseEventDetails data)
        {
            if (HighLogic.LoadedSceneIsEditor &&
                data.Get<AttachNode>("node") is AttachNode node &&
                data.Get<float>("minDia") is float minDia)
            {
                attachedEndSize = minDia;
                if (node.id == bottomAttachNodeName)
                    UpdateMaxThrust();
            }
        }

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartNodeMoved()
        {
            MoveBellAndBottomNode();
            SetBellRotation(thrustDeflection);
        }

        #endregion

        [KSPField]
        public float costMultiplier = 1.0f;

        #region IPartCostModifier implementation
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => thrust * 0.5f * costMultiplier;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;
        #endregion

        #region Bell selection
        
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "SRB Type", groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName, groupStartCollapsed = false), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedBellName;

        // ReSharper disable once InconsistentNaming
        [KSPField(guiName = "ISP", guiActiveEditor = true, groupName = PAWGroupName)]
        public string srbISP;

        [KSPField(isPersistant = true)]
        public float bellScale = -1;

        [KSPField]
        public float deprecatedThrustScaleFactor = 256;

        private SRBBellConfig selectedBell;
        private readonly Dictionary<string, SRBBellConfig> srbConfigs = new Dictionary<string, SRBBellConfig>();

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
            public void Save(ConfigNode node) => ConfigNode.CreateConfigFromObject(this, node);
        }

        private void LoadBells(ConfigNode node)
        {
            srbConfigsSerialized = node.GetNodes("SRB_BELL");
            LoadSRBConfigs();
        }

        private void LoadSRBConfigs()
        {
            srbConfigs.Clear();
            foreach (ConfigNode srbNode in srbConfigsSerialized)
            {
                SRBBellConfig conf = new SRBBellConfig();
                conf.Load(srbNode);
                srbConfigs.Add(conf.name, conf);
            }
        }
        
        #region Objects

        [KSPField(isPersistant = true)]
        public bool isMirrored;

        [KSPField]
        public string srbBellName;

        [KSPField]
        public string bottomAttachNodeName;
        private AttachNode bottomAttachNode;

        [KSPField]
        public string thrustVectorTransformName;
        private Transform thrustTransform;
        private Transform bellTransform;
        private Transform bellRootTransform;
        
        private EngineWrapper _engineWrapper;
        private EngineWrapper Engine => _engineWrapper ?? (_engineWrapper = new EngineWrapper(part));

        #endregion

        private void InitializeBells()
        {
            Debug.Log($"{ModTag} {this}: InitializeBells");
            // Initialize the configs.
            if (srbConfigs.Count < 1)
                LoadSRBConfigs();
            if (srbConfigs.Count < 1)
            {
                Debug.LogError($"{ModTag} {this}: No SRB bells configured");
                return;
            }

            Fields[nameof(selectedBellName)].guiActiveEditor = srbConfigs.Count > 1;
            (Fields[nameof(selectedBellName)].uiControlEditor as UI_ChooseOption).options = srbConfigs.Keys.ToArray();

            bellTransform = part.FindModelTransform(srbBellName);
            bellRootTransform = part.FindModelTransform(srbBellName + "root");
            bellRootTransform ??= bellTransform;
            thrustTransform = bellTransform.Find(thrustVectorTransformName);

            PrepareBellModels();

            // Select the bell
            if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
                selectedBellName = srbConfigs.First().Key;
            selectedBell = srbConfigs[selectedBellName];

            ConfigureRealFuels();

            // Initialize the modules.
            InitModulesFromBell();

            if (HighLogic.LoadedScene == GameScenes.LOADING ||
                HighLogic.LoadedSceneIsEditor ||
                (HighLogic.LoadedSceneIsFlight && (bellScale <= 0 || heatProduction <= 0)))
                UpdateThrustDependentCalcs();

            // Break out at this stage during loading scene
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                return;

            // Update the thrust according to the equation when in editor mode, don't mess with ships in flight
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (bellScale <= 0 || heatProduction <= 0)
                {
                    // Legacy bell scaling equation
                    bellScale = Mathf.Sqrt(thrust / deprecatedThrustScaleFactor);
                    Debug.Log($"{ModTag} {this}: legacy bell scale: {bellScale}");
                }

                UpdateEngineAndBellScale();
            }

            // Disable SRB thrust limiter
            BaseField thrustLimiter = ((PartModule)Engine).Fields["thrustPercentage"];
            thrustLimiter.guiActive = thrustLimiter.guiActiveEditor = false;

            if (PPart != null)
                SetBellRotation(thrustDeflection);
            else
                Debug.Log($"{ModTag} {part}.{this}: ProceduralSRB.InitializeBells() Unable to find ProceduralPart component! (null) for {part.name}");

            // Move thrust transform to the end of the bell
            thrustTransform.position = selectedBell.srbAttach.position;
        }

        #region Attachments and nodes

        private void MoveBellAndBottomNode()
        {
            if (bottomAttachNode == null)
            {
                return;
            }
            Debug.Log($"{ModTag} {this} ({part.persistentId}): MoveBellAndBottomNode: bottom node: {bottomAttachNode.position}");

            // Move bell a little bit inside the SRB so gimbaling and tilting won't make weird gap between bell choke and SRB
            // d = chokeDia * scale * 0.5 * sin(max deflection angle)
            float d = (float)(selectedBell.bellChokeDiameter / 2 * bellScale * Math.PI * (
                                  selectedBell.gimbalRange + Math.Abs(thrustDeflection)) / 180f);
            //Debug.Log($"{ModTag} {this}: bell d: {d}; bellD: {selectedBell.bellChokeDiameter}; scale: {bellScale}; angle: {selectedBell.gimbalRange}");
            
            // Using part transform as a base and shape length as offset for bell placement
            bellTransform.position = PPart.transform.TransformPoint(
                PPart.transform.InverseTransformPoint(part.transform.position) - 
                                GetLength() / 2 * Vector3.up + d * Vector3.up);

            Vector3 origNodePosition = PPart.transform.TransformPoint(bottomAttachNode.position);

            // Place attachment node inside the bell 
            bottomAttachNode.position = PPart.transform.InverseTransformPoint(selectedBell.srbAttach.position);

            // Translate attached parts
            // ReSharper disable once Unity.InefficientPropertyAccess
            TranslateAttachedPart(selectedBell.srbAttach.position);
        }

        private void TranslateAttachedPart(Vector3 newPosition)
        {
            if (bottomAttachNode.attachedPart is Part pushTarget)
            {
                Vector3 opposingNodePos = pushTarget.transform.TransformPoint(bottomAttachNode.FindOpposingNode().position);
                
                if (pushTarget == part.parent)
                {
                    pushTarget = part;
                }
                Vector3 shift = pushTarget == part
                    ? opposingNodePos - newPosition
                    : newPosition - opposingNodePos;
                Debug.Log($"{ModTag} {this}: shifting: {pushTarget} by {shift}");
                pushTarget.transform.Translate(shift, Space.World);
            }
        }
        
        public void RotateAttachedPartAndNode(Vector3 rotAxis, float delta, float adjustedDeflection)
        {
            Vector3 rotationDelta = Vector3.right * (float) (Math.PI * adjustedDeflection / 180f);
            Debug.Log($"{ModTag} {this}: setting node orientation: {bottomAttachNode.originalOrientation} + {rotationDelta}");
            bottomAttachNode.orientation = bottomAttachNode.originalOrientation + rotationDelta;

            if (bottomAttachNode.attachedPart is Part rotTarget)
            {
                // If the attached part is a child of ours, rotate it directly.
                // If it is our parent, then we need to rotate ourselves
                Vector3 opposingNodePos = rotTarget.transform.TransformPoint(bottomAttachNode.FindOpposingNode().position);

                if (rotTarget == part.parent)
                {
                    rotTarget = part;
                    delta = -delta;
                }
                
                rotTarget.partTransform.Rotate(rotAxis, delta, Space.World);
                // Check new bell position after it was rotated
                Vector3 shift = rotTarget == part
                    ? opposingNodePos - selectedBell.srbAttach.position
                    // ReSharper disable once Unity.InefficientPropertyAccess
                    : selectedBell.srbAttach.position - opposingNodePos;
                // If we've rotated anything, we've moved that thing away from attachment node. Need to bring it back.
                Debug.Log($"{ModTag} {this}: shifting: {rotTarget} by {shift}");

                rotTarget.transform.Translate(shift, Space.World);
            }
            // Fix attach node position
            bottomAttachNode.position = PPart.transform.InverseTransformPoint(selectedBell.srbAttach.position);
        }
        
        #endregion

        private void ConfigureRealFuels()
        {
            // Config for Real Fuels.
            if (part.Modules.Contains("ModuleEngineConfigs") && part.Modules["ModuleEngineConfigs"] is PartModule MEC)
            {
                ModularEnginesChangeThrust = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), MEC, "ChangeThrust");
                try
                {
                    ModularEnginesChangeEngineType = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), MEC, "ChangeEngineType");
                }
                catch
                {
                    ModularEnginesChangeEngineType = null;
                }
            }
        }
        private void PrepareBellModels()
        {
            foreach (SRBBellConfig conf in srbConfigs.Values)
            {
                conf.model = part.FindModelTransform(conf.modelName);
                if (conf.model == null)
                {
                    Debug.LogError($"{ModTag} {part}.{this}: Unable to find model transform for SRB bell name: {conf.modelName}");
                    srbConfigs.Remove(conf.modelName);
                    continue;
                }
                conf.model.transform.parent = bellTransform;

                conf.srbAttach = conf.model.Find(conf.srbAttachName);
                if (conf.srbAttach == null)
                {
                    Debug.LogError($"{ModTag} {part}.{this}: Unable to find srbAttach for SRB bell name: {conf.modelName}");
                    srbConfigs.Remove(conf.modelName);
                    continue;
                }

                // Only enable the collider for flight mode. This prevents any surface attachments.
                if (HighLogic.LoadedSceneIsEditor && conf.model.GetComponent<Collider>() != null)
                    Destroy(conf.model.GetComponent<Collider>());

                conf.model.gameObject.SetActive(false);
            }
        }

        private void UpdateBellType()
        {
            if (selectedBell == null || selectedBellName == selectedBell.name)
                return;

            SRBBellConfig oldSelectedBell = selectedBell;

            if (!srbConfigs.TryGetValue(selectedBellName, out selectedBell))
            {
                Debug.LogError($"{ModTag} {this}: Selected bell name \"{selectedBellName}\" does not exist. Reverting.");
                selectedBellName = oldSelectedBell.name;
                selectedBell = oldSelectedBell;
                return;
            }

            oldSelectedBell.model.gameObject.SetActive(false);

            InitModulesFromBell();
            UpdateMaxThrust();
        }

        private void SetBellRotation(float oldThrustDeflection = 0f)
        {
            try
            {
                if (bellTransform == null || selectedBell == null)
                    return;

                bellTransform.localEulerAngles = Vector3.zero;
                var rotAxis = Vector3.Cross(part.partTransform.right, part.partTransform.up);

                var adjustedDir = thrustDeflection;
                var rotationDelta = thrustDeflection - oldThrustDeflection;

                Debug.Log($"{ModTag} {part}.{part.persistentId}: symmetry: {part.symMethod}; mirrored? {isMirrored}");

                if (part.symMethod == SymmetryMethod.Mirror)
                {
                    if (!isMirrored)
                    {
                        adjustedDir *= -1;
                        rotationDelta *= -1;
                    }
                }

                Debug.Log($"{ModTag} {this}: rotating to: {adjustedDir}; delta={rotationDelta}");

                bellTransform.Rotate(rotAxis, adjustedDir, Space.World);
                selectedBell.srbAttach.Rotate(rotAxis, adjustedDir, Space.World);
                RotateAttachedPartAndNode(rotAxis, rotationDelta, adjustedDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{ModTag} {this}:** Exception within SetBellRotation:");
                Debug.LogException(ex);
            }
        }

        private void InitModulesFromBell()
        {
            // Set the bits and pieces
            selectedBell.model.gameObject.SetActive(true);
            if (selectedBell.atmosphereCurve != null)
                Engine.atmosphereCurve = selectedBell.atmosphereCurve;
            if (GetComponent<ModuleGimbal>() is ModuleGimbal md)
            {
                md.gimbalRange = selectedBell.gimbalRange;
                md.gimbalTransformName = selectedBell.model.transform.name;
                md.gimbalRangeXN = md.gimbalRangeXP = md.gimbalRangeYN = md.gimbalRangeYP = selectedBell.gimbalRange;
            }

            if (ModularEnginesChangeEngineType != null && selectedBell.realFuelsEngineType != null)
                ModularEnginesChangeEngineType(selectedBell.realFuelsEngineType);
        }

        #endregion

        #region Thrust

        private bool UsingME => ModularEnginesChangeThrust != null;

        // ReSharper disable once InconsistentNaming
        private Action<float> ModularEnginesChangeThrust;
        // ReSharper disable once InconsistentNaming
        private Action<string> ModularEnginesChangeEngineType;

        [KSPField(isPersistant = true, guiName = "Thrust", guiActive = true, guiActiveEditor = true, guiFormat = "F3", guiUnits = "kN", groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSmall = 10, incrementSlide = 1f, sigFigs = 5, unit = "kN", useSI = true)]
        public float thrust = 250;

        [KSPField(guiActiveEditor = true, guiName = "Thrust", groupName = PAWGroupName)]
        public string thrustStats;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Burn Time", guiFormat = "F1", guiUnits = "s", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = 600f, incrementLarge = 60f, incrementSmall = 0, incrementSlide = 2e-4f)]
        public float burnTimeME = 60;

        [KSPField(isPersistant = true, guiName = "Deflection", guiActiveEditor = true, guiFormat = "F3", guiUnits = "°", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = -25f, maxValue = 25f, incrementLarge = 5f, incrementSmall = 1f, incrementSlide = 0.1f, sigFigs = 5, unit = "°")]
        public float thrustDeflection;

        [KSPField]
        public float thrust1m = 1;

        private float maxThrust = float.PositiveInfinity;
        private float attachedEndSize = float.PositiveInfinity;

        // Real fuels integration
        //[PartMessageListener(typeof(PartEngineConfigChanged))]
        [KSPEvent(guiActive = false, active = true)]
        public void OnPartEngineConfigsChanged() => UpdateMaxThrust();

        private void UpdateMaxThrust()
        {
            if (selectedBell == null)
                return;

            maxThrust = Mathf.Max(attachedEndSize * attachedEndSize * thrust1m, 10);
            maxThrust = Convert.ToSingle(Math.Round(maxThrust, 1));

            if (!UsingME)
            {
                (Fields[nameof(thrust)].uiControlEditor as UI_FloatEdit).maxValue = maxThrust;
                if (HighLogic.LoadedSceneIsEditor && thrust > maxThrust)
                    thrust = maxThrust;
            }
            else
            {
                // Work out the min burn time.
                if (part.Resources["SolidFuel"] is PartResource solidFuel)
                {
                    float isp0 = Engine.atmosphereCurve.Evaluate(0);
                    float minBurnTime = (float)Math.Ceiling(isp0 * solidFuel.maxAmount * solidFuel.info.density * Engine.g / maxThrust);
                    Debug.Log($"{ModTag} {this}: UpdateMaxThrust: minBurnTime = {minBurnTime}, maxThrust = {maxThrust}");
                    (Fields[nameof(burnTimeME)].uiControlEditor as UI_FloatEdit).minValue = minBurnTime;

                    // Keep the thrust constant, change the current burn time to match
                    // Don't round the value, this stops it jumping around and won't matter that much
                    burnTimeME = (float)(isp0 * solidFuel.maxAmount * solidFuel.info.density * Engine.g / thrust);
                }
            }

            UpdateThrust();
        }

        private void UpdateThrust()
        {
            UpdateThrustDependentCalcs();

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        [KSPField]
        public float fuelRate;

        private float BurnTimeFromFuelRate => (part.Resources["SolidFuel"] is PartResource solidFuel) ?
            Convert.ToSingle(solidFuel.amount * solidFuel.info.density / fuelRate) : 1;

        private void UpdateThrustDependentCalcs()
        {
            double solidFuelMassG;
            if (part.Resources["SolidFuel"] is PartResource solidFuel)
                solidFuelMassG = solidFuel.amount * solidFuel.info.density * Engine.g;
            else
                solidFuelMassG = UsingME ? 7.454 : 30.75;

            float isp0 = Engine.atmosphereCurve.Evaluate(0);
            float isp1 = Engine.atmosphereCurve.Evaluate(1);
            float burnTime = burnTimeME;

            // burnTime = isp * solidFuelMassG / thrust

            if (UsingME)
            {
                // Calculate (Vac) thrust from burn time.
                thrust = Convert.ToSingle(isp0 * solidFuelMassG / burnTimeME);
                if (thrust > maxThrust)
                {
                    thrust = maxThrust;
                    burnTimeME = Convert.ToSingle(Math.Ceiling(isp0 * solidFuelMassG / thrust));
                    burnTime = burnTimeME;
                }
            }
            else
            {
                // Calculate fuel rate => burn time from thrust.
                fuelRate = thrust / (isp0 * Engine.g);
                burnTime = BurnTimeFromFuelRate;
            }

            fuelRate = thrust / (isp0 * Engine.g);
            // fuelRate = (float)solidFuelMassG / (burnTimeME * Engine.g);

            Debug.Log($"{ModTag} [ProceduralSRB.UpdateThrustDependentCalcs] UsingME? {UsingME}, thrust {thrust:F1}, maxThrust {maxThrust:F1}, burnTime {burnTime:F1}, fuelRate {fuelRate:F1}, isp {isp0:F0}s Vac / {isp1:F0}s ASL");

            float thrust0 = thrust;
            float thrust1 = thrust * isp1 / isp0;
            srbISP = $"{isp0:F0}s Vac / {isp1:F0}s ASL";
            thrustStats = $"{thrust0.ToStringSI(unit: "N", exponent: 3)} Vac / {thrust1.ToStringSI(unit: "N", exponent: 3)} ASL";

            heatProduction = heatPerThrust * Mathf.Sqrt(thrust) / (1 + part.mass);

            // Rescale the bell.
            float bellScale1m = selectedBell.chokeEndRatio / selectedBell.bellChokeDiameter;
            bellScale = bellScale1m * Mathf.Sqrt(thrust / thrust1m);
            Debug.Log($"{ModTag} {this}: bell scale: {bellScale}; thrust: {thrust}, thrust1m: {thrust1m}");

            UpdateEngineAndBellScale();
        }

        private void UpdateEngineAndBellScale()
        {
            Engine.heatProduction = heatProduction;
            Engine.maxThrust = thrust;
            part.GetComponent<ModuleEngines>().maxFuelFlow = fuelRate;

            Debug.Log($"{ModTag} {this}: rescaling bell: {bellScale}");
            selectedBell.model.transform.localScale = bellScale * Vector3.one;

            UpdateFAR();
            MoveBellAndBottomNode();
        }

        public void UpdateFAR()
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
                part.SendMessage("GeometryPartModuleRebuildMeshData");
        }

        #endregion

        public override void OnWasCopied(PartModule copyPartModule, bool asSymCounterpart)
        {
            MoveBellAndBottomNode();
            SetBellRotation(thrustDeflection);
        }

        private void HandleBellTypeChange(BaseField f, object obj) => HandleBellTypeChange();
        private void HandleBellTypeChange()
        {
            UpdateBellType();
            UpdateThrust();
            thrustDeflection = Mathf.Clamp(thrustDeflection, -25f, 25f);
        }
        
        private void HandleBellDeflectionChange(BaseField f, object obj)
        {
            HandleBellDeflectionChange((float)obj);
            // onSymmetryFieldChanged() callback has the incorrect value for parameter obj
            // So we manually invoke things for our symmetry counterparts.
            foreach (Part p in part.symmetryCounterparts)
            {
                p.FindModuleImplementing<ProceduralSRB>()?.HandleBellDeflectionChange((float)obj);
            }
        }
        private void HandleBellDeflectionChange(float oldDeflection)
        {
            // First adjust the bell relatively to the SRB, then rotate stuff
            MoveBellAndBottomNode();
            SetBellRotation(oldDeflection);
            thrustDeflection = Mathf.Clamp(thrustDeflection, -25f, 25f);
        }
        
        private void HandleThrustChange(BaseField f, object obj) => UpdateThrust();

        #region Heat

        [KSPField(isPersistant = true, guiName = "Heat", guiActiveEditor = true, guiFormat = "F3", guiUnits = "K/s", groupName = PAWGroupName)]
        public float heatProduction;

        [KSPField]
        public float heatPerThrust = 2.0f;

        // ReSharper disable once InconsistentNaming
        internal const double DraperPoint = 798;

        private void AnimateHeat()
        {
            // The emissive module is too much effort to get working, just do it the easy way.
            double n = (part.temperature - DraperPoint) / (part.maxTemp / DraperPoint);
            float num = Mathf.Clamp01(Convert.ToSingle(n));
            Material mat = selectedBell.model.GetComponent<Renderer>().sharedMaterial;
            mat.SetColor("_EmissiveColor", new Color(num * num, 0, 0));
        }

        #endregion

        private float GetLength()
        {
            return PPart.CurrentShape switch
            {
                ProceduralShapeBezierCone cone => cone.length,
                ProceduralShapeCone cone => cone.length,
                ProceduralShapeCylinder cyl => cyl.length,
                ProceduralShapePill pill => pill.length,
                ProceduralShapePolygon poly => poly.length,
                _ => 0f,
            };
        }

        #region DebugMarkers

        // ReSharper disable once InconsistentNaming
        private void LR(String txt, Vector3 point)
        {
            Color[] c = {Color.green, Color.blue, Color.magenta, Color.red, Color.yellow};
            TextAnchor[] a =
            {
                TextAnchor.UpperLeft, TextAnchor.LowerLeft, TextAnchor.UpperRight, TextAnchor.LowerRight,
                TextAnchor.MiddleCenter
            };
            float s = 0.3f;
            LineRenderer lr;
            TextMesh tm;
            if (LRs.ContainsKey(txt))
            {
                if ((point - VECs[txt]).magnitude < 0.01f)
                {
                    return;
                }
                lr = LRs[txt].GetComponent<LineRenderer>();
                tm = LRs[txt].GetComponent<TextMesh>();
            }
            else
            {
                GameObject go = new GameObject(txt);
                Debug.Log($"{ModTag} {this} added GO {txt} ({LRs.Count % c.Length})");

                lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 8;
                lr.startColor = c[LRs.Count % c.Length];
                lr.endColor = lr.startColor;
                lr.startWidth = 0.03f;
                lr.endWidth = 0.03f;
                lr.useWorldSpace = true;
                lr.material = new Material (Shader.Find("Particles/Additive"));
                LRs[txt] = go;

                tm = go.AddComponent<TextMesh>();
                tm.color = lr.startColor;
                tm.characterSize = 0.1f;
                tm.anchor = a[LRs.Count % a.Length];
            }
            VECs[txt] = point;

            //lr.SetPosition(0, point);
            lr.SetPosition(0, point + Vector3.up * s);
            lr.SetPosition(1, point - Vector3.up * s);
            lr.SetPosition(2, point);
            lr.SetPosition(3, point + Vector3.left * s);
            lr.SetPosition(4, point - Vector3.left * s);
            lr.SetPosition(5, point);
            lr.SetPosition(6, point + Vector3.forward * s);
            lr.SetPosition(7, point - Vector3.forward * s);

            tm.text = txt + " " + point;
            tm.transform.position = point + Vector3.up * s / 2 + Vector3.right * s / 2;
        }

        #endregion
    }
}
