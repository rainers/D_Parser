﻿using System;
using System.Collections.Generic;

namespace D_Parser.Dom.Expressions
{
	/// <summary>
	/// NewExpression:
	///		NewArguments Type [ AssignExpression ]
	///		NewArguments Type ( ArgumentList )
	///		NewArguments Type
	/// </summary>
	public class NewExpression : UnaryExpression, ContainerExpression
	{
		public ITypeDeclaration Type { get; set; }

		public IExpression[] NewArguments { get; set; }

		public IExpression[] Arguments { get; set; }

		public override string ToString()
		{
			var ret = "new";

			if (NewArguments != null)
			{
				ret += "(";
				foreach (var e in NewArguments)
					ret += e.ToString() + ",";
				ret = ret.TrimEnd(',') + ")";
			}

			if (Type != null)
				ret += " " + Type.ToString();

			if (!(Type is ArrayDecl))
			{
				ret += '(';
				if (Arguments != null)
					foreach (var e in Arguments)
						ret += e.ToString() + ",";

				ret = ret.TrimEnd(',') + ')';
			}

			return ret;
		}

		public CodeLocation Location
		{
			get;
			set;
		}

		public CodeLocation EndLocation
		{
			get;
			set;
		}

		public IExpression[] SubExpressions
		{
			get
			{
				var l = new List<IExpression>();

				// In case of a template instance
				if (Type is IExpression)
					l.Add(Type as IExpression);

				if (NewArguments != null)
					l.AddRange(NewArguments);

				if (Arguments != null)
					l.AddRange(Arguments);

				if (l.Count > 0)
					return l.ToArray();

				return null;
			}
		}

		public void Accept(ExpressionVisitor vis)
		{
			vis.Visit(this);
		}

		public R Accept<R>(ExpressionVisitor<R> vis)
		{
			return vis.Visit(this);
		}

		public ulong GetHash()
		{
			ulong hashCode = 0uL;
			unchecked
			{
				if (Type != null)
					hashCode += 1000000007 * (ulong)Type.GetHashCode();
				if (NewArguments != null && NewArguments.Length != 0)
					for (int i = NewArguments.Length; i != 0;)
						hashCode += 1000000009 * (ulong)i * NewArguments[--i].GetHash();
				if (Arguments != null && Arguments.Length != 0)
					for (int i = Arguments.Length; i != 0;)
						hashCode += 1000000021 * (ulong)i * Arguments[--i].GetHash();
			}
			return hashCode;
		}
	}
}

