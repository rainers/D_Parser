﻿using System;
using D_Parser.Parser;

namespace D_Parser.Dom.Expressions
{
	public class TraitsExpression : PrimaryExpression, ContainerExpression
	{
		public string Keyword;
		public TraitsArgument[] Arguments;

		public override string ToString()
		{
			var ret = "__traits(" + Keyword;

			if (Arguments != null)
				foreach (var a in Arguments)
					ret += "," + a.ToString();

			return ret + ")";
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
			ulong hashCode = DTokens.__traits;
			unchecked
			{
				if (Keyword != null)
					hashCode += 1000000007 * (ulong)Keyword.GetHashCode();
				if (Arguments != null)
				{
					ulong i = 1;
					foreach (var arg in Arguments)
						hashCode += 1000000009 * i++ * arg.GetHash();
				}
			}
			return hashCode;
		}

		public IExpression[] SubExpressions
		{
			get
			{
				if (Arguments == null || Arguments.Length == 0)
					return null;

				var exs = new IExpression[Arguments.Length];
				for (int i = Arguments.Length - 1; i >= 0; i--)
					exs[i] = Arguments[i].AssignExpression;
				return exs;
			}
		}
	}
}

