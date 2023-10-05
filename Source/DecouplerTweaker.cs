using System;
using UnityEngine;

namespace ProceduralParts
{

    /// <summary>
    /// Module to allow tweaking of decouplers in the VAB.
    /// 
    /// This fairly flexible module allows tech dependent tweaking of the type and size of modules.
    /// There are options for it to target just one specific module, or all the modules on a part.
    /// </summary>
    public class DecouplerTweaker : PartModule, IPartMassModifier
    {
        #region IPartMassModifier implementation

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => density > 0 ? mass - defaultMass : 0;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        #endregion

        /// <summary>
        /// In career mode, if this tech is not available then the option to have separators is not present
        /// </summary>
        [KSPField]
        public string separatorTechRequired;

        /// <summary>
        /// Maximum ejection impulse available.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float maxImpulse = float.PositiveInfinity;

        /// <summary>
        /// Listen for a specific texture message, again for use with proceedural parts. If set, then listens for ChangeAttachNodeSize message.
        /// </summary>
        [KSPField]
        public string textureMessageName;

        /// <summary>
        /// Density of the decoupler. This is for use with Procedural Parts. Listens for ChangeVolume message
        /// </summary>
        [KSPField]
        public float density = 0;

        /// <summary>
        /// If specified, this will set a maximum impulse based on the diameter of the node.
        /// </summary>
        [KSPField]
        public float maxImpulseDiameterRatio = 0;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Style:", groupName = ProceduralPart.PAWGroupName),
         UI_Toggle(disabledText = "Decoupler", enabledText = "Separator")]
        public bool isOmniDecoupler = false;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Impulse", guiUnits = " kN*s", guiFormat = "F1", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.1f, maxValue = float.PositiveInfinity, incrementLarge = 10f, incrementSmall = 1, incrementSlide = 0.1f, unit = " kN")]
        public float ejectionImpulse = 0.1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName="Mass", guiUnits="T", guiFormat="F3", groupName = ProceduralPart.PAWGroupName)]
        public float mass = 0;

        [KSPField]
        public bool usePFStyleMass = false;
        [KSPField]
        public Vector4 specificMass = new Vector4(0.0002f, 0.0075f, 0.005f, 0f);
        [KSPField]
        public float decouplerMassMult = 4f;

        private ModuleDecouple decouple;
        internal const float ImpulsePerForceUnit = 0.02f;
        private ProceduralPart procPart;

        public override void OnStart(StartState state)
        {
            decouple = part.FindModuleImplementing<ModuleDecouple>();
            procPart = part.FindModuleImplementing<ProceduralPart>();
            if (decouple == null)
            {
                Debug.LogError($"[ProceduralParts] No ModuleDecouple found on {part}");
                isEnabled = enabled = false;
                return;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(isOmniDecoupler)].guiActiveEditor =
                    string.IsNullOrEmpty(separatorTechRequired) || ResearchAndDevelopment.GetTechnologyState(separatorTechRequired) == RDTech.State.Available;

                if (ejectionImpulse < float.Epsilon || float.IsNaN(ejectionImpulse))
                    ejectionImpulse = decouple.ejectionForce * ImpulsePerForceUnit;

                (Fields[nameof(ejectionImpulse)].uiControlEditor as UI_FloatEdit).maxValue = maxImpulse;
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                if (float.IsNaN(ejectionImpulse)) ejectionImpulse = 0;
                decouple.isOmniDecoupler = isOmniDecoupler;
                decouple.ejectionForce = ejectionImpulse / TimeWarp.fixedDeltaTime;
                GameEvents.onTimeWarpRateChanged.Add(OnTimeWarpRateChanged);
            }
        }

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);
            if (HighLogic.LoadedSceneIsEditor && mass == 0f && density > 0)
            {
                if (procPart != null)
                    UpdateMass(procPart.Volume);
            }
        }

        public void OnDestroy() => GameEvents.onTimeWarpRateChanged.Remove(OnTimeWarpRateChanged);

        private bool updatingTimeWarp = false;
        public void OnTimeWarpRateChanged() => StartCoroutine(TimeWarpRateChangedCR());
        private System.Collections.IEnumerator TimeWarpRateChangedCR()
        {
            if (!updatingTimeWarp)
            {
                float prevWarp = 0;
                updatingTimeWarp = true;
                while (HighLogic.LoadedSceneIsFlight && TimeWarp.fixedDeltaTime != prevWarp && decouple)
                {
                    decouple.ejectionForce = ejectionImpulse / TimeWarp.fixedDeltaTime;
                    prevWarp = TimeWarp.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }
                updatingTimeWarp = false;
            }
        }

        // Plugs into procedural parts.
        //public void ChangeAttachNodeSize(AttachNode node, float minDia, float area)
        [KSPEvent(guiActive = false, active = true)]
        public void OnPartAttachNodeSizeChanged(BaseEventDetails data)
        {
            if (HighLogic.LoadedSceneIsEditor &&
                data.Get<AttachNode>("node") is AttachNode node &&
                data.Get<float>("minDia") is float minDia &&
                node.id == textureMessageName &&
                maxImpulseDiameterRatio >= float.Epsilon &&
                Fields[nameof(ejectionImpulse)].uiControlEditor is UI_FloatEdit ejectionImpulseEdit)
            {
                maxImpulse = CalcMaxImpulse(minDia, ejectionImpulseEdit.minValue);
                float oldMax = float.IsPositiveInfinity(ejectionImpulseEdit.maxValue) ? maxImpulse : ejectionImpulseEdit.maxValue;
                float ratio = Mathf.Clamp01(ejectionImpulse / oldMax);
                ejectionImpulse = maxImpulse * ratio;
                ejectionImpulseEdit.maxValue = maxImpulse;
            }
        }

        private float CalcMaxImpulse(float diam, float min)
        {
            diam = Mathf.Max(diam, 0.001f); // Disallow values too small
            return Mathf.Max(maxImpulseDiameterRatio * diam, min);
        }

        [KSPEvent(active = true)]
        public void OnPartVolumeChanged(BaseEventDetails data)
        {
            if (HighLogic.LoadedSceneIsEditor && density > 0 && data.Get<double>("newTotalVolume") is double volume)
            {
                UpdateMass(volume);
            }
        }

        private void UpdateMass(double volume)
        {
            if (usePFStyleMass && procPart != null)
            {
                float diam = procPart.Diameter;
                float baseMass = (((((specificMass.x * diam) + specificMass.y) * diam) + specificMass.z) * diam) + specificMass.w;
                mass = baseMass * decouplerMassMult;
                //mass *= Mathf.Lerp(0.9f, 1.1f, Mathf.InverseLerp(0.1f, 0.3f, procPart.Length / diam));
            }
            else
            {
                mass = Convert.ToSingle(density * volume);
            }
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }
    }
}