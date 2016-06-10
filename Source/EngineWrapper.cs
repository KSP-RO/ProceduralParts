using System;
using System.Collections.Generic;

namespace KSPAPIExtensions.Utils
{
    // ReSharper disable InconsistentNaming
    public class EngineWrapper
    {
        public enum ModuleType
        {
            MODULEENGINES,
            MODULEENGINESFX,
            MODULERCS
        }

        private readonly ModuleType type;
        private readonly ModuleEngines mE;
        private readonly ModuleEnginesFX mEFX;

        public EngineWrapper(Part part)
        {
            if ((mEFX = part.transform.GetComponent<ModuleEnginesFX>()) != null)
                type = ModuleType.MODULEENGINESFX;
            else if ((mE = part.transform.GetComponent<ModuleEngines>()) != null)
                type = ModuleType.MODULEENGINES;
            else
                throw new ArgumentException("Unable to find engine-like module");
        }

        public EngineWrapper(ModuleEngines mod)
        {
            mE = mod;
            type = ModuleType.MODULEENGINES;
        }

        public EngineWrapper(ModuleEnginesFX mod)
        {
            mEFX = mod;
            type = ModuleType.MODULEENGINESFX;
        }

        public static implicit operator PartModule(EngineWrapper wrapper)
        {
            return (PartModule)wrapper.mE ?? wrapper.mEFX;
        }

        public static explicit operator ModuleEngines(EngineWrapper wrapper)
        {
            return wrapper.mE;
        }

        public static explicit operator ModuleEnginesFX(EngineWrapper wrapper)
        {
            return wrapper.mEFX;
        }

        public ModuleType Type { get { return type; } }

        // ReSharper disable once InconsistentNaming
        public List<Propellant> propellants
        {
            get
            {
                switch(type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.propellants;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.propellants;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public void SetupPropellant()
        {
            switch (type)
            {
                case ModuleType.MODULEENGINES:
                    mE.SetupPropellant();
                    break;
                case ModuleType.MODULEENGINESFX:
                    mEFX.SetupPropellant();
                    break;
                default:
                    throw new InvalidProgramException();
            }
        }
        public BaseActionList Actions
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.Actions;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.Actions;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public bool getIgnitionState
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.getIgnitionState;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.getIgnitionState;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public bool EngineIgnited
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.EngineIgnited;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.EngineIgnited;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.EngineIgnited = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.EngineIgnited = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public FloatCurve atmosphereCurve
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.atmosphereCurve;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.atmosphereCurve;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.atmosphereCurve = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.atmosphereCurve = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public FloatCurve velCurve
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.velCurve;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.velCurve;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.velCurve = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.velCurve = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public bool useVelCurve
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.useVelCurve;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.useVelCurve;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.useVelCurve = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.useVelCurve = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public float maxThrust
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.maxThrust;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.maxThrust;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.maxThrust = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.maxThrust = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public float minThrust
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.minThrust;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.minThrust;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.minThrust = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.minThrust = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
        public float heatProduction
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.heatProduction;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.heatProduction;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.heatProduction = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.heatProduction = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }

        public float g
        {
            get
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        return mE.g;
                    case ModuleType.MODULEENGINESFX:
                        return mEFX.g;
                    default:
                        throw new InvalidProgramException();
                }
            }
            set
            {
                switch (type)
                {
                    case ModuleType.MODULEENGINES:
                        mE.g = value;
                        break;
                    case ModuleType.MODULEENGINESFX:
                        mEFX.g = value;
                        break;
                    default:
                        throw new InvalidProgramException();
                }
            }
        }
    }
    // ReSharper restore InconsistentNaming
}
