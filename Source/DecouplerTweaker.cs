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
        [KSPField]
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

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Impulse", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.1f, maxValue = float.PositiveInfinity, incrementLarge = 10f, incrementSmall = 0, incrementSlide = 0.1f, unit = " kN", sigFigs = 1)]
        public float ejectionImpulse = 0;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName="Mass", guiUnits="T", guiFormat="F3", groupName = ProceduralPart.PAWGroupName)]
        public float mass = 0;

        private ModuleDecouple decouple;
        internal const float ImpulsePerForceUnit = 0.02f;

        public override void OnStart(StartState state)
        {
            decouple = part.FindModuleImplementing<ModuleDecouple>();
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

                if (ejectionImpulse == 0 || float.IsNaN(ejectionImpulse))
                    ejectionImpulse = Mathf.Round(decouple.ejectionForce * ImpulsePerForceUnit);

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
                minDia = Mathf.Max(minDia, 0.001f); // Disallow values too small
                maxImpulse = Mathf.Round(maxImpulseDiameterRatio * minDia);
                float oldRatio = ejectionImpulse / ejectionImpulseEdit.maxValue;
                ejectionImpulseEdit.maxValue = maxImpulse;
                ejectionImpulse = Convert.ToSingle(Math.Round(maxImpulse * oldRatio, 1));
            }
        }

        [KSPEvent(active = true)]
        public void OnPartVolumeChanged(BaseEventDetails data)
        {
            if (HighLogic.LoadedSceneIsEditor && density > 0 && data.Get<double>("newTotalVolume") is double volume)
            {
                mass = Convert.ToSingle(density * volume);
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }
    }
}