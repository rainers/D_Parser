using System.Collections.Generic;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Parser;

namespace D_Parser.Completion
{
	public class ArgumentsResolutionResult
	{
		public bool IsMethodArguments;
		public bool IsTemplateInstanceArguments;

		public IExpression ParsedExpression;

		/// <summary>
		/// Usually some part of the ParsedExpression.
		/// For instance in a PostfixExpression_MethodCall it'd be the PostfixForeExpression.
		/// </summary>
		public object MethodIdentifier;

		public AbstractType[] ResolvedTypesOrMethods;

		public readonly Dictionary<IExpression, AbstractType> TemplateArguments = new Dictionary<IExpression, AbstractType>();
		/// <summary>
		/// Stores the already typed arguments (Expressions) + their resolved types.
		/// The value part will be null if nothing could get returned.
		/// </summary>
		public readonly Dictionary<IExpression, AbstractType> Arguments = new Dictionary<IExpression, AbstractType>();

		/// <summary>
		///	Identifies the currently called method overload. Is an index related to <see cref="ResolvedTypesOrMethods"/>
		/// </summary>
		public int CurrentlyCalledMethod;
		public IExpression CurrentlyTypedArgument
		{
			get
			{
				if (Arguments != null && Arguments.Count > CurrentlyTypedArgumentIndex)
				{
					int i = 0;
					foreach (var kv in Arguments)
					{
						if (i == CurrentlyTypedArgumentIndex)
							return kv.Key;
						i++;
					}
				}
				return null;
			}
		}
		public int CurrentlyTypedArgumentIndex;
	}

	public class ParameterInsightResolution
	{
		
		/// <summary>
		/// Reparses the given method's fucntion body until the cursor position,
		/// searches the last occurring method call or template instantiation,
		/// counts its already typed arguments
		/// and returns a wrapper containing all the information.
		/// </summary>
		public static ArgumentsResolutionResult ResolveArgumentContext(
			IEditorData Editor,
			ResolverContextStack ctxt)
		{
			ParserTrackerVariables trackVars = null;
			IStatement curStmt = null; 
			IExpression e = null, lastParamExpression = null;

			// Get the currently scoped block
			var curBlock = DResolver.SearchBlockAt(Editor.SyntaxTree, Editor.CaretLocation, out curStmt);

			if (curBlock == null)
				return null;

			// Get an updated abstract view on the module's code
			var parsedStmtBlock = CtrlSpaceCompletionProvider.FindCurrentCaretContext(
				Editor.ModuleCode, curBlock ,Editor.CaretOffset,	Editor.CaretLocation, out trackVars) as StatementContainingStatement;

			if (parsedStmtBlock == null)
				return null;

			// Search the returned statement block (i.e. function body) for the current statement
			var exStmt = BlockStatement.SearchBlockStatement(parsedStmtBlock, Editor.CaretLocation) as IExpressionContainingStatement;

			// Generally expect it to be an expression containing statement 
			// - and search for the most fitting
			if (exStmt != null && exStmt.SubExpressions != null)
				foreach (var ex in exStmt.SubExpressions)
					if (ex != null && ex.Location < Editor.CaretLocation && ex.EndLocation >= Editor.CaretLocation)
					{
						e = lastParamExpression = ex;
						break;
					}

			// Go search deeper for the inner-most call/parametric expression
			while (e is ContainerExpression)
			{
				var lastE = e;

				foreach (var subEx in ((ContainerExpression)e).SubExpressions)
					if (subEx.Location < Editor.CaretLocation && subEx.EndLocation >= Editor.CaretLocation)
					{
						e = subEx;
						if (ExpressionHelper.IsParamRelatedExpression(subEx))
							lastParamExpression = subEx;
						break;
					}

				if (lastE == e) // Small deadlock prevention
					break;
			}

			if (lastParamExpression == null)
			{
				// Give it a last chance by handling the lastly parsed object 
				// - which is a TemplateInstanceExpression in quite all cases
				lastParamExpression = trackVars.LastParsedObject as IExpression;
			}

			/*
			 * Then handle the lastly found expression regarding the following points:
			 * 
			 * 1) foo(			-- normal arguments only
			 * 2) foo!(...)(	-- normal arguments + template args
			 * 3) foo!(		-- template args only
			 * 4) new myclass(  -- ctor call
			 * 5) new myclass!( -- ditto
			 * 6) new myclass!(...)(
			 * 7) mystruct(		-- opCall call
			 */

			var res = new ArgumentsResolutionResult() { 
				ParsedExpression = lastParamExpression
			};

			// 1), 2)
			if (lastParamExpression is PostfixExpression_MethodCall)
			{
				res.IsMethodArguments = true;
				var call = (PostfixExpression_MethodCall) lastParamExpression;

				res.MethodIdentifier = call.PostfixForeExpression;
				res.ResolvedTypesOrMethods = TryGetUnfilteredMethodOverloads(call.PostfixForeExpression, ctxt, call);

				if (call.Arguments != null)
				{
					int i = 0;
					foreach (var arg in call.Arguments)
					{
						if (Editor.CaretLocation >= arg.Location && Editor.CaretLocation <= arg.EndLocation)
						{
							res.CurrentlyTypedArgumentIndex = i;
							break;
						}
						i++;
					}
				}

			}
			// 3)
			else if (lastParamExpression is TemplateInstanceExpression)
			{
				var templ = (TemplateInstanceExpression)lastParamExpression;

				res.IsTemplateInstanceArguments = true;

				res.MethodIdentifier = templ;
				res.ResolvedTypesOrMethods = Evaluation.GetOverloads(templ, ctxt, null, false);

				if (templ.Arguments != null)
				{
					int i = 0;
					foreach (var arg in templ.Arguments)
					{
						if (Editor.CaretLocation >= arg.Location && Editor.CaretLocation <= arg.EndLocation)
						{
							res.CurrentlyTypedArgumentIndex = i;
							break;
						}
						i++;
					}
				}
			}
			else if (lastParamExpression is PostfixExpression_Access)
			{
				var acc = (PostfixExpression_Access)lastParamExpression;

				res.MethodIdentifier = acc.PostfixForeExpression;
				res.ResolvedTypesOrMethods = TryGetUnfilteredMethodOverloads(acc.PostfixForeExpression, ctxt, acc);

				if (res.ResolvedTypesOrMethods == null)
					return res;

				if (acc.AccessExpression is NewExpression)
					CalculateCurrentArgument(acc.AccessExpression as NewExpression, res, Editor.CaretLocation, ctxt, res.ResolvedTypesOrMethods);
			}
			else if (lastParamExpression is NewExpression)
				HandleNewExpression((NewExpression)lastParamExpression,res,Editor,ctxt,curBlock);

			/*
			 * alias int function(int a, bool b) myDeleg;
			 * alias myDeleg myDeleg2;
			 * 
			 * myDeleg dg;
			 * 
			 * dg( -- it's not needed to have myDeleg but the base type for what it stands for
			 * 
			 * ISSUE:
			 * myDeleg( -- not allowed though
			 * myDeleg2( -- allowed neither!
			 */
			if (res.ResolvedTypesOrMethods != null)
				res.ResolvedTypesOrMethods = DResolver.StripAliasSymbols(res.ResolvedTypesOrMethods);

			return res;
		}

