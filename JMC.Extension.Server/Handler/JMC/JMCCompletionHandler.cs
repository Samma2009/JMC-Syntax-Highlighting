﻿using JMC.Shared;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Collections.Immutable;

namespace JMC.Extension.Server.Handler.JMC
{
    internal class JMCCompletionHandler(ILogger<JMCCompletionHandler> logger) : CompletionHandlerBase
    {
        private readonly ILogger<JMCCompletionHandler> _logger = logger;
        public static readonly ImmutableArray<string> TriggerChars = [".", "#", " ", "/", "$"];

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) =>
            Task.FromResult(request);

        public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
        {
            var documentUri = request.TextDocument.Uri;

            if (documentUri.Path.EndsWith(".jmc"))
                return GetJMCAsync(request, cancellationToken);
            else if (documentUri.Path.EndsWith(".hjmc"))
                return GetHJMCAsync(request, cancellationToken);
            else
                throw new NotImplementedException();
        }

        private Task<CompletionList> GetJMCAsync(CompletionParams request, CancellationToken cancellationToken)
        {
            var list = new List<CompletionItem>();
            var file = ExtensionDatabase.Workspaces.GetJMCFile(request.TextDocument.Uri);

            if (request.Context == null || file == null)
                return Task.FromResult(CompletionList.From(list));

            var workspace = ExtensionDatabase.Workspaces.GetWorkspaceByUri(file.DocumentUri);
            if (workspace == null)
                return Task.FromResult(CompletionList.From(list));

            var triggerChar = request.Context.TriggerCharacter;
            var triggerType = request.Context.TriggerKind;

            //variables
            if (triggerChar != null &&
                triggerChar == "$" &&
                triggerType == CompletionTriggerKind.TriggerCharacter)
            {
                var arr = workspace.GetAllJMCVariableNames().AsSpan();
                for (var i = 0; i < arr.Length; i++)
                {
                    ref var v = ref arr[i];
                    list.Add(new()
                    {
                        Label = v[1..],
                        Kind = CompletionItemKind.Variable
                    });
                }
            }
            //normal case
            else if (triggerChar != null &&
                triggerChar == "$" &&
                triggerType == CompletionTriggerKind.TriggerCharacter)
                return GetJMCHirechyCompletionAsync(request, cancellationToken);
            else
            {
                //from .jmc
                var funcs = workspace.GetAllJMCFunctionNames().AsSpan();
                for (var i = 0; i < funcs.Length; i++)
                {
                    ref var v = ref funcs[i];
                    list.Add(new()
                    {
                        Label = v,
                        Kind = CompletionItemKind.Function
                    });
                }
                var cls = workspace.GetAllJMCClassNames().AsSpan();
                for (var i = 0; i < funcs.Length; i++)
                {
                    ref var v = ref cls[i];
                    list.Add(new()
                    {
                        Label = v,
                        Kind = CompletionItemKind.Class
                    });
                }
                //from built-in
                var builtIn = ExtensionData.JMCBuiltInFunctions.DistinctBy(v => v.Class).ToArray().AsSpan();
                //classes
                for (var i = 0; i < builtIn.Length; i++)
                {
                    ref var v = ref builtIn[i];
                    list.Add(new()
                    {
                        Label = v.Class,
                        Kind = CompletionItemKind.Class
                    });
                }
                //functions
                list.Add(new()
                {
                    Label = "print",
                    Kind = CompletionItemKind.Function
                });
                list.Add(new()
                {
                    Label = "printf",
                    Kind = CompletionItemKind.Function
                });
            }

            return Task.FromResult(CompletionList.From(list));
        }

        private Task<CompletionList> GetJMCHirechyCompletionAsync(CompletionParams request, CancellationToken cancellationToken)
        {
            var list = new List<CompletionItem>();

            return Task.FromResult(new CompletionList(list));
        }

        private Task<CompletionList> GetHJMCAsync(CompletionParams request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CompletionList());
        }

        protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities) => new()
        {
            ResolveProvider = true,
            TriggerCharacters = TriggerChars,
            DocumentSelector = ExtensionSelector.JMC
        };
    }
}
