using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralTankSRB : PartModule
{
    #region callbacks
    public override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            if (part.Modules.Contains("ModuleEngineConfigs"))
                changeThrust();
        }
        catch (Exception ex)
        {
            print("OnInitialize exception: " + ex);
            throw ex;
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        try
        {
            if (part.Modules.Contains("ModuleEngineConfigs"))
                changeThrust();
        }
        catch (Exception ex)
        {
            print("OnLoad exception: " + ex);
            throw ex;
        }
    }

    public override void OnStart(PartModule.StartState state)
    {
        base.OnStart(state);
        InitializeSRB();
    }

    public void Update()
    {
        UpdateSRB();
    }

    #endregion

    #region SRB stuff

    [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "SRB Burn Time", guiFormat = "F2"), UI_FloatRange(minValue = 0.25f, maxValue = 360f, stepIncrement = 0.25f)]
    public float srbBurnTime = 60f;
    private float oldSRBBurnTime; // NK for SRBs

    [KSPField(isPersistant = false, guiName = "SRB Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "F2")]
    public float srbThrust;

    [KSPField(isPersistant = false, guiName = "SRB Heat", guiActive = false, guiActiveEditor = true, guiFormat = "F2")]
    public float srbHeatProduction;

    [KSPField]
    public string srbBellName = null;

    public Transform srbBell
    {
        get { return _srbBell; }
    }
    private Transform _srbBell;

    private void InitializeSRB()
    {
        _srbBell = part.FindModelTransform(srbBellName);
        if (srbBell == null)
            print("*ST* Unable to find SRB Bell");

        /*
            // When the object is deserialized, the nodes will be already in position, not in standard position.
            nodeAttachments.Add(shape.AddTankAttachment(TransformFollower.createFollower(tankModel, _srbBell), !deserialized));
         */
    }

    private void UpdateSRB()
    {
        if (oldSRBBurnTime == srbBurnTime)
            return;
        changeThrust();

        oldSRBBurnTime = srbBurnTime;
    }


    private void changeThrust()
    {
        try
        {
            ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
            PartResource solidFuel = part.Resources["SolidFuel"];
            srbThrust = mE.maxThrust = (float)Math.Round(mE.atmosphereCurve.Evaluate(0) * solidFuel.maxAmount * solidFuel.info.density * 9.81f / srbBurnTime, 2);
            srbHeatProduction = mE.heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((srbBurnTime + 20f), 0.75f)) * 0.5f);

            if (part.Modules.Contains("ModuleEngineConfigs"))
            {

                var mEC = part.Modules["ModuleEngineConfigs"];
                if (mEC != null)
                {
                    Type engineType = mEC.GetType();
                    engineType.GetMethod("ChangeThrust").Invoke(mEC, new object[] { srbThrust });
                }
            }
        }
        catch (Exception e)
        {
            print("*ST* ChangeThrust, caught " + e.Message);
        }
    }
    #endregion

}
