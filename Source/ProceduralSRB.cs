using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.Utils;
using LibNoise.Modifiers;

namespace ProceduralParts
{

    public class ProceduralSRB : PartModule, IPartCostModifier
    {
        private static readonly string ModTag = "[ProceduralSRB]";
        
        public const string PAWGroupName = "ProcSRB";
        public const string PAWGroupDisplayName = "ProceduralSRB";

        private Dictionary<String, GameObject> LRs = new Dictionary<string, GameObject>();
        private Dictionary<String, Vector3> VECs = new Dictionary<string, Vector3>();
        
        public ProceduralPart PPart => _pPart ?? (_pPart = GetComponent<ProceduralPart>());
        private ProceduralPart _pPart;

        #region callbacks

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                OnUpdateEditor();
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                if (HighLogic.LoadedScene == GameScenes.LOADING)
                    LoadBells(node);
            }
            catch (Exception ex)
            {
                Debug.Log($"{ModTag}: OnLoad exception: {ex}");
                throw;
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

        public override void OnStart(StartState state)
        {
            try
            {
                if (PPart is null)
                {
                    Debug.LogError($"{ModTag} {part}.{this} Procedural Part not found");
                    return;
                }
                bottomAttachNode = part.FindAttachNode(bottomAttachNodeName);
                
                StartCoroutine(WaitAndInitialize());

                if (HighLogic.LoadedSceneIsEditor)
                {
                    GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);

                    PPart.Fields[nameof(PPart.shapeName)].uiControlEditor.onFieldChanged += HandleShapeChange;
                    Fields[nameof(selectedBellName)].uiControlEditor.onFieldChanged += HandleBellChange;
                    Fields[nameof(thrustDeflection)].uiControlEditor.onFieldChanged += HandleBellChange;
                    Fields[nameof(thrust)].uiControlEditor.onFieldChanged += HandleBellChange;
                    AddLengthChangeListener();
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"{ModTag}: OnStart exception: {ex}");
                throw;
            }
        }

        private IEnumerator WaitAndInitialize()
        {
            // Node must be initialized by procedural shape module first 
            while (!PPart.CurrentShape.nodesInitialized)
            {
                yield return new WaitForFixedUpdate();
            }
            InitializeBells();
            UpdateMaxThrust();
        }
        
        private void AddLengthChangeListener()
        {
            switch(PPart.CurrentShape)
            {
                case ProceduralShapeBezierCone cone:
                    cone.Fields[nameof(cone.length)].uiControlEditor.onFieldChanged += HandleLengthChange;
                    break;
                case ProceduralShapeCone cone:
                    cone.Fields[nameof(cone.length)].uiControlEditor.onFieldChanged += HandleLengthChange;
                    break;
                case ProceduralShapeCylinder cyl:
                    cyl.Fields[nameof(cyl.length)].uiControlEditor.onFieldChanged += HandleLengthChange;
                    break;
                case ProceduralShapePill pill:
                    pill.Fields[nameof(pill.length)].uiControlEditor.onFieldChanged += HandleLengthChange;
                    break;
            }   
        }
        
        public void OnDestroy()
        {
            GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
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
                LR("srbNozzle", part.FindModelTransform("srbNozzle").position);
                LR("bellModel ", selectedBell.model.position);
                LR("srbAttach  ", selectedBell.srbAttach.position);
                LR("bellTransform", bellTransform.position);
                LR("Transform", part.transform.position);
            }
            catch (Exception)
            {
            }

