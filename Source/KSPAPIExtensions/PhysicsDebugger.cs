using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSPAPIExtensions
{

    public class PhysicsDebugger : PartModule
    {

        private double lastFixedUpdate;

        public override void OnFixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && (Time.time - lastFixedUpdate) > 10)
            {
                lastFixedUpdate = Time.time;

                Transform rootT = part.vessel.rootPart.transform;

                StringBuilder sb = new StringBuilder();

                float massTotal = 0;
                Vector3 wtSum = Vector3.zero;

                foreach (Part p in part.vessel.parts)
                {
                    sb.AppendLine(p.name + " position=" + rootT.InverseTransformPoint(p.transform.position).ToString("F5"));

                    if (p.rigidbody != null)
                    {
                        massTotal += p.rigidbody.mass;
                        wtSum += p.rigidbody.mass * rootT.InverseTransformPoint(p.transform.TransformPoint(p.rigidbody.centerOfMass));
                        if (p.rigidbody.centerOfMass != Vector3.zero)
                            sb.AppendLine(p.name + " CoM offset=" + rootT.InverseTransformDirection(p.transform.TransformDirection(p.rigidbody.centerOfMass)).ToString("F5"));
                        sb.AppendLine(p.name + " inertia tensor=" + p.rigidbody.inertiaTensor.ToString("F5") + " rotation=" + p.rigidbody.inertiaTensorRotation.ToStringAngleAxis("F5"));

                        Joint j = p.gameObject.GetComponent<Joint>();
                        if (j != null)
                        {
                            sb.AppendLine(p.name + " joint  type=" + j.GetType() + "position=" + rootT.InverseTransformDirection(p.transform.TransformDirection(j.anchor)).ToString("F5") + " force=" + j.breakForce + " torque=" + j.breakTorque);
                        }
                    }

                    if (p.Modules.Contains("ModuleEngines"))
                    {
                        ModuleEngines mE = (ModuleEngines)p.Modules["ModuleEngines"];
                        foreach (Transform t in mE.thrustTransforms)
                        {
                            sb.AppendLine(p.name + " thrust transform position=" + rootT.InverseTransformPoint(t.position).ToString("F5"));
                        }
                    }
                }
                if (massTotal > 0)
                {
                    sb.AppendLine("CoM = " + (wtSum / massTotal).ToString("F5"));
                }
                Debug.Log(sb);
            }
        }

    }
}