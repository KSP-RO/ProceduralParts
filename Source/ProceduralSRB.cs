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

        public ProceduralPart PPart => _pPart ??= GetComponent<ProceduralPart>();
        private ProceduralPart _pPart;
        private bool started = false;

        private PartResource fuelResource;

        [KSPField]
        public bool debugMarkers = true;

        #region Callbacks

        public override void OnAwake()
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING)
                srbConfigNodes = part.partInfo.partPrefab.FindModuleImplementing<ProceduralSRB>().srbConfigNodes;
        }

        public override void OnLoad(ConfigNode node)
        {
            bottomAttachNode ??= part.FindAttachNode(bottomAttachNodeName);
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                srbConfigNodes = node.GetNodes("SRB_BELL");
            LoadBells(srbConfigNodes);
            InitializeBells();
            UpdateMaxThrust(false);
        }

        public override void OnSave(ConfigNode node) => node.SetValue("isEnabled", "True");

        public override void OnStart(StartState state)
        {
            if (PPart is null)
            {
                Debug.LogError($"{ModTag} {part}.{this} Procedural Part not found");
                return;
            }

            bottomAttachNode = part.FindAttachNode(bottomAttachNodeName);
            fuelResource = GetFuelResource();
            LoadBells(srbConfigNodes);
            InitializeBells();
            UpdateMaxThrust(false);

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(selectedBellName)].guiActiveEditor = srbConfigs.Count > 1;
                (Fields[nameof(selectedBellName)].uiControlEditor as UI_ChooseOption).options = srbConfigs.Keys.ToArray();
                Fields[nameof(selectedBellName)].uiControlEditor.onFieldChanged += HandleBellTypeChange;
                Fields[nameof(selectedBellName)].uiControlEditor.onSymmetryFieldChanged += HandleBellTypeChange;

                Fields[nameof(thrustDeflection)].uiControlEditor.onFieldChanged += HandleBellDeflectionChange;
                Fields[nameof(thrustDeflection)].uiControlEditor.onSymmetryFieldChanged += HandleBellDeflectionChange;

                Fields[nameof(burnTimeME)].uiControlEditor.onFieldChanged += HandleBurnTimeChange;
                Fields[nameof(burnTimeME)].uiControlEditor.onSymmetryFieldChanged += HandleBurnTimeChange;

                Fields[nameof(thrust)].uiControlEditor.onFieldChanged += HandleThrustChange;
                Fields[nameof(thrust)].uiControlEditor.onSymmetryFieldChanged += HandleThrustChange;

                Fields[nameof(thrust)].guiActiveEditor = !UsingME;
                if (!UsingME)
                    Fields[nameof(burnTimeME)].uiControlEditor = Fields[nameof(burnTimeME)].uiControlFlight;
            }
            started = true;
            StartCoroutine(DisableThrustLimitorCR());
        }

        public void OnDestroy()
        {
            foreach (var data in LRs.Values)
                data.transform.gameObject.DestroyGameObject();
            LRs.Clear();
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
                AnimateHeat();
            if (debugMarkers)
            {
                LR("srbNozzle", part.FindModelTransform("srbNozzle"));
                LR("bellModel", selectedBell.model);
                LR("srbAttach", selectedBell.srbAttach);
                LR("bellTransform", bellTransform);
                LR("Transform", part.transform);
//                LR("BottomNode", part.transform.TransformPoint(bottomAttachNode.position));
            }
        }

        [KSPEvent(active = true)]
        public void OnResourceMaxChanged(BaseEventDetails _)
        {
            if (HighLogic.LoadedSceneIsEditor)
                UpdateMaxThrust(true);
        }

        [KSPEvent(active = true)]
        public void OnPartAttachNodeSizeChanged(BaseEventDetails data)
        {
            if (HighLogic.LoadedSceneIsEditor &&
                data.Get<AttachNode>("node") is AttachNode node &&
                data.Get<float>("minDia") is float minDia &&
                node.id == bottomAttachNodeName)
            {
                attachedEndSize = minDia;
                UpdateMaxThrust(false);
            }
        }

        [KSPEvent(active = true)]
        public void OnPartNodeMoved(BaseEventDetails data)
        {
            if (data.Get<AttachNode>("node") is AttachNode node && node == bottomAttachNode)
                SetBellAndBottomNodePositionAndRotation();
        }

        #endregion

        [KSPField]
        public float costMultiplier = 1.0f;

        #region IPartCostModifier implementation
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => thrust * 0.5f * costMultiplier;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;
        #endregion

        #region Bell selection

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "SRB Type", groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedBellName;

        [KSPField(isPersistant = true)]
        public float bellScale = 1;

        private SRBBellConfig selectedBell;
        private readonly Dictionary<string, SRBBellConfig> srbConfigs = new Dictionary<string, SRBBellConfig>();
        private ConfigNode[] srbConfigNodes;

        [Serializable]
        public class SRBBellConfig : IConfigNode
        {
            [Persistent] public string name;
            [Persistent] public string modelName;
            [Persistent] public string srbAttachName = "srbAttach";
            [Persistent] public FloatCurve atmosphereCurve;
            [Persistent] public float gimbalRange = -1;
            [Persistent] public float bellChokeDiameter = 0.5f;
            [Persistent] public float chokeEndRatio = 0.5f;
            [Persistent] public string realFuelsEngineType = string.Empty;

            [NonSerialized] public Transform model;
            [NonSerialized] public Transform srbAttach;

            public SRBBellConfig() { }

            public void Load(ConfigNode node)
            {
                ConfigNode.LoadObjectFromConfig(this, node);
                name ??= node.GetValue("displayName");
            }
            public void Save(ConfigNode node) => ConfigNode.CreateConfigFromObject(this, node);
        }

        private void LoadBells(ConfigNode[] bellNodes)
        {
            srbConfigs.Clear();
            foreach (ConfigNode srbNode in bellNodes)
            {
                SRBBellConfig conf = ConfigNode.CreateObjectFromConfig<SRBBellConfig>(srbNode);
                srbConfigs.Add(conf.name, conf);
            }
        }

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
        private EngineWrapper Engine => _engineWrapper ??= new EngineWrapper(part);

        #endregion

        private void InitializeBells()
        {
            Debug.Log($"{ModTag} {this}: InitializeBells");

            bellTransform = part.FindModelTransform(srbBellName);
            PrepareBellModels();

            if (srbConfigs.Count < 1)
            {
                Debug.LogError($"{ModTag} {this}: No valid SRB bells configured");
                return;
            }

            // Select the bell
            if (string.IsNullOrEmpty(selectedBellName) || !srbConfigs.ContainsKey(selectedBellName))
                selectedBellName = srbConfigs.First().Key;
            selectedBell = srbConfigs[selectedBellName];

            ConfigureRealFuels();

            InitModulesFromBell();
            SetBellAndBottomNodePositionAndRotation();

            if (HighLogic.LoadedSceneIsFlight)
            {
                thrustTransform = bellTransform.Find(thrustVectorTransformName);
                thrustTransform.position = selectedBell.srbAttach.position;
            }
        }

        #region Attachments and nodes

        // Set bell, bottom node, and attached parts based on thrustDeflection and other parameters
        private void SetBellAndBottomNodePositionAndRotation()
        {
            if (bellTransform == null || selectedBell == null)
                return;

            // Move bell inside the SRB so gimbaling and tilting won't make weird gap between bell choke and SRB
            float maxRange = selectedBell.gimbalRange + Math.Abs(thrustDeflection);
            float d = selectedBell.bellChokeDiameter * 0.5f * bellScale * Mathf.Sin(maxRange * Mathf.Deg2Rad);

            // Use part transform as a base and shape length as offset for bell placement
            var rotAxis = Vector3.forward;
            bellTransform.position = part.transform.TransformPoint((-GetLength() / 2 + d) * Vector3.up);
            bellTransform.localRotation = Quaternion.AngleAxis(thrustDeflection, rotAxis);

            // Place attachment node inside the bell 
            var pos = selectedBell.srbAttach.position;
            bottomAttachNode.originalPosition = bottomAttachNode.position = part.transform.InverseTransformPoint(pos);
            Vector3 rotation = Vector3.right * (Mathf.Deg2Rad * thrustDeflection);
            bottomAttachNode.orientation = bottomAttachNode.originalOrientation + rotation;

            Debug.Log($"{ModTag} {this} ({part.persistentId}): MoveBellAndBottomNode: bottom node moved to {bottomAttachNode.position}");
        }

        private void TranslateAndRotateAttachedPart(Vector3 newPosition, Vector3 rotAxis, float deflection)
        {
            if (bottomAttachNode.attachedPart is Part moveTarget)
            {
                var rot = Quaternion.AngleAxis(deflection, rotAxis);

                if (moveTarget == part.parent)
                    moveTarget = part;
                moveTarget.transform.localRotation = moveTarget == part ? Quaternion.Inverse(rot) : rot;
                Vector3 opposingNodePos = moveTarget.transform.TransformPoint(bottomAttachNode.FindOpposingNode().position);
                Vector3 shift = (moveTarget == part ? 1 : -1) * (opposingNodePos - newPosition);
                Debug.Log($"{ModTag} {this}: shifting: {moveTarget} by {shift}");
                moveTarget.transform.Translate(shift, Space.World);
            }
        }

        #endregion

        private void ConfigureRealFuels()
        {
            if (part.Modules.GetModule("ModuleEngineConfigs") is PartModule MEC)
            {
                try
                {
                    ModularEnginesChangeThrust = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), MEC, "ChangeThrust");
                    ModularEnginesChangeEngineType = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), MEC, "ChangeEngineType");
                }
                catch
                {
                    ModularEnginesChangeThrust = null;
                    ModularEnginesChangeEngineType = null;
                }
            }
        }

        private void PrepareBellModels()
        {
            var toRemove = new List<string>();
            foreach (SRBBellConfig conf in srbConfigs.Values)
            {
                if (part.FindModelTransform(conf.modelName) is Transform model &&
                    model.Find(conf.srbAttachName) is Transform srbAttach)
                {
                    conf.model = model;
                    conf.model.transform.parent = bellTransform;
                    conf.srbAttach = srbAttach;
                    // Only enable the collider for flight mode. This prevents any surface attachments.
                    var coll = conf.model.GetComponent<Collider>();
                    if (HighLogic.LoadedSceneIsEditor && coll != null)
                        Destroy(coll);
                    conf.model.gameObject.SetActive(false);
                } else
                    toRemove.Add(conf.modelName);

                if (conf.model == null)
                    Debug.LogError($"{ModTag} {part}.{this}: Unable to find model transform for SRB bell name: {conf.modelName}");
                else if (conf.srbAttach == null)
                    Debug.LogError($"{ModTag} {part}.{this}: Unable to find srbAttach for SRB bell name: {conf.modelName}");
            }
            foreach (var s in toRemove)
                srbConfigs.Remove(s);
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

            if (ModularEnginesChangeEngineType != null && !string.IsNullOrEmpty(selectedBell.realFuelsEngineType))
                ModularEnginesChangeEngineType(selectedBell.realFuelsEngineType);
        }

        #endregion

        #region Thrust

        private bool UsingME => ModularEnginesChangeThrust != null;

        [KSPField(guiName = "Isp", guiActiveEditor = true, groupName = PAWGroupName)]
        public string srbISP;

        [KSPField(isPersistant = true, guiName = "Thrust", guiActive = true, guiActiveEditor = true, guiFormat = "F3", guiUnits = "kN", groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = float.PositiveInfinity, incrementLarge = 100f, incrementSmall = 10, incrementSlide = 1f, sigFigs = 5, unit = "kN", useSI = true)]
        public float thrust = 250;

        [KSPField(guiActiveEditor = true, guiName = "Thrust", groupName = PAWGroupName)]
        public string thrustStats;

        [KSPField(isPersistant = true, guiName = "Burn Time", guiActive = true, guiActiveEditor = true, guiFormat = "F1", guiUnits = "s", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 1f, maxValue = 600f, incrementLarge = 60f, incrementSmall = 5, incrementSlide = 0.1f, unit = "s", sigFigs = 1)]
        public float burnTimeME = 60;

        [KSPField(isPersistant = true, guiName = "Deflection", guiActiveEditor = true, guiFormat = "F1", guiUnits = "°", groupName = PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = -25f, maxValue = 25f, incrementLarge = 5f, incrementSmall = 1f, incrementSlide = 0.05f, unit = "°", sigFigs = 2)]
        public float thrustDeflection;

        [KSPField]
        public float thrust1m = 1;

        private float MaxThrust => (float)Math.Round(Mathf.Max(attachedEndSize * attachedEndSize * thrust1m, 10), 1);
        public float attachedEndSize = float.PositiveInfinity;

        [KSPField]
        public float fuelRate;

        private float BurnTimeFromFuelRate => fuelResource is PartResource ?
            Convert.ToSingle(fuelResource.maxAmount * fuelResource.info.density / fuelRate) : 1;

        private float FuelMassG => fuelResource is PartResource
                                    ? (float)fuelResource.maxAmount * fuelResource.info.density * Engine.g
                                    : UsingME ? 7.454f : 30.75f;

        private void UpdateMaxThrust(bool fireEvent = false)
        {
            if (selectedBell == null)
                return;
            float minBurnTime = Mathf.Ceil(Engine.atmosphereCurve.Evaluate(0) * FuelMassG / MaxThrust);
            thrust = Mathf.Clamp(thrust, 0, MaxThrust);
            burnTimeME = Mathf.Max(burnTimeME, minBurnTime);

            if (HighLogic.LoadedSceneIsEditor)
            {
                Debug.Log($"{ModTag} {this}: UpdateMaxThrust: minBurnTime = {minBurnTime}, maxThrust = {MaxThrust}");
                (Fields[nameof(thrust)].uiControlEditor as UI_FloatEdit).maxValue = MaxThrust;
                (Fields[nameof(burnTimeME)].uiControlEditor as UI_FloatEdit).minValue = minBurnTime;
            }

            // Uncertain why this keeps getting turned on under certain KSPField changes.
            FixThrustLimiter();

            AutoAlignThrustAndBurnTime();
            UpdateThrustDependentCalcs();
            UpdateEngineAndBellScale();
            SetBellAndBottomNodePositionAndRotation();
            UpdateFAR();

            if (HighLogic.LoadedSceneIsEditor && fireEvent)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        private void UpdateThrustDependentCalcs()
        {
            float isp0 = Engine.atmosphereCurve.Evaluate(0);
            float isp1 = Engine.atmosphereCurve.Evaluate(1);
            float burnTime = burnTimeME;
            fuelRate = thrust / (isp0 * Engine.g);

            Debug.Log($"{ModTag} [ProceduralSRB.UpdateThrustDependentCalcs] UsingME? {UsingME}, thrust {thrust:F1}, maxThrust {MaxThrust:F1}, burnTime {burnTime:F1}, fuelRate {fuelRate:F1}, isp {isp0:F0}s Vac / {isp1:F0}s ASL");

            float thrust0 = thrust;
            float thrust1 = thrust * isp1 / isp0;
            srbISP = $"{isp0:F0}s Vac / {isp1:F0}s ASL";
            thrustStats = $"{thrust0.ToStringSI(unit: "N", exponent: 3)} Vac / {thrust1.ToStringSI(unit: "N", exponent: 3)} ASL";

            heatProduction = heatPerThrust * Mathf.Sqrt(thrust) / (1 + part.mass);

            // Rescale the bell.
            float bellScale1m = selectedBell.chokeEndRatio / selectedBell.bellChokeDiameter;
            bellScale = bellScale1m * Mathf.Sqrt(thrust / thrust1m);
        }

        private void UpdateEngineAndBellScale()
        {
            Engine.heatProduction = heatProduction;
            Engine.maxThrust = thrust;
            part.GetComponent<ModuleEngines>().maxFuelFlow = fuelRate;
            selectedBell.model.transform.localScale = bellScale * Vector3.one;
        }

        #endregion

        #region Third Party Integration

        private Action<float> ModularEnginesChangeThrust;
        private Action<string> ModularEnginesChangeEngineType;

        private DateTime updateMEThrustReqTime;
        private Coroutine delayedUpdateMEThrustCR = null;
        private void DelayedUpdateMEThrust()
        {
            updateMEThrustReqTime = DateTime.Now.Add(TimeSpan.FromSeconds(1));
            if (delayedUpdateMEThrustCR == null)
                delayedUpdateMEThrustCR = StartCoroutine(DelayedUpdateMEThrustCR());
        }
        private System.Collections.IEnumerator DelayedUpdateMEThrustCR()
        {
            while (DateTime.Now < updateMEThrustReqTime)
                yield return new WaitForSeconds(0.25f);
            ModularEnginesChangeThrust?.Invoke(thrust);
            delayedUpdateMEThrustCR = null;
        }

        // Real fuels integration
        [KSPEvent(active = true)]
        public void OnEngineConfigurationChanged()
        {
            fuelResource = GetFuelResource();
            UpdateMaxThrust();
        }

        public void UpdateFAR()
        {
            if (started && (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight))
                part.SendMessage("GeometryPartModuleRebuildMeshData");
        }

        // OnStartFinished is too soon for this.  It must be turned back on then.
        private System.Collections.IEnumerator DisableThrustLimitorCR()
        {
            yield return new WaitForSeconds(0.1f);
            FixThrustLimiter();
        }

        private void FixThrustLimiter()
        {
            BaseField thrustLimiter = ((PartModule)Engine).Fields["thrustPercentage"];
            thrustLimiter.SetValue(100, (PartModule)Engine);
            thrustLimiter.guiActive = thrustLimiter.guiActiveEditor = false;
        }

        #endregion

        private PartResource GetFuelResource()
        {
            var prop = part.FindModuleImplementing<ModuleEngines>().propellants.FirstOrDefault();
            string resName = prop?.name ?? "SolidFuel";
            return part.Resources[resName];
        }

        #region ChangeHandlers

        private void HandleBellTypeChange(BaseField f, object obj)
        {
            selectedBell.model.gameObject.SetActive(false);
            selectedBell = srbConfigs[selectedBellName];
            InitModulesFromBell();
            UpdateMaxThrust();
        }

        private void HandleBellDeflectionChange(BaseField f, object obj)
        {
            SetBellAndBottomNodePositionAndRotation();
            TranslateAndRotateAttachedPart(selectedBell.srbAttach.position, Vector3.forward, thrustDeflection);
        }

        // User changed thrust, automatically adjust burntime
        private void HandleThrustChange(BaseField f, object obj)
        {
            AlignBurnTimeToThrust();
            UpdateMaxThrust(false);
        }

        // User changed burntime, automatically adjust thrust
        private void HandleBurnTimeChange(BaseField f, object obj)
        {
            AlignThrustToBurnTime();
            UpdateMaxThrust(false);
            if (ModularEnginesChangeThrust != null)
                DelayedUpdateMEThrust();
        }

        private void AlignBurnTimeToThrust()
        {
            float isp0 = Engine.atmosphereCurve.Evaluate(0);
            burnTimeME = isp0 * FuelMassG / thrust;
        }

        private void AlignThrustToBurnTime()
        {
            float isp0 = Engine.atmosphereCurve.Evaluate(0);
            thrust = isp0 * FuelMassG / burnTimeME;
        }

        private void AutoAlignThrustAndBurnTime()
        {
            if (UsingME)
                AlignThrustToBurnTime();
            else
                AlignBurnTimeToThrust();
        }

        #endregion

        #region Heat

        [KSPField(isPersistant = true, guiName = "Heat", guiActiveEditor = true, guiFormat = "F3", guiUnits = "K/s", groupName = PAWGroupName)]
        public float heatProduction;

        [KSPField]
        public float heatPerThrust = 2.0f;

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

        private readonly TextAnchor[] textAnchors = { TextAnchor.UpperLeft, TextAnchor.LowerLeft, TextAnchor.UpperRight, TextAnchor.LowerRight, TextAnchor.MiddleCenter };
        private readonly Color[] LRcolors = { Color.green, Color.blue, Color.magenta, Color.red, Color.yellow, Color.cyan };
        private readonly Dictionary<string, RendererData> LRs = new Dictionary<string, RendererData>(8);
        internal class RendererData
        {
            internal string txt;
            internal Transform transform;
            internal LineRenderer lr;
            internal TextMesh tm;

            public RendererData(string txt, Transform transform, LineRenderer lr, TextMesh tm)
            {
                this.txt = txt;
                this.transform = transform;
                this.lr = lr;
                this.tm = tm;
            }
        }
        private void LR(string txt, Transform transform)
        {
            float s = 0.3f;
            if (!LRs.TryGetValue(txt, out RendererData data))
            {
                GameObject go = new GameObject(txt);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 8;
                lr.startColor = LRcolors[LRs.Count % LRcolors.Length];
                lr.endColor = lr.startColor;
                lr.startWidth = 0.03f;
                lr.endWidth = 0.03f;
                lr.useWorldSpace = true;
                lr.material = new Material(Shader.Find("KSP/Particles/Additive"));

                TextMesh tm = go.AddComponent<TextMesh>();
                tm.color = lr.startColor;
                tm.characterSize = 0.1f;
                tm.anchor = textAnchors[LRs.Count % textAnchors.Length];
                tm.transform.localPosition = Vector3.up * s / 2 + Vector3.right * s / 2;
                tm.transform.SetParent(transform, false);
                LRs[txt] = data = new RendererData(txt, transform, lr, tm);
            }
            Transform t = data.transform;
            Vector3 point = t.position;

            Vector3[] positions = new Vector3[]
            {
                point + t.up * s,
                point - t.up * s,
                point,
                point + t.right * s * 0.5f,
                point - t.right * s * 0.5f,
                point,
                point + t.forward * s * 2,
                point - t.forward * s * 2,
            };
            data.lr.SetPositions(positions);
            data.tm.text = txt + " " + point;
        }
        #endregion
    }
}
