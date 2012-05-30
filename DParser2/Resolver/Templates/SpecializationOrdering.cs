﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using D_Parser.Dom;

namespace D_Parser.Resolver.Templates
{
	/// <summary>
	/// See http://msdn.microsoft.com/de-de/library/zaycz069.aspx
	/// </summary>
	public class SpecializationOrdering
	{
		List<ResolveResult> templateOverloads;
		ResolverContextStack ctxt;

		public static ResolveResult[] FilterFromMostToLeastSpecialized(
			List<ResolveResult> templateOverloads,
			ResolverContextStack ctxt)
		{
			if (templateOverloads == null)
				return null;

			var so = new SpecializationOrdering { ctxt = ctxt,  templateOverloads=templateOverloads };

			var currentlyMostSpecialized = templateOverloads[0] as TemplateInstanceResult;

			for (int i = 1; i < templateOverloads.Count; i++)
			{
				var evenMoreSpecialized = so.GetTheMoreSpecialized(currentlyMostSpecialized, templateOverloads[i] as TemplateInstanceResult);

				if (evenMoreSpecialized != null)
				{
					currentlyMostSpecialized = evenMoreSpecialized;
				}
				else if (i == templateOverloads.Count - 1)
				{
					/*
					 * It might be the case that Type 1 is equally specialized as Type 2 is, but:
					 * If comparing Type 2 with Type 3 turns out that Type 3 is more specialized, return Type 3!
					 * (There probably will be a global resolution error cache  required to warn the user that
					 * all template parameters of Type 1 are equal to those of Type 2)
					 */

					// Ambigious result -- ERROR!
					return new[] { currentlyMostSpecialized, templateOverloads[i] };
				}
			}

			return new[]{ currentlyMostSpecialized };
		}

		TemplateInstanceResult GetTheMoreSpecialized(TemplateInstanceResult r1, TemplateInstanceResult r2)
		{
			if (r1 == null || r2 == null)
				return null;

			if (IsMoreSpecialized(r1, r2))
			{
				if (IsMoreSpecialized(r2, r1))
					return null;
				else
					return r1;
			}
			else
			{ 
				if (IsMoreSpecialized(r2, r1))
					return r2;
				else
					return null;
			}
		}

		bool IsMoreSpecialized(TemplateInstanceResult r1, TemplateInstanceResult r2)
		{
			var dn1 = r1.Node as DNode;
			var dn2 = r2.Node as DNode;

			if (dn1 == null || dn2 == null)
				return false;

			var dummyList = new Dictionary<string, ResolveResult[]>();
			foreach (var t in dn1.TemplateParameters)
				dummyList.Add(t.Name, null);

			var tp1_enum = dn1.TemplateParameters.GetEnumerator();
			var tp2_enum = dn2.TemplateParameters.GetEnumerator();

			while (tp1_enum.MoveNext() && tp2_enum.MoveNext())
			{
				if (tp1_enum.Current is TemplateTypeParameter && tp2_enum.Current is TemplateTypeParameter)
				{
					if (!IsMoreSpecialized((TemplateTypeParameter)tp1_enum.Current, (TemplateTypeParameter)tp2_enum.Current, dummyList))
						return false;
				}
				else
					return false;
			}

			return true;
		}

		/// <summary>
		/// Tests if t1 is more specialized than t2
		/// </summary>
		bool IsMoreSpecialized(TemplateTypeParameter t1, TemplateTypeParameter t2, Dictionary<string,ResolveResult[]> t1_DummyParamList)
		{
			// If one parameter is not specialized it should be clear
			if (t1.Specialization != null && t2.Specialization == null)
				return true;
			else if (t1.Specialization == null) // Return false if t2 is more specialized or if t1 as well as t2 are not specialized
				return false;

			// Make a type out of t1's specialization
			var block=ctxt.ScopedBlock.NodeRoot as IBlockNode;
			var frame=ctxt.PushNewScope(block);

			// Make the T in e.g. T[] a virtual type so T will be replaced by it
			var dummyType= new[]{ new TypeResult { Node = new DClassLike {	Name = "X" } }};
			foreach(var kv in t1_DummyParamList)
				frame.PreferredLocals[kv.Key] = dummyType;

			var t1_TypeResults = Resolver.TypeResolution.TypeDeclarationResolver.Resolve(t1.Specialization,ctxt);
			if(t1_TypeResults== null || t1_TypeResults.Length == 0)
				return true;

			ctxt.Pop();

			// Now try to fit the virtual Type t2 into t1 - and return true if it's possible
			return new TemplateParameterDeduction(new Dictionary<string, ResolveResult[]>(), ctxt)
				.Handle(t2, t1_TypeResults[0]);
		}
	}
}
