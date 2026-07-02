using System;
using System.Reflection;
using UnityEngine;

namespace ProceduralParts
{
    /// <summary>
    /// Adds independent width / height / length scaling to a fixed-mesh intake part (e.g. the stock
    /// intakes), so a single modeled part covers a range of sizes. Scales the visual model and its
    /// colliders per axis, repositions attach nodes, regenerates drag cubes, scales mass/cost, and --
    /// for AJE -- drives AJEInlet.Area from the frontal area (width x height). Applied to every intake
    /// via a ModuleManager patch; nothing here is intake-specific except the AJE Area hook.
    ///
    /// Axis convention: the part's model-local LENGTH (airflow) axis is <see cref="lengthAxis"/> (x|y|z);
    /// the other two local axes are the width and height that set the frontal capture area. Meshes differ
    /// in orientation, so lengthAxis is per-part config.
    /// </summary>
    public class ModuleIntakeScale : PartModule, IPartMassModifier, IPartCostModifier
    {
        private const string GRP = "IntakeScale";
        private static readonly string ModTag = "[ProceduralParts.ModuleIntakeScale]";

        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Intake width", guiFormat = "F2", groupName = GRP, groupDisplayName = "Intake Scale"),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 6f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = 0.01f, sigFigs = 2)]
        public float scaleWidth = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Intake height", guiFormat = "F2", groupName = GRP),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 6f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = 0.01f, sigFigs = 2)]
        public float scaleHeight = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Intake length", guiFormat = "F2", groupName = GRP),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, maxValue = 6f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = 0.01f, sigFigs = 2)]
        public float scaleLength = 1f;

        // Which model-local axis is length/airflow (x|y|z). The other two are width & height (frontal area).
        [KSPField] public string lengthAxis = "y";
        // mass ~ (w*h*l)^(massExponent/3); default 3 => mass proportional to volume. cost likewise.
        [KSPField] public float massExponent = 3f;
        [KSPField] public float costExponent = 2.5f;

        #endregion

        private Vector3 _baseModelScale = Vector3.one;
        private Vector3[] _baseNodePos;
        private float _baseMass, _baseCost, _baseAjeArea = -1f;
        private ModuleResourceIntake _intake;
        private FieldInfo _ajeAreaField;
        private bool _ajeResolved;

        // width/height/length -> model-local axis indices (length from lengthAxis; the remaining two are w,h).
        private int _lenIdx, _wIdx, _hIdx;

