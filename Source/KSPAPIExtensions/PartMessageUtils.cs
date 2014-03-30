using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using System.Linq.Expressions;
using System.Collections;

namespace KSPAPIExtensions.PartMessage
{
    /// <summary>
    /// Apply this attribute to any method you wish to recieve messages. 
    /// 
    /// The access modifier is important - public events can call other Assemblies (other mods) and should use a public
    /// delegate and will pass messages to public listener methods. 
    /// Internal messages are internal to a particular Assembly, will pass messages to internal listeners in the
    /// same assembly, and should use internal delegates.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class PartMessageListener : Attribute
    {
        public PartMessageListener(Type message, GameSceneFilter scenes = GameSceneFilter.All)
        {
            if (message == null || !message.IsSubclassOf(typeof(Delegate)))
                throw new ArgumentException("Message is not a delegate type");
            if (message.GetCustomAttributes(typeof(PartMessageEvent), true).Length == 0)
                throw new ArgumentException("Message does not have the PartMessageEvent attribute");

            this.message = message;
            this.scenes = scenes;
        }

        /// <summary>
        /// The delegate type that we are listening for.
        /// </summary>
        public readonly Type message;
        /// <summary>
        /// Scene to listen for message in. Defaults to All.
        /// </summary>
        public readonly GameSceneFilter scenes;
    }

    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Delegate)]
    public class PartMessageEvent : Attribute
    {
        public PartMessageEvent(Type parent = null)
        {
            if (parent != null)
            {
                if (!parent.IsSubclassOf(typeof(Delegate)))
                    throw new ArgumentException("Parent is not a delegate type");
                if (parent.GetCustomAttributes(typeof(PartMessageEvent), true).Length != 1)
                    throw new ArgumentException("Parent does not have the PartMessageEvent attribute");
            }
            this.parent = parent;
        }

        readonly public Type parent;
    }


    /// <summary>
    /// PartMessageListeners can use the properties in this class to examine the source of the message
    /// </summary>
    public class PartMessageSourceInfo
    {
        internal PartMessageSourceInfo() { }

        public object source
        {
            get
            {
                return curr.Peek().source;
            }
        }

        public EventInfo srcEvent
        {
            get
            {
                return curr.Peek().evt;
            }
        }

        public Part srcPart
        {
            get
            {
                object src = source;
                if (src is Part)
                    return (Part)src;
                if (src is PartModule)
                    return ((PartModule)src).part;
                return null;
            }
        }

        public PartModule srcModule
        {
            get { return source as PartModule; }
        }

        public Type srcMessage
        {
            get { return srcEvent.EventHandlerType; }
        }

        public IEnumerable<Type> srcAllMessages
        {
            get
            {
                return curr.Peek();
            }
        }

        public bool isSourcePartRelation(Part listener, PartRelationship relation)
        {
            Part src = srcPart;
            if (src == null)
                return false;

            if (TestFlag(relation, PartRelationship.Vessel))
                return src.localRoot == listener.localRoot;

            if (TestFlag(relation, PartRelationship.Self) && src == listener)
                return true;

            if (TestFlag(relation, PartRelationship.Ancestor))
            {
                for (Part upto = listener.parent; upto != null; upto = upto.parent)
                    if (upto == src)
                        return true;
            }
            else if (TestFlag(relation, PartRelationship.Parent) && src == listener.parent)
                return true;

            if (TestFlag(relation, PartRelationship.Decendent))
            {
                for (Part upto = src.parent; upto != null; upto = upto.parent)
                    if (upto == listener)
                        return true;
            }
            else if (TestFlag(relation, PartRelationship.Child) && src.parent == listener)
                return true;

            if (TestFlag(relation, PartRelationship.Symmetry))
                foreach (Part sym in listener.symmetryCounterparts)
                    if (src == sym)
                        return true;

            return false;
        }

        public bool isSourceSamePart(Part listener)
        {
            return srcPart == listener;
        }

        public bool isSourceSameVessel(Part listener)
        {
            return srcPart.localRoot == listener.localRoot;
        }

        #region Internal Bits
        private Stack<Info> curr = new Stack<Info>();

        internal IDisposable Push(object source, EventInfo evt)
        {
            return new Info(this, source, evt);
        }

        private class Info : IDisposable, IEnumerable<Type>
        {
            internal Info(PartMessageSourceInfo info, object source, EventInfo evt)
            {
                this.source = source;
                this.evt = evt;
                this.info = info;
                info.curr.Push(this);
            }

            internal PartMessageSourceInfo info;
            internal object source;
            internal EventInfo evt;

            void IDisposable.Dispose()
            {
                info.curr.Pop();
            }

            IEnumerator<Type> IEnumerable<Type>.GetEnumerator()
            {
                return new MessageEnumerator(evt.EventHandlerType);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new MessageEnumerator(evt.EventHandlerType);
            }
        }

        private static bool TestFlag(PartRelationship e, PartRelationship flags)
        {
            return (e & flags) == flags;
        }

        private class MessageEnumerator : IEnumerator<Type>
        {

            public MessageEnumerator(Type top)
            {
                this.current = this.top = top;
            }
            private int pos = -1;
            private Type current;
            private Type top;

            object IEnumerator.Current
            {
                get {
                    if (pos != 0)
                        throw new InvalidOperationException();
                    return current; 
                }
            }

            Type IEnumerator<Type>.Current
            {
                get
                {
                    if (pos != 0)
                        throw new InvalidOperationException();
                    return current;
                }
            }

            bool IEnumerator.MoveNext()
            {
                switch (pos)
                {
                    case -1:
                        current = top;
                        pos = 0;
                        break;
                    case 1:
                        return false;
                    case 0:
                        PartMessageEvent evt = (PartMessageEvent)current.GetCustomAttributes(typeof(PartMessageEvent), true)[0];
                        current = evt.parent;
                        break;
                    case 2:
                        throw new InvalidOperationException("Enumerator disposed");
                }
                if (current == null)
                {
                    pos = 1;
                    return false;
                }
                return true;
            }

            void IEnumerator.Reset()
            {
                pos = -1;
                current = null;
            }

            void IDisposable.Dispose() {
                current = top = null;
                pos = 2;
            }
        }

        #endregion
    }

    // Delegates for some standard events

    /// <summary>
    /// Listen for this to get notification when any physical constant is changed
    /// including the mass, CoM, moments of inertia, boyancy, ect.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartPhysicsChanged();

    /// <summary>
    /// Message for when the part's mass is modified.
    /// </summary>
    [PartMessageEvent(typeof(PartPhysicsChanged))]
    public delegate void PartMassChanged();

    /// <summary>
    /// Message for when the part's CoMOffset changes.
    /// </summary>
    [PartMessageEvent(typeof(PartPhysicsChanged))]
    public delegate void PartCoMOffsetChanged();

    /// <summary>
    /// Message for when the part's moments of intertia change.
    /// </summary>
    [PartMessageEvent(typeof(PartPhysicsChanged))]
    public delegate void PartMomentsChanged();


    /// <summary>
    /// Message for when the part's mass is modified.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartResourcesChanged();

    /// <summary>
    /// Message for when the part's resource list is modified (added to or subtracted from).
    /// </summary>
    [PartMessageEvent(typeof(PartResourcesChanged))]
    public delegate void PartResourceListChanged();

    /// <summary>
    /// Message for when the max amount of a resource is modified.
    /// </summary>
    [PartMessageEvent(typeof(PartResourcesChanged))]
    public delegate void PartResourceMaxAmountChanged(PartResource resource);

    /// <summary>
    /// Message for when some change has been made to the part's rendering model.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartModelChanged();

    /// <summary>
    /// Message for when some change has been made to the part's collider.
    /// </summary>
    [PartMessageEvent]
    public delegate void PartColliderChanged();

    #region Implementation
    internal class ListenerInfo
    {
        public WeakReference targetRef;
        public MethodInfo method;

        public LinkedListNode<ListenerInfo> node;

        public object target
        {
            get
            {
                return targetRef.Target;
            }
        }

        public Part part
        {
            get
            {
                object target = this.target;
                if (target is PartModule)
                    return ((PartModule)target).part;
                return target as Part;
            }
        }

        public PartModule module
        {
            get
            {
                return target as PartModule;
            }
        }

        public void Invoke(object[] args)
        {
            object target = this.target;
            if (target == null)
                return;

            PartModule module = target as PartModule;
            if (module != null && !(module.isEnabled && module.enabled))
                return;

            method.Invoke(target, args);
        }
    }

    public class PartMessageManager : PartModule
    {
        public override string GetInfo()
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING)
                return "";
            Debug.LogWarning("Scanning part: " + part.name + " in scene " + HighLogic.LoadedScene);
            try
            {
                PartMessageService.Instance.ScanPartInternal(part, this);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            return "";
        }

        public override void OnInitialize()
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            Debug.LogWarning("Scanning part in OnInitialize: " + part.name + " in scene " + HighLogic.LoadedScene);
            PartMessageService.Instance.ScanPartInternal(part, this);
            //part.Modules.Remove(this);
        }

        public override void OnStart(PartModule.StartState state)
        {
            if (!HighLogic.LoadedSceneIsEditor || modulesScanned.Count > 0)
                return;
            Debug.LogWarning("Scanning part in OnStart: " + part.name + " in scene " + HighLogic.LoadedScene);
            PartMessageService.Instance.ScanPartInternal(part, this);
            //part.Modules.Remove(this);
        }

        
        public override void OnLoad(ConfigNode node) 
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            Debug.LogWarning("Scanning part: " + part.name + " in scene " + HighLogic.LoadedScene);
            PartMessageService.Instance.ScanPartInternal(part, this);
            //part.Modules.Remove(this);
        }

        internal List<PartModule> modulesScanned = new List<PartModule>();
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PartMessageService : MonoBehaviour
    {
        // The singleton instance of the service.
        public static PartMessageService Instance
        {
            get;
            private set;
        }

        public static PartMessageSourceInfo SourceInfo
        {
            get;
            private set;
        }

        private Dictionary<string, LinkedList<ListenerInfo>> listeners = new Dictionary<string, LinkedList<ListenerInfo>>();

        #region Startup and instance management
        // Version of the compatibility checker itself.
        private static int _version = 1;

        private static FieldInfo moduleListListField
            = typeof(PartModuleList)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(t => t.FieldType == typeof(List<PartModule>))
                .First();

        protected PartMessageService() { }

        internal void Start()
        {
            // Checkers are identified by the type name and version field name.
            var allTypes = getAllTypes();

            var fields = from t in allTypes
                         where t.FullName == typeof(PartMessageService).FullName
                         let f = t.GetField("_version", BindingFlags.Static | BindingFlags.NonPublic)
                         where f != null && f.FieldType == typeof(int)
                         select f;

            // Let the latest version of the checker execute.
            if (_version != fields.Max(f => (int)f.GetValue(null)))
                return;

            Debug.Log(String.Format("[PartMessageService] Running {0} version {1} from '{2}'", typeof(PartMessageService).Name, _version, Assembly.GetExecutingAssembly().GetName().Name));

            // Other checkers will see this version and not run.
            // This accomplishes the same as an explicit "ran" flag with fewer moving parts.
            _version = int.MaxValue;

            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Instance = this;
            SourceInfo = new PartMessageSourceInfo();

            // Clear the listeners list when reloaded.
            GameEvents.onGameSceneLoadRequested.Add(scene => listeners.Clear());

            var parts = (from t in allTypes
                         where typeof(Part).IsAssignableFrom(t)
                         select t).ToLookup(t => t.Name);

            var modules = (from t in allTypes
                           where typeof(PartModule).IsAssignableFrom(t)
                           select t).ToLookup(t => t.Name);

            foreach (UrlDir.UrlConfig urlConf in GameDatabase.Instance.root.AllConfigs)
            {
                if (urlConf.type != "PART")
                    continue;

                ConfigNode part = urlConf.config;

                string partModule = part.GetValue("module");
                Type partType = parts[partModule].FirstOrDefault();

                if (partType == null)
                    continue;

                if (NeedsManager(partType))
                    goto addmanager;

                foreach (ConfigNode module in part.GetNodes("MODULE"))
                {
                    string moduleName = module.GetValue("name");
                    Type moduleType = modules[moduleName].FirstOrDefault();

                    if (moduleType == null)
                        continue;

                    if (NeedsManager(moduleType))
                        goto addmanager;
                }
                continue;
                
                addmanager:

                Debug.LogWarning("[PartMessageService] Adding part message manager to part " + part.GetValue("name"));

                try
                {
                    ConfigNode orig = part.CreateCopy();
                    part.ClearNodes();

                    ConfigNode myModule = new ConfigNode("MODULE");
                    myModule.AddValue("name", typeof(PartMessageManager).Name);
                    part.AddNode(myModule);

                    foreach (ConfigNode node in orig.nodes)
                        part.AddNode(node);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex.ToString());
                }

            }
        }
        
        private static PartMessageManager AddManagerModule(Part part)
        {
            PartMessageManager manager = part.gameObject.AddComponent<PartMessageManager>();

            // Need to do a bit of reflection to stick it first in the module list.
            List<PartModule> moduleList = (List<PartModule>)moduleListListField.GetValue(part);
            moduleList.Insert(0, manager);

            return manager;
        }

        private static bool NeedsManager(Type t)
        {
            foreach (EventInfo info in t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (info.GetCustomAttributes(typeof(PartMessageEvent), true).Length > 0)
                    return true;

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (meth.GetCustomAttributes(typeof(PartMessageListener), true).Length > 0)
                    return true;

            return false;
        }

        private static IEnumerable<Type> getAllTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    types = Type.EmptyTypes;
                }

                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }
        #endregion

        #region Object scanning

        /// <summary>
        /// Scan an object for message events and message listeners and hook them up.
        /// Note that all references are dumped on game scene change, so objects must be rescanned when reloaded.
        /// </summary>
        /// <param name="obj">the object to scan</param>
        public void ScanObject(object obj)
        {
            if (obj is PartModule)
                ScanModule((PartModule)obj);
            else if (obj is Part)
                ScanPart((Part)obj);
            else
                ScanObjectInternal(obj);
        }

        /// <summary>
        /// Scan a module for message events and listeners. 
        /// </summary>
        /// <param name="module"></param>
        public void ScanModule(PartModule module)
        {
            if (!NeedsManager(module.GetType()))
                return;

            PartMessageManager manager = module.GetComponent<PartMessageManager>();
            if (manager == null)
            {
                if (!NeedsManager(module.GetType()))
                    return;
                manager = AddManagerModule(module.part);
            }
                
            if (manager.modulesScanned.Contains(module))
                return;

            ScanObjectInternal((object)module);
            manager.modulesScanned.Add(module);
        }

        public void ScanPart(Part part)
        {
            PartMessageManager manager = part.GetComponent<PartMessageManager>();
            if (manager != null)
                return;
            manager = AddManagerModule(part);
            ScanPartInternal(part, manager);
        }

        internal void ScanPartInternal(Part part, PartMessageManager manager)
        {
            ScanObjectInternal(part);
            foreach (PartModule module in part.Modules)
            {
                if (module == manager)
                    continue;

                ScanObjectInternal(module);
                manager.modulesScanned.Add(module);
            }
        }

        private void ScanObjectInternal(object obj)
        {
            Type t = obj.GetType();

            foreach (MethodInfo meth in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (PartMessageListener attr in meth.GetCustomAttributes(typeof(PartMessageListener), true))
                    AddListener(obj, meth, attr);
            }

            foreach (EventInfo evt in t.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Type deleg = evt.EventHandlerType;
                foreach (PartMessageEvent attr in deleg.GetCustomAttributes(typeof(PartMessageEvent), true))
                    GenerateEventHandoff(obj, evt);
            }
        }

        internal void AddListener(object target, MethodInfo meth, PartMessageListener attr)
        {
            //Debug.LogWarning(string.Format("[PartMessageUtils] {0}.{1} Adding listener for {2}", target.GetType().Name, meth.Name, attr.message.FullName));

            if (!attr.scenes.IsLoaded())
                return;

            string message = attr.message.FullName;
            if (Delegate.CreateDelegate(attr.message, target, meth, false) == null)
            {
                Debug.LogError(string.Format("PartMessageListener method {0}.{1} does not support the delegate type {2} as declared in the attribute", meth.DeclaringType, meth.Name, attr.message.Name));
                return;
            }

            LinkedList<ListenerInfo> listenerList;
            if (!listeners.TryGetValue(message, out listenerList))
            {
                listenerList = new LinkedList<ListenerInfo>();
                listeners.Add(message, listenerList);
            }

            ListenerInfo info = new ListenerInfo();
            info.targetRef = new WeakReference(target);
            info.method = meth;
            info.node = listenerList.AddLast(info);
        }

        private void GenerateEventHandoff(object source, EventInfo evt)
        {
            MethodAttributes addAttrs = evt.GetAddMethod(true).Attributes;

            // This generates a dynamic method that pulls the properties of the event
            // plus the arguments passed and hands it off to the EventHandler method below.
            Type message = evt.EventHandlerType;
            MethodInfo m = message.GetMethod("Invoke", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            //Debug.LogWarning(string.Format("[PartMessageUtils] {0}.{1} Adding event handler for {2}", source.GetType().Name, evt.Name, message.FullName));
            
            ParameterInfo[] pLst = m.GetParameters();
            ParameterExpression[] peLst = new ParameterExpression[pLst.Length];
            Expression[] cvrt = new Expression[pLst.Length];
            for (int i = 0; i < pLst.Length; i++)
            {
                peLst[i] = Expression.Parameter(pLst[i].ParameterType, pLst[i].Name);
                cvrt[i] = Expression.Convert(peLst[i], typeof(object));
            }
            Expression createArr = Expression.NewArrayInit(typeof(object), cvrt);

            Expression invoke = Expression.Call(Expression.Constant(this), GetType().GetMethod("HandleMessage", BindingFlags.NonPublic | BindingFlags.Instance),
                Expression.Constant(source), Expression.Constant(evt), createArr);
            
            Delegate d = Expression.Lambda(message, invoke, peLst).Compile();

            // Shouldn't need to use a weak delegate here.
            evt.AddEventHandler(source, d);
        }
        #endregion

        #region Message Handler
        private void HandleMessage(object source, EventInfo evt, object[] args)
        {
            //Debug.LogWarning(string.Format("[PartMessageUtils] {0}.{1} Event invoked", source.GetType().Name, evt.Name));
            
            using (SourceInfo.Push(source, evt))
            {
                foreach (Type messageCls in SourceInfo.srcAllMessages)
                {
                    string message = messageCls.FullName;

                    LinkedList<ListenerInfo> listenerList;
                    if (!listeners.TryGetValue(message, out listenerList))
                        continue;

                    // Shorten parameter list if required
                    ParameterInfo[] methodParams = messageCls.GetMethod("Invoke").GetParameters();
                    if (args.Length > methodParams.Length)
                    {
                        object[] newArgs = new object[methodParams.Length];
                        Array.Copy(args, newArgs, methodParams.Length);
                        args = newArgs;
                    }

                    for (var node = listenerList.First; node != null; )
                    {
                        // hold reference for duration of call
                        object target = node.Value.target;
                        if (target == null)
                        {
                            // Remove dead links from the list
                            var tmp = node;
                            node = node.Next;
                            listenerList.Remove(tmp);
                            continue;
                        }
                        //Debug.LogWarning(string.Format("Invoking {0}.{1} to handle message {2} .", target.GetType(), node.Value.method.Name, SourceInfo.srcMessage));

                        try
                        {
                            node.Value.Invoke(args);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(string.Format("Invoking {0}.{1} to handle message {2} resulted in an exception.", target.GetType(), node.Value.method, SourceInfo.srcMessage));
                            Debug.LogException(ex);
                        }
                        node = node.Next;
                    }
                }
            }
        }
        #endregion

    }
    #endregion

}
