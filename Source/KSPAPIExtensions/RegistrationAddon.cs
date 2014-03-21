using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;

namespace KSPAPIExtensions
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    class RegistrationAddon : MonoBehaviour
    {
        public void Start()
        {
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                return;

            if (UIPartActionController.Instance == null)
            {
                print("Controller instance is null");
                return;
            }

            if (UIPartActionController.Instance.fieldPrefabs.Find(item => item.GetType() == typeof(UIPartActionFloatEdit)) != null)
                return;

            // Register prefabs.
            UIPartActionController.Instance.fieldPrefabs.Add(UIPartActionFloatEdit.CreateTemplate());
            UIPartActionController.Instance.fieldPrefabs.Add(UIPartActionChooseOption.CreateTemplate());

            if (UIPartActionController.Instance.GetFieldControl(typeof(UI_FloatEdit)) == null)
            {
                print("Unable to find field prefab, will reinitialize. Ignore error below... ");
                MethodInfo method = typeof(UIPartActionController).GetMethod("SetupItemControls", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                    print("Can't find method");
                else
                    method.Invoke(UIPartActionController.Instance, new object[] { });
            }
            print("...done");
        }


    }
}