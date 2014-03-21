using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;

namespace KSPAPIExtensions
{
    /// <summary>
    /// This utility class replaces the clunky and rather broken KSPEvent system for passing messages, 
    /// plus the also overly broad SendMessage method in Unity. It allows you to send messages to 
    /// the Part class itself, plus PartModules attached to the part. It can optionally pass the message 
    /// on to children of the part, or to the whole ship.
    /// </summary>
    public static class PartMessageUtils
    {
        /// <summary>
        /// Invoke a method on the part and all enabled modules attached to the part. This is similar in scope to
        /// the SendMessage method on GameObject, only you can send it just to the part and its modules, rather
        /// than all the children. To recieve a message, just declare a method with the PartMessageListener attribute
        /// with any access level who's parameters are compatible with what is passed.
        /// </summary>
        /// <param name="part">the part</param>
        /// <param name="messageName">Name of the method to invoke</param>
        /// <param name="args">parameters</param>
        /// <returns>True if at least one reciever recieved the message</returns>
        public static bool SendPartMessage(this Part part, string messageName, params object[] args)
        {
            return SendPartMessage(part, PartMessageScope.Default, messageName, args);
        }

        /// <summary>
        /// Invoke a method on the part and all enabled modules attached to the part. This is similar in scope to
        /// the SendMessage method on GameObject, only you can send it just to the part and its modules, rather
        /// than all the children. To recieve a message, just declare a method with the PartMessageListener attribute
        /// with any access level who's parameters are compatible with what is passed.
        /// </summary>
        /// <param name="part">the part</param>
        /// <param name="messageName">Name of the method to invoke</param>
        /// <param name="scope">Allow a scope for the message - can send up and down the heirachy.</param>
        /// <param name="args">parameters</param>
        /// <returns>True if at least one reciever recieved the message</returns>
        public static bool SendPartMessage(this Part part, PartMessageScope scope, string messageName, params object[] args)
        {
            bool success = false;

            if ((scope & PartMessageScope.Ship) == PartMessageScope.Ship)
            {
                PartMessageScope scopeCall = (scope & (PartMessageScope.IgnoreAttribute)) | PartMessageScope.Self | PartMessageScope.Decendents;
                return SendPartMessage(part.localRoot, scopeCall, messageName, args);
            }

            if ((scope & PartMessageScope.Self) == PartMessageScope.Self)
            {
                bool ignoreAttribute = (scope & PartMessageScope.IgnoreAttribute) == PartMessageScope.IgnoreAttribute;
                success = SendPartMessageToModules(part, messageName, ignoreAttribute, args) || success;
            }

            if ((scope & PartMessageScope.Symmetry) == PartMessageScope.Symmetry)
            {
                bool ignoreAttribute = (scope & PartMessageScope.IgnoreAttribute) == PartMessageScope.IgnoreAttribute;
                foreach (Part p in part.symmetryCounterparts)
                    success = SendPartMessageToModules(p, messageName, ignoreAttribute, args) || success;
            }

            if (part.parent != null && (scope & PartMessageScope.Parent) == PartMessageScope.Parent)
            {
                PartMessageScope scopeCall = (scope & (PartMessageScope.IgnoreAttribute | PartMessageScope.Symmetry)) | PartMessageScope.Self;
                if((scope & PartMessageScope.Ancestors) == PartMessageScope.Ancestors)
                    scopeCall = scopeCall | PartMessageScope.Ancestors;

                success = SendPartMessage(part.parent, scopeCall, messageName, args) || success;
            }

            if ((scope & PartMessageScope.Children) == PartMessageScope.Children)
            {
                PartMessageScope scopeCall = (scope & (PartMessageScope.IgnoreAttribute | PartMessageScope.Symmetry)) | PartMessageScope.Self;
                if ((scope & PartMessageScope.Decendents) == PartMessageScope.Decendents)
                    scopeCall = scopeCall | PartMessageScope.Decendents;

                foreach (Part p in part.children)
                    success = SendPartMessage(p, scopeCall, messageName, args) || success;
            }

            return success;
        }

        private static bool SendPartMessageToModules(Part part, string message, bool ignoreAttribute, object[] args)
        {
            bool success = false;
            if (part.enabled)
                success = SendPartMessageToTarget(part, message, ignoreAttribute, args) || success;
            foreach (PartModule module in part.Modules)
                if (module.enabled && module.isEnabled)
                    success = SendPartMessageToTarget(module, message, ignoreAttribute, args) || success;
            return success;
        }

        private static bool SendPartMessageToTarget(object target, string message, bool ignoreAttribute, object[] args)
        {
            bool success = false;
            Type t = target.GetType();
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == message)
                {
                    if (!ignoreAttribute &&  m.GetCustomAttributes(typeof(PartMessageListener), true).Length == 0)
                        continue;

                    // Just invoke it and deal with the consequences, rather than stuff around trying to match parameters
                    // MethodInfo does all the parameter checking anyhow.
                    try
                    {
                        m.Invoke(target, args);
                        success = true;
                    }
                    catch (ArgumentException) { }
                    catch (TargetParameterCountException) { }
                }
            return success;
        }
    }

    /// <summary>
    /// Apply this attribute to any method you wish to recieve messages.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple=true, Inherited=true)]
    public class PartMessageListener : Attribute { }

    /// <summary>
    /// 
    /// </summary>
    [Flags]
    public enum PartMessageScope
    {
        Default = 0x1,
        Self = 0x1,
        Symmetry = 0x2,
        Children = 0x10,
        Decendents = 0x30,
        Parent = 0x100,
        Ancestors = 0x300,
        Ship = 0xFFFF,

        IgnoreAttribute = 0x10000,
    }
}
