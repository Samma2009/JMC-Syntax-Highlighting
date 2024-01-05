﻿using JMC.Parser.JMC.Error;
using JMC.Parser.JMC.Error.Base;
using JMC.Parser.JMC.Types;
using NJsonSchema;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Runtime.InteropServices;

namespace JMC.Parser.JMC
{
    /// <summary>
    /// Use <see cref="InitializeAsync(string)"/> for constructor
    /// </summary>
    internal partial class JMCSyntaxTree
    {
        public List<JMCSyntaxNode> Nodes { get; set; } = [];
        public JMCSyntaxNode[] FlattenedNodes { get; set; } = [];
        public string RawText { get; set; } = string.Empty;
        public string[] SplitText { get; private set; } = [];
        public string[] TrimmedText { get; private set; } = [];
        public List<JMCBaseError> Errors { get; set; } = [];
        public CancellationTokenSource CancellationSource { get; private set; } = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public async Task<JMCSyntaxTree> InitializeAsync(string text)
        {
            Errors.Clear();
            Nodes.Clear();
            RawText = text;
            var split = new JMCLexer(text).StartLexing();
            SplitText = split.Where(v => v != "").ToArray();
            TrimmedText = SplitText.Select(x => x.Trim()).ToArray();
            await InitAsync();
            FlattenedNodes = [.. GetFlattenNodes()];
            return this;
        }

