﻿//
// IncrementalParsing.cs
//
// Author:
//       Alexander Bothe <info@alexanderbothe.com>
//
// Copyright (c) 2013 Alexander Bothe
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using D_Parser.Dom;
using D_Parser.Misc;
using D_Parser.Completion;
using D_Parser.Dom.Statements;

namespace D_Parser.Parser
{
	public static class IncrementalParsing
	{
		#region DMethod updating
		public static void UpdateBlockPartly(this BlockStatement bs, IEditorData ed, out bool isInsideNonCodeSegment)
		{
			UpdateBlockPartly (bs, ed.ModuleCode, ed.CaretOffset, ed.CaretLocation, out isInsideNonCodeSegment);
		}

		public static void UpdateBlockPartly(this BlockStatement bs, string code, int caretOffset, CodeLocation caretLocation, out bool isInsideNonCodeSegment)
		{
			isInsideNonCodeSegment = false;
			var finalParentMethod = bs.ParentNode as DMethod;
			var finalStmtsList = bs._Statements;

			var startLoc = bs.Location;
			int startStmtIndex;
			for(startStmtIndex = finalStmtsList.Count-1; startStmtIndex >= 0; startStmtIndex--) {
				var n = finalStmtsList [startStmtIndex];
				if (n.EndLocation.Line > 0 && n.EndLocation.Line < caretLocation.Line) {
					startLoc = --startStmtIndex == -1 ? 
						bs.Location : finalStmtsList [startStmtIndex].EndLocation;
					break;
				}
			}

			var startOff = startLoc.Line > 1 ? DocumentHelper.GetOffsetByRelativeLocation (code, caretLocation, caretOffset, startLoc) : 0;

			if (startOff >= caretOffset)
				return;

			var tempParentBlock = new DMethod ();
			var tempBlockStmt = new BlockStatement { ParentNode = tempParentBlock };

			try{
				using (var sv = new StringView (code, startOff, caretOffset - startOff))
				using (var p = DParser.Create(sv)) {
					p.Lexer.SetInitialLocation (startLoc);
					p.Step ();

					if(p.laKind == DTokens.OpenCurlyBrace)
						p.Step();

					while (!p.IsEOF) {

						if (p.laKind == DTokens.CloseCurlyBrace) {
							p.Step ();
							/*if (metaDecls.Count > 0)
								metaDecls.RemoveAt (metaDecls.Count - 1);*/
							continue;
						}

						var stmt = p.Statement (true, true, tempParentBlock, tempBlockStmt);
						if(stmt != null)
							tempBlockStmt.Add(stmt);
					}

					tempBlockStmt.EndLocation = new CodeLocation(p.la.Column+1,p.la.Line);

					if(isInsideNonCodeSegment = p.Lexer.endedWhileBeingInNonCodeSequence)
						return;
				}
			}catch(Exception ex) {
				Console.WriteLine (ex.Message);
			}

			// Remove old statements from startLoc until caretLocation
			int i = startStmtIndex + 1;
			while (i < finalStmtsList.Count && finalStmtsList [i].Location < caretLocation)
				finalStmtsList.RemoveAt (i);

			// Insert new statements
			if (tempBlockStmt.EndLocation > bs.EndLocation)
				bs.EndLocation = tempBlockStmt.EndLocation;
			foreach (var stmt in tempBlockStmt._Statements)
			{
				stmt.ParentNode = finalParentMethod;
				stmt.Parent = bs;
			}
			AssignInStatementDeclarationsToNewParentNode(tempBlockStmt, finalParentMethod);
			finalStmtsList.InsertRange(startStmtIndex+1, tempBlockStmt._Statements);
			
			if (finalParentMethod != null) {
				var finalParentChildren = finalParentMethod.AdditionalChildren;
				// Remove old parent block children
				int startDeclIndex;
				for(startDeclIndex = finalParentChildren.Count-1; startDeclIndex >= 0; startDeclIndex--) {
					var n = finalParentChildren [startDeclIndex];
					if (n == null) {
						finalParentChildren.RemoveAt (startDeclIndex);
						continue;
					}
					if (n.Location < startLoc)
						break;
					if (n.Location < caretLocation)
						finalParentChildren.RemoveAt(startDeclIndex);
				}

				// Insert new special declarations
				foreach (var decl in tempParentBlock)
					decl.Parent = finalParentMethod;
				finalParentChildren.InsertRange(startDeclIndex+1, tempParentBlock);

				finalParentMethod.UpdateChildrenArray ();
				if (bs.EndLocation > finalParentMethod.EndLocation)
					finalParentMethod.EndLocation = bs.EndLocation;
			}

			//TODO: Handle DBlockNode parents?
		}

		static void AssignInStatementDeclarationsToNewParentNode(StatementContainingStatement ss, INode newParentNode)
		{
			IDeclarationContainingStatement dcs;
			if (ss.SubStatements != null)
			{
				foreach (var s in ss.SubStatements)
				{
					dcs = s as IDeclarationContainingStatement;
					if (dcs != null && dcs.Declarations != null)
						foreach (var n in dcs.Declarations)
							n.Parent = newParentNode;

					if (s is StatementContainingStatement)
						AssignInStatementDeclarationsToNewParentNode(s as StatementContainingStatement, newParentNode);
				}
			}
		}
		#endregion