		static void HandleNewExpression(NewExpression nex, 
			ArgumentsResolutionResult res, 
			IEditorData Editor, 
			ResolverContextStack ctxt,
			IBlockNode curBlock)
		{
			res.MethodIdentifier = nex;
			CalculateCurrentArgument(nex, res, Editor.CaretLocation, ctxt);

			var type = TypeDeclarationResolver.ResolveSingle(nex.Type, ctxt) as ClassType;

			//TODO: Inform the user that only classes can be instantiated
			if (type != null)
			{
				var constructors = new List<DMethod>();
				bool explicitCtorFound = false;

				foreach (var member in type.Definition)
				{
					var dm = member as DMethod;

					if (dm != null && dm.SpecialType == DMethod.MethodType.Constructor)
					{
						explicitCtorFound = true;
						if (!dm.IsPublic)
						{
							var curNode = curBlock;
							bool pass = false;
							do
							{
								if (curNode == type.Definition)
								{
									pass = true;
									break;
								}
							}
							while ((curNode = curNode.Parent as IBlockNode) != curNode);

							if (!pass)
								continue;
						}

						constructors.Add(dm);
					}
				}

				if (constructors.Count == 0)
				{
					if (explicitCtorFound)
					{
						// TODO: Somehow inform the user that the current class can't be instantiated
					}
					else
					{
						// Introduce default constructor
						constructors.Add(new DMethod(DMethod.MethodType.Constructor)
						{
							Description = "Default constructor for " + type.Name,
							Parent = type.Definition
						});
					}
				}

				// Wrapp all ctor members in MemberSymbols
				var _ctors = new List<AbstractType>();
				foreach (var ctor in constructors)
					_ctors.Add(new MemberSymbol(ctor, type, nex.Type));
				res.ResolvedTypesOrMethods = _ctors.ToArray();

				//TODO: Probably pre-select the current ctor by handling previously typed arguments etc.
			}
		}

		public static AbstractType[] TryGetUnfilteredMethodOverloads(IExpression foreExpression, ResolverContextStack ctxt, IExpression supExpression=null)
		{
			if (foreExpression is TemplateInstanceExpression)
				return Evaluation.GetOverloads((TemplateInstanceExpression)foreExpression, ctxt, null);
			else if (foreExpression is IdentifierExpression)
				return Evaluation.GetOverloads((IdentifierExpression)foreExpression, ctxt);
			else if (foreExpression is PostfixExpression_Access)
			{
				bool ufcs=false; // TODO?
				return Evaluation.GetAccessedOverloads((PostfixExpression_Access)foreExpression, ctxt, out ufcs, null, false);
			}
			else
				return new[] { Evaluation.EvaluateType(foreExpression, ctxt) };
		}