        //TODO: Not Finished
        public void ModifyIncremental(TextDocumentContentChangeEvent eventArgs)
        {
            if (eventArgs.Range == null)
                return;
            var start = ToOffset(eventArgs.Range.Start);
            var end = ToOffset(eventArgs.Range.End);
            var modifier = eventArgs.Text.Length - (end - start);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Diagnostic[] GetDiagnostics()
        {
            var diagnostics = new List<Diagnostic>();

            var arr = CollectionsMarshal.AsSpan(Errors);
            for (var i = 0; i < arr.Length; i++)
            {
                ref var error = ref arr[i];
                diagnostics.Add(new()
                {
                    Range = error.Range,
                    Severity = error.DiagnosticSeverity,
                    Message = error.Message,
                });
            }

            return [.. diagnostics];
        }

        /// <summary>
        /// Reset a tree
        /// </summary>
        /// <param name="changedText"></param>
        public void ModifyFull(string changedText) => InitializeAsync(changedText).Wait();

        /// <inheritdoc cref="ModifyFull(string)"/>
        public async Task ModifyFullAsync(string changedText) => await InitializeAsync(changedText);

        /// <summary>
        /// return index of <see cref="FlattenedNodes"/>
        /// </summary>
        /// <param name="pos"></param>
        /// <returns>-1 if not found</returns>
        public int GetIndexByRange(Position pos)
        {
            var node = FlattenedNodes.First(v => v.Range != null && v.Range.Contains(pos));
            return Array.IndexOf(FlattenedNodes, node);
        }

        /// <summary>
        /// offset of text to <seealso cref="TrimmedText"/> Index
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        private int ToIndex(int offset)
        {
            var current = 0;

            var arr = SplitText.AsSpan();
            for (var i = 0; i < arr.Length; i++)
            {
                ref var text = ref arr[i];
                current += text.Length;
                if (current + text.Length > offset + 1) return i;
            }

            return -1;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task InitAsync() => await ParseNextAsync(SkipToValue(0), CancellationSource.Token);
        /// <summary>
        /// print a tree view
        /// </summary>
        public void PrintPretty() => Nodes.ForEach(v => v.PrintPretty("", true));
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<JMCSyntaxNode> GetFlattenNodes() => Nodes.SelectMany(v => v.ToFlattenNodes());
        /// <summary>
        /// parse next expression
        /// </summary>
        /// <param name="index"></param>
        /// <param name="token"></param>
        /// <returns>-1 is success,>= 0 is stopped index point</returns>
        /// <exception cref="OperationCanceledException"/>
        public async Task ParseNextAsync(int index, CancellationToken token)
        {
            if (index == -1)
                return;

            var result = Parse(index, isStart: true);
            index = NextIndex(result.EndIndex);
            if (result.Node != null) Nodes.Add(result.Node);

            if (index < TrimmedText.Length - 1)
                await ParseNextAsync(index, token);
        }
        /// <summary>
        /// Parse a text
        /// </summary>
        /// <param name="index">index of current <seealso cref="TrimmedText"/></param>
        /// <param name="noNext">Does it require to parse the children</param>
        /// <param name="isStart">Is it not a call from a parent node</param>
        /// <returns></returns>
        public JMCParseResult Parse(int index, bool noNext = false, bool isStart = false)
        {
            var value = TrimmedText[index];
            var nextIndex = NextIndex(index, out var errorCode);

            var node = new JMCSyntaxNode();

            var offset = ToOffset(index);
            var range = new Range(GetIndexStartPos(index), GetIndexEndPos(index));
            node.Range = range;
            node.Value = value;
            node.Offset = offset;

            switch (value)
            {
                #region Keywords
                case "class":
                    return noNext ? new(node, index) : ParseClass(index);
                case "function":
                    return noNext ? new(node, index) : ParseFunction(index);
                case "import":
                    return noNext ? new(node, index) : ParseImport(index);
                case "new":
                    return noNext ? new(node, index) : ParseNew(index);
                case "true":
                    node.NodeType = JMCSyntaxNodeType.True;
                    return new(node, nextIndex);
                case "false":
                    node.NodeType = JMCSyntaxNodeType.False;
                    return new(node, nextIndex);
                case "while":
                    node.NodeType = JMCSyntaxNodeType.While;
                    return new(node, nextIndex);
                case "do":
                    node.NodeType = JMCSyntaxNodeType.Do;
                    return new(node, nextIndex);
                case "for":
                    node.NodeType = JMCSyntaxNodeType.For;
                    return new(node, nextIndex);
                case "break":
                    node.NodeType = JMCSyntaxNodeType.Break;
                    return new(node, nextIndex);
                #endregion

                #region Ops
                case "++":
                    node.NodeType = JMCSyntaxNodeType.OpIncrement;
                    return new(node, nextIndex);
                case "--":
                    node.NodeType = JMCSyntaxNodeType.OpDecrement;
                    return new(node, nextIndex);
                case "+":
                    node.NodeType = JMCSyntaxNodeType.OpPlus;
                    return new(node, nextIndex);
                case "-":
                    node.NodeType = JMCSyntaxNodeType.OpSubtract;
                    return new(node, nextIndex);
                case "*":
                    node.NodeType = JMCSyntaxNodeType.OpMultiply;
                    return new(node, nextIndex);
                case "/":
                    node.NodeType = JMCSyntaxNodeType.OpDivide;
                    return new(node, nextIndex);
                case "%":
                    node.NodeType = JMCSyntaxNodeType.OpRemainder;
                    return new(node, nextIndex);
                case "+=":
                    node.NodeType = JMCSyntaxNodeType.OpPlusEqual;
                    return new(node, nextIndex);
                case "-=":
                    node.NodeType = JMCSyntaxNodeType.OpSubtractEqual;
                    return new(node, nextIndex);
                case "*=":
                    node.NodeType = JMCSyntaxNodeType.OpMultiplyEqual;
                    return new(node, nextIndex);
                case "/=":
                    node.NodeType = JMCSyntaxNodeType.OpDivideEqual;
                    return new(node, nextIndex);
                case "%=":
                    node.NodeType = JMCSyntaxNodeType.OpRemainderEqual;
                    return new(node, nextIndex);
                case "??=":
                    node.NodeType = JMCSyntaxNodeType.OpNullcoale;
                    return new(node, nextIndex);
                case "?=":
                    node.NodeType = JMCSyntaxNodeType.OpSuccess;
                    return new(node, nextIndex);
                case "><":
                    node.NodeType = JMCSyntaxNodeType.OpSwap;
                    return new(node, nextIndex);
                #endregion

                #region Comps
                case "||":
                    node.NodeType = JMCSyntaxNodeType.CompOr;
                    return new(node, nextIndex);
                case "&&":
                    node.NodeType = JMCSyntaxNodeType.CompAnd;
                    return new(node, nextIndex);
                case "!":
                    node.NodeType = JMCSyntaxNodeType.CompNot;
                    return new(node, nextIndex);
                #endregion

                #region Chars
                case "{":
                    node.NodeType = JMCSyntaxNodeType.LCP;
                    return new(node, nextIndex);
                case "}":
                    node.NodeType = JMCSyntaxNodeType.RCP;
                    return new(node, nextIndex);
                case "(":
                    node.NodeType = JMCSyntaxNodeType.LParen;
                    return new(node, nextIndex);
                case ")":
                    node.NodeType = JMCSyntaxNodeType.RParen;
                    return new(node, nextIndex);
                case ";":
                    node.NodeType = JMCSyntaxNodeType.Semi;
                    return new(node, nextIndex);
                case ":":
                    node.NodeType = JMCSyntaxNodeType.Colon;
                    return new(node, nextIndex);
                case "=>":
                    node.NodeType = JMCSyntaxNodeType.Arrow;
                    return new(node, nextIndex);
                #endregion

                #region Misc
                case ">":
                    node.NodeType = JMCSyntaxNodeType.GreaterThan;
                    return new(node, nextIndex);
                case "<":
                    node.NodeType = JMCSyntaxNodeType.LessThan;
                    return new(node, nextIndex);
                case ">=":
                    node.NodeType = JMCSyntaxNodeType.GreaterThanEqual;
                    return new(node, nextIndex);
                case "<=":
                    node.NodeType = JMCSyntaxNodeType.LessThanEqual;
                    return new(node, nextIndex);
                case "=":
                    node.NodeType = JMCSyntaxNodeType.EqualTo;
                    return new(node, nextIndex);
                case "==":
                    node.NodeType = JMCSyntaxNodeType.Equal;
                    return new(node, nextIndex);
                case ".":
                    node.NodeType = JMCSyntaxNodeType.Dot;
                    return new(node, nextIndex);
                #endregion
                default:
                    break;
            }

            var result = ParseSpecialToken(value);

            if (isStart)
            {
                var r = ParseExpression(index);
                if (r.Node != null)
                    return r;
            }

            if (result != default)
            {
                node.NodeType = result;
                return new(node, nextIndex);
            }

            return new(node, nextIndex);
        }
        private static JMCSyntaxNodeType ParseSpecialToken(string text)
        {
            if ((text.StartsWith("//") || text.StartsWith('#')) && !text.Contains(Environment.NewLine))
                return JMCSyntaxNodeType.Comment;
            if (int.TryParse(text, out _))
                return JMCSyntaxNodeType.Int;
            if (float.TryParse(text, out _))
                return JMCSyntaxNodeType.Float;
            if (text == "~")
                return JMCSyntaxNodeType.Tilde;
            if (text == "^")
                return JMCSyntaxNodeType.Caret;

            //selector matching
            var selectorChars = "parse";
            if (text.StartsWith('@') && selectorChars.Contains(text[1]))
                return JMCSyntaxNodeType.Selector;

            //variable matching
            if (text.StartsWith('$'))
            {
                var vString = text[1..];
                var isValid = vString.All(LiteralChars[..(LiteralChars.Length - 1)].Contains);
                if (isValid)
                    return JMCSyntaxNodeType.Variable;
            }
            
            //string matching
            var splitString = text.Split('"');
            if (splitString.Length == 3 && !splitString.Contains(Environment.NewLine))
                return JMCSyntaxNodeType.String;
            splitString = text.Split('\'');
            if (splitString.Length == 3 && !splitString.Contains(Environment.NewLine))
                return JMCSyntaxNodeType.String;
            if (text.StartsWith('`') && text.EndsWith('`'))
                return JMCSyntaxNodeType.MultilineString;

            if (text.All(LiteralChars.Contains))
                return JMCSyntaxNodeType.Literal;

            //literal matching

            return default;
        }
        /// <summary>
        /// parse a import expression
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private JMCParseResult ParseImport(int index)
        {
            var node = new JMCSyntaxNode();

            var query = this.AsParseQuery(index);
            var match = query.ExpectList(out _, true, JMCSyntaxNodeType.String, JMCSyntaxNodeType.Semi);

            var start = GetIndexStartPos(index);

            //get path
            var str = Parse(NextIndex(index));

            //move next
            index = SkipToValue(query.Index);

            var end = GetIndexStartPos(index);

            //set node
            node.NodeType = JMCSyntaxNodeType.Import;
            node.Next = match && str.Node != null ? [str.Node] : null;
            node.Range = new Range(start, end);

            return new(node, index);
        }
        /// <summary>
        /// parse a class expression
        /// </summary>
        /// <remarks>
        /// literal '{' function* '}'
        /// </remarks>
        /// <param name="index"></param>
        /// <returns></returns>
        private JMCParseResult ParseClass(int index)
        {
            var node = new JMCSyntaxNode();
            var next = new List<JMCSyntaxNode>();

            //check for `literal '{'`
            var query = this.AsParseQuery(index);
            var match = query.ExpectList(out _, true, JMCSyntaxNodeType.Literal, JMCSyntaxNodeType.LCP);

            //get Key
            var literal = TrimmedText[NextIndex(index)];

            //get start pos
            index = SkipToValue(query.Index);
            var start = GetIndexStartPos(index);

            //parse functions
            query.Reset(this, index);
            while (index < TrimmedText.Length && match)
            {
                query.Next();
                var funcTest = query.Expect("function", out _, false);
                var newTest = query.Expect("new", out _, false);
                if (funcTest)
                {
                    var result = ParseFunction(query.Index);
                    if (result.Node != null) next.Add(result.Node);
                    index = result.EndIndex;
                    query.Reset(this, index);
                }
                else if (newTest)
                {
                    var result = ParseNew(query.Index);
                    if (result.Node != null) next.Add(result.Node);
                    index = result.EndIndex;
                    query.Reset(this, index);
                }
                else
                {
                    index = NextIndex(index);
                    if (TrimmedText[index] == "}") break;
                    query.Reset(this, index);
                }
            }
            var end = GetIndexStartPos(index);

            //set next
            node.NodeType = JMCSyntaxNodeType.Class;
            node.Next = next.Count != 0 ? next : null;
            node.Range = new Range(start, end);
            node.Value = literal;

            return new(node, index);
        }
        /// <summary>
        /// parse a function expression
        /// </summary>
        /// <remarks>
        /// literal '(' ')' block
        /// </remarks>
        /// <param name="index">current index</param>
        /// <returns></returns>
        private JMCParseResult ParseFunction(int index)
        {
            var node = new JMCSyntaxNode();
            var next = new List<JMCSyntaxNode>();

            var query = this.AsParseQuery(index);
            var match = query.
                ExpectList(out _, true, JMCSyntaxNodeType.Literal, JMCSyntaxNodeType.LParen, JMCSyntaxNodeType.RParen, JMCSyntaxNodeType.LCP);

            index = SkipToValue(index);

            var literal = TrimmedText[NextIndex(index)];
            index = query.Index;

            var start = GetIndexStartPos(index);

            //parse expressions
            if (match)
            {
                var exps = ParseBlock(index);
                var expsNode = exps.Node;
                if (expsNode != null)
                    next.Add(expsNode);
                index = exps.EndIndex;
            }

            var end = GetIndexStartPos(index);
            //set next
            node.NodeType = JMCSyntaxNodeType.Function;
            node.Next = next.Count != 0 ? next : null;
            node.Range = new Range(start, end);
            node.Value = literal;

            return new(node, index);
        }
        private JMCParseResult ParseNew(int index)
        {
            var node = new JMCSyntaxNode();
            var next = new List<JMCSyntaxNode>();
            var query = this.AsParseQuery(index);

            var start = GetIndexStartPos(index);
            var match = query.ExpectList(out var list, true, JMCSyntaxNodeType.Literal, JMCSyntaxNodeType.LParen, JMCSyntaxNodeType.Literal, JMCSyntaxNodeType.RParen, JMCSyntaxNodeType.LCP);
            index = NextIndex(query.Index);
            if (match && list != null)
            {
                //check file type
                var lvalue = list.First().Value ?? string.Empty;
                var isValidType = JSONFileTypes.Contains(lvalue);
                if (!isValidType)
                    Errors.Add(new JMCSyntaxError(GetRangeByIndex(index), "Unexpected File Type"));

                //read json
                var arr = TrimmedText.AsSpan(index);
                var tempString = "{";
                var counter = 1;
                for (var i = 0; i < arr.Length; i++)
                {
                    ref var s = ref arr[i];
                    tempString += s;
                    counter += s switch
                    {
                        "{" => 1,
                        "}" => -1,
                        _ => 0
                    };
                    if (counter == 0) break;
                }

                //check json
                var jsonSchema = @"https://json.schemastore.org/minecraft-advancement.json";
                var schema = JsonSchema.FromUrlAsync(jsonSchema).Result ?? null;
                if (tempString.Length < 3)
                {
                    Errors.Add(new JMCSyntaxError(GetRangeByIndex(index), "JSON must not be empty"));
                }
                else if (schema != null)
                {
                    //TODO
                }
                else
                {
                    //TODO add errror
                }
            }
            var end = GetIndexStartPos(index);

            node.NodeType = JMCSyntaxNodeType.New;
            node.Next = next.Count != 0 ? next : null;
            node.Range = new Range(start, end);

            return new(node, index);
        }
        /// <summary>
        /// Get the <seealso cref="Range"/> of a <seealso cref="string"/> in <seealso cref="TrimmedText"/> by index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal Range GetRangeByIndex(int index, bool isError = false)
        {
            var start = GetIndexStartPos(index);
            var end = GetIndexEndPos(index, isError);
            return new Range(start, end);
        }
        /// <summary>
        /// Get the <seealso cref="Position"/> of a <seealso cref="string"/> in <seealso cref="TrimmedText"/>'s end pos
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal Position GetIndexEndPos(int index, bool isError = false)
        {
            var errorOffset = isError ? 0 : 1;
            var offset = ToOffset(index);
            var posOffset = offset + TrimmedText[index].Length - errorOffset;
            return posOffset.ToPosition(RawText);
        }
        /// <summary>
        /// Skip to a non-space index
        /// </summary>
        /// <param name="index"></param>
        /// <param name="errorCode">0 if success, 1 if out of range</param>
        internal int SkipToValue(int index, out int errorCode)
        {
            errorCode = 0;
            if (index >= TrimmedText.Length - 1)
            {
                errorCode = 1;
                return TrimmedText.Length - 1;
            }

            var arr = TrimmedText.AsSpan();

            ref var value = ref arr[index];
            var nextIndex = index;

            while (string.IsNullOrEmpty(value) || value.StartsWith("//"))
            {
                nextIndex++;
                try
                {
                    value = ref arr[nextIndex]!;
                }
                catch (IndexOutOfRangeException)
                {
                    errorCode = 1;
                    return TrimmedText.Length - 1;
                }
            }

            return nextIndex;
        }
        /// <inheritdoc cref="SkipToValue(int, out int)"/>
        internal int SkipToValue(int index) => SkipToValue(index, out _);
        /// <summary>
        /// Get next non-space character index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal int NextIndex(int index) => SkipToValue(index + 1);
        /// <summary>
        /// Get next index with error code
        /// </summary>
        /// <param name="index"></param>
        /// <param name="errorCode"></param>
        /// <returns></returns>
        internal int NextIndex(int index, out int errorCode) => SkipToValue(index + 1, out errorCode);
        /// <summary>
        /// index of <seealso cref="TrimmedText"/> to offset
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal int ToOffset(int index)
        {
            var offset = 0;

            var arr = SplitText.AsSpan(0, index);
            for (var i = 0; i < arr.Length; i++)
            {
                ref var v = ref arr[i];
                offset += v.Length;
            }

            return offset;
        }
        /// <summary>
        /// <seealso cref="Position"/> to <seealso cref="int"/>
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        internal int ToOffset(Position position)
        {
            if (position.Line == 0) return position.Character;

            var offset = 0;
            var split = RawText.Split(Environment.NewLine);
            var arr = split.AsSpan();

            for (var i = 0; i < position.Line; i++)
            {
                ref var line = ref arr[i];
                offset += line.Length + Environment.NewLine.Length;
            }
            offset += position.Character;

            return offset;
        }
        /// <summary>
        /// Get the start <seealso cref="Position"/> by index of the <seealso cref="TrimmedText"/>
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal Position GetIndexStartPos(int index) => ToOffset(index).ToPosition(RawText);
    }
}