            // try
            // {
            //     UpdateBell();
            //     UpdateThrust();
            //     thrustDeflection = Mathf.Clamp(thrustDeflection, -25f, 25f);
            // }
            // catch (Exception ex)
            // {
            //     Debug.LogException(ex);
            //     enabled = false;
            // }
        }

        public void OnEditorPartEvent(ConstructionEventType type, Part ePart)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (!(ePart == part || ePart.FindChildPart(part.name, true) != null))
                return;

            if (ePart != null && type != ConstructionEventType.PartDeleted)
            {
                if (type == ConstructionEventType.PartCopied ||
                    type == ConstructionEventType.PartCreated ||
                    type == ConstructionEventType.PartAttached)
                {
                    isSymmetryOriginal = true;
                    foreach (var counterPart in part.symmetryCounterparts)
                        counterPart.GetComponent<ProceduralSRB>().isSymmetryOriginal = false;
                }

                if (type == ConstructionEventType.PartCreated ||
                    type == ConstructionEventType.PartCopied ||
                    type == ConstructionEventType.PartAttached)
                {
                    SetBellRotation();
                    foreach (var counterPart in part.symmetryCounterparts)
                        counterPart.GetComponent<ProceduralSRB>().SetBellRotation();
                }
            }

        }

        //[PartMessageListener(typeof(PartResourceInitialAmountChanged), scenes: GameSceneFilter.AnyEditor)]
        //public void PartResourceChanged(PartResource resource, double amount)
        [KSPEvent(guiActive = false, active = true)]
        public void OnPartResourceInitialAmountChanged(BaseEventDetails data)
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
        public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
        {
            var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
            data.Set<AttachNode> ("node", node);
            data.Set<float> ("minDia", minDia);
            data.Set<float> ("area", area);
            part.SendEvent ("OnPartAttachNodeSizeChanged", data, 0);
        }

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartAttachNodeSizeChanged(BaseEventDetails data)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            AttachNode node = data.Get<AttachNode>("node");
            float minDia = data.Get<float>("minDia");
            // ReSharper disable once CompareOfFloatsByEqualityOperator

            if (minDia != attachedEndSize)
                attachedEndSize = minDia;

            if (node.id == bottomAttachNodeName)
                UpdateMaxThrust();
        }

        [KSPField]
        public float costMultiplier = 1.0f;

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

        
        #region Bell selection
        
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "SRB Type", groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName, groupStartCollapsed = false), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedBellName;

        // ReSharper disable once InconsistentNaming
        [KSPField(isPersistant = false, guiName = "ISP", guiActive = false, guiActiveEditor = true, groupName = PAWGroupName)]
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
        
        #region Objects

        [KSPField(isPersistant = true)]
        public bool isSymmetryOriginal = false;

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
            Debug.Log($"{ModTag} {part}.{this}: InitializeBells");
            // Initialize the configs.
            if (srbConfigs == null)
                LoadSRBConfigs();

            BaseField field = Fields["selectedBellName"];
            // ReSharper disable once PossibleNullReferenceException
            switch (srbConfigs.Count)
            {
                case 0:
                    Debug.LogError($"{ModTag} {part}.{this}: No SRB bells configured");
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
            bellRootTransform = part.FindModelTransform(srbBellName + "root");
            if (bellRootTransform == null)
                bellRootTransform = bellTransform;
            thrustTransform = bellTransform.Find(thrustVectorTransformName);

            PrepareBellModels();

            // Select the bell
            if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
                selectedBellName = srbConfigsSerialized[0].GetValue("name");
            selectedBell = srbConfigs[selectedBellName];

            ConfigureRealFuels();

            // Initialize the modules.
            InitModulesFromBell();

            // Break out at this stage during loading scene
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                UpdateThrustDependentCalcs();
                return;
            }

            // Update the thrust according to the equation when in editor mode, don't mess with ships in flight
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateThrustDependentCalcs();
            }
            else
            {
                if (bellScale <= 0 || heatProduction <= 0)
                {
                    // We've reloaded from a legacy save
                    // Use the new heat production equation, but use the legacy bell scaling one.
                    UpdateThrustDependentCalcs();

                    // Legacy bell scaling equation
                    bellScale = Mathf.Sqrt(thrust / deprecatedThrustScaleFactor);
                    Debug.Log($"{ModTag} {part}.{this}: legacy bell scale: {bellScale}");
                }

                UpdateEngineAndBellScale();
            }

            // It makes no sense to have a thrust limiter for SRBs
            // Even though this is present in stock, I'm disabling it.
            BaseField thrustLimiter = ((PartModule)Engine).Fields["thrustPercentage"];
            thrustLimiter.guiActive = false;
            thrustLimiter.guiActiveEditor = false;

            //ProceduralPart pPart = GetComponent<ProceduralPart>();
            if (PPart != null)
            {
                SetBellRotation();
            }
            else
                Debug.Log($"{ModTag} {part}.{this}: ProceduralSRB.InitializeBells() Unable to find ProceduralPart component! (null) for {part.name}");

            // Move thrust transform to the end of the bell
            thrustTransform.position = selectedBell.srbAttach.position;
        }

        private void MoveBellAndBottomNode()
        {
            if (!PPart.CurrentShape.nodesInitialized)
            {
                return;
            }
            Debug.Log($"{ModTag} {part}.{this}: placing bell and node");
            // Move bell a little bit inside the SRB so gimbaling and tilting won't make weird gap between bell choke and SRB
            // d = chokeDia * scale * 0.5 * sin(max deflection angle)
            float d = (float)(selectedBell.bellChokeDiameter / 2 * bellScale * Math.PI * (
                                  selectedBell.gimbalRange + Math.Abs(thrustDeflection)) / 180f);
            Debug.Log($"{ModTag} {part}.{this}: bell d: {d}; bellD: {selectedBell.bellChokeDiameter}; scale: {bellScale}; angle: {selectedBell.gimbalRange}");
            
            // Using part transform as a base and shape length as offset for bell placement
            bellTransform.position = PPart.transform.TransformPoint(
                PPart.transform.InverseTransformPoint(part.transform.position) - 
                                GetLength() / 2 * Vector3.up + d * Vector3.up);

            Debug.Log($"{ModTag} {part}.{this}: node position before: {bottomAttachNode.position}");
            Vector3 origNodePosition = bottomAttachNode.position;
            // Place attachment node inside the bell 
            bottomAttachNode.position = PPart.transform.InverseTransformPoint(selectedBell.srbAttach.position);
            Debug.Log($"{ModTag} {part}.{this}: node position after: {bottomAttachNode.position}");

            // Translate attached parts
            TranslateAttachedPart(origNodePosition, bottomAttachNode.position);
        }

        private void TranslateAttachedPart(Vector3 origPosition, Vector3 newPosition)
        {
            if (bottomAttachNode.attachedPart is Part pushTarget)
            {
                Vector3 translation = newPosition - origPosition; 
                Debug.Log($"{ModTag} {part}.{this}: translation: {translation}");

                PPart.CurrentShape.TranslatePart(pushTarget, translation);
            }
        }
        
        public void RotateAttachedPartAndNode(Vector3 rotAxis, float delta)
        {
            Vector3 rotationDelta = Vector3.right * (float) (Math.PI * thrustDeflection / 180f);
            bottomAttachNode.orientation = bottomAttachNode.originalOrientation + rotationDelta;
            Debug.Log($"{ModTag} {part}.{this}: node orientation : {bottomAttachNode.orientation}; original: {bottomAttachNode.originalOrientation}");

            if (bottomAttachNode.attachedPart is Part rotTarget)
            {
                // If the attached part is a child of ours, rotate it directly.
                // If it is our parent, then we need to rotate ourselves
                Vector3 opposingNodePos = rotTarget.transform.TransformPoint(bottomAttachNode.FindOpposingNode().position);

                if (rotTarget == part.parent)
                {
                    // We will push once for each symmetry sibling, so scale this push.
                    //float sibMult = part.symmetryCounterparts == null ? 1f : 1f / (part.symmetryCounterparts.Count + 1);
                    //pushTarget = GetEldestParent(this.part);

                    //this.part.transform.Translate(-translation * sibMult, Space.Self);
                    rotTarget = part;
                    delta = -delta;
                }
                
                rotTarget.partTransform.Rotate(rotAxis, delta, Space.World);
                // Check new bell position after rotation
                Vector3 shift = rotTarget == part
                    ? opposingNodePos - selectedBell.srbAttach.position
                    : selectedBell.srbAttach.position - opposingNodePos;
                // If we've rotated anything, we've moved that thing away from attachment node. Need to bring it back.
                rotTarget.transform.Translate(shift, Space.World);
                
                // Fix attach node position
                bottomAttachNode.position = PPart.transform.InverseTransformPoint(selectedBell.srbAttach.position);
            }
        }
        private void ConfigureRealFuels()
        {
            // Config for Real Fuels.
            if (part.Modules.Contains("ModuleEngineConfigs"))
            {
                // ReSharper disable once InconsistentNaming
                var MEC = part.Modules["ModuleEngineConfigs"];
                ModularEnginesChangeThrust = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), MEC, "ChangeThrust");
                try
                {
                    ModularEnginesChangeEngineType = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), MEC, "ChangeEngineType");
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

        private void UpdateBell()
        {
            if (selectedBell == null || selectedBellName == selectedBell.name && oldThrustDeflection == thrustDeflection)
                return;

            SRBBellConfig oldSelectedBell = selectedBell;

            if (!srbConfigs.TryGetValue(selectedBellName, out selectedBell))
            {
                Debug.LogError($"{ModTag} {part}.{this}: Selected bell name \"{selectedBellName}\" does not exist. Reverting.");
                selectedBellName = oldSelectedBell.name;
                selectedBell = oldSelectedBell;
                return;
            }

            oldSelectedBell.model.gameObject.SetActive(false);

            SetBellRotation();

            InitModulesFromBell();

            UpdateMaxThrust();

            oldThrustDeflection = thrustDeflection;
        }

        private void SetBellRotation()
        {
            try
            {
                if (bellTransform == null || selectedBell == null)
                    return;

                bellTransform.localEulerAngles = Vector3.zero;
                var rotAxis = Vector3.Cross(part.partTransform.right, part.partTransform.up);

                var adjustedDir = thrustDeflection;
                var oldAdjustedDir = oldThrustDeflection;

                if (part.symMethod == SymmetryMethod.Mirror && !part.IsSurfaceAttached())
                {
                    if (!isSymmetryOriginal)
                        adjustedDir *= -1;
                }

                bellTransform.Rotate(rotAxis, adjustedDir, Space.World);
                selectedBell.srbAttach.Rotate(rotAxis, adjustedDir, Space.World);
                RotateAttachedPartAndNode(rotAxis, adjustedDir - oldAdjustedDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{ModTag} {part}.{this}:** Exception within SetBellRotation:");
                Debug.LogException(ex);
            }
        }

        private void InitModulesFromBell()
        {
            // Set the bits and pieces
            selectedBell.model.gameObject.SetActive(true);
            if (selectedBell.atmosphereCurve != null)
                Engine.atmosphereCurve = selectedBell.atmosphereCurve;
            ModuleGimbal md = GetComponent<ModuleGimbal>();
            if (md != null) 
            {
                md.gimbalRange = selectedBell.gimbalRange;
                md.gimbalTransformName = selectedBell.model.transform.name;
                md.gimbalRangeXN = md.gimbalRangeXP = md.gimbalRangeYN = md.gimbalRangeYP = selectedBell.gimbalRange;
            }

            //Debug.Log($"{ModTag} {part}.{this}: gimbal range: {selectedBell.gimbalRange}");
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

        [KSPField(isPersistant = true, guiName = "Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "F3", guiUnits = "N", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSmall = 10, incrementSlide = 1f, sigFigs = 5, unit = "kN", useSI = true)]
        public float thrust = 250;
        private float oldThrust;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "Thrust ASL", groupName = PAWGroupName)]
        public string thrustASL;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Burn Time", groupName = PAWGroupName)]
        public string burnTime;

        [KSPField(isPersistant = true, guiName = "Burn Time", guiActive = false, guiActiveEditor = false, guiFormat = "F0", guiUnits = "s", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = 600f, incrementLarge = 60f, incrementSmall = 0, incrementSlide = 2e-4f)]
        public float burnTimeME = 60;
        private float oldBurnTimeME;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Thrust", groupName = PAWGroupName)]
        public string thrustME;

        [KSPField(isPersistant = true, guiName = "Deflection", guiActive = false, guiActiveEditor = true, guiFormat = "F3", guiUnits = "°", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = -25f, maxValue = 25f, incrementLarge = 5f, incrementSmall = 1f, incrementSlide = 0.1f, sigFigs = 5, unit = "°")]
        public float thrustDeflection = 0;
        private float oldThrustDeflection;

        // ReSharper disable once InconsistentNaming
        [KSPField]
        public float thrust1m = 1;

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

            Debug.Log($"{ModTag} {part}.{this}: attachedEndSize: {attachedEndSize}");
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
                    Debug.Log($"{ModTag} {part}.{this}: UsingME = {UsingME}, minBurnTime = {minBurnTime}, maxThrust = {maxThrust}");
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
            if (!force && oldThrust == thrust && burnTimeME == oldBurnTimeME && oldThrustDeflection == thrustDeflection)
                return;
            // ReSharper restore CompareOfFloatsByEqualityOperator

            UpdateThrustDependentCalcs();

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
            Debug.Log($"{ModTag} {part}.{this}: ProceduralSRB.UpdateThrustDependentCalcs();");
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
                Debug.Log($"{ModTag} {part}.{this}: Not using MEC ChangeThrust, thrust = {thrust}");
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
                Debug.Log($"{ModTag} {part}.{this}: ME thrust calculation");
                thrust = (float)(atmosphereCurve.Evaluate(0) * solidFuelMassG / burnTimeME);
                Debug.Log($"thrust = {thrust}; maxThrust = {maxThrust}");
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
            Debug.Log($"{ModTag} {part}.{this}: bell scale: {bellScale}; thrust: {thrust}, thrust1m: {thrust1m}");

            UpdateEngineAndBellScale();
        }

        private void UpdateEngineAndBellScale()
        {
            Engine.heatProduction = heatProduction;
            Engine.maxThrust = thrust;
            //part.GetComponent<ModuleEngines>().maxFuelFlow = (float)(0.1*fuelRate);
            part.GetComponent<ModuleEngines>().maxFuelFlow = fuelRate;

            Debug.Log($"{ModTag} {part}.{this}: rescaling bell: {bellScale}");
            selectedBell.model.transform.localScale = new Vector3(bellScale, bellScale, bellScale);

            //if (UsingME)
            //    ModularEnginesChangeThrust(thrust);
            UpdateFAR();
            MoveBellAndBottomNode();
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

        #endregion

        private void HandleShapeChange(BaseField f, object obj)
        {
            HandleShapeChange();
        }

        private void HandleShapeChange()
        {
            HandleLengthChange();
            AddLengthChangeListener();
            MoveBellAndBottomNode();
        }
        private void HandleLengthChange(BaseField f, object obj)
        {
            HandleLengthChange();
        }

        private void HandleLengthChange()
        {
            MoveBellAndBottomNode();
        }

        private void HandleBellChange(BaseField f, object obj)
        {
            HandleBellChange();
        }
        private void HandleBellChange()
        {
            UpdateBell();
            UpdateThrust();
            thrustDeflection = Mathf.Clamp(thrustDeflection, -25f, 25f);
        }

        #region Heat

        [KSPField(isPersistant = true, guiName = "Heat", guiActive = false, guiActiveEditor = true, guiFormat = "F3", guiUnits = "K/s", groupName = PAWGroupName)]
        public float heatProduction;

        [KSPField]
        public float heatPerThrust = 2.0f;

        [KSPField]
        public bool useOldHeatEquation = false;

        internal const double DraperPoint = 798; 

        private void AnimateHeat()
        {
            // The emissive module is too much effort to get working, just do it the easy way.
            double num = Clamp((part.temperature - DraperPoint) / (part.maxTemp / DraperPoint), 0, 1);
            Material mat = selectedBell.model.GetComponent<Renderer>().sharedMaterial;
            mat.SetColor("_EmissiveColor", new Color((float)(num * num), 0, 0));
        }

        private double Clamp(double x, double min, double max) => double.IsNaN(x) ? 0 : Math.Max(min, Math.Min(x, max));

        #endregion

        private float GetLength()
        {
            switch(PPart.CurrentShape)
            {
                case ProceduralShapeBezierCone cone:
                    return cone.length;
                case ProceduralShapeCone cone:
                    return cone.length;
                case ProceduralShapeCylinder cyl:
                    return cyl.length;
                case ProceduralShapePill pill:
                    return pill.length;
            }
            return 0f;
        } 
        
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
                Debug.Log($"{ModTag} {part}.{this} added GO {txt} ({LRs.Count % c.Length})");

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
    }
}