		static void CalculateCurrentArgument(NewExpression nex, 
			ArgumentsResolutionResult res, 
			CodeLocation caretLocation, 
			ResolverContextStack ctxt,
			IEnumerable<AbstractType> resultBases=null)
		{
			if (nex.Arguments != null)
			{
				int i = 0;
				foreach (var arg in nex.Arguments)
				{
					if (caretLocation >= arg.Location && caretLocation <= arg.EndLocation)
					{
						res.CurrentlyTypedArgumentIndex = i;
						break;
					}
					i++;
				}
			}
		}

		public static ArgumentsResolutionResult ResolveArgumentContext(IEditorData editorData)
		{
			return ResolveArgumentContext(editorData, ResolverContextStack.Create(editorData));
		}

		static IExpression SearchForMethodCallsOrTemplateInstances(IStatement Statement, CodeLocation Caret)
		{
			IExpression curExpression = null;
			INode curDeclaration = null;

			/*
			 * Step 1: Step down the statement hierarchy to find the stmt that's most next to Caret
			 * Note: As long we haven't found any fitting elements, go on searching
			 */
			while (Statement != null && curExpression == null && curDeclaration == null)
			{
				if (Statement is IExpressionContainingStatement)
				{
					var exprs = (Statement as IExpressionContainingStatement).SubExpressions;

					if (exprs != null && exprs.Length > 0)
						foreach (var expr in exprs)
							if (expr != null && Caret >= expr.Location && Caret <= expr.EndLocation)
							{
								curExpression = expr;
								break;
							}
				}

				if (Statement is IDeclarationContainingStatement)
				{
					var decls = (Statement as IDeclarationContainingStatement).Declarations;

					if (decls != null && decls.Length > 0)
						foreach (var decl in decls)
							if (decl != null && Caret >= decl.Location && Caret <= decl.EndLocation)
							{
								curDeclaration = decl;
								break;
							}
				}

				if (Statement is StatementContainingStatement)
				{
					var stmts = (Statement as StatementContainingStatement).SubStatements;

					bool foundDeeperStmt = false;

					if (stmts != null && stmts.Length > 0)
						foreach (var stmt in stmts)
							if (stmt != null && Caret >= stmt.Location && Caret <= stmt.EndLocation)
							{
								foundDeeperStmt = true;
								Statement = stmt;
								break;
							}

					if (foundDeeperStmt)
						continue;
				}

				break;
			}

			if (curDeclaration == null && curExpression == null)
				return null;


			/*
			 * Step 2: If a declaration was found, check for its inner elements
			 */
			if (curDeclaration != null)
			{
				if (curDeclaration is DVariable)
				{
					var dv = curDeclaration as DVariable;

					if (dv.Initializer != null && Caret >= dv.Initializer.Location && Caret <= dv.Initializer.EndLocation)
						curExpression = dv.Initializer;
				}

				//TODO: Watch the node's type! Over there, there also can be template instances..
			}

			if (curExpression != null)
			{
				IExpression curMethodOrTemplateInstance = null;

				while (curExpression != null)
				{
					if (!(curExpression.Location <= Caret || curExpression.EndLocation >= Caret))
						break;

					if (curExpression is PostfixExpression_MethodCall)
						curMethodOrTemplateInstance = curExpression;

					else if (curExpression is TemplateInstanceExpression)
						curMethodOrTemplateInstance = curExpression;

					else if (curExpression is PostfixExpression_Access)
					{
						var acc = curExpression as PostfixExpression_Access;

						if (acc.AccessExpression is TemplateInstanceExpression)
							curMethodOrTemplateInstance = (TemplateInstanceExpression)acc.AccessExpression;
						else if (acc.AccessExpression is NewExpression)
							curMethodOrTemplateInstance = (NewExpression)acc.AccessExpression;
					}

					else if (curExpression is NewExpression)
						curMethodOrTemplateInstance = curExpression;

					if (curExpression is ContainerExpression)
					{
						var currentContainer = curExpression as ContainerExpression;

						var subExpressions = currentContainer.SubExpressions;
						bool foundMatch = false;
						if (subExpressions != null && subExpressions.Length > 0)
							foreach (var se in subExpressions)
								if (se != null && Caret >= se.Location && Caret <= se.EndLocation)
								{
									curExpression = se;
									foundMatch = true;
									break;
								}

						if (foundMatch)
							continue;
					}
					break;
				}

				return curMethodOrTemplateInstance;
			}


			return null;
		}
	}
}
