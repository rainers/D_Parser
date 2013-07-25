﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D_Parser.Misc;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Parser;

namespace D_Parser.Dom
{
	public class MutableRootPackage : RootPackage
	{
		public MutableRootPackage() {}
		public MutableRootPackage(params DModule[] modules)
		{
			foreach(var m in modules)
				base.AddModule(m);
		}

		public new bool AddModule(DModule ast)
		{
			return base.AddModule (ast);
		}

		public new ModulePackage GetOrCreateSubPackage(string package, bool create = false)
		{
			return base.GetOrCreateSubPackage (package, create);
		}

		public new bool RemovePackage(string name)
		{
			return base.RemovePackage(name);
		}

		public new bool RemoveModule(string name)
		{
			return base.RemoveModule (name);
		}
	}

	public class RootPackage : ModulePackage
	{
		#region Ufcs caching
		public readonly UFCSCache UfcsCache;
		#endregion

		public RootPackage() : base(null, "<root>") {
			UfcsCache = new UFCSCache (this);
		}

		#region Common type bypasses
		public bool IsObjectClassDefined
		{
			get { return ObjectClass != null; }
		}

		/// <summary>
		/// To improve resolution performance, the object class that can be defined only once will be stored over here.
		/// </summary>
		public DClassLike ObjectClass
		{
			get;
			private set;
		}

		public AbstractType SizeT
		{
			get;
			private set;
		}

		/// <summary>
		/// See <see cref="ObjectClass"/>
		/// </summary>
		public ClassType ObjectClassResult
		{
			get;
			private set;
		}

		public void TryPreResolveCommonTypes()
		{
			var obj = GetModule ("object");
			if (obj == null)
				return;

			ParseCacheView cache = null;

			foreach (var m in obj) {
				if (m.Name == "Object" && m is DClassLike) {
					ObjectClassResult = new ClassType (ObjectClass = (DClassLike)m, new IdentifierDeclaration ("Object"), null);
					break;
				} else if (m.Name == "size_t") {
					if (cache == null)
						cache = new ParseCacheView (new[]{this});
					//TODO: Do a version check, so that only on x64 dmd installations, size_t equals ulong.
					SizeT = TypeDeclarationResolver.HandleNodeMatch (m, 
					                                                 ResolutionContext.Create (cache, null, obj));
				}
			}
		}
		#endregion
	}

	public class ModulePackage : IEnumerable<DModule>, IEnumerable<ModulePackage>
	{
		internal ModulePackage(ModulePackage parent, string name) {
			this.Parent = parent;
			this.Name = name;
		}

		public readonly ModulePackage Parent;

		public readonly string Name;
		internal Dictionary<string, ModulePackage> packages = new Dictionary<string, ModulePackage>();
		internal Dictionary<string, DModule> modules = new Dictionary<string, DModule>();
		
		public IEnumerable<KeyValuePair<string,ModulePackage>> Packages {get{return packages;}}
		public IEnumerable<KeyValuePair<string, DModule>> Modules {get{return modules;}}
		
		public bool IsEmpty {get{return packages.Count == 0 && modules.Count == 0;}}
		
		public ModulePackage[] GetPackages()
		{
			var packs = new ModulePackage[packages.Count];
			packages.Values.CopyTo(packs,0);
			return packs;
		}
		
		public DModule[] GetModules()
		{
			var mods = new DModule[modules.Count];
			modules.Values.CopyTo(mods,0);
			return mods;
		}
		
		public ModulePackage GetPackage(string name)
		{
			ModulePackage pack;
			packages.TryGetValue(name,out pack);
			return pack;
		}
		
		public DModule GetModule(string name)
		{
			var pack = GetSubPackage (ModuleNameHelper.ExtractPackageName (name));
			DModule ast;
			if (pack == null)
				return null;
			pack.modules.TryGetValue(name, out ast);
			return ast;
		}

		public string Path
		{
			get
			{
				return ((Parent == null || Parent is RootPackage) ? "" : (Parent.Path + ".")) + Name;
			}
		}

		public override string ToString()
		{
			return Path;
		}

		public IEnumerator<DModule> GetEnumerator()
		{
			lock(modules)
				foreach (var kv in modules)
					yield return kv.Value;

			lock(packages)
				foreach (var kv in packages)
					lock(kv.Value)
						foreach (var ast in kv.Value)
							yield return ast;
		}

		IEnumerator<ModulePackage> IEnumerable<ModulePackage>.GetEnumerator()
		{
			lock(packages)
				foreach (var kv in packages)
				{
					yield return kv.Value;

					lock ((IEnumerable<ModulePackage>)kv.Value)
						foreach (var p in (IEnumerable<ModulePackage>)kv.Value)
							yield return p;
				}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
		
		internal bool AddModule(DModule ast)
		{
			if(ast == null || string.IsNullOrEmpty(ast.ModuleName))
				return false;

			lock(modules)
				modules[ModuleNameHelper.ExtractModuleName(ast.ModuleName)] = ast;
			return true;
		}
		
		internal bool RemovePackage(string name)
		{
			return packages.Remove(ModuleNameHelper.ExtractModuleName(name));
		}
		
		internal bool RemoveModule(string name)
		{
			name = ModuleNameHelper.ExtractModuleName(name);
			DModule ast;
			if(modules.TryGetValue(name, out ast))
			{
				modules.Remove(name);
				return true;
			}
			return false;
		}

		public ModulePackage GetSubPackage(string package)
		{
			return GetOrCreateSubPackage (package, false);
		}

		public DModule GetSubModule(string moduleName)
		{
			if (string.IsNullOrEmpty (moduleName))
				return null;

			var pack = GetOrCreateSubPackage (ModuleNameHelper.ExtractPackageName (moduleName));

			if (pack == null)
				return null;

			return pack.GetModule (ModuleNameHelper.ExtractModuleName(moduleName));
		}

		internal ModulePackage GetOrCreateSubPackage(string package, bool create = false)
		{
			if (string.IsNullOrEmpty(package))
				return this;

			var currentPackage = this;
			var parts = ModuleNameHelper.SplitModuleName(package);

			for(int k = 0; k < parts.Length; k++)
			{
				ModulePackage returnValue;

				lock(currentPackage.packages)
					if (!currentPackage.packages.TryGetValue(parts[k], out returnValue))
					{
						if (create)
							returnValue = currentPackage.packages[parts[k]] = new ModulePackage(currentPackage, parts[k]);
						else
							return null;
					}

				currentPackage = returnValue;
			}

			return currentPackage;
		}

		internal static ModulePackage GetOrCreatePackage(ModulePackage root, string package, bool create = false)
		{
			return root.GetOrCreateSubPackage(package, create);
		}

		public DModule this[string modName]
		{
			get{return GetModule (modName);}
		}
	}
}
