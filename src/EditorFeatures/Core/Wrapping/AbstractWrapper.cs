﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.Editor.Wrapping
{
    /// <summary>
    /// Common implementation of all IWrappers.  This type takes care of a lot of common logic for
    /// all of them, including:
    /// 
    /// 1. Keeping track of code action invocations, allowing code actions to then be prioritized on
    ///    subsequent invocations.
    ///    
    /// 2. Checking nodes and tokens to make sure they are safe to be wrapped.
    /// 
    /// Individual subclasses may be targeted at specific syntactic forms.  For example, wrapping
    /// lists, or wrapping logical expressions.  Most subclasses should 
    /// </summary>
    internal abstract partial class AbstractWrapper : IWrapper
    {
        public abstract Task<ICodeActionComputer> TryCreateComputerAsync(Document document, int position, SyntaxNode node, CancellationToken cancellationToken);

        // Keeps track of the invoked code actions.  That way we can prioritize those code actions 
        // in the future since they're more likely the ones the user wants.  This is important as 
        // we have 9 different code actions offered (3 major groups, with 3 actions per group).  
        // It's likely the user will just pick from a few of these. So we'd like the ones they
        // choose to be prioritized accordingly.
        private static ImmutableArray<string> s_mruTitles = ImmutableArray<string>.Empty;

        private static ImmutableArray<CodeAction> SortActionsByMostRecentlyUsed(ImmutableArray<CodeAction> codeActions)
        {
            // make a local so this array can't change out from under us.
            var mruTitles = s_mruTitles;
            return codeActions.Sort((ca1, ca2) =>
            {
                var titleIndex1 = mruTitles.IndexOf(GetSortTitle(ca1));
                var titleIndex2 = mruTitles.IndexOf(GetSortTitle(ca2));

                if (titleIndex1 >= 0 && titleIndex2 >= 0)
                {
                    // we've invoked both of these before.  Order by how recently it was invoked.
                    return titleIndex1 - titleIndex2;
                }

                // one of these has never been invoked.  It's always after an item that has been
                // invoked.
                if (titleIndex1 >= 0)
                {
                    return -1;
                }

                if (titleIndex2 >= 0)
                {
                    return 1;
                }

                // Neither of these has been invoked.   Keep it in the same order we found it in the
                // array.  Note: we cannot return 0 here as ImmutableArray/Array are not guaranteed
                // to sort stably.
                return codeActions.IndexOf(ca1) - codeActions.IndexOf(ca2);
            });
        }

        private static string GetSortTitle(CodeAction codeAction)
            => (codeAction as WrapItemsAction)?.SortTitle ?? codeAction.Title;

        protected static async Task<bool> ContainsUnformattableContentAsync(
            Document document, IEnumerable<SyntaxNodeOrToken> nodesAndTokens, CancellationToken cancellationToken)
        {
            // For now, don't offer if any item spans multiple lines.  We'll very likely screw up
            // formatting badly.  If this is really important to support, we can put in the effort
            // to properly move multi-line items around (which would involve properly fixing up the
            // indentation of lines within them.
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in nodesAndTokens)
            {
                if (item == null ||
                    item.Span.IsEmpty)
                {
                    return true;
                }

                var firstToken = item.IsToken ? item.AsToken() : item.AsNode().GetFirstToken();
                var lastToken = item.IsToken ? item.AsToken() : item.AsNode().GetLastToken();

                if (!sourceText.AreOnSameLine(firstToken, lastToken))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