		#region DBlockNode updating
		public static void UpdateBlockPartly(this DBlockNode bn, IEditorData ed, out bool isInsideNonCodeSegment)
		{
			UpdateBlockPartly (bn, ed.ModuleCode, ed.CaretOffset, ed.CaretLocation, out isInsideNonCodeSegment);
		}

		public static void UpdateBlockPartly(this DBlockNode bn, string code,
			int caretOffset, CodeLocation caretLocation, out bool isInsideNonCodeSegment)
		{
			isInsideNonCodeSegment = false;

			// Get the end location of the declaration that appears before the caret.
			var startLoc = bn.BlockStartLocation;
			int startDeclIndex;
			for(startDeclIndex = bn.Children.Count-1; startDeclIndex >= 0; startDeclIndex--) {
				var n = bn.Children [startDeclIndex];
				if (n.EndLocation.Line > 0 && n.EndLocation.Line < caretLocation.Line) {
					startLoc = --startDeclIndex == -1 ? 
						bn.BlockStartLocation : bn.Children [startDeclIndex].EndLocation;
					break;
				}
			}

			var startOff = startLoc.Line > 1 ? DocumentHelper.GetOffsetByRelativeLocation (code, caretLocation, caretOffset, startLoc) : 0;

			// Immediately break to waste no time if there's nothing to parse
			if (startOff >= caretOffset)
				return;

			// Get meta block stack so they can be registered while parsing 
			//var metaDecls = bn.GetMetaBlockStack (startLoc, true, false);

			// Parse region from start until caret for maximum efficiency
			var tempBlock = bn is DEnum ? new DEnum() : new DBlockNode();
			try{
				using (var sv = new StringView (code, startOff, caretOffset - startOff))
				using (var p = DParser.Create(sv)) {
					p.Lexer.SetInitialLocation (startLoc);
					p.Step ();

					if(p.laKind == DTokens.OpenCurlyBrace)
						p.Step();

					// Enum bodies
					if(bn is DEnum)
					{
						do
						{
							if(p.laKind == DTokens.Comma)
								p.Step();
							var laBackup = p.la;
							p.EnumValue(tempBlock as DEnum);
							if(p.la == laBackup)
								break;
						}
						while(!p.IsEOF);
					}
					else // Normal class/module bodies
					{
						if(p.laKind == DTokens.Module && bn is DModule)
							tempBlock.Add(p.ModuleDeclaration());

						while (!p.IsEOF) {
							// 
							if (p.laKind == DTokens.CloseCurlyBrace) {
								p.Step ();
								/*if (metaDecls.Count > 0)
									metaDecls.RemoveAt (metaDecls.Count - 1);*/
								continue;
							}

							p.DeclDef (tempBlock);
						}
					}

					// Update the actual tempBlock as well as methods/other blocks' end location that just appeared while parsing the code incrementally,
					// so they are transparent to SearchBlockAt
					var block = tempBlock as IBlockNode;
					while(block != null && 
						(block.EndLocation.Line < 1 || block.EndLocation == p.la.Location)){
						block.EndLocation = new CodeLocation(p.la.Column+1,p.la.Line);
						if(block.Children.Count == 0)
							break;
						block = block.Children[block.Count-1] as IBlockNode;
					}

					if(isInsideNonCodeSegment = p.Lexer.endedWhileBeingInNonCodeSequence)
						return;
				}
			}catch(Exception ex) {
				Console.WriteLine (ex.Message);
			}

			// Remove old static stmts, declarations and meta blocks from bn
			/*bn.MetaBlocks;*/
			int startStatStmtIndex;
			for (startStatStmtIndex = bn.StaticStatements.Count - 1; startStatStmtIndex >= 0; startStatStmtIndex--) {
				var ss = bn.StaticStatements [startStatStmtIndex];
				if (ss.Location >= startLoc && ss.Location <= caretLocation)
					bn.StaticStatements.RemoveAt (startStatStmtIndex);
				else if(ss.EndLocation < startLoc)
					break;
			}

			INode ch_;
			int i = startDeclIndex + 1;
			while (i < bn.Count && (ch_ = bn.Children [i]).Location < caretLocation)
				bn.Children.Remove (ch_);

			// Insert new static stmts, declarations and meta blocks(?) into bn
			if (tempBlock.EndLocation > bn.EndLocation)
				bn.EndLocation = tempBlock.EndLocation;
			foreach (var n in tempBlock.Children) {
				if (n != null) {
					n.Parent = bn;
					bn.Children.Insert (n, ++startDeclIndex);
				}
			}

			bn.StaticStatements.InsertRange(startStatStmtIndex+1, tempBlock.StaticStatements);
		}
		#endregion
	}
}