        #region Initialization

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            ResolveAxes();
            CaptureBase();

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(scaleWidth)].uiControlEditor.onFieldChanged = OnScaleChanged;
                Fields[nameof(scaleHeight)].uiControlEditor.onFieldChanged = OnScaleChanged;
                Fields[nameof(scaleLength)].uiControlEditor.onFieldChanged = OnScaleChanged;
            }
            ApplyScale();
        }

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);
            ApplyScale();   // after all modules started (AJEInlet, drag cubes) -> authoritative
        }

        #endregion

        #region Scaling

        private void ResolveAxes()
        {
            _lenIdx = AxisIndex(lengthAxis);
            // width = first non-length axis, height = second (arbitrary but consistent; area = w*h either way).
            _wIdx = -1; _hIdx = -1;
            for (int i = 0; i < 3; i++)
            {
                if (i == _lenIdx) continue;
                if (_wIdx < 0) _wIdx = i; else _hIdx = i;
            }
        }

        private static int AxisIndex(string a) =>
            string.IsNullOrEmpty(a) ? 1 : (a.Trim().ToLower() == "x" ? 0 : a.Trim().ToLower() == "z" ? 2 : 1);

        private Transform ModelRoot => part.transform.Find("model");

        private void CaptureBase()
        {
            // Base geometry/mass/cost from the prefab so repeated loads don't compound.
            Part prefab = part.partInfo?.partPrefab;
            Transform pm = prefab != null ? prefab.transform.Find("model") : null;
            _baseModelScale = pm != null ? pm.localScale : (ModelRoot != null ? ModelRoot.localScale : Vector3.one);
            _baseMass = prefab != null ? prefab.mass : part.mass;
            _baseCost = (prefab != null && prefab.partInfo != null) ? prefab.partInfo.cost : 0f;

            _baseNodePos = new Vector3[part.attachNodes.Count];
            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                AttachNode pn = (prefab != null) ? prefab.attachNodes.Find(n => n.id == part.attachNodes[i].id) : null;
                _baseNodePos[i] = (pn != null) ? pn.originalPosition : part.attachNodes[i].originalPosition;
            }

            if (_baseAjeArea < 0f)
            {
                // Read the base Area from the PREFAB's intake (like the other base captures above), not the
                // live instance: a persisted / already-scaled Area on the instance would otherwise be taken
                // as the base and compound on every save-reload.
                ModuleResourceIntake pmri = (prefab != null) ? prefab.FindModuleImplementing<ModuleResourceIntake>() : Intake;
                if (pmri != null) _baseAjeArea = ReadAjeArea(pmri);
            }
        }

        private ModuleResourceIntake Intake => _intake != null ? _intake : (_intake = part.FindModuleImplementing<ModuleResourceIntake>());

        private void OnScaleChanged(BaseField f, object obj)
        {
            ApplyScale();
            foreach (Part p in part.symmetryCounterparts)
                if (p.FindModuleImplementing<ModuleIntakeScale>() is ModuleIntakeScale m)
                {
                    m.scaleWidth = scaleWidth; m.scaleHeight = scaleHeight; m.scaleLength = scaleLength;
                    m.ApplyScale();
                }
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        private void ApplyScale()
        {
            // 1. Model + colliders: per-axis localScale on the model root.
            if (ModelRoot is Transform model)
            {
                Vector3 ls = _baseModelScale;
                ls[_wIdx] = _baseModelScale[_wIdx] * scaleWidth;
                ls[_hIdx] = _baseModelScale[_hIdx] * scaleHeight;
                ls[_lenIdx] = _baseModelScale[_lenIdx] * scaleLength;
                model.localScale = ls;
            }

            // 2. Attach nodes: scale each component by its axis factor.
            for (int i = 0; i < part.attachNodes.Count && i < _baseNodePos.Length; i++)
            {
                Vector3 bp = _baseNodePos[i];
                Vector3 np = bp;
                np[_wIdx] = bp[_wIdx] * scaleWidth;
                np[_hIdx] = bp[_hIdx] * scaleHeight;
                np[_lenIdx] = bp[_lenIdx] * scaleLength;
                // Only the live position; leave originalPosition at the design-time base (KSP uses it for
                // symmetry / node reset). We re-apply from _baseNodePos on every rebuild, so it persists.
                part.attachNodes[i].position = np;
            }
            if (part.srfAttachNode != null && _baseNodePos.Length == 0) { /* srf-only parts: nothing to move */ }

            // 3. AJE frontal area scales with width x height.
            if (Intake is ModuleResourceIntake mri && _baseAjeArea > 0f)
                WriteAjeArea(mri, _baseAjeArea * scaleWidth * scaleHeight);

            // 4. Drag cubes regen from the now-scaled mesh (also feeds FAR voxelization via the events).
            if (part.DragCubes != null)
                part.DragCubes.ForceUpdate(true, true, true);
        }

        #endregion

        #region AJE Area reflection (AJEInlet.Area)

        private FieldInfo AjeAreaField(ModuleResourceIntake mri)
        {
            if (!_ajeResolved)
            {
                _ajeResolved = true;
                _ajeAreaField = mri.GetType().GetField("Area", BindingFlags.Public | BindingFlags.Instance);
                if (_ajeAreaField != null && _ajeAreaField.FieldType != typeof(float)) _ajeAreaField = null;
            }
            return _ajeAreaField;
        }

        private float ReadAjeArea(ModuleResourceIntake mri)
        {
            FieldInfo fi = AjeAreaField(mri);
            try { return fi != null ? (float)fi.GetValue(mri) : -1f; }
            catch { return -1f; }
        }

        private void WriteAjeArea(ModuleResourceIntake mri, float area)
        {
            FieldInfo fi = AjeAreaField(mri);
            if (fi == null) return;
            try { fi.SetValue(mri, area); }
            catch (Exception e) { Debug.LogError($"{ModTag} set AJEInlet.Area failed: {e.Message}"); }
        }

        #endregion

        #region Mass / cost modifiers

        private float VolumeFactor => scaleWidth * scaleHeight * scaleLength;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) =>
            _baseMass * (Mathf.Pow(Mathf.Max(1e-3f, VolumeFactor), massExponent / 3f) - 1f);
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) =>
            _baseCost * (Mathf.Pow(Mathf.Max(1e-3f, VolumeFactor), costExponent / 3f) - 1f);
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion
    }
}
