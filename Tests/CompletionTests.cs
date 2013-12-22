﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using D_Parser;
using D_Parser.Completion;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Misc;
using D_Parser.Parser;
using D_Parser.Resolver;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Resolver.Templates;
using D_Parser.Resolver.TypeResolution;
using NUnit.Framework;
using System.IO;
using NUnit.Framework.Constraints;

namespace Tests
{
	/// <summary>
	/// Description of CompletionTests.
	/// </summary>
	public class CompletionTests
	{
		[Test]
		public void ForStatement()
		{
			var code = @"module A; // 1
void main(){
	for(
		int
		i= // 5
		0;
		i<100;
		i++)
		
	{ // 10
	}";
			var mod = DParser.ParseString(code);
			var main = mod["main"].First() as DMethod;
			
			var l = new CodeLocation(1,4);
			var off = DocumentHelper.LocationToOffset(code,l);
			CodeLocation caretLoc;
			//var parsedBlock = AbstractCompletionProvider.FindCurrentCaretContext(code,main, off,l,out ptr, out caretLoc);
			
			/*
			 * a) Test if completion popup would open in each code situation
			 * b) Test in which case iteration variables can be found.
			 */
			
			//TODO
		}

		[Test]
		public void ModuleCompletion()
		{
			var code = @"module 
";

			var ed = GenEditorData (1, 8, code);
			ed.SyntaxTree.ModuleName = "asdf";
			TestCompletionListContents (ed, new[]{ ed.SyntaxTree }, new INode[0]);
		}

		[Test]
		public void ForeachCompletion()
		{
			var code =@"module A;
void main() {
foreach( 
}";
			var ed = GenEditorData (3, 9, code);
			var g = new TestCompletionDataGen (null, null);
			Assert.That(CodeCompletion.GenerateCompletionData (ed, g, 'a', true), Is.False);
		}

		[Test]
		public void IncrementalParsing_()
		{
			var code=@"module A;
int foo;
void main() {}";

			var m = DParser.ParseString (code);
			var main = m ["main"].First () as DMethod;
			var foo = m ["foo"].First () as DVariable;

			IStatement stmt;
			var b = DResolver.SearchBlockAt (m, new CodeLocation (11, 3), out stmt);
			Assert.That (b, Is.SameAs (main));
			Assert.That (stmt, Is.Null);

			code = @"module A;
int foo;
void main(
string ) {

}";
			var caret = new CodeLocation (8, 4);
			bool _u;
			IncrementalParsing.UpdateBlockPartly (m, code, DocumentHelper.LocationToOffset (code, caret), caret, out _u);

			var main2 = m ["main"].First () as DMethod;
			Assert.That (main, Is.Not.SameAs (main2));
			Assert.That (main2.Parameters.Count, Is.EqualTo (1));
			Assert.That (main2.Parameters [0].Type, Is.Not.Null);
			Assert.That (main2.Parameters [0].NameHash, Is.EqualTo(DTokens.IncompleteIdHash));

			var foo2 = m ["foo"].First () as DVariable;
			Assert.That (foo2, Is.SameAs(foo));
		}

		[Test]
		public void IncrementalParsing_Lambdas()
		{
			var code=@"module A;
int foo;
void main() {
	
}";

			var m = DParser.ParseString (code);
			var main = m ["main"].First () as DMethod;
			var foo = m ["foo"].First () as DVariable;

			IStatement stmt;
			var b = DResolver.SearchBlockAt (m, new CodeLocation (1, 4), out stmt);
			Assert.That (b, Is.SameAs (main));
			Assert.That (stmt, Is.TypeOf(typeof(BlockStatement)));

			code = @"module A;
int foo;
void main() {
int a;
(string b) =>  ;
}";
			var caret = new CodeLocation (15, 5);
			bool _u;
			IncrementalParsing.UpdateBlockPartly (stmt as BlockStatement, code, DocumentHelper.LocationToOffset (code, caret), caret, out _u);

			var main2 = m ["main"].First () as DMethod;
			Assert.That (main, Is.SameAs (main2));

			var foo2 = m ["foo"].First () as DVariable;
			Assert.That (foo2, Is.SameAs(foo));

			var lambda = main2.AdditionalChildren [0] as DMethod;
			Assert.That (lambda, Is.Not.Null);
			Assert.That (lambda.Parameters.Count, Is.EqualTo (1));
			Assert.That (lambda.Parameters [0].Type, Is.Not.Null);
			Assert.That (lambda.Parameters [0].Name, Is.EqualTo("b"));

			b = DResolver.SearchBlockAt (m, caret, out stmt);
			Assert.That (lambda, Is.SameAs (b));
			Assert.That (stmt, Is.TypeOf(typeof(ReturnStatement)));
		}

		[Test]
		public void CompletionTrigger()
		{
			Assert.That (@"module ", Does.Trigger);
			Assert.That (@"import ", Does.Trigger);

			Assert.That (@"alias ", Does.Trigger);
			Assert.That (@"alias string ", Does.Not.Trigger);
			Assert.That (@"int ", Does.Not.Trigger);

			Assert.That (@"class ", Does.Not.Trigger);
			Assert.That (@"class A : ", Does.Trigger);
			Assert.That (@"class A(T) if(is( ", Does.Trigger);

		}

		[Test]
		public void StaticMemberCompletion()
		{
			EditorData ed;
			ResolutionContext ctxt = null;
			INode[] wl;
			INode[] bl;

			var s = @"module A;
class Class { static int statInt; int normal; }
void main() { Class. }";

			ed = GenEditorData (3, 21, s);

			wl = new[]{ GetNode(ed, "A.Class.statInt", ref ctxt) };
			bl = new[]{ GetNode(ed, "A.Class.normal", ref ctxt) };

			TestCompletionListContents (ed, wl, bl);
		}

		#region Test lowlevel
		public static class Does
		{
			public class TriggerConstraint : Constraint
			{
				readonly bool neg;
				public TriggerConstraint(bool neg = false)
				{
					this.neg = neg;
				}

				public override bool Matches (object actual)
				{
					var code = actual as string;
					if (code == null)
						return false;

					code += "\n";

					var m = DParser.ParseString (code);
					var cache = ResolutionTests.CreateCache ();
					(cache [0] as MutableRootPackage).AddModule (m);

					var ed = new EditorData{ 
						ModuleCode = code, 
						CaretOffset = code.Length-1, 
						CaretLocation = DocumentHelper.OffsetToLocation(code,code.Length-1),
						SyntaxTree = m,
						ParseCache = cache
					};

					var gen = new TestCompletionDataGen (null, null);
					var res = CodeCompletion.GenerateCompletionData (ed, gen, '\0');

					if (neg ? res : !res)
						return false;

					res = CodeCompletion.GenerateCompletionData (ed, gen, 'a');

					return neg ? !res : res;
				}

				public override void WriteDescriptionTo (MessageWriter writer)
				{
					writer.WriteLine ();
				}
			}

			public readonly static TriggerConstraint Trigger = new TriggerConstraint();

			public static class Not
			{
				public readonly static TriggerConstraint Trigger = new TriggerConstraint (true);
			}
		}

		public class TestCompletionDataGen : ICompletionDataGenerator
		{
			public TestCompletionDataGen(INode[] whiteList, INode[] blackList)
			{
				if(whiteList != null){
					remainingWhiteList= new List<INode>(whiteList);
					this.whiteList = new List<INode>(whiteList);
				}
				if(blackList != null)
					this.blackList = new List<INode>(blackList);
			}

			public List<INode> remainingWhiteList;
			public List<INode> whiteList;
			public List<INode> blackList;

			#region ICompletionDataGenerator implementation

			public List<byte> Tokens = new List<byte> ();
			public void Add (byte Token)
			{
				Tokens.Add (Token);
			}

			public List<string> Attributes = new List<string> ();
			public void AddPropertyAttribute (string AttributeText)
			{
				Attributes.Add (AttributeText);
			}

			public void AddTextItem (string Text, string Description)
			{

			}

			public List<INode> addedItems = new List<INode> ();
			public void Add (INode n)
			{
				if (blackList != null)
					Assert.That (blackList.Contains (n), Is.False);

				if (whiteList != null && whiteList.Contains(n))
					Assert.That (remainingWhiteList.Remove (n), Is.True, n+" occurred at least twice!");

				addedItems.Add (n);
			}

			public void AddModule (DModule module, string nameOverride = null)
			{
				this.Add (module);
			}

			public List<string> Packages = new List<string> ();
			public void AddPackage (string packageName)
			{
				Packages.Add (packageName);
			}

			#endregion

			public bool HasRemainingItems { get{ return remainingWhiteList != null && remainingWhiteList.Count > 0; } }
		}

		public static INode GetNode(EditorData ed, string id, ref ResolutionContext ctxt)
		{
			if (ctxt == null)
				ctxt = ResolutionContext.Create (ed);

			DToken tk;
			var bt = DParser.ParseBasicType (id, out tk);
			var t = TypeDeclarationResolver.ResolveSingle(bt, ctxt);

			var n = (t as DSymbol).Definition;
			Assert.That (n, Is.Not.Null);
			return n;
		}

		public static EditorData GenEditorData(int caretLine, int caretPos,string focusedModuleCode,params string[] otherModuleCodes)
		{
			var cache = ResolutionTests.CreateCache (otherModuleCodes);
			var ed = new EditorData { ParseCache = cache };

			UpdateEditorData (ed, caretLine, caretPos, focusedModuleCode);

			return ed;
		}

		public static void UpdateEditorData(EditorData ed,int caretLine, int caretPos, string focusedModuleCode)
		{
			var mod = DParser.ParseString (focusedModuleCode);
			var pack = ed.ParseCache [0] as MutableRootPackage;

			pack.AddModule (mod);

			ed.ModuleCode = focusedModuleCode;
			ed.SyntaxTree = mod;
			ed.CaretLocation = new CodeLocation (caretPos, caretLine);
			ed.CaretOffset = DocumentHelper.LocationToOffset (focusedModuleCode, caretLine, caretPos);
		}

		public static void TestCompletionListContents(IEditorData ed, INode[] itemWhiteList, INode[] itemBlackList = null, char trigger = '\0')
		{
			var gen = new TestCompletionDataGen (itemWhiteList, itemBlackList);
			Assert.That (CodeCompletion.GenerateCompletionData (ed, gen, trigger), Is.True);

			Assert.That (gen.HasRemainingItems, Is.False, "Some items were not enlisted!");
		}
		#endregion
	}
}
